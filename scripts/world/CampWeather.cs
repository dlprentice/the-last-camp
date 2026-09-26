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

/// Deterministic weather cues shared by interactive preview, captures and film.
/// Rain lives in the world, with terrain/roof rejection and normal depth tests.
public partial class CampWeather : Node3D
{
    public double cloud = 0.0;
    public double rain = 0.0;
    public double wet = 0.0;
    public double flash = 0.0;
    public double _clock = 0.0;
    public string _chapter = "";
    public double _previous_time = -1.0;
    public MultiMeshInstance3D _rain_mesh;
    public DirectionalLight3D _light;
    public MeshInstance3D _bolt;
    public StandardMaterial3D _bolt_material;
    public RainImpacts _impacts;
    public AudioStreamPlayer _rain_audio;
    public Godot.Collections.Array<AudioStreamPlayer> _thunder_voices = new Godot.Collections.Array<AudioStreamPlayer>();
    public long _thunder_voice = 0;
    public double _rain_gain = 0.0;
    public double _rain_shelter_mix = 0.0;
    public AudioEffectLowPassFilter _rain_filter;
    public Godot.Collections.Array<Vector2> _thunder_due = new Godot.Collections.Array<Vector2>();
    public double _thunder_clock = 0.0;
    public bool _roofs_ready = false;

    // Cue position, intensity and thunder travel time describe the same event.
    // Cloud-only pulses alternate with visible forks; quiet gaps remain between
    // brief multi-stroke flashes. Distances are deliberately beyond the forest.
    public static readonly Godot.Collections.Array STORM_STRIKES = new Godot.Collections.Array { new Godot.Collections.Dictionary { { (StringName)"time", 9.0 }, { (StringName)"delay", 2.8 }, { (StringName)"power", 0.55 }, { (StringName)"at", new Vector3(-900, 0, -160) }, { (StringName)"fork", true } }, new Godot.Collections.Dictionary { { (StringName)"time", 15.0 }, { (StringName)"delay", 1.35 }, { (StringName)"power", 0.72 }, { (StringName)"at", new Vector3(-470, 0, 40) }, { (StringName)"fork", false } }, new Godot.Collections.Dictionary { { (StringName)"time", 21.0 }, { (StringName)"delay", 2.0 }, { (StringName)"power", 0.62 }, { (StringName)"at", new Vector3(-680, 0, -150) }, { (StringName)"fork", true } }, new Godot.Collections.Dictionary { { (StringName)"time", 28.0 }, { (StringName)"delay", 0.85 }, { (StringName)"power", 0.82 }, { (StringName)"at", new Vector3(-300, 0, -70) }, { (StringName)"fork", false } }, new Godot.Collections.Dictionary { { (StringName)"time", 33.0 }, { (StringName)"delay", 0.65 }, { (StringName)"power", 1.0 }, { (StringName)"at", new Vector3(-240, 0, -50) }, { (StringName)"fork", true } }, new Godot.Collections.Dictionary { { (StringName)"time", 37.0 }, { (StringName)"delay", 1.65 }, { (StringName)"power", 0.68 }, { (StringName)"at", new Vector3(-570, 0, 65) }, { (StringName)"fork", false } } };
    public static readonly Godot.Collections.Array SHELTER_STRIKES = new Godot.Collections.Array { new Godot.Collections.Dictionary { { (StringName)"time", 5.0 }, { (StringName)"delay", 1.2 }, { (StringName)"power", 0.62 }, { (StringName)"at", new Vector3(-415, 0, -90) }, { (StringName)"fork", false } }, new Godot.Collections.Dictionary { { (StringName)"time", 14.0 }, { (StringName)"delay", 2.0 }, { (StringName)"power", 0.48 }, { (StringName)"at", new Vector3(-700, 0, 120) }, { (StringName)"fork", false } }, new Godot.Collections.Dictionary { { (StringName)"time", 24.0 }, { (StringName)"delay", 0.9 }, { (StringName)"power", 0.78 }, { (StringName)"at", new Vector3(-310, 0, -50) }, { (StringName)"fork", false } } };
    public static readonly Godot.Collections.Array SHORT_STORM_STRIKES = new Godot.Collections.Array { new Godot.Collections.Dictionary { { (StringName)"time", 4.5 }, { (StringName)"delay", 2.8 }, { (StringName)"power", 0.55 }, { (StringName)"at", new Vector3(-900, 0, -160) }, { (StringName)"fork", true } }, new Godot.Collections.Dictionary { { (StringName)"time", 8.0 }, { (StringName)"delay", 1.35 }, { (StringName)"power", 0.72 }, { (StringName)"at", new Vector3(-470, 0, 40) }, { (StringName)"fork", false } }, new Godot.Collections.Dictionary { { (StringName)"time", 12.0 }, { (StringName)"delay", 2.0 }, { (StringName)"power", 0.62 }, { (StringName)"at", new Vector3(-680, 0, -150) }, { (StringName)"fork", true } }, new Godot.Collections.Dictionary { { (StringName)"time", 16.5 }, { (StringName)"delay", 0.65 }, { (StringName)"power", 1.0 }, { (StringName)"at", new Vector3(-240, 0, -50) }, { (StringName)"fork", true } } };

