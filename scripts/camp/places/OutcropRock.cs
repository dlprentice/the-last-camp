#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using LastCamp.Construction;

namespace LastCamp;

/// The bedrock of Tom's Rock, built from the game's credited photoscanned rocks
/// (models/SOURCES.md): the moss-set masses scaled up into rounded, fractured
/// domes of granite breaking through the outcrop's thin soil, a few boulders
/// between them, and small stones and moss at the steps' ends where the flight is
/// bedded into the rock. Every piece is turned along the outcrop's contours,
/// tipped with its slope and sunk until a good part of it is in the ground all
/// round. Seeded; the same field always gives the same rock.
public static class OutcropRock
{
    public struct Piece
    {
        public string model;
        public int variant;
        /// Rotation, scale and position of the scan.
        public Transform3D pose;
        /// Where its middle stands, and its horizontal reach from there, metres.
        public Vector2 centre;
        public double reach;
        /// Share of its height that is in the ground all round.
        public double bury;
        public bool collide;
    }

    static readonly Dictionary<string, List<Mesh>> _meshes = new();

    /// The scan's LOD0 meshes, in name order, with their materials tuned as the
    /// scattered dressing tunes them.
    public static List<Mesh> meshes(string model)
    {
        if (_meshes.TryGetValue(model, out List<Mesh> found)) return found;
        var list = new List<Mesh>();
        PackedScene scene = Content.Load<PackedScene>($"res://models/{model}/{model}.gltf");
        if (scene != null)
        {
            Node root = scene.Instantiate();
            foreach (MeshInstance3D mi in ScannedDressing._mesh_instances(root))
            {
                string name = mi.Name;
                if (name.Contains("_LOD") && !name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
                ScannedDressing._tune_materials(mi.Mesh, "rock", Colors.White);
                list.Add(mi.Mesh);
            }
            root.Free();
        }
        _meshes[model] = list;
        return list;
    }

    public static Aabb bounds(string model, int variant) => meshes(model)[variant].GetAabb();

    static readonly Vector2[] TRAIL = System.Linq.Enumerable.ToArray(TerrainField.TRAIL);

    /// Where the outcrop's rock may stand: on the knob, clear of the steps, the
    /// paths, the trail and its post, the bench and its footstone, and below the
    /// seated sight line to the pond's near shore.
    static bool clear(TerrainField field, Vector2 at, double reach, double top, double path_margin)
    {
        if (PlacesLayout.rock_rise(at) < PlacesLayout.ROCK_RISE * 0.1) return false;
        if (PlacesLayout.distance_to(at, TRAIL, out _) < reach + 4.0 || at.DistanceTo(PlacesLayout.TRAILHEAD_POST) < reach + 5.0) return false;
        double keep = reach + path_margin;
        if (PlacesLayout.distance_to(at, PlacesLayout.STEP_LINE, out _) < keep) return false;
        if (PlacesLayout.distance_to(at, PlacesLayout.PAINTED_PATH, out _) < keep) return false;
        if (PlacesLayout.distance_to(at, PlacesLayout.TOMS_ROUTE, out _) < keep) return false;
        if (at.DistanceTo(PlacesLayout.BENCH) < reach + 1.6 || at.DistanceTo(PlacesLayout.LOOSE_STEP) < reach + 1.4) return false;
        if (PlacesLayout.lookout_openness(at) > 0.0)
        {
            double eye = field.height(PlacesLayout.BENCH.X, PlacesLayout.BENCH.Y) + PlacesLayout.SEATED_EYE;
            double d = at.DistanceTo(PlacesLayout.BENCH);
            double shore = PlacesLayout.BENCH.DistanceTo(TerrainField.POND_CENTRE) - TerrainField.POND_RADIUS;
            double line = eye - (eye - TerrainField.WATER_LEVEL) * d / shore;
            if (top > line - 0.25) return false;
        }
        return true;
    }

    /// A scan at `at`, its long axis along the contour (turned by `yaw`), tipped
    /// `align` of the way to the slope, scaled by `scale` (and `squash` in height),
    /// and sunk so that `bury` of its height is in the ground all round.
    public static Piece place(TerrainField field, string model, int variant, Vector2 at, double scale, double squash, double yaw, double align, double bury)
    {
        Aabb b = bounds(model, variant);
        Vector2 radial = at.DistanceTo(PlacesLayout.TOMS_ROCK) > 0.3f ? (at - PlacesLayout.TOMS_ROCK).Normalized() : Vector2.Right;
        Vector3 tangent = new(-radial.Y, 0, radial.X);
        Vector3 up = Vector3.Up.Lerp(field.normal(at.X, at.Y, 1.5), (float)align).Normalized();
        Vector3 along = (tangent - up * tangent.Dot(up)).Normalized().Rotated(up, (float)yaw);
        // The scan's longer horizontal axis lies along the contour.
        bool long_z = b.Size.Z > b.Size.X;
        Vector3 x_axis = long_z ? along.Cross(up).Normalized() : along;
        Vector3 z_axis = x_axis.Cross(up).Normalized();
        Basis basis = new Basis(x_axis, up, z_axis) * Basis.FromScale(new Vector3((float)scale, (float)(scale * squash), (float)scale));
        Vector3 c = b.GetCenter();
        // The scan's middle stands over `at`.
        Vector3 centre_offset = basis * new Vector3(c.X, 0, c.Z);
        float ox = at.X - centre_offset.X, oz = at.Y - centre_offset.Z;
        double origin_y = double.PositiveInfinity;
        for (int k = 0; k < 20; k++)
        {
            double a = k / 20.0 * Math.Tau;
            Vector3 local = new((float)(c.X + Math.Cos(a) * b.Size.X * 0.42), (float)(b.Position.Y + bury * b.Size.Y), (float)(c.Z + Math.Sin(a) * b.Size.Z * 0.42));
            Vector3 w = basis * local;
            origin_y = Math.Min(origin_y, field.height(ox + w.X, oz + w.Z) - w.Y);
        }
        Vector3 origin = new(ox, (float)origin_y, oz);
        double reach = Math.Max(b.Size.X, b.Size.Z) * 0.5 * scale;
        return new Piece { model = model, variant = variant, pose = new Transform3D(basis, origin), centre = at, reach = reach, bury = bury };
    }

    public static double top(Piece p)
    {
        Aabb b = bounds(p.model, p.variant);
        double best = double.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.GetEndpoint(i);
            best = Math.Max(best, (p.pose * corner).Y);
        }
        return best;
    }

