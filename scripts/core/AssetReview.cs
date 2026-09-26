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

/// Visual QA of the actual generated meshes and materials. Four studio angles
/// reveal construction; two contextual angles reveal scale and placement.
/// Output is deliberately local and reviewable, never part of the game HUD.
public partial class AssetReview : Node3D
{
    public Camera3D camera;
    public Node3D studio;
    public Node3D sources;
    public Environment studio_environment;
    public Godot.Collections.Array<Godot.Collections.Dictionary> entries = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    public string output = "";
    public Godot.Collections.Array<Godot.Collections.Dictionary> _metadata = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    public static readonly Godot.Collections.Array VIEWS = new Godot.Collections.Array { new Godot.Collections.Array { 35.0, 18.0 }, new Godot.Collections.Array { 125.0, 24.0 }, new Godot.Collections.Array { 215.0, 20.0 }, new Godot.Collections.Array { 305.0, 62.0 } };

    public async void run(string directory)
    {
        output = ProjectSettings.GlobalizePath(directory);
        DirAccess.MakeDirRecursiveAbsolute(output);
        Game.Instance.hud_visible = false;
        Game.Instance.mode = Game.Mode.PHOTO;
        camera = new Camera3D();
        camera.Near = 0.015f;
        camera.Far = 1800;
        camera.Fov = 44;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        AddChild(camera);
        camera.MakeCurrent();
        _build_studio();
        _inventory();
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        List<string> filter = G.split(Game.Instance.arg_value("assets", ""), ",", false);
        G.print(G.format("ASSET_REVIEW_START entries=%d", (long)entries.Count));
        foreach (Godot.Collections.Dictionary entry in entries)
        {
            if (!(filter.Count == 0) && !filter.Contains(entry["name"].AsString()))
            {
                continue;
            }
            await _review(entry);
        }
        FileAccess file = FileAccess.Open(output.PathJoin("inventory.json"), FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(_metadata, "  "));
        file.Close();
        G.print(G.format("ASSET_REVIEW_DONE assets=%d output=%s", new Godot.Collections.Array { (long)_metadata.Count, output }));
        GetTree().Quit();
    }

    public void _build_studio()
    {
        sources = new Node3D();
        sources.Name = "ReviewSources";
        sources.Visible = false;
        AddChild(sources);
        studio = new Node3D();
        studio.Name = "ReviewStudio";
        AddChild(studio);
        studio_environment = new Environment();
        studio_environment.BackgroundMode = Environment.BGMode.Color;
        studio_environment.BackgroundColor = new Color(0.15f, 0.17f, 0.18f);
        studio_environment.AmbientLightSource = Environment.AmbientSource.Color;
        studio_environment.AmbientLightColor = new Color(0.86f, 0.9f, 1.0f);
        studio_environment.AmbientLightEnergy = 0.65f;
        studio_environment.TonemapMode = Environment.ToneMapper.Agx;
        studio_environment.SsaoEnabled = true;
        studio_environment.SsaoRadius = 0.25f;
        studio_environment.SsaoIntensity = 1.0f;
        DirectionalLight3D key = new DirectionalLight3D();
        key.LightEnergy = 2.5f;
        key.RotationDegrees = new Vector3(-38, -35, 0);
        key.ShadowEnabled = true;
        key.DirectionalShadowMaxDistance = 160;
        studio.AddChild(key);
        DirectionalLight3D fill = new DirectionalLight3D();
        fill.LightColor = new Color(0.72f, 0.82f, 1.0f);
        fill.LightEnergy = 0.6f;
        fill.RotationDegrees = new Vector3(-22, 135, 0);
        studio.AddChild(fill);
        MeshInstance3D plane = new MeshInstance3D();
        PlaneMesh mesh = new PlaneMesh();
        mesh.Size = new Vector2(400, 400);
        plane.Mesh = mesh;
        Vector3 _t1 = plane.Position;
        _t1.Y = -0.015f;
        plane.Position = _t1;
        plane.MaterialOverride = FieldKit.solid(new Color(0.29f, 0.31f, 0.32f), 0.88);
        studio.AddChild(plane);
    }

