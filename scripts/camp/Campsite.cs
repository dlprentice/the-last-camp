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

/// Hand-authored camp props: tent, dock, lanterns, seats, woodpile. The firepit
/// is owned here so the rest of the clearing can sit relative to it.
public partial class Campsite : Node3D
{
    public TerrainField field;
    public Firepit firepit;
    public Godot.Collections.Array<CampLantern> lanterns = new Godot.Collections.Array<CampLantern>();
    public Interactable woodpile;
    public Dock dock;
    public Tent tent;
    public CampKitchen kitchen;

    public ShaderMaterial _bark;
    public ShaderMaterial _bark_dry;
    public ShaderMaterial _end_grain;

    public Campsite(TerrainField p_field)
    {
        field = p_field;
        Name = "Campsite";
    }

    public Campsite()
    {
    }

    public void build()
    {
        _bark = PropMaterials.bark("bark_oak", 0.08);
        _bark_dry = PropMaterials.bark("bark_oak", 0.02);
        _end_grain = PropMaterials.wood(new Color(0.95f, 0.85f, 0.66f), 0.15, 0.0, 1.0);
        firepit = new Firepit(field);
        AddChild(firepit);
        firepit.build();
        _build_seats();
        _build_woodpile();
        _build_tent();
        _build_dock();
        kitchen = new CampKitchen(field);
        AddChild(kitchen);
        kitchen.build();
        _place_lanterns();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Campsite.MethodName._on_quality));
        _on_quality(Quality.Instance.current);
    }

    public void _on_quality(QualityPreset p)
    {
        foreach (CampLantern lantern in lanterns)
        {
            if (lantern.light != null)
            {
                lantern.light.ShadowEnabled = p.lantern_shadows;
            }
        }
    }

    public void _build_seats()
    {
        // ------------------------------------------------------------------- seats
        for (long i = 0, i_end = (long)TerrainField.SEATS.Count; i < i_end; i++)
        {
            Vector2 pos = TerrainField.SEATS[(int)i];
            // The long local X axis follows the circle; local -Z faces the hearth.
            double yaw = atan2((double)pos.X - TerrainField.FIRE.X, (double)pos.Y - TerrainField.FIRE.Y);
            Node3D root = new Node3D();
            root.Name = G.format("SplitLogBench_%d", i);
            root.Position = new Vector3(pos.X, (float)field.height(pos.X, pos.Y), pos.Y);
            Vector3 _t1 = root.Rotation;
            _t1.Y = (float)yaw;
            root.Rotation = _t1;
            AddChild(root);
            double seat_y = 0.375;
            // Each support starts just under the local earth and meets the actual
            // underside of the half-round seat, including on a sloping pad edge.
            foreach (Variant x_item in new Godot.Collections.Array { -0.62, 0.62 })
            {
                double x = x_item.AsDouble();
                Vector3 at = root.ToGlobal(new Vector3((float)x, 0, 0));
                double foot_y = field.height(at.X, at.Z) - root.GlobalPosition.Y - 0.015;
                double support_h = seat_y - 0.20 * 0.82 - foot_y;
                MeshInstance3D support = FieldKit.add(root, PropMeshes.stump_mesh(0.12, support_h, 807 + i), null, new Vector3((float)x, (float)foot_y, 0), "BenchFoot");
                support.Mesh.SurfaceSetMaterial(0, _bark);
                support.Mesh.SurfaceSetMaterial(1, _end_grain);
            }
            MeshInstance3D seat = FieldKit.add(root, PropMeshes.bark_log_mesh(1.85, 0.20, 800 + i, 0.0, 0.40), null, new Vector3(0, (float)seat_y, 0), "HewnSeat");
            seat.Mesh.SurfaceSetMaterial(0, _bark);
            seat.Mesh.SurfaceSetMaterial(1, _end_grain);
            seat.GIMode = GeometryInstance3D.GIModeEnum.Static;
            _box_collider(root.Position + new Vector3(0, 0.23f, 0), new Vector3(1.85f, 0.46f, 0.40f), yaw, "wood");
        }
    }

    public void _build_woodpile()
    {
        // ---------------------------------------------------------------- woodpile
        Vector2 origin = TerrainField.WOODPILE;
        double y = field.height(origin.X, origin.Y);
        Node3D root = new Node3D();
        root.Name = "Woodpile";
        root.Position = new Vector3(origin.X, (float)y, origin.Y);
        Vector3 _t1 = root.Rotation;
        _t1.Y = (float)TerrainField.WOODPILE_YAW;
        root.Rotation = _t1;
        AddChild(root);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(707));
        Godot.Collections.Array<Godot.Collections.Dictionary> supports = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        Callable ground = Callable.From((Vector2 at) =>
{
    Vector3 world = root.ToGlobal(new Vector3(at.X, 0.0f, at.Y));
    return field.surface_height(world.X, world.Z) - root.GlobalPosition.Y;
});
        // Narrowing, staggered courses of hand-split pieces. Heights come from the
        // actual triangles below each piece, not a common radius or row spacing.
        for (long row = 0; row < 4; row++)
        {
            long count = 4 - row;
            for (long col = 0; col < count; col++)
            {
                MeshInstance3D log = new MeshInstance3D();
                log.Name = G.format("StackedFirewood_%d_%d", new Godot.Collections.Array { row, col });
                double length = rng.RandfRange(0.43f, 0.54f);
                log.Mesh = PropMeshes.split_log_mesh(length, rng.RandfRange(0.097f, 0.108f), 820 + row * 10 + col);
                log.Mesh.SurfaceSetMaterial(0, _bark_dry);
                log.Mesh.SurfaceSetMaterial(1, _end_grain);
                double x = ((double)col - (double)(count - 1) * 0.5) * 0.22 + rng.RandfRange(-0.004f, 0.004f);
                double roll = PI + rng.RandfRange(-0.14f, 0.14f);
                // The uppermost split shows pale fractured wood without laying a
                // flat timber shelf across an entire course.
                if (row == 3)
                {
                    roll = rng.RandfRange(-0.35f, 0.35f);
                }
                Basis basis = new Basis(Vector3.Up, (float)(PI * 0.5 + rng.RandfRange(-0.03f, 0.03f))) * new Basis(Vector3.Right, (float)roll);
                Transform3D pose = new Transform3D(basis, new Vector3((float)x, 0.0f, rng.RandfRange(-0.045f, 0.045f)));
                log.Transform = _settle_firewood(log.Mesh, pose, supports, ground);
                supports.AddRange(_firewood_faces(log.Mesh, log.Transform));
                log.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                root.AddChild(log);
            }
        }
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            MeshInstance3D stake = new MeshInstance3D();
            MeshBuilder mb = new MeshBuilder();
            PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { new Vector3(0.0f, -0.15f, 0.0f), new Vector3(0.0f, 0.62f, 0.0f) }, new Godot.Collections.Array<double> { 0.028, 0.022 }, 6, 1, Colors.White, 1.2);
            stake.Mesh = mb.commit(null, true);
            stake.MaterialOverride = PropMaterials.wood(new Color(0.55f, 0.45f, 0.33f), 0.5, 0.0, 1.0);
            stake.Position = new Vector3((float)(side * 0.46), 0.0f, 0.0f);
            Vector3 _t2 = stake.Rotation;
            _t2.Z = (float)(-side * 0.06);
            stake.Rotation = _t2;
            stake.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            root.AddChild(stake);
        }
        for (long i2 = 0; i2 < 3; i2++)
        {
            MeshInstance3D log2 = new MeshInstance3D();
            log2.Name = G.format("LooseFirewood_%d", i2);
            log2.Mesh = PropMeshes.split_log_mesh(rng.RandfRange(0.4f, 0.5f), rng.RandfRange(0.085f, 0.098f), 870 + i2);
            log2.Mesh.SurfaceSetMaterial(0, _bark_dry);
            log2.Mesh.SurfaceSetMaterial(1, _end_grain);
            Basis basis2 = new Basis(Vector3.Up, (float)(rng.Randf() * TAU)) * new Basis(Vector3.Right, (float)(PI + rng.RandfRange(-0.3f, 0.3f)));
            Transform3D pose2 = new Transform3D(basis2, new Vector3((float)(-0.37 + (double)i2 * 0.31), 0.0f, (float)(0.46 + rng.RandfRange(0.0f, 0.12f))));
            log2.Transform = _settle_firewood(log2.Mesh, pose2, supports, ground);
            supports.AddRange(_firewood_faces(log2.Mesh, log2.Transform));
            log2.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            root.AddChild(log2);
        }
        // Chopping block with the axe sunk into it.

        MeshInstance3D stump = new MeshInstance3D();
        stump.Name = "ChoppingBlock";
        stump.Mesh = PropMeshes.stump_mesh(0.2, 0.42, 880);
        stump.Mesh.SurfaceSetMaterial(0, _bark_dry);
        stump.Mesh.SurfaceSetMaterial(1, _end_grain);
        stump.Position = new Vector3(0.95f, 0.0f, 0.5f);
        Vector3 _t3 = stump.Rotation;
        _t3.Y = 1.1f;
        stump.Rotation = _t3;
        stump.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        stump.GIMode = GeometryInstance3D.GIModeEnum.Static;
        root.AddChild(stump);
        MeshInstance3D axe = new MeshInstance3D();
        axe.Name = "Axe";
        axe.Mesh = PropMeshes.axe_mesh();
        axe.Mesh.SurfaceSetMaterial(0, PropMaterials.wood(new Color(0.9f, 0.78f, 0.6f), 0.1, 0.0, 0.9));
        StandardMaterial3D forged = FieldKit.solid(new Color(0.37f, 0.39f, 0.40f), 0.42, 0.84);
        forged.VertexColorUseAsAlbedo = true;
        axe.Mesh.SurfaceSetMaterial(1, forged);
        axe.Position = new Vector3(0.95f, 0.4f, 0.5f);
        axe.Rotation = new Vector3((float)deg_to_rad(-62.0), 0.8f, 0.0f);
        axe.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        root.AddChild(axe);
        _box_collider(root.Position + new Vector3(0.95f, 0.21f, 0.5f).Rotated(Vector3.Up, root.Rotation.Y), new Vector3(0.42f, 0.42f, 0.42f), root.Rotation.Y, "wood");

        woodpile = new Interactable();
        woodpile.Name = "WoodpileBody";
        woodpile.CollisionLayer = unchecked((uint)(1 | 1L << 1));
        woodpile.CollisionMask = unchecked((uint)(0));
        woodpile.SetMeta("surface", (StringName)"wood");
        woodpile.prompt_text = "Take a log";
        woodpile.on_interact = new Callable(this, Campsite.MethodName._take_log);
        CollisionShape3D shape = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = new Vector3(1.0f, 0.55f, 0.5f);
        shape.Shape = box;
        Vector3 _t4 = shape.Position;
        _t4.Y = 0.28f;
        shape.Position = _t4;
        woodpile.AddChild(shape);
        root.AddChild(woodpile);
    }

    public static Transform3D _settle_firewood(Mesh mesh, Transform3D pose, Godot.Collections.Array<Godot.Collections.Dictionary> supports, Callable ground)
    {
        /// Static prop assembly only: vertically lower a piece until its actual
        /// surface touches the terrain or another split piece. A 1.5 mm seating inset
        /// closes subpixel contact cracks without burying the wood's profile.
        double lift = -INF;
        List<Vector3> vertices = new List<Vector3>(mesh.GetFaces());
        for (long i = 0, i_end = (long)vertices.Count; i < i_end; i += 3)
        {
            Vector3 a = pose * vertices[(int)i];
            Vector3 b = pose * vertices[(int)(i + 1)];
            Vector3 c = pose * vertices[(int)(i + 2)];
            foreach (Variant point_item in new Godot.Collections.Array { a, b, c, (a + b) * 0.5f, (b + c) * 0.5f, (c + a) * 0.5f, (a + b + c) / 3.0f })
            {
                Vector3 point = point_item.AsVector3();
                lift = maxf(lift, G.to_float(ground.Call(new Vector2(point.X, point.Z))) - point.Y);
            }
        }
        foreach (Godot.Collections.Dictionary upper in _firewood_faces(mesh, pose))
        {
            Rect2 upper_bounds = upper["bounds"].AsRect2();
            List<Vector2> upper_polygon = G.ListFromVariant<Vector2>(upper["polygon"]);
            Vector3 upper_height = upper["height"].AsVector3();
            foreach (Godot.Collections.Dictionary lower in supports)
            {
                if (!upper_bounds.Intersects(lower["bounds"].AsRect2(), true))
                {
                    continue;
                }
                List<Vector2> lower_polygon = G.ListFromVariant<Vector2>(lower["polygon"]);
                Vector3 difference = G.op("-", lower["height"], upper_height).AsVector3();
                // The difference of two triangle planes is affine. Its maximum is
                // at a vertex of their projected intersection, including edge meets.
                foreach (Vector2 point2 in upper_polygon)
                {
                    if (Geometry2D.IsPointInPolygon(point2, lower_polygon.ToArray()))
                    {
                        lift = maxf(lift, difference.Dot(new Vector3(point2.X, point2.Y, 1.0f)));
                    }
                }
                foreach (Vector2 point3 in lower_polygon)
                {
                    if (Geometry2D.IsPointInPolygon(point3, upper_polygon.ToArray()))
                    {
                        lift = maxf(lift, difference.Dot(new Vector3(point3.X, point3.Y, 1.0f)));
                    }
                }
                for (long first = 0; first < 3; first++)
                {
                    for (long second = 0; second < 3; second++)
                    {
                        Variant crossing = Geometry2D.SegmentIntersectsSegment(upper_polygon[(int)first], upper_polygon[(int)((first + 1) % 3)], lower_polygon[(int)second], lower_polygon[(int)((second + 1) % 3)]);
                        if (crossing.VariantType == Variant.Type.Vector2)
                        {
                            lift = maxf(lift, difference.Dot(new Vector3(G.Index(crossing, "x").AsSingle(), G.Index(crossing, "y").AsSingle(), 1.0f)));
                        }
                    }
                }
            }
        }
        pose.Origin.Y = (float)(pose.Origin.Y + (lift - 0.0015));
        return pose;
    }

    public static Godot.Collections.Array<Godot.Collections.Dictionary> _firewood_faces(Mesh mesh, Transform3D pose)
    {
        /// Project triangle surfaces into the pile's XZ plane, keeping the plane
        /// equation so tilted split faces determine the contact height correctly.
        Godot.Collections.Array<Godot.Collections.Dictionary> result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        List<Vector3> vertices = new List<Vector3>(mesh.GetFaces());
        for (long i = 0, i_end = (long)vertices.Count; i < i_end; i += 3)
        {
            Vector3 a = pose * vertices[(int)i];
            Vector3 b = pose * vertices[(int)(i + 1)];
            Vector3 c = pose * vertices[(int)(i + 2)];
            Vector3 normal = (b - a).Cross(c - a);
            if (absf(normal.Y) < 0.00000001)
            {
                continue;
            }
            List<Vector2> polygon = new List<Vector2>(new List<Vector2> { new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(c.X, c.Z) });
            Rect2 bounds = new Rect2(polygon[0], Vector2.Zero).Expand(polygon[1]).Expand(polygon[2]);
            result.Add(new Godot.Collections.Dictionary { { "polygon", Variant.From(polygon.ToArray()) }, { "bounds", bounds }, { "height", new Vector3((float)((double)-normal.X / normal.Y), (float)((double)-normal.Z / normal.Y), (float)((double)normal.Dot(a) / normal.Y)) } });
        }
        return result;
    }

    public void _take_log(Player player)
    {
        if (player != null && player.held_item == "")
        {
            player.held_item = "log";
            if (Game.Instance.audio != null)
            {
                Game.Instance.audio.play_interact("pickup");
            }
        }
    }

    public void _build_tent()
    {
        // --------------------------------------------------------------------- tent
        tent = new Tent(field);
        AddChild(tent);
        tent.build();
        lanterns.Add(tent.lantern);
    }

    public void _build_dock()
    {
        // --------------------------------------------------------------------- dock
        dock = new Dock(field);
        AddChild(dock);
        dock.build();
        lanterns.Add(dock.lantern);
    }

    public void _place_lanterns()
    {
        // ---------------------------------------------------------------- lanterns
        // One post at the edge of the trail where you arrive, one beside the tent
        // door; both hold their arm out over the path or doorway.
        Godot.Collections.Array posts = new Godot.Collections.Array { new Godot.Collections.Dictionary { { (StringName)"at", new Vector2(3.7f, 13.2f) }, { (StringName)"face", new Vector2(1.4f, 12.6f) } }, new Godot.Collections.Dictionary { { (StringName)"at", TerrainField.TENT + new Vector2(-2.6f, 2.4f) }, { (StringName)"face", TerrainField.TENT + new Vector2(-0.8f, 1.0f) } } };
        foreach (Variant post_item in posts)
        {
            Godot.Collections.Dictionary post = post_item.AsGodotDictionary();
            Vector2 pos = post["at"].AsVector2();
            Vector2 face = post["face"].AsVector2();
            CampLantern lantern = new CampLantern();
            lantern.Position = new Vector3(pos.X, (float)field.height(pos.X, pos.Y), pos.Y);
            // The hook arm extends along local +X: yaw it towards `face`.
            Vector3 _t1 = lantern.Rotation;
            _t1.Y = (float)atan2(-((double)face.Y - pos.Y), (double)face.X - pos.X);
            lantern.Rotation = _t1;
            AddChild(lantern);
            lantern.build(true, Quality.Instance.current.lantern_shadows);
            lanterns.Add(lantern);
        }
    }

    public void _box_collider(Vector3 origin, Vector3 size, double yaw, StringName surface)
    {
        // ---------------------------------------------------------------- helpers
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)surface);
        CollisionShape3D shape = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = size;
        shape.Shape = box;
        body.AddChild(shape);
        body.Position = origin;
        Vector3 _t1 = body.Rotation;
        _t1.Y = (float)yaw;
        body.Rotation = _t1;
        AddChild(body);
    }
}