    /// The outcrop's rock, the big masses first.
    public static List<Piece> pieces(TerrainField field)
    {
        var rng = new RandomNumberGenerator { Seed = 1971 };
        var list = new List<Piece>();
        // Rings out from the summit: big rounded masses high on the knob, smaller
        // ones and boulders towards its foot.
        (double r, double step, double s0, double s1, string model)[] rings =
        {
            (4.4, 3.4, 2.2, 3.0, "rock_moss_set_01"), (6.8, 3.6, 2.4, 3.2, "rock_moss_set_01"), (9.2, 3.8, 2.3, 3.1, "rock_moss_set_01"),
            (11.6, 4.0, 2.1, 2.9, "rock_moss_set_01"), (14.0, 4.2, 1.9, 2.7, "rock_moss_set_01"),
            (16.4, 4.4, 1.7, 2.5, "rock_moss_set_02"), (18.8, 4.8, 1.5, 2.3, "rock_moss_set_02"),
        };
        foreach (var ring in rings)
        {
            int count = Math.Max(4, (int)(Math.Tau * ring.r / ring.step));
            double offset = rng.Randf() * 360.0;
            for (int i = 0; i < count; i++)
            {
                double bearing = offset + (i + rng.RandfRange(-0.3f, 0.3f)) * 360.0 / count;
                Vector2 at = PlacesLayout.polar(ring.r + rng.RandfRange(-0.8f, 0.8f), bearing);
                string model = ring.model;
                if (rng.Randf() < 0.16) model = "boulder_01";
                int variant = (int)(rng.Randi() % (uint)meshes(model).Count);
                double scale = rng.RandfRange((float)ring.s0, (float)ring.s1) * (model == "boulder_01" ? 1.2 : 1.0);
                double squash = rng.RandfRange(0.72f, 0.95f);
                double yaw = rng.RandfRange(-0.4f, 0.4f);
                double bury = rng.RandfRange(0.28f, 0.45f);
                Piece p = place(field, model, variant, at, scale, squash, yaw, 0.7, bury);
                p.collide = true;
                if (!clear(field, at, p.reach * 0.75, top(p), 0.6)) continue;
                bool crowded = false;
                foreach (Piece q in list)
                {
                    if (q.centre.DistanceTo(at) < (q.reach + p.reach) * 0.42) { crowded = true; break; }
                }
                if (crowded) continue;
                list.Add(p);
            }
        }
        // The summit's front edge, where the knob falls away under the bench's
        // view: broad, low rock worn nearly flat, kept below the seated sight line.
        for (int i = 0; i < 14; i++)
        {
            Vector2 at = PlacesLayout.polar(rng.RandfRange(2.8f, 6.0f), PlacesLayout.BENCH_BEARING_DEG + rng.RandfRange(-60.0f, 60.0f));
            string model = rng.Randf() < 0.5 ? "rock_moss_set_01" : "rock_moss_set_02";
            int variant = (int)(rng.Randi() % (uint)meshes(model).Count);
            Piece p = place(field, model, variant, at, rng.RandfRange(1.3f, 1.9f), rng.RandfRange(0.22f, 0.32f), rng.RandfRange(-0.5f, 0.5f), 0.9, 0.5);
            p.collide = true;
            if (!clear(field, at, p.reach * 0.75, top(p), 0.6)) continue;
            bool crowded = false;
            foreach (Piece q in list)
            {
                if (q.centre.DistanceTo(at) < (q.reach + p.reach) * 0.42) { crowded = true; break; }
            }
            if (crowded) continue;
            list.Add(p);
        }
        // The flight is bedded into the rock: a stone or a mossy rock at the ends of
        // most steps, where soil and moss have gathered in the joint.
        List<TomsRock.Slab> steps = TomsRock.set_steps(field);
        for (int k = 0; k < steps.Count; k++)
        {
            TomsRock.Slab s = steps[k];
            foreach (int side in new[] { -1, 1 })
            {
                if (rng.Randf() < 0.15) continue;
                bool moss = rng.Randf() < 0.45;
                string model = moss ? "rock_moss_set_02" : (rng.Randf() < 0.5 ? "rock_07" : "rock_09");
                int variant = (int)(rng.Randi() % (uint)meshes(model).Count);
                Aabb b = bounds(model, variant);
                double size = Math.Max(b.Size.X, b.Size.Z);
                double want = moss ? rng.RandfRange(0.55f, 0.9f) : rng.RandfRange(0.45f, 0.8f);
                double scale = want / size;
                double out_by = s.size.X * 0.5 + want * 0.38 + 0.04;
                Vector3 w = s.xform * new Vector3((float)(side * out_by), 0, rng.RandfRange(-0.12f, 0.12f));
                Vector2 at = new(w.X, w.Z);
                Piece p = place(field, model, variant, at, scale, rng.RandfRange(0.7f, 1.0f), rng.RandfRange(-1.0f, 1.0f), 0.6, rng.RandfRange(0.35f, 0.5f));
                list.Add(p);
            }
        }
        return list;
    }

