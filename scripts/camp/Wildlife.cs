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

/// A few birds and small insects occupy specific habitats. Film cues use the
/// shot clock, while ordinary play keeps the quieter autonomous visits.
public partial class Wildlife : Node3D
{
    public static readonly StringName BIRD_CUE = "pond_birds";
    public static readonly StringName MEADOW_CUE = "meadow_life";
    public static readonly StringName POND_CUE = "pond_life";
    public const double BIRD_TAKEOFF = 3.5;
    public const double BIRD_FLIGHT_SECONDS = 8.0;
    public const double COMPANION_START = 8.0;
    public const double COMPANION_END = 12.5;
    public static readonly Vector2 MEADOW_INSECTS = new Vector2(4.3f, 10.2f);
    public static readonly Vector2 POND_INSECTS = new Vector2(-28.5f, 13.3f);

    public TerrainField field;
    public GpuParticles3D fireflies;
    public GpuParticles3D motes;
    public bool _shelter_ready = false;
    public double _life_clock = 0.0;
    public double _fish_clock = 0.0;
    public double _bird_wind_phase = 0.0;
    public Godot.Collections.Array<double> _bird_heights = new Godot.Collections.Array<double>();
    public Godot.Collections.Array<Node3D> _birds = new Godot.Collections.Array<Node3D>();
    public Godot.Collections.Array<MeshInstance3D> _fish = new Godot.Collections.Array<MeshInstance3D>();
    public List<List<Vector3>> fish_routes = new List<List<Vector3>>();
    public MultiMeshInstance3D _gnats;
    public Godot.Collections.Array<Vector3> _gnat_centres = new Godot.Collections.Array<Vector3>();
    public Godot.Collections.Array<Node3D> _butterflies = new Godot.Collections.Array<Node3D>();
    public Godot.Collections.Array<Node3D> _dragonflies = new Godot.Collections.Array<Node3D>();
    public Vector3 _butterfly_anchor = Vector3.Zero;
    public Vector3 _dragonfly_anchor = Vector3.Zero;
    public Godot.Collections.Array<Vector3> _butterfly_habitats = new Godot.Collections.Array<Vector3>();
    public Godot.Collections.Array<Vector3> _dragonfly_habitats = new Godot.Collections.Array<Vector3>();
    public double _quality_life_scale = 1.0;
    public Vector3 _bird_perch = Vector3.Zero;
    public Basis _bird_perch_basis = Basis.Identity;
    public bool _perch_ready = false;
    public bool _film_controlled = false;
    public StringName _film_cue = "";
    public double _film_seconds = 0.0;

    public Wildlife(TerrainField p_field)
    {
        field = p_field;
        Name = "Wildlife";
    }

    public Wildlife()
    {
    }

    public void build()
    {
        _build_motes();
        _build_fireflies();
        _build_small_life();
        _prepare_bird_perch();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Wildlife.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
    }

    public void set_film_cue(StringName cue, double seconds)
    {
        /// Called each shot frame, including an empty cue for the quiet shots. This
        /// makes the same event visible in previews and movies regardless of loading
        /// time. The caller owns camera composition; wildlife stays in world space.
        _film_controlled = true;
        _film_cue = cue;
        _film_seconds = maxf(seconds, 0.0);
    }

    public void clear_film_cue()
    {
        _film_controlled = false;
        _film_cue = "";
    }

    public Vector3 bird_perch_position()
    {
        /// Feet contact the rendered, tilted post cap. Useful for framing a close
        /// observation from the dock; the bird itself is not enlarged for the film.
        return ToGlobal(_bird_perch);
    }

    public Vector3 insect_focus(StringName cue)
    {
        return ToGlobal(cue == POND_CUE ? _dragonfly_anchor : _butterfly_anchor);
    }

    public override void _Process(double delta)
    {
        _life_clock += delta;
        if (!_shelter_ready && Game.Instance.camp != null && Game.Instance.camp.campsite != null)
        {
            Tent tent = Game.Instance.camp.campsite.tent;
            if (tent != null)
            {
                ((ShaderMaterial)motes.MaterialOverride).SetShaderParameter("tent_inverse", tent.GlobalTransform.AffineInverse());
                ((ShaderMaterial)fireflies.MaterialOverride).SetShaderParameter("tent_inverse", tent.GlobalTransform.AffineInverse());
                _shelter_ready = true;
            }
        }
        WorldController world = Game.Instance.world;
        double daylight = world != null ? world.daylight() : 1.0;
        double rain = world != null && world.weather != null ? world.weather.rain : 0.0;
        double wind = world != null ? world.wind_strength() : 0.5;
        _bird_wind_phase += delta * clampf(wind, 0.0, 1.5);
        double fair = 1.0 - smoothstep(0.10, 0.65, rain);
        motes.Emitting = fair > 0.1 && daylight < 0.8;
        motes.AmountRatio = (float)(fair * (1.0 - smoothstep(0.35, 0.8, daylight)));
        _fish_clock += delta * lerpf(0.35, 1.0, daylight) * lerpf(0.8, 1.0, fair);
        _update_small_life(daylight, fair, wind);
        if (fireflies != null)
        {
            fireflies.Emitting = daylight < 0.45 && fair > 0.1;
            fireflies.AmountRatio = (float)(clampf(1.0 - daylight * 2.2, 0.0, 1.0) * fair);
        }
    }

    public void apply_quality(QualityPreset p)
    {
        _quality_life_scale = p.particle_scale;
        if (fireflies != null)
        {
            fireflies.Amount = (int)maxi((long)round(160.0 * p.particle_scale), 16);
        }
        if (motes != null)
        {
            motes.Amount = (int)maxi((long)round(40.0 * p.particle_scale), 8);
        }
    }

    public void _build_motes()
    {
        motes = new GpuParticles3D();
        motes.Name = "Motes";
        motes.Amount = 40;
        motes.Lifetime = 14.0;
        motes.Preprocess = 10.0;
        motes.VisibilityAabb = new Aabb(new Vector3(-18, -2, -18), new Vector3(36, 10, 36));
        motes.Position = new Vector3(0.0f, 1.4f, 0.0f);
        ParticleProcessMaterial process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        process.EmissionBoxExtents = new Vector3(12.0f, 1.6f, 12.0f);
        process.Direction = new Vector3(0.2f, 0.4f, 0.1f);
        process.Spread = 180.0f;
        process.InitialVelocityMin = 0.02f;
        process.InitialVelocityMax = 0.08f;
        process.Gravity = new Vector3(0.0f, 0.008f, 0.0f);
        process.DampingMin = 0.2f;
        process.DampingMax = 0.5f;
        process.ScaleMin = 0.008f;
        process.ScaleMax = 0.02f;
        process.Color = new Color(1.0f, 0.82f, 0.55f, 0.7f);
        motes.ProcessMaterial = process;
        motes.DrawPass1 = PropMeshes.sphere_mesh(0.5, 4, 5);
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/mote.gdshader");
        mat.SetShaderParameter("glow", 1.4);
        motes.MaterialOverride = mat;
        AddChild(motes);
    }