    public static Godot.Collections.Array strikes(string chapter)
    {
        if (chapter == "storm")
        {
            return STORM_STRIKES;
        }
        if (chapter == "storm_short")
        {
            return SHORT_STORM_STRIKES;
        }
        if (chapter == "shelter_rain")
        {
            return SHELTER_STRIKES;
        }
        return new Godot.Collections.Array();
    }

    public static double chapter_flash(string chapter, double seconds)
    {
        double total = 0.0;
        foreach (Variant cue in strikes(chapter))
        {
            total = G.op("+", total, G.op("*", strike_flash(G.op("-", seconds, G.Index(cue, "time")).AsDouble()), G.Index(cue, "power"))).AsDouble();
        }
        return total;
    }

    public double cloud_coverage(double daylight)
    {
        // The star chapter must actually clear, regardless of the advected noise
        // phase reached after several hours of accelerated cinematic time.
        double fair_cover = _chapter == "clearing" ? 0.0 : lerpf(0.18, 0.36, daylight);
        return lerpf(fair_cover, 0.48, cloud);
    }

    public override void _Ready()
    {
        Name = "Weather";
        ProcessPriority = -5;
        _build_rain();
        _build_lightning();
        apply_chapter("", 0, 1, false);
    }

    public static double strike_flash(double age)
    {
        if (age < 0.0 || age > 0.48)
        {
            return 0.0;
        }
        return exp(-age * 28.0) + 0.5 * exp(-pow((age - 0.105) / 0.025, 2.0));
    }

    public static Vector4 conditions(string chapter, double seconds, double duration)
    {
        double t = clampf(seconds / maxf(duration, 0.001), 0.0, 1.0);
        switch (chapter)
        {
            case "gathering":
                return new Vector4((float)(smoothstep(0.0, 1.0, t) * 0.65), (float)(smoothstep(0.75, 1.0, t) * 0.08), (float)(t * 0.05), 0.0f);
            case "storm":
                return new Vector4((float)lerpf(0.65, 1.0, smoothstep(0.0, 0.4, t)), (float)smoothstep(8.0, 19.0, seconds), (float)smoothstep(8.0, 28.0, seconds), (float)chapter_flash(chapter, seconds));
            case "storm_short":
                return new Vector4((float)lerpf(0.65, 1.0, smoothstep(0.0, 0.4, t)), (float)smoothstep(3.0, 10.0, seconds), (float)smoothstep(3.0, 17.0, seconds), (float)chapter_flash(chapter, seconds));
            case "shelter_rain":
                return new Vector4(1.0f, 1.0f, 1.0f, (float)chapter_flash(chapter, seconds));
            case "rain_detail":
                return new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
            case "clearing":
                return new Vector4((float)(1.0 - smoothstep(0.0, 6.0, seconds)), (float)(1.0 - smoothstep(0.0, 4.0, seconds)), 1.0f, 0.0f);
            case "dawn":
                return new Vector4(0.0f, 0.0f, (float)lerpf(1.0, 0.35, t), 0.0f);
            case "morning":
                return new Vector4(0.0f, 0.0f, (float)lerpf(0.35, 0.15, t), 0.0f);
        }
        return Vector4.Zero;
    }

