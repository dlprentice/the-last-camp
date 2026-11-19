#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// The first place June and Tom camped: a ring of stones half sunk and grassed
/// over, one stone gone and one knocked out of line, and the fallen log they sat
/// on. On the log is Tom's first notch, the start of his route.
public partial class OldFireRing : Node3D
{
    public const int STONES = 12;
    public const int CHARCOAL = 9;
    public const double LOG_LENGTH_PAD = 0.0;
    public const double LOG_SINK = 0.025;
    /// Where on the log Tom cut his first notch, from its south-west end.
    public const double NOTCH_FROM_END = 0.55;

    public TerrainField field;
    public MeshInstance3D log;
    public Vector3 notch_world;
    public Vector3 notch_face;

    public OldFireRing(TerrainField p_field)
    {
        field = p_field;
        Name = "OldFireRing";
    }

    public OldFireRing()
    {
    }

    public struct Stone
    {
        public Transform3D pose;
        public GraniteSlab.Shape shape;
        /// Largest dimension across, metres.
        public double size;
    }

    /// The ring's stones, two fists to a loaf in size, each half sunk where it
    /// was set fifty summers ago; one long gone, one knocked out of line.
    public static List<Stone> stones(TerrainField field)
    {
        var rng = new RandomNumberGenerator { Seed = 1974 };
        var list = new List<Stone>();
        for (int i = 0; i < STONES; i++)
        {
            double a = i / (double)STONES * Math.Tau + rng.RandfRange(-0.1f, 0.1f);
            double across = rng.RandfRange(0.18f, 0.32f);
            Vector3 size = new((float)across, (float)(across * rng.RandfRange(0.55f, 0.75f)), (float)(across * rng.RandfRange(0.7f, 0.95f)));
            double r = PlacesLayout.OLD_RING_RADIUS + rng.RandfRange(-0.05f, 0.05f);
            double yaw = a + Math.PI * 0.5 + rng.RandfRange(-0.4f, 0.4f);
            double roundness = rng.RandfRange(0.38f, 0.55f);
            if (i == 7) continue;                 // long gone
            if (i == 3) r += 0.5;                 // kicked out of line
            Vector2 at = PlacesLayout.OLD_RING + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (float)r;
            double ground = double.PositiveInfinity;
            for (int k = 0; k < 8; k++)
            {
                double b = k / 8.0 * Math.Tau;
                ground = Math.Min(ground, field.height(at.X + Math.Cos(b) * across * 0.45, at.Y + Math.Sin(b) * across * 0.45));
            }
            // Half sunk: the stone's middle sits a little below the lowest ground under it.
            Basis basis = new(Vector3.Up, (float)yaw);
            list.Add(new Stone
            {
                pose = new Transform3D(basis, new Vector3(at.X, (float)(ground - size.Y * 0.08), at.Y)),
                shape = new GraniteSlab.Shape { size = size, roundness = roundness, rough = 0.22, seed = 7400 + i },
                size = across,
            });
        }
        return list;
    }

    /// Old charcoal in the ring's middle: a few lumps, mostly grown over.
    public static List<Transform3D> charcoal(TerrainField field)
    {
        var rng = new RandomNumberGenerator { Seed = 1975 };
        var list = new List<Transform3D>();
        for (int i = 0; i < CHARCOAL; i++)
        {
            double a = rng.Randf() * Math.Tau;
            double r = Math.Sqrt(rng.Randf()) * PlacesLayout.OLD_RING_RADIUS * 0.6;
            Vector2 at = PlacesLayout.OLD_RING + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (float)r;
            float size = rng.RandfRange(0.025f, 0.06f);
            Basis basis = new Basis(new Vector3(rng.Randf(), 1.0f, rng.Randf()).Normalized(), rng.Randf() * (float)Math.Tau).Scaled(new Vector3(size, size * 0.6f, size * 0.8f));
            list.Add(new Transform3D(basis, new Vector3(at.X, (float)(field.height(at.X, at.Y) - size * 0.25), at.Y)));
        }
        return list;
    }

    public static ArrayMesh log_mesh()
    {
        double length = PlacesLayout.OLD_LOG_A.DistanceTo(PlacesLayout.OLD_LOG_B);
        return PropMeshes.bark_log_mesh(length, PlacesLayout.OLD_LOG_RADIUS, 1974, 0.035, 1.0);
    }

