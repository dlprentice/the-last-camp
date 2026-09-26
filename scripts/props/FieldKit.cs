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

/// Small camp objects share real dimensions and rounded edges. All geometry
/// is built here, so the table, sleeping kit and tent remain editable in metres.
public partial class FieldKit
{
    public static ArrayMesh rounded_box(Vector3 size, double bevel)
    {
        MeshBuilder mb = new MeshBuilder();
        Vector3 half = size * 0.5f;
        double radius = minf(bevel, minf(half.X, minf(half.Y, half.Z)) * 0.98);
        Vector3 core = half - Vector3.One * (float)radius;
        for (long axis = 0; axis < 3; axis++)
        {
            foreach (Variant sign_value_item in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double sign_value = sign_value_item.AsDouble();
                Vector3 normal = Vector3.Zero;
                normal[(int)axis] = (float)sign_value;
                long u_axis = (axis + 1) % 3;
                long v_axis = (axis + 2) % 3;
                Godot.Collections.Array us = new Godot.Collections.Array { -half[(int)u_axis], -half[(int)u_axis] + radius * 0.5, -core[(int)u_axis], core[(int)u_axis], half[(int)u_axis] - radius * 0.5, half[(int)u_axis] };
                Godot.Collections.Array vs = new Godot.Collections.Array { -half[(int)v_axis], -half[(int)v_axis] + radius * 0.5, -core[(int)v_axis], core[(int)v_axis], half[(int)v_axis] - radius * 0.5, half[(int)v_axis] };
                List<List<int>> rows = new List<List<int>>();
                for (long j = 0, j_end = (long)vs.Count; j < j_end; j++)
                {
                    List<int> row = new List<int>();
                    for (long i2 = 0, i_end = (long)us.Count; i2 < i_end; i2++)
                    {
                        Vector3 p = normal * half[(int)axis];
                        p[(int)u_axis] = us[(int)i2].AsSingle();
                        p[(int)v_axis] = vs[(int)j].AsSingle();
                        Vector3 nearest = p.Clamp(-core, core);
                        Vector3 n = (p - nearest).Normalized();
                        row.Add((int)mb.add_vertex(nearest + n * (float)radius, n, new Vector2(p[(int)u_axis], p[(int)v_axis]), Colors.White, MeshBuilder.tangent_for(n, absf(n.X) < 0.9 ? Vector3.Right : Vector3.Forward, Vector3.Up)));
                    }
                    rows.Add(row);
                }
                for (long j2 = 0, j_end2 = (long)vs.Count - 1; j2 < j_end2; j2++)
                {
                    for (long i3 = 0, i_end2 = (long)us.Count - 1; i3 < i_end2; i3++)
                    {
                        mb.add_quad_facing(rows[(int)j2][(int)i3], rows[(int)j2][(int)(i3 + 1)], rows[(int)(j2 + 1)][(int)(i3 + 1)], rows[(int)(j2 + 1)][(int)i3], normal);
                    }
                }
            }
        }
        return mb.commit();
    }

    public static ArrayMesh lathe(Godot.Collections.Array<Vector2> profile, long sides = 48)
    {
        MeshBuilder mb = new MeshBuilder();
        List<List<int>> rows = new List<List<int>>();
        for (long j = 0, j_end = (long)profile.Count; j < j_end; j++)
        {
            List<int> row = new List<int>();
            Vector2 tangent = profile[(int)mini(j + 1, (long)profile.Count - 1)] - profile[(int)maxi(j - 1, 0)];
            for (long i = 0, i_end = sides + 1; i < i_end; i++)
            {
                double a = (double)i / (double)sides * TAU;
                Vector3 n = new Vector3((float)(cos(a) * tangent.Y), -tangent.X, (float)(sin(a) * tangent.Y)).Normalized();
                row.Add((int)mb.add_vertex(new Vector3((float)(cos(a) * profile[(int)j].X), profile[(int)j].Y, (float)(sin(a) * profile[(int)j].X)), n, new Vector2((float)((double)i / sides), profile[(int)j].Y), Colors.White, new Vector4((float)-sin(a), 0, (float)cos(a), 1)));
            }
            rows.Add(row);
        }
        for (long j2 = 0, j_end2 = (long)rows.Count - 1; j2 < j_end2; j2++)
        {
            for (long i2 = 0; i2 < sides; i2++)
            {
                mb.add_quad_indices(rows[(int)j2][(int)i2], rows[(int)(j2 + 1)][(int)i2], rows[(int)(j2 + 1)][(int)(i2 + 1)], rows[(int)j2][(int)(i2 + 1)]);
            }
        }
        return mb.commit();
    }