    public void _build_fireflies()
    {
        fireflies = new GpuParticles3D();
        fireflies.Name = "Fireflies";
        fireflies.Amount = 160;
        fireflies.Lifetime = 11.0;
        fireflies.Preprocess = 8.0;
        fireflies.VisibilityAabb = new Aabb(new Vector3(-24, -2, -24), new Vector3(48, 12, 48));
        fireflies.Position = new Vector3(0.0f, 1.0f, 0.0f);
        ParticleProcessMaterial process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        process.EmissionBoxExtents = new Vector3(18.0f, 1.2f, 18.0f);
        process.Direction = new Vector3(0.0f, 1.0f, 0.0f);
        process.Spread = 180.0f;
        process.InitialVelocityMin = 0.12f;
        process.InitialVelocityMax = 0.4f;
        process.Gravity = new Vector3(0.0f, 0.0f, 0.0f);
        process.DampingMin = 0.05f;
        process.DampingMax = 0.2f;
        process.ScaleMin = 0.012f;
        process.ScaleMax = 0.024f;
        process.Color = new Color(0.75f, 1.0f, 0.35f);
        process.HueVariationMin = -0.04f;
        process.HueVariationMax = 0.06f;
        // Meandering flight: the turbulence re-aims each firefly as it drifts.
        process.TurbulenceEnabled = true;
        process.TurbulenceNoiseStrength = 1.4f;
        process.TurbulenceNoiseScale = 1.3f;
        process.TurbulenceNoiseSpeed = new Vector3(0.25f, 0.1f, 0.25f);
        process.TurbulenceInfluenceMin = 0.12f;
        process.TurbulenceInfluenceMax = 0.3f;
        fireflies.ProcessMaterial = process;
        fireflies.DrawPass1 = PropMeshes.sphere_mesh(0.5, 4, 5);
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/firefly.gdshader");
        mat.SetShaderParameter("glow", 6.0);
        mat.SetShaderParameter("lifetime", fireflies.Lifetime);
        fireflies.MaterialOverride = mat;
        fireflies.Emitting = false;
        AddChild(fireflies);
    }