    public void _add(string name_value, Node3D source)
    {
        if (source == null)
        {
            G.push_error("Missing review model: " + name_value);
            return;
        }
        Aabb local = _bounds(source, source.Transform.AffineInverse());
        Transform3D placement = source is SoftBody3D ? source.GetParent().Get("global_transform").AsTransform3D() : source.GlobalTransform;
        entries.Add(new Godot.Collections.Dictionary { { (StringName)"name", name_value }, { (StringName)"source", source }, { (StringName)"local_bounds", local }, { (StringName)"context_bounds", placement * local } });
    }

    public void _inventory()
    {
        Camp camp = Game.Instance.camp;
        // Passing birds scale away between flights. Inspect a full-size snapshot,
        // rather than framing the almost invisible idle pose.
        Node3D bird = _copy_visual(camp.wildlife.GetNode<Node3D>("PondBird0"));
        bird.Scale = Vector3.One;
        sources.AddChild(bird);
        _add("pond_bird", bird);
        if (camp.wildlife.HasNode("PondFish0"))
        {
            _add("pond_fish", camp.wildlife.GetNode<Node3D>("PondFish0"));
        }
        // Every generated tree variant, located at one of its real planted instances.
        foreach (long kind in G.enum_values<TreeSpecies.Kind>())
        {
            TreeSpecies species = TreeSpecies.by_kind((TreeSpecies.Kind)kind);
            for (long variant = 0, variant_end = Forest.VARIANTS_PER_SPECIES; variant < variant_end; variant++)
            {
                TreeGenerator.Result result = G.Index(camp.forest.variants[kind], variant).As<TreeGenerator.Result>();
                Node3D root = new Node3D();
                sources.AddChild(root);
                foreach (ScenePlan.TreeEntry t in camp.plan.trees)
                {
                    if ((long)t.kind == kind && absi(t.seed_value) % Forest.VARIANTS_PER_SPECIES == variant)
                    {
                        root.Transform = camp.forest._tree_transform(t);
                        break;
                    }
                }
                MeshInstance3D bark = FieldKit.add(root, result.bark, camp.forest.bark_materials[species.bark_set].As<Material>());
                bark.SetInstanceShaderParameter("tree_height", result.height);
                if (result.leaves != null)
                {
                    MeshInstance3D leaf = FieldKit.add(root, result.leaves, camp.forest.leaf_materials[species.leaf_atlas].As<Material>());
                    leaf.SetInstanceShaderParameter("tree_height", result.height);
                    leaf.SetInstanceShaderParameter("crown", new Color(result.crown_center.X, result.crown_center.Y, result.crown_center.Z, (float)result.crown_radius));
                    leaf.SetInstanceShaderParameter("tint", species.leaf_tint);
                }
                _add(G.format("tree_%s_%d", new Godot.Collections.Array { species.name, variant + 1 }), root);
            }
        }
        // Actual vegetation meshes, sampled from their planted MultiMeshes.
        foreach (Variant name_value in new Godot.Collections.Array { "Ferns", "Shrubs", "Meadow_0", "Meadow_1", "Meadow_2", "Meadow_3", "MeadowSeedHeads", "Reeds" })
        {
            _single(G.Call(name_value, "to_lower").AsString(), _understory_batch(camp.understory, name_value.AsString()), new Vector2(6, 8));
        }
        for (long species2 = 0; species2 < 4; species2++)
        {
            for (long variant2 = 1; variant2 < 3; variant2++)
            {
                string node_name = G.format("Meadow_%d_variant_%d", new Godot.Collections.Array { species2, variant2 });
                _single(node_name.ToLowerInvariant(), camp.understory.GetNode<MultiMeshInstance3D>(node_name), new Vector2(6, 8));
            }
        }
        MultiMeshInstance3D grass_patch = camp.understory.GetNode<MultiMeshInstance3D>("Grass_0_8");
        _single("grass_clump", grass_patch, new Vector2(6, 8));
        // A local patch exposes spacing and repeated silhouettes as well as blades.
        Node3D patch = _copy_visual(grass_patch);
        sources.AddChild(patch);
        _add("grass_patch", patch);
        foreach (Variant name_value2 in new Godot.Collections.Array { "LilyPads", "WaterLilies", "LilyStems", "WaterwornStones" })
        {
            _single(G.Call(name_value2, "to_lower").AsString(), camp.shore_life.GetNode<MultiMeshInstance3D>(name_value2.AsNodePath()), new Vector2(-28, 15));
        }
        Godot.Collections.Dictionary reviewed_rocks = new Godot.Collections.Dictionary();
        foreach (Node child in camp.rocks.GetChildren())
        {
            if (child is MeshInstance3D)
            {
                long variant3 = (long)camp.rocks.variants.IndexOf((ArrayMesh)((MeshInstance3D)child).Mesh);
                if (!reviewed_rocks.ContainsKey(variant3))
                {
                    _add(G.format("boulder_%d", variant3 + 1), ((MeshInstance3D)child));
                    reviewed_rocks[variant3] = true;
                }
            }
        }
        // The wooded ridges plant the forest's own variants, reviewed above.
        Campsite site = camp.campsite;
        _add("tent", site.tent);
        _add("backpack", site.tent.GetNode<Node3D>("WaxedCanvasPack"));
        _add("groundsheet", site.tent.GetNode<Node3D>("GroundSheet"));
        _add("bedroll", site.tent.GetNode<Node3D>("Bedroll"));
        _add("hanging_lantern", site.tent.lantern);
        _add("post_lantern", site.lanterns[2]);
        _add("dock_lantern", site.dock.lantern);
        _add("cooking_corner", site.kitchen);
        _add("tablecloth", site.kitchen.cloth);
        foreach (Variant name_value3 in new Godot.Collections.Array { "CoffeePot", "EnamelMug", "EnamelMug2", "FieldJournal", "SlattedSupplyCrate" })
        {
            _add(G.Call(name_value3, "to_snake_case").AsString(), site.kitchen.GetNode<Node3D>(name_value3.AsNodePath()));
        }
        _add("blanket", site.kitchen.GetNode<Node3D>("SlattedSupplyCrate/RolledWoolBlanket"));
        for (long i = 0; i < 3; i++)
        {
            _add(G.format("bench_%d", i + 1), site.GetNode<Node3D>(G.format("SplitLogBench_%d", i)));
        }
        Node pile = site.GetNode("Woodpile");
        _add("woodpile", (Node3D)pile);
        _add("chopping_block", pile.GetNode<Node3D>("ChoppingBlock"));
        _add("axe", pile.GetNode<Node3D>("Axe"));
        _add("firepit", site.firepit);
        _add("hanging_pot", site.firepit.GetNode<Node3D>("Pot"));
        _add("tripod", site.firepit.GetNode<Node3D>("Tripod"));
        _add("coals", site.firepit.GetNode<Node3D>("Coals"));
        _add("dock", site.dock);
        _add("canoe", site.dock.canoe);
        _add("deck", site.dock.GetNode<Node3D>("Deck"));
        _add("mooring", site.dock.GetNode<Node3D>("Mooring"));
        _add("skipping_stones", site.dock.GetNode<Node3D>("SkippingStones"));
    }

