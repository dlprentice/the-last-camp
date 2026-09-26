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

/// A modest, lived-in cooking corner beside the tent. Legs are measured down
/// to the terrain; the tabletop is the single mounting datum for every prop.
public partial class CampKitchen : Node3D
{
    public const double TOP = 0.82;
    public const double CLOTH_TOP = TOP + 0.004;
    public TerrainField field;
    public Godot.Collections.Array<Vector3> feet = new Godot.Collections.Array<Vector3>();
    public SoftBody3D cloth;
    public List<int> cloth_pins = new List<int>();
    public List<int> _cloth_wind_points = new List<int>();
    public List<float> _cloth_wind_phases = new List<float>();
    public double _cloth_time = 0.0;

    public CampKitchen(TerrainField p_field)
    {
        field = p_field;
        Name = "CampKitchen";
    }

    public CampKitchen()
    {
    }

    public void build()
    {
        Position = new Vector3(TerrainField.TABLE.X, (float)field.height(TerrainField.TABLE.X, TerrainField.TABLE.Y), TerrainField.TABLE.Y);
        Vector3 _t1 = Rotation;
        _t1.Y = -0.28f;
        Rotation = _t1;
        ShaderMaterial wood = PropMaterials.wood(new Color(0.66f, 0.57f, 0.43f), 0.48, 0.0, 0.93);
        StandardMaterial3D iron = FieldKit.solid(new Color(0.11f, 0.12f, 0.12f), 0.55, 0.7);
        for (long i = 0; i < 6; i++)
        {
            ArrayMesh plank = FieldKit.rounded_box(new Vector3(1.55f, 0.042f, 0.11f), 0.007);
            // Offset the UVs per board so saw marks and wood grain never repeat.
            Godot.Collections.Array arrays = plank.SurfaceGetArrays(0);
            List<Vector2> uv = G.ListFromVariant<Vector2>(arrays[(int)Mesh.ArrayType.TexUV]);
            for (long j = 0, j_end = (long)uv.Count; j < j_end; j++)
            {
                uv[(int)j] += new Vector2((float)((double)i * 0.271), (float)((double)i * 0.613));
            }
            arrays[(int)Mesh.ArrayType.TexUV] = Variant.From(uv.ToArray());
            ArrayMesh mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            FieldKit.add(this, mesh, wood, new Vector3(0, (float)(TOP - 0.021), (float)(((double)i - 2.5) * 0.115)), "TableBoard");
        }
        MeshBuilder frame = new MeshBuilder();
        foreach (Variant x_item in new Godot.Collections.Array { -0.60, 0.60 })
        {
            double x = x_item.AsDouble();
            foreach (Variant z_item in new Godot.Collections.Array { -0.26, 0.26 })
            {
                double z = z_item.AsDouble();
                Vector3 local_foot = new Vector3((float)(x * 1.07), 0, (float)(z * 1.10));
                Vector3 world = ToGlobal(local_foot);
                local_foot.Y = (float)(field.height(world.X, world.Z) - GlobalPosition.Y - 0.025);
                feet.Add(ToGlobal(local_foot));
                PropMeshes.add_timber(frame, new Godot.Collections.Array<Vector3> { local_foot, new Vector3((float)x, (float)(TOP - 0.035), (float)z) }, new Godot.Collections.Array<double> { 0.034, 0.03 }, 5, 2, Colors.White, 1.2);
            }
            // Flat bearers seat against the board undersides; the legs pass into
            // their ends. Round poles touching at a tangent left visible daylight.
            FieldKit.add(this, FieldKit.rounded_box(new Vector3(0.083f, 0.090f, 0.66f), 0.004), wood, new Vector3((float)x, (float)(TOP - 0.078), 0), "CrossBearer");
        }
        foreach (Variant z_item2 in new Godot.Collections.Array { -0.26, 0.26 })
        {
            double z2 = z_item2.AsDouble();
            FieldKit.add(this, FieldKit.rounded_box(new Vector3(1.26f, 0.105f, 0.047f), 0.004), wood, new Vector3(0, (float)(TOP - 0.090), (float)z2), "TableApron");
        }
        PropMeshes.add_timber(frame, new Godot.Collections.Array<Vector3> { new Vector3(-0.6f, 0.2f, -0.26f), new Vector3(0.6f, 0.66f, -0.26f) }, new Godot.Collections.Array<double> { 0.02, 0.02 }, 4, 1, Colors.White, 1.2);
        PropMeshes.add_timber(frame, new Godot.Collections.Array<Vector3> { new Vector3(-0.6f, 0.66f, 0.26f), new Vector3(0.6f, 0.2f, 0.26f) }, new Godot.Collections.Array<double> { 0.02, 0.02 }, 4, 1, Colors.White, 1.2);
        FieldKit.add(this, frame.commit(), wood, Vector3.Zero, "TrestleFrame");
        MeshBuilder nails = new MeshBuilder();
        foreach (Variant x_item2 in new Godot.Collections.Array { -0.60, 0.60 })
        {
            double x2 = x_item2.AsDouble();
            for (long i6 = 0; i6 < 6; i6++)
            {
                double z3 = ((double)i6 - 2.5) * 0.115;
                nails.add_tube(new Godot.Collections.Array<Vector3> { new Vector3((float)x2, (float)(TOP - 0.002), (float)z3), new Vector3((float)x2, (float)(TOP + 0.001), (float)z3) }, new Godot.Collections.Array<double> { 0.005, 0.005 }, 8, Colors.White, 1, 1, 0, true);
            }
        }
        FieldKit.add(this, nails.commit(), iron, Vector3.Zero, "NailHeads");
        _box(new Vector3(0, 0.41f, 0), new Vector3(1.55f, 0.82f, 0.69f));
        // The tall player blocker has rounded collision corners which sit inside
        // the visible boards. A thin, cloth-only shape follows the actual top.
        StaticBody3D cloth_support = new StaticBody3D();
        cloth_support.Name = "ClothSupport";
        cloth_support.CollisionLayer = unchecked((uint)(1L << 7));
        cloth_support.CollisionMask = unchecked((uint)(0));
        CollisionShape3D support_shape = new CollisionShape3D();
        BoxShape3D top_box = new BoxShape3D();
        top_box.Size = new Vector3(1.554f, 0.042f, 0.689f);
        top_box.Margin = 0.001f;
        support_shape.Shape = top_box;
        Vector3 _t2 = support_shape.Position;
        _t2.Y = (float)(TOP - 0.021);
        support_shape.Position = _t2;
        cloth_support.AddChild(support_shape);
        AddChild(cloth_support);
        _add_tablecloth();
        FieldKit.kettle(this, new Vector3(-0.36f, (float)CLOTH_TOP, -0.02f));
        Node3D mug = FieldKit.mug(this, new Vector3(0.12f, (float)CLOTH_TOP, -0.15f), new Color(0.73f, 0.72f, 0.57f));
        Vector3 _t3 = mug.Rotation;
        _t3.Y = -0.5f;
        mug.Rotation = _t3;
        Vector3 _t4 = FieldKit.mug(this, new Vector3(0.34f, (float)CLOTH_TOP, 0.12f), new Color(0.20f, 0.33f, 0.34f)).Rotation;
        _t4.Y = 1.6f;
        FieldKit.mug(this, new Vector3(0.34f, (float)CLOTH_TOP, 0.12f), new Color(0.20f, 0.33f, 0.34f)).Rotation = _t4;
        _add_journal();
        _add_crate(wood);
    }