    public void _build_small_life()
    {
        // All routes and habitat heights are prepared once. Animation has no terrain queries.
        ShaderMaterial bird_mat = new ShaderMaterial();
        bird_mat.Shader = Content.Load<Shader>("res://shaders/small_wildlife.gdshader");
        for (long i = 0; i < 3; i++)
        {
            Node3D bird = new Node3D();
            bird.Name = G.format("PondBird%d", i);
            AddChild(bird);
            MeshInstance3D body = new MeshInstance3D();
            body.Name = "FlyingBody";
            body.Mesh = _bird_body();
            body.MaterialOverride = bird_mat;
            bird.AddChild(body);
            foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
            {
                MeshInstance3D wing = new MeshInstance3D();
                // Twelve millimetres of feather overlap cover wrist yaw/flexion.
                wing.Mesh = _bird_wing(side.AsDouble(), 0.0, 0.58, Vector3.Zero);
                wing.MaterialOverride = bird_mat;
                bird.AddChild(wing);
                Vector3 wrist = new Vector3(G.op("*", side, 0.13).AsSingle(), 0.010f, 0.008f);
                MeshInstance3D outer = new MeshInstance3D();
                outer.Name = "Primaries";
                outer.Mesh = _bird_wing(side.AsDouble(), 0.52, 1.0, wrist);
                outer.Position = wrist;
                outer.MaterialOverride = bird_mat;
                wing.AddChild(outer);
            }
            Node3D perched = new Node3D();
            perched.Name = "Perched";
            perched.Visible = false;
            bird.AddChild(perched);
            MeshInstance3D resting = new MeshInstance3D();
            resting.Mesh = _perched_bird_mesh();
            resting.MaterialOverride = bird_mat;
            perched.AddChild(resting);
            double altitude = 4.5 + i * 0.7;
            for (long step = 0; step < 81; step++)
            {
                double t = (double)step / 80.0;
                double x = -46.0 + t * 24.0;
                double z = 1.0 + i * 3.0 + sin(t * PI) * 2.0;
                altitude = maxf(altitude, field.height(x, z) + 3.5);
            }
            _bird_heights.Add(altitude);
            _birds.Add(bird);
        }
        // Small clusters at the meadow vegetation and sheltered east pond margin.
        foreach (Variant at in new Godot.Collections.Array { new Vector2(-15.0f, 9.4f), new Vector2(-10.0f, -5.0f), MEADOW_INSECTS, new Vector2(-44, 18), new Vector2(15, 20), new Vector2(-52, -3), new Vector2(30, -18), new Vector2(-19, 24) })
        {
            _gnat_centres.Add(new Vector3(G.Index(at, "x").AsSingle(), (float)(field.height(G.Index(at, "x").AsDouble(), G.Index(at, "y").AsDouble()) + 0.85), G.Index(at, "y").AsSingle()));
        }
        _gnats = new MultiMeshInstance3D();
        _gnats.Name = "VegetationGnats";
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.Mesh = _gnat_mesh();
        mm.InstanceCount = 64;
        _gnats.Multimesh = mm;
        _gnats.MaterialOverride = bird_mat;
        _gnats.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_gnats);
        _build_day_insects(bird_mat);
        ShaderMaterial fish_mat = new ShaderMaterial();
        fish_mat.Shader = Content.Load<Shader>("res://shaders/small_fish.gdshader");
        ArrayMesh fish_mesh = _fish_mesh();
        for (long i2 = 0; i2 < 18; i2++)
        {
            List<Vector3> route = new List<Vector3>();
            long school = i2 / 6;
            long member = i2 % 6;
            Vector2 centre = new Vector2((float)(-28.0 - school * 5.0 - member * 0.32), (float)(5.2 + school * 2.2 + member * 0.22));
            double level = TerrainField.WATER_LEVEL - 0.65 - school * 0.20 - member * 0.06;
            bool safe = true;
            for (long step2 = 0; step2 < 160; step2++)
            {
                double angle = TAU * step2 / 160.0;
                Vector2 at2 = centre + new Vector2((float)(cos(angle) * 2.0), (float)(sin(angle) * 0.85));
                // Allow for body, tail motion and interpolation between samples.
                if (field.height(at2.X, at2.Y) > level - 0.35)
                {
                    safe = false;
                    break;
                }
                route.Add(new Vector3(at2.X, (float)level, at2.Y));
            }
            if (!safe)
            {
                continue;
            }
            MeshInstance3D fish = new MeshInstance3D();
            fish.Name = G.format("PondFish%d", i2);
            fish.Mesh = fish_mesh;
            fish.SetInstanceShaderParameter("body_variant", (double)(i2 % 3));
            fish.MaterialOverride = fish_mat;
            fish.SetInstanceShaderParameter("swim_phase", i2 * 1.73);
            fish.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(fish);
            _fish.Add(fish);
            fish_routes.Add(route);
        }
    }

    public void _prepare_bird_perch(Dock dock = null)
    {
        if (dock == null && Game.Instance.camp != null && Game.Instance.camp.campsite != null)
        {
            dock = Game.Instance.camp.campsite.dock;
        }
        if (dock == null || (long)dock._piles.Count < 5)
        {
            return;
        }
        // Third left post: no rope wraps or lantern occupy this cap. Sample the
        // actual mesh, since the driven posts deliberately vary in tilt and height.
        Vector3 cap = dock._piles[4];
        MeshInstance3D piles = dock.GetNode<MeshInstance3D>("Piles");
        Godot.Collections.Dictionary hit = piles.Mesh.GenerateTriangleMesh().IntersectRay(cap + Vector3.Up * 0.2f, Vector3.Down);
        if ((hit.Count == 0))
        {
            return;
        }
        _bird_perch = ToLocal(dock.ToGlobal(hit["position"].AsVector3()));
        Vector3 normal = G.op("*", GlobalBasis.Inverse() * dock.GlobalBasis, hit["normal"]).AsVector3();
        if (normal.Y < 0.0)
        {
            normal = -normal;
        }
        // A side-on resting pose exposes the head/breast from the dock approach.
        // The bird turns toward open water during the first part of its launch.
        Vector3 heading = new Vector3(-0.40f, 0.0f, 1.0f).Normalized();
        _bird_perch_basis = new Basis(new Quaternion(Vector3.Up, normal.Normalized())) * Basis.LookingAt(heading);
        _perch_ready = true;
    }

    public void _build_day_insects(ShaderMaterial material)
    {
        _butterfly_anchor = new Vector3(MEADOW_INSECTS.X, (float)(field.height(MEADOW_INSECTS.X, MEADOW_INSECTS.Y) + 0.82), MEADOW_INSECTS.Y);
        _dragonfly_anchor = new Vector3(POND_INSECTS.X, (float)maxf(field.height(POND_INSECTS.X, POND_INSECTS.Y) + 0.25, TerrainField.WATER_LEVEL + 0.65), POND_INSECTS.Y);
        for (long i = 0; i < 12; i++)
        {
            Node3D butterfly = new Node3D();
            butterfly.Name = G.format("MeadowButterfly%d", i);
            AddChild(butterfly);
            MeshInstance3D body = new MeshInstance3D();
            body.Mesh = _insect_body(false);
            body.MaterialOverride = material;
            butterfly.AddChild(body);
            foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
            {
                MeshInstance3D wing = new MeshInstance3D();
                wing.Mesh = _butterfly_wing(side.AsDouble(), i);
                wing.MaterialOverride = material;
                wing.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                butterfly.AddChild(wing);
            }
            _butterflies.Add(butterfly);
            Godot.Collections.Array meadow_sites = new Godot.Collections.Array { new Vector2(4.3f, 10.2f), new Vector2(12, 17), new Vector2(-10, 19), new Vector2(20, -12), new Vector2(29, 15), new Vector2(-19, -26) };
            Vector2 bp = meadow_sites[(int)(i / 2)].AsVector2();
            _butterfly_habitats.Add(new Vector3(bp.X, (float)(maxf(field.surface_height(bp.X, bp.Y), TerrainField.WATER_LEVEL) + 0.82), bp.Y));
            Node3D dragonfly = new Node3D();
            dragonfly.Name = G.format("PondDragonfly%d", i);
            AddChild(dragonfly);
            MeshInstance3D thorax = new MeshInstance3D();
            thorax.Mesh = _insect_body(true);
            thorax.MaterialOverride = material;
            dragonfly.AddChild(thorax);
            foreach (Variant side2 in new Godot.Collections.Array { -1.0, 1.0 })
            {
                MeshInstance3D wings = new MeshInstance3D();
                wings.Mesh = _dragonfly_wings(side2.AsDouble());
                wings.MaterialOverride = material;
                wings.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                dragonfly.AddChild(wings);
            }
            _dragonflies.Add(dragonfly);
            Godot.Collections.Array pond_sites = new Godot.Collections.Array { POND_INSECTS, new Vector2(-42, 17), new Vector2(-48, -2), new Vector2(-32, -10), new Vector2(-24, 15), new Vector2(-49, 8) };
            Vector2 dp = pond_sites[(int)(i / 2)].AsVector2();
            _dragonfly_habitats.Add(new Vector3(dp.X, (float)maxf(field.surface_height(dp.X, dp.Y) + 0.25, TerrainField.WATER_LEVEL + 0.65), dp.Y));
        }
    }

    public Vector3 bird_cue_position(long index, double seconds)
    {
        /// A small songbird stays on a real perch long enough to read, then travels
        /// out across open water. No camera-relative scaling or near-screen overlay.
        Vector3 start = _bird_perch + Vector3.Up * 0.060f;
        if (index == 0)
        {
            double t = clampf((seconds - BIRD_TAKEOFF) / BIRD_FLIGHT_SECONDS, 0.0, 1.0);
            return start.BezierInterpolate(start + new Vector3(-0.9f, 0.70f, -0.30f), start + new Vector3(-14.0f, 1.7f, -6.0f), start + new Vector3(-25.0f, 2.8f, -11.0f), (float)t);
        }
        double t2 = clampf((seconds - COMPANION_START) / (COMPANION_END - COMPANION_START), 0.0, 1.0);
        // A later, low crossing occupies reflected water in the dock composition
        // instead of shrinking into the similarly coloured far-bank vegetation.
        return (_bird_perch + new Vector3(-6.5f, 0.04f, 4.1f)).BezierInterpolate(_bird_perch + new Vector3(-5.3f, -0.23f, 1.9f), _bird_perch + new Vector3(-3.3f, -0.23f, -4.1f), _bird_perch + new Vector3(-2.0f, 0.10f, -7.6f), (float)t2);
    }

    public void _update_film_birds(double active)
    {
        for (long i = 0, i_end = (long)_birds.Count; i < i_end; i++)
        {
            Node3D bird = _birds[(int)i];
            bool started = i == 0 ? _film_seconds >= 0.0 : _film_seconds >= COMPANION_START;
            bool finished = i == 0 ? _film_seconds > BIRD_TAKEOFF + BIRD_FLIGHT_SECONDS : _film_seconds > COMPANION_END;
            bird.Visible = _film_cue == BIRD_CUE && _perch_ready && active > 0.20 && i < 2 && started && !finished;
            bird.Scale = Vector3.One;
            if (!bird.Visible)
            {
                continue;
            }
            bool perching = i == 0 && _film_seconds < BIRD_TAKEOFF;
            for (long part = 0; part < 3; part++)
            {
                bird.GetChild<Node3D>((int)part).Visible = !perching;
            }
            bird.GetNode<Node3D>("Perched").Visible = perching;
            if (perching)
            {
                bird.Position = _bird_perch;
                bird.Basis = _bird_perch_basis;
                continue;
            }
            bird.Position = bird_cue_position(i, _film_seconds);
            Vector3 ahead = bird_cue_position(i, _film_seconds + 0.02);
            Vector3 before = bird_cue_position(i, _film_seconds - 0.02);
            Basis flight_basis = Basis.LookingAt((ahead - before).Normalized(), Vector3.Up);
            double opening = i == 0 ? smoothstep(BIRD_TAKEOFF, BIRD_TAKEOFF + 0.35, _film_seconds) : 1.0;
            bird.Basis = i == 0 ? _bird_perch_basis.Slerp(flight_basis, (float)opening) : flight_basis;
            Vector3 _t1 = bird.GetChild<Node3D>(0).Rotation;
            _t1.X = (float)lerpf(0.43, 0.0, opening);
            bird.GetChild<Node3D>(0).Rotation = _t1;
            _animate_bird(bird, i, _film_seconds, 0.0);
            for (long side_index = 0; side_index < 2; side_index++)
            {
                Node3D wing = bird.GetChild<Node3D>((int)(side_index + 1));
                double side = side_index == 0 ? -1.0 : 1.0;
                Vector3 _t2 = wing.Rotation;
                _t2.Z = (float)lerpf(side * 1.30, wing.Rotation.Z, opening);
                wing.Rotation = _t2;
            }
        }
    }

    public void _animate_bird(Node3D bird, long index, double clock, double wind_phase)
    {
        // Short, unequal glides interrupt each bird's strokes. The wrist lags
        // the shoulder on recovery, then settles into a shallow gliding dihedral.
        double beat = clock * (31.0 + index * 1.8) + wind_phase;
        double cycle = fmod(clock + index * 0.83, 2.7 + index * 0.37);
        double strokes = 1.0 - smoothstep(1.30, 1.58, cycle);
        strokes = maxf(strokes, smoothstep(2.25 + index * 0.3, 2.60 + index * 0.37, cycle));
        double flap = lerpf(0.10, sin(beat) * 0.72, strokes);
        double wrist_flap = sin(beat - 0.72) * 0.25 * strokes;
        for (long side_index = 0; side_index < 2; side_index++)
        {
            Node3D wing = bird.GetChild<Node3D>((int)(side_index + 1));
            double side = side_index == 0 ? -1.0 : 1.0;
            Vector3 _t1 = wing.Rotation;
            _t1.Z = (float)(side * flap);
            wing.Rotation = _t1;
            Node3D outer = wing.GetChild<Node3D>(0);
            Vector3 _t2 = outer.Rotation;
            _t2.Z = (float)(side * wrist_flap);
            outer.Rotation = _t2;
            Vector3 _t3 = outer.Rotation;
            _t3.Y = (float)(side * maxf(0.0, cos(beat)) * 0.14 * strokes);
            outer.Rotation = _t3;
        }
    }

    public void _update_small_life(double daylight, double fair, double wind)
    {
        double active = smoothstep(0.12, 0.40, daylight) * fair;
        if (_film_controlled)
        {
            _update_film_birds(active);
        }
        for (long i = 0, i_end = (long)_birds.Count; i < i_end; i++)
        {
            if (_film_controlled)
            {
                break;
            }
            Node3D bird = _birds[(int)i];
            double t = (_life_clock + i * 13.7) / (36.0 + i * 7.0) * TAU;
            bird.Visible = active > 0.20;
            if (!bird.Visible)
            {
                continue;
            }
            bird.Scale = Vector3.One;
            bird.Position = new Vector3((float)(-34.0 + cos(t) * 12.0), (float)(_bird_heights[(int)i] + 1.2 + sin(t * 2.0) * 0.75), (float)(4.0 + sin(t) * 8.0));
            Vector3 tangent = new Vector3((float)(-sin(t) * 12.0), (float)(cos(t * 2.0) * 1.5), (float)(cos(t) * 8.0));
            bird.Basis = Basis.LookingAt(tangent.Normalized(), Vector3.Up);
            bird.RotateObjectLocal(Vector3.Forward, -0.12f);
            for (long part = 0; part < 3; part++)
            {
                bird.GetChild<Node3D>((int)part).Visible = true;
            }
            Vector3 _t1 = bird.GetChild<Node3D>(0).Rotation;
            _t1.X = 0.0f;
            bird.GetChild<Node3D>(0).Rotation = _t1;
            bird.GetNode<Node3D>("Perched").Visible = false;
            _animate_bird(bird, i, _life_clock, _bird_wind_phase);
        }
        if (_gnats != null)
        {
            _gnats.Visible = active > 0.05;
            double clock = _film_controlled ? _film_seconds : _life_clock;
            for (long i2 = 0, i_end2 = _gnats.Multimesh.InstanceCount; i2 < i_end2; i2++)
            {
                double t2 = clock * (1.3 + (double)(i2 % 7) * 0.13) + i2 * 2.399;
                Vector3 offset = new Vector3((float)(sin(t2) * 0.30), (float)(sin(t2 * 1.71) * 0.20), (float)(cos(t2 * 1.23) * 0.30));
                offset.X = (float)(offset.X + sin(clock * 0.7) * minf(wind, 1.5) * 0.06);
                Basis basis = new Basis(Vector3.Up, (float)(t2 * 0.8));
                _gnats.Multimesh.SetInstanceTransform((int)i2, new Transform3D(basis, _gnat_centres[(int)(i2 % (long)_gnat_centres.Count)] + offset));
            }
        }
        _update_day_insects(active, wind);
        if (!(_fish.Count == 0))
        {
            (_fish[0].MaterialOverride as ShaderMaterial).SetShaderParameter("swim_clock", _fish_clock);
        }
        for (long i3 = 0, i_end3 = (long)_fish.Count; i3 < i_end3; i3++)
        {
            List<Vector3> route = fish_routes[(int)i3];
            double cursor = fmod(_fish_clock * (2.4 + (double)(i3 / 6) * 0.24) + i3 % 6 * 7.0 + i3 / 6 * 31.0, (double)(long)route.Count);
            long index = (long)cursor;
            long next = (index + 1) % (long)route.Count;
            Vector3 at = route[(int)index].Lerp(route[(int)next], (float)(cursor - index));
            Vector3 tangent_a = (route[(int)next] - route[(int)((index - 1 + (long)route.Count) % (long)route.Count)]).Normalized();
            Vector3 tangent_b = (route[(int)((next + 1) % (long)route.Count)] - route[(int)index]).Normalized();
            Vector3 direction = tangent_a.Lerp(tangent_b, (float)(cursor - index)).Normalized();
            _fish[(int)i3].Position = at;
            _fish[(int)i3].Basis = Basis.LookingAt(direction, Vector3.Up);
        }
    }

    public Vector3 _butterfly_position(long index, double seconds)
    {
        double t = seconds * (0.74 + index * 0.09) + index * 2.1;
        Vector3 anchor = _film_controlled || (_butterfly_habitats.Count == 0) ? _butterfly_anchor : _butterfly_habitats[(int)index];
        long partner = index % 2;
        return anchor + new Vector3((float)(sin(t) * 0.38 + partner * 0.28), (float)(sin(t * 1.71) * 0.15 + cos(t * 0.6) * 0.08), (float)(cos(t * 0.87) * 0.29 + partner * 0.18));
    }

    public void _update_day_insects(double active, double wind)
    {
        double clock = _film_controlled ? _film_seconds : _life_clock;
        for (long i = 0, i_end = (long)_butterflies.Count; i < i_end; i++)
        {
            Node3D butterfly = _butterflies[(int)i];
            butterfly.Visible = active > 0.25 && wind < 1.8 && (!_film_controlled || _film_cue == MEADOW_CUE && i < 2) && i < maxi(2, (long)(12 * _quality_life_scale));
            if (!butterfly.Visible)
            {
                continue;
            }
            butterfly.Position = _butterfly_position(i, clock);
            Vector3 tangent = _butterfly_position(i, clock + 0.04) - _butterfly_position(i, clock - 0.04);
            butterfly.Basis = Basis.LookingAt(tangent.Normalized(), Vector3.Up);
            double beat = 0.2 + (sin(clock * (27.0 + i * 2.1) + i) * 0.5 + 0.5) * 1.0;
            for (long side = 0; side < 2; side++)
            {
                Vector3 _t1 = butterfly.GetChild<Node3D>((int)(side + 1)).Rotation;
                _t1.Z = (float)((side == 0 ? -1.0 : 1.0) * beat);
                butterfly.GetChild<Node3D>((int)(side + 1)).Rotation = _t1;
            }
        }
        for (long i2 = 0, i_end2 = (long)_dragonflies.Count; i2 < i_end2; i2++)
        {
            Node3D dragonfly = _dragonflies[(int)i2];
            dragonfly.Visible = active > 0.25 && wind < 1.8 && (!_film_controlled || _film_cue == POND_CUE && i2 == 0) && i2 < maxi(2, (long)(12 * _quality_life_scale));
            if (!dragonfly.Visible)
            {
                continue;
            }
            double t = clock * 0.73 + i2 * 2.7;
            double dart = smoothstep(-0.40, 0.40, sin(t * 1.2));
            if (_film_controlled)
            {
                // Pass through the lily composition. Only one dragonfly uses this
                // close cinematic lane; the second stays out of the film view.
                double progress = clampf(clock / 12.0, 0.0, 1.0);
                double along = lerpf(-1.5, 1.5, progress);
                dragonfly.Position = _dragonfly_anchor + new Vector3((float)along, (float)(sin(t * 2.1) * 0.045), (float)(sin(progress * TAU) * 0.22));
                Vector3 _t2 = dragonfly.Rotation;
                _t2.Y = (float)(-PI * 0.5 + cos(progress * TAU) * 0.18);
                dragonfly.Rotation = _t2;
            }
            else
            {
                Vector3 anchor = !(_dragonfly_habitats.Count == 0) ? _dragonfly_habitats[(int)i2] : _dragonfly_anchor;
                dragonfly.Position = anchor + new Vector3((float)(lerpf(-0.4, 0.4, dart) + i2 % 2 * 0.32), (float)(sin(t * 2.1) * 0.065 + i2 % 2 * 0.13), (float)(cos(t * 0.6) * 0.35));
                Vector3 _t3 = dragonfly.Rotation;
                _t3.Y = (float)(sin(t * 0.6) * 0.7 + 0.4);
                dragonfly.Rotation = _t3;
            }
            for (long side2 = 0; side2 < 2; side2++)
            {
                Vector3 _t4 = dragonfly.GetChild<Node3D>((int)(side2 + 1)).Rotation;
                _t4.Z = (float)((side2 == 0 ? -1.0 : 1.0) * sin(clock * 91.0 + i2 * 1.2) * 0.18);
                dragonfly.GetChild<Node3D>((int)(side2 + 1)).Rotation = _t4;
            }
        }
    }

    public static ArrayMesh _perched_bird_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        Godot.Collections.Array body = _bird_body().SurfaceGetArrays(0);
        Basis tilt = new Basis(Vector3.Right, 0.43f);
        Vector3 lift = new Vector3(0.0f, 0.067f, 0.0f);
        List<Vector3> points = G.ListFromVariant<Vector3>(body[(int)Mesh.ArrayType.Vertex]);
        List<Vector3> normals = G.ListFromVariant<Vector3>(body[(int)Mesh.ArrayType.Normal]);
        List<Vector2> uv = G.ListFromVariant<Vector2>(body[(int)Mesh.ArrayType.TexUV]);
        List<Color> colors = G.ListFromVariant<Color>(body[(int)Mesh.ArrayType.Color]);
        for (long i = 0, i_end = (long)points.Count; i < i_end; i++)
        {
            mb.add_vertex(tilt * points[(int)i] + lift, tilt * normals[(int)i], uv[(int)i], colors[(int)i]);
        }
        mb.indices.AddRange(G.ListFromVariant<int>(body[(int)Mesh.ArrayType.Index]));
        foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
        {
            long first = mb.vertex_count();
            // Closed, narrow coverts lie along the flank; flight wings are hidden
            // while resting, rather than sticking out sideways through the post.
            _ellipsoid(mb, new Vector3(G.op("*", side, 0.023).AsSingle(), 0.002f, 0.027f), new Vector3(0.013f, 0.027f, 0.072f), new Color(0.16f, 0.17f, 0.135f), 8, 12);
            for (long j = first, j_end = mb.vertex_count(); j < j_end; j++)
            {
                mb.vertices[(int)j] = tilt * mb.vertices[(int)j] + lift;
                mb.normals[(int)j] = tilt * mb.normals[(int)j];
            }
            Vector3 ankle = new Vector3(G.op("*", side, 0.014).AsSingle(), 0.003f, -0.010f);
            mb.add_tube(new Godot.Collections.Array<Vector3> { ankle, new Vector3(G.op("*", side, 0.017).AsSingle(), 0.034f, 0.0f), new Vector3(G.op("*", side, 0.019).AsSingle(), 0.048f, 0.012f) }, new Godot.Collections.Array<double> { 0.0015, 0.0020, 0.0025 }, 5, new Color(0.24f, 0.19f, 0.13f), 1.0, 1.0, 0.0, true);
            for (long toe = 0; toe < 3; toe++)
            {
                Vector3 end = ankle + new Vector3((float)((toe - 1) * 0.007), -0.0023f, (float)(-0.021 + absf(toe - 1) * 0.004));
                mb.add_tube(new Godot.Collections.Array<Vector3> { ankle, end }, new Godot.Collections.Array<double> { 0.0012, 0.00065 }, 4, new Color(0.22f, 0.17f, 0.12f), 1.0, 1.0, 0.0, true);
            }
            mb.add_tube(new Godot.Collections.Array<Vector3> { ankle, ankle + new Vector3(0.0f, -0.0023f, 0.021f) }, new Godot.Collections.Array<double> { 0.0012, 0.0006 }, 4, new Color(0.22f, 0.17f, 0.12f), 1.0, 1.0, 0.0, true);
        }
        return mb.commit();
    }

    public static ArrayMesh _gnat_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        _ellipsoid(mb, Vector3.Zero, new Vector3(0.0010f, 0.0010f, 0.0023f), new Color(0.09f, 0.075f, 0.05f), 4, 6);
        foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
        {
            mb.add_quad(new Vector3(G.op("*", side, 0.0005).AsSingle(), 0.0007f, -0.001f), new Vector3(G.op("*", side, 0.004).AsSingle(), 0.0018f, -0.0006f), new Vector3(G.op("*", side, 0.0035).AsSingle(), 0.0013f, 0.0011f), new Vector3(G.op("*", side, 0.0005).AsSingle(), 0.0007f, 0.0007f), new Color(0.38f, 0.34f, 0.23f, 0.25f));
        }
        return mb.commit();
    }

    public static ArrayMesh _insect_body(bool dragonfly)
    {
        MeshBuilder mb = new MeshBuilder();
        Color dark = new Color(0.055f, 0.060f, 0.033f);
        _ellipsoid(mb, Vector3.Zero, new Vector3(0.0026f, 0.0028f, 0.006f), dark, 5, 8);
        _ellipsoid(mb, new Vector3(0, 0.001f, -0.007f), new Vector3(0.0028f, 0.0025f, 0.0028f), dark, 5, 8);
        if (dragonfly)
        {
            mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0, 0, 0.004f), new Vector3(0, 0, 0.020f), new Vector3(0, -0.001f, 0.038f) }, new Godot.Collections.Array<double> { 0.0025, 0.0018, 0.00065 }, 6, new Color(0.16f, 0.26f, 0.27f), 1.0, 1.0, 0.0, true);
        }
        else
        {
            mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0, 0, 0.003f), new Vector3(0, -0.001f, 0.012f) }, new Godot.Collections.Array<double> { 0.0020, 0.0008 }, 6, dark, 1.0, 1.0, 0.0, true);
            foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
            {
                mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(G.op("*", side, 0.001).AsSingle(), 0.002f, -0.008f), new Vector3(G.op("*", side, 0.005).AsSingle(), 0.005f, -0.017f) }, new Godot.Collections.Array<double> { 0.0003, 0.0004 }, 4, dark);
            }
        }
        return mb.commit();
    }

    public static ArrayMesh _butterfly_wing(double side, long variant)
    {
        MeshBuilder mb = new MeshBuilder();
        // Separate, curved fore/hind lobes avoid the bright nine-sided paper
        // silhouette of the initial mesh. Reflectance stays below ivory canvas.
        Color @base = variant == 0 ? new Color(0.42f, 0.285f, 0.12f) : new Color(0.48f, 0.46f, 0.32f);
        List<Vector2> fore = new List<Vector2>();
        List<Vector2> hind = new List<Vector2>();
        Godot.Collections.Array curves = new Godot.Collections.Array { new Godot.Collections.Array { new Vector2(0.001f, -0.003f), new Vector2(0.010f, -0.020f), new Vector2(0.017f, -0.033f), new Vector2(0.027f, -0.028f) }, new Godot.Collections.Array { new Vector2(0.027f, -0.028f), new Vector2(0.038f, -0.023f), new Vector2(0.036f, -0.014f), new Vector2(0.026f, -0.006f) }, new Godot.Collections.Array { new Vector2(0.026f, -0.006f), new Vector2(0.022f, 0.001f), new Vector2(0.009f, 0.006f), new Vector2(0.001f, 0.002f) }, new Godot.Collections.Array { new Vector2(0.001f, 0.001f), new Vector2(0.012f, -0.002f), new Vector2(0.026f, 0.002f), new Vector2(0.027f, 0.010f) }, new Godot.Collections.Array { new Vector2(0.027f, 0.010f), new Vector2(0.028f, 0.023f), new Vector2(0.018f, 0.027f), new Vector2(0.012f, 0.023f) }, new Godot.Collections.Array { new Vector2(0.012f, 0.023f), new Vector2(0.006f, 0.019f), new Vector2(0.003f, 0.012f), new Vector2(0.001f, 0.005f) } };
        for (long segment = 0, segment_end = (long)curves.Count; segment < segment_end; segment++)
        {
            Godot.Collections.Array curve = curves[(int)segment].AsGodotArray();
            Vector2 start = curve[0].AsVector2();
            for (long step = 0; step < 8; step++)
            {
                Vector2 point = start.BezierInterpolate(curve[1].AsVector2(), curve[2].AsVector2(), curve[3].AsVector2(), (float)((double)step / 8.0));
                if (segment < 3)
                {
                    fore.Add(point);
                }
                else
                {
                    hind.Add(point);
                }
            }
        }
        fore.Add(new Vector2(0.001f, 0.002f));
        hind.Add(new Vector2(0.001f, 0.005f));
        _butterfly_lobe(mb, fore, new Vector2(0.009f, -0.009f), side, @base, 0.0004);
        _butterfly_lobe(mb, hind, new Vector2(0.009f, 0.009f), side, @base.Darkened(0.09f), 0.0);
        // Small scale markings remain dark under direct sun instead of allowing
        // the entire folded wing to wash into a uniform white fleck.
        _ellipsoid(mb, new Vector3((float)(side * 0.018), 0.0014f, -0.016f), new Vector3(0.0018f, 0.00015f, 0.0023f), new Color(0.10f, 0.085f, 0.050f), 4, 10);
        return mb.commit();
    }

    public static void _butterfly_lobe(MeshBuilder mb, List<Vector2> outline, Vector2 centre, double side, Color @base, double height)
    {
        long RINGS = 4;
        long ci = mb.add_vertex(new Vector3((float)(centre.X * side), (float)(height + 0.0009), centre.Y), Vector3.Up, Vector2.Zero, @base.Darkened(0.14f));
        long start = mb.vertex_count();
        for (long ring = 1, ring_end = RINGS + 1; ring < ring_end; ring++)
        {
            double r = (double)ring / RINGS;
            for (long i = 0, i_end = (long)outline.Count; i < i_end; i++)
            {
                Vector2 p = centre.Lerp(outline[(int)i], (float)r);
                Vector2 direction = (p - centre).Normalized();
                double border = smoothstep(0.80, 1.0, r) * 0.36;
                double vein = pow(maxf(cos((double)i / (long)outline.Count * TAU * 7.0), 0.0), 18.0) * 0.13;
                Color tint = @base.Darkened((float)(border + vein + (1.0 - r) * 0.07));
                Vector3 normal = new Vector3((float)(direction.X * side * 0.08 * r), 1.0f, (float)(direction.Y * 0.08 * r)).Normalized();
                mb.add_vertex(new Vector3((float)(p.X * side), (float)(height + (1.0 - r * r) * 0.0009), p.Y), normal, new Vector2((float)r, (float)((double)i / (long)outline.Count)), tint);
            }
        }
        for (long i2 = 0, i_end2 = (long)outline.Count; i2 < i_end2; i2++)
        {
            long next = (i2 + 1) % (long)outline.Count;
            if (side < 0.0)
            {
                mb.add_triangle(ci, start + i2, start + next);
            }
            else
            {
                mb.add_triangle(ci, start + next, start + i2);
            }
            for (long ring2 = 0, ring_end2 = RINGS - 1; ring2 < ring_end2; ring2++)
            {
                long a = start + ring2 * (long)outline.Count + i2;
                long b = start + ring2 * (long)outline.Count + next;
                mb.add_quad_facing(a, a + (long)outline.Count, b + (long)outline.Count, b, Vector3.Up);
            }
        }
    }

    public static ArrayMesh _dragonfly_wings(double side)
    {
        MeshBuilder mb = new MeshBuilder();
        for (long pair = 0; pair < 2; pair++)
        {
            double z = pair * 0.007 - 0.003;
            mb.add_quad(new Vector3((float)(side * 0.002), 0.001f, (float)z), new Vector3((float)(side * 0.037), 0.002f, (float)(z - 0.009)), new Vector3((float)(side * 0.040), 0.001f, (float)(z - 0.004)), new Vector3((float)(side * 0.007), 0.001f, (float)(z + 0.003)), new Color(0.46f, 0.49f, 0.39f, 0.25f));
        }
        return mb.commit();
    }

    public static void _ellipsoid(MeshBuilder mb, Vector3 centre, Vector3 radii, Color color, long rings = 10, long sectors = 16)
    {
        // Analytic ellipsoid normals share the wrap seam exactly; never recompute them
        // after joining eyes, head or fins to the body.
        long start = mb.vertex_count();
        for (long row = 0, row_end = rings + 1; row < row_end; row++)
        {
            double phi = PI * row / (double)rings;
            for (long col = 0; col < sectors; col++)
            {
                double theta = TAU * col / (double)sectors;
                Vector3 dir = new Vector3((float)(sin(phi) * cos(theta)), (float)cos(phi), (float)(sin(phi) * sin(theta)));
                mb.add_vertex(centre + dir * radii, (dir / radii).Normalized(), new Vector2((float)((double)col / sectors), (float)((double)row / rings)), color);
            }
        }
        // Pole quads have coincident first vertices: their zero-area winding
        // test cannot select a front face. Emit explicit clockwise fans instead.
        for (long col2 = 0; col2 < sectors; col2++)
        {
            long next = (col2 + 1) % sectors;
            mb.add_triangle(start + col2, start + sectors + col2, start + sectors + next);
            mb.add_triangle(start + rings * sectors + col2, start + (rings - 1) * sectors + next, start + (rings - 1) * sectors + col2);
        }
        for (long row2 = 1, row_end2 = rings - 1; row2 < row_end2; row2++)
        {
            for (long col3 = 0; col3 < sectors; col3++)
            {
                long a = start + row2 * sectors + col3;
                long b = start + row2 * sectors + (col3 + 1) % sectors;
                Vector3 outward = (mb.vertices[(int)a] + mb.vertices[(int)b]) * 0.5f - centre;
                mb.add_quad_facing(a, b, b + sectors, a + sectors, outward);
            }
        }
    }

    public static ArrayMesh _bird_body()
    {
        MeshBuilder mb = new MeshBuilder();
        _ellipsoid(mb, new Vector3(0, 0, 0.002f), new Vector3(0.029f, 0.029f, 0.076f), new Color(0.12f, 0.125f, 0.10f));
        _ellipsoid(mb, new Vector3(0, 0.017f, -0.062f), new Vector3(0.023f, 0.025f, 0.030f), new Color(0.10f, 0.105f, 0.085f));
        // Small breast and cheek tones survive silhouette-distance lighting.
        _ellipsoid(mb, new Vector3(0, -0.011f, -0.020f), new Vector3(0.025f, 0.022f, 0.050f), new Color(0.38f, 0.355f, 0.28f));
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0, 0.015f, -0.083f), new Vector3(0, 0.011f, -0.110f) }, new Godot.Collections.Array<double> { 0.007, 0.0006 }, 8, new Color(0.055f, 0.052f, 0.04f));
        foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
        {
            _ellipsoid(mb, new Vector3(G.op("*", side, 0.020).AsSingle(), 0.022f, -0.073f), new Vector3(0.003f, 0.003f, 0.003f), new Color(0.008f, 0.009f, 0.007f), 6, 8);
        }
        // Two tapered tail lobes with an actual shallow central cleft.
        long start = mb.vertex_count();
        for (long row = 0; row < 5; row++)
        {
            double t = row / 4.0;
            for (long col = 0; col < 13; col++)
            {
                double u = col / 6.0 - 1.0;
                double edge = 0.107 + absf(u) * 0.031;
                Vector3 pos = new Vector3((float)(u * lerpf(0.010, 0.026, t)), (float)(-0.002 - t * 0.006), (float)lerpf(0.055, edge, t));
                mb.add_vertex(pos, Vector3.Up, new Vector2((float)(col / 12.0), (float)t), new Color(0.08f, 0.085f, 0.065f));
            }
        }
        for (long row2 = 0; row2 < 4; row2++)
        {
            for (long col2 = 0; col2 < 12; col2++)
            {
                long a = start + row2 * 13 + col2;
                mb.add_quad_facing(a, a + 1, a + 14, a + 13, Vector3.Up);
            }
        }
        return mb.commit();
    }

    public static ArrayMesh _bird_wing(double side, double span_start, double span_end, Vector3 origin)
    {
        MeshBuilder mb = new MeshBuilder();
        long ROWS = 12;
        long COLS = 5;
        for (long row = 0, row_end = ROWS + 1; row < row_end; row++)
        {
            double t = lerpf(span_start, span_end, row / (double)ROWS);
            double x = side * (0.020 + t * 0.20);
            double leading = -0.040 - sin(t * PI) * 0.025 + t * t * 0.140;
            double chord = (0.085 + sin(t * PI) * 0.023) * pow(1.0 - t, 0.65) + 0.001;
            for (long col = 0, col_end = COLS + 1; col < col_end; col++)
            {
                double u = col / (double)COLS;
                double feather_edge = sin(t * PI * 13.0) * sin(t * PI * 13.0) * 0.002 * u * u * smoothstep(0.35, 0.75, t);
                Vector3 pos = new Vector3((float)x, (float)(sin(t * PI) * 0.010 + sin(u * PI) * 0.003), (float)(leading + chord * u - feather_edge));
                if (span_start > 0.0)
                {
                    // Layer the proximal primaries just under the coverts while
                    // gliding, avoiding coplanar flicker in the overlapping strip.
                    pos.Y = (float)(pos.Y - 0.0008 * (1.0 - smoothstep(0.52, 0.61, t)));
                }
                Color tone = new Color(0.13f, 0.135f, 0.105f).Lerp(new Color(0.065f, 0.070f, 0.052f), (float)(smoothstep(0.35, 1.0, t) * 0.7 + u * 0.2));
                mb.add_vertex(pos - origin, Vector3.Up, new Vector2((float)t, (float)u), tone);
            }
        }
        for (long row2 = 0; row2 < ROWS; row2++)
        {
            for (long col2 = 0; col2 < COLS; col2++)
            {
                long a = row2 * (COLS + 1) + col2;
                mb.add_quad_facing(a, a + 1, a + COLS + 2, a + COLS + 1, Vector3.Up);
            }
        }
        mb.recompute_normals();
        return mb.commit();
    }

    public static Vector3 _fish_section(double t)
    {
        double belly = pow(maxf(sin(t * PI), 0.0), 0.82);
        double taper = 1.0 - smoothstep(0.38, 0.92, t) * 0.79;
        return new Vector3((float)(-0.095 + t * 0.165), (float)(0.001 + belly * 0.018 * taper + smoothstep(0.78, 1.0, t) * 0.002), (float)(0.001 + belly * 0.029 * taper + smoothstep(0.78, 1.0, t) * 0.004));
    }

    public static ArrayMesh _fish_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        long RINGS = 40;
        long SECTORS = 20;
        // One continuous longitudinal surface with derivative normals: no split
        // longitude vertices or separately shaded halves at the belly.
        for (long row = 0, row_end = RINGS + 1; row < row_end; row++)
        {
            double t = row / (double)RINGS;
            Vector3 section = _fish_section(t);
            Vector3 before = _fish_section(maxf(0.0, t - 0.001));
            Vector3 after = _fish_section(minf(1.0, t + 0.001));
            Vector3 derivative = (after - before) / (float)maxf((double)after.X - before.X, 0.00001);
            for (long col = 0; col < SECTORS; col++)
            {
                double a = TAU * col / (double)SECTORS;
                Vector3 n = new Vector3((float)(cos(a) / section.Y), (float)(sin(a) / section.Z), (float)(-derivative.Y * cos(a) * cos(a) / section.Y - derivative.Z * sin(a) * sin(a) / section.Z)).Normalized();
                mb.add_vertex(new Vector3((float)(section.Y * cos(a)), (float)(section.Z * sin(a)), section.X), n, new Vector2((float)(col / (double)SECTORS), (float)t));
            }
        }
        for (long row2 = 0; row2 < RINGS; row2++)
        {
            for (long col2 = 0; col2 < SECTORS; col2++)
            {
                long a2 = row2 * SECTORS + col2;
                long b = row2 * SECTORS + (col2 + 1) % SECTORS;
                mb.add_quad_facing(a2, b, b + SECTORS, a2 + SECTORS, new Vector3(mb.vertices[(int)a2].X, mb.vertices[(int)a2].Y, 0));
            }
        }
        // Close the tiny mouth and peduncle ends without splitting body normals.
        for (long end = 0; end < 2; end++)
        {
            Vector3 section2 = _fish_section((double)end);
            Vector3 outward = end == 0 ? Vector3.Forward : Vector3.Back;
            long centre = mb.add_vertex(new Vector3(0, 0, section2.X), outward, Vector2.Zero);
            for (long col3 = 0; col3 < SECTORS; col3++)
            {
                long a3 = end * RINGS * SECTORS + col3;
                long b2 = end * RINGS * SECTORS + (col3 + 1) % SECTORS;
                if (end == 0)
                {
                    mb.add_triangle(centre, a3, b2);
                }
                else
                {
                    mb.add_triangle(centre, b2, a3);
                }
            }
        }
        // Forked caudal fin grows from the narrow peduncle, with swept lobes.
        long start = mb.vertex_count();
        for (long row3 = 0; row3 < 7; row3++)
        {
            double t2 = row3 / 6.0;
            for (long col4 = 0; col4 < 17; col4++)
            {
                double u = col4 / 8.0 - 1.0;
                double z = lerpf(0.066, 0.090 + pow(absf(u), 0.65) * 0.025, t2);
                double y = u * lerpf(0.005, 0.029, t2);
                mb.add_vertex(new Vector3((float)(sin(u * PI) * t2 * 0.001), (float)y, (float)z), Vector3.Right, new Vector2((float)(col4 / 16.0), (float)t2), new Color(0.72f, 0.73f, 0.57f, 0.5f));
            }
        }
        for (long row4 = 0; row4 < 6; row4++)
        {
            for (long col5 = 0; col5 < 16; col5++)
            {
                long a4 = start + row4 * 17 + col5;
                mb.add_quad_facing(a4, a4 + 1, a4 + 18, a4 + 17, Vector3.Right);
            }
        }
        // Low rounded dorsal/anal fins and paired pectoral/pelvic fins.
        _fin(mb, new Vector3(0, 0.022f, -0.022f), new Vector3(0, 0.010f, 0.044f), new Vector3(0, 0.043f, -0.006f));
        _fin(mb, new Vector3(0, -0.018f, 0.012f), new Vector3(0, -0.009f, 0.050f), new Vector3(0, -0.032f, 0.026f));
        foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
        {
            _fin(mb, new Vector3(G.op("*", side, 0.014).AsSingle(), -0.011f, -0.049f), new Vector3(G.op("*", side, 0.011).AsSingle(), -0.013f, -0.027f), new Vector3(G.op("*", side, 0.034).AsSingle(), -0.021f, -0.014f));
            _fin(mb, new Vector3(G.op("*", side, 0.009).AsSingle(), -0.016f, 0.015f), new Vector3(G.op("*", side, 0.008).AsSingle(), -0.009f, 0.037f), new Vector3(G.op("*", side, 0.022).AsSingle(), -0.024f, 0.037f));
            // Tiny olive iris and black pupil are real geometry, not a pasted disc.
            _ellipsoid(mb, new Vector3(G.op("*", side, 0.0117).AsSingle(), 0.007f, -0.073f), new Vector3(0.0027f, 0.0034f, 0.0034f), new Color(0.32f, 0.29f, 0.12f, 0.0f), 6, 10);
            _ellipsoid(mb, new Vector3(G.op("*", side, 0.0139).AsSingle(), 0.007f, -0.073f), new Vector3(0.0012f, 0.0022f, 0.0022f), new Color(0.008f, 0.011f, 0.008f, 0.0f), 6, 10);
        }
        return mb.commit();
    }

    public static void _fin(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 tip)
    {
        long start = mb.vertex_count();
        Vector3 n = (b - a).Cross(tip - a).Normalized();
        for (long row = 0; row < 5; row++)
        {
            double t = row / 4.0;
            for (long col = 0; col < 9; col++)
            {
                double u = col / 8.0;
                Vector3 root = a.Lerp(b, (float)u);
                Vector3 edge = a * (float)((1.0 - u) * (1.0 - u)) + tip * (float)(2.0 * u * (1.0 - u)) + b * (float)(u * u);
                mb.add_vertex(root.Lerp(edge, (float)t), n, new Vector2((float)u, (float)t), new Color(0.72f, 0.73f, 0.57f, 0.5f));
            }
        }
        for (long row2 = 0; row2 < 4; row2++)
        {
            for (long col2 = 0; col2 < 8; col2++)
            {
                long v = start + row2 * 9 + col2;
                mb.add_quad_facing(v, v + 1, v + 10, v + 9, n);
            }
        }
    }
}
