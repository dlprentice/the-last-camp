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

/// The pond: tessellated surface mesh, water material and a planar reflection
/// rendered by a mirrored camera into a SubViewport. The mirror camera uses a
/// lightweight Environment and an extra cull layer that lets shaders clip
/// geometry below the water plane (see common.gdshaderinc).
public partial class Pond : Node3D
{
    public const long WATER_LAYER = 1L << 4;
    public const long REFLECTION_LAYER = 1L << 19;
    public const long UNDERWATER_REFLECTION_LAYER = 1L << 18;
    public const long AIR_FOG_LAYER = 1L << 17;
    public const long GRASS_LAYER = 1L << 3;
    /// ~31 cm cells keep the displaced triangles close to the analytic waves
    /// used by lilies and the lens waterline; metre-wide cells left visible gaps.
    public const long GRID_CELLS = 192;
    public const double SURFACE_MARGIN = 7.0;

    public TerrainField field;
    public MeshInstance3D surface;
    public ShaderMaterial material;
    public ShaderMaterial underside_material;
    public SubViewport reflection_viewport;
    public Camera3D reflection_camera;
    public Environment reflection_environment;
    public SubViewport underwater_viewport;
    public Camera3D underwater_camera;
    public ShaderMaterial underwater_filter_material;
    public Environment underwater_environment;
    public SubViewport transmission_viewport;
    public Camera3D transmission_camera;
    public Environment transmission_environment;
    public bool planar_enabled = true;
    public bool _active = false;
    public Shader _planar_shader;
    public Shader _fallback_shader;
    public Shader _underside_planar_shader;
    public Shader _underside_fallback_shader;
    public double _scale = 0.5;
    public bool _half_rate = false;
    public long _frame_parity = 0;
    public bool _reflection_visible = false;
    public double _wading = 0.0;
    public const long RIPPLE_COUNT = 8;
    public double _ripple_clock = 0.0;
    public long _ripple_cursor = 0;
    public List<Vector4> _ripples = new List<Vector4>();
    public double _skip_age = -1.0;
    public Vector3 _skip_origin = Vector3.Zero;
    public Vector3 _skip_direction = Vector3.Left;
    public long _skip_index = 0;
    public MeshInstance3D _stone;
    public PondSimulation simulation;
    public double surface_time = 0.0;
    public bool simulation_paused = false;
    public bool _simulation_bound = false;
    public Canoe _canoe;
    public double _interaction_clock = 0.0;
    public Vector2 _previous_bow = Vector2.Zero;
    public Vector2 _previous_wader = Vector2.Inf;
    public List<Vector2> _probe_positions = new List<Vector2>();
    public List<float> _probe_heights = new List<float>();
    public RandomNumberGenerator _rain_rng = new RandomNumberGenerator();
    public double _rain_accumulator = 0.0;
    public const long INTERACTION_RESOLUTION = 256;

    public Pond(TerrainField p_field)
    {
        field = p_field;
        Name = "Pond";
        _rain_rng.Seed = unchecked((ulong)(73191));
    }

    public Pond()
    {
    }

    public void build()
    {
        _build_shaders();
        _build_surface();
        _build_reflection();
        G.resize(_ripples, (int)RIPPLE_COUNT);
        for (long i = 0; i < RIPPLE_COUNT; i++)
        {
            _ripples[(int)i] = new Vector4(0, 0, -100, 0);
        }
        material.SetShaderParameter("ripples", Variant.From(_ripples.ToArray()));
        _stone = new MeshInstance3D();
        _stone.Mesh = PropMeshes.rock(813, 0.035);
        _stone.MaterialOverride = PropMaterials.iron(new Color(0.20f, 0.23f, 0.22f));
        _stone.Visible = false;
        AddChild(_stone);
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Pond.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
    }

    public void _build_shaders()
    {
        /// Two shader variants from one source: with planar reflections the sky's own
        /// specular contribution is disabled so reflections are not counted twice.
        Shader @base = Content.Load<Shader>("res://shaders/water.gdshader");
        string source = @base.Code;
        _planar_shader = new Shader();
        _planar_shader.Code = source.Replace("//PLANAR_RENDER_MODE", "render_mode ambient_light_disabled;");
        _fallback_shader = new Shader();
        _fallback_shader.Code = source.Replace("//PLANAR_RENDER_MODE", "");
        _underside_planar_shader = new Shader();
        _underside_planar_shader.Code = "#define BELOW_SURFACE\n" + _planar_shader.Code;
        _underside_fallback_shader = new Shader();
        _underside_fallback_shader.Code = "#define BELOW_SURFACE\n" + _fallback_shader.Code;
    }

