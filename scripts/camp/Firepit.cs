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

/// The camp's heart: a soot-blackened stone ring around a bed of glowing
/// coals, charred logs leaning into eroding flame cards, ember streaks, lit
/// smoke and heat shimmer, all under a lashed tripod with a hanging pot. A
/// shadowed omni and a wide fill light carry the firelight into the clearing.
/// Intensity decays slowly; feeding a log brings it back.
public partial class Firepit : Node3D
{
    [Signal]
    public delegate void fedEventHandler();
    [Signal]
    public delegate void intensity_changedEventHandler(double value);

    public const double DECAY_PER_SECOND = 0.0018;
    public const double FEED_AMOUNT = 0.45;
    public const double MIN_INTENSITY = 0.0;
    public const double MAX_INTENSITY = 2.0;
    public static readonly Color LIGHT_COLOR = new Color(1.0f, 0.53f, 0.2f);
    public const double BASE_ENERGY = 1.5;
    public const double BASE_RANGE = 12.0;
    public const double FILL_ENERGY = 0.20;
    public const double FILL_RANGE = 22.0;
    public const double RING_RADIUS = 0.9;
    public const double TRIPOD_FOOT_RADIUS = 1.48;
    public const double BED_RADIUS = 0.5;
    public const double FLAME_HEIGHT = 0.12;

    public double intensity = 1.0;
    public TerrainField field;
    public OmniLight3D light;
    public OmniLight3D fill_light;
    public MeshInstance3D flame_volume;
    public GpuParticles3D sparks;
    public GpuParticles3D smoke;
    public MeshInstance3D haze;
    public FogVolume fog;
    public Interactable body;

    public double _time = 0.0;
    public ParticleProcessMaterial _smoke_process;
    public ShaderMaterial _flame_mat;
    public NoiseTexture3D _flame_noise;
    public bool _noise_reported = false;
    public long _base_sparks = 44;
    public long _base_smoke = 36;

    public Firepit(TerrainField p_field)
    {
        field = p_field;
        Name = "Firepit";
    }

    public Firepit()
    {
    }

    public void build()
    {
        double y = field.height(TerrainField.FIRE.X, TerrainField.FIRE.Y);
        Position = new Vector3(TerrainField.FIRE.X, (float)y, TerrainField.FIRE.Y);
        _build_ring();
        _build_bed();
        _build_logs();
        _build_tripod();
        _build_particles();
        _build_haze();
        _build_lights();
        _build_fog();
        _build_collision();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Firepit.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
        _apply_intensity();
    }

    public string prompt()
    {
        if (Game.Instance.player != null && Game.Instance.player.held_item == "log")
        {
            return "Add a log";
        }
        if (intensity < 0.2)
        {
            return "The fire is dying";
        }
        return "Warm your hands";
    }

    public void interact(Player player)
    {
        if (player != null && player.held_item == "log")
        {
            player.held_item = "";
            feed();
            if (Game.Instance.audio != null)
            {
                Game.Instance.audio.play_interact("log_added");
            }
            return;
        }
        if (Game.Instance.audio != null)
        {
            Game.Instance.audio.play_interact("ui");
        }
    }