    public void _single(string name_value, MultiMeshInstance3D source, Vector2 near)
    {
        MultiMesh mm = source.Multimesh;
        if (mm.InstanceCount == 0)
        {
            G.push_error("Empty vegetation stand: " + name_value);
            return;
        }
        double best = INF;
        long selected = 0;
        for (long i = 0, i_end = mm.InstanceCount; i < i_end; i++)
        {
            Vector3 at = mm.GetInstanceTransform((int)i).Origin;
            double distance = new Vector2(at.X, at.Z).DistanceSquaredTo(near);
            if (distance < best)
            {
                best = distance;
                selected = i;
            }
        }
        MultiMesh one = new MultiMesh();
        one.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        one.Mesh = mm.Mesh;
        one.UseColors = mm.UseColors;
        one.UseCustomData = mm.UseCustomData;
        one.InstanceCount = 1;
        one.SetInstanceTransform(0, Transform3D.Identity);
        if (mm.UseColors)
        {
            one.SetInstanceColor(0, mm.GetInstanceColor((int)selected));
        }
        if (mm.UseCustomData)
        {
            one.SetInstanceCustomData(0, mm.GetInstanceCustomData((int)selected));
        }
        MultiMeshInstance3D root = new MultiMeshInstance3D();
        root.Multimesh = one;
        root.MaterialOverride = source.MaterialOverride;
        sources.AddChild(root);
        root.GlobalTransform = source.GlobalTransform * mm.GetInstanceTransform((int)selected);
        _add(name_value, root);
    }

