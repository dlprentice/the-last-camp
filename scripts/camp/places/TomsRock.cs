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
    public const double STEP_MAX_GAP = 2.6;
    public const double TURN_CLEARANCE = 1.0;
    public const double STEP_MAX_RISER = 0.24;
    /// The loose footstone: its size, how far it tips and turns, its offset.
    public static readonly Vector3 LOOSE_SIZE = new(0.80f, 0.13f, 0.46f);
    public const double LOOSE_TILT_DEG = 3.0;
    public const double LOOSE_YAW_DEG = 2.2;
    public const double LOOSE_SHIFT = 0.008;
    /// How far its back edge has settled into the ground.
    public const double LOOSE_SETTLE = 0.016;
    /// The hollow under it: how deep, and how wide a rim it rests on.
    public const double LOOSE_HOLLOW = 0.06;
    public const double LOOSE_RIM = 0.075;
    public static readonly Vector3 TIN_SIZE = new(0.17f, 0.03f, 0.11f);
    public const double TIN_SINK = 0.006;

    public static readonly Vector3 BENCH_SEAT = new(1.32f, 0.13f, 0.46f);
    public static readonly Vector3 BENCH_SUPPORT = new(0.30f, 0.0f, 0.40f);
    public const double BENCH_SUPPORT_X = 0.42;
    public const double BENCH_SUPPORT_SINK = 0.08;

    /// Exposed bedrock: ledges this far from the summit, along rings of the outcrop.
    public const double LEDGE_INNER = 2.6;
    public const double LEDGE_OUTER = 13.5;

    public struct Ledge
    {
        public GraniteSlab.Shape shape;
        public Transform3D pose;
    }

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
        double previous = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            double jitter = rng.RandfRange(-0.05f, 0.05f);
            Vector3 size = new(rng.RandfRange(0.84f, 0.98f), 0.0f, rng.RandfRange(0.44f, 0.52f));
            // Tom set each stone where the ground let it sit low: nudge it along the
            // line to the nearest spot where its front stands no more than a step.
            Slab best = default;
            double best_score = double.PositiveInfinity;
            for (int o = 0; o <= 12; o++)
            {
                double offset = (o % 2 == 0 ? 1 : -1) * ((o + 1) / 2) * 0.1;
                double s = along[k] + offset;
                if (s < previous + STEP_MIN_GAP || s > last || s < first) continue;
                Slab slab = make_slab(field, line, total, s, size, jitter);
                double r = riser(field, slab);
                double score = (r > STEP_MAX_RISER ? 10.0 + r : 0.0) + Math.Abs(offset);
                if (score < best_score) { best_score = score; best = slab; }
            }
            if (double.IsPositiveInfinity(best_score)) best = make_slab(field, line, total, Math.Max(along[k], previous + STEP_MIN_GAP), size, jitter);
            previous = best.along;
            result.Add(best);
        }
        return result;
    }

    static Slab make_slab(TerrainField field, Vector2[] line, double total, double along, Vector3 size, double jitter)
    {
        Vector2 a = PlacesLayout.point_at(line, Math.Max(along - 0.3, 0.0), out _);
        Vector2 b = PlacesLayout.point_at(line, Math.Min(along + 0.3, total), out _);
        Vector2 centre = PlacesLayout.point_at(line, along, out _);
        Vector2 forward = (b - a).Normalized();
        double yaw = Math.Atan2(-forward.X, -forward.Y) + jitter;
        Basis basis = new(Vector3.Up, (float)yaw);
        // Level top just proud of the highest ground under its tread, bottom below the lowest.
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
        return new Slab { xform = new Transform3D(basis, new Vector3(centre.X, (float)(top - thickness * 0.5), centre.Y)), size = size, along = along };
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
        Material granite = GraniteSlab.material();
        _build_ledges(granite);
        _build_steps(granite);
        _build_bench(granite);
        _build_loose_step(granite);
    }

    /// Clear of the steps, the paths, the bench and the bench's view.
    public static bool ledge_clear(TerrainField field, Vector2 at, double reach, double top)
    {
        double keep = reach + 0.7;
        if (PlacesLayout.distance_to(at, PlacesLayout.STEP_LINE, out _) < keep) return false;
        if (PlacesLayout.distance_to(at, PlacesLayout.PAINTED_PATH, out _) < keep) return false;
        if (PlacesLayout.distance_to(at, PlacesLayout.TOMS_ROUTE, out _) < keep) return false;
        if (at.DistanceTo(PlacesLayout.BENCH) < keep + 0.9 || at.DistanceTo(PlacesLayout.LOOSE_STEP) < keep + 0.9) return false;
        // In front of the bench a ledge may stand, but only below the seated sight line.
        if (PlacesLayout.lookout_openness(at) > 0.0)
        {
            double eye = field.height(PlacesLayout.BENCH.X, PlacesLayout.BENCH.Y) + PlacesLayout.SEATED_EYE;
            double d = at.DistanceTo(PlacesLayout.BENCH);
            double line = eye - (eye - TerrainField.WATER_LEVEL) * d / PlacesLayout.BENCH.DistanceTo(TerrainField.POND_CENTRE);
            if (top > line - 0.8) return false;
        }
        return true;
    }

    /// The outcrop's bedrock: broad, low granite ledges laid along its contours,
    /// tipped with the slope and settled so no part of their lower half stands
    /// above the ground. Seeded; the same field always gives the same rock.
    public static List<Ledge> ledges(TerrainField field)
    {
        var rng = new RandomNumberGenerator { Seed = 1971 };
        var list = new List<Ledge>();
        int index = 0;
        for (double r = LEDGE_INNER; r <= LEDGE_OUTER; r += 1.75)
        {
            double circumference = Math.Tau * r;
            int count = Math.Max(3, (int)(circumference / 3.1));
            double offset = rng.Randf() * 360.0;
            for (int i = 0; i < count; i++)
            {
                double bearing = offset + i * 360.0 / count + rng.RandfRange(-8f, 8f);
                double rr = r + rng.RandfRange(-0.5f, 0.5f);
                Vector2 at = PlacesLayout.polar(rr, bearing);
                double scale = Mathf.Lerp(1.0f, 0.7f, (float)((rr - LEDGE_INNER) / (LEDGE_OUTER - LEDGE_INNER)));
                Vector3 size = new(rng.RandfRange(2.0f, 3.6f) * (float)scale, rng.RandfRange(0.7f, 1.3f) * (float)scale, rng.RandfRange(1.4f, 2.4f) * (float)scale);
                long seed = 900 + index++;
                double roundness = rng.RandfRange(0.22f, 0.36f);
                double yaw_jitter = rng.RandfRange(-0.35f, 0.35f);
                // Long axis along the contour; up leans halfway to the slope's normal.
                Vector2 radial = (at - PlacesLayout.TOMS_ROCK).Normalized();
                Vector3 tangent = new(-radial.Y, 0, radial.X);
                Vector3 normal = field.normal(at.X, at.Y, 1.0);
                Vector3 up = (Vector3.Up + normal).Normalized();
                Vector3 x_axis = (tangent - up * tangent.Dot(up)).Normalized().Rotated(up, (float)yaw_jitter);
                Vector3 z_axis = x_axis.Cross(up).Normalized();
                Basis basis = new(x_axis, up, z_axis);
                double origin_y = double.PositiveInfinity;
                for (int k = 0; k < 16; k++)
                {
                    double a = k / 16.0 * Math.Tau;
                    Vector3 local = new((float)(Math.Cos(a) * size.X * 0.45), -size.Y * 0.2f, (float)(Math.Sin(a) * size.Z * 0.45));
                    Vector3 w = basis * local;
                    origin_y = Math.Min(origin_y, field.height(at.X + w.X, at.Y + w.Z) - w.Y);
                }
                double top = origin_y + size.Y * 0.5;
                double reach = Math.Max(size.X, size.Z) * 0.5;
                if (!ledge_clear(field, at, reach, top)) continue;
                list.Add(new Ledge
                {
                    shape = new GraniteSlab.Shape { size = size, roundness = roundness, rough = 0.16, seed = seed },
                    pose = new Transform3D(basis, new Vector3(at.X, (float)origin_y, at.Y)),
                });
            }
        }
        return list;
    }

    /// The summit's own rock: low, broad slabs of the bedrock showing through the
    /// thin soil round the bench, a few centimetres proud, walkable, clear of the
    /// bench, its footstone and the steps.
    public static List<Ledge> pavement(TerrainField field)
    {
        var rng = new RandomNumberGenerator { Seed = 1972 };
        var list = new List<Ledge>();
        int index = 0;
        foreach (double r in new[] { 1.5, 2.5, 3.5 })
        {
            int count = (int)(Math.Tau * r / 1.6);
            double offset = rng.Randf() * 360.0;
            for (int i = 0; i < count; i++)
            {
                Vector2 at = PlacesLayout.polar(r + rng.RandfRange(-0.25f, 0.25f), offset + i * 360.0 / count);
                Vector3 size = new(rng.RandfRange(1.0f, 1.6f), rng.RandfRange(0.28f, 0.4f), rng.RandfRange(0.7f, 1.1f));
                double yaw = rng.Randf() * Math.Tau;
                long seed = 1300 + index++;
                double keep = Math.Max(size.X, size.Z) * 0.5 + 0.35;
                if (PlacesLayout.distance_to(at, PlacesLayout.STEP_LINE, out _) < keep) continue;
                if (PlacesLayout.distance_to(at, PlacesLayout.PAINTED_PATH, out _) < keep) continue;
                if (at.DistanceTo(PlacesLayout.BENCH) < keep + 0.85 || at.DistanceTo(PlacesLayout.LOOSE_STEP) < keep + 0.5) continue;
                Basis basis = new(Vector3.Up, (float)yaw);
                double low = double.PositiveInfinity;
                for (int k = 0; k < 12; k++)
                {
                    double a = k / 12.0 * Math.Tau;
                    Vector3 w = basis * new Vector3((float)(Math.Cos(a) * size.X * 0.48), 0, (float)(Math.Sin(a) * size.Z * 0.48));
                    low = Math.Min(low, field.height(at.X + w.X, at.Y + w.Z));
                }
                double proud = rng.RandfRange(0.04f, 0.07f);
                list.Add(new Ledge
                {
                    shape = new GraniteSlab.Shape { size = size, roundness = 0.3, rough = 0.18, flat_top = true, seed = seed },
                    pose = new Transform3D(basis, new Vector3(at.X, (float)(low + proud - size.Y * 0.5), at.Y)),
                });
            }
        }
        return list;
    }

    public void _build_ledges(Material granite)
    {
        var holder = new Node3D { Name = "Bedrock" };
        AddChild(holder);
        int i = 0;
        var all = ledges(field);
        all.AddRange(pavement(field));
        foreach (Ledge l in all)
        {
            ArrayMesh mesh = GraniteSlab.build(l.shape, l.pose, field.height);
            holder.AddChild(new MeshInstance3D
            {
                Name = $"Ledge_{i}", Mesh = mesh, MaterialOverride = granite, Transform = l.pose,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static,
            });
            var body = new StaticBody3D { Name = $"LedgeBody_{i}", CollisionLayer = 1, CollisionMask = 0, Position = l.pose.Origin };
            body.SetMeta("surface", (StringName)"rock");
            body.AddChild(new CollisionShape3D { Shape = GraniteSlab.hull(mesh, l.pose.Basis) });
            holder.AddChild(body);
            i++;
        }
    }

    public static GraniteSlab.Shape step_shape(Slab s, int index) => new()
    {
        size = s.size, roundness = 0.16, rough = 0.10, flat_top = true, seed = 2000 + index,
    };

    public void _build_steps(Material stone)
    {
        steps.Clear();
        steps.AddRange(set_steps(field));
        var holder = new Node3D { Name = "Steps" };
        AddChild(holder);
        for (int i = 0; i < steps.Count; i++)
        {
            Slab s = steps[i];
            ArrayMesh mesh = GraniteSlab.build(step_shape(s, i), s.xform, field.height);
            var mi = new MeshInstance3D { Name = $"Step_{i + 1}", Mesh = mesh, MaterialOverride = stone, Transform = s.xform, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static };
            holder.AddChild(mi);
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

    public static GraniteSlab.Shape loose_shape() => new()
    {
        size = LOOSE_SIZE, roundness = 0.16, rough = 0.10, flat_top = true, seed = 2019, hollow = LOOSE_HOLLOW, hollow_inset = LOOSE_RIM,
    };

    public void _build_bench(Material stone)
    {
        double ground = field.height(PlacesLayout.BENCH.X, PlacesLayout.BENCH.Y);
        Transform3D frame = new(summit_basis(), new Vector3(PlacesLayout.BENCH.X, (float)ground, PlacesLayout.BENCH.Y));
        var bench = new Node3D { Name = "StoneBench", Transform = frame };
        AddChild(bench);
        float seat_bottom = (float)PlacesLayout.BENCH_SEAT_HEIGHT - BENCH_SEAT.Y;
        float support_h = seat_bottom + (float)BENCH_SUPPORT_SINK;
        void stone_at(string name, GraniteSlab.Shape shape, Vector3 at)
        {
            Transform3D local = new(Basis.Identity, at);
            ArrayMesh mesh = GraniteSlab.build(shape, frame * local, field.height);
            bench.AddChild(new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = stone, Transform = local, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static });
        }
        stone_at("Seat", new GraniteSlab.Shape { size = BENCH_SEAT, roundness = 0.18, rough = 0.09, flat_top = true, seed = 2101 }, new Vector3(0, seat_bottom + BENCH_SEAT.Y * 0.5f, 0));
        int n = 0;
        foreach (float x in new[] { -(float)BENCH_SUPPORT_X, (float)BENCH_SUPPORT_X })
        {
            stone_at($"Support_{n}", new GraniteSlab.Shape { size = new Vector3(BENCH_SUPPORT.X, support_h + 0.02f, BENCH_SUPPORT.Z), roundness = 0.24, rough = 0.14, seed = 2102 + n },
                new Vector3(x, seat_bottom - support_h * 0.5f + 0.01f, 0.02f));
            n++;
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
        // One stone, like the others, but hollowed underneath: it rests on its
        // rim, so the tin under it has room.
        ArrayMesh mesh = GraniteSlab.build(loose_shape(), loose_rest, field.height);
        loose_step.AddChild(new MeshInstance3D { Name = "Stone", Mesh = mesh, MaterialOverride = stone, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
        loose_body = new StaticBody3D { Name = "LooseStepBody", CollisionLayer = 1, CollisionMask = 0 };
        loose_body.SetMeta("surface", (StringName)"rock");
        loose_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = LOOSE_SIZE } });
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
