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

/// A small timber jetty from the beach out over the pond: driven piles with
/// rope-wrapped heads, bearers and stringers, a deck of individually varied
/// planks, an iron cleat with a mooring line to the canoe, and the lantern at
/// the end. Local frame: the node sits at the deck's inland end with -Z along
/// the dock, +X to the right and the deck's top face at y = 0.
public partial class Dock : Node3D
{
    public const double LENGTH = 8.0;
    public const double INLAND = 0.9;
    public const double WIDTH = 1.6;
    public const double DECK_ABOVE_WATER = 0.42;
    public const double PILE_SPACING = 2.1;
    public const double PILE_RADIUS = 0.085;
    public const double PILE_ABOVE_DECK = 0.62;
    public const double PLANK_THICKNESS = 0.042;
    public static readonly Vector2 STRINGER_SIZE = new Vector2(0.11f, 0.17f);
    public const double BEARER_SIZE = 0.12;

    public TerrainField field;
    public CampLantern lantern;
    public Canoe canoe;
    public double deck_y = 0.0;
    public Godot.Collections.Array<Vector3> _piles = new Godot.Collections.Array<Vector3>();
    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public double _rope_clock = 0.0;

    public Dock(TerrainField p_field)
    {
        field = p_field;
        Name = "Dock";
        _rng.Seed = unchecked((ulong)(2024));
    }

    public Dock()
    {
    }

    public double total_length()
    {
        return LENGTH + INLAND;
    }

    public void build()
    {
        Vector2 start = TerrainField.DOCK_START;
        Vector2 dir2 = (TerrainField.POND_CENTRE - start).Normalized();
        Vector3 forward = new Vector3(dir2.X, 0.0f, dir2.Y);
        deck_y = TerrainField.WATER_LEVEL + DECK_ABOVE_WATER;
        Position = new Vector3(start.X, (float)deck_y, start.Y) - forward * (float)INLAND;
        Basis = Basis.LookingAt(forward, Vector3.Up);

        ShaderMaterial wood = PropMaterials.wood(new Color(0.86f, 0.74f, 0.56f), 0.55, 0.7, 1.0);
        ShaderMaterial pile_wood = PropMaterials.wood(new Color(0.62f, 0.5f, 0.36f), 0.4, 0.85, 1.0);
        _add_mesh("Deck", _deck_mesh(), wood);
        _add_mesh("Piles", _pile_mesh(), pile_wood);
        _add_mesh("Rope", _rope_mesh(), PropMaterials.rope());
        _add_mesh("Cleat", _cleat_mesh(), PropMaterials.iron());
        _add_collision();
        _add_lantern();
        _moor_canoe();
        _add_mesh("Mooring", _mooring_mesh(), PropMaterials.rope());
        _add_skipping_stones();
    }