    public void _build_surface()
    {
        material = new ShaderMaterial();
        material.Shader = _planar_shader;
        Camp.bind_texture(material, "normal_a", "res://textures/water_normal_a.png");
        Camp.bind_texture(material, "normal_b", "res://textures/water_normal_b.png");
        Camp.bind_texture(material, "foam_tex", "res://textures/foam.png");

        MeshBuilder mb = new MeshBuilder();
        mb.use_tangents = false;
        double radius = TerrainField.POND_MAX_RADIUS + SURFACE_MARGIN;
        long n = GRID_CELLS + 1;
        Vector2 centre = TerrainField.POND_CENTRE;
        for (long iz = 0; iz < n; iz++)
        {
            for (long ix = 0; ix < n; ix++)
            {
                double u = (double)ix / (double)GRID_CELLS * 2.0 - 1.0;
                double v = (double)iz / (double)GRID_CELLS * 2.0 - 1.0;
                // Square grid mapped to a disc so triangles stay even near the shore.
                Vector2 p = _square_to_disc(new Vector2((float)u, (float)v)) * (float)radius;
                mb.add_vertex(new Vector3((float)((double)centre.X + p.X), (float)TerrainField.WATER_LEVEL, (float)((double)centre.Y + p.Y)), Vector3.Up, new Vector2((float)u, (float)v) * 0.5f + new Vector2(0.5f, 0.5f));
            }
        }
        for (long iz2 = 0; iz2 < GRID_CELLS; iz2++)
        {
            for (long ix2 = 0; ix2 < GRID_CELLS; ix2++)
            {
                long a = iz2 * n + ix2;
                long b = a + 1;
                long c = a + n;
                long d = c + 1;
                mb.add_triangle(a, b, d);
                mb.add_triangle(a, d, c);
            }
        }
        surface = new MeshInstance3D();
        surface.Name = "Surface";
        surface.Mesh = mb.commit(material);
        surface.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        surface.Layers = unchecked((uint)(WATER_LAYER));
        surface.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        surface.CustomAabb = new Aabb(new Vector3((float)(centre.X - radius), (float)(TerrainField.WATER_LEVEL - 1.0), (float)(centre.Y - radius)), new Vector3((float)(radius * 2.0), 2.0f, (float)(radius * 2.0)));
        AddChild(surface);
        underside_material = (ShaderMaterial)material.Duplicate();
        underside_material.Shader = _underside_planar_shader;
        MeshInstance3D underside = new MeshInstance3D();
        underside.Name = "Underside";
        underside.Mesh = surface.Mesh;
        underside.MaterialOverride = underside_material;
        underside.Layers = unchecked((uint)(WATER_LAYER));
        underside.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        underside.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        underside.CustomAabb = surface.CustomAabb;
        AddChild(underside);
    }

    public static Vector2 _square_to_disc(Vector2 p)
    {
        double x = p.X;
        double y = p.Y;
        if (x == 0.0 && y == 0.0)
        {
            return Vector2.Zero;
        }
        double r = 0;
        double phi = 0;
        if (absf(x) > absf(y))
        {
            r = x;
            phi = PI / 4.0 * (y / x);
        }
        else
        {
            r = y;
            phi = PI / 2.0 - PI / 4.0 * (x / y);
        }
        return new Vector2((float)(r * cos(phi)), (float)(r * sin(phi)));
    }