    static readonly Dictionary<Mesh, Material> _granite = new();
    /// Granite's cool grey: the scans' own maps, their warm cast taken down.
    public static readonly Color GRANITE_TINT = new(0.80f, 0.82f, 0.86f);

    /// The scan's material, tinted for the knob's granite (a copy: the scattered
    /// dressing keeps the original).
    static Material granite(Mesh mesh)
    {
        if (_granite.TryGetValue(mesh, out Material found)) return found;
        Material mat = mesh.SurfaceGetMaterial(0);
        if (mat is BaseMaterial3D source)
        {
            var copy = (BaseMaterial3D)source.Duplicate();
            copy.AlbedoColor = source.AlbedoColor * GRANITE_TINT;
            mat = copy;
        }
        _granite[mesh] = mat;
        return mat;
    }

    /// Builds the pieces under `holder`, with convex collision for the masses.
    public static void build(Node3D holder, TerrainField field)
    {
        int i = 0;
        foreach (Piece p in pieces(field))
        {
            Mesh mesh = meshes(p.model)[p.variant];
            holder.AddChild(new MeshInstance3D
            {
                Name = $"Rock_{i}", Mesh = mesh, Transform = p.pose, MaterialOverride = granite(mesh),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static,
                VisibilityRangeEnd = p.collide ? 320.0f : 70.0f,
            });
            if (p.collide)
            {
                var body = new StaticBody3D { Name = $"RockBody_{i}", CollisionLayer = 1, CollisionMask = 0, Position = p.pose.Origin };
                body.SetMeta("surface", (StringName)"rock");
                var shape = (ConvexPolygonShape3D)mesh.CreateConvexShape(true, true);
                Vector3[] points = shape.Points;
                for (int k = 0; k < points.Length; k++) points[k] = p.pose.Basis * points[k];
                body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } });
                holder.AddChild(body);
            }
            i++;
        }
    }
}
