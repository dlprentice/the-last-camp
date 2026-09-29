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

/// Instantiates the planned trees. Near trees are individual MeshInstance3Ds
/// (per-instance LOD, culling and shader parameters, plus trunk collision);
/// the distant forest is grouped by variant and spatial cell for culling.
public partial class Forest : Node3D
{
    public const long VARIANTS_PER_SPECIES = 6;
    public const double TRUNK_COLLISION_HEIGHT = 5.0;
    public const double FAR_CELL_SIZE = 48.0;

    public TerrainField field;
    public ScenePlan plan;
    // These records belong to C#, not the native Variant/ref-count bridge.
    // Keep their meshes strongly reachable for the forest lifetime.
    public readonly Dictionary<long, List<TreeGenerator.Result>> variants = new();
    public Godot.Collections.Dictionary bark_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary leaf_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary far_bark_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary far_leaf_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary far_leaf_variant_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Array<MeshInstance3D> near_instances = new Godot.Collections.Array<MeshInstance3D>();
    public Godot.Collections.Array<MultiMeshInstance3D> far_multimeshes = new Godot.Collections.Array<MultiMeshInstance3D>();
    public Godot.Collections.Dictionary ridge_leaf_variant_materials = new Godot.Collections.Dictionary();
    public Godot.Collections.Array<MultiMeshInstance3D> ridge_multimeshes = new Godot.Collections.Array<MultiMeshInstance3D>();
    public readonly List<(TreeSpecies.Kind Kind, TreeGenerator.Result Mesh, string Key, Godot.Collections.Array<Transform3D> Transforms, int First)> ridge_groups = new();
    public readonly List<(TreeSpecies.Kind Kind, TreeGenerator.Result Mesh, string Key, Godot.Collections.Array<Transform3D> Transforms, int First)> far_groups = new();
    public double _foliage_distance = 1.0;
    public Godot.Collections.Array<Godot.Collections.Dictionary> near_records = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    public Godot.Collections.Array<Godot.Collections.Dictionary> impostor_nodes = new Godot.Collections.Array<Godot.Collections.Dictionary>();

    public Forest(TerrainField p_field, ScenePlan p_plan)
    {
        field = p_field;
        plan = p_plan;
        Name = "Forest";
    }

    public Forest()
    {
    }

    public void build()
    {
        _build_variants();
        _build_near();
        _build_far();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Forest.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
    }

    public void _build_variants()
    {
        foreach (long kind in G.enum_values<TreeSpecies.Kind>())
        {
            TreeSpecies species = TreeSpecies.by_kind((TreeSpecies.Kind)kind);
            var list = new List<TreeGenerator.Result>();
            for (long v = 0; v < VARIANTS_PER_SPECIES; v++)
            {
                TreeGenerator gen = new TreeGenerator();
                list.Add(gen.generate(TreeSpecies.variant((TreeSpecies.Kind)kind, v), 9000 + kind * 100 + v * 17));
            }
            variants[kind] = list;
            if (!bark_materials.ContainsKey(species.bark_set))
            {
                bark_materials[species.bark_set] = _make_bark_material(species.bark_set, false);
                far_bark_materials[species.bark_set] = _make_bark_material(species.bark_set, true);
            }
            if (species.has_leaves() && !leaf_materials.ContainsKey(species.leaf_atlas))
            {
                leaf_materials[species.leaf_atlas] = _make_leaf_material(species, false);
                far_leaf_materials[species.leaf_atlas] = _make_leaf_material(species, true);
            }
        }
    }

    public TreeGenerator.Result _variant_for(ScenePlan.TreeEntry entry)
    {
        var list = variants[(long)entry.kind];
        return list[(int)(absi(entry.seed_value) % (long)list.Count)];
    }

    public Transform3D _tree_transform(ScenePlan.TreeEntry entry)
    {
        Vector2 pos = entry.position;
        // Keep the authored camp roots fixed; outside the fine inner mesh, roots
        // must follow the rendered triangles rather than the analytic hill height.
        bool outer = absf(pos.X) > TerrainBuilder.INNER_UNIFORM_HALF || absf(pos.Y) > TerrainBuilder.INNER_UNIFORM_HALF;
        double ground = outer ? field.surface_height(pos.X, pos.Y) : field.height(pos.X, pos.Y);
        double y = ground - 0.12 * entry.scale;
        Basis basis = new Basis(Vector3.Up, (float)entry.rotation).Scaled(Vector3.One * (float)entry.scale);
        return new Transform3D(basis, new Vector3(entry.position.X, (float)y, entry.position.Y));
    }

