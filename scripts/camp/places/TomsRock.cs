#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// Tom's Rock: the granite outcrop at the top of his route, the twenty flat
/// stones he set up its north-east flank, and the stone bench facing the pond
/// and the sunset. The twentieth step, the footstone under the bench, is loose:
/// it sits a few degrees off level and askew, with a hairline gap under its
/// front edge, and nothing marks it. Under it is a small rusted tin, closed.
///
/// The summit's local frame: +X to the right of someone on the bench, -Z the way
/// they look (PlacesLayout.FACE), Y up.
public partial class TomsRock : Node3D
{
    public struct Slab
    {
        /// Centre of the stone and its orientation (local X across the path).
        public Transform3D xform;
        public Vector3 size;
        /// Arc length along PlacesLayout.STEP_LINE.
        public double along;
    }

    public const double STEP_PROUD = 0.035;
    public const double STEP_SINK = 0.05;
    public const double STEP_MIN_THICKNESS = 0.14;
    public const double STEP_MIN_GAP = 0.62;
    public const double STEP_MAX_GAP = 1.7;
    public const double TURN_CLEARANCE = 1.0;
    /// The loose footstone: its size, how far it tips and turns, its offset.
    public static readonly Vector3 LOOSE_SIZE = new(0.62f, 0.10f, 0.42f);
    public const double LOOSE_TILT_DEG = 3.0;
    public const double LOOSE_YAW_DEG = 2.2;
    public const double LOOSE_SHIFT = 0.008;
    /// How far its back edge has settled into the ground.
    public const double LOOSE_SETTLE = 0.016;
    public const double LOOSE_LID = 0.045;
    public static readonly Vector3 TIN_SIZE = new(0.17f, 0.03f, 0.11f);
    public const double TIN_SINK = 0.006;

    public static readonly Vector3 BENCH_SEAT = new(1.25f, 0.09f, 0.40f);
    public static readonly Vector3 BENCH_SUPPORT = new(0.22f, 0.0f, 0.34f);
    public const double BENCH_SUPPORT_X = 0.42;
    public const double BENCH_SUPPORT_SINK = 0.08;

    /// Granite boulders breaking the outcrop's flanks: (distance, bearing, size).
    public static readonly Vector3[] BOULDERS =
    {
        new(3.9f, 150f, 1.25f), new(5.6f, 112f, 1.7f), new(7.2f, 160f, 2.3f), new(4.9f, 195f, 1.5f),
        new(8.6f, 222f, 1.9f), new(11.0f, 248f, 2.4f), new(6.3f, 268f, 1.35f), new(11.6f, 178f, 2.1f),
        new(9.4f, 92f, 1.6f), new(12.6f, 128f, 1.9f), new(10.2f, 292f, 1.3f), new(3.6f, 228f, 1.1f),
    };

    public TerrainField field;
    public Node3D loose_step;
    public StaticBody3D loose_body;
    public Node3D tin;
    public StaticBody3D bench_body;
    public bool step_lifted = false;
    public Transform3D loose_rest;
    public readonly List<Slab> steps = new();

    public TomsRock(TerrainField p_field)
    {
        field = p_field;
        Name = "TomsRock";
    }

    public TomsRock()
    {
    }

    public static Basis summit_basis(double yaw_offset_deg = 0.0)
    {
        Vector3 right = new(PlacesLayout.RIGHT.X, 0, PlacesLayout.RIGHT.Y);
        Vector3 back = new(-PlacesLayout.FACE.X, 0, -PlacesLayout.FACE.Y);
        Basis b = new(right, Vector3.Up, back);
        return yaw_offset_deg == 0.0 ? b : new Basis(Vector3.Up, Mathf.DegToRad((float)yaw_offset_deg)) * b;
    }