    public void _build_reflection()
    {
        reflection_viewport = new SubViewport();
        reflection_viewport.Name = "ReflectionViewport";
        reflection_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        reflection_viewport.UseTaa = false;
        reflection_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        reflection_viewport.Msaa3D = Viewport.Msaa.Disabled;
        reflection_viewport.UseDebanding = false;
        // HDR output keeps the mirror image linear so the water shader can
        // composite it before the main tonemapper.
        reflection_viewport.UseHdr2D = true;
        reflection_viewport.PositionalShadowAtlasSize = 0;
        reflection_viewport.MeshLodThreshold = 4.0f;
        reflection_viewport.AudioListenerEnable3D = false;
        reflection_viewport.Size = new Vector2I(960, 540);
        AddChild(reflection_viewport);

        reflection_environment = new Environment();
        reflection_environment.BackgroundMode = Environment.BGMode.Sky;
        reflection_environment.AmbientLightSource = Environment.AmbientSource.Sky;
        reflection_environment.ReflectedLightSource = Environment.ReflectionSource.Sky;
        reflection_environment.TonemapMode = Environment.ToneMapper.Linear;
        reflection_environment.FogEnabled = true;
        reflection_environment.FogMode = Environment.FogModeEnum.Exponential;
        reflection_environment.GlowEnabled = false;
        reflection_environment.SsaoEnabled = false;
        reflection_environment.SsilEnabled = false;
        reflection_environment.SdfgiEnabled = false;
        reflection_environment.SsrEnabled = false;
        reflection_environment.VolumetricFogEnabled = false;

        reflection_camera = new Camera3D();
        reflection_camera.Name = "MirrorCamera";
        reflection_camera.CullMask = unchecked((uint)(0xFFFFF & ~WATER_LAYER & ~GRASS_LAYER & ~UNDERWATER_REFLECTION_LAYER | REFLECTION_LAYER));
        reflection_camera.Environment = reflection_environment;
        reflection_camera.Near = 0.1f;
        reflection_camera.Far = 700.0f;
        reflection_viewport.AddChild(reflection_camera);
        material.SetShaderParameter("reflection_tex", reflection_viewport.GetTexture());
        material.SetShaderParameter("debug_reflection", Game.Instance.has_flag("debug-reflection"));
        material.SetShaderParameter("highlight_debug", G.to_int(Game.Instance.arg_value("water-debug", "0")));
        // A separate mirror keeps the submerged halfspace. Both mirrors remain
        // available while the lens straddles the surface, avoiding a texture swap
        // precisely when the waterline passes across the image.
        underwater_viewport = new SubViewport();
        underwater_viewport.Name = "UnderwaterReflectionViewport";
        underwater_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        underwater_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        underwater_viewport.UseHdr2D = true;
        underwater_viewport.PositionalShadowAtlasSize = 0;
        underwater_viewport.AudioListenerEnable3D = false;
        underwater_viewport.MeshLodThreshold = 4.0f;
        underwater_viewport.Size = reflection_viewport.Size;
        AddChild(underwater_viewport);
        underwater_environment = (Environment)reflection_environment.Duplicate();
        underwater_environment.BackgroundMode = Environment.BGMode.Color;
        underwater_environment.BackgroundColor = new Color(0.05f, 0.17f, 0.15f);
        underwater_environment.FogEnabled = false;
        underwater_environment.VolumetricFogDensity = 0.0f;
        underwater_environment.VolumetricFogAlbedo = WorldController.WATER_VOLUME_ALBEDO;
        underwater_environment.VolumetricFogAnisotropy = 0.75f;
        underwater_environment.VolumetricFogLength = 90.0f;
        underwater_environment.VolumetricFogDetailSpread = 2.2f;
        underwater_environment.VolumetricFogAmbientInject = 0.18f;
        underwater_environment.VolumetricFogTemporalReprojectionEnabled = false;
        // The reflected leg needs the same lit water as the direct leg. Extinction
        // alone made the reflection dark and left a visible seam at the horizon.
        FogVolume reflected_volume = new FogVolume();
        reflected_volume.Name = "ReflectedWaterVolume";
        reflected_volume.Layers = unchecked((uint)(UNDERWATER_REFLECTION_LAYER));
        reflected_volume.Shape = RenderingServer.FogVolumeShape.Box;
        reflected_volume.Size = new Vector3(60.0f, 6.0f, 60.0f);
        // The mirror uses the mean plane while the visible surface moves within
        // the wave envelope. Include that envelope so near-horizontal reflected
        // rays do not fall through a centimetre-wide unlit gap at the waterline.
        reflected_volume.Position = new Vector3(TerrainField.POND_CENTRE.X, (float)(TerrainField.WATER_LEVEL - 3.0 + WorldController.WATER_WAVE_ENVELOPE), TerrainField.POND_CENTRE.Y);
        ShaderMaterial volume_material = new ShaderMaterial();
        volume_material.Shader = Content.Load<Shader>("res://shaders/pond_fog.gdshader");
        volume_material.SetShaderParameter("density", WorldController.WATER_VOLUME_DENSITY);
        volume_material.SetShaderParameter("water_albedo", WorldController.WATER_VOLUME_ALBEDO);
        reflected_volume.Material = volume_material;
        AddChild(reflected_volume);
        underwater_camera = new Camera3D();
        underwater_camera.Name = "UnderwaterMirrorCamera";
        underwater_camera.CullMask = unchecked((uint)(((long)reflection_camera.CullMask | UNDERWATER_REFLECTION_LAYER) & ~AIR_FOG_LAYER));
        underwater_camera.Environment = underwater_environment;
        underwater_camera.Near = 0.05f;
        underwater_camera.Far = 90.0f;
        underwater_viewport.AddChild(underwater_camera);
        MeshInstance3D mirror_fog = new MeshInstance3D();
        mirror_fog.Name = "ReflectedWaterPath";
        mirror_fog.Layers = unchecked((uint)(UNDERWATER_REFLECTION_LAYER));
        QuadMesh fog_quad = new QuadMesh();
        fog_quad.Size = new Vector2(2, 2);
        mirror_fog.Mesh = fog_quad;
        mirror_fog.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        mirror_fog.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        mirror_fog.CustomAabb = new Aabb(Vector3.One * (-4000.0f), Vector3.One * 8000.0f);
        ShaderMaterial fog_material = new ShaderMaterial();
        fog_material.Shader = Content.Load<Shader>("res://shaders/post/water_mirror_fog.gdshader");
        fog_material.SetShaderParameter("density", WorldController.WATER_FOG_DENSITY);
        fog_material.SetShaderParameter("fog_color", WorldController.UNDERWATER_FOG);
        fog_material.RenderPriority = 100;
        mirror_fog.MaterialOverride = fog_material;
        AddChild(mirror_fog);
        ColorRect mirror_filter = new ColorRect();
        mirror_filter.Name = "RoughWaterFilter";
        underwater_viewport.AddChild(mirror_filter);
        mirror_filter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        mirror_filter.MouseFilter = Control.MouseFilterEnum.Ignore;
        underwater_filter_material = new ShaderMaterial();
        underwater_filter_material.Shader = Content.Load<Shader>("res://shaders/post/water_mirror_filter.gdshader");
        mirror_filter.Material = underwater_filter_material;
        material.SetShaderParameter("underwater_reflection_tex", underwater_viewport.GetTexture());
        transmission_viewport = new SubViewport();
        transmission_viewport.Name = "WaterTransmissionViewport";
        transmission_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        transmission_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        transmission_viewport.UseHdr2D = true;
        transmission_viewport.PositionalShadowAtlasSize = 0;
        transmission_viewport.AudioListenerEnable3D = false;
        transmission_viewport.MeshLodThreshold = 4.0f;
        transmission_viewport.Size = reflection_viewport.Size;
        AddChild(transmission_viewport);
        transmission_environment = (Environment)reflection_environment.Duplicate();
        transmission_camera = new Camera3D();
        transmission_camera.Name = "AirTransmissionCamera";
        transmission_camera.Environment = transmission_environment;
        transmission_camera.CullMask = unchecked((uint)((long)reflection_camera.CullMask));
        transmission_camera.Near = 0.05f;
        transmission_camera.Far = 700.0f;
        transmission_viewport.AddChild(transmission_camera);
        if (underside_material != null)
        {
            underside_material.SetShaderParameter("underwater_reflection_tex", underwater_viewport.GetTexture());
            underside_material.SetShaderParameter("transmission_tex", transmission_viewport.GetTexture());
        }
    }