    public void _build_near()
    {
        foreach (ScenePlan.TreeEntry entry in plan.near_trees())
        {
            TreeSpecies species = TreeSpecies.by_kind(entry.kind);
            TreeGenerator.Result result = _variant_for(entry);
            Transform3D xform = _tree_transform(entry);
            Vector3 tint = _tint_for(entry);

            MeshInstance3D bark = new MeshInstance3D();
            bark.Name = G.format("%s_bark", species.name);
            bark.Mesh = result.bark;
            bark.MaterialOverride = bark_materials[species.bark_set].As<Material>();
            bark.Transform = xform;
            bark.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            bark.GIMode = GeometryInstance3D.GIModeEnum.Static;
            bark.SetInstanceShaderParameter("tree_height", result.height);
            bark.SetInstanceShaderParameter("tint", new Color(tint.X, tint.Y, tint.Z));
            AddChild(bark);
            near_instances.Add(bark);
            Godot.Collections.Dictionary record = new Godot.Collections.Dictionary { { "entry", entry }, { "xform", xform }, { "bark", bark }, { "leaves", default(Variant) } };
            near_records.Add(record);

            if (result.leaves != null)
            {
                MeshInstance3D leaves = new MeshInstance3D();
                leaves.Name = G.format("%s_leaves", species.name);
                leaves.Mesh = result.leaves;
                leaves.MaterialOverride = leaf_materials[species.leaf_atlas].As<Material>();
                leaves.Transform = xform;
                leaves.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                leaves.GIMode = GeometryInstance3D.GIModeEnum.Static;
                leaves.CustomAabb = _leaf_aabb(result);
                leaves.SetInstanceShaderParameter("tree_height", result.height);
                leaves.SetInstanceShaderParameter("crown", new Color(result.crown_center.X, result.crown_center.Y, result.crown_center.Z, (float)result.crown_radius));
                Color leaf_tint = species.leaf_tint;
                double v = hash_unit(entry.seed_value);
                leaf_tint = leaf_tint.Lerp(new Color(1.08f, 1.0f, 0.82f), (float)((v - 0.5) * species.leaf_color_variation * 4.0));
                leaves.SetInstanceShaderParameter("tint", leaf_tint);
                AddChild(leaves);
                near_instances.Add(leaves);
                record["leaves"] = leaves;
            }

            _add_trunk_collision(xform, result.trunk_radius * entry.scale, result.height * entry.scale);
        }
    }

    public Aabb leaf_bounds(TreeGenerator.Result result)
    {
        return _leaf_aabb(result);
    }

    public Aabb _leaf_aabb(TreeGenerator.Result result)
    {
        double r = result.crown_radius * 1.6 + 1.0;
        return new Aabb(new Vector3((float)-r, -0.5f, (float)-r), new Vector3((float)(r * 2.0), (float)(result.height + r), (float)(r * 2.0)));
    }