    /// The nineteen set steps, bottom first, placed at even rises up the cut
    /// line, each a level stone whose top stands just proud of the uphill ground.
    public static List<Slab> set_steps(TerrainField field)
    {
        Vector2[] line = PlacesLayout.STEP_LINE;
        double total = PlacesLayout.length(line);
        const double ds = 0.05;
        int n = (int)(total / ds) + 1;
        var envelope = new double[n];
        double running = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = PlacesLayout.point_at(line, i * ds, out _);
            running = Math.Max(running, field.height(p.X, p.Y));
            envelope[i] = running;
        }
        double first = 0.35;
        double last = total - LOOSE_SIZE.X * 0.5 - 0.42;
        double h0 = envelope[(int)(first / ds)], h1 = envelope[Math.Min((int)(last / ds), n - 1)];
        int count = (int)PlacesLayout.STEP_COUNT - 1;
        var along = new double[count];
        // A stone never straddles a switchback turn: it goes just past it instead.
        var turns = new List<double>();
        double run = 0.0;
        for (int i = 1; i + 1 < line.Length; i++)
        {
            run += line[i - 1].DistanceTo(line[i]);
            double turn = Math.Abs((line[i] - line[i - 1]).AngleTo(line[i + 1] - line[i]));
            if (turn > Mathf.DegToRad(50.0f)) turns.Add(run);
        }
        double clear_of_turns(double s)
        {
            foreach (double t in turns)
            {
                if (Math.Abs(s - t) < TURN_CLEARANCE) return t + TURN_CLEARANCE;
            }
            return s;
        }
        for (int k = 0; k < count; k++)
        {
            double target = h0 + (h1 - h0) * k / (count - 1);
            double s = first;
            while (s < last && envelope[Math.Min((int)(s / ds), n - 1)] < target) s += ds;
            if (k > 0) s = Math.Clamp(s, along[k - 1] + STEP_MIN_GAP, along[k - 1] + STEP_MAX_GAP);
            along[k] = Math.Min(clear_of_turns(s), last);
        }
        // Spacing pushed the top steps against the summit: spread them back down.
        for (int k = count - 2; k >= 0; k--) along[k] = Math.Min(along[k], along[k + 1] - STEP_MIN_GAP);

