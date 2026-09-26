#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using Environment = Godot.Environment;
using Range = Godot.Range;
using LastCamp;
using LastCamp.Construction;

namespace LastCamp;

/// Owns the Environment, sky, sun/moon lights, camera attributes, the final
/// post pass and the time-of-day simulation. Everything lighting-related that
/// changes with the hour (or with the camera going under water) is applied
/// here so the rest of the scene can stay declarative.
public partial class WorldController : Node3D
{
    [Signal]
    public delegate void hour_changedEventHandler(double hour);

    /// Hero time: the sun sits a few degrees above the treeline across the pond.
    public const double DEFAULT_HOUR = 19.05;
    public const double CYCLE_HOURS_PER_SECOND = 24.0 / 180.0;
    public const double SCRUB_HOURS_PER_SECOND = 1.6;
    public static readonly Vector3 FIRE_ORIGIN = new Vector3(0.0f, 0.55f, 0.0f);
    /// Prevailing wind (also the `wind_direction` shader global).
    public static readonly Vector2 WIND_DIRECTION = new Vector2(1.0f, 0.3f);

    /// Warm-up stages: heavy renderer features come online one at a time so the
    /// first frames after loading never pile SDFGI, SSIL, volumetrics and the
    /// planar reflection onto one GPU submission.
    public enum Warm
    {
        NONE,
        VOLUMETRICS,
        SCREEN_SPACE,
        GLOBAL_ILLUMINATION,
        ALL,
    }

    public static readonly Color UNDERWATER_FOG = new Color(0.05f, 0.17f, 0.15f);
    public static readonly Color UNDERWATER_SKY = new Color(0.10f, 0.30f, 0.28f);
    /// Maximum displacement of the three shared pond waves at the wind limit.
    /// Clear-air haze. 0.0007 mixed a quarter of the bright horizon into anything
    /// 400 m away and turned the wooded ridges pale yellow-green; real morning air
    /// over a forest is about half that.
    public const double AIR_FOG_DENSITY = 0.0004;
    public const double WATER_FOG_DENSITY = 0.16;
    public const double AIR_VOLUME_DENSITY = 0.00045;
    public const double WATER_VOLUME_DENSITY = 0.09;
    public static readonly Color WATER_VOLUME_ALBEDO = new Color(0.40f, 0.72f, 0.57f);
    public const double WATER_WAVE_ENVELOPE = 0.012 * 2.1 * 1.3 + PondSurface.RESIDUAL_BOUND;

    private double _hour_value = DEFAULT_HOUR;
    public double hour
    {
        get => _hour_value;
        set
        {
            _hour_value = fposmod(value, 24.0);
            _apply_hour();
        }
    }
    public double haze = 0.85;
    public double wind_scale = 1.0;
    public bool cycle_running = false;
    public WorldController.Warm warm_stage = WorldController.Warm.NONE;
    private double _exposure_scale_value = 1.0;
    public double exposure_scale
    {
        get => _exposure_scale_value;
        set
        {
            _exposure_scale_value = value;
            if (environment != null)
            {
                environment.TonemapExposure = (float)(_exposure_for(daylight(), Atmosphere.sun_direction(hour)) * _exposure_scale_value);
            }
        }
    }

    public bool underwater = false;
    public double lens_entry_age = 100.0;
    public double lens_exit_age = 100.0;
    public bool _lens_in_water = false;
    public bool _lens_initialized = false;
    public double underwater_blend = 0.0;
    public long _fog_history_reset = 0;
    public Godot.Collections.Dictionary _water_focus_saved = new Godot.Collections.Dictionary();

    public Environment environment;
    public GpuParticles3D entry_bubbles;
    public CampWeather weather;
    public Sky sky;
    public ShaderMaterial sky_material;
    public DirectionalLight3D sun;
    public DirectionalLight3D moon;
    public CameraAttributesPractical attributes;
    public WorldEnvironment world_env;
    public PostStack post;
    public MeshInstance3D waterline;
    public ShaderMaterial waterline_material;
    public FogVolume water_volume;
    public ShaderMaterial water_volume_material;

    public double _wind_time = 0.0;
    public double _cloud_time = 0.0;
    public double _cloud_hour = DEFAULT_HOUR;
    public double _gust = 1.0;
    public Color _fog_color = new Color(0.5f, 0.6f, 0.8f);
    public QualityPreset _preset;
    public Godot.Collections.Array<FogVolume> _mist = new Godot.Collections.Array<FogVolume>();
    public double _applied_cloud = -1.0;
    public double _applied_rain = -1.0;