    public void activate()
    {
        /// Starts rendering the planar reflection (deferred until the renderer is warm).
        _active = true;
        _start_simulation();
        _apply_update_mode(false);
    }

    public void _start_simulation()
    {
        if (simulation != null || Game.Instance.camp == null || Game.Instance.camp.campsite == null)
        {
            return;
        }
        ProcessPhysicsPriority = -20;
        Dock dock = Game.Instance.camp.campsite.dock;
        _canoe = dock.canoe;
        _canoe.start_floating(this);
        Vector3 bow = _canoe.bow_point();
        _previous_bow = new Vector2(bow.X, bow.Z);
        if (Game.Instance.has_flag("no-water-simulation"))
        {
            return;
        }
        double radius = TerrainField.POND_MAX_RADIUS + 1.0;
        Rect2 bounds = new Rect2(TerrainField.POND_CENTRE - Vector2.One * (float)radius, Vector2.One * (float)radius * 2.0f);
        Image mask = interaction_mask(field, Game.Instance.camp.plan, dock, bounds, INTERACTION_RESOLUTION);
        simulation = new PondSimulation();
        simulation.setup(bounds, mask, INTERACTION_RESOLUTION);
        RenderingServer.GlobalShaderParameterSet("pond_interaction_bounds", new Vector4(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y));
    }