    public static ShaderMaterial enamel(Color tint)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/enamel.gdshader");
        mat.SetShaderParameter("chip_amount", 0.32);
        mat.SetShaderParameter("age", 0.45);
        mat.SetShaderParameter("tint", tint);
        return mat;
    }

    public static StandardMaterial3D solid(Color tint, double roughness = 0.6, double metal = 0.0)
    {
        StandardMaterial3D mat = new StandardMaterial3D();
        mat.AlbedoColor = tint;
        mat.Roughness = (float)roughness;
        mat.Metallic = (float)metal;
        return mat;
    }

    public static MeshInstance3D add(Node3D parent, ArrayMesh mesh, Material mat, Vector3? at_opt = null, string node_name = "Detail")
    {
        Vector3 at = at_opt ?? Vector3.Zero;
        MeshInstance3D mi = new MeshInstance3D();
        mi.Name = node_name;
        mi.Mesh = mesh;
        mi.MaterialOverride = mat;
        mi.Position = at;
        parent.AddChild(mi, true);
        return mi;
    }

    public static Node3D mug(Node3D parent, Vector3 at, Color tint)
    {
        Node3D root = new Node3D();
        root.Name = "EnamelMug";
        root.Position = at;
        parent.AddChild(root, true);
        ShaderMaterial mat = enamel(tint);
        add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0, 0.003f), new Vector2(0.045f, 0.003f), new Vector2(0.051f, 0.008f), new Vector2(0.054f, 0.109f), new Vector2(0.057f, 0.114f), new Vector2(0.055f, 0.119f), new Vector2(0.050f, 0.119f), new Vector2(0.048f, 0.109f), new Vector2(0.045f, 0.017f), new Vector2(0, 0.017f) }), mat);
        MeshBuilder handle = new MeshBuilder();
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        for (long i = 0; i < 17; i++)
        {
            double a = (double)i / 16.0 * PI;
            points.Add(new Vector3((float)(0.050 + sin(a) * 0.038), (float)(0.064 + cos(a) * 0.041), 0));
            radii.Add(0.006);
        }
        handle.add_tube(points, radii, 8);
        add(root, handle.commit(), mat);
        add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0.049f, 0.097f), new Vector2(0, 0.097f) }), solid(new Color(0.065f, 0.035f, 0.015f), 0.2), Vector3.Zero, "Tea");
        return root;
    }

    public static void kettle(Node3D parent, Vector3 at)
    {
        Node3D root = new Node3D();
        root.Name = "CoffeePot";
        root.Position = at;
        parent.AddChild(root, true);
        ShaderMaterial enamel_mat = enamel(new Color(0.18f, 0.28f, 0.28f));
        add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0, 0.004f), new Vector2(0.105f, 0.004f), new Vector2(0.128f, 0.025f), new Vector2(0.13f, 0.08f), new Vector2(0.098f, 0.20f), new Vector2(0.078f, 0.23f), new Vector2(0.079f, 0.239f), new Vector2(0.01f, 0.25f), new Vector2(0, 0.25f) }), enamel_mat);
        MeshBuilder spout = new MeshBuilder();
        Godot.Collections.Array<Vector3> spout_points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> spout_radii = new Godot.Collections.Array<double>();
        Vector3 start = new Vector3(-0.095f, 0.071f, 0);
        Vector3 control_a = new Vector3(-0.205f, 0.08f, 0);
        Vector3 control_b = new Vector3(-0.145f, 0.205f, 0);
        Vector3 end = new Vector3(-0.225f, 0.232f, 0);
        for (long i = 0; i < 25; i++)
        {
            double t = (double)i / 24.0;
            spout_points.Add(start.BezierInterpolate(control_a, control_b, end, (float)t));
            spout_radii.Add(lerpf(0.035, 0.018, smoothstep(0.0, 1.0, t)));
        }
        spout.add_tube(spout_points, spout_radii, 24);
        add(root, spout.commit(), enamel_mat, Vector3.Zero, "CurvedSpout");
        // Rolled metal lip and a short inner wall keep the opening hollow from
        // above and from either side, instead of exposing a single-sided tube.
        MeshInstance3D lip = add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0.0155f, -0.028f), new Vector2(0.0155f, 0), new Vector2(0.017f, 0.001f), new Vector2(0.018f, 0), new Vector2(0.018f, -0.003f) }, 32), enamel_mat, end, "SpoutLip");
        lip.Quaternion = new Quaternion(Vector3.Up, (end - control_b).Normalized());
        MeshInstance3D interior = add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0.0156f, 0), new Vector2(0, 0) }, 32), solid(new Color(0.025f, 0.03f, 0.027f), 0.85), end - (end - control_b).Normalized() * 0.026f, "SpoutInterior");
        interior.Quaternion = lip.Quaternion;
        StandardMaterial3D steel = solid(new Color(0.14f, 0.145f, 0.14f), 0.3, 0.85);
        MeshBuilder arc = new MeshBuilder();
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        for (long i2 = 0; i2 < 25; i2++)
        {
            double a = (double)i2 / 24.0 * PI;
            points.Add(new Vector3(0, (float)(0.16 + sin(a) * 0.23), (float)(cos(a) * 0.11)));
            radii.Add(0.005);
        }
        arc.add_tube(points, radii, 8);
        add(root, arc.commit(), steel);
        add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0, 0), new Vector2(0.021f, 0), new Vector2(0.021f, 0.014f), new Vector2(0.014f, 0.019f), new Vector2(0, 0.019f) }, 24), steel, new Vector3(0, 0.25f, 0), "LidKnob");
    }

    public static Node3D pack(Node3D parent, Vector3 at)
    {
        Node3D root = new Node3D();
        root.Name = "WaxedCanvasPack";
        root.Position = at;
        parent.AddChild(root, true);
        ShaderMaterial cloth = PropMaterials.triplanar("canvas", new Color(0.35f, 0.37f, 0.22f), 2.8);
        StandardMaterial3D leather = solid(new Color(0.19f, 0.105f, 0.045f), 0.82);
        StandardMaterial3D brass = solid(new Color(0.42f, 0.29f, 0.12f), 0.35, 0.8);
        add(root, rounded_box(new Vector3(0.39f, 0.48f, 0.25f), 0.07), cloth, new Vector3(0, 0.242f, 0), "SoftPackBody");
        add(root, rounded_box(new Vector3(0.4f, 0.095f, 0.29f), 0.04), cloth, new Vector3(0, 0.46f, -0.013f), "TopFlap");
        add(root, rounded_box(new Vector3(0.25f, 0.22f, 0.07f), 0.033), cloth, new Vector3(0, 0.18f, -0.141f), "FrontPocket");
        foreach (Variant x_item in new Godot.Collections.Array { -0.12, 0.12 })
        {
            double x = x_item.AsDouble();
            add(root, rounded_box(new Vector3(0.026f, 0.29f, 0.012f), 0.004), leather, new Vector3((float)x, 0.30f, -0.153f), "LeatherStrap");
            MeshBuilder buckle = new MeshBuilder();
            buckle.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(-0.018f, -0.015f, 0), new Vector3(0.018f, -0.015f, 0), new Vector3(0.018f, 0.015f, 0), new Vector3(-0.018f, 0.015f, 0), new Vector3(-0.018f, -0.015f, 0) }, new Godot.Collections.Array<double> { 0.0025, 0.0025, 0.0025, 0.0025, 0.0025 }, 6);
            add(root, buckle.commit(), brass, new Vector3((float)x, 0.3f, -0.163f), "Buckle");
        }
        foreach (Variant x_item2 in new Godot.Collections.Array { -0.095, 0.095 })
        {
            double x2 = x_item2.AsDouble();
            MeshBuilder strap = new MeshBuilder();
            Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
            Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
            for (long i3 = 0; i3 < 25; i3++)
            {
                double t = (double)i3 / 24.0;
                points.Add(new Vector3((float)x2, (float)lerpf(0.43, 0.09, t), (float)(0.125 + sin(t * PI) * 0.065)));
                radii.Add(0.012);
            }
            strap.add_tube(points, radii, 8);
            for (long i4 = 0, i_end = strap.vertex_count(); i4 < i_end; i4++)
            {
                long row = i4 / 9;
                Vector3 offset = strap.vertices[(int)i4] - points[(int)row];
                strap.vertices[(int)i4] = points[(int)row] + offset * new Vector3(1.15f, 0.4f, 0.4f);
                strap.normals[(int)i4] = (strap.normals[(int)i4] / new Vector3(1.15f, 0.4f, 0.4f)).Normalized();
            }
            add(root, strap.commit(), leather, Vector3.Zero, "ShoulderStrap");
        }
        blanket(root, new Vector3(0, 0.60f, 0.015f), 0.5);
        return root;
    }

    public static void blanket(Node3D parent, Vector3 at, double length = 0.56)
    {
        Node3D root = new Node3D();
        root.Name = "RolledWoolBlanket";
        root.Position = at;
        parent.AddChild(root, true);
        ShaderMaterial cloth = PropMaterials.triplanar("canvas", new Color(0.28f, 0.36f, 0.40f), 5.0);
        StandardMaterial3D leather = solid(new Color(0.16f, 0.09f, 0.044f), 0.8);
        Godot.Collections.Array<Vector2> profile = new Godot.Collections.Array<Vector2> { new Vector2(0, (float)(-length * 0.5)), new Vector2(0.085f, (float)(-length * 0.5)) };
        for (long i = 0; i < 25; i++)
        {
            double u = (double)i / 24.0;
            double x = (u - 0.5) * (length - 0.016);
            double squeeze = exp(-pow((absf(x) - length * 0.3) / 0.025, 2.0)) * 0.003;
            profile.Add(new Vector2((float)(0.092 + sin(u * PI) * 0.003 - squeeze), (float)x));
        }
        profile.Add(new Vector2(0.085f, (float)(length * 0.5)));
        profile.Add(new Vector2(0, (float)(length * 0.5)));
        MeshInstance3D roll = add(root, lathe(profile, 64), cloth, Vector3.Zero, "WoolRoll");
        Vector3 _t1 = roll.Rotation;
        _t1.Z = (float)(-PI * 0.5);
        roll.Rotation = _t1;
        // Compressed fabric layers read as a fine spiral seam on each soft end.
        StandardMaterial3D seam_mat = solid(new Color(0.115f, 0.17f, 0.19f), 0.97);
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            MeshBuilder hem = new MeshBuilder();
            Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
            Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
            for (long i3 = 0; i3 < 513; i3++)
            {
                double t = (double)i3 / 512.0;
                double a = t * TAU * 8.0;
                double r = lerpf(0.006, 0.084, t);
                points.Add(new Vector3((float)(side * (length * 0.5 + 0.0002)), (float)(cos(a) * r), (float)(sin(a) * r)));
                radii.Add(0.00065);
            }
            hem.add_tube(points, radii, 4);
            add(root, hem.commit(), seam_mat, Vector3.Zero, "WovenHem");
        }
        foreach (Variant x_item in new Godot.Collections.Array { -length * 0.30, length * 0.30 })
        {
            double x2 = x_item.AsDouble();
            MeshInstance3D belt = add(root, lathe(new Godot.Collections.Array<Vector2> { new Vector2(0.092f, -0.011f), new Vector2(0.094f, -0.011f), new Vector2(0.094f, 0.011f), new Vector2(0.092f, 0.011f), new Vector2(0.092f, -0.011f) }, 64), leather, new Vector3((float)x2, 0, 0), "BlanketBelt");
            Vector3 _t2 = belt.Rotation;
            _t2.Z = (float)(-PI * 0.5);
            belt.Rotation = _t2;
        }
    }
}