    public Node3D _copy_visual(Node3D source)
    {
        Node3D root = null;
        if (source is SoftBody3D)
        {
            MeshInstance3D mi = new MeshInstance3D();
            mi.Mesh = _soft_mesh(((SoftBody3D)source));
            mi.MaterialOverride = ((SoftBody3D)source).MaterialOverride;
            root = mi;
        }
        else if (source is MeshInstance3D)
        {
            MeshInstance3D mi2 = new MeshInstance3D();
            mi2.Mesh = ((MeshInstance3D)source).Mesh;
            mi2.MaterialOverride = ((MeshInstance3D)source).MaterialOverride;
            for (long i = 0, i_end = ((MeshInstance3D)source).GetSurfaceOverrideMaterialCount(); i < i_end; i++)
            {
                mi2.SetSurfaceOverrideMaterial((int)i, ((MeshInstance3D)source).GetSurfaceOverrideMaterial((int)i));
            }
            if (((MeshInstance3D)source).MaterialOverride is ShaderMaterial)
            {
                string code = ((ShaderMaterial)((MeshInstance3D)source).MaterialOverride).Shader.Code;
                foreach (Variant parameter in new Godot.Collections.Array { "tree_height", "crown", "tint" })
                {
                    if (code.Contains("instance uniform") && ((MeshInstance3D)source).GetInstanceShaderParameter(parameter.AsStringName()).VariantType != Variant.Type.Nil)
                    {
                        mi2.SetInstanceShaderParameter(parameter.AsStringName(), ((MeshInstance3D)source).GetInstanceShaderParameter(parameter.AsStringName()));
                    }
                }
            }
            root = mi2;
        }
        else if (source is MultiMeshInstance3D)
        {
            MultiMeshInstance3D mi3 = new MultiMeshInstance3D();
            mi3.Multimesh = ((MultiMeshInstance3D)source).Multimesh;
            mi3.MaterialOverride = ((MultiMeshInstance3D)source).MaterialOverride;
            root = mi3;
        }
        else if (source is Label3D)
        {
            root = (Node3D)((Label3D)source).Duplicate();
        }
        else
        {
            root = new Node3D();
        }
        root.Transform = source.Transform;
        foreach (Node child in source.GetChildren())
        {
            if (child is Node3D && !(((Node3D)child) is Light3D) && !(((Node3D)child) is GpuParticles3D) && !(((Node3D)child) is CollisionShape3D))
            {
                root.AddChild(_copy_visual(((Node3D)child)));
            }
        }
        return root;
    }