    public void _add_skipping_stones()
    {
        Interactable stones = new Interactable();
        stones.Name = "SkippingStones";
        stones.Position = new Vector3(-0.38f, 0.06f, (float)(-total_length() + 1.15));
        stones.CollisionLayer = unchecked((uint)(1L << 1));
        stones.CollisionMask = unchecked((uint)(0));
        stones.prompt_text = "Skip a stone across the pond";
        stones.on_interact = Callable.From((Node _player) =>
{
    if (Game.Instance.camp != null && Game.Instance.camp.pond != null)
    {
        Vector3 origin = ToGlobal(new Vector3(0.0f, 0.9f, (float)(-total_length() - 0.2)));
        Game.Instance.camp.pond.skip_stone(origin, -GlobalBasis.Z);
    }
});
        CollisionShape3D collider = new CollisionShape3D();
        SphereShape3D shape = new SphereShape3D();
        shape.Radius = 0.28f;
        collider.Shape = shape;
        stones.AddChild(collider);
        ShaderMaterial stone_material = PropMaterials.triplanar("rock", new Color(0.74f, 0.76f, 0.72f), 3.0);
        stone_material.SetShaderParameter("normal_strength", 0.30);
        stone_material.SetShaderParameter("wet_band", 0.08);
        TriangleMesh deck_surface = GetNode<MeshInstance3D>("Deck").Mesh.GenerateTriangleMesh();
        // A handful set down loosely, with room between the stones. Their contact
        // follows the individual tilted/raised planks, not the ideal deck height.
        Godot.Collections.Array placements = new Godot.Collections.Array { new Vector3(-0.072f, 0, 0.090f), new Vector3(0.055f, 0, 0.139f), new Vector3(0.103f, 0, -0.003f), new Vector3(-0.060f, 0, -0.066f), new Vector3(0.049f, 0, -0.150f) };
        Godot.Collections.Array radii = new Godot.Collections.Array { 0.042, 0.055, 0.050, 0.045, 0.039 };
        Godot.Collections.Array angles = new Godot.Collections.Array { 0.4, 2.1, -0.8, 1.1, -2.3 };
        for (long i = 0; i < 5; i++)
        {
            MeshInstance3D pebble = new MeshInstance3D();
            pebble.Name = G.format("Stone%d", i + 1);
            pebble.Mesh = PropMeshes.rock(610 + i, radii[(int)i].AsDouble());
            pebble.Position = placements[(int)i].AsVector3();
            Vector3 centre = stones.Position + pebble.Position;
            Godot.Collections.Dictionary contact = deck_surface.IntersectRay(new Vector3(centre.X, 0.25f, centre.Z), Vector3.Down);
            Vector3 normal = G.get(contact, "normal", Vector3.Up).AsVector3();
            if (normal.Y < 0.0)
            {
                normal = -normal;
            }
            pebble.Basis = new Basis(new Quaternion(Vector3.Up, normal)) * new Basis(Vector3.Up, angles[(int)i].AsSingle()) * Basis.FromScale(new Vector3(1.0f, (float)(0.31 + 0.02 * i), 0.87f));
            double support_y = -INF;
            foreach (Variant vertex_item in G.Iter(pebble.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]))
            {
                Vector3 vertex = vertex_item.AsVector3();
                Vector3 relative = pebble.Basis * vertex;
                Vector3 sample = centre + relative;
                Godot.Collections.Dictionary hit = deck_surface.IntersectRay(new Vector3(sample.X, 0.25f, sample.Z), Vector3.Down);
                if (!(hit.Count == 0))
                {
                    support_y = maxf(support_y, G.op("-", G.Index(hit["position"], "y"), relative.Y).AsDouble());
                }
            }
            G.assert(is_finite(support_y), "Skipping stones must have a deck beneath them");
            Vector3 _t1 = pebble.Position;
            _t1.Y = (float)(support_y - stones.Position.Y + 0.00015);
            pebble.Position = _t1;
            pebble.MaterialOverride = stone_material;
            stones.AddChild(pebble);
        }
        AddChild(stones);
    }

    public MeshInstance3D _add_mesh(string node_name, ArrayMesh mesh, Material material)
    {
        MeshInstance3D mi = new MeshInstance3D();
        mi.Name = node_name;
        mi.Mesh = mesh;
        mi.MaterialOverride = material;
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mi.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(mi);
        return mi;
    }

    public List<float> _pile_stations()
    {
        /// Pile stations along the dock (local z, negative outwards).
        List<float> stations = new List<float>();
        double z = -0.45;
        while (z > -total_length() + 0.3)
        {
            stations.Add((float)z);
            z -= PILE_SPACING;
        }
        stations.Add((float)(-total_length() + 0.3));
        return stations;
    }

    public double _ground_local(Vector3 local)
    {
        Vector3 world = ToGlobal(local);
        return field.height_fast(world.X, world.Z) - deck_y;
    }

    public ArrayMesh _deck_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        double length = total_length();
        // Bearers across the piles, stringers along them, planks on top.
        double bearer_y = -PLANK_THICKNESS - STRINGER_SIZE.Y - BEARER_SIZE * 0.5;
        foreach (float z in _pile_stations())
        {
            Transform3D xf = new Transform3D(new Basis(Vector3.Up, (float)(PI * 0.5)), new Vector3(0.0f, (float)bearer_y, z));
            PropMeshes.add_board(mb, xf, new Vector3((float)BEARER_SIZE, (float)BEARER_SIZE, (float)(WIDTH + 0.16)), (long)_rng.Randi() % 4, _rng.Randf(), new Color(0.8f, 0.74f, 0.66f));
        }
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            double x = side * (WIDTH * 0.5 - 0.22);
            Transform3D xf2 = new Transform3D(Basis.Identity, new Vector3((float)x, (float)(-PLANK_THICKNESS - STRINGER_SIZE.Y * 0.5), (float)(-length * 0.5)));
            PropMeshes.add_board(mb, xf2, new Vector3(STRINGER_SIZE.X, STRINGER_SIZE.Y, (float)length), (long)_rng.Randi() % 4, _rng.Randf(), new Color(0.82f, 0.76f, 0.68f));
        }
        double z2 = 0.0;
        while (z2 > -length)
        {
            double w = _rng.RandfRange(0.14f, 0.19f);
            double gap = _rng.RandfRange(0.012f, 0.03f);
            double centre_z = z2 - w * 0.5;
            if (centre_z - w * 0.5 < -length)
            {
                break;
            }
            double plank_len = WIDTH + _rng.RandfRange(-0.03f, 0.07f);
            Vector3 offset = new Vector3(_rng.RandfRange(-0.02f, 0.02f), 0.0f, (float)centre_z);
            double yaw = deg_to_rad(_rng.RandfRange(-1.0f, 1.0f));
            double tilt = deg_to_rad(_rng.RandfRange(-0.6f, 0.6f));
            // Now and then a board has worked loose and sits a little proud.
            double raised = _rng.Randf() < 0.08 ? 0.01 : 0.0;
            // Boards weather unevenly: some silvered, some still brown.
            double grey = _rng.Randf();
            double shade = _rng.RandfRange(0.78f, 1.05f);
            Color tint = new Color((float)shade, (float)(shade * lerpf(0.95, 1.0, grey)), (float)(shade * lerpf(0.87, 1.0, grey)));
            Basis rot = new Basis(Vector3.Up, (float)(PI * 0.5 + yaw)).Rotated(Vector3.Forward, (float)tilt);
            Transform3D xf3 = new Transform3D(rot, offset + new Vector3(0.0f, (float)(raised - PLANK_THICKNESS * 0.5), 0.0f));
            PropMeshes.add_board(mb, xf3, new Vector3((float)w, (float)PLANK_THICKNESS, (float)plank_len), (long)_rng.Randi() % 4, _rng.Randf(), tint);
            // Two nails over each stringer, heads just proud of the surface.
            foreach (Variant side_item2 in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double side2 = side_item2.AsDouble();
                double x2 = side2 * (WIDTH * 0.5 - 0.22);
                foreach (Variant k_item in new Godot.Collections.Array { -1.0, 1.0 })
                {
                    double k = k_item.AsDouble();
                    Vector3 head = new Vector3((float)x2, (float)(raised + 0.0025), (float)(centre_z + k * w * 0.25));
                    mb.add_tube(new Godot.Collections.Array<Vector3> { head + new Vector3(0.0f, -0.006f, 0.0f), head }, new Godot.Collections.Array<double> { 0.0045, 0.0055 }, 6, new Color(0.22f, 0.2f, 0.18f), 1.0, 1.0, 0.0, true);
                }
            }
            z2 -= w + gap;
        }
        return mb.commit(null, true);
    }

    public ArrayMesh _pile_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        _piles.Clear();
        foreach (float z in _pile_stations())
        {
            foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double side = side_item.AsDouble();
                double x = side * (WIDTH * 0.5 - 0.02);
                Vector3 foot = new Vector3((float)x, 0.0f, z);
                double ground = _ground_local(foot);
                Vector3 bottom = new Vector3((float)x, (float)(ground - 0.5), z);
                Vector3 top = new Vector3((float)(x + _rng.RandfRange(-0.03f, 0.03f)), (float)(PILE_ABOVE_DECK + _rng.RandfRange(-0.08f, 0.06f)), (float)((double)z + _rng.RandfRange(-0.03f, 0.03f)));
                double r = PILE_RADIUS * _rng.RandfRange(0.9f, 1.1f);
                PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { bottom, bottom.Lerp(top, 0.5f), top }, new Godot.Collections.Array<double> { r * 1.05, r, r * 0.92 }, 9, (long)_rng.Randi() % 4, new Color(0.96f, 0.94f, 0.9f), 1.4);
                _piles.Add(top);
            }
        }
        return mb.commit(null, true);
    }

    public ArrayMesh _rope_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        // Rope wraps on a few pile heads.
        for (long i = 0, i_end = (long)_piles.Count; i < i_end; i++)
        {
            if (i % 3 != 0 && i != (long)_piles.Count - 1)
            {
                continue;
            }
            Vector3 top = _piles[(int)i];
            PropMeshes.add_rope_coil(mb, top - new Vector3(0.0f, 0.2f, 0.0f), PILE_RADIUS, 4, 0.026);
        }
        return mb.commit();
    }

    public Vector3 _cleat_position()
    {
        return new Vector3((float)(WIDTH * 0.5 - 0.14), 0.0f, (float)(-total_length() + 0.55));
    }

    public ArrayMesh _cleat_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        Vector3 p = _cleat_position();
        mb.add_box(new Vector3(0.05f, 0.06f, 0.08f), Colors.White, 2.0);
        // add_box is centred on the origin; move it into place by offsetting vertices.
        for (long i = 0, i_end = (long)mb.vertices.Count; i < i_end; i++)
        {
            mb.vertices[(int)i] += p + new Vector3(0.0f, 0.03f, 0.0f);
        }
        long first = mb.vertex_count();
        mb.add_box(new Vector3(0.2f, 0.035f, 0.05f), Colors.White, 2.0);
        for (long i2 = first, i_end2 = (long)mb.vertices.Count; i2 < i_end2; i2++)
        {
            mb.vertices[(int)i2] += p + new Vector3(0.0f, 0.075f, 0.0f);
        }
        return mb.commit();
    }

    public void _add_collision()
    {
        StaticBody3D body = new StaticBody3D();
        body.Name = "DeckBody";
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"wood");
        CollisionShape3D deck = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = new Vector3((float)(WIDTH + 0.08), 0.3f, (float)total_length());
        deck.Shape = box;
        deck.Position = new Vector3(0.0f, -0.15f, (float)(-total_length() * 0.5));
        body.AddChild(deck);
        foreach (Vector3 top in _piles)
        {
            CollisionShape3D post = new CollisionShape3D();
            CylinderShape3D cyl = new CylinderShape3D();
            cyl.Radius = (float)(PILE_RADIUS + 0.03);
            cyl.Height = (float)PILE_ABOVE_DECK;
            post.Shape = cyl;
            post.Position = new Vector3(top.X, (float)(PILE_ABOVE_DECK * 0.5), top.Z);
            body.AddChild(post);
        }
        AddChild(body);
    }

    public void _add_lantern()
    {
        lantern = new CampLantern();
        Vector3 top = _piles[(int)((long)_piles.Count - 1)];
        lantern.Position = top + new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 _t1 = lantern.Rotation;
        _t1.Y = (float)(PI * 0.5);
        lantern.Rotation = _t1;
        AddChild(lantern);
        lantern.build(false, Quality.Instance.current.lantern_shadows, true);
    }

    public void _moor_canoe()
    {
        canoe = new Canoe();
        // Moored alongside, parallel to the deck, clear of the piles: half the
        // deck width plus half the beam plus fender room.
        double end_z = -total_length();
        Vector3 local = new Vector3((float)(WIDTH * 0.5 + Canoe.HALF_BEAM + 0.55), 0.0f, (float)(end_z + 2.6));
        Vector3 world = ToGlobal(local);
        world.Y = (float)(TerrainField.WATER_LEVEL - Canoe.DRAFT);
        canoe.Position = world;
        Vector3 _t1 = canoe.Rotation;
        _t1.Y = (float)(Rotation.Y + deg_to_rad(4.0));
        canoe.Rotation = _t1;
        GetParent().AddChild(canoe);
        canoe.build(new Color(1.0f, 0.7f, 0.44f));
    }

    public ArrayMesh _mooring_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        Vector3 from = _cleat_position() + new Vector3(0.0f, 0.09f, 0.0f);
        Vector3 to = ToLocal(canoe.bow_point());
        PropMeshes.add_rope(mb, from, to, 0.22, 0.011, 14);
        return mb.commit();
    }

    public override void _Process(double delta)
    {
        // A small rope mesh follows the physically moving bow; 12 Hz is ample
        // for this slow tether and avoids reallocating it every rendered frame.
        _rope_clock += delta;
        if (_rope_clock < 1.0 / 12.0 || canoe == null || canoe.Freeze)
        {
            return;
        }
        _rope_clock = fmod(_rope_clock, 1.0 / 12.0);
        ((MeshInstance3D)GetNode("Mooring")).Mesh = _mooring_mesh();
    }
}
