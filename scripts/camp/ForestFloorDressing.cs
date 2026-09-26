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

/// Coarse forest-floor structure that makes the woodland read as an ecosystem,
/// not trees planted into grass. Placement is deterministic and habitat-driven.
/// Fallen logs receive simple collision. Low decorative roots are conformed to
/// the terrain and kept off the walked paths; they are not tall obstacles.
public partial class ForestFloorDressing : Node3D
{
    public const long FALLEN_LOG_TARGET = 58;
    public const long FALLEN_LOG_ATTEMPTS = 1800;
    public const long MOSS_STONE_TARGET = 260;
    public const long MOSS_STONE_ATTEMPTS = 3600;
    public const double ROOT_MAX_DISTANCE = 76.0;

    public Camp _camp;
    public TerrainField _field;
    public ScenePlan _plan;
    public bool _built = false;

    public override void _Ready()
    {
        Name = "ForestFloorDressing";
        SetProcess(false);
    }

    public void setup(Camp camp)
    {
        if (_built)
        {
            return;
        }
        _camp = camp;
        _field = camp.field;
        _plan = camp.plan;
        _build();
        _built = true;
    }

    public void _build()
    {
        _build_buttress_roots();
        _build_fallen_logs();
        _build_moss_stones();
    }

    public void _build_buttress_roots()
    {
        // --------------------------------------------------------------- tree grounding
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        ShaderMaterial material = PropMaterials.bark("bark_oak", 0.62);
        long count = 0;
        foreach (ScenePlan.TreeEntry tree in _plan.near_trees())
        {
            if (tree.position.Length() > ROOT_MAX_DISTANCE || !new Godot.Collections.Array { (long)TreeSpecies.Kind.OAK, (long)TreeSpecies.Kind.ALDER }.Contains((long)tree.kind))
            {
                continue;
            }
            if (tree.position.DistanceTo(TerrainField.FIRE) < 11.0)
            {
                continue;
            }
            RandomNumberGenerator rng = new RandomNumberGenerator();
            rng.Seed = unchecked((ulong)(tree.seed_value ^ 0x72A4E1));
            long roots = tree.kind == TreeSpecies.Kind.OAK ? 5 : 3;
            double phase = rng.Randf() * TAU;
            for (long i = 0; i < roots; i++)
            {
                double angle = phase + i * TAU / (double)roots + rng.RandfRange(-0.30f, 0.30f);
                Vector2 direction = new Vector2((float)cos(angle), (float)sin(angle));
                double length = rng.RandfRange(0.80f, 1.75f) * tree.scale;
                double radius = rng.RandfRange(0.12f, 0.22f) * tree.scale;
                Godot.Collections.Dictionary curve = root_curve(_field, tree.position, direction, length, radius, rng.RandfRange(-0.18f, 0.18f));
                bool clear = true;
                foreach (Variant point_item in G.Iter(curve["points"]))
                {
                    Vector3 point = point_item.AsVector3();
                    if (_field.walking_distance(new Vector2(point.X, point.Z)) < radius + 0.35)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                {
                    continue;
                }
                Vector2I key = new Vector2I((int)floori(tree.position.X / 24.0), (int)floori(tree.position.Y / 24.0));
                if (!groups.ContainsKey(key))
                {
                    groups[key] = new MeshBuilder();
                }
                MeshBuilder builder = groups[key].As<MeshBuilder>();
                builder.add_tube(curve["points"].AsGodotArray<Vector3>(), curve["radii"].AsGodotArray<double>(), 10, Colors.White, 1.0, 1.0 / 1.2, rng.Randf(), true);
                count += 1;
            }
        }
        foreach (Variant key2 in groups.Keys)
        {
            MeshInstance3D node = new MeshInstance3D();
            node.Name = G.format("RootFlare_%d_%d", new Godot.Collections.Array { G.Index(key2, "x"), G.Index(key2, "y") });
            node.Mesh = G.Call(groups[key2], "commit", default(Variant), true).As<Mesh>();
            node.MaterialOverride = material;
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            node.GIMode = GeometryInstance3D.GIModeEnum.Static;
            AddChild(node);
        }
        G.print(G.format("Forest floor: %d terrain-conforming tapered roots", count));
    }

    public static Godot.Collections.Dictionary root_curve(TerrainField field, Vector2 origin, Vector2 direction, double length, double radius, double bend)
    {
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        Vector2 side = new Vector2(-direction.Y, direction.X);
        for (long i = 0; i < 11; i++)
        {
            double t = i / 10.0;
            Vector2 at = origin + direction * (float)(0.07 + length * t) + side * (float)sin(t * PI) * (float)bend;
            double r = radius * pow(1.0 - t, 1.8) + 0.003;
            double bury = r * 0.55 + smoothstep(0.8, 1.0, t) * 0.012;
            points.Add(new Vector3(at.X, (float)(field.surface_height(at.X, at.Y) - bury), at.Y));
            radii.Add(r);
        }
        return new Godot.Collections.Dictionary { { (StringName)"points", points }, { (StringName)"radii", radii } };
    }

    public static ArrayMesh fractured_log(double radius, long seed_value)
    {
        /// Apply the same fracture field to bark and cap boundary positions. The
        /// end-grain rim becomes irregular without separating the two surfaces.
        ArrayMesh source = PropMeshes.bark_log_mesh(1.0, radius, seed_value, 0.09);
        ArrayMesh result = new ArrayMesh();
        for (long surface = 0, surface_end = source.GetSurfaceCount(); surface < surface_end; surface++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays((int)surface);
            MeshBuilder builder = new MeshBuilder();
            builder.vertices = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
            builder.normals = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Normal]);
            builder.uvs = G.ListFromVariant<Vector2>(arrays[(int)Mesh.ArrayType.TexUV]);
            builder.colors = G.ListFromVariant<Color>(arrays[(int)Mesh.ArrayType.Color]);
            builder.indices = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
            for (long i = 0, i_end = (long)builder.vertices.Count; i < i_end; i++)
            {
                Vector3 point = builder.vertices[(int)i];
                double end = smoothstep(0.34, 0.49, absf(point.X));
                double angle = atan2(point.Z, point.Y);
                double grain = sin(angle * 3.0 + seed_value * 0.13) * 0.6 + cos(angle * 7.0 - seed_value * 0.07) * 0.4;
                point.X = (float)(point.X + signf(point.X) * end * grain * radius * 0.23);
                builder.vertices[(int)i] = point;
            }
            builder.recompute_normals();
            builder.recompute_tangents();
            result = builder.commit(null, true, result);
        }
        return result;
    }

    public void _build_fallen_logs()
    {
        // ------------------------------------------------------------------- dead wood
        ShaderMaterial bark = PropMaterials.bark("bark_oak", 0.72);
        ShaderMaterial end_grain = PropMaterials.wood(new Color(0.58f, 0.48f, 0.34f), 0.88, 0.18, 1.15);
        Godot.Collections.Array<ArrayMesh> meshes = new Godot.Collections.Array<ArrayMesh>();
        for (long i = 0; i < 4; i++)
        {
            ArrayMesh mesh = fractured_log(0.11 + (double)i * 0.012, 9600 + i * 37);
            mesh.SurfaceSetMaterial(0, bark);
            if (mesh.GetSurfaceCount() > 1)
            {
                mesh.SurfaceSetMaterial(1, end_grain);
            }
            meshes.Add(mesh);
        }
        Godot.Collections.Array<Godot.Collections.Array> groups = new Godot.Collections.Array<Godot.Collections.Array> { new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array() };
        Godot.Collections.Array<Godot.Collections.Dictionary> collisions = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(97231));
        long placed = 0;
        for (long attempt = 0; attempt < FALLEN_LOG_ATTEMPTS; attempt++)
        {
            if (placed >= FALLEN_LOG_TARGET)
            {
                break;
            }
            double angle = rng.Randf() * TAU;
            double radius = sqrt(lerpf(30.0 * 30.0, 118.0 * 118.0, rng.Randf()));
            Vector2 p = new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            if (!_forest_floor_allowed(p, 4.2))
            {
                continue;
            }
            double cover = _field.woodland_cover(p.X, p.Y);
            double shore = _shore_offset(p);
            double wet_bonus = 1.0 - smoothstep(6.0, 26.0, shore);
            if (rng.Randf() > clampf(cover * 0.72 + wet_bonus * 0.20, 0.0, 0.92))
            {
                continue;
            }
            double yaw = rng.Randf() * TAU;
            double length = rng.RandfRange(1.5f, 3.7f);
            double radius_scale = rng.RandfRange(0.85f, 1.40f);
            Vector2 direction2 = new Vector2((float)cos(yaw), (float)sin(yaw));
            Vector2 a = p - direction2 * (float)length * 0.42f;
            Vector2 b = p + direction2 * (float)length * 0.42f;
            if (!_forest_floor_allowed(a, 3.5) || !_forest_floor_allowed(b, 3.5))
            {
                continue;
            }
            double ya = _field.height_fast(a.X, a.Y);
            double yb = _field.height_fast(b.X, b.Y);
            Vector3 x_axis = new Vector3(direction2.X, (float)((yb - ya) / maxf(length * 0.84, 0.1)), direction2.Y).Normalized();
            Vector3 up_hint = _field.normal(p.X, p.Y, 0.45);
            Vector3 z_axis = x_axis.Cross(up_hint).Normalized();
            Vector3 y_axis = z_axis.Cross(x_axis).Normalized();
            Basis basis = new Basis(x_axis, y_axis, z_axis) * Basis.FromScale(new Vector3((float)length, (float)radius_scale, (float)radius_scale));
            long variant = (long)rng.Randi() % (long)meshes.Count;
            // Support the whole rotated timber, not just its middle. A small
            // intentional burial gives deadwood a settled contact with the soil.
            double y = -INF;
            for (long surface = 0, surface_end = meshes[(int)variant].GetSurfaceCount(); surface < surface_end; surface++)
            {
                foreach (Variant vertex_item in G.Iter(meshes[(int)variant].SurfaceGetArrays((int)surface)[(int)Mesh.ArrayType.Vertex]))
                {
                    Vector3 vertex = vertex_item.AsVector3();
                    Vector3 local = basis * vertex;
                    y = maxf(y, _field.surface_height((double)p.X + local.X, (double)p.Y + local.Z) - local.Y);
                }
            }
            y -= 0.035;
            Transform3D transform = new Transform3D(basis, new Vector3(p.X, (float)y, p.Y));
            groups[(int)variant].Add(transform);
            collisions.Add(new Godot.Collections.Dictionary { { "position", new Vector3(p.X, (float)y, p.Y) }, { "basis", new Basis(x_axis, y_axis, z_axis) }, { "length", length * 0.84 }, { "radius", (0.11 + (double)variant * 0.012) * radius_scale } });
            placed += 1;
        }
        for (long i2 = 0, i_end = (long)meshes.Count; i2 < i_end; i2++)
        {
            _add_multimesh(G.format("FallenLogs_%d", i2), meshes[(int)i2], null, groups[(int)i2], true);
        }
        foreach (Godot.Collections.Dictionary info in collisions)
        {
            _add_log_collision(info);
        }
        G.print(G.format("Forest floor: %d fallen logs", placed));
    }

    public void _add_log_collision(Godot.Collections.Dictionary info)
    {
        StaticBody3D body = new StaticBody3D();
        body.Name = "DeadwoodCollision";
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"wood");
        CollisionShape3D shape = new CollisionShape3D();
        CapsuleShape3D capsule = new CapsuleShape3D();
        capsule.Radius = (float)maxf(G.to_float(info["radius"]) * 0.78, 0.07);
        capsule.Height = (float)maxf(G.to_float(info["length"]), capsule.Radius * 2.1);
        shape.Shape = capsule;
        // CapsuleShape3D runs along local Y; rotate local Y onto the log's local X.
        Basis align = new Basis(Vector3.Forward, (float)(PI * 0.5));
        body.Transform = new Transform3D(G.op("*", info["basis"], align).AsBasis(), info["position"].AsVector3());
        body.AddChild(shape);
        AddChild(body);
    }

    public void _build_moss_stones()
    {
        // ---------------------------------------------------------------- mossy stones
        Godot.Collections.Array<ShaderMaterial> materials = new Godot.Collections.Array<ShaderMaterial> { PropMaterials.triplanar("rock", new Color(0.56f, 0.60f, 0.54f), 0.82, 0.58), PropMaterials.triplanar("rock", new Color(0.48f, 0.52f, 0.47f), 0.74, 0.76) };
        Godot.Collections.Array<ArrayMesh> meshes = new Godot.Collections.Array<ArrayMesh>();
        for (long i = 0; i < 4; i++)
        {
            meshes.Add(PropMeshes.rock(9800 + i * 23, 1.0));
        }
        Godot.Collections.Array<Godot.Collections.Array> groups = new Godot.Collections.Array<Godot.Collections.Array> { new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array(), new Godot.Collections.Array() };
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(98117));
        long placed = 0;
        for (long attempt = 0; attempt < MOSS_STONE_ATTEMPTS; attempt++)
        {
            if (placed >= MOSS_STONE_TARGET)
            {
                break;
            }
            double angle = rng.Randf() * TAU;
            double radius = sqrt(lerpf(15.0 * 15.0, 115.0 * 115.0, rng.Randf()));
            Vector2 p = new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            if (!_forest_floor_allowed(p, 1.6))
            {
                continue;
            }
            double cover = _field.woodland_cover(p.X, p.Y);
            double shore = _shore_offset(p);
            double moisture = maxf(cover, 1.0 - smoothstep(4.0, 30.0, shore));
            if (rng.Randf() > 0.20 + moisture * 0.52)
            {
                continue;
            }
            double s = rng.RandfRange(0.08f, 0.34f) * lerpf(0.85, 1.28, moisture);
            Vector3 normal = _field.normal(p.X, p.Y, 0.28);
            Vector3 tangent = new Vector3((float)cos(angle + rng.RandfRange(-1.6f, 1.6f)), 0.0f, (float)sin(angle + rng.RandfRange(-1.6f, 1.6f)));
            tangent = (tangent - normal * tangent.Dot(normal)).Normalized();
            Vector3 bitangent = tangent.Cross(normal).Normalized();
            Basis basis = new Basis(tangent, normal, bitangent);
            basis = basis.Rotated(normal, (float)(rng.Randf() * TAU)) * Basis.FromScale(new Vector3((float)(s * rng.RandfRange(0.85f, 1.28f)), (float)(s * rng.RandfRange(0.55f, 0.92f)), (float)s));
            double y = _field.height_fast(p.X, p.Y);
            Transform3D transform = new Transform3D(basis, new Vector3(p.X, (float)(y - s * rng.RandfRange(0.22f, 0.42f)), p.Y));
            long mesh_index = (long)rng.Randi() % (long)meshes.Count;
            long mat_index = moisture > 0.62 ? 1 : 0;
            groups[(int)(mesh_index * 2 + mat_index)].Add(transform);
            placed += 1;
        }
        for (long mesh_index2 = 0, mesh_index_end = (long)meshes.Count; mesh_index2 < mesh_index_end; mesh_index2++)
        {
            for (long mat_index2 = 0, mat_index_end = (long)materials.Count; mat_index2 < mat_index_end; mat_index2++)
            {
                long group_index = mesh_index2 * 2 + mat_index2;
                _add_multimesh(G.format("MossStones_%d_%d", new Godot.Collections.Array { mesh_index2, mat_index2 }), meshes[(int)mesh_index2], materials[(int)mat_index2], groups[(int)group_index], true);
            }
        }
        G.print(G.format("Forest floor: %d moss stones", placed));
    }

    public bool _forest_floor_allowed(Vector2 p, double trail_clearance)
    {
        // ------------------------------------------------------------------- helpers
        if (p.DistanceTo(TerrainField.FIRE) < 11.0 || p.DistanceTo(TerrainField.TENT) < 8.0)
        {
            return false;
        }
        if (_field.trail_distance(p.X, p.Y) < trail_clearance)
        {
            return false;
        }
        if (TerrainField.camp_wear(p) > 0.10)
        {
            return false;
        }
        double y = _field.height_fast(p.X, p.Y);
        if (y < TerrainField.WATER_LEVEL + 0.12)
        {
            return false;
        }
        if (_field.slope(p.X, p.Y) > 0.62)
        {
            return false;
        }
        return true;
    }

    public static double _shore_offset(Vector2 pos)
    {
        Vector2 delta = pos - TerrainField.POND_CENTRE;
        return delta.Length() - TerrainField.pond_radius_at(atan2(delta.Y, delta.X));
    }

    public MultiMeshInstance3D _add_multimesh(string node_name, ArrayMesh mesh, Material material, Godot.Collections.Array transforms, bool cast_shadow)
    {
        if ((transforms.Count == 0))
        {
            return null;
        }
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i].AsTransform3D());
        }
        Aabb local_bounds = mesh.GetAabb().Grow(0.18f);
        Aabb bounds = G.op("*", transforms[0], local_bounds).AsAabb();
        for (long i2 = 1, i_end2 = (long)transforms.Count; i2 < i_end2; i2++)
        {
            bounds = bounds.Merge(G.op("*", transforms[(int)i2], local_bounds).AsAabb());
        }
        mm.CustomAabb = bounds;
        MultiMeshInstance3D node = new MultiMeshInstance3D();
        node.Name = node_name;
        node.Multimesh = mm;
        if (material != null)
        {
            node.MaterialOverride = material;
        }
        node.CastShadow = cast_shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        node.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(node);
        return node;
    }
}