    public void feed()
    {
        intensity = minf(intensity + FEED_AMOUNT, MAX_INTENSITY);
        EmitSignal(SignalName.intensity_changed, intensity);
        EmitSignal(SignalName.fed);
        _apply_intensity();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_flame_noise != null && !_noise_reported && _time > 3.0)
        {
            _noise_reported = true;
            Godot.Collections.Array layers = (Godot.Collections.Array)_flame_noise.GetData();
            if ((layers.Count == 0))
            {
                G.print("FLAME_NOISE no data yet");
            }
            else
            {
                Image mid = layers[(int)((long)layers.Count / 2)].As<Image>();
                G.print(G.format("FLAME_NOISE layers=%d size=%s format=%d samples=%s %s %s intensity=%.2f height=%s density=%s", new Godot.Collections.Array { (long)layers.Count, mid.GetSize(), (long)mid.GetFormat(), mid.GetPixel(8, 8), mid.GetPixel(32, 32), mid.GetPixel(50, 20), intensity, _flame_mat.GetShaderParameter("height"), _flame_mat.GetShaderParameter("density") }));
            }
        }
        if (Game.Instance.has_flag("fire-debug") && _flame_mat != null)
        {
            _flame_mat.SetShaderParameter("debug_mode", G.to_int(Game.Instance.arg_value("fire-debug", "1")));
        }
        if (intensity > MIN_INTENSITY)
        {
            intensity = maxf(intensity - DECAY_PER_SECOND * delta, MIN_INTENSITY);
        }
        _apply_intensity();
        if (light == null)
        {
            return;
        }
        double flicker = 1.0;
        flicker += 0.09 * sin(_time * 4.7);
        flicker += 0.06 * sin(_time * 9.1 + 1.3);
        flicker += 0.035 * sin(_time * 17.0 + 0.6);
        double live = smoothstep(0.05, 0.35, intensity);
        double strength = clampf(intensity, 0.0, MAX_INTENSITY) * (0.55 + 0.45 * live);
        light.LightEnergy = (float)(BASE_ENERGY * strength * flicker);
        light.OmniRange = (float)(BASE_RANGE * (0.7 + 0.3 * clampf(intensity, 0.0, 1.0)));
        light.Position = new Vector3((float)(0.04 * sin(_time * 3.2)), (float)(0.5 + 0.03 * sin(_time * 5.1)), (float)(0.03 * cos(_time * 2.7)));
        fill_light.LightEnergy = (float)(FILL_ENERGY * strength * (0.9 + 0.1 * flicker));
        if (body != null)
        {
            body.prompt_text = prompt();
        }
        if (_smoke_process != null && Game.Instance.world != null)
        {
            Vector2 wind = WorldController.WIND_DIRECTION;
            double gust = Game.Instance.world.wind_strength();
            Vector2 drift = wind.Normalized() * (float)(0.12 + 0.25 * gust);
            _smoke_process.Gravity = new Vector3(drift.X, 0.3f, drift.Y);
        }
    }

    public void _apply_intensity()
    {
        double live = smoothstep(0.04, 0.45, intensity);
        double roar = clampf(intensity, 0.0, MAX_INTENSITY);
        if (flame_volume != null)
        {
            flame_volume.Visible = live > 0.02;
            _flame_mat.SetShaderParameter("flare", roar);
            // Even a freshly fed fire keeps its upper envelope below the pot.
            _flame_mat.SetShaderParameter("height", flame_height(roar));
            _flame_mat.SetShaderParameter("density", 2.0 + 1.0 * live);
        }
        if (sparks != null)
        {
            sparks.AmountRatio = (float)clampf(0.2 + roar * 0.45, 0.0, 1.0);
            sparks.Emitting = intensity > 0.04;
        }
        if (smoke != null)
        {
            smoke.AmountRatio = (float)clampf(0.3 + (1.2 - live) * 0.35 + roar * 0.2, 0.15, 1.0);
            ((ShaderMaterial)smoke.MaterialOverride).SetShaderParameter("density", lerpf(0.46, 0.30, live));
        }
        if (haze != null)
        {
            haze.Visible = live > 0.05;
        }
        if (fog != null && fog.Material is FogMaterial)
        {
            (fog.Material as FogMaterial).Density = (float)(0.025 * live * roar);
            (fog.Material as FogMaterial).Emission = LIGHT_COLOR * (float)(0.01 * live * roar);
        }
        if (light != null)
        {
            light.Visible = intensity > 0.02;
            fill_light.Visible = intensity > 0.02;
        }
        RenderingServer.GlobalShaderParameterSet("fire_intensity", intensity);
        if (IsInsideTree())
        {
            RenderingServer.GlobalShaderParameterSet("fire_position", GlobalPosition + new Vector3(0.0f, 0.4f, 0.0f));
        }
    }

    public static double flame_height(double value)
    {
        return 0.46 + 0.16 * clampf(value, 0.0, MAX_INTENSITY);
    }

    public void apply_quality(QualityPreset p)
    {
        if (light != null)
        {
            light.ShadowEnabled = p.fire_shadows;
            light.ShadowCasterMask = unchecked((uint)(p.fire_shadow_casters));
        }
        if (_flame_mat != null)
        {
            _flame_mat.SetShaderParameter("steps", (long)round(lerpf(16.0, 48.0, clampf(p.particle_scale, 0.0, 1.0))));
        }
        if (sparks != null)
        {
            sparks.Amount = (int)maxi((long)round(_base_sparks * p.particle_scale), 12);
        }
        if (smoke != null)
        {
            smoke.Amount = (int)maxi((long)round(_base_smoke * p.particle_scale), 8);
        }
    }

    public void _build_ring()
    {
        // ---------------------------------------------------------------- geometry
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(404));
        ShaderMaterial rock_mat = PropMaterials.triplanar("rock", new Color(0.66f, 0.64f, 0.6f), 0.75, 0.2);
        rock_mat.SetShaderParameter("char_amount", 0.9);
        rock_mat.SetShaderParameter("char_radius", 1.05);
        long count = 15;
        for (long i = 0; i < count; i++)
        {
            double a = (double)i / (double)count * TAU + rng.RandfRange(-0.08f, 0.08f);
            double r = RING_RADIUS + rng.RandfRange(-0.06f, 0.06f);
            double s = rng.RandfRange(0.19f, 0.31f);
            MeshInstance3D mesh = new MeshInstance3D();
            mesh.Name = G.format("RingStone_%02d", i);
            mesh.Mesh = PropMeshes.rock(900 + i * 13, 1.0);
            mesh.MaterialOverride = rock_mat;
            mesh.Position = new Vector3((float)(cos(a) * r), 0.0f, (float)(sin(a) * r));
            mesh.Scale = new Vector3((float)s, (float)(s * rng.RandfRange(0.6f, 0.85f)), (float)(s * rng.RandfRange(0.85f, 1.15f)));
            mesh.Rotation = new Vector3(rng.RandfRange(-0.25f, 0.25f), (float)(rng.Randf() * TAU), rng.RandfRange(-0.25f, 0.25f));
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            mesh.GIMode = GeometryInstance3D.GIModeEnum.Static;
            AddChild(mesh);
            // Measure the rotated stone against the terrain across its footprint.
            // Bury 28–36% of its actual height, so the irregular base seats into
            // earth without leaving a dark gap or a rim of barely touching points.
            double low = INF;
            double high = -INF;
            List<Vector3> vertices = G.ListFromVariant<Vector3>(mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]);
            foreach (Vector3 vertex in vertices)
            {
                Vector3 p = mesh.ToGlobal(vertex);
                double clearance = p.Y - field.height(p.X, p.Z);
                low = minf(low, clearance);
                high = maxf(high, clearance);
            }
            Vector3 _t1 = mesh.Position;
            _t1.Y = (float)(mesh.Position.Y - lerpf(low, high, rng.RandfRange(0.28f, 0.36f)));
            mesh.Position = _t1;
        }
    }

    public void _build_bed()
    {
        MeshBuilder mb = new MeshBuilder();
        // Enough segments that the bed's rim is a curve, and the whole bed sits
        // two centimetres into the pit floor so no slab edge shows past the logs.
        mb.add_displaced_sphere(16, 28, BED_RADIUS + 0.12, Callable.From((Vector3 dir) =>
{
    double rim = 1.0 - maxf(-dir.Y, 0.0) * 0.5;
    return rim * (0.90 + 0.10 * sin(dir.X * 9.0) * cos(dir.Z * 7.0) + 0.04 * sin(dir.X * 23.0 + dir.Z * 17.0));
}));
        MeshInstance3D bed = new MeshInstance3D();
        bed.Name = "Coals";
        bed.Mesh = mb.commit();
        bed.Scale = new Vector3(1.0f, 0.16f, 1.0f);
        Vector3 _t1 = bed.Position;
        _t1.Y = -0.02f;
        bed.Position = _t1;
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/coals.gdshader");
        Camp.bind_texture(mat, "noise_tex", PropMaterials.NOISE);
        mat.SetShaderParameter("bed_radius", BED_RADIUS);
        bed.MaterialOverride = mat;
        bed.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(bed);
    }

    public void _build_logs()
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(505));
        ShaderMaterial charred = PropMaterials.triplanar("wood", new Color(0.55f, 0.42f, 0.3f), 1.1, 0.0);
        charred.SetShaderParameter("char_amount", 1.0);
        charred.SetShaderParameter("char_radius", 0.62);
        charred.SetShaderParameter("ember_glow", 0.7);
        charred.SetShaderParameter("fire_warmth", 0.2);
        // Four logs leaning into the centre, two lying across the coals.
        for (long i = 0; i < 4; i++)
        {
            double a = (double)i / 4.0 * TAU + 0.4 + rng.RandfRange(-0.2f, 0.2f);
            double length = rng.RandfRange(0.62f, 0.82f);
            MeshInstance3D mesh = new MeshInstance3D();
            mesh.Mesh = PropMeshes.log_mesh(length, rng.RandfRange(0.05f, 0.072f), 600 + i, 0.02);
            mesh.MaterialOverride = charred;
            Vector3 foot = new Vector3((float)(cos(a) * 0.46), 0.05f, (float)(sin(a) * 0.46));
            Vector3 head = new Vector3((float)(cos(a) * 0.08), (float)(0.28 + (double)i * 0.03), (float)(sin(a) * 0.08));
            Vector3 axis = (head - foot).Normalized();
            mesh.Position = (foot + head) * 0.5f;
            mesh.Basis = Basis.LookingAt(axis, Vector3.Up).Rotated(axis, (float)(rng.Randf() * TAU)) * new Basis(Vector3.Up, (float)(PI * 0.5));
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            mesh.GIMode = GeometryInstance3D.GIModeEnum.Static;
            AddChild(mesh);
        }
        for (long i2 = 0; i2 < 2; i2++)
        {
            MeshInstance3D mesh2 = new MeshInstance3D();
            mesh2.Mesh = PropMeshes.log_mesh(rng.RandfRange(0.5f, 0.62f), rng.RandfRange(0.045f, 0.06f), 640 + i2, 0.03);
            mesh2.MaterialOverride = charred;
            mesh2.Position = new Vector3(rng.RandfRange(-0.08f, 0.08f), 0.07f, rng.RandfRange(-0.08f, 0.08f));
            mesh2.Rotation = new Vector3(0.0f, (float)(rng.Randf() * TAU), rng.RandfRange(-0.1f, 0.1f));
            mesh2.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            AddChild(mesh2);
        }
    }

    public void _build_tripod()
    {
        MeshBuilder mb = new MeshBuilder();
        Vector3 apex = new Vector3(0.0f, 1.72f, 0.0f);
        for (long i = 0; i < 3; i++)
        {
            Godot.Collections.Array<Vector3> points = tripod_leg(i);
            Vector3 foot = points[0];
            Vector3 top = points[1];
            PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { foot, foot.Lerp(top, 0.5f), top }, new Godot.Collections.Array<double> { 0.03, 0.028, 0.024 }, 7, i + 1, new Color(0.75f, 0.68f, 0.58f), 1.6);
        }
        MeshInstance3D tripod = new MeshInstance3D();
        tripod.Name = "Tripod";
        tripod.Mesh = mb.commit(null, true);
        tripod.MaterialOverride = PropMaterials.wood(new Color(0.62f, 0.5f, 0.36f), 0.5, 0.0, 1.0);
        tripod.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(tripod);

        MeshBuilder rope = new MeshBuilder();
        PropMeshes.add_rope_coil(rope, apex - new Vector3(0.0f, 0.04f, 0.0f), 0.07, 5, 0.024);
        MeshInstance3D lash = new MeshInstance3D();
        lash.Mesh = rope.commit();
        lash.MaterialOverride = PropMaterials.rope();
        lash.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(lash);

        MeshBuilder iron = new MeshBuilder();
        Vector3 bail_top = new Vector3(0.0f, (float)(1.02 + 0.46), 0.0f);
        iron.add_tube(new Godot.Collections.Array<Vector3> { apex + new Vector3(0.0f, 0.02f, 0.0f), bail_top }, new Godot.Collections.Array<double> { 0.009, 0.009 }, 5, Colors.White, 1.0, 12.0, 0.0, true);
        PropMeshes.add_pot(iron, new Vector3(0.0f, 1.02f, 0.0f));
        MeshInstance3D pot = new MeshInstance3D();
        pot.Name = "Pot";
        pot.Mesh = iron.commit(null, true);
        ShaderMaterial pot_material = new ShaderMaterial();
        pot_material.Shader = Content.Load<Shader>("res://shaders/pot_iron.gdshader");
        pot.MaterialOverride = pot_material;
        pot.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(pot);
    }

    public void _build_particles()
    {
        Texture2D noise = Content.Load<Texture2D>(PropMaterials.NOISE);

        _build_flame_volume();

        sparks = _particles("Sparks", _base_sparks, 1.8, new Aabb(new Vector3(-1.6f, -0.2f, -1.6f), new Vector3(3.2f, 4.5f, 3.2f)));
        ParticleProcessMaterial spark_p = new ParticleProcessMaterial();
        spark_p.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        spark_p.EmissionSphereRadius = 0.14f;
        spark_p.Direction = new Vector3(0.0f, 1.0f, 0.0f);
        spark_p.Spread = 32.0f;
        spark_p.InitialVelocityMin = 1.1f;
        spark_p.InitialVelocityMax = 2.6f;
        spark_p.Gravity = new Vector3(0.0f, -0.3f, 0.0f);
        spark_p.DampingMin = 0.6f;
        spark_p.DampingMax = 1.4f;
        spark_p.ScaleMin = 0.5f;
        spark_p.ScaleMax = 1.2f;
        spark_p.ParticleFlagAlignY = true;
        spark_p.Color = new Color(1.0f, 0.6f, 0.2f);
        _lifetime_color(spark_p, new Godot.Collections.Array<Color> { new Color(1.0f, 0.85f, 0.5f, 1.0f), new Color(1.0f, 0.5f, 0.12f, 1.0f), new Color(0.85f, 0.2f, 0.02f, 0.6f), new Color(0.3f, 0.02f, 0.0f, 0.0f) });
        spark_p.TurbulenceEnabled = true;
        spark_p.TurbulenceNoiseStrength = 1.3f;
        spark_p.TurbulenceNoiseScale = 1.6f;
        spark_p.TurbulenceInfluenceMin = 0.03f;
        spark_p.TurbulenceInfluenceMax = 0.1f;
        sparks.ProcessMaterial = spark_p;
        sparks.DrawPass1 = _quad(new Vector2(0.03f, 0.13f), Vector3.Zero);
        sparks.MaterialOverride = _particle_material("res://shaders/spark.gdshader", null, new Godot.Collections.Dictionary { { "glow", 3.0 } });
        Vector3 _t1 = sparks.Position;
        _t1.Y = 0.3f;
        sparks.Position = _t1;
        AddChild(sparks);

        smoke = _particles("Smoke", _base_smoke, 6.5, new Aabb(new Vector3(-2.5f, 0.0f, -2.5f), new Vector3(5.0f, 8.0f, 5.0f)));
        ParticleProcessMaterial smoke_p = new ParticleProcessMaterial();
        // Born in a ring around the pot's footprint, just under its base, so the
        // column rises past the pot; the pot's collision sphere below deflects
        // what drifts into it instead of letting puffs pass through the iron.
        smoke_p.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        smoke_p.EmissionRingAxis = Vector3.Up;
        smoke_p.EmissionRingRadius = 0.22f;
        smoke_p.EmissionRingInnerRadius = 0.10f;
        smoke_p.EmissionRingHeight = 0.06f;
        smoke_p.Direction = new Vector3(0.0f, 1.0f, 0.0f);
        smoke_p.Spread = 11.0f;
        smoke_p.CollisionMode = ParticleProcessMaterial.CollisionModeEnum.Rigid;
        smoke_p.CollisionFriction = 0.55f;
        smoke_p.CollisionBounce = 0.0f;
        smoke_p.InitialVelocityMin = 0.5f;
        smoke_p.InitialVelocityMax = 0.95f;
        smoke_p.Gravity = new Vector3(0.0f, 0.3f, 0.0f);
        smoke_p.DampingMin = 0.12f;
        smoke_p.DampingMax = 0.3f;
        smoke_p.ScaleMin = 0.7f;
        smoke_p.ScaleMax = 1.2f;
        smoke_p.ScaleCurve = _curve(new Godot.Collections.Array { new Godot.Collections.Array { 0.0, 0.22 }, new Godot.Collections.Array { 0.3, 1.0 }, new Godot.Collections.Array { 1.0, 1.9 } });
        smoke_p.AngleMin = -180.0f;
        smoke_p.AngleMax = 180.0f;
        smoke_p.AngularVelocityMin = -18.0f;
        smoke_p.AngularVelocityMax = 18.0f;
        smoke_p.Color = new Color(0.7f, 0.68f, 0.66f, 1.0f);
        _lifetime_color(smoke_p, new Godot.Collections.Array<Color> { new Color(0.55f, 0.5f, 0.46f, 0.0f), new Color(0.62f, 0.6f, 0.58f, 0.7f), new Color(0.7f, 0.7f, 0.7f, 0.4f), new Color(0.75f, 0.75f, 0.75f, 0.0f) });
        smoke_p.TurbulenceEnabled = true;
        smoke_p.TurbulenceNoiseStrength = 0.7f;
        smoke_p.TurbulenceNoiseScale = 0.9f;
        smoke_p.TurbulenceInfluenceMin = 0.006f;
        smoke_p.TurbulenceInfluenceMax = 0.02f;
        smoke.ProcessMaterial = smoke_p;
        smoke.DrawPass1 = _quad(new Vector2(1.5f, 1.5f), Vector3.Zero);
        smoke.MaterialOverride = _particle_material("res://shaders/smoke.gdshader", noise, new Godot.Collections.Dictionary { { "density", 0.20 } });
        _smoke_process = smoke_p;
        // Smoke is born above the flame envelope: sprites starting inside it
        // whitened the fire into haze before the tongues could read.
        Vector3 _t2 = smoke.Position;
        _t2.Y = 0.92f;
        smoke.Position = _t2;
        AddChild(smoke);
        GpuParticlesCollisionSphere3D pot_shield = new GpuParticlesCollisionSphere3D();
        pot_shield.Name = "PotSmokeShield";
        pot_shield.Radius = 0.21f;
        pot_shield.Position = new Vector3(0.0f, (float)(1.02 + 0.15), 0.0f);
        AddChild(pot_shield);
    }

    public void _build_flame_volume()
    {
        /// The flames: a ray-marched volume over the ember bed (shaders/fire_volume).
        FastNoiseLite noise = new FastNoiseLite();
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        noise.Seed = 11;
        noise.Frequency = 0.045f;
        noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        noise.FractalOctaves = 3;
        noise.FractalLacunarity = 2.2f;
        noise.FractalGain = 0.55f;
        _flame_noise = new NoiseTexture3D();
        _flame_noise.Width = 64;
        _flame_noise.Height = 64;
        _flame_noise.Depth = 64;
        _flame_noise.Seamless = true;
        _flame_noise.Noise = noise;
        Vector3 half = new Vector3(0.75f, 0.6f, 0.75f);
        flame_volume = new MeshInstance3D();
        flame_volume.Name = "Flames";
        BoxMesh box = new BoxMesh();
        box.Size = half * 2.0f;
        flame_volume.Mesh = box;
        _flame_mat = new ShaderMaterial();
        _flame_mat.Shader = Content.Load<Shader>("res://shaders/fire_volume.gdshader");
        _flame_mat.SetShaderParameter("noise_tex", _flame_noise);
        _flame_mat.SetShaderParameter("box_half", half);
        _flame_mat.RenderPriority = -1;
        flame_volume.MaterialOverride = _flame_mat;
        flame_volume.Position = new Vector3(0.0f, (float)(FLAME_HEIGHT - 0.04 + half.Y), 0.0f);
        flame_volume.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        flame_volume.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        flame_volume.CustomAabb = new Aabb(-half - new Vector3(0.2f, 0.2f, 0.2f), half * 2.0f + new Vector3(0.4f, 0.4f, 0.4f));
        AddChild(flame_volume);
    }

    public void _build_haze()
    {
        haze = new MeshInstance3D();
        haze.Name = "Heat";
        haze.Mesh = _quad(new Vector2(1.1f, 1.5f), Vector3.Zero);
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/heat.gdshader");
        mat.SetShaderParameter("strength", 0.018);
        mat.RenderPriority = -2;
        haze.MaterialOverride = mat;
        haze.Position = new Vector3(0.0f, 1.15f, 0.0f);
        haze.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        haze.CustomAabb = new Aabb(new Vector3(-1.0f, -1.0f, -1.0f), new Vector3(2.0f, 2.0f, 2.0f));
        AddChild(haze);
    }

    public void _build_lights()
    {
        light = new OmniLight3D();
        light.Name = "FireLight";
        light.LightColor = LIGHT_COLOR;
        light.LightEnergy = (float)BASE_ENERGY;
        light.OmniRange = (float)BASE_RANGE;
        // A shallow falloff preserves warmth across the clearing without the
        // inverse-distance hotspot bleaching the log tips beside this source.
        light.OmniAttenuation = 0.8f;
        light.LightVolumetricFogEnergy = 0.18f;
        light.LightSize = 0.25f;
        light.LightSpecular = 0.7f;
        light.ShadowEnabled = true;
        light.ShadowBlur = 2.6f;
        light.ShadowBias = 0.05f;
        light.ShadowNormalBias = 1.5f;
        light.OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid;
        light.Position = new Vector3(0.0f, 0.5f, 0.0f);
        AddChild(light);
        // Shadowless fill that carries a faint warmth across the clearing.

        fill_light = new OmniLight3D();
        fill_light.Name = "FireFill";
        fill_light.LightColor = new Color(1.0f, 0.6f, 0.3f);
        fill_light.LightEnergy = (float)FILL_ENERGY;
        fill_light.OmniRange = (float)FILL_RANGE;
        fill_light.OmniAttenuation = 1.0f;
        // Bounced warmth originates at the coals. An elevated source illuminated
        // smoke particles as an orb inside the pot bail, even with fog energy zero.
        fill_light.LightVolumetricFogEnergy = 0.0f;
        fill_light.LightSpecular = 0.0f;
        fill_light.ShadowEnabled = false;
        fill_light.Position = new Vector3(0.0f, 0.22f, 0.0f);
        AddChild(fill_light);
    }

    public void _build_fog()
    {
        fog = new FogVolume();
        fog.Name = "FireHaze";
        fog.Layers = unchecked((uint)(Pond.AIR_FOG_LAYER));
        fog.Size = new Vector3(2.6f, 3.2f, 2.6f);
        fog.Shape = RenderingServer.FogVolumeShape.Ellipsoid;
        FogMaterial mat = new FogMaterial();
        mat.Density = 0.025f;
        mat.Albedo = new Color(1.0f, 0.6f, 0.3f);
        mat.Emission = LIGHT_COLOR * 0.01f;
        mat.EdgeFade = 0.6f;
        fog.Material = mat;
        fog.Position = new Vector3(0.0f, 1.3f, 0.0f);
        AddChild(fog);
    }

    public void _build_collision()
    {
        body = new Interactable();
        body.Name = "FireBody";
        body.CollisionLayer = unchecked((uint)(1 | 1L << 1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"rock");
        body.on_interact = new Callable(this, Firepit.MethodName.interact);
        body.prompt_text = prompt();
        CollisionShape3D pit = new CollisionShape3D();
        CylinderShape3D cyl = new CylinderShape3D();
        cyl.Radius = (float)(RING_RADIUS + 0.12);
        cyl.Height = 0.6f;
        pit.Shape = cyl;
        Vector3 _t1 = pit.Position;
        _t1.Y = 0.3f;
        pit.Position = _t1;
        body.AddChild(pit);
        for (long i = 0; i < 3; i++)
        {
            Godot.Collections.Array<Vector3> points = tripod_leg(i);
            Vector3 axis = points[1] - points[0];
            CollisionShape3D leg = new CollisionShape3D();
            leg.Name = G.format("TripodLeg_%d", i);
            CylinderShape3D post = new CylinderShape3D();
            post.Radius = 0.04f;
            post.Height = axis.Length();
            leg.Shape = post;
            leg.Position = (points[0] + points[1]) * 0.5f;
            leg.Quaternion = new Quaternion(Vector3.Up, axis.Normalized());
            body.AddChild(leg);
        }
        AddChild(body);
    }

    public static Godot.Collections.Array<Vector3> tripod_leg(long index)
    {
        // ---------------------------------------------------------------- helpers
        /// One source for visible timber, player collision and camera clearance.
        double angle = (double)index / 3.0 * TAU + 0.9;
        // Feet stand far enough out that the leaning shafts clear the stone crowns.
        return new Godot.Collections.Array<Vector3> { new Vector3((float)(cos(angle) * TRIPOD_FOOT_RADIUS), -0.05f, (float)(sin(angle) * TRIPOD_FOOT_RADIUS)), new Vector3((float)(cos(angle) * 0.05), 1.88f, (float)(sin(angle) * 0.05)) };
    }

    public GpuParticles3D _particles(string node_name, long amount, double lifetime, Aabb aabb)
    {
        GpuParticles3D p = new GpuParticles3D();
        p.Name = node_name;
        p.Amount = (int)amount;
        p.Lifetime = lifetime;
        p.Preprocess = lifetime * 0.8;
        p.Explosiveness = 0.0f;
        p.Randomness = 0.5f;
        p.VisibilityAabb = aabb;
        p.LocalCoords = false;
        p.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return p;
    }

    public static QuadMesh _quad(Vector2 size, Vector3 offset)
    {
        QuadMesh quad = new QuadMesh();
        quad.Size = size;
        quad.CenterOffset = offset;
        return quad;
    }

    public static CurveTexture _curve(Godot.Collections.Array points)
    {
        Curve curve = new Curve();
        foreach (Variant p_item in points)
        {
            Godot.Collections.Array p = p_item.AsGodotArray();
            curve.AddPoint(new Vector2(p[0].AsSingle(), p[1].AsSingle()));
        }
        CurveTexture tex = new CurveTexture();
        tex.Curve = curve;
        return tex;
    }

    public static void _lifetime_color(ParticleProcessMaterial process, Godot.Collections.Array<Color> stops)
    {
        Gradient ramp = new Gradient();
        List<float> offsets = new List<float>();
        List<Color> colors = new List<Color>();
        for (long i = 0, i_end = (long)stops.Count; i < i_end; i++)
        {
            offsets.Add((float)((double)i / (double)maxi((long)stops.Count - 1, 1)));
            colors.Add(stops[(int)i]);
        }
        ramp.Offsets = offsets.ToArray();
        ramp.Colors = colors.ToArray();
        GradientTexture1D tex = new GradientTexture1D();
        tex.Gradient = ramp;
        tex.Width = 64;
        process.ColorRamp = tex;
    }

    public static ShaderMaterial _particle_material(string shader_path, Texture2D noise, Godot.Collections.Dictionary @params)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>(shader_path);
        if (noise != null)
        {
            mat.SetShaderParameter("noise_tex", noise);
        }
        foreach (Variant key in @params.Keys)
        {
            mat.SetShaderParameter(key.AsStringName(), @params[key]);
        }
        return mat;
    }
}