    public void _add_tablecloth()
    {
        MeshBuilder mb = new MeshBuilder();
        long NX = 76;
        long NZ = 48;
        for (long j = 0, j_end = NZ + 1; j < j_end; j++)
        {
            for (long i = 0, i_end = NX + 1; i < i_end; i++)
            {
                double u = ((double)i / NX - 0.5) * 1.90;
                double v = ((double)j / NZ - 0.5) * 1.20;
                // Both boundaries land exactly on grid vertices. The 7.5 mm side
                // allowance clears the board ends while the skirt rounds the edge.
                Vector2 excess = new Vector2((float)maxf(absf(u) - 0.775, 0), (float)maxf(absf(v) - 0.350, 0));
                double drop = excess.Length();
                double bend = minf(drop / 0.014, PI * 0.5);
                double tail = maxf(drop - 0.014 * PI * 0.5, 0.0);
                double free_weight = smoothstep(0.015, 0.15, drop);
                double folds = (sin(u * 19.0 + v * 11.0) * 0.005 + sin(u * 8.0 - v * 17.0 + 0.6) * 0.003) * free_weight;
                double spread = 0.014 * sin(bend) + tail * 0.20 + folds;
                double down = 0.014 * (1.0 - cos(bend)) + tail * 0.98;
                down += (sin(u * 7.0 + v * 5.0) * 0.003 + sin(u * 17.0 - v * 9.0) * 0.0015) * free_weight;
                Vector2 outward = excess.Normalized();
                // Angular separation preserves a fan of fabric around each corner,
                // rather than collapsing its two-dimensional mesh onto a line.
                Vector3 p = new Vector3((float)(clampf(u, -0.775, 0.775) + signf(u) * outward.X * spread), (float)(CLOTH_TOP - down), (float)(clampf(v, -0.350, 0.350) + signf(v) * outward.Y * spread));
                p.Y = (float)(p.Y + _linen_crease(clampf(u, -0.775, 0.775), clampf(v, -0.350, 0.350)) * (1.0 - smoothstep(0.0, 0.06, drop)));
                Vector3 normal = new Vector3((float)(signf(u) * excess.X * 60.0), 1.0f, (float)(signf(v) * excess.Y * 60.0)).Normalized();
                long index = mb.add_vertex(p, normal, new Vector2((float)(u + 0.95), (float)(v + 0.60)), Colors.White);
                // Secure the narrow wrap until it clears the board underside. Point
                // collisions alone let a free vertex slip below the thin board and
                // pull its connecting triangle straight through the visible end grain.
                if (drop <= 0.0751)
                {
                    cloth_pins.Add((int)index);
                }
                else if (i % 4 == 0 && j % 4 == 0)
                {
                    _cloth_wind_points.Add((int)index);
                    _cloth_wind_phases.Add((float)(u * 8.0 + v * 5.0));
                }
            }
        }
        for (long j2 = 0; j2 < NZ; j2++)
        {
            for (long i2 = 0; i2 < NX; i2++)
            {
                long a = j2 * (NX + 1) + i2;
                // A connected sheet needs consistent winding even where it folds
                // down vertically. Per-face UP tests flipped alternate skirt cells.
                mb.add_quad_indices(a, a + NX + 1, a + NX + 2, a + 1);
            }
        }
        mb.recompute_normals();
        mb.recompute_tangents();
        cloth = new SoftBody3D();
        cloth.Name = "LinenTablecloth";
        cloth.Mesh = mb.commit();
        ShaderMaterial material = new ShaderMaterial();
        material.Shader = Content.Load<Shader>("res://shaders/tablecloth.gdshader");
        Camp.bind_texture(material, "weave_tex", "res://textures/canvas_albedo.png");
        Camp.bind_texture(material, "weave_normal", "res://textures/canvas_normal.png");
        cloth.MaterialOverride = material;
        cloth.TotalMass = 0.22f;
        cloth.LinearStiffness = 0.88f;
        cloth.DampingCoefficient = 0.12f;
        cloth.SimulationPrecision = 6;
        cloth.CollisionLayer = unchecked((uint)(0));
        cloth.CollisionMask = unchecked((uint)(1L << 7));
        cloth.RayPickable = false;
        AddChild(cloth);
        foreach (int point in cloth_pins)
        {
            cloth.SetPointPinned(point, true);
        }
    }