    public void apply_chapter(string chapter, double seconds, double duration, bool sound = true)
    {
        if (chapter != _chapter)
        {
            _chapter = chapter;
            _previous_time = -1.0;
        }
        Vector4 state = conditions(chapter, seconds, duration);
        cloud = state.X;
        rain = state.Y;
        wet = state.Z;
        flash = state.W;
        Godot.Collections.Array cues = strikes(chapter);
        if (sound)
        {
            foreach (Variant cue in cues)
            {
                if (G.op("<", G.op("+", G.Index(cue, "time"), G.Index(cue, "delay")), duration - 0.7).AsBool() && G.op("<", _previous_time, G.Index(cue, "time")).AsBool() && G.op(">=", seconds, G.Index(cue, "time")).AsBool())
                {
                    _thunder_due.Add(new Vector2(G.op("+", _thunder_clock, G.Index(cue, "delay")).AsSingle(), G.Index(cue, "power").AsSingle()));
                }
            }
        }
        _previous_time = seconds;
        RenderingServer.GlobalShaderParameterSet("rainfall", rain);
        RenderingServer.GlobalShaderParameterSet("scene_wetness", wet);
        RenderingServer.GlobalShaderParameterSet("lightning_flash", flash);
        if (_rain_mesh != null)
        {
            _rain_mesh.Visible = rain > 0.001;
            ((ShaderMaterial)_rain_mesh.MaterialOverride).SetShaderParameter("rain_strength", rain);
        }
        if (_light != null)
        {
            _light.Visible = flash > 0.005;
            _light.LightEnergy = (float)(flash * 7.0);
            _bolt.Visible = false;
            foreach (Variant cue2 in cues)
            {
                if (strike_flash(G.op("-", seconds, G.Index(cue2, "time")).AsDouble()) <= 0.01)
                {
                    continue;
                }
                // Only the local bolt geometry scales. Its ground position never
                // jumps because a width/intensity adjustment changed world coordinates.
                _bolt.Position = G.Index(cue2, "at").AsVector3();
                Vector3 _t1 = _bolt.Rotation;
                _t1.Y = (float)(G.to_float(G.Index(cue2, "time")) * 0.37);
                _bolt.Rotation = _t1;
                _bolt.Scale = Vector3.One * (float)lerpf(0.70, 1.0, G.Index(cue2, "power").AsDouble());
                _bolt.Visible = G.truthy(G.Index(cue2, "fork")) && flash > 0.08;
                _light.Rotation = new Vector3((float)deg_to_rad(-48), (float)atan2(G.Index(G.Index(cue2, "at"), "x").AsDouble(), G.Index(G.Index(cue2, "at"), "z").AsDouble()), 0);
                RenderingServer.GlobalShaderParameterSet("lightning_position", _bolt.ToGlobal(new Vector3(0, 270, 0)));
            }
            _bolt_material.EmissionEnergyMultiplier = (float)(flash * 28.0);
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _thunder_clock += delta;
        Camera3D camera = GetViewport().GetCamera3D();
        if (Game.Instance.camp != null && Game.Instance.camp.campsite != null && !_roofs_ready)
        {
            Campsite site = Game.Instance.camp.campsite;
            if (site.tent != null && site.dock != null)
            {
                ShaderMaterial mat = _rain_mesh.MaterialOverride as ShaderMaterial;
                mat.SetShaderParameter("tent_inverse", site.tent.GlobalTransform.AffineInverse());
                mat.SetShaderParameter("dock_inverse", site.dock.GlobalTransform.AffineInverse());
                mat.SetShaderParameter("dock_length", site.dock.total_length());
                _impacts = new RainImpacts();
                AddChild(_impacts);
                _impacts.build(Game.Instance.camp);
                _roofs_ready = true;
            }
        }
        if (_impacts != null)
        {
            _impacts.update(_clock, rain);
        }
        if (camera != null && _rain_mesh.Visible)
        {
            _rain_mesh.GlobalPosition = new Vector3(camera.GlobalPosition.X, 0, camera.GlobalPosition.Z);
            ((ShaderMaterial)_rain_mesh.MaterialOverride).SetShaderParameter("rain_clock", _clock);
        }
        if (Game.Instance.audio != null && Game.Instance.audio.is_ready())
        {
            if (_rain_audio == null)
            {
                _build_audio();
            }
            _rain_gain = move_toward(_rain_gain, rain, delta * 0.5);
            bool sheltered = _chapter == "shelter_rain";
            if (camera != null && Game.Instance.camp != null && Game.Instance.camp.campsite != null)
            {
                Tent tent = Game.Instance.camp.campsite.tent;
                if (tent != null)
                {
                    sheltered = tent_shelter(tent.ToLocal(camera.GlobalPosition));
                }
            }
            _rain_shelter_mix = move_toward(_rain_shelter_mix, sheltered ? 1.0 : 0.0, delta * 1.4);
            _rain_audio.VolumeDb = (float)(linear_to_db(maxf(0.0001, _rain_gain)) + lerpf(-8.0, -5.0, _rain_shelter_mix));
            _rain_filter.CutoffHz = (float)move_toward(_rain_filter.CutoffHz, sheltered ? 2800.0 : 9000.0, delta * 9000.0);
            for (long i = (long)_thunder_due.Count - 1; i > -1; i += -1)
            {
                if (_thunder_clock >= _thunder_due[(int)i].X)
                {
                    AudioStreamPlayer voice = _thunder_voices[(int)(_thunder_voice % (long)_thunder_voices.Count)];
                    _thunder_voice += 1;
                    voice.VolumeDb = (float)((_chapter == "shelter_rain" ? -12.0 : -8.0) + linear_to_db(maxf(_thunder_due[(int)i].Y, 0.3)));
                    voice.PitchScale = (float)(0.92 + (double)(_thunder_voice % 3) * 0.06);
                    voice.Play();
                    if (Game.Instance.has_flag("film-quality") || Game.Instance.has_flag("cinematic"))
                    {
                        G.print("THUNDER_CUE chapter=", _chapter, " frames_drawn=", Engine.GetFramesDrawn());
                    }
                    _thunder_due.RemoveAt((int)i);
                }
            }
        }
    }

    public void _build_rain()
    {
        _rain_mesh = new MultiMeshInstance3D();
        _rain_mesh.Name = "Rain";
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        QuadMesh quad = new QuadMesh();
        quad.Size = new Vector2(0.011f, 0.30f);
        mm.Mesh = quad;
        mm.InstanceCount = 24000;
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(63009));
        for (long i = 0, i_end = mm.InstanceCount; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, new Transform3D(Basis.Identity, new Vector3(rng.RandfRange(-26, 26), 0, rng.RandfRange(-26, 26))));
            mm.SetInstanceCustomData((int)i, new Color(rng.Randf(), rng.Randf(), rng.Randf(), rng.Randf()));
        }
        _rain_mesh.Multimesh = mm;
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/rain.gdshader");
        TerrainField field = new TerrainField();
        Image heights = Image.CreateEmpty(512, 512, false, Image.Format.Rf);
        for (long y = 0; y < 512; y++)
        {
            for (long x = 0; x < 512; x++)
            {
                Vector2 pos = new Vector2(x, y) / 511.0f * 256.0f - Vector2.One * 128.0f;
                heights.SetPixel((int)x, (int)y, new Color((float)field.height(pos.X, pos.Y), 0, 0));
            }
        }
        mat.SetShaderParameter("ground_height", ImageTexture.CreateFromImage(heights));
        _rain_mesh.MaterialOverride = mat;
        _rain_mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _rain_mesh.CustomAabb = new Aabb(new Vector3(-40, -4, -40), new Vector3(80, 24, 80));
        _rain_mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        AddChild(_rain_mesh);
    }

    public void _build_lightning()
    {
        _light = new DirectionalLight3D();
        _light.Name = "LightningLight";
        // Cool blue-white: a whiter flash pushed the wet foliage to lime green.
        _light.LightColor = new Color(0.66f, 0.78f, 1.0f);
        // Godot temporal volumetrics ghost brief lights. Clouds get their own
        // synchronous emission; this light supplies real surface lighting/shadows.
        _light.LightVolumetricFogEnergy = 0.0f;
        // SDFGI also converges across frames and feeds the fog through GI Inject.
        // Keep this millisecond pulse out of that delayed indirect-light history.
        _light.LightBakeMode = Light3D.BakeMode.Disabled;
        _light.LightIndirectEnergy = 0.0f;
        _light.ShadowEnabled = true;
        _light.DirectionalShadowMaxDistance = 150.0f;
        _light.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        _light.Rotation = new Vector3((float)deg_to_rad(-48), (float)deg_to_rad(72), 0);
        AddChild(_light);
        _bolt = new MeshInstance3D();
        _bolt.Name = "LightningFork";
        MeshBuilder mb = new MeshBuilder();
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(9451));
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        for (long i = 0; i < 25; i++)
        {
            double t = (double)i / 24.0;
            points.Add(new Vector3((float)(t * 20.0 + rng.RandfRange(-7, 7)), (float)(270 * (1.0 - t)), rng.RandfRange(-7, 7)));
            radii.Add(0.42);
        }
        mb.add_tube(points, radii, 4);
        foreach (Variant fork in new Godot.Collections.Array { 7, 13, 17 })
        {
            Godot.Collections.Array<Vector3> branch = new Godot.Collections.Array<Vector3> { points[fork.AsInt32()] };
            Godot.Collections.Array<double> widths = new Godot.Collections.Array<double> { 0.13 };
            for (long i2 = 1; i2 < 7; i2++)
            {
                branch.Add(points[fork.AsInt32()] + new Vector3((float)(i2 * -3.5 + rng.RandfRange(-2, 2)), (float)(i2 * -3.7), (float)(i2 * 1.1 + rng.RandfRange(-2, 2))));
                widths.Add(lerpf(0.13, 0.025, i2 / 6.0));
            }
            mb.add_tube(branch, widths, 4);
        }
        _bolt.Mesh = mb.commit();
        _bolt_material = new StandardMaterial3D();
        _bolt_material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _bolt_material.AlbedoColor = new Color(0.75f, 0.83f, 1.0f);
        _bolt_material.EmissionEnabled = true;
        _bolt_material.Emission = new Color(0.60f, 0.72f, 1.0f);
        _bolt.MaterialOverride = _bolt_material;
        _bolt.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_bolt);
    }

    public void _build_audio()
    {
        long bus = AudioServer.GetBusIndex("CampRain");
        if (bus < 0)
        {
            bus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName((int)bus, "CampRain");
            AudioServer.SetBusSend((int)bus, AudioDirector.BUS_AMBIENCE);
        }
        _rain_filter = new AudioEffectLowPassFilter();
        _rain_filter.CutoffHz = 9000.0f;
        AudioServer.AddBusEffect((int)bus, _rain_filter);
        _rain_audio = new AudioStreamPlayer();
        _rain_audio.Name = "RainSound";
        AudioStreamOggVorbis rain_stream = _preload_rain.Duplicate() as AudioStreamOggVorbis;
        rain_stream.Loop = true;
        _rain_audio.Stream = rain_stream;
        _rain_audio.Bus = "CampRain";
        _rain_audio.VolumeDb = -80;
        AddChild(_rain_audio);
        _rain_audio.Play();
        for (long i = 0; i < 4; i++)
        {
            AudioStreamPlayer voice = new AudioStreamPlayer();
            voice.Name = G.format("ThunderSound%d", i);
            voice.Stream = _preload_thunder;
            voice.Bus = AudioDirector.BUS_SFX;
            AddChild(voice);
            _thunder_voices.Add(voice);
        }
    }

    public static bool tent_shelter(Vector3 local)
    {
        /// Same A-frame roof used by rain clipping, now also used by the live listener.
        /// Shelter no longer depends on a cinematic chapter name during exploration.
        if (absf(local.X) >= Tent.WIDTH * 0.5 || absf(local.Z) >= Tent.LENGTH * 0.5 || local.Y < -0.15)
        {
            return false;
        }
        return local.Y < Tent.HEIGHT * (1.0 - absf(local.X) / (Tent.WIDTH * 0.5));
    }
    private static AudioStreamOggVorbis _preload_rain => _preload_rain_cache ??= Content.Load<AudioStreamOggVorbis>("res://audio/rain.ogg");
    private static AudioStreamOggVorbis _preload_rain_cache;
    private static AudioStreamWav _preload_thunder => _preload_thunder_cache ??= Content.Load<AudioStreamWav>("res://audio/thunder.wav");
    private static AudioStreamWav _preload_thunder_cache;
}