    public void _add_trunk_collision(Transform3D xform, double radius, double height)
    {
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"wood");
        CollisionShape3D shape = new CollisionShape3D();
        CylinderShape3D cylinder = new CylinderShape3D();
        cylinder.Radius = (float)maxf(radius * 1.15, 0.12);
        cylinder.Height = (float)minf(TRUNK_COLLISION_HEIGHT, height);
        shape.Shape = cylinder;
        shape.Position = new Vector3(0.0f, (float)(cylinder.Height * 0.5), 0.0f);
        body.AddChild(shape);
        body.Transform = new Transform3D(Basis.Identity, xform.Origin);
        AddChild(body);
    }

    public void _build_far()
    {
        // A world-wide MultiMesh keeps the entire forest alive whenever any part
        // is visible, and its near edge prevents useful mesh LOD. Spatial cells
        // retain the same trees while letting the renderer cull and simplify them.
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        foreach (ScenePlan.TreeEntry entry in plan.far_trees())
        {
            var list = variants[(long)entry.kind];
            long index = absi(entry.seed_value) % (long)list.Count;
            string key = G.format("%d_%d_%d_%d", new Godot.Collections.Array { (long)entry.kind, index, floori(entry.position.X / FAR_CELL_SIZE), floori(entry.position.Y / FAR_CELL_SIZE) });
            if (!groups.ContainsKey(key))
            {
                groups[key] = new Godot.Collections.Dictionary { { (StringName)"kind", (long)entry.kind }, { (StringName)"index", index }, { (StringName)"entries", new Godot.Collections.Array() } };
            }
            G.Call(G.Index(groups[key], "entries"), "append", entry);
            TreeGenerator.Result result = _variant_for(entry);
            _add_trunk_collision(_tree_transform(entry), result.trunk_radius * entry.scale, result.height * entry.scale);
        }

        foreach (Variant key2 in groups.Keys)
        {
            Godot.Collections.Dictionary group = groups[key2].AsGodotDictionary();
            TreeSpecies species = TreeSpecies.by_kind((TreeSpecies.Kind)group["kind"].AsInt64());
            TreeGenerator.Result result2 = variants[group["kind"].AsInt64()][group["index"].AsInt32()];
            Godot.Collections.Array entries = group["entries"].AsGodotArray();
            Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
            Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
            foreach (Variant entry2 in entries)
            {
                transforms.Add(_tree_transform(entry2.As<ScenePlan.TreeEntry>()));
                customs.Add(new Color((float)(result2.height / 40.0), (float)(result2.crown_center.Y / 40.0), (float)(result2.crown_radius / 20.0), (float)hash_unit(G.Index(entry2, "seed_value").AsInt64())));
            }
            far_groups.Add((species.kind, result2, TreeImpostors.key_for(species.kind, group["index"].AsInt64()), transforms, far_multimeshes.Count));
            _add_far_multimesh(result2.bark, far_bark_materials[species.bark_set].As<Material>(), transforms, customs, true);
            if (result2.leaves != null)
            {
                // Each batch uses one mesh, so its actual off-centre crown can be
                // shared by that material instead of losing its X/Z to custom data.
                string material_key = G.format("%d_%d", new Godot.Collections.Array { G.to_int(group["kind"]), group["index"] });
                if (!far_leaf_variant_materials.ContainsKey(material_key))
                {
                    ShaderMaterial variant_mat = (ShaderMaterial)far_leaf_materials[species.leaf_atlas].As<ShaderMaterial>().Duplicate();
                    variant_mat.SetShaderParameter("crown_offset", new Vector2(result2.crown_center.X, result2.crown_center.Z));
                    far_leaf_variant_materials[material_key] = variant_mat;
                }
                ShaderMaterial leaf_mat = far_leaf_variant_materials[material_key].As<ShaderMaterial>();
                _add_far_multimesh(result2.leaves, leaf_mat, transforms, customs, false);
            }
        }
    }

    public void add_ridge_group(TreeSpecies.Kind kind, TreeGenerator.Result result, string material_key, Godot.Collections.Array<Transform3D> transforms)
    {
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            customs.Add(new Color((float)(result.height / 40.0), (float)(result.crown_center.Y / 40.0), (float)(result.crown_radius / 20.0), (float)hash_unit(i * 7919 + (long)material_key.Hash() % 1000 + (long)kind * 17)));
        }
        if (Game.Instance?.has_flag("unbatched-canopy") != true)
        {
            foreach (var cell in Enumerable.Range(0, transforms.Count).GroupBy(i => canopy_cell(transforms[i].Origin)))
                add_ridge_cell(kind, result, material_key,
                    new Godot.Collections.Array<Transform3D>(cell.Select(i => transforms[i])),
                    new Godot.Collections.Array<Color>(cell.Select(i => customs[i])));
        }
        else add_ridge_cell(kind, result, material_key, transforms, customs);
    }

    public static Vector2I canopy_cell(Vector3 position) => new((int)Math.Floor(position.X / 64), (int)Math.Floor(position.Z / 64));

    private void add_ridge_cell(TreeSpecies.Kind kind, TreeGenerator.Result result, string material_key,
        Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs)
    {
        ridge_groups.Add((kind, result, material_key, transforms, ridge_multimeshes.Count));
        TreeSpecies species = TreeSpecies.by_kind(kind);
        _add_far_multimesh(result.bark, far_bark_materials[species.bark_set].As<Material>(), transforms, customs, true, true);
        if (result.leaves != null)
        {
            if (!ridge_leaf_variant_materials.ContainsKey(material_key))
            {
                ShaderMaterial variant_mat = (ShaderMaterial)far_leaf_materials[species.leaf_atlas].As<ShaderMaterial>().Duplicate();
                variant_mat.SetShaderParameter("crown_offset", new Vector2(result.crown_center.X, result.crown_center.Z));
                variant_mat.SetShaderParameter("lod_start", RIDGE_LEAF_LOD[0]);
                variant_mat.SetShaderParameter("lod_end", RIDGE_LEAF_LOD[1]);
                variant_mat.SetShaderParameter("lod_keep", RIDGE_LEAF_LOD[2]);
                variant_mat.SetShaderParameter("lod_grow", RIDGE_LEAF_LOD[3]);
                ridge_leaf_variant_materials[material_key] = variant_mat;
            }
            _add_far_multimesh(result.leaves, ridge_leaf_variant_materials[material_key].As<Material>(), transforms, customs, false, true);
        }
    }

    public void attach_ridge_impostors(TreeImpostors impostors)
    {
        if (Game.Instance.has_flag("unbatched-canopy")) attach_group_impostors(impostors, ridge_groups, ridge_multimeshes, 90);
        else attach_batched_ridges(impostors);
    }

    private void attach_batched_ridges(TreeImpostors impostors)
    {
        int cells = 0, trees = 0;
        var keys = ridge_groups.Select(g => g.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (keys.Length == 0 || keys.Any(k => !impostors.baked.ContainsKey(k))) return;
        TreeImpostors.Family atlas = impostors.family(keys);
        foreach (var cell in ridge_groups.GroupBy(g => canopy_cell(g.Transforms[0].Origin)))
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true, Mesh = atlas.Quad, InstanceCount = cell.Sum(g => g.Transforms.Count) };
            Aabb bounds = default;
            int instance = 0;
            foreach (var group in cell)
            {
                TreeImpostors.Baked b = impostors.baked[group.Key];
                Aabb local = new(new Vector3(-b.extent.X * 0.5f, (float)b.base_y, -b.extent.X * 0.5f),
                    new Vector3(b.extent.X, b.extent.Y, b.extent.X));
                local = local.Merge(group.Mesh.bark.GetAabb());
                if (group.Mesh.leaves != null) local = local.Merge(leaf_bounds(group.Mesh));
                for (int i = 0; i < group.Transforms.Count; i++)
                {
                    Transform3D transform = group.Transforms[i];
                    Aabb at = transform * local;
                    bounds = instance == 0 ? at : bounds.Merge(at);
                    mm.SetInstanceTransform(instance, transform);
                    mm.SetInstanceCustomData(instance, new Color(MathF.Atan2(transform.Basis.Z.X, transform.Basis.Z.Z),
                        ridge_multimeshes[group.First].Multimesh.GetInstanceCustomData(i).A, atlas.Layers[group.Key], 0));
                    instance++;
                }
            }
            mm.CustomAabb = bounds.Grow(1.5f);
            float switchAt = 90 + new Vector2(bounds.Size.X, bounds.Size.Z).Length() * 0.5f;
            AddChild(new MultiMeshInstance3D { Name = "RidgeCanopy", Multimesh = mm, MaterialOverride = atlas.Color,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                VisibilityRangeBegin = switchAt, VisibilityRangeBeginMargin = 8,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
            AddChild(new MultiMeshInstance3D { Name = "RidgeCanopyShadow", Multimesh = mm, MaterialOverride = atlas.Shadow,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                VisibilityRangeBegin = switchAt });
            // Every variant in this cell uses the same bounds and native
            // range reference, so no hole or duplicate band opens at the swap.
            foreach (var group in cell)
            for (int i = group.First; i < group.First + (group.Mesh.leaves == null ? 1 : 2); i++)
            {
                var original = ridge_multimeshes[i];
                original.CustomAabb = mm.CustomAabb;
                original.VisibilityRangeEnd = switchAt;
                original.VisibilityRangeEndMargin = 8;
                original.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            cells++;
            trees += instance;
        }
        GD.Print($"Batched canopy: {trees} trees in {cells} spatial families (previously {ridge_groups.Count} variant groups)");
        // Every cell now holds the independent array material. The temporary
        // individual atlases no longer need to occupy another copy of VRAM.
        impostors.baked.Clear();
    }

    public void attach_far_impostors(TreeImpostors impostors) => attach_group_impostors(impostors, far_groups, far_multimeshes, 70);

    private void attach_group_impostors(TreeImpostors impostors,
        List<(TreeSpecies.Kind Kind, TreeGenerator.Result Mesh, string Key, Godot.Collections.Array<Transform3D> Transforms, int First)> groups,
        Godot.Collections.Array<MultiMeshInstance3D> originals, float nearDistance)
    {
        foreach (var group in groups)
        {
            if (!impostors.baked.ContainsKey(group.Key)) return;
        }
        var materials = new Dictionary<string, (ShaderMaterial Color, ShaderMaterial Shadow)>();
        foreach (var group in groups)
        {
            var baked = impostors.baked[group.Key];
            if (!materials.TryGetValue(group.Key, out var pair))
            {
                pair = ((ShaderMaterial)baked.material.Duplicate(), (ShaderMaterial)baked.shadow_material.Duplicate());
                pair.Color.Shader = Content.Load<Shader>("res://shaders/tree_impostor_multimesh.gdshader");
                pair.Shadow.Shader = pair.Color.Shader;
                pair.Color.SetShaderParameter("use_instance_custom", true);
                pair.Shadow.SetShaderParameter("use_instance_custom", true);
                materials.Add(group.Key, pair);
            }
            var mm = new MultiMesh {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true, Mesh = baked.quad, InstanceCount = group.Transforms.Count };
            // A camera-facing card can turn through every azimuth. Its cull box
            // must enclose that swept width, not just the rest-oriented quad.
            Aabb localBounds = new(new Vector3(-baked.extent.X * 0.5f, (float)baked.base_y, -baked.extent.X * 0.5f),
                new Vector3(baked.extent.X, baked.extent.Y, baked.extent.X));
            localBounds = localBounds.Merge(group.Mesh.bark.GetAabb());
            if (group.Mesh.leaves != null) localBounds = localBounds.Merge(leaf_bounds(group.Mesh));
            Aabb bounds = group.Transforms[0] * localBounds;
            for (int i = 0; i < group.Transforms.Count; i++)
            {
                Transform3D transform = group.Transforms[i];
                mm.SetInstanceTransform(i, transform);
                float yaw = MathF.Atan2(transform.Basis.Z.X, transform.Basis.Z.Z);
                mm.SetInstanceCustomData(i, new Color(yaw, (float)hash_unit(i * 7919 + group.Key.Hash()), 0, 0));
                bounds = bounds.Merge(transform * localBounds);
            }
            mm.CustomAabb = bounds.Grow(1.5f);
            // Native range culling omits the full mesh draws entirely. Matching
            // bounds give both representations the same distance reference;
            // the nearest tree remains three-dimensional inside 90 metres.
            float switchAt = nearDistance + new Vector2(bounds.Size.X, bounds.Size.Z).Length() * 0.5f;
            AddChild(new MultiMeshInstance3D {
                Name = "RidgeCanopy", Multimesh = mm, MaterialOverride = pair.Color,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeBegin = switchAt, VisibilityRangeBeginMargin = 8,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled });
            AddChild(new MultiMeshInstance3D {
                Name = "RidgeCanopyShadow", Multimesh = mm, MaterialOverride = pair.Shadow,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
                VisibilityRangeBegin = switchAt, VisibilityRangeBeginMargin = 0,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled });
            for (int i = group.First; i < group.First + (group.Mesh.leaves == null ? 1 : 2); i++)
            {
                var original = originals[i];
                original.CustomAabb = mm.CustomAabb;
                original.VisibilityRangeEnd = switchAt;
                original.VisibilityRangeEndMargin = 8;
                original.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
        }
    }

    // TreeGenerator already emits native bark LODs. Share those meshes in
    // every distance band instead of rebuilding and caching a second copy.
    public void _add_far_multimesh(ArrayMesh mesh, Material material, Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs, bool is_bark, bool ridge = false)
    {
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
            mm.SetInstanceCustomData((int)i, customs[(int)i]);
        }
        // GPU sway and the retained LOD sprays extend beyond the rest mesh.
        if (!is_bark)
        {
            Aabb bounds = transforms[0] * mesh.GetAabb().Grow(1.5f);
            for (long i2 = 1, i_end2 = (long)transforms.Count; i2 < i_end2; i2++)
            {
                bounds = bounds.Merge(transforms[(int)i2] * mesh.GetAabb().Grow(1.5f));
            }
            mm.CustomAabb = bounds;
        }
        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = G.format("%s_%s", new Godot.Collections.Array { ridge ? "Ridge" : "FarTrees", is_bark ? "bark" : "leaves" });
        mmi.Multimesh = mm;
        mmi.MaterialOverride = material;
        mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        // Static for the ridges too: the outer SDFGI cascades reach the nearer
        // band, and occluding the sky between crowns is what keeps a canopy dark.
        mmi.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(mmi);
        if (ridge)
        {
            ridge_multimeshes.Add(mmi);
        }
        else
        {
            far_multimeshes.Add(mmi);
        }
    }

    public ShaderMaterial _make_bark_material(string bark_set, bool far)
    {
        // ---------------------------------------------------------------- materials
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>(far ? "res://shaders/bark_multimesh.gdshader" : "res://shaders/bark.gdshader");
        Camp.bind_texture(mat, "albedo_tex", G.format("res://textures/%s_albedo.png", bark_set));
        Camp.bind_texture(mat, "normal_tex", G.format("res://textures/%s_normal.png", bark_set));
        Camp.bind_texture(mat, "orm_tex", G.format("res://textures/%s_orm.png", bark_set));
        Camp.bind_texture(mat, "noise_tex", "res://textures/noise_rgba.png");
        mat.SetShaderParameter("use_instance_custom", far);
        mat.SetShaderParameter("moss_amount", bark_set == "bark_oak" ? 0.5 : 0.25);
        return mat;
    }

    public ShaderMaterial _make_leaf_material(TreeSpecies species, bool far)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>(far ? "res://shaders/foliage_multimesh.gdshader" : "res://shaders/foliage.gdshader");
        Camp.bind_texture(mat, "albedo_tex", G.format("res://textures/%s.png", species.leaf_atlas));
        Camp.bind_texture(mat, "normal_trans_tex", G.format("res://textures/%s_nt.png", species.leaf_atlas));
        mat.SetShaderParameter("use_instance_custom", far);
        bool conifer = species.kind == TreeSpecies.Kind.SPRUCE || species.kind == TreeSpecies.Kind.PINE;
        mat.SetShaderParameter("translucency", conifer ? 0.24 : 0.36);
        // Rounder crown shading: a stronger share of the crown-centred normal
        // reads the canopy as a lit volume at mid distance instead of flat sprays.
        mat.SetShaderParameter("spherical_normal_mix", conifer ? 0.62 : 0.66);
        mat.SetShaderParameter("flutter", conifer ? 0.06 : 0.14);
        mat.SetShaderParameter("roughness", conifer ? 0.62 : 0.5);
        return mat;
    }

    public Vector3 _tint_for(ScenePlan.TreeEntry entry)
    {
        double v = hash_unit(entry.seed_value + 7);
        return new Vector3(0.92f, 0.92f, 0.92f).Lerp(new Vector3(1.06f, 1.02f, 0.98f), (float)v);
    }

    public static double hash_unit(long value)
    {
        long h = absi(value * 2654435761L) % 100000;
        return (double)h / 100000.0;
    }

    /// Leaf-card LOD: [start, end, share collapsed at full LOD, survivor growth].
    /// Overlapping alpha-tested cards are the largest cost in the frame (see the
    /// profiler's overdraw view), but collapsing cards while growing the survivors
    /// measured neutral, so these stay close to the original look; the far forest
    /// thins slightly more because its cards only ever fill silhouettes.
    public static readonly Godot.Collections.Array NEAR_LEAF_LOD = new Godot.Collections.Array { 40.0, 115.0, 0.52, 0.16 };
    public static readonly Godot.Collections.Array FAR_LEAF_LOD = new Godot.Collections.Array { 40.0, 110.0, 0.62, 0.30 };
    /// The ridge variants are already thinned at build time; the shader only
    /// collapses a little more far out and barely grows the survivors, or the
    /// crowns turn into bright shells of oversized cards.
    public static readonly Godot.Collections.Array RIDGE_LEAF_LOD = new Godot.Collections.Array { 180.0, 420.0, 0.30, 0.20 };

    public void attach_impostors(TreeImpostors impostors)
    {
        /// Beyond the switch distance each hero tree hands over to a baked billboard
        /// and a sun-facing shadow card; within it the hero meshes render as before.
        if ((impostors.baked.Count == 0))
        {
            return;
        }
        foreach (Godot.Collections.Dictionary record in near_records)
        {
            ScenePlan.TreeEntry entry = record["entry"].As<ScenePlan.TreeEntry>();
            var list = variants[(long)entry.kind];
            long index = absi(entry.seed_value) % (long)list.Count;
            if (!impostors.baked.TryGetValue(TreeImpostors.key_for(entry.kind, index), out TreeImpostors.Baked b))
            {
                continue;
            }
            Transform3D xform = record["xform"].AsTransform3D();
            Transform3D placement = new Transform3D(Basis.Identity.Scaled(Vector3.One * (float)entry.scale), xform.Origin);
            MeshInstance3D card = new MeshInstance3D();
            card.Name = G.format("%s_impostor", TreeSpecies.by_kind(entry.kind).name);
            card.Mesh = b.quad;
            card.MaterialOverride = b.material;
            card.Transform = placement;
            card.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            card.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            card.SetInstanceShaderParameter("yaw", entry.rotation);
            AddChild(card);
            MeshInstance3D shadow = new MeshInstance3D();
            shadow.Name = G.format("%s_impostor_shadow", TreeSpecies.by_kind(entry.kind).name);
            shadow.Mesh = b.quad;
            shadow.MaterialOverride = b.shadow_material;
            shadow.Transform = placement;
            shadow.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            shadow.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            shadow.SetInstanceShaderParameter("yaw", entry.rotation);
            AddChild(shadow);
            impostor_nodes.Add(new Godot.Collections.Dictionary { { (StringName)"bark", record["bark"] }, { (StringName)"leaves", record["leaves"] }, { (StringName)"card", card }, { (StringName)"shadow", shadow } });
        }
        _apply_switch_distance();
    }

    public double _switch_override = -1.0;

    public void set_switch_distance(double distance)
    {
        /// Profiler hook: force the hero/impostor switch distance (negative restores the preset's).
        _switch_override = distance;
        _apply_switch_distance();
    }

    public void _apply_switch_distance()
    {
        double d = _switch_override < 0.0 ? TreeImpostors.SWITCH_DISTANCE * _foliage_distance : _switch_override;
        foreach (Godot.Collections.Dictionary n in impostor_nodes)
        {
            foreach (Variant hero in new Godot.Collections.Array { n["bark"], n["leaves"] })
            {
                if (hero.VariantType == Variant.Type.Nil)
                {
                    continue;
                }
                MeshInstance3D mi = hero.As<MeshInstance3D>();
                mi.VisibilityRangeEnd = (float)d;
                mi.VisibilityRangeEndMargin = (float)TreeImpostors.SWITCH_MARGIN;
                mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            MeshInstance3D card = n["card"].As<MeshInstance3D>();
            card.VisibilityRangeBegin = (float)d;
            card.VisibilityRangeBeginMargin = (float)TreeImpostors.SWITCH_MARGIN;
            card.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            MeshInstance3D shadow = n["shadow"].As<MeshInstance3D>();
            shadow.VisibilityRangeBegin = (float)d;
            shadow.VisibilityRangeBeginMargin = 0.0f;
        }
    }

    public void apply_quality(QualityPreset p)
    {
        _foliage_distance = p.foliage_distance;
        set_leaf_lod(NEAR_LEAF_LOD, FAR_LEAF_LOD, p.foliage_distance);
        if (!(impostor_nodes.Count == 0))
        {
            _apply_switch_distance();
        }
    }

    public void set_leaf_lod(Godot.Collections.Array near_lod, Godot.Collections.Array far_lod, double distance_scale)
    {
        foreach (Variant key in leaf_materials.Keys)
        {
            ShaderMaterial mat = leaf_materials[key].As<ShaderMaterial>();
            mat.SetShaderParameter("lod_start", G.op("*", near_lod[0], distance_scale));
            mat.SetShaderParameter("lod_end", G.op("*", near_lod[1], distance_scale));
            mat.SetShaderParameter("lod_keep", near_lod[2]);
            mat.SetShaderParameter("lod_grow", near_lod[3]);
        }
        foreach (Variant mat_dict in new Godot.Collections.Array { far_leaf_materials, far_leaf_variant_materials })
        {
            foreach (Variant key_item in G.Iter(mat_dict))
            {
                Variant key2 = key_item;
                ShaderMaterial mat2 = G.Index(mat_dict, key2).As<ShaderMaterial>();
                mat2.SetShaderParameter("lod_start", G.op("*", far_lod[0], distance_scale));
                mat2.SetShaderParameter("lod_end", G.op("*", far_lod[1], distance_scale));
                mat2.SetShaderParameter("lod_keep", far_lod[2]);
                mat2.SetShaderParameter("lod_grow", far_lod[3]);
            }
        }
    }
}