    /// The fallen log's pose: along A to B, tipped to the ground's fall, and
    /// settled until no part of its underside stands above the ground.
    public static Transform3D log_pose(TerrainField field, ArrayMesh mesh)
    {
        Vector2 a = PlacesLayout.OLD_LOG_A, b = PlacesLayout.OLD_LOG_B;
        Vector2 dir = (b - a).Normalized();
        double length = a.DistanceTo(b);
        double ga = field.height(a.X, a.Y), gb = field.height(b.X, b.Y);
        Basis basis = new Basis(Vector3.Up, (float)Math.Atan2(-dir.Y, dir.X)) * new Basis(Vector3.Back, (float)Math.Atan2(gb - ga, length));
        Vector2 mid = (a + b) * 0.5f;
        Vector3[] verts = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        double origin_y = double.PositiveInfinity;
        foreach (Vector3 v in verts)
        {
            if (v.Y > -PlacesLayout.OLD_LOG_RADIUS * 0.5) continue;
            Vector3 w = basis * v;
            origin_y = Math.Min(origin_y, field.height(mid.X + w.X, mid.Y + w.Z) - w.Y);
        }
        return new Transform3D(basis, new Vector3(mid.X, (float)(origin_y - LOG_SINK), mid.Y));
    }

    public void build()
    {
        Material granite = GraniteSlab.material();
        int i = 0;
        foreach (Stone s in stones(field))
        {
            ArrayMesh stone = GraniteSlab.build(s.shape, s.pose, field.height);
            AddChild(new MeshInstance3D
            {
                Name = $"RingStone_{i}", Mesh = stone, MaterialOverride = granite, Transform = s.pose,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static,
            });
            var body = new StaticBody3D { Name = $"RingStoneBody_{i}", CollisionLayer = 1, CollisionMask = 0, Position = s.pose.Origin };
            body.SetMeta("surface", (StringName)"rock");
            var hull = GraniteSlab.hull(stone, s.pose.Basis);
            body.AddChild(new CollisionShape3D { Shape = hull });
            GC.KeepAlive(hull);
            AddChild(body);
            i++;
        }
        var coal = FieldKit.solid(new Color(0.035f, 0.032f, 0.03f), 0.95);
        ArrayMesh lump = PropMeshes.rock(7600, 1.0);
        int c = 0;
        foreach (Transform3D coal_pose in charcoal(field))
        {
            AddChild(new MeshInstance3D { Name = $"Charcoal_{c++}", Mesh = lump, MaterialOverride = coal, Transform = coal_pose, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        ArrayMesh mesh = log_mesh();
        Transform3D pose = log_pose(field, mesh);
        ShaderMaterial bark = PropMaterials.bark("bark_oak", 0.42);
        ShaderMaterial grain = PropMaterials.wood(new Color(0.62f, 0.58f, 0.52f), 0.9, 0.0, 1.1);
        mesh.SurfaceSetMaterial(0, bark);
        mesh.SurfaceSetMaterial(1, grain);
        log = new MeshInstance3D { Name = "FallenLog", Mesh = mesh, Transform = pose, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, GIMode = GeometryInstance3D.GIModeEnum.Static };
        AddChild(log);
        var log_body = new StaticBody3D { Name = "FallenLogBody", CollisionLayer = 1, CollisionMask = 0, Transform = pose };
        log_body.SetMeta("surface", (StringName)"wood");
        double length = PlacesLayout.OLD_LOG_A.DistanceTo(PlacesLayout.OLD_LOG_B);
        var shape_125 = new CylinderShape3D { Radius = (float)PlacesLayout.OLD_LOG_RADIUS, Height = (float)length };
        log_body.AddChild(new CollisionShape3D { Shape = shape_125, Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f) });
        GC.KeepAlive(shape_125);
        AddChild(log_body);
        _build_notch(mesh, pose, bark);
    }

    /// Tom's first notch: on the log's outer flank near its south-west end,
    /// turned to someone leaving the ring along his route.
    public void _build_notch(ArrayMesh mesh, Transform3D pose, Material bark)
    {
        double length = PlacesLayout.OLD_LOG_A.DistanceTo(PlacesLayout.OLD_LOG_B);
        Vector3 centre = new((float)(-length * 0.5 + NOTCH_FROM_END), 0.0f, 0.0f);
        Vector2 away2 = (PlacesLayout.TOMS_ROUTE[1] - PlacesLayout.OLD_RING).Normalized();
        Vector3 away = new Vector3(away2.X, 0.55f, away2.Y).Normalized();
        Basis inv = pose.Basis.Inverse();
        ArrayMesh notch = BlazeNotch.build(mesh, 0, centre, inv * away, inv * Vector3.Up, out Vector3 surface);
        if (notch == null)
        {
            GD.PushWarning("Old ring: no log surface for Tom's first notch");
            return;
        }
        var mi = new MeshInstance3D { Name = "FirstNotch", Mesh = notch, Transform = pose, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mi.SetSurfaceOverrideMaterial(0, bark);
        if (notch.GetSurfaceCount() > 1) mi.SetSurfaceOverrideMaterial(1, BlazeNotch.cut_material(false));
        mi.VisibilityRangeEnd = 40.0f;
        AddChild(mi);
        notch_world = pose * surface;
        notch_face = away;
    }
}