    public static Image interaction_mask(TerrainField terrain, ScenePlan plan, Dock dock, Rect2 bounds, long resolution)
    {
        Image mask = Image.CreateEmpty((int)resolution, (int)resolution, false, Image.Format.Rf);
        Vector2 cell = bounds.Size / (float)(double)resolution;
        for (long y = 0; y < resolution; y++)
        {
            for (long x = 0; x < resolution; x++)
            {
                Vector2 p = bounds.Position + (new Vector2(x, y) + Vector2.One * 0.5f) * cell;
                mask.SetPixel((int)x, (int)y, new Color((float)(terrain.water_depth(p.X, p.Y) < 0.025 ? 1.0 : 0.0), 0, 0));
            }
        }
        if (plan != null)
        {
            foreach (ScenePlan.RockEntry rock in plan.rocks)
            {
                double radius = rock.scale * CampRocks.COLLIDE_SCALE;
                double centre_y = terrain.height(rock.position.X, rock.position.Y) - rock.sink * rock.scale * 0.35 + rock.scale * 0.2;
                double dy = TerrainField.WATER_LEVEL - centre_y;
                if (absf(dy) < radius)
                {
                    _mask_disc(mask, bounds, rock.position, sqrt(radius * radius - dy * dy));
                }
            }
        }
        if (dock != null)
        {
            foreach (Vector3 local in dock._piles)
            {
                Vector3 p2 = dock.ToGlobal(local);
                // Keep a sub-cell pile represented without opening pinholes in the mask.
                _mask_disc(mask, bounds, new Vector2(p2.X, p2.Z), maxf(Dock.PILE_RADIUS, cell.X * 0.75));
            }
        }
        return mask;
    }

    public static void _mask_disc(Image mask, Rect2 bounds, Vector2 centre, double radius)
    {
        long size = mask.GetWidth();
        Vector2 cell = bounds.Size / (float)(double)size;
        Vector2 low = ((centre - Vector2.One * (float)radius - bounds.Position) / cell).Floor();
        Vector2 high = ((centre + Vector2.One * (float)radius - bounds.Position) / cell).Ceil();
        for (long y = maxi(0, (long)low.Y), y_end = mini(size, (long)high.Y + 1); y < y_end; y++)
        {
            for (long x = maxi(0, (long)low.X), x_end = mini(size, (long)high.X + 1); x < x_end; x++)
            {
                Vector2 at = bounds.Position + (new Vector2(x, y) + Vector2.One * 0.5f) * cell;
                if (at.DistanceSquaredTo(centre) <= radius * radius)
                {
                    mask.SetPixel((int)x, (int)y, new Color(1, 0, 0));
                }
            }
        }
    }

    public double base_height(Vector2 at)
    {
        double strength = Game.Instance.world != null ? Game.Instance.world.wind_strength() : 0.4;
        return PondSurface.height_at(at, surface_time, WorldController.WIND_DIRECTION, strength);
    }

