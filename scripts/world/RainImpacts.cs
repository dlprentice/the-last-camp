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

/// Fixed contact samples on the actual uppermost camp meshes. The GPU animates
/// small ballistic droplets; terrain/triangle queries happen only at build.
public partial class RainImpacts : Node3D
{
    public const double CELL = 2.0;
    public List<List<Vector3>> _triangles = new List<List<Vector3>>();
    public Godot.Collections.Dictionary _cells = new Godot.Collections.Dictionary();
    public TerrainField _field;
    public ShaderMaterial _material;
    public long sample_count = 0;

    public void build(Camp camp)
    {
        Name = "RainImpacts";
        _field = camp.field;
        _collect(camp.campsite);
        _collect(camp.rocks);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(821553));
        Godot.Collections.Array<Vector3> samples = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<Vector3> normals = new Godot.Collections.Array<Vector3>();
        for (long z = -22; z < 31; z++)
        {
            for (long x = -54; x < 20; x++)
            {
                Vector2 p = new Vector2((float)(x + rng.Randf()), (float)(z + rng.Randf()));
                // Dense grass conceals small ground impacts; spend them at the pond,
                // along paths and the clearing rather than above the blade canopy.
                if (_field.grass_suitability(p.X, p.Y) > 0.40 && rng.Randf() > 0.16)
                {
                    continue;
                }
                _sample(p, samples, normals);
            }
        }
        // Close prop surfaces merit denser droplets than the distant terrain.
        Dock dock = camp.campsite.dock;
        for (long i = 0; i < 460; i++)
        {
            Vector3 p2 = dock.ToGlobal(new Vector3(rng.RandfRange(-0.77f, 0.77f), 0, rng.RandfRange((float)-dock.total_length(), 0)));
            _sample(new Vector2(p2.X, p2.Z), samples, normals);
        }
        Tent tent = camp.campsite.tent;
        for (long i2 = 0; i2 < 240; i2++)
        {
            Vector3 p3 = tent.ToGlobal(new Vector3(rng.RandfRange(-1.18f, 1.18f), 0, rng.RandfRange(-1.48f, 1.48f)));
            _sample(new Vector2(p3.X, p3.Z), samples, normals);
        }
        foreach (ScenePlan.RockEntry rock in camp.plan.rocks)
        {
            if (rock.scale < 0.6 || rock.position.Length() > 56.0)
            {
                continue;
            }
            for (long i3 = 0, i_end = mini(32, (long)(rock.scale * rock.scale * 8.0)); i3 < i_end; i3++)
            {
                Vector2 p4 = rock.position + new Vector2(rng.RandfRange(-0.42f, 0.42f), rng.RandfRange(-0.42f, 0.42f)) * (float)rock.scale;
                _sample(p4, samples, normals);
            }
        }
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.Mesh = _droplets();
        mm.InstanceCount = (int)(long)samples.Count;
        for (long i4 = 0, i_end2 = (long)samples.Count; i4 < i_end2; i4++)
        {
            Vector3 normal = normals[(int)i4];
            Vector3 tangent = normal.Cross(Vector3.Forward).Normalized();
            if (tangent.LengthSquared() < 0.01)
            {
                tangent = Vector3.Right;
            }
            Basis basis = new Basis(tangent, normal, tangent.Cross(normal)).Rotated(normal, (float)(rng.Randf() * TAU));
            mm.SetInstanceTransform((int)i4, new Transform3D(basis, samples[(int)i4] + normal * 0.008f));
            bool water_contact = is_equal_approx(samples[(int)i4].Y, TerrainField.WATER_LEVEL);
            mm.SetInstanceCustomData((int)i4, new Color(rng.Randf(), rng.Randf(), (float)(water_contact ? 1.0 : 0.0), rng.Randf()));
        }
        _material = new ShaderMaterial();
        _material.Shader = Content.Load<Shader>("res://shaders/rain_impact.gdshader");
        MultiMeshInstance3D mesh = new MultiMeshInstance3D();
        mesh.Multimesh = mm;
        mesh.MaterialOverride = _material;
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        mesh.ExtraCullMargin = 0.3f;
        AddChild(mesh);
        sample_count = (long)samples.Count;
        _triangles.Clear();
        _cells.Clear();
        G.print("RAIN_IMPACTS contacts=", sample_count);
    }

    public void update(double clock, double amount)
    {
        Visible = amount > 0.001;
        if (_material != null)
        {
            _material.SetShaderParameter("impact_clock", clock);
        }
    }

    public void _collect(Node node)
    {
        // This cache is for fixed surfaces. The canoe bobs and rolls; retaining its
        // construction pose would leave small splash droplets hanging in space.
        if (node is Canoe)
        {
            return;
        }
        if (node is MeshInstance3D && ((MeshInstance3D)node).Mesh != null)
        {
            Material material = ((MeshInstance3D)node).MaterialOverride;
            bool include_mesh = material is StandardMaterial3D || material == null;
            if (material is ShaderMaterial)
            {
                include_mesh = new Godot.Collections.Array { "prop.gdshader", "wood_uv.gdshader", "canvas.gdshader" }.Contains(((ShaderMaterial)material).Shader.ResourcePath.GetFile());
            }
            if (include_mesh)
            {
                List<Vector3> faces = new List<Vector3>(((MeshInstance3D)node).Mesh.GetFaces());
                for (long i = 0, i_end = (long)faces.Count; i < i_end; i += 3)
                {
                    Vector3 a = ((MeshInstance3D)node).GlobalTransform * faces[(int)i];
                    Vector3 b = ((MeshInstance3D)node).GlobalTransform * faces[(int)(i + 1)];
                    Vector3 c = ((MeshInstance3D)node).GlobalTransform * faces[(int)(i + 2)];
                    Vector3 n = (b - a).Cross(c - a).Normalized();
                    if (absf(n.Y) < 0.20)
                    {
                        continue;
                    }
                    long idx = (long)_triangles.Count;
                    _triangles.Add(new List<Vector3>(new List<Vector3> { a, b, c }));
                    for (long z = (long)floor(minf(a.Z, minf(b.Z, c.Z)) / CELL), z_end = (long)floor(maxf(a.Z, maxf(b.Z, c.Z)) / CELL) + 1; z < z_end; z++)
                    {
                        for (long x = (long)floor(minf(a.X, minf(b.X, c.X)) / CELL), x_end = (long)floor(maxf(a.X, maxf(b.X, c.X)) / CELL) + 1; x < x_end; x++)
                        {
                            Vector2I key = new Vector2I((int)x, (int)z);
                            if (!_cells.ContainsKey(key))
                            {
                                _cells[key] = new Godot.Collections.Array();
                            }
                            G.Call(_cells[key], "append", idx);
                        }
                    }
                }
            }
        }
        foreach (Node child in node.GetChildren())
        {
            _collect(child);
        }
    }

    public void _sample(Vector2 p, Godot.Collections.Array<Vector3> samples, Godot.Collections.Array<Vector3> normals)
    {
        double ground = _field.height(p.X, p.Y);
        double height = maxf(ground, TerrainField.WATER_LEVEL);
        Vector3 normal = Vector3.Up;
        if (ground > TerrainField.WATER_LEVEL)
        {
            normal = new Vector3((float)(_field.height(p.X - 0.05, p.Y) - _field.height(p.X + 0.05, p.Y)), 0.1f, (float)(_field.height(p.X, p.Y - 0.05) - _field.height(p.X, p.Y + 0.05))).Normalized();
        }
        Vector2I key = new Vector2I((int)floori(p.X / CELL), (int)floori(p.Y / CELL));
        foreach (Variant idx_item in G.Iter(G.get(_cells, key, new Godot.Collections.Array())))
        {
            Variant idx = idx_item;
            List<Vector3> tri = _triangles[idx.AsInt32()];
            Vector2 a = new Vector2(tri[0].X, tri[0].Z);
            Vector2 b = new Vector2(tri[1].X, tri[1].Z);
            Vector2 c = new Vector2(tri[2].X, tri[2].Z);
            double denom = (b - a).Cross(c - a);
            if (absf(denom) < 0.000001)
            {
                continue;
            }
            double u = (p - a).Cross(c - a) / denom;
            double v = (b - a).Cross(p - a) / denom;
            if (u < 0 || v < 0 || u + v > 1)
            {
                continue;
            }
            double y = tri[0].Y + u * ((double)tri[1].Y - tri[0].Y) + v * ((double)tri[2].Y - tri[0].Y);
            if (y > height)
            {
                height = y;
                normal = (tri[1] - tri[0]).Cross(tri[2] - tri[0]).Normalized();
                normal *= (float)signf(normal.Y);
            }
        }
        samples.Add(new Vector3(p.X, (float)height, p.Y));
        normals.Add(normal);
    }

    public static ArrayMesh _droplets()
    {
        MeshBuilder mb = new MeshBuilder();
        for (long i = 0; i < 6; i++)
        {
            // Red encodes azimuth, green vertical launch speed, blue size.
            Color data = new Color((float)((double)i / 6.0), (float)fmod(i * 0.618, 1.0), (float)fmod(i * 0.37, 1.0), 1);
            mb.add_quad(new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0), data);
        }
        return mb.commit();
    }
}