    public ArrayMesh _soft_mesh(SoftBody3D source)
    {
        /// SoftBody3D becomes top-level and exposes simulated points in world space.
        /// Snapshot those points back into the owning prop's frame for studio review.
        Godot.Collections.Array arrays = source.Mesh.SurfaceGetArrays(0);
        MeshBuilder mb = new MeshBuilder();
        Transform3D owner_inverse = G.Call(source.GetParent().Get("global_transform"), "affine_inverse").AsTransform3D();
        G.resize(mb.vertices, G.Call(arrays[(int)Mesh.ArrayType.Vertex], "size").AsInt32());
        for (long i = 0, i_end = (long)mb.vertices.Count; i < i_end; i++)
        {
            mb.vertices[(int)i] = owner_inverse * source.GetPointTransform((int)i);
        }
        mb.uvs = G.ListFromVariant<Vector2>(arrays[(int)Mesh.ArrayType.TexUV]);
        mb.normals = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Normal]);
        mb.colors = G.ListFromVariant<Color>(arrays[(int)Mesh.ArrayType.Color]);
        mb.indices = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
        mb.recompute_normals();
        mb.recompute_tangents();
        return mb.commit();
    }

    public Aabb _bounds(Node3D root, Transform3D? parent_transform_opt = null)
    {
        Transform3D parent_transform = parent_transform_opt ?? Transform3D.Identity;
        Transform3D transform_here = parent_transform * root.Transform;
        Aabb bounds = new Aabb();
        if (root is SoftBody3D && ((SoftBody3D)root).Mesh != null)
        {
            bounds = transform_here * _soft_mesh(((SoftBody3D)root)).GetAabb();
        }
        else if (root is MeshInstance3D && ((MeshInstance3D)root).Mesh != null)
        {
            bounds = transform_here * ((MeshInstance3D)root).Mesh.GetAabb();
        }
        else if (root is MultiMeshInstance3D && ((MultiMeshInstance3D)root).Multimesh != null)
        {
            bounds = transform_here * ((MultiMeshInstance3D)root).Multimesh.GetAabb();
        }
        foreach (Node child in root.GetChildren())
        {
            if (child is Node3D)
            {
                Aabb child_bounds = _bounds(((Node3D)child), transform_here);
                if (child_bounds.Size.LengthSquared() > 0)
                {
                    bounds = bounds.Size.LengthSquared() == 0 ? child_bounds : bounds.Merge(child_bounds);
                }
            }
        }
        return bounds;
    }

    public void _frame(Aabb bounds, double azimuth, double elevation, bool contextual)
    {
        Vector3 centre = bounds.GetCenter();
        double size = maxf(bounds.Size.X, maxf(bounds.Size.Y, bounds.Size.Z));
        double distance = maxf(size * 1.85, contextual ? 1.4 : 0.25);
        double az = deg_to_rad(azimuth);
        double el = deg_to_rad(elevation);
        Vector3 at = centre + new Vector3((float)(cos(az) * cos(el)), (float)sin(el), (float)(sin(az) * cos(el))) * (float)distance;
        if (contextual)
        {
            at.Y = (float)maxf(at.Y, Game.Instance.camp.field.height(at.X, at.Z) + 0.4);
        }
        camera.GlobalPosition = at;
        camera.LookAt(centre, Vector3.Up);
    }

    public async Task _review(Godot.Collections.Dictionary entry)
    {
        Godot.Collections.Array<string> paths = new Godot.Collections.Array<string>();
        Node3D source = entry["source"].As<Node3D>();
        Node3D isolated = _copy_visual(source);
        isolated.Transform = new Transform3D(Basis.FromScale(source.GlobalTransform.Basis.Scale), Vector3.Zero);
        studio.AddChild(isolated);
        Aabb bounds = _bounds(isolated);
        isolated.Position = -new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        bounds = _bounds(isolated);
        Game.Instance.camp.Visible = false;
        // Native soft bodies are top-level, so their visibility does not inherit
        // the hidden camp. Exclude the live body while its snapshot is on stage.
        SoftBody3D live_cloth = Game.Instance.camp.campsite.kitchen.cloth;
        long cloth_layers = (long)live_cloth.Layers;
        live_cloth.Layers = unchecked((uint)(0));
        Game.Instance.camp.ProcessMode = Node.ProcessModeEnum.Disabled;
        Game.Instance.world.Visible = false;
        Game.Instance.world.SetProcess(false);
        Game.Instance.world.post.set_enabled(false);
        studio.Visible = true;
        camera.Environment = studio_environment;
        RenderingServer.GlobalShaderParameterSet("wind_strength", 0.0);
        // Grass in the studio must not dissolve or flatten around the old player.
        RenderingServer.GlobalShaderParameterSet("player_position", new Vector3(9000, 9000, 9000));
        for (long i = 0, i_end = (long)VIEWS.Count; i < i_end; i++)
        {
            _frame(bounds, G.Index(VIEWS[(int)i], 0).AsDouble(), G.Index(VIEWS[(int)i], 1).AsDouble(), false);
            paths.Add(await _capture(entry["name"].AsString(), G.format("isolated_%d", i + 1)));
        }
        isolated.Free();
        Game.Instance.camp.Visible = true;
        live_cloth.Layers = unchecked((uint)(cloth_layers));
        Game.Instance.camp.ProcessMode = Node.ProcessModeEnum.Inherit;
        Game.Instance.world.Visible = true;
        Game.Instance.world.SetProcess(true);
        Game.Instance.world.post.set_enabled(true);
        Game.Instance.world.hour = 19.05;
        studio.Visible = false;
        camera.Environment = null;
        for (long i2 = 0; i2 < 2; i2++)
        {
            _frame(entry["context_bounds"].AsAabb(), 60.0 + (double)i2 * 155.0, 22.0, true);
            if (new Godot.Collections.Array { "backpack", "groundsheet", "bedroll", "hanging_lantern" }.Contains(entry["name"]))
            {
                // Look through the doorway; exterior orbit cameras see the canvas.
                Vector3 eye = i2 == 0 ? new Vector3(0.12f, 0.78f, -2.15f) : new Vector3(-0.24f, 0.68f, -1.28f);
                camera.GlobalPosition = Game.Instance.camp.campsite.tent.ToGlobal(eye);
                camera.LookAt(G.Call(entry["context_bounds"], "get_center").AsVector3(), Vector3.Up);
            }
            paths.Add(await _capture(entry["name"].AsString(), G.format("placed_%d", i2 + 1)));
        }
        _metadata.Add(new Godot.Collections.Dictionary { { (StringName)"name", entry["name"] }, { (StringName)"source", G.str((NodePath)source.GetPath()) }, { (StringName)"images", paths } });
        G.print(G.format("ASSET_REVIEW %s views=%d", new Godot.Collections.Array { entry["name"], (long)paths.Count }));
    }

    public async Task<string> _capture(string asset, string view_name)
    {
        double elapsed = 0.0;
        long frames = 0;
        while (elapsed < 0.65 || frames < 12)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
            frames += 1;
        }
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        string path = output.PathJoin(asset + "__" + view_name + ".png");
        Error error = GetViewport().GetTexture().GetImage().SavePng(path);
        if (error != Error.Ok)
        {
            G.push_error("Could not save asset review: " + path);
            GetTree().Quit(1);
        }
        return path;
    }

    public static MultiMeshInstance3D _understory_batch(Node understory, string name_value)
    {
        /// Vegetation batches are split per cell (for example `Ferns_3_-2`); review the
        /// first cell when no batch carries the plain name.
        Node direct = understory.GetNodeOrNull(name_value);
        if (direct != null)
        {
            return (MultiMeshInstance3D)direct;
        }
        foreach (Node child in understory.GetChildren())
        {
            if (child is MultiMeshInstance3D && ((string)((MultiMeshInstance3D)child).Name).StartsWith(name_value + "_", StringComparison.Ordinal))
            {
                return ((MultiMeshInstance3D)child);
            }
        }
        G.push_error(G.format("No understory batch named %s", name_value));
        return null;
    }
}