    public override void _Ready()
    {
        Game.Instance.world = this;
        _build_environment();
        _build_lights();
        _build_camera_attributes();
        _build_post();
        _build_water_volume();
        _build_entry_bubbles();
        _build_mist();
        weather = new CampWeather();
        AddChild(weather);
        RenderingServer.GlobalShaderParameterSet("wind_direction", WIND_DIRECTION);
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, WorldController.MethodName._on_quality_changed));
        _on_quality_changed(Quality.Instance.current);
        _apply_hour();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_time_cycle"))
        {
            cycle_running = !cycle_running;
        }
    }

    public override void _Process(double delta)
    {
        double scrub = (double)Input.GetActionStrength("time_forward") - Input.GetActionStrength("time_back");
        if (Game.Instance.mode == Game.Mode.PLAY || Game.Instance.mode == Game.Mode.PHOTO)
        {
            if (scrub != 0.0)
            {
                hour = hour + scrub * SCRUB_HOURS_PER_SECOND * delta;
            }
            else if (cycle_running)
            {
                hour = hour + CYCLE_HOURS_PER_SECOND * delta;
            }
        }
        _wind_time += delta;
        _gust = (0.7 + 0.3 * sin(_wind_time * 0.23) * sin(_wind_time * 0.071 + 1.3)) * wind_scale;
        RenderingServer.GlobalShaderParameterSet("wind_strength", _gust);
        double hour_step = absf(wrapf(hour - _cloud_hour, -12.0, 12.0));
        // Weather advances with a continuous time lapse; a cut to another hour
        // does not teleport the cloud texture. Vegetation keeps its real clock.
        _cloud_time += delta + (hour_step < 0.08 ? hour_step * 360.0 : 0.0);
        _cloud_hour = hour;
        sky_material.SetShaderParameter("cloud_time", _cloud_time);
        if (Game.Instance.player != null)
        {
            RenderingServer.GlobalShaderParameterSet("player_position", Game.Instance.player.GlobalPosition);
        }
        _update_underwater(delta);
        _update_post();
        // Sun energy, sky overcast and fog density all follow the weather but were
        // only recomputed when the hour changed, so a chapter cut that clears the
        // sky without moving the hour kept the storm's lighting for the whole next
        // shot. Re-apply the hour whenever the weather state moves.
        if (weather != null && (!is_equal_approx(weather.cloud, _applied_cloud) || !is_equal_approx(weather.rain, _applied_rain)))
        {
            _apply_hour();
        }
    }

    public async Task warm_up()
    {
        /// Brings the expensive features online over a few frames. Awaited by the
        /// scene entry point while the loading screen is still up.
        foreach (Variant stage_item in new Godot.Collections.Array { (long)WorldController.Warm.VOLUMETRICS, (long)WorldController.Warm.SCREEN_SPACE, (long)WorldController.Warm.GLOBAL_ILLUMINATION, (long)WorldController.Warm.ALL })
        {
            WorldController.Warm stage = (WorldController.Warm)stage_item.AsInt64();
            warm_stage = stage;
            _apply_features();
            for (long i2 = 0; i2 < 3; i2++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    public void _build_environment()
    {
        // ------------------------------------------------------------------ building
        world_env = new WorldEnvironment();
        world_env.Name = "Environment";
        AddChild(world_env);

        sky_material = new ShaderMaterial();
        sky_material.Shader = Content.Load<Shader>("res://shaders/sky.gdshader");
        sky = new Sky();
        sky.SkyMaterial = sky_material;
        sky.ProcessMode = Sky.ProcessModeEnum.Incremental;
        sky.RadianceSize = Sky.RadianceSizeEnum.Size256;

        Environment env = new Environment();
        env.BackgroundMode = Environment.BGMode.Sky;
        env.Sky = sky;
        env.AmbientLightSource = Environment.AmbientSource.Sky;
        env.AmbientLightSkyContribution = 1.0f;
        env.AmbientLightEnergy = 1.0f;
        env.ReflectedLightSource = Environment.ReflectionSource.Sky;

        env.TonemapMode = Environment.ToneMapper.Agx;
        env.TonemapExposure = 1.0f;
        env.TonemapWhite = 6.0f;

        env.GlowEnabled = true;
        env.GlowNormalized = false;
        env.GlowIntensity = 0.32f;
        env.GlowStrength = 0.85f;
        env.GlowBloom = 0.006f;
        env.GlowBlendMode = Environment.GlowBlendModeEnum.Softlight;
        env.GlowHdrThreshold = 1.45f;
        env.GlowHdrScale = 1.6f;
        env.GlowHdrLuminanceCap = 8.0f;
        for (long level = 0; level < 7; level++)
        {
            env.SetGlowLevel((int)level, 0.0f);
        }
        env.SetGlowLevel(2, 0.35f);
        env.SetGlowLevel(3, 0.75f);
        env.SetGlowLevel(4, 0.9f);
        env.SetGlowLevel(5, 0.6f);
        env.SetGlowLevel(6, 0.35f);

        env.SsaoEnabled = false;
        env.SsaoRadius = 1.2f;
        env.SsaoIntensity = 1.35f;
        env.SsaoPower = 1.25f;
        env.SsaoDetail = 0.6f;
        env.SsaoHorizon = 0.06f;
        env.SsaoSharpness = 0.98f;
        env.SsaoLightAffect = 0.05f;
        env.SsaoAOChannelAffect = 0.0f;

        env.SsilEnabled = false;
        env.SsilRadius = 4.0f;
        env.SsilIntensity = 1.1f;
        env.SsilSharpness = 0.98f;
        env.SsilNormalRejection = 1.0f;

        env.SdfgiEnabled = false;
        env.SdfgiCascades = 6;
        env.SdfgiMinCellSize = 0.2f;
        env.SdfgiUseOcclusion = true;
        env.SdfgiReadSkyLight = true;
        env.SdfgiBounceFeedback = 0.5f;
        env.SdfgiEnergy = 1.0f;
        env.SdfgiNormalBias = 1.1f;
        env.SdfgiProbeBias = 1.1f;
        env.SdfgiYScale = Environment.SdfgiyScale.Scale75Percent;

        env.SsrEnabled = false;
        env.SsrMaxSteps = 64;
        env.SsrFadeIn = 0.15f;
        env.SsrFadeOut = 2.0f;
        env.SsrDepthTolerance = 0.2f;

        env.FogEnabled = true;
        env.FogMode = Environment.FogModeEnum.Exponential;
        env.FogDensity = 0.0007f;
        env.FogAerialPerspective = 0.6f;
        env.FogSkyAffect = 0.12f;
        env.FogSunScatter = 0.07f;
        env.FogLightEnergy = 1.0f;
        env.FogHeight = (float)(TerrainField.WATER_LEVEL + 1.0);
        env.FogHeightDensity = 0.02f;

        env.VolumetricFogEnabled = false;
        env.VolumetricFogDensity = 0.00065f;
        env.VolumetricFogAlbedo = new Color(0.92f, 0.92f, 0.92f);
        env.VolumetricFogEmission = new Color(0, 0, 0);
        env.VolumetricFogGIInject = 0.9f;
        env.VolumetricFogAnisotropy = 0.72f;
        env.VolumetricFogLength = 128.0f;
        env.VolumetricFogDetailSpread = 2.2f;
        env.VolumetricFogAmbientInject = 0.18f;
        env.VolumetricFogSkyAffect = 0.45f;
        env.VolumetricFogTemporalReprojectionEnabled = true;
        env.VolumetricFogTemporalReprojectionAmount = 0.92f;

        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.04f;
        env.AdjustmentSaturation = 1.06f;

        environment = env;
        world_env.Environment = env;
    }

    public void _build_lights()
    {
        sun = new DirectionalLight3D();
        sun.Name = "Sun";
        sun.LightAngularDistance = 0.53f;
        sun.ShadowEnabled = true;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowSplit1 = 0.05f;
        sun.DirectionalShadowSplit2 = 0.15f;
        sun.DirectionalShadowSplit3 = 0.4f;
        sun.DirectionalShadowBlendSplits = true;
        sun.DirectionalShadowFadeStart = 0.85f;
        sun.DirectionalShadowMaxDistance = 160.0f;
        sun.ShadowBias = 0.025f;
        sun.ShadowNormalBias = 1.6f;
        sun.ShadowBlur = 1.0f;
        sun.ShadowOpacity = 1.0f;
        sun.LightVolumetricFogEnergy = 1.0f;
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        AddChild(sun);

        moon = new DirectionalLight3D();
        moon.Name = "Moon";
        moon.LightAngularDistance = 0.5f;
        moon.ShadowEnabled = true;
        moon.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
        moon.DirectionalShadowMaxDistance = 90.0f;
        moon.ShadowBlur = 2.0f;
        moon.LightVolumetricFogEnergy = 0.6f;
        moon.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        moon.LightColor = Atmosphere.MOON_COLOR;
        AddChild(moon);
    }

    public void _build_camera_attributes()
    {
        attributes = new CameraAttributesPractical();
        attributes.AutoExposureEnabled = false;
        attributes.ExposureMultiplier = 1.0f;
        attributes.ExposureSensitivity = 100.0f;
        attributes.DofBlurFarEnabled = false;
        attributes.DofBlurNearEnabled = false;
        attributes.DofBlurAmount = 0.06f;
    }

    public void _build_post()
    {
        post = new PostStack();
        AddChild(post);
        // Murk for half-submerged frames: a full-screen quad drawn after the water.
        waterline = new MeshInstance3D();
        waterline.Name = "Waterline";
        // This lens effect belongs only to the main view. Drawing it from the
        // submerged mirror camera bakes a dark fog band into the pond reflection.
        waterline.Layers = unchecked((uint)(Pond.WATER_LAYER));
        QuadMesh quad = new QuadMesh();
        quad.Size = new Vector2(2.0f, 2.0f);
        waterline.Mesh = quad;
        waterline_material = new ShaderMaterial();
        waterline_material.Shader = Content.Load<Shader>("res://shaders/post/waterline.gdshader");
        waterline_material.RenderPriority = 100;
        waterline_material.SetShaderParameter("fog_color", UNDERWATER_FOG);
        waterline_material.SetShaderParameter("water_level", TerrainField.WATER_LEVEL);
        waterline.MaterialOverride = waterline_material;
        waterline.CustomAabb = new Aabb(new Vector3(-4000.0f, -4000.0f, -4000.0f), new Vector3(8000.0f, 8000.0f, 8000.0f));
        waterline.ExtraCullMargin = 16384.0f;
        waterline.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        waterline.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        waterline.Visible = false;
        AddChild(waterline);
    }

    public void _build_water_volume()
    {
        // Only the crossing needs a bounded volume: global water fog owns fully
        // submerged views. Auxiliary reflection views exclude the water layer.
        water_volume = new FogVolume();
        water_volume.Name = "WaterCrossingVolume";
        water_volume.Layers = unchecked((uint)(Pond.WATER_LAYER));
        water_volume.Shape = RenderingServer.FogVolumeShape.Box;
        water_volume.Size = new Vector3(60.0f, 6.0f, 60.0f);
        water_volume.Position = new Vector3(TerrainField.POND_CENTRE.X, (float)(TerrainField.WATER_LEVEL - 3.005), TerrainField.POND_CENTRE.Y);
        water_volume_material = new ShaderMaterial();
        water_volume_material.Shader = Content.Load<Shader>("res://shaders/pond_fog.gdshader");
        water_volume_material.SetShaderParameter("water_albedo", WATER_VOLUME_ALBEDO);
        water_volume.Material = water_volume_material;
        water_volume.Visible = false;
        AddChild(water_volume);
    }

    public void _apply_hour()
    {
        // ----------------------------------------------------------------- per hour
        if (weather != null)
        {
            _applied_cloud = weather.cloud;
            _applied_rain = weather.rain;
        }
        if (environment == null)
        {
            return;
        }
        Vector3 sun_dir = Atmosphere.sun_direction(hour);
        Vector3 moon_dir = Atmosphere.moon_direction(hour);
        Godot.Collections.Dictionary sun_light = Atmosphere.sun_light(sun_dir, haze);
        Godot.Collections.Dictionary moon_light = Atmosphere.moon_light(sun_dir, moon_dir);
        // Lights point along -Z, so aim them away from the celestial body.

        sun.GlobalTransform = new Transform3D(Basis.LookingAt(-sun_dir, _stable_up(sun_dir)), Vector3.Zero);
        sun.LightColor = sun_light["color"].AsColor();
        double overcast = weather != null ? weather.cloud : 0.0;
        // Preserve the readable blue-hour meadow; deep night keeps enough moon that
        // the treeline, the shore and the water still have form under the stars
        // instead of falling to black around the lanterns.
        double deep_night = smoothstep(-0.12, -0.35, sun_dir.Y);
        sun.LightEnergy = G.op("*", sun_light["energy"], lerpf(1.0, 0.20, overcast)).AsSingle();
        sun.Visible = G.op(">", sun_light["energy"], 0.001).AsBool();
        moon.GlobalTransform = new Transform3D(Basis.LookingAt(-moon_dir, _stable_up(moon_dir)), Vector3.Zero);
        // Storm cloud still passes a little moon: the rain shots kept only the dock lantern at 0.18.
        moon.LightEnergy = G.op("*", G.op("*", moon_light["energy"], lerpf(2.0, 1.1, deep_night)), lerpf(1.0, 0.32, overcast)).AsSingle();
        moon.Visible = G.op(">", moon_light["energy"], 0.001).AsBool();

        sky_material.SetShaderParameter("sun_direction", sun_dir);
        sky_material.SetShaderParameter("moon_direction", moon_dir);
        sky_material.SetShaderParameter("haze", haze);
        sky_material.SetShaderParameter("star_rotation", hour / 24.0 * TAU);
        // Clouds break apart as the evening cools, revealing the star field.
        sky_material.SetShaderParameter("cloud_coverage", weather != null ? weather.cloud_coverage(Atmosphere.daylight(hour)) : lerpf(0.18, 0.36, Atmosphere.daylight(hour)));

        sky_material.SetShaderParameter("storm_amount", overcast);
        double daylight = Atmosphere.daylight(hour);
        _fog_color = Atmosphere.fog_color(sun_dir, haze);
        _apply_fog();
        environment.VolumetricFogAmbientInject = (float)lerpf(0.04, 0.18, daylight);
        environment.TonemapExposure = (float)(_exposure_for(daylight, sun_dir) * exposure_scale);
        // Night vision is colour-poor: desaturate and cool the grade after dark,
        // further still once the sun is well down and only the moon is left.
        environment.AdjustmentSaturation = (float)lerpf(lerpf(0.86, 0.72, deep_night), 0.98, daylight);
        environment.AdjustmentContrast = (float)lerpf(1.04, 1.02, daylight);

        Color sun_col = sun_light["color"].AsColor();
        double sun_energy = sun.LightEnergy;
        Vector3 sun_radiance = new Vector3(sun_col.R, sun_col.G, sun_col.B) * (float)sun_energy;
        RenderingServer.GlobalShaderParameterSet("sun_direction", sun_dir);
        RenderingServer.GlobalShaderParameterSet("sun_color", sun_radiance);
        RenderingServer.GlobalShaderParameterSet("daylight", daylight);
        EmitSignal(SignalName.hour_changed, hour);
    }

    public double _exposure_for(double daylight, Vector3 sun_dir)
    {
        /// Manual exposure curve: bright days are pulled down, deep night is lifted so
        /// the fire-lit camp stays readable without ever looking like daylight.
        double low_sun = 1.0 - smoothstep(0.0, 0.45, sun_dir.Y);
        double day_exposure = lerpf(0.9, 1.25, low_sun);
        double night_exposure = 2.7;
        return lerpf(night_exposure, day_exposure, daylight);
    }

    public static Vector3 _stable_up(Vector3 dir)
    {
        return absf(dir.Y) < 0.98 ? Vector3.Up : Vector3.Forward;
    }

    public Color fog_color()
    {
        return _fog_color;
    }

    public double wind_strength()
    {
        return _gust;
    }

    public Vector3 sun_direction()
    {
        return Atmosphere.sun_direction(hour);
    }

    public double daylight()
    {
        return Atmosphere.daylight(hour);
    }

    public void _update_underwater(double delta)
    {
        // --------------------------------------------------------------- underwater
        /// Fog and volumetrics take over from the per-pixel crossing pass once the
        /// active camera's entire near plane is under the pond surface.
        Camera3D camera = GetViewport().GetCamera3D();
        double blend = 0.0;
        double lens_depth = -1.0;
        if (camera != null && Game.Instance.camp != null)
        {
            Vector3 p = camera.GlobalPosition;
            if (Game.Instance.camp.field.water_depth(p.X, p.Z) > 0.0)
            {
                double level = Game.Instance.camp.pond != null ? Game.Instance.camp.pond.surface_height(new Vector2(p.X, p.Z)) : TerrainField.WATER_LEVEL;
                lens_depth = level - p.Y;
                Vector2 size = GetViewport().GetVisibleRect().Size;
                double aspect = size.X / maxf(size.Y, 1.0);
                double tan_half = tan(deg_to_rad(camera.Fov) * 0.5);
                if (camera.KeepAspect == Camera3D.KeepAspectEnum.Width)
                {
                    tan_half /= aspect;
                }
                blend = water_fog_blend(camera.GlobalTransform, camera.Near, tan_half, aspect);
            }
        }
        update_water_lens(lens_depth, delta);
        if (!is_equal_approx(blend, underwater_blend))
        {
            underwater_blend = blend;
            _apply_fog();
            _fog_history_reset = 6;
        }
        _apply_water_focus(blend);
        bool now = blend > 0.001;
        if (now != underwater)
        {
            underwater = now;
            _apply_features();
            // Keep history writes active while giving old samples zero weight.
            // Disabling reprojection can retain the old water history for reuse.
            _fog_history_reset = 6;
        }
        if (_fog_history_reset > 0)
        {
            _fog_history_reset -= 1;
            environment.VolumetricFogTemporalReprojectionEnabled = true;
            environment.VolumetricFogTemporalReprojectionAmount = 0.0f;
        }
        else
        {
            environment.VolumetricFogTemporalReprojectionAmount = 0.92f;
        }
    }

    public void update_water_lens(double depth, double delta)
    {
        /// The dead band prevents waves from firing a new splash on every frame.
        /// The first pose (including a capture teleport) does not fabricate a crossing.
        lens_entry_age = minf(lens_entry_age + delta, 100.0);
        lens_exit_age = minf(lens_exit_age + delta, 100.0);
        if (!_lens_initialized)
        {
            _lens_initialized = true;
            _lens_in_water = depth > 0.04;
            return;
        }
        if (!_lens_in_water && depth > 0.005)
        {
            _lens_in_water = true;
            lens_entry_age = 0.0;
            if (IsInsideTree() && Game.Instance.audio != null)
            {
                Game.Instance.audio.water_crossing(true);
            }
            Camera3D camera = IsInsideTree() ? GetViewport().GetCamera3D() : null;
            if (camera != null && Game.Instance.camp != null && Game.Instance.camp.pond != null)
            {
                Game.Instance.camp.pond.ripple(camera.GlobalPosition, 1.1);
            }
            if (entry_bubbles != null && camera != null)
            {
                entry_bubbles.GlobalPosition = camera.GlobalPosition - camera.GlobalBasis.Z * 0.30f - camera.GlobalBasis.Y * 0.16f;
                entry_bubbles.Restart();
                entry_bubbles.Emitting = true;
            }
        }
        else if (_lens_in_water && depth < -0.04)
        {
            _lens_in_water = false;
            lens_exit_age = 0.0;
            if (IsInsideTree() && Game.Instance.audio != null)
            {
                Game.Instance.audio.water_crossing(false);
            }
        }
    }

    public void _apply_water_focus(double blend)
    {
        /// Native depth-of-field lets nearby timber keep its silhouette while distant
        /// detail dissolves. The final lens pass adds only a small baseline defocus.
        /// Global DOF starts after the whole near plane is underwater, preserving the
        /// sharp above-water part of a split view and the user's previous photo focus.
        if (blend > 0.0)
        {
            if ((_water_focus_saved.Count == 0))
            {
                foreach (Variant key in new Godot.Collections.Array { (StringName)"dof_blur_far_enabled", (StringName)"dof_blur_near_enabled", (StringName)"dof_blur_far_distance", (StringName)"dof_blur_far_transition", (StringName)"dof_blur_amount" })
                {
                    _water_focus_saved[key] = attributes.Get(key.AsStringName());
                }
            }
            attributes.DofBlurNearEnabled = false;
            attributes.DofBlurFarEnabled = true;
            attributes.DofBlurFarDistance = 2.2f;
            attributes.DofBlurFarTransition = 6.0f;
            attributes.DofBlurAmount = (float)(0.010 * blend);
        }
        else if (!(_water_focus_saved.Count == 0))
        {
            foreach (Variant key2 in _water_focus_saved.Keys)
            {
                attributes.Set(key2.AsStringName(), _water_focus_saved[key2]);
            }
            _water_focus_saved.Clear();
        }
    }

    public void _build_entry_bubbles()
    {
        entry_bubbles = new GpuParticles3D();
        entry_bubbles.Name = "ImmersionBubbles";
        entry_bubbles.Layers = unchecked((uint)(Pond.WATER_LAYER));
        entry_bubbles.Emitting = false;
        entry_bubbles.OneShot = true;
        entry_bubbles.Amount = 36;
        entry_bubbles.Lifetime = 1.8;
        entry_bubbles.Explosiveness = 0.90f;
        entry_bubbles.LocalCoords = false;
        entry_bubbles.VisibilityAabb = new Aabb(new Vector3(-2, -2, -2), new Vector3(4, 6, 4));
        entry_bubbles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        ParticleProcessMaterial process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        process.EmissionBoxExtents = new Vector3(0.22f, 0.025f, 0.08f);
        process.Direction = Vector3.Up;
        process.Spread = 22.0f;
        process.InitialVelocityMin = 0.35f;
        process.InitialVelocityMax = 0.75f;
        process.Gravity = new Vector3(0, 0.18f, 0);
        process.ScaleMin = 0.4f;
        process.ScaleMax = 1.3f;
        entry_bubbles.ProcessMaterial = process;
        QuadMesh quad = new QuadMesh();
        quad.Size = new Vector2(0.010f, 0.013f);
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/bubble.gdshader");
        mat.SetShaderParameter("water_level", TerrainField.WATER_LEVEL);
        quad.Material = mat;
        entry_bubbles.DrawPass1 = quad;
        AddChild(entry_bubbles);
    }

    public void reset_water_lens()
    {
        _lens_initialized = false;
        lens_entry_age = 100.0;
        lens_exit_age = 100.0;
    }

    public static double water_fog_blend(Transform3D frame, double near, double tan_half, double aspect)
    {
        /// Global fog cannot split the image. Begin its handoff only after the highest
        /// near-plane corner is below even the lowest possible wave; the per-pixel
        /// pass supplies the remaining water density until the handoff is complete.
        double highest_y = frame.Origin.Y + near * (-frame.Basis.Z.Y + tan_half * (absf(frame.Basis.Y.Y) + aspect * absf(frame.Basis.X.Y)));
        return smoothstep(0.0, 0.05, TerrainField.WATER_LEVEL - WATER_WAVE_ENVELOPE - highest_y);
    }

    public void _apply_fog()
    {
        double u = underwater_blend;
        environment.BackgroundColor = Colors.Black.Lerp(UNDERWATER_SKY, (float)u);
        // Aerial perspective is the horizon sky, which sits a little cooler than
        // the averaged scattering sample: bias the haze toward blue so distant
        // canopy fades grey-blue instead of yellow.
        Color haze_colour = new Color((float)(_fog_color.R * 0.94), (float)(_fog_color.G * 0.99), (float)minf(_fog_color.B * 1.06, 1.0)).Lightened(0.03f);
        environment.FogLightColor = haze_colour.Lerp(UNDERWATER_FOG, (float)u);
        double rain = weather != null ? weather.rain : 0.0;
        environment.FogDensity = (float)lerpf(AIR_FOG_DENSITY + rain * 0.006, WATER_FOG_DENSITY, u);
        environment.FogAerialPerspective = (float)lerpf(0.6, 0.0, u);
        environment.FogSkyAffect = (float)lerpf(0.12, 1.0, u);
        environment.FogSunScatter = (float)lerpf(0.07, 0.0, u);
        environment.FogHeightDensity = (float)lerpf(0.005, 0.0, u);
        environment.VolumetricFogDensity = (float)lerpf(AIR_VOLUME_DENSITY + rain * 0.0012, WATER_VOLUME_DENSITY, u);
        environment.VolumetricFogAlbedo = new Color(0.92f, 0.92f, 0.92f).Lerp(WATER_VOLUME_ALBEDO, (float)u);
        environment.VolumetricFogAnisotropy = (float)lerpf(0.72, 0.75, u);
        environment.VolumetricFogSkyAffect = (float)lerpf(0.45, 1.0, u);
        double deep_night = smoothstep(-0.12, -0.35, Atmosphere.sun_direction(hour).Y);
        environment.AmbientLightEnergy = (float)lerpf(lerpf(1.6, 1.0, daylight()) * lerpf(1.0, 0.75, deep_night) * lerpf(1.0, 0.65, rain), 0.6, u);
        environment.FogLightEnergy = 1.0f;
        if (waterline_material != null)
        {
            waterline_material.SetShaderParameter("density", (WATER_FOG_DENSITY - AIR_FOG_DENSITY) * (1.0 - u));
        }
    }

    public void _update_post()
    {
        if (post == null)
        {
            return;
        }
        double day = daylight();
        bool photo = Game.Instance.mode == Game.Mode.PHOTO;
        post.set_param("grain", lerpf(0.005, 0.007, day));
        post.set_param("vignette", photo ? 0.16 : 0.12);
        post.set_param("chromatic", 0.025);
        post.set_param("sharpen", 0.13);
        post.set_param("entry_age", lens_entry_age);
        post.set_param("exit_age", lens_exit_age);
        _update_waterline();
    }

    public void _update_waterline()
    {
        /// Feeds the camera frame to the waterline passes so each pixel can decide
        /// whether its near-plane point is under the pond surface.
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null || Game.Instance.camp == null)
        {
            post.set_param("underwater", 0.0);
            if (waterline != null)
            {
                waterline.Visible = false;
            }
            if (water_volume != null)
            {
                water_volume.Visible = false;
            }
            return;
        }
        Transform3D xf = camera.GlobalTransform;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        double aspect = size.X / maxf(size.Y, 1.0);
        double tan_half = tan(deg_to_rad(camera.Fov) * 0.5);
        if (camera.KeepAspect == Camera3D.KeepAspectEnum.Width)
        {
            tan_half /= aspect;
        }
        bool over_water = Game.Instance.camp.field.water_depth(xf.Origin.X, xf.Origin.Z) > 0.0;
        if (water_volume != null)
        {
            double near_surface = 1.0 - smoothstep(TerrainField.WATER_LEVEL + 0.015, TerrainField.WATER_LEVEL + 0.15, xf.Origin.Y);
            double volume_density = WATER_VOLUME_DENSITY * (1.0 - underwater_blend) * near_surface;
            water_volume_material.SetShaderParameter("density", volume_density);
            water_volume.Visible = over_water && environment.VolumetricFogEnabled && volume_density > 0.00001;
        }
        post.set_param("underwater", over_water ? 1.0 : 0.0);
        post.set_param("cam_pos", xf.Origin);
        post.set_param("cam_right", xf.Basis.X);
        post.set_param("cam_up", xf.Basis.Y);
        post.set_param("cam_fwd", -xf.Basis.Z);
        post.set_param("cam_tan_half", tan_half);
        post.set_param("cam_aspect", aspect);
        post.set_param("cam_near", camera.Near);
        post.set_param("water_level", TerrainField.WATER_LEVEL);
        if (waterline == null)
        {
            return;
        }
        double lowest_y = xf.Origin.Y + camera.Near * (-xf.Basis.Z.Y - tan_half * (absf(xf.Basis.Y.Y) + aspect * absf(xf.Basis.X.Y)));
        waterline.Visible = over_water && lowest_y < TerrainField.WATER_LEVEL + WATER_WAVE_ENVELOPE + 0.002 && underwater_blend < 1.0;
        if (waterline.Visible)
        {
            waterline_material.SetShaderParameter("strength", 1.0);
        }
    }

    public void _on_quality_changed(QualityPreset p)
    {
        // ------------------------------------------------------------------ quality
        _preset = p;
        _apply_features();
        sun.DirectionalShadowMaxDistance = (float)p.directional_shadow_distance;
        switch (p.directional_shadow_splits)
        {
            case 2:
                sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                break;
            case 3:
                sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                sun.DirectionalShadowSplit1 = 0.12f;
                break;
            default:
                sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
                break;
        }
        sky.RadianceSize = p.sky_radiance;
        // Film and Ultra march the clouds with 40 steps and a finer erosion octave.
        sky_material.SetShaderParameter("cloud_quality", p.tier == QualityPreset.Tier.ULTRA ? 2 : p.tier == QualityPreset.Tier.HIGH ? 1 : 0);
        if (post != null)
        {
            post.set_enabled(p.lens_effects);
        }
    }

    public void _apply_features()
    {
        /// Combines the preset with the warm-up gate.
        if (_preset == null || environment == null)
        {
            return;
        }
        QualityPreset p = _preset;
        environment.VolumetricFogEnabled = p.volumetric_fog && warm_stage >= WorldController.Warm.VOLUMETRICS;
        environment.SsaoEnabled = p.ssao && warm_stage >= WorldController.Warm.SCREEN_SPACE;
        environment.SsilEnabled = p.ssil && warm_stage >= WorldController.Warm.SCREEN_SPACE;
        environment.SdfgiEnabled = p.sdfgi && warm_stage >= WorldController.Warm.GLOBAL_ILLUMINATION;
        environment.SdfgiCascades = (int)p.sdfgi_cascades;
        environment.SsrEnabled = p.ssr && warm_stage >= WorldController.Warm.ALL;
        environment.SsrMaxSteps = (int)p.ssr_steps;
        environment.GlowEnabled = p.glow;
        foreach (FogVolume volume in _mist)
        {
            volume.Visible = environment.VolumetricFogEnabled && !underwater;
        }
    }

    public void _build_mist()
    {
        /// Local banks leave the foreground clear while separating the shoreline and
        /// the forest into depth planes. They share the renderer's warm-up gate.
        Shader shader = Content.Load<Shader>("res://shaders/ground_mist.gdshader");
        Godot.Collections.Array banks = new Godot.Collections.Array { new Godot.Collections.Array { new Vector3(-34.0f, -0.15f, 4.0f), new Vector3(48.0f, 2.6f, 38.0f), 0.040 }, new Godot.Collections.Array { new Vector3(0.0f, 4.0f, -43.0f), new Vector3(90.0f, 7.0f, 24.0f), 0.025 }, new Godot.Collections.Array { new Vector3(42.0f, 4.5f, 5.0f), new Vector3(28.0f, 7.0f, 75.0f), 0.022 }, new Godot.Collections.Array { new Vector3(0.0f, 0.9f, -4.0f), new Vector3(20.0f, 1.6f, 12.0f), 0.11 } };
        // Thinner over the pond: with the sun low across the water the bank
        // turned the left half of the jetty shots into a white haze.
        // After-rain ground mist over the camp: thin and low. At 0.38 it lit up as
        // a white bank around the tent in the sunlit after-the-rain shot.
        foreach (Variant bank in banks)
        {
            FogVolume volume = new FogVolume();
            volume.Name = G.format("LowMist%d", (long)_mist.Count);
            volume.Layers = unchecked((uint)(Pond.AIR_FOG_LAYER));
            volume.Shape = RenderingServer.FogVolumeShape.Box;
            volume.Size = G.Index(bank, 1).AsVector3();
            volume.Position = G.Index(bank, 0).AsVector3();
            ShaderMaterial mat = new ShaderMaterial();
            mat.Shader = shader;
            mat.SetShaderParameter("density", G.Index(bank, 2));
            mat.SetShaderParameter("after_rain", (long)_mist.Count == 3);
            volume.Material = mat;
            volume.Visible = false;
            AddChild(volume);
            _mist.Add(volume);
        }
    }
    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.world, this)) Game.Instance.world = null;
    }

}