    public double surface_height(Vector2 at)
    {
        double height = base_height(at);
        // Sparse async samples are for bodies/lens only. A camera cut must never
        // reuse a residual from its former location; distant queries use base waves.
        for (long i = 0, i_end = mini((long)_probe_positions.Count, (long)_probe_heights.Count); i < i_end; i++)
        {
            if (at.DistanceSquaredTo(_probe_positions[(int)i]) < 0.04)
            {
                return height + _probe_heights[(int)i];
            }
        }
        return height;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active)
        {
            return;
        }
        surface_time += delta;
        RenderingServer.GlobalShaderParameterSet("pond_time", surface_time);
        if (simulation == null || simulation_paused)
        {
            return;
        }
        if (!_simulation_bound && simulation.is_ready())
        {
            RenderingServer.GlobalShaderParameterSet("pond_interaction_tex", simulation.get_texture());
            RenderingServer.GlobalShaderParameterSet("pond_interaction_enabled", true);
            _simulation_bound = true;
        }
        List<Vector2> probes = _canoe.probe_positions();
        Camera3D camera = GetViewport().GetCamera3D();
        Vector3 lens = camera != null ? camera.GlobalPosition : Vector3.Zero;
        probes.Add(new Vector2(lens.X, lens.Z));
        simulation.set_probe_positions(probes);
        _probe_heights = simulation.get_probe_heights();
        _probe_positions = simulation.get_sampled_probe_positions();
        if ((long)_probe_heights.Count >= (long)Canoe.FLOAT_POINTS.Count)
        {
            _canoe.set_residual_heights(G.slice(_probe_heights, 0, (long)Canoe.FLOAT_POINTS.Count));
        }
        _interaction_clock += delta;
        if (_interaction_clock >= 1.0 / 12.0)
        {
            double elapsed = _interaction_clock;
            _interaction_clock = 0.0;
            Vector3 bow = _canoe.bow_point();
            Vector2 at = new Vector2(bow.X, bow.Z);
            double travel = at.DistanceTo(_previous_bow);
            if (travel > 0.002 && travel < 0.5)
            {
                simulation.queue_wake(_previous_bow, at, 0.32, minf(travel / elapsed * 0.12, 0.10));
            }
            _previous_bow = at;
            if (_wading > 0.01 && Game.Instance.player != null)
            {
                Vector3 p = Game.Instance.player.GlobalPosition;
                Vector2 now = new Vector2(p.X, p.Z);
                if (_previous_wader.IsFinite() && _previous_wader.DistanceTo(now) < 1.0)
                {
                    simulation.queue_wake(_previous_wader, now, 0.25, 0.14 * _wading);
                }
                _previous_wader = now;
            }
            else
            {
                _previous_wader = Vector2.Inf;
            }
        }
        // The large raindrop contacts join the persistent field. Fine rain texture
        // still supplies sub-cell detail; no expensive all-drop CPU simulation.
        double rain = Game.Instance.world != null && Game.Instance.world.weather != null ? Game.Instance.world.weather.rain : 0.0;
        _rain_accumulator += rain * delta * 24.0;
        for (long drop = 0, drop_end = mini(floori(_rain_accumulator), 12); drop < drop_end; drop++)
        {
            _rain_accumulator -= 1.0;
            Vector2 at2 = new Vector2(lens.X, lens.Z) + new Vector2(_rain_rng.RandfRange(-9.0f, 9.0f), _rain_rng.RandfRange(-9.0f, 9.0f));
            if (field.water_depth(at2.X, at2.Y) > 0.03)
            {
                simulation.queue_impulse(at2, 0.16, -0.035);
            }
        }
        simulation.step(delta, WorldController.WIND_DIRECTION.Normalized() * 0.025f);
    }

    public override void _ExitTree()
    {
        RenderingServer.GlobalShaderParameterSet("pond_interaction_enabled", false);
        if (simulation != null)
        {
            simulation.shutdown();
        }
    }

    public override void _Process(double delta)
    {
        _ripple_clock += delta;
        if (material == null)
        {
            return;
        }
        material.SetShaderParameter("ripple_clock", _ripple_clock);
        underside_material.SetShaderParameter("ripple_clock", _ripple_clock);
        _update_skip(delta);
        _sync_environment();
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return;
        }
        // A conservative box test includes the bank and displaced water. Only
        // suspend the mirror when its entire surface is outside the frustum.
        bool intersects = bounds_in_frustum(surface.GlobalTransform * surface.CustomAabb, camera.GetFrustum());
        double lens_height = camera.GlobalPosition.Y - TerrainField.WATER_LEVEL;
        bool below_visible = planar_enabled && _active && intersects && lens_height < 0.25;
        underwater_viewport.RenderTargetUpdateMode = below_visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        transmission_viewport.RenderTargetUpdateMode = underwater_viewport.RenderTargetUpdateMode;
        if (below_visible)
        {
            _update_viewport_size();
            _mirror_camera(camera, underwater_camera);
            Projection projection = underwater_camera.GetCameraProjection();
            underwater_filter_material.SetShaderParameter("ray_forward", -underwater_camera.GlobalBasis.Z);
            underwater_filter_material.SetShaderParameter("ray_right", underwater_camera.GlobalBasis.X / projection.X.X);
            underwater_filter_material.SetShaderParameter("ray_up", underwater_camera.GlobalBasis.Y / projection.Y.Y);
            transmission_camera.GlobalTransform = camera.GlobalTransform;
            transmission_camera.Fov = camera.Fov;
            transmission_camera.KeepAspect = camera.KeepAspect;
            transmission_camera.Projection = camera.Projection;
        }
        // Keep the upper view alive through the whole near-plane crossing.
        bool visible_water = lens_height > -0.20 && intersects;
        if (!planar_enabled || !_active || !visible_water)
        {
            _reflection_visible = false;
            _apply_update_mode(true);
            return;
        }
        _update_viewport_size();
        _mirror_camera(camera, reflection_camera);
        if (_half_rate && _reflection_visible)
        {
            // Half-rate mirror: refresh every other frame (the ripples hide the lag).
            _frame_parity = (_frame_parity + 1) % 2;
            reflection_viewport.RenderTargetUpdateMode = _frame_parity == 0 ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        }
        else
        {
            _apply_update_mode(false);
        }
        _reflection_visible = true;
    }

    public static bool bounds_in_frustum(Aabb bounds, Godot.Collections.Array<Plane> planes)
    {
        Vector3 centre = bounds.GetCenter();
        Vector3 half = bounds.Size * 0.5f;
        foreach (Plane plane in planes)
        {
            if (plane.DistanceTo(centre) > plane.Normal.Abs().Dot(half))
            {
                return false;
            }
        }
        return true;
    }

    public void ripple(Vector3 at, double strength = 0.65)
    {
        /// A finite impulse persists at the point of contact after the player leaves.
        if (simulation != null)
        {
            simulation.queue_impulse(new Vector2(at.X, at.Z), 0.24, -strength * 0.40);
        }
        _ripples[(int)_ripple_cursor] = new Vector4(at.X, at.Z, (float)_ripple_clock, (float)strength);
        _ripple_cursor = (_ripple_cursor + 1) % RIPPLE_COUNT;
        material.SetShaderParameter("ripples", Variant.From(_ripples.ToArray()));
        underside_material.SetShaderParameter("ripples", Variant.From(_ripples.ToArray()));
    }

    public bool skip_stone(Vector3 origin, Vector3 direction)
    {
        /// Three diminishing ballistic hops, with an impact sound at each contact.
        if (_skip_age >= 0.0 || direction.LengthSquared() < 0.01)
        {
            return false;
        }
        Vector3 flat = new Vector3(direction.X, 0, direction.Z).Normalized();
        Vector3 target = origin + flat * 3.0f;
        if (field.water_depth(target.X, target.Z) < 0.25)
        {
            return false;
        }
        _skip_origin = origin;
        _skip_direction = flat;
        _skip_age = 0.0;
        _skip_index = 0;
        _stone.GlobalPosition = origin;
        _stone.Visible = true;
        return true;
    }

    public void _update_skip(double delta)
    {
        if (_skip_age < 0.0)
        {
            return;
        }
        _skip_age += delta;
        double duration = 0.52 * pow(0.75, _skip_index);
        double distance = 3.0 * pow(0.72, _skip_index);
        Vector3 target = _skip_origin + _skip_direction * (float)distance;
        target.Y = (float)(surface_height(new Vector2(target.X, target.Z)) + 0.025);
        double t = clampf(_skip_age / duration, 0.0, 1.0);
        _stone.GlobalPosition = _skip_origin.Lerp(target, (float)t) + Vector3.Up * (float)sin(t * PI) * 0.30f * (float)pow(0.7, _skip_index);
        _stone.RotateZ((float)(delta * 14.0));
        if (t < 1.0)
        {
            return;
        }
        if (field.water_depth(target.X, target.Z) > 0.05)
        {
            ripple(target, 0.9 * pow(0.65, _skip_index));
            if (Game.Instance.audio != null)
            {
                Game.Instance.audio.water_impact(target, _skip_index);
            }
        }
        _skip_index += 1;
        _skip_origin = target;
        _skip_age = 0.0;
        if (_skip_index >= 3 || field.water_depth(target.X, target.Z) < 0.1)
        {
            _skip_age = -1.0;
            _stone.Visible = false;
        }
    }

    public void _apply_update_mode(bool submerged)
    {
        SubViewport.UpdateMode wanted = planar_enabled && _active && !submerged ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (reflection_viewport.RenderTargetUpdateMode != wanted)
        {
            reflection_viewport.RenderTargetUpdateMode = wanted;
        }
    }

    public void _sync_environment()
    {
        /// Keeps the mirror's cheap environment in step with the real one (sky, fog).
        if (Game.Instance.world == null || Game.Instance.world.environment == null)
        {
            return;
        }
        Environment env = Game.Instance.world.environment;
        reflection_environment.Sky = env.Sky;
        reflection_environment.FogLightColor = env.FogLightColor;
        reflection_environment.FogDensity = env.FogDensity;
        reflection_environment.FogAerialPerspective = env.FogAerialPerspective;
        reflection_environment.FogSkyAffect = env.FogSkyAffect;
        reflection_environment.AmbientLightEnergy = env.AmbientLightEnergy;
        reflection_environment.TonemapExposure = 1.0f;
        underwater_environment.Sky = env.Sky;
        underwater_environment.AmbientLightEnergy = env.AmbientLightEnergy;
        underwater_environment.VolumetricFogEnabled = env.VolumetricFogEnabled;
        underwater_environment.VolumetricFogAmbientInject = env.VolumetricFogAmbientInject;
        transmission_environment.Sky = env.Sky;
        transmission_environment.FogLightColor = Game.Instance.world.fog_color();
        transmission_environment.FogDensity = (float)WorldController.AIR_FOG_DENSITY;
        transmission_environment.FogSkyAffect = 0.12f;
        transmission_environment.AmbientLightEnergy = 1.0f;
    }

    public void _update_viewport_size()
    {
        Vector2 main_size = GetViewport().GetVisibleRect().Size;
        Vector2I target = (Vector2I)(main_size * (float)_scale).Round();
        target = new Vector2I((int)maxi(target.X, 160), (int)maxi(target.Y, 90));
        if (reflection_viewport.Size != target)
        {
            reflection_viewport.Size = target;
            underwater_viewport.Size = target;
            transmission_viewport.Size = target;
        }
    }

    public void _mirror_camera(Camera3D camera, Camera3D target)
    {
        /// Reflects the main camera through the water plane. The up axis is negated to
        /// keep the basis right-handed, which flips the image vertically; the water
        /// shader samples it with 1 - v.
        double plane_y = TerrainField.WATER_LEVEL;
        Transform3D xf = camera.GlobalTransform;
        Vector3 pos = xf.Origin;
        pos.Y = (float)(2.0 * plane_y - pos.Y);
        Callable reflect = Callable.From((Vector3 v) => new Vector3(v.X, -v.Y, v.Z));
        Vector3 bx = reflect.Call(xf.Basis.X).AsVector3();
        Vector3 by = reflect.Call(xf.Basis.Y).AsVector3();
        Vector3 bz = reflect.Call(xf.Basis.Z).AsVector3();
        target.GlobalTransform = new Transform3D(new Basis(bx, -by, bz), pos);
        target.Fov = camera.Fov;
        target.KeepAspect = camera.KeepAspect;
        target.Projection = camera.Projection;
    }

    public void set_reflection_scale(double scale)
    {
        /// Mirror resolution as a fraction of the screen (overrides the preset).
        _scale = scale;
        _update_viewport_size();
    }

    public void set_wading(double strength)
    {
        _wading = strength;
        material.SetShaderParameter("wading_strength", strength);
        underside_material.SetShaderParameter("wading_strength", strength);
    }

    public void apply_quality(QualityPreset p)
    {
        planar_enabled = p.planar_reflections;
        _scale = p.reflection_scale;
        _half_rate = p.reflection_half_rate;
        _apply_update_mode(false);
        material.Shader = planar_enabled ? _planar_shader : _fallback_shader;
        material.SetShaderParameter("use_planar", planar_enabled);
        material.SetShaderParameter("reflection_tex", reflection_viewport.GetTexture());
        material.SetShaderParameter("underwater_reflection_tex", underwater_viewport.GetTexture());
        underside_material.Shader = planar_enabled ? _underside_planar_shader : _underside_fallback_shader;
        underside_material.SetShaderParameter("use_planar", planar_enabled);
        underside_material.SetShaderParameter("underwater_reflection_tex", underwater_viewport.GetTexture());
        underside_material.SetShaderParameter("transmission_tex", transmission_viewport.GetTexture());
    }
}
