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

/// Real-scale plant communities, not extra copies of the lawn blade. All roots
/// sample the shared surface. Small spatial cells keep off-screen work cullable.
public partial class HabitatDiversity : Node3D
{
    public enum Form
    {
        ROSETTE,
        FERNLET,
        RUSH,
        LITTER,
    }
    public const double CELL_SIZE = 16.0;
    public const double EXTENT = 92.0;
    public const long ATTEMPTS = 18500;
    public static readonly Godot.Collections.Array TARGETS = new Godot.Collections.Array { 1550, 1450, 1150, 2600 };
    public static Shader SHADER => _SHADER_cache ??= _preload_habitat;
    private static Shader _SHADER_cache;
    public List<int> counts = new List<int>(new List<int> { 0, 0, 0, 0 });
    public Godot.Collections.Array<MultiMeshInstance3D> batches = new Godot.Collections.Array<MultiMeshInstance3D>();
    public ShaderMaterial material;
    public double _clock = 0.0;
    public bool _built = false;

    public void setup(Camp camp)
    {
        if (_built)
        {
            return;
        }
        _built = true;
        Name = "HabitatDiversity";
        Godot.Collections.Dictionary groups = plan(camp.field);
        Godot.Collections.Array<ArrayMesh> meshes = new Godot.Collections.Array<ArrayMesh>();
        for (long form = 0; form < 4; form++)
        {
            for (long variant = 0; variant < 2; variant++)
            {
                meshes.Add(plant_mesh(form, 60011 + form * 113 + variant * 31));
            }
        }
        material = new ShaderMaterial();
        material.Shader = SHADER;
        foreach (Variant key_key in groups.Keys)
        {
            Vector4I key = key_key.AsVector4I();
            Godot.Collections.Array items = groups[key].AsGodotArray();
            MultiMesh mm = new MultiMesh();
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseCustomData = true;
            mm.Mesh = meshes[(int)((long)key.Z * 2 + key.W)];
            mm.InstanceCount = (int)(long)items.Count;
            Aabb bounds = new Aabb();
            for (long i = 0, i_end = (long)items.Count; i < i_end; i++)
            {
                Godot.Collections.Dictionary item = items[(int)i].AsGodotDictionary();
                Transform3D frame = item["frame"].AsTransform3D();
                mm.SetInstanceTransform((int)i, frame);
                mm.SetInstanceCustomData((int)i, item["custom"].AsColor());
                Aabb local = frame * mm.Mesh.GetAabb().Grow(0.14f);
                bounds = i == 0 ? local : bounds.Merge(local);
            }
            mm.CustomAabb = bounds;
            MultiMeshInstance3D node = new MultiMeshInstance3D();
            node.Name = G.format("Habitat_%d_%d_%d_%d", new Godot.Collections.Array { key.X, key.Y, key.Z, key.W });
            node.Multimesh = mm;
            node.MaterialOverride = material;
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            node.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            // Keep broad wet-bank leaves in the mirror; small forest-floor details
            // use the existing cheap grass layer, not every reflection viewport.
            node.Layers = unchecked((uint)(key.Z == (long)HabitatDiversity.Form.RUSH ? 1 : Pond.GRASS_LAYER));
            node.VisibilityRangeEndMargin = 4.0f;
            AddChild(node);
            batches.Add(node);
            counts[key.Z] = (int)(counts[key.Z] + (long)items.Count);
        }
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, HabitatDiversity.MethodName._quality));
        _quality(Quality.Instance.current);
        G.print(G.format("SHOWCASE_HABITAT rosettes=%d fernlets=%d rushes=%d litter=%d batches=%d", new Godot.Collections.Array { counts[0], counts[1], counts[2], counts[3], (long)batches.Count }));
    }

    public static bool placement_allowed(TerrainField field, Vector2 p, double radius)
    {
        if (field.walking_distance(p) < 0.70 + radius || TerrainField.camp_wear(p) > 0.05)
        {
            return false;
        }
        if (p.DistanceTo(TerrainField.DOCK_START) < 4.0 + radius)
        {
            return false;
        }
        Godot.Collections.Array<Vector2> lane = new Godot.Collections.Array<Vector2> { TerrainField.DOCK_START, TerrainField.POND_CENTRE };
        if (TerrainField.distance_to_polyline(p, lane) < 2.8 + radius)
        {
            return false;
        }
        return field.surface_slope(p.X, p.Y) < 0.63;
    }

    public static Godot.Collections.Dictionary plan(TerrainField field, long attempts = ATTEMPTS)
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(811357));
        FastNoiseLite patches = new FastNoiseLite();
        patches.Seed = 7347;
        patches.Frequency = 0.16f;
        List<int> accepted = new List<int>(new List<int> { 0, 0, 0, 0 });
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        for (long i = 0; i < attempts; i++)
        {
            double angle = rng.Randf() * TAU;
            Vector2 p = default;
            if (i % 3 == 0)
            {
                Vector2 shore = TerrainField.shore_point(angle);
                p = shore + (shore - TerrainField.POND_CENTRE).Normalized() * rng.RandfRange(-0.18f, 6.0f);
            }
            else
            {
                p = new Vector2((float)cos(angle), (float)sin(angle)) * (float)sqrt(rng.Randf()) * (float)EXTENT;
            }
            double width = rng.RandfRange(0.60f, 1.3f);
            if (!placement_allowed(field, p, 0.48 * width))
            {
                continue;
            }
            double y = field.surface_height(p.X, p.Y);
            double above = y - TerrainField.WATER_LEVEL;
            if (above < -0.09)
            {
                continue;
            }
            double shade = field.woodland_cover(p.X, p.Y);
            double colony = smoothstep(-0.4, 0.38, patches.GetNoise2D(p.X, p.Y));
            if (rng.Randf() > colony * 0.82)
            {
                continue;
            }
            HabitatDiversity.Form form = HabitatDiversity.Form.ROSETTE;
            if (above < 0.34)
            {
                form = HabitatDiversity.Form.RUSH;
            }
            else if (shade > 0.42)
            {
                form = rng.Randf() < 0.46 ? HabitatDiversity.Form.FERNLET : HabitatDiversity.Form.LITTER;
            }
            else if (rng.Randf() > 0.32)
            {
                continue;
            }
            if (G.op(">=", accepted[(int)form], TARGETS[(int)form]).AsBool())
            {
                continue;
            }
            double yaw = rng.Randf() * TAU;
            Vector3 normal = field.normal(p.X, p.Y);
            // Tilt only low litter to the terrain; live plants grow against gravity.
            Basis basis = form == HabitatDiversity.Form.LITTER ? new Basis(new Quaternion(Vector3.Up, normal)) : Basis.Identity;
            basis = basis * new Basis(Vector3.Up, (float)yaw) * Basis.FromScale(new Vector3((float)width, rng.RandfRange(0.68f, 1.20f), (float)width));
            Vector3 origin = new Vector3(p.X, (float)(y + (form == HabitatDiversity.Form.LITTER ? 0.009 : -0.012)), p.Y);
            Vector4I key = new Vector4I((int)floori(p.X / CELL_SIZE), (int)floori(p.Y / CELL_SIZE), (int)form, (int)((long)rng.Randi() % 2));
            if (!groups.ContainsKey(key))
            {
                groups[key] = new Godot.Collections.Array();
            }
            double tint = rng.RandfRange(0.83f, 1.12f);
            G.Call(groups[key], "append", new Godot.Collections.Dictionary { { (StringName)"frame", new Transform3D(basis, origin) }, { (StringName)"custom", new Color(rng.Randf(), (float)tint, (float)(tint * rng.RandfRange(0.95f, 1.05f)), (float)(tint * rng.RandfRange(0.83f, 1.0f))) } });
            accepted[(int)form] += 1;
        }
        return groups;
    }

    public static ArrayMesh plant_mesh(long form, long seed_value)
    {
        /// Geometry uses continuous curled leaves and tapered stems. Leaf colour is
        /// stored per vertex, so no new external atlas or texture import is required.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        switch (form)
        {
            case (long)HabitatDiversity.Form.ROSETTE:
                for (long i = 0; i < 7; i++)
                {
                    double a = i * 2.39996 + rng.RandfRange(-0.20f, 0.20f);
                    Vector3 @out = new Vector3((float)cos(a), 0.0f, (float)sin(a));
                    double h = rng.RandfRange(0.04f, 0.13f);
                    Vector3 at = @out * 0.05f + Vector3.Up * (float)h;
                    mb.add_tube(new Godot.Collections.Array<Vector3> { Vector3.Zero, at }, new Godot.Collections.Array<double> { 0.002, 0.001 }, 4, new Color(0.09f, 0.18f, 0.055f));
                    _leaf(mb, at, @out, rng.RandfRange(0.13f, 0.23f), 0.055, 0.065, new Color(0.12f, 0.24f, 0.065f), false);
                }
                break;
            case (long)HabitatDiversity.Form.FERNLET:
                for (long frond = 0; frond < 6; frond++)
                {
                    double a2 = frond * 2.39996;
                    Vector3 out2 = new Vector3((float)cos(a2), 0, (float)sin(a2));
                    double reach = rng.RandfRange(0.20f, 0.39f);
                    Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
                    Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
                    for (long j = 0; j < 15; j++)
                    {
                        double t = j / 14.0;
                        Vector3 at2 = out2 * (float)t * (float)reach + Vector3.Up * (float)(0.018 + sin(t * PI * 0.76) * 0.17);
                        points.Add(at2);
                        radii.Add(0.002 * (1.0 - t * 0.70));
                        if (j > 0)
                        {
                            foreach (Variant side in new Godot.Collections.Array { -1.0, 1.0 })
                            {
                                _leaf(mb, at2, out2.Rotated(Vector3.Up, G.op("*", side, 1.05).AsSingle()), 0.087 * pow(1.0 - t * 0.88, 0.8), 0.014, 0.008, new Color(0.08f, 0.22f, 0.055f), false);
                            }
                        }
                    }
                    mb.add_tube(points, radii, 4, new Color(0.10f, 0.19f, 0.06f));
                }
                break;
            case (long)HabitatDiversity.Form.RUSH:
                for (long i2 = 0; i2 < 11; i2++)
                {
                    double a3 = i2 * 2.39996;
                    Vector3 out3 = new Vector3((float)cos(a3), 0, (float)sin(a3));
                    double h2 = rng.RandfRange(0.38f, 0.82f);
                    double bend = rng.RandfRange(0.06f, 0.17f);
                    Godot.Collections.Array<Vector3> points2 = new Godot.Collections.Array<Vector3>();
                    Godot.Collections.Array<double> radii2 = new Godot.Collections.Array<double>();
                    for (long j2 = 0; j2 < 6; j2++)
                    {
                        double t2 = j2 / 5.0;
                        points2.Add(out3 * (float)(0.022 + bend * t2 * t2) + Vector3.Up * (float)h2 * (float)t2);
                        radii2.Add(lerpf(0.0038, 0.0005, t2));
                    }
                    mb.add_tube(points2, radii2, 5, new Color(0.15f, 0.24f, 0.085f));
                    if (i2 % 3 == 0)
                    {
                        Vector3 tip = points2[4];
                        for (long seed = 0; seed < 4; seed++)
                        {
                            Vector3 at3 = tip + out3.Rotated(Vector3.Up, (float)(seed * 1.7)) * 0.014f + Vector3.Up * (float)seed * 0.003f;
                            MeadowPlants._flower_core(mb, at3, 0.005, 0.85, new Color(0.24f, 0.15f, 0.05f));
                        }
                    }
                }
                break;
            case (long)HabitatDiversity.Form.LITTER:
                for (long i3 = 0; i3 < 5; i3++)
                {
                    double a4 = rng.Randf() * TAU;
                    Vector3 out4 = new Vector3((float)cos(a4), 0, (float)sin(a4));
                    Vector3 at4 = out4 * rng.RandfRange(0.0f, 0.16f) + Vector3.Up * rng.RandfRange(0.004f, 0.014f);
                    _leaf(mb, at4, out4, rng.RandfRange(0.07f, 0.14f), 0.037, 0.008, new Color(0.23f, 0.115f, 0.045f).Lerp(new Color(0.39f, 0.24f, 0.08f), rng.Randf()), true);
                }
                break;
        }
        mb.recompute_normals();
        mb.recompute_tangents();
        return mb.commit(null, true);
    }

    public static void _leaf(MeshBuilder mb, Vector3 at, Vector3 @out, double length, double width, double lift, Color tint, bool lobed)
    {
        long start = mb.vertex_count();
        Vector3 side = new Vector3(-@out.Z, 0, @out.X);
        for (long row = 0; row < 9; row++)
        {
            double t = row / 8.0;
            double w = maxf(sin(t * PI), 0.01) * width;
            if (lobed)
            {
                w *= 0.78 + 0.22 * cos(t * PI * 8.0);
            }
            for (long col = 0; col < 3; col++)
            {
                double u = (double)col - 1.0;
                Vector3 p = at + @out * (float)t * (float)length + side * (float)u * (float)w + Vector3.Up * (float)(sin(t * PI) * lift + u * u * sin(t * PI) * width * 0.16);
                mb.add_vertex(p, Vector3.Up, new Vector2((float)(col / 2.0), (float)t), tint * (float)(col == 1 ? 0.90 : 1.0));
            }
        }
        for (long row2 = 0; row2 < 8; row2++)
        {
            for (long col2 = 0; col2 < 2; col2++)
            {
                long a = start + row2 * 3 + col2;
                mb.add_quad_facing(a, a + 1, a + 4, a + 3, Vector3.Up);
            }
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (material != null)
        {
            material.SetShaderParameter("habitat_clock", _clock);
        }
    }

    public void _quality(QualityPreset p)
    {
        material.SetShaderParameter("fade_start", 85.0 * p.foliage_distance);
        material.SetShaderParameter("fade_end", 140.0 * p.foliage_distance);
        // Range-cull each cell once its plants have faded out (range is measured to
        // the bounds' centre, so add half the cell bounds' diagonal).
        foreach (MultiMeshInstance3D node in batches)
        {
            node.VisibilityRangeEnd = (float)(140.0 * p.foliage_distance + node.Multimesh.CustomAabb.Size.Length() * 0.5 + 2.0);
        }
    }

    public override void _ExitTree()
    {
        if (Quality.Instance.IsConnected(Quality.SignalName.preset_changed, new Callable(this, HabitatDiversity.MethodName._quality)))
        {
            Quality.Instance.Disconnect(Quality.SignalName.preset_changed, new Callable(this, HabitatDiversity.MethodName._quality));
        }
    }
    private static Shader _preload_habitat => _preload_habitat_cache ??= Content.Load<Shader>("res://shaders/habitat.gdshader");
    private static Shader _preload_habitat_cache;
}