        var rng = new RandomNumberGenerator { Seed = 2014 };
        var result = new List<Slab>();
        for (int k = 0; k < count; k++)
        {
            Vector2 a = PlacesLayout.point_at(line, Math.Max(along[k] - 0.3, 0.0), out _);
            Vector2 b = PlacesLayout.point_at(line, Math.Min(along[k] + 0.3, total), out _);
            Vector2 centre = PlacesLayout.point_at(line, along[k], out _);
            Vector2 forward = (b - a).Normalized();
            double yaw = Math.Atan2(-forward.X, -forward.Y) + rng.RandfRange(-0.05f, 0.05f);
            Vector3 size = new(rng.RandfRange(0.84f, 0.98f), 0.0f, rng.RandfRange(0.44f, 0.52f));
            Basis basis = new(Vector3.Up, (float)yaw);
            // Level top just proud of the highest ground under it, bottom below the lowest.
            double high = double.NegativeInfinity, low = double.PositiveInfinity;
            for (int i = -2; i <= 2; i++)
            {
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 q = basis * new Vector3(size.X * 0.5f * i / 2.0f, 0, size.Z * 0.5f * j / 2.0f);
                    double g = field.height(centre.X + q.X, centre.Y + q.Z);
                    // The back edge may sit into the bank; the tread and front stand proud.
                    if (j >= 0) high = Math.Max(high, g);
                    low = Math.Min(low, g);
                }
            }
            double top = high + STEP_PROUD;
            double thickness = Math.Max(top - (low - STEP_SINK), STEP_MIN_THICKNESS);
            size.Y = (float)thickness;
            result.Add(new Slab
            {
                xform = new Transform3D(basis, new Vector3(centre.X, (float)(top - thickness * 0.5), centre.Y)),
                size = size,
                along = along[k],
            });
        }
        return result;
    }

    /// The footstone as Tom left it, before it settled wrong.
    public static Transform3D loose_neutral(TerrainField field)
    {
        double ground = field.height(PlacesLayout.LOOSE_STEP.X, PlacesLayout.LOOSE_STEP.Y);
        Vector3 at = new(PlacesLayout.LOOSE_STEP.X, (float)(ground - LOOSE_SETTLE + LOOSE_SIZE.Y * 0.5), PlacesLayout.LOOSE_STEP.Y);
        return new Transform3D(summit_basis(), at);
    }

    /// The footstone as it lies: turned a little, tipped up at the front about
    /// its settled back edge, and shifted a few millimetres to one side.
    public static Transform3D loose_pose(TerrainField field)
    {
        Transform3D neutral = loose_neutral(field);
        Basis basis = summit_basis(LOOSE_YAW_DEG) * new Basis(Vector3.Right, Mathf.DegToRad((float)LOOSE_TILT_DEG));
        Vector3 hinge_local = new(0, -LOOSE_SIZE.Y * 0.5f, LOOSE_SIZE.Z * 0.5f);
        Vector3 hinge_world = neutral * hinge_local + summit_basis() * new Vector3((float)LOOSE_SHIFT, 0, 0);
        return new Transform3D(basis, hinge_world - basis * hinge_local);
    }

    public void build()
    {
        ShaderMaterial granite = PropMaterials.triplanar("rock", new Color(0.97f, 0.95f, 0.91f), 0.5, 0.32);
        ShaderMaterial worn = PropMaterials.triplanar("rock", new Color(0.93f, 0.91f, 0.87f), 0.6, 0.12);
        _build_boulders(granite);
        _build_steps(worn);
        _build_bench(worn);
        _build_loose_step(worn);
    }

    public static bool boulder_clear(Vector2 at, double size)
    {
        double keep = size * 1.3 + 0.8;
        return PlacesLayout.distance_to(at, PlacesLayout.STEP_LINE, out _) > keep
            && PlacesLayout.distance_to(at, PlacesLayout.PAINTED_PATH, out _) > keep
            && PlacesLayout.distance_to(at, PlacesLayout.TOMS_ROUTE, out _) > keep
            && at.DistanceTo(PlacesLayout.BENCH) > keep + 0.6
            && !PlacesLayout.in_lookout_view(at, 0.0);
    }

    /// Where each boulder sits: sunk so its widest part is near the ground and
    /// its underside is buried all round.
    public static List<Transform3D> boulder_poses(TerrainField field)
    {
        var poses = new List<Transform3D>();
        var rng = new RandomNumberGenerator { Seed = 40 };
        foreach (Vector3 b in BOULDERS)
        {
            Vector2 at = PlacesLayout.polar(b.X, b.Y);
            double yaw = rng.Randf() * Math.Tau;
            if (!boulder_clear(at, b.Z)) continue;
            double low = double.PositiveInfinity;
            for (int i = 0; i < 12; i++)
            {
                double a = i / 12.0 * Math.Tau;
                low = Math.Min(low, field.height(at.X + Math.Cos(a) * b.Z * 0.8, at.Y + Math.Sin(a) * b.Z * 0.8));
            }
            Basis basis = new Basis(Vector3.Up, (float)yaw).Scaled(new Vector3(b.Z, b.Z * 0.62f, b.Z * 1.08f));
            poses.Add(new Transform3D(basis, new Vector3(at.X, (float)(low - b.Z * 0.18), at.Y)));
        }
        return poses;
    }

    public void _build_boulders(Material granite)
    {
        int i = 0;
        foreach (Transform3D pose in boulder_poses(field))
        {
            var mesh = new MeshInstance3D
            {
                Name = $"Granite_{i}", Mesh = PropMeshes.rock(5100 + i * 37, 1.0), MaterialOverride = granite, Transform = pose,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static,
            };
            AddChild(mesh);
            // The hull of the stretched stone itself, baked into world-scaled points
            // (a scaled collision shape is not a reliable physics primitive).
            var hull = (ConvexPolygonShape3D)((ArrayMesh)mesh.Mesh).CreateConvexShape(true, true);
            Vector3[] points = hull.Points;
            for (int k = 0; k < points.Length; k++) points[k] = pose.Basis * points[k];
            var body = new StaticBody3D { Name = $"GraniteBody_{i}", CollisionLayer = 1, CollisionMask = 0, Position = pose.Origin };
            body.SetMeta("surface", (StringName)"rock");
            body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } });
            AddChild(body);
            i++;
        }
    }

    public void _build_steps(Material stone)
    {
        steps.Clear();
        steps.AddRange(set_steps(field));
        var holder = new Node3D { Name = "Steps" };
        AddChild(holder);
        for (int i = 0; i < steps.Count; i++)
        {
            Slab s = steps[i];
            MeshInstance3D mesh = FieldKit.add(holder, FieldKit.rounded_box(s.size, 0.035), stone, Vector3.Zero, $"Step_{i + 1}");
            mesh.Transform = s.xform;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            mesh.GIMode = GeometryInstance3D.GIModeEnum.Static;
            var body = new StaticBody3D { Name = $"StepBody_{i + 1}", CollisionLayer = 1, CollisionMask = 0 };
            body.SetMeta("surface", (StringName)"rock");
            body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = step_collision(field, s) } });
            holder.AddChild(body);
        }
    }

    /// Height of a step's front (downhill) face above the ground before it: the
    /// step up a walker actually takes.
    public static double riser(TerrainField field, Slab s)
    {
        double top = s.xform.Origin.Y + s.size.Y * 0.5;
        double highest = 0.0;
        foreach (float x in new[] { -s.size.X * 0.4f, 0.0f, s.size.X * 0.4f })
        {
            Vector3 before = s.xform * new Vector3(x, 0, s.size.Z * 0.5f + 0.04f);
            highest = Math.Max(highest, top - field.height(before.X, before.Z));
        }
        return highest;
    }

    /// A step's collision: the stone itself, plus a short ramp down from its
    /// downhill edge to the ground so the walker's capsule climbs the riser
    /// instead of stopping against it. World-space points.
    public static Vector3[] step_collision(TerrainField field, Slab s)
    {
        Vector3 h = s.size * 0.5f;
        var pts = new List<Vector3>();
        foreach (float x in new[] { -h.X, h.X })
            foreach (float y in new[] { -h.Y, h.Y })
                foreach (float z in new[] { -h.Z, h.Z })
                    pts.Add(s.xform * new Vector3(x, y, z));
        // Downhill is local +Z (the path runs towards -Z, up the steps).
        double top = s.xform.Origin.Y + h.Y;
        foreach (float x in new[] { -h.X * 0.9f, h.X * 0.9f })
        {
            Vector3 edge = s.xform * new Vector3(x, h.Y, h.Z);
            double rise = top - field.height(edge.X, edge.Z);
            float run = (float)Math.Max(rise / Math.Tan(Mathf.DegToRad(30.0f)), 0.02);
            Vector3 foot = s.xform * new Vector3(x, h.Y, h.Z + run);
            foot.Y = (float)field.height(foot.X, foot.Z) - 0.02f;
            pts.Add(foot);
        }
        return pts.ToArray();
    }

    public void _build_bench(Material stone)
    {
        double ground = field.height(PlacesLayout.BENCH.X, PlacesLayout.BENCH.Y);
        var bench = new Node3D { Name = "StoneBench", Transform = new Transform3D(summit_basis(), new Vector3(PlacesLayout.BENCH.X, (float)ground, PlacesLayout.BENCH.Y)) };
        AddChild(bench);
        float seat_bottom = (float)PlacesLayout.BENCH_SEAT_HEIGHT - BENCH_SEAT.Y;
        float support_h = seat_bottom + (float)BENCH_SUPPORT_SINK;
        MeshInstance3D seat = FieldKit.add(bench, FieldKit.rounded_box(BENCH_SEAT, 0.03), stone, new Vector3(0, seat_bottom + BENCH_SEAT.Y * 0.5f, 0), "Seat");
        seat.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        foreach (float x in new[] { -(float)BENCH_SUPPORT_X, (float)BENCH_SUPPORT_X })
        {
            MeshInstance3D support = FieldKit.add(bench, FieldKit.rounded_box(new Vector3(BENCH_SUPPORT.X, support_h, BENCH_SUPPORT.Z), 0.03), stone,
                new Vector3(x, seat_bottom - support_h * 0.5f, 0.02f), "Support");
            support.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        }
        bench_body = new StaticBody3D { Name = "BenchBody", CollisionLayer = 1, CollisionMask = 0 };
        bench_body.SetMeta("surface", (StringName)"rock");
        bench_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = BENCH_SEAT }, Position = new Vector3(0, seat_bottom + BENCH_SEAT.Y * 0.5f, 0) });
        foreach (float x in new[] { -(float)BENCH_SUPPORT_X, (float)BENCH_SUPPORT_X })
            bench_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(BENCH_SUPPORT.X, seat_bottom, BENCH_SUPPORT.Z) }, Position = new Vector3(x, seat_bottom * 0.5f, 0.02f) });
        bench.AddChild(bench_body);
    }

    public void _build_loose_step(Material stone)
    {
        loose_rest = loose_pose(field);
        loose_step = new Node3D { Name = "LooseStep", Transform = loose_rest };
        AddChild(loose_step);
        // A lid with a rim under it: from above and the side it is one stone, but
        // it rests on its rim, so the tin under it has room.
        Vector3 s = LOOSE_SIZE;
        float lid = (float)LOOSE_LID;
        float rim_h = s.Y - lid;
        float rim_x = 0.08f, rim_z = 0.075f;
        void part(Vector3 size, Vector3 at, string name)
        {
            MeshInstance3D m = FieldKit.add(loose_step, FieldKit.rounded_box(size, Math.Min(0.02f, size.Y * 0.4f)), stone, at, name);
            m.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        }
        part(new Vector3(s.X, lid, s.Z), new Vector3(0, s.Y * 0.5f - lid * 0.5f, 0), "Lid");
        float rim_y = -s.Y * 0.5f + rim_h * 0.5f + 0.001f;
        part(new Vector3(s.X, rim_h, rim_z), new Vector3(0, rim_y, s.Z * 0.5f - rim_z * 0.5f), "RimBack");
        part(new Vector3(s.X, rim_h, rim_z), new Vector3(0, rim_y, -s.Z * 0.5f + rim_z * 0.5f), "RimFront");
        part(new Vector3(rim_x, rim_h, s.Z - 2 * rim_z), new Vector3(s.X * 0.5f - rim_x * 0.5f, rim_y, 0), "RimRight");
        part(new Vector3(rim_x, rim_h, s.Z - 2 * rim_z), new Vector3(-s.X * 0.5f + rim_x * 0.5f, rim_y, 0), "RimLeft");
        loose_body = new StaticBody3D { Name = "LooseStepBody", CollisionLayer = 1, CollisionMask = 0 };
        loose_body.SetMeta("surface", (StringName)"rock");
        loose_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = s } });
        loose_step.AddChild(loose_body);

        tin = new Node3D { Name = "TomsTin", Transform = tin_pose(field) };
        AddChild(tin);
        Material rust = PropMaterials.iron(new Color(0.30f, 0.16f, 0.09f));
        FieldKit.add(tin, FieldKit.rounded_box(TIN_SIZE - new Vector3(0.004f, 0.006f, 0.004f), 0.008), rust, new Vector3(0, -0.003f, 0), "TinBody");
        FieldKit.add(tin, FieldKit.rounded_box(new Vector3(TIN_SIZE.X, 0.008f, TIN_SIZE.Z), 0.003), rust, new Vector3(0, TIN_SIZE.Y * 0.5f - 0.004f, 0), "TinLid");
    }

    /// The tin lies under the footstone's centre, bedded a few millimetres into
    /// the soil, closed. Origin at its centre.
    public static Transform3D tin_pose(TerrainField field)
    {
        Vector2 at = PlacesLayout.LOOSE_STEP;
        double ground = field.height(at.X, at.Y);
        return new Transform3D(summit_basis(-4.0), new Vector3(at.X, (float)(ground - TIN_SINK + TIN_SIZE.Y * 0.5), at.Y));
    }

    /// True when `feet` (the player's floor position) stand on the footstone.
    public bool is_on_loose_step(Vector3 feet)
    {
        if (step_lifted || loose_step == null) return false;
        Vector3 local = loose_step.GlobalTransform.AffineInverse() * feet;
        Vector3 h = LOOSE_SIZE * 0.5f;
        return Math.Abs(local.X) <= h.X + 0.12f && Math.Abs(local.Z) <= h.Z + 0.12f && local.Y > h.Y - 0.08f && local.Y < h.Y + 0.3f;
    }

    /// Lifts the footstone off and lays it on the summit beside its place,
    /// leaving the tin open to view and reach. Its collision goes with it.
    public void lift_step()
    {
        if (step_lifted) return;
        step_lifted = true;
        Vector2 aside = PlacesLayout.local(0.25, -0.95);
        double ground = field.height(aside.X, aside.Y);
        Basis basis = summit_basis(-14.0) * new Basis(Vector3.Forward, Mathf.DegToRad(1.5f));
        loose_step.Transform = new Transform3D(basis, new Vector3(aside.X, (float)(ground - 0.012 + LOOSE_SIZE.Y * 0.5), aside.Y));
    }
}