    public static double _linen_crease(double u, double v)
    {
        Vector2 p = new Vector2((float)u, (float)v);
        // Keep the fabric flat under the objects, easing into shallow creases in
        // the open cloth. These are folds of clean linen, not painted dirt.
        double clear = smoothstep(0.17, 0.20, p.DistanceTo(new Vector2(-0.36f, -0.02f)));
        clear *= smoothstep(0.055, 0.085, p.DistanceTo(new Vector2(0.12f, -0.15f)));
        clear *= smoothstep(0.055, 0.085, p.DistanceTo(new Vector2(0.34f, 0.12f)));
        Vector2 book = (p - new Vector2(0.42f, -0.12f)).Abs() - new Vector2(0.135f, 0.165f);
        clear *= smoothstep(0.0, 0.03, maxf(book.X, book.Y));
        double fold = u * 0.9 + v * 0.5 + 0.19;
        return clear * (0.0014 * exp(-fold * fold / 0.0003) + 0.0008 * pow(maxf(cos(u * 7.0 + v * 5.0), 0.0), 8.0));
    }

    public override void _PhysicsProcess(double delta)
    {
        _cloth_time += delta;
        if (cloth == null || Game.Instance.world == null)
        {
            return;
        }
        Vector2 wind = WorldController.WIND_DIRECTION.Normalized();
        double gust = Game.Instance.world.wind_strength() * (0.75 + 0.25 * sin(_cloth_time * 1.7));
        cloth.ApplyCentralForce(new Vector3(wind.X, 0.0f, wind.Y) * (float)gust * 0.14f);
        // Each sample represents a 10 cm square of cloth. Spatial gust phases
        // disturb the free hem gently instead of tilting the whole skirt together.
        double SAMPLE_AREA = 0.10 * 0.10;
        for (long i = 0, i_end = (long)_cloth_wind_points.Count; i < i_end; i++)
        {
            double pulse = sin(_cloth_time * 2.4 - _cloth_wind_phases[(int)i]);
            double pressure = gust * (0.22 + 0.13 * pulse);
            cloth.ApplyForce(_cloth_wind_points[(int)i], new Vector3((float)(wind.X * pressure), (float)(pulse * gust * 0.045), (float)(wind.Y * pressure)) * (float)SAMPLE_AREA);
        }
    }

