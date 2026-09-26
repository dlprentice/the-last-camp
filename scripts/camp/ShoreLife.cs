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

/// Small-scale habitat: lily colonies in sheltered shallows and waterworn
/// stones along the bank. Shared meshes keep the dressing inexpensive.
public partial class ShoreLife : Node3D
{
    public TerrainField field;
    public Godot.Collections.Array<Vector3> pad_positions = new Godot.Collections.Array<Vector3>();

    public ShoreLife(TerrainField p_field)
    {
        field = p_field;
        Name = "ShoreLife";
    }

    public ShoreLife()
    {
    }

    public void build()
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(90426));
        Godot.Collections.Array<Transform3D> pads = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Transform3D> stones = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Transform3D> flowers = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Transform3D> stems = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<double> pad_radii = new Godot.Collections.Array<double>();
        for (long i = 0; i < 1200; i++)
        {
            double angle = rng.Randf() * TAU;
            Vector2 shore = TerrainField.shore_point(angle);
            Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
            Vector2 p = shore + radial * rng.RandfRange(-1.3f, 2.5f);
            if (p.DistanceTo(TerrainField.DOCK_START) < 3.0)
            {
                continue;
            }
            double size = rng.RandfRange(0.035f, 0.16f);
            double y = field.height_fast(p.X, p.Y);
            Basis basis = new Basis(Vector3.Up, (float)(rng.Randf() * TAU)).Scaled(new Vector3((float)size, (float)(size * 0.5), (float)(size * 0.8)));
            stones.Add(new Transform3D(basis, new Vector3(p.X, (float)(y + size * 0.08), p.Y)));
        }
        // Clusters live away from the travelled water and the canoe. Interleaved
        // rings of different sizes avoid uniform green discs along the whole bank.
        foreach (Variant colony in new Godot.Collections.Array { new Vector2(-28.0f, 15.0f), new Vector2(-42.0f, 11.0f), new Vector2(-36.0f, -8.0f) })
        {
            for (long i2 = 0; i2 < 75; i2++)
            {
                double angle2 = rng.Randf() * TAU;
                double radius = sqrt(rng.Randf()) * 3.7;
                Vector2 p2 = G.op("+", colony, new Vector2((float)cos(angle2), (float)sin(angle2)) * (float)radius).AsVector2();
                if (!pad_allowed(field, p2))
                {
                    continue;
                }
                double size2 = rng.RandfRange(0.16f, 0.36f);
                bool crowded = false;
                for (long j = 0, j_end = (long)pad_positions.Count; j < j_end; j++)
                {
                    if (p2.DistanceTo(new Vector2(pad_positions[(int)j].X, pad_positions[(int)j].Z)) < (size2 + pad_radii[(int)j]) * 0.85)
                    {
                        crowded = true;
                        break;
                    }
                }
                if (crowded)
                {
                    continue;
                }
                Vector3 origin = new Vector3(p2.X, (float)(TerrainField.WATER_LEVEL + 0.0015), p2.Y);
                Basis basis2 = new Basis(Vector3.Up, (float)(rng.Randf() * TAU)).Scaled(Vector3.One * (float)size2);
                pads.Add(new Transform3D(basis2, origin));
                stems.Add(_stem_transform(origin));
                pad_positions.Add(origin);
                pad_radii.Add(size2);
                if (rng.Randf() < 0.09)
                {
                    Vector3 offset = new Vector3((float)cos(angle2), 0.0f, (float)sin(angle2)) * (float)(size2 + 0.14);
                    flowers.Add(new Transform3D(new Basis(Vector3.Up, (float)(rng.Randf() * TAU)), origin + offset));
                    stems.Add(_stem_transform(origin + offset));
                }
            }
        }
        ShaderMaterial pad_material = new ShaderMaterial();
        pad_material.Shader = Content.Load<Shader>("res://shaders/lily.gdshader");
        _instances("LilyPads", _pad_mesh(), pad_material, pads);
        ShaderMaterial stone_material = new ShaderMaterial();
        stone_material.Shader = Content.Load<Shader>("res://shaders/prop.gdshader");
        Camp.bind_prop_pbr(stone_material, "rock");
        Camp.bind_texture(stone_material, "noise_tex", "res://textures/noise_rgba.png");
        stone_material.SetShaderParameter("tile", 2.0);
        stone_material.SetShaderParameter("moss_amount", 0.08);
        stone_material.SetShaderParameter("tint", new Color(0.7f, 0.74f, 0.71f));
        _instances("WaterwornStones", PropMeshes.rock(9042), stone_material, stones);
        ShaderMaterial petal_material = new ShaderMaterial();
        petal_material.Shader = Content.Load<Shader>("res://shaders/lily.gdshader");
        petal_material.SetShaderParameter("blossom", true);
        _instances("WaterLilies", _flower_mesh(), petal_material, flowers);
        // Stalks reach from the bed to the exact moving centre of each leaf.
        MeshBuilder stalk = new MeshBuilder();
        stalk.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.08f, -1, -0.06f), new Vector3(-0.07f, -0.65f, 0.05f), new Vector3(0.045f, -0.3f, 0.015f), Vector3.Zero }, new Godot.Collections.Array<double> { 0.004, 0.0035, 0.0035, 0.004 }, 7, Colors.White, 1, 1, 0, true);
        ShaderMaterial stalk_material = new ShaderMaterial();
        stalk_material.Shader = pad_material.Shader;
        stalk_material.SetShaderParameter("stalk", true);
        _instances("LilyStems", stalk.commit(), stalk_material, stems);
    }

    public Transform3D _stem_transform(Vector3 origin)
    {
        // Leaves spread out from irregular rhizome patches. Their petioles converge
        // on those roots instead of forming parallel vertical lines in the water.
        // The top stays attached to its pad and the root meets the actual bed.
        double phase = sin(origin.X * 12.3 + origin.Z * 42.4);
        double bend = lerpf(0.65, 1.6, phase * 0.5 + 0.5);
        Basis rotation = new Basis(Vector3.Up, (float)(phase * 1249.0));
        Vector2 patch = new Vector2((float)(floorf(origin.X / 1.4) + 0.5), (float)(floorf(origin.Z / 1.4) + 0.5)) * 1.4f;
        patch += new Vector2((float)sin(patch.X * 4.7), (float)cos(patch.Y * 5.3)) * 0.18f;
        Vector3 root_offset = new Vector3((float)((double)patch.X - origin.X), 0, (float)((double)patch.Y - origin.Z));
        Vector3 curve_root = rotation * new Vector3(0.08f, 0, -0.06f) * (float)bend;
        double height = origin.Y - field.height((double)origin.X + root_offset.X, (double)origin.Z + root_offset.Z) + 0.012;
        Basis frame = rotation.Scaled(new Vector3((float)bend, (float)height, (float)bend));
        Vector3 _t1 = frame.Y;
        _t1.X = (float)((double)curve_root.X - root_offset.X);
        Basis _t2 = frame;
        _t2.Y = _t1;
        frame = _t2;
        Vector3 _t3 = frame.Y;
        _t3.Z = (float)((double)curve_root.Z - root_offset.Z);
        Basis _t4 = frame;
        _t4.Y = _t3;
        frame = _t4;
        return new Transform3D(frame, origin);
    }

    public static bool pad_allowed(TerrainField terrain, Vector2 p)
    {
        double depth = terrain.water_depth(p.X, p.Y);
        // The dock runs toward the pond centre; reserve that entire navigation lane.
        Godot.Collections.Array<Vector2> lane = new Godot.Collections.Array<Vector2> { TerrainField.DOCK_START, TerrainField.POND_CENTRE };
        return depth > 0.20 && depth < 2.4 && TerrainField.distance_to_polyline(p, lane) > 3.0;
    }

    public static ArrayMesh _pad_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        // A thin cupped leaf with a narrow cleft. Radial subdivisions give the
        // reflected highlight a continuous curve instead of a triangle fan.
        long SECTORS = 128;
        long RINGS = 8;
        mb.add_vertex(Vector3.Zero, Vector3.Up, Vector2.One * 0.5f);
        for (long ring = 1, ring_end = RINGS + 1; ring < ring_end; ring++)
        {
            double t = (double)ring / RINGS;
            for (long i = 0, i_end = SECTORS + 1; i < i_end; i++)
            {
                double a = lerpf(0.095, TAU - 0.095, (double)i / SECTORS);
                double radius = t * (0.94 + 0.022 * sin(a * 5.0) + 0.008 * sin(a * 13.0));
                // The rim turns up a couple of centimetres, unevenly, so the pad
                // catches light as a shallow dish rather than a flat disc.
                double y = pow(t, 3.0) * (0.016 + 0.007 * sin(a * 4.0 + 1.3) + 0.004 * sin(a * 9.0));
                Vector3 p = new Vector3((float)(cos(a) * radius), (float)y, (float)(sin(a) * radius));
                mb.add_vertex(p, Vector3.Up, new Vector2(p.X, p.Z) * 0.5f + Vector2.One * 0.5f);
            }
        }
        for (long i2 = 0; i2 < SECTORS; i2++)
        {
            mb.add_triangle(0, i2 + 1, i2 + 2);
        }
        for (long ring2 = 0, ring_end2 = RINGS - 1; ring2 < ring_end2; ring2++)
        {
            for (long i3 = 0; i3 < SECTORS; i3++)
            {
                long a2 = 1 + ring2 * (SECTORS + 1) + i3;
                mb.add_quad_facing(a2, a2 + 1, a2 + SECTORS + 2, a2 + SECTORS + 1, Vector3.Up);
            }
        }
        mb.recompute_normals();
        return mb.commit();
    }

    public static ArrayMesh _flower_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        // The peduncle continues below the water into the rhizome; the green
        // receptacle supports the cup at the surface instead of above a leaf.
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.02f, -0.32f, 0.01f), new Vector3(0.005f, -0.06f, 0), Vector3.Zero }, new Godot.Collections.Array<double> { 0.004, 0.005, 0.007 }, 8, new Color(0.065f, 0.15f, 0.035f), 1, 1, 0, true);
        long calyx_start = mb.vertex_count();
        mb.add_displaced_sphere(6, 16, 0.023, Callable.From((Vector3 _d) => 1.0), new Color(0.09f, 0.19f, 0.04f));
        for (long i = calyx_start, i_end = mb.vertex_count(); i < i_end; i++)
        {
            Vector3 _t1 = mb.vertices[(int)i];
            _t1.Y = (float)(mb.vertices[(int)i].Y * 0.35 - 0.002);
            mb.vertices[(int)i] = _t1;
        }
        // Temperate white water lily: lanceolate curved petals and a dense gold
        // centre. Both the cup and the petal edges have real geometry.
        for (long layer = 0; layer < 3; layer++)
        {
            long count = 12 - layer * 2;
            for (long petal = 0; petal < count; petal++)
            {
                double a = (double)petal / count * TAU + (double)layer * 0.29;
                Vector3 dir = new Vector3((float)cos(a), 0, (float)sin(a));
                Vector3 side = new Vector3((float)-sin(a), 0, (float)cos(a));
                double length = 0.115 - (double)layer * 0.021;
                long start = mb.vertex_count();
                for (long row = 0; row < 11; row++)
                {
                    double t = (double)row / 10.0;
                    double width = pow(maxf(sin(t * PI), 0.0), 0.72) * (0.021 - layer * 0.003) + 0.0002;
                    Vector3 centre = dir * (float)(0.013 + t * length) + Vector3.Up * (float)(0.006 + layer * 0.008 + t * t * (0.024 + layer * 0.019));
                    Color cream = new Color(0.88f, 0.64f, 0.18f).Lerp(new Color(0.94f, 0.93f, 0.86f), (float)smoothstep(0.0, 0.33, t));
                    for (long col = 0; col < 7; col++)
                    {
                        double u = (double)col / 6.0 * 2.0 - 1.0;
                        Vector3 p = centre + side * (float)width * (float)u + Vector3.Up * (float)u * (float)u * (float)sin(t * PI) * 0.006f;
                        mb.add_vertex(p, Vector3.Up, new Vector2((float)((double)col / 6.0), (float)t), cream);
                    }
                }
                for (long row2 = 0; row2 < 10; row2++)
                {
                    for (long col2 = 0; col2 < 6; col2++)
                    {
                        long v = start + row2 * 7 + col2;
                        mb.add_quad_facing(v, v + 1, v + 8, v + 7, Vector3.Up);
                    }
                }
            }
        }
        for (long ring = 0; ring < 3; ring++)
        {
            long count2 = 12 + ring * 6;
            for (long i2 = 0; i2 < count2; i2++)
            {
                double a2 = (double)i2 / count2 * TAU + ring * 0.4;
                Vector3 dir2 = new Vector3((float)cos(a2), 0, (float)sin(a2));
                double radius = 0.005 + ring * 0.007;
                Vector3 @base = dir2 * (float)radius + Vector3.Up * 0.016f;
                Vector3 tip = dir2 * (float)(radius + 0.006) + Vector3.Up * (float)(0.052 - ring * 0.005);
                mb.add_tube(new Godot.Collections.Array<Vector3> { @base, @base.Lerp(tip, 0.75f), tip }, new Godot.Collections.Array<double> { 0.0008, 0.0014, 0.001 }, 5, new Color(0.95f, (float)(0.57 + ring * 0.07), 0.08f), 1, 1, 0, true);
            }
        }
        mb.recompute_normals();
        return mb.commit();
    }

    public void _instances(string node_name, Mesh mesh, Material mat, Godot.Collections.Array<Transform3D> transforms)
    {
        if ((transforms.Count == 0))
        {
            return;
        }
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
        }
        MultiMeshInstance3D mi = new MultiMeshInstance3D();
        mi.Name = node_name;
        mi.Multimesh = mm;
        mi.MaterialOverride = mat;
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mi.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(mi);
    }
}