    public void _add_journal()
    {
        Node3D root = new Node3D();
        root.Name = "FieldJournal";
        root.Position = new Vector3(0.42f, (float)CLOTH_TOP, -0.12f);
        Vector3 _t1 = root.Rotation;
        _t1.Y = 0.14f;
        root.Rotation = _t1;
        AddChild(root);
        StandardMaterial3D leather = FieldKit.solid(new Color(0.22f, 0.14f, 0.075f), 0.85);
        FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.21f, 0.003f, 0.27f), 0.001), leather, new Vector3(0, 0.0025f, 0));
        FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.196f, 0.023f, 0.255f), 0.002), FieldKit.solid(new Color(0.66f, 0.62f, 0.48f), 0.98), new Vector3(0.005f, 0.016f, 0));
        FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.21f, 0.003f, 0.27f), 0.001), leather, new Vector3(0, 0.03f, 0));
        Label3D label = new Label3D();
        label.Text = "FIELD\nNOTES";
        label.FontSize = 48;
        label.PixelSize = 0.0006f;
        label.Modulate = new Color(0.66f, 0.55f, 0.33f);
        label.OutlineSize = 0;
        label.NoDepthTest = false;
        label.Position = new Vector3(0, 0.032f, -0.026f);
        Vector3 _t2 = label.Rotation;
        _t2.X = (float)(-PI * 0.5);
        label.Rotation = _t2;
        root.AddChild(label);
        // Pencil lies wholly on the book, aligned to the long edge.
        MeshBuilder mb = new MeshBuilder();
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.069f, 0.035f, -0.09f), new Vector3(0.069f, 0.035f, 0.07f), new Vector3(0.069f, 0.035f, 0.086f) }, new Godot.Collections.Array<double> { 0.003, 0.003, 0.0002 }, 6);
        FieldKit.add(root, mb.commit(), PropMaterials.wood(new Color(0.66f, 0.48f, 0.21f), 0.1, 0));
    }

    public void _add_crate(Material wood)
    {
        Node3D root = new Node3D();
        root.Name = "SlattedSupplyCrate";
        root.Position = new Vector3(-0.13f, 0, 0.83f);
        Vector3 _t1 = root.Rotation;
        _t1.Y = 0.12f;
        root.Rotation = _t1;
        Vector3 world = ToGlobal(root.Position);
        Vector3 _t2 = root.Position;
        _t2.Y = (float)(field.height(world.X, world.Z) - GlobalPosition.Y + 0.015);
        root.Position = _t2;
        AddChild(root);
        for (long row = 0; row < 4; row++)
        {
            foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double side = side_item.AsDouble();
                FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.64f, 0.073f, 0.019f), 0.004), wood, new Vector3(0, (float)(0.055 + (double)row * 0.079), (float)(side * 0.23)));
                FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.019f, 0.073f, 0.44f), 0.004), wood, new Vector3((float)(side * 0.31), (float)(0.055 + (double)row * 0.079), 0));
            }
        }
        foreach (Variant side_x_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side_x = side_x_item.AsDouble();
            foreach (Variant side_z_item in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double side_z = side_z_item.AsDouble();
                FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.032f, 0.36f, 0.032f), 0.003), wood, new Vector3((float)(side_x * 0.285), 0.18f, (float)(side_z * 0.201)));
            }
        }
        for (long i4 = 0; i4 < 4; i4++)
        {
            FieldKit.add(root, FieldKit.rounded_box(new Vector3(0.61f, 0.022f, 0.107f), 0.004), wood, new Vector3(0, 0.343f, (float)(((double)i4 - 1.5) * 0.112)));
        }
        FieldKit.blanket(root, new Vector3(0, 0.45f, 0));
        _box(root.Position + new Vector3(0, 0.18f, 0), new Vector3(0.66f, 0.36f, 0.48f));
    }

    public void _box(Vector3 at, Vector3 size)
    {
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"wood");
        body.Position = at;
        CollisionShape3D shape = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = size;
        shape.Shape = box;
        body.AddChild(shape);
        AddChild(body);
    }
}
