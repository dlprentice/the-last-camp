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

/// Photoscanned CC0 models (see models/SOURCES.md) placed as deterministic
/// MultiMesh dressing beside the generated geometry: stumps, fallen trunks,
/// surface roots at trunk bases, mossy rocks and boulders, small stones, ferns,
/// shrubs, nettles and weeds, grass tufts, dry branches and conifer saplings.
/// Placement follows the same route, camp and water rules as the generated
/// dressing, keeps large pieces clear of every film and intro camera path, and
/// batches per 32 m cell with distance culling. Small plants cast sun shadows
/// only from cells near the camera (the same instances exist as a casting and a
/// non-casting cell that hand over at SHADOW_RANGE).
public partial class ScannedDressing : Node3D
{
    public const double CELL = 32.0;
    public const long SEED = 51037;
    public const double SHADOW_RANGE = 45.0;

    /// kind: rock, wood, plant, sapling. fade: metres at foliage_distance 1.
    /// lod0_only keeps the scan's LOD0 variants when a file carries several LODs.
    public static readonly Godot.Collections.Dictionary LIBRARY = new Godot.Collections.Dictionary { { "tree_stump_01", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 170.0 }, { (StringName)"collide", "cylinder" } } }, { "tree_stump_02", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 170.0 }, { (StringName)"collide", "cylinder" } } }, { "dead_tree_trunk", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 190.0 }, { (StringName)"collide", "capsule" } } }, { "dead_tree_trunk_02", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 190.0 }, { (StringName)"collide", "capsule" } } }, { "dry_branches_medium_01", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 80.0 } } }, { "root_cluster_01", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 120.0 }, { (StringName)"tint", new Color(0.64f, 0.58f, 0.50f) } } }, { "root_cluster_02", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 110.0 }, { (StringName)"tint", new Color(0.64f, 0.58f, 0.50f) } } }, { "single_root", new Godot.Collections.Dictionary { { (StringName)"kind", "wood" }, { (StringName)"fade", 110.0 }, { (StringName)"tint", new Color(0.66f, 0.60f, 0.52f) } } }, { "rock_moss_set_01", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 200.0 }, { (StringName)"collide", "sphere" } } }, { "rock_moss_set_02", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 200.0 }, { (StringName)"collide", "sphere" } } }, { "boulder_01", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 240.0 }, { (StringName)"collide", "sphere" } } }, { "rock_07", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 70.0 } } }, { "rock_09", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 60.0 } } }, { "stone_01", new Godot.Collections.Dictionary { { (StringName)"kind", "rock" }, { (StringName)"fade", 70.0 } } }, { "fern_02", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 95.0 }, { (StringName)"tint", new Color(0.66f, 0.72f, 0.58f) } } }, { "shrub_01", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 110.0 }, { (StringName)"tint", new Color(0.56f, 0.62f, 0.50f) } } }, { "shrub_02", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 130.0 }, { (StringName)"tint", new Color(0.52f, 0.60f, 0.48f) } } }, { "shrub_03", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 100.0 }, { (StringName)"tint", new Color(0.56f, 0.62f, 0.50f) } } }, { "shrub_04", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 90.0 }, { (StringName)"tint", new Color(0.52f, 0.60f, 0.46f) } } }, { "nettle_plant", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 85.0 }, { (StringName)"tint", new Color(0.58f, 0.64f, 0.52f) } } }, { "weed_plant_02", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 75.0 }, { (StringName)"tint", new Color(0.60f, 0.66f, 0.54f) } } }, { "grass_medium_01", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 60.0 }, { (StringName)"tint", new Color(0.70f, 0.74f, 0.58f) } } }, { "grass_medium_02", new Godot.Collections.Dictionary { { (StringName)"kind", "plant" }, { (StringName)"fade", 60.0 }, { (StringName)"tint", new Color(0.70f, 0.74f, 0.58f) } } }, { "fir_sapling", new Godot.Collections.Dictionary { { (StringName)"kind", "sapling" }, { (StringName)"fade", 160.0 } } }, { "pine_sapling_small", new Godot.Collections.Dictionary { { (StringName)"kind", "sapling" }, { (StringName)"fade", 160.0 } } }, { "hatchet", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 60.0 } } }, { "wooden_bucket_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 90.0 }, { (StringName)"collide", "box" } } }, { "wooden_crate_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 110.0 }, { (StringName)"collide", "box" } } }, { "wicker_basket_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 70.0 } } }, { "pot_enamel_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 70.0 } } }, { "brass_pot_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 70.0 } } }, { "handsaw_wood", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 60.0 } } }, { "modified_thermos", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 60.0 } } }, { "wooden_lantern_01", new Godot.Collections.Dictionary { { (StringName)"kind", "prop" }, { (StringName)"fade", 90.0 } } } };

    // Bark-toned: the darker tints made the clusters read as dark mounds.
    // camp props: whole assemblies placed once at authored spots
    public partial class ScanVariant : RefCounted
    {
        public string model = "";
        public Mesh mesh;
        public Aabb bounds;
        public double footprint;
    }

    public Godot.Collections.Dictionary counts = new Godot.Collections.Dictionary();
    public Godot.Collections.Array<MultiMeshInstance3D> batches = new Godot.Collections.Array<MultiMeshInstance3D>();
    public Camp _camp;
    public TerrainField _field;
    public ScenePlan _plan;
    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public Godot.Collections.Dictionary _variants = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary _prop_scenes = new Godot.Collections.Dictionary();
    public Godot.Collections.Array<Node3D> props = new Godot.Collections.Array<Node3D>();
    public Godot.Collections.Dictionary _placements = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary _occupied = new Godot.Collections.Dictionary();
    public List<Vector3> _camera_samples = new();
    public Godot.Collections.Dictionary _camera_grid = new Godot.Collections.Dictionary();
    public Godot.Collections.Dictionary _trunk_grid = new Godot.Collections.Dictionary();
    public Godot.Collections.Array<Godot.Collections.Dictionary> _ranges = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    public bool _built = false;

    public void setup(Camp camp)
    {
        _camp = camp;
        setup_from(camp.field, camp.plan);
    }

    public void setup_from(TerrainField field, ScenePlan plan)
    {
        /// Build from a field and plan alone (tests, tools); setup(camp) wraps this.
        if (_built)
        {
            return;
        }
        _built = true;
        Name = "ScannedDressing";
        _field = field;
        _plan = plan;
        _rng.Seed = unchecked((ulong)(SEED));
        _camera_samples = camera_samples(_field);
        _index_obstacles();
        _load_library();
        _place_all();
        _upload();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, ScannedDressing.MethodName._quality));
        _quality(Quality.Instance.current);
        List<string> summary = new List<string>();
        foreach (Variant key_key in counts.Keys)
        {
            string key = key_key.AsString();
            summary.Add(G.format("%s=%d", new Godot.Collections.Array { key, counts[key] }));
        }
        G.print(G.format("Scanned dressing: %s in %d batches", new Godot.Collections.Array { string.Join(" ", summary), (long)batches.Count }));
    }

    public void _load_library()
    {
        // ------------------------------------------------------------------ library
        // --scanned-id-colours paints each model a flat colour so a frame can be
        // traced back to the asset it shows.
        bool id_colours = Game.Instance.has_flag("scanned-id-colours");
        Godot.Collections.Array palette = new Godot.Collections.Array { Colors.Red, Colors.Blue, Colors.Magenta, Colors.Cyan, Colors.Yellow, Colors.Green, Colors.Orange, Colors.Purple, Colors.White, Colors.Black };
        long index = 0;
        foreach (Variant model_key in LIBRARY.Keys)
        {
            string model = model_key.AsString();
            PackedScene scene = Content.Load<PackedScene>(G.format("res://models/%s/%s.gltf", new Godot.Collections.Array { model, model }));
            if (scene == null)
            {
                G.push_warning(G.format("Scanned model %s is missing; run tools/import_models.py", model));
                continue;
            }
            if (G.eq(G.Index(LIBRARY[model], "kind"), "prop"))
            {
                _prop_scenes[model] = scene;
                index += 1;
                continue;
            }
            Node root = scene.Instantiate();
            Godot.Collections.Array<ScannedDressing.ScanVariant> list = new Godot.Collections.Array<ScannedDressing.ScanVariant>();
            foreach (MeshInstance3D mi in _mesh_instances(root))
            {
                if (G.str((StringName)mi.Name).Contains("_LOD") && !((string)mi.Name).EndsWith("_LOD0", StringComparison.Ordinal))
                {
                    continue;
                }
                ScannedDressing.ScanVariant v = new ScannedDressing.ScanVariant();
                v.model = model;
                v.mesh = mi.Mesh;
                v.bounds = mi.Mesh.GetAabb();
                v.footprint = maxf(v.bounds.Size.X, v.bounds.Size.Z) * 0.5;
                Color tint = G.Call(LIBRARY[model], "get", "tint", Colors.White).AsColor();
                if (id_colours)
                {
                    tint = palette[(int)(index % (long)palette.Count)].AsColor();
                    G.print(G.format("SCANNED_ID %s = %s", new Godot.Collections.Array { model, tint.ToHtml(false) }));
                }
                _tune_materials(v.mesh, G.Index(LIBRARY[model], "kind").AsString(), tint, id_colours);
                list.Add(v);
            }
            index += 1;
            root.Free();
            G.sort_custom(list, (ScannedDressing.ScanVariant a, ScannedDressing.ScanVariant b) => a.footprint < b.footprint);
            _variants[model] = list;
        }
    }

    public static Godot.Collections.Array<MeshInstance3D> _mesh_instances(Node node)
    {
        Godot.Collections.Array<MeshInstance3D> @out = new Godot.Collections.Array<MeshInstance3D>();
        Godot.Collections.Array<Node> stack = new Godot.Collections.Array<Node> { node };
        while (!(stack.Count == 0))
        {
            Node n = G.pop_back(stack);
            if (n is MeshInstance3D && ((MeshInstance3D)n).Mesh != null)
            {
                @out.Add(((MeshInstance3D)n));
            }
            foreach (Node c in n.GetChildren())
            {
                stack.Add(c);
            }
        }
        G.sort_custom(@out, (MeshInstance3D a, MeshInstance3D b) => string.CompareOrdinal((string)a.Name, (string)b.Name) < 0);
        return @out;
    }

    public static void _tune_materials(Mesh mesh, string kind, Color tint, bool flat_colour = false)
    {
        /// The importer's StandardMaterial3D carries the scan's albedo, normal and ARM
        /// maps. Plants are cut out and lit from both sides; solids cull back faces.
        for (long s = 0, s_end = mesh.GetSurfaceCount(); s < s_end; s++)
        {
            BaseMaterial3D mat = mesh.SurfaceGetMaterial((int)s) as BaseMaterial3D;
            if (mat == null)
            {
                continue;
            }
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            // Scans are captured under flat light; a tint settles them into the
            // scene's darker, less saturated vegetation and soil.
            mat.AlbedoColor = tint;
            if (flat_colour)
            {
                mat.AlbedoTexture = null;
            }
            if (kind == "plant" || kind == "sapling" && mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                mat.AlphaScissorThreshold = 0.42f;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                // Matte, with a faint backlight only: no sheen (it turned sunlit cards
                // white), but enough transmission that a weed between the lens and a
                // low sun is a green leaf rather than a black cut-out.
                mat.BacklightEnabled = true;
                mat.Backlight = new Color(0.16f, 0.20f, 0.12f);
                mat.MetallicSpecular = 0.05f;
                mat.RoughnessTexture = null;
                mat.Roughness = 0.92f;
            }
            else
            {
                mat.CullMode = BaseMaterial3D.CullModeEnum.Back;
                mat.MetallicSpecular = 0.3f;
            }
        }
    }

    public void _place_all()
    {
        // ---------------------------------------------------------------- placement
        _place_stumps();
        _place_trunks();
        // Scanned root clusters include large soil balls that overwhelm the camp
        // composition. Leave those optional assets unplaced and use generated
        // buttress roots to blend standing trees into the ground.
        _place_rocks();
        _place_stones();
        _place_plants("fern_02", 700, 10.0, 72.0, 0.42, 0.85, 1.2, new Vector2(0.85f, 1.45f), 0.02);
        _place_plants("shrub_02", 70, 16.0, 86.0, 0.25, 3.2, 1.8, new Vector2(0.75f, 1.25f), 0.04);
        _place_plants("shrub_03", 60, 14.0, 80.0, 0.30, 2.0, 1.4, new Vector2(0.9f, 1.4f), 0.02);
        _place_plants("shrub_04", 40, 14.0, 80.0, 0.30, 1.6, 1.2, new Vector2(1.0f, 1.6f), 0.02);
        _place_plants("shrub_01", 30, 16.0, 80.0, 0.35, 2.4, 1.4, new Vector2(0.9f, 1.3f), 0.02);
        _place_plan_shrubs();
        _place_wet_plants("nettle_plant", 110, 3.0, 16.0, 1.1, new Vector2(0.9f, 1.5f));
        _place_wet_plants("weed_plant_02", 140, 2.0, 22.0, 0.8, new Vector2(1.2f, 2.2f));
        _place_tufts("grass_medium_01", 320, new Vector2(1.3f, 2.2f));
        _place_tufts("grass_medium_02", 160, new Vector2(1.1f, 1.9f));
        _place_plants("dry_branches_medium_01", 70, 14.0, 78.0, 0.35, 2.5, 1.5, new Vector2(0.9f, 1.4f), 0.05, true);
        _place_saplings();
        _place_camp_props();
    }

    public void _place_camp_props()
    {
        /// Hero props at authored spots: a crate with a thermos on it and a lantern
        /// and pot by the tent door, a bucket and basket at the table, an enamel pot
        /// by the seat, and a saw and hatchet at the woodpile. Multi-part scans stay
        /// whole; each prop is one scene instance, not a MultiMesh.
        Transform3D tent = Cinematic.tent_transform(_field);
        double tent_yaw = tent.Basis.GetEuler().Y;
        Vector2 table = new Vector2(TerrainField.TABLE.X, TerrainField.TABLE.Y);
        Vector2 wood = TerrainField.WOODPILE;
        Vector2 wood_side = new Vector2((float)cos(TerrainField.WOODPILE_YAW), (float)-sin(TerrainField.WOODPILE_YAW));
        Vector2 wood_front = new Vector2((float)sin(TerrainField.WOODPILE_YAW), (float)cos(TerrainField.WOODPILE_YAW));
        Vector3 crate_at = tent * new Vector3(-1.55f, 0.0f, -1.25f);
        _prop("wooden_crate_01", new Vector2(crate_at.X, crate_at.Z), tent_yaw + 0.35, 0.0);
        _prop("modified_thermos", new Vector2(crate_at.X, crate_at.Z) + new Vector2(0.12f, -0.05f), tent_yaw + 1.2, 0.36);
        Vector3 lantern_at = tent * new Vector3(1.05f, 0.0f, -1.65f);
        _prop("wooden_lantern_01", new Vector2(lantern_at.X, lantern_at.Z), tent_yaw - 0.4, 0.0);
        Vector3 brass_at = tent * new Vector3(1.3f, 0.0f, -1.1f);
        _prop("brass_pot_01", new Vector2(brass_at.X, brass_at.Z), tent_yaw + 0.8, 0.0);
        _prop("wooden_bucket_01", table + new Vector2(1.05f, 0.45f), 0.4, 0.0);
        _prop("wicker_basket_01", table + new Vector2(0.55f, -0.95f), 1.2, 0.0);
        // Outer side of the third seat, away from the intro dolly's approach.
        _prop("pot_enamel_01", new Vector2(1.8f, -3.45f), 0.9, 0.0);
        _prop("handsaw_wood", wood + wood_side * 1.15f + wood_front * 0.25f, TerrainField.WOODPILE_YAW + 0.2, 0.0, new Vector3(0.0f, 0.0f, (float)(PI * 0.5)));
        _prop("hatchet", wood - wood_side * 1.0f - wood_front * 0.7f, TerrainField.WOODPILE_YAW - 0.6, 0.0, new Vector3((float)(PI * 0.5), 0.0f, 0.0f));
    }

    public void _prop(string model, Vector2 p, double yaw, double lift, Vector3? lay_opt = null)
    {
        Vector3 lay = lay_opt ?? Vector3.Zero;
        PackedScene scene = G.get(_prop_scenes, model).As<PackedScene>();
        if (scene == null)
        {
            return;
        }
        if (!_camera_clear(p, 0.6))
        {
            G.push_warning(G.format("Camp prop %s at %s sits on a camera path", new Godot.Collections.Array { model, p }));
        }
        Node3D root = scene.Instantiate<Node3D>();
        Aabb bounds = new Aabb();
        bool first = true;
        foreach (MeshInstance3D mi in _mesh_instances(root))
        {
            _tune_materials(mi.Mesh, "prop", G.Call(LIBRARY[model], "get", "tint", Colors.White).AsColor());
            Aabb part = mi.Transform * mi.Mesh.GetAabb();
            bounds = first ? part : bounds.Merge(part);
            first = false;
        }
        double ground = _field.height(p.X, p.Y);
        Vector3 n = _field.normal(p.X, p.Y);
        Basis basis = new Basis(Vector3.Up, (float)yaw);
        if (lay != Vector3.Zero)
        {
            basis = basis * Basis.FromEuler(lay);
        }
        Vector3 up = Vector3.Up.Lerp(n, 0.5f).Normalized();
        if (up.DistanceTo(Vector3.Up) > 0.001)
        {
            basis = new Basis(new Quaternion(Vector3.Up, up)) * basis;
        }
        // Rest the assembly's lowest point on the ground (or the surface it sits on).
        Aabb rested = new Transform3D(basis, Vector3.Zero) * bounds;
        root.Transform = new Transform3D(basis, new Vector3(p.X, (float)(ground + lift - rested.Position.Y), p.Y));
        root.Name = "Prop_" + model;
        if (G.truthy(G.Call(LIBRARY[model], "has", "collide")))
        {
            StaticBody3D body = new StaticBody3D();
            body.Name = "PropCollision";
            body.CollisionLayer = unchecked((uint)(1));
            body.CollisionMask = unchecked((uint)(0));
            body.SetMeta("surface", (StringName)"wood");
            CollisionShape3D shape = new CollisionShape3D();
            BoxShape3D box = new BoxShape3D();
            box.Size = bounds.Size;
            shape.Shape = box;
            shape.Position = bounds.GetCenter();
            body.AddChild(shape);
            root.AddChild(body);
        }
        AddChild(root);
        props.Add(root);
        counts[model] = G.to_int(G.get(counts, model, 0)) + 1;
        _placements[G.format("%s|prop", model)] = new Godot.Collections.Dictionary { { (StringName)"variant", default(Variant) }, { (StringName)"transforms", new Godot.Collections.Array { root.Transform } } };
    }

    public void _place_stumps()
    {
        Godot.Collections.Array models = new Godot.Collections.Array { "tree_stump_01", "tree_stump_02" };
        long placed = 0;
        long attempts = 0;
        while (placed < 14 && attempts < 3000)
        {
            attempts += 1;
            Vector2 p = _woodland_point(16.0, 75.0);
            if (_field.canopy.sample(p.X, p.Y) < 0.35 || !_allowed(p, 2.6, 0.2))
            {
                continue;
            }
            if (_trunk_distance(p) < 4.0 || !_spaced("stump", p, 9.0) || !_camera_clear(p, 2.4))
            {
                continue;
            }
            string model = models[(int)(placed % (long)models.Count)].AsString();
            _add(model, p, _rng.RandfRange(0.9f, 1.25f), 0.06, 0.55, true);
            placed += 1;
        }
    }

    public void _place_trunks()
    {
        Godot.Collections.Array models = new Godot.Collections.Array { "dead_tree_trunk", "dead_tree_trunk_02" };
        long placed = 0;
        long attempts = 0;
        while (placed < 8 && attempts < 4000)
        {
            attempts += 1;
            Vector2 p = _woodland_point(18.0, 70.0);
            if (_field.canopy.sample(p.X, p.Y) < 0.3 || !_allowed(p, 3.2, 0.25))
            {
                continue;
            }
            if (_trunk_distance(p) < 3.0 || !_spaced("trunk", p, 12.0) || !_camera_clear(p, 3.6))
            {
                continue;
            }
            string model = models[(int)(placed % (long)models.Count)].AsString();
            // Lay the trunk across the slope with a little random turn; the mesh
            // runs along its local X axis.
            Vector3 n = _field.normal(p.X, p.Y);
            Vector2 downhill = new Vector2(n.X, n.Z);
            double yaw = downhill.Length() < 0.03 ? _rng.Randf() * TAU : atan2(downhill.X, downhill.Y) + PI * 0.5 + _rng.RandfRange(-0.5f, 0.5f);
            _add(model, p, _rng.RandfRange(0.95f, 1.3f), 0.09, 1.0, true, yaw);
            placed += 1;
        }
    }

    public void _place_roots()
    {
        /// Surface roots radiate from near trunks that are not part of the camp itself.
        // Mostly single roots, one per tree, tucked under the basal flare and
        // bedded into the soil: clusters standing a step from the trunk read as
        // dark mounds in the camp shots.
        Godot.Collections.Array models = new Godot.Collections.Array { "single_root", "single_root", "root_cluster_02", "root_cluster_01" };
        long placed = 0;
        foreach (ScenePlan.TreeEntry t in _plan.near_trees())
        {
            if (t.position.Length() > 80.0 || t.position.Length() < 12.0)
            {
                continue;
            }
            if (_rng.Randf() > 0.32)
            {
                continue;
            }
            TreeSpecies species = TreeSpecies.by_kind(t.kind);
            if (!species.has_leaves() && species.kind != TreeSpecies.Kind.SNAG)
            {
                continue;
            }
            double trunk_r = species.trunk_radius.Y * t.scale * species.root_flare;
            double a = _rng.Randf() * TAU;
            Vector2 p = t.position + new Vector2((float)cos(a), (float)sin(a)) * (float)(trunk_r * 0.3);
            if (!_allowed(p, 1.6, 0.15) || !_camera_clear(p, 1.2))
            {
                continue;
            }
            string model = models[(int)((long)_rng.Randi() % (long)models.Count)].AsString();
            // Roots point away from the trunk; the scans run along local Z.
            double yaw = -a + PI * 0.5 + _rng.RandfRange(-0.35f, 0.35f);
            _add(model, p, _rng.RandfRange(0.7f, 1.0f), 0.16, 1.0, false, yaw);
            placed += 1;
        }
    }

    public void _place_rocks()
    {
        long placed = 0;
        long attempts = 0;
        while (placed < 44 && attempts < 5000)
        {
            attempts += 1;
            Vector2 p = _woodland_point(13.0, 82.0);
            if (!_allowed(p, 2.4, 0.1) || _trunk_distance(p) < 2.2)
            {
                continue;
            }
            if (!_spaced("rock", p, 6.5) || !_camera_clear(p, 2.2))
            {
                continue;
            }
            string model = _rng.Randf() < 0.5 ? "rock_moss_set_01" : "rock_moss_set_02";
            _add(model, p, _rng.RandfRange(0.7f, 1.15f), 0.16, 0.7, true);
            placed += 1;
        }
        placed = 0;
        attempts = 0;
        while (placed < 6 && attempts < 4000)
        {
            attempts += 1;
            Vector2 p2 = _woodland_point(20.0, 70.0);
            if (!_allowed(p2, 3.5, 0.3) || _trunk_distance(p2) < 3.5)
            {
                continue;
            }
            if (!_spaced("rock", p2, 14.0) || !_camera_clear(p2, 3.2))
            {
                continue;
            }
            _add("boulder_01", p2, _rng.RandfRange(1.1f, 1.7f), 0.22, 0.6, true);
            placed += 1;
        }
    }

    public void _place_stones()
    {
        /// Small stones settle along the trail edges and the drier shore.
        Godot.Collections.Array models = new Godot.Collections.Array { "rock_07", "rock_09", "stone_01" };
        long placed = 0;
        long attempts = 0;
        while (placed < 170 && attempts < 9000)
        {
            attempts += 1;
            Vector2 p = default;
            if (_rng.Randf() < 0.6)
            {
                Godot.Collections.Array<Vector2> trail = TerrainField.TRAIL;
                long seg = (long)_rng.Randi() % ((long)trail.Count - 1);
                Vector2 along = trail[(int)seg].Lerp(trail[(int)(seg + 1)], _rng.Randf());
                double side = _rng.RandfRange(1.3f, 3.4f) * (_rng.Randf() < 0.5 ? 1.0 : -1.0);
                Vector2 dir = (trail[(int)(seg + 1)] - trail[(int)seg]).Normalized();
                p = along + new Vector2(-dir.Y, dir.X) * (float)side;
            }
            else
            {
                double a = _rng.Randf() * TAU;
                Vector2 shore = TerrainField.shore_point(a);
                p = shore + (shore - TerrainField.POND_CENTRE).Normalized() * _rng.RandfRange(0.6f, 4.0f);
            }
            if (!_allowed(p, 0.9, 0.05) || !_spaced("stone", p, 0.9))
            {
                continue;
            }
            string model = _rng.Randf() < 0.85 ? models[(int)((long)_rng.Randi() % (long)models.Count)].AsString() : "stone_01";
            _add(model, p, _rng.RandfRange(1.2f, 2.6f), 0.25, 0.8, false);
            placed += 1;
        }
    }

    public void _place_plants(string model, long target, double inner, double outer, double min_cover, double spacing, double trail_clearance, Vector2 scale, double sink, bool floor_align = false)
    {
        long placed = 0;
        long attempts = 0;
        while (placed < target && attempts < target * 14)
        {
            attempts += 1;
            Vector2 p = _woodland_point(inner, outer);
            if (_field.canopy.sample(p.X, p.Y) < min_cover)
            {
                continue;
            }
            if (!_allowed(p, trail_clearance, 0.12) || _trunk_distance(p) < 0.9)
            {
                continue;
            }
            if (!_spaced(model, p, spacing))
            {
                continue;
            }
            // Bushes a step from the lens fill a film frame; keep them two metres off.
            if (!_camera_clear(p, model.StartsWith("shrub", StringComparison.Ordinal) ? 2.0 : 1.0))
            {
                continue;
            }
            _add(model, p, _rng.RandfRange(scale.X, scale.Y), sink, floor_align ? 1.0 : 0.35, false);
            placed += 1;
        }
    }

    public void _place_plan_shrubs()
    {
        /// The scene plan's shrubs around the camp used to be 21-card procedural
        /// bushes whose cards grew to half-metre single leaves beside the lens. Each
        /// planned bush is now one of the photoscanned shrubs at the same spot.
        if (_plan == null)
        {
            return;
        }
        Godot.Collections.Array models = new Godot.Collections.Array { "shrub_02", "shrub_02", "shrub_03", "shrub_04", "shrub_01" };
        foreach (ScenePlan.ShrubEntry shrub in _plan.shrubs)
        {
            Vector2 p = shrub.position;
            if (!_camera_clear(p, 1.5))
            {
                continue;
            }
            string model = models[(int)((long)_rng.Randi() % (long)models.Count)].AsString();
            _add(model, p, clampf(shrub.scale * 0.85, 0.7, 1.5), 0.03, 0.35, false, shrub.rotation);
        }
    }

    public void _place_wet_plants(string model, long target, double inner, double outer, double spacing, Vector2 scale)
    {
        /// Nettles and weeds follow the damp pond margin and the wetter clearing edge.
        long placed = 0;
        long attempts = 0;
        while (placed < target && attempts < target * 16)
        {
            attempts += 1;
            double a = _rng.Randf() * TAU;
            Vector2 shore = TerrainField.shore_point(a);
            Vector2 p = shore + (shore - TerrainField.POND_CENTRE).Normalized() * _rng.RandfRange((float)inner, (float)outer);
            if (!_allowed(p, 1.5, 0.12) || _trunk_distance(p) < 1.0 || !_spaced(model, p, spacing))
            {
                continue;
            }
            if (!_camera_clear(p, 1.0))
            {
                continue;
            }
            _add(model, p, _rng.RandfRange(scale.X, scale.Y), 0.02, 0.3, false);
            placed += 1;
        }
    }

    public void _place_tufts(string model, long target, Vector2 scale)
    {
        /// Scanned tufts sit where the camera passes: beside the trail and the camp.
        long placed = 0;
        long attempts = 0;
        while (placed < target && attempts < target * 14)
        {
            attempts += 1;
            Vector2 p = default;
            if (_rng.Randf() < 0.55)
            {
                Godot.Collections.Array<Vector2> trail = TerrainField.TRAIL;
                long seg = (long)_rng.Randi() % ((long)trail.Count - 1);
                Vector2 along = trail[(int)seg].Lerp(trail[(int)(seg + 1)], _rng.Randf());
                Vector2 dir = (trail[(int)(seg + 1)] - trail[(int)seg]).Normalized();
                p = along + new Vector2(-dir.Y, dir.X) * _rng.RandfRange(0.9f, 6.0f) * (float)(_rng.Randf() < 0.5 ? 1.0 : -1.0);
            }
            else
            {
                double a = _rng.Randf() * TAU;
                p = new Vector2((float)cos(a), (float)sin(a)) * (float)sqrt(lerpf(6.0 * 6.0, 24.0 * 24.0, _rng.Randf()));
            }
            if (!_allowed(p, 0.7, 0.1) || _trunk_distance(p) < 0.8 || !_spaced("tuft", p, 0.7))
            {
                continue;
            }
            _add(model, p, _rng.RandfRange(scale.X, scale.Y), 0.015, 0.4, false);
            placed += 1;
        }
    }

    public void _place_saplings()
    {
        Godot.Collections.Array models = new Godot.Collections.Array { "fir_sapling", "pine_sapling_small" };
        long placed = 0;
        long attempts = 0;
        while (placed < 36 && attempts < 4000)
        {
            attempts += 1;
            Vector2 p = _woodland_point(24.0, 64.0);
            double cover = _field.canopy.sample(p.X, p.Y);
            if (cover < 0.15 || cover > 0.8 || !_allowed(p, 2.2, 0.2))
            {
                continue;
            }
            if (_trunk_distance(p) < 2.5 || !_spaced("sapling", p, 5.0) || !_camera_clear(p, 1.8))
            {
                continue;
            }
            _add(models[(int)(placed % 2)].AsString(), p, _rng.RandfRange(1.0f, 1.7f), 0.03, 0.15, false);
            placed += 1;
        }
    }

    public Vector2 _woodland_point(double inner, double outer)
    {
        // ------------------------------------------------------------------- rules
        double a = _rng.Randf() * TAU;
        double r = sqrt(lerpf(inner * inner, outer * outer, _rng.Randf()));
        return new Vector2((float)cos(a), (float)sin(a)) * (float)r;
    }

    public bool _allowed(Vector2 p, double trail_clearance, double water_margin)
    {
        if (p.DistanceTo(TerrainField.FIRE) < 5.5 || p.DistanceTo(TerrainField.TENT) < 5.0)
        {
            return false;
        }
        if (p.DistanceTo(TerrainField.TABLE) < 2.6 || p.DistanceTo(TerrainField.WOODPILE) < 2.4)
        {
            return false;
        }
        if (_field.walking_distance(p) < trail_clearance)
        {
            return false;
        }
        if (TerrainField.camp_wear(p) > 0.10 && trail_clearance > 1.0)
        {
            return false;
        }
        double y = _field.height(p.X, p.Y);
        if (y < TerrainField.WATER_LEVEL + water_margin)
        {
            return false;
        }
        if (_field.slope(p.X, p.Y) > 0.62)
        {
            return false;
        }
        if (p.DistanceTo(TerrainField.DOCK_START) < 4.0)
        {
            return false;
        }
        return true;
    }

    public const double GRID = 8.0;

    public void _index_obstacles()
    {
        /// Near trunks, planned rocks and camera samples go into 8 m grids once, so the
        /// thousands of placement attempts only test their neighbourhood.
        Godot.Collections.Dictionary radii = new Godot.Collections.Dictionary();
        foreach (ScenePlan.TreeEntry t in _plan.trees)
        {
            if (t.far)
            {
                continue;
            }
            if (!radii.ContainsKey((long)t.kind))
            {
                radii[(long)t.kind] = TreeSpecies.by_kind(t.kind).trunk_radius.Y;
            }
            _grid_add(_trunk_grid, t.position, new Godot.Collections.Array { t.position, G.to_float(radii[(long)t.kind]) * t.scale });
        }
        foreach (ScenePlan.RockEntry r in _plan.rocks)
        {
            _grid_add(_trunk_grid, r.position, new Godot.Collections.Array { r.position, r.scale });
        }
        foreach (Vector3 s in _camera_samples)
        {
            _grid_add(_camera_grid, new Vector2(s.X, s.Z), s);
        }
    }

    public static void _grid_add(Godot.Collections.Dictionary grid, Vector2 p, Variant value)
    {
        Vector2I key = new Vector2I((int)floori(p.X / GRID), (int)floori(p.Y / GRID));
        if (!grid.ContainsKey(key))
        {
            grid[key] = new Godot.Collections.Array();
        }
        G.Call(grid[key], "append", value);
    }

    public double _trunk_distance(Vector2 p)
    {
        /// Clearance to the nearest trunk or planned rock within one grid cell (8 m);
        /// anything farther counts as clear for these small dressings.
        double best = GRID;
        Vector2I key = new Vector2I((int)floori(p.X / GRID), (int)floori(p.Y / GRID));
        for (long dx = -1; dx < 2; dx++)
        {
            for (long dz = -1; dz < 2; dz++)
            {
                Vector2I k = new Vector2I((int)(key.X + dx), (int)(key.Y + dz));
                if (!_trunk_grid.ContainsKey(k))
                {
                    continue;
                }
                foreach (Variant entry_item in G.Iter(_trunk_grid[k]))
                {
                    Godot.Collections.Array entry = entry_item.AsGodotArray();
                    best = minf(best, p.DistanceTo(entry[0].AsVector2()) - G.to_float(entry[1]));
                }
            }
        }
        return best;
    }

    public bool _spaced(string category, Vector2 p, double spacing)
    {
        double cell = 4.0;
        Vector2I key = new Vector2I((int)floori(p.X / cell), (int)floori(p.Y / cell));
        long reach = ceili(spacing / cell);
        for (long dx = -reach, dx_end = reach + 1; dx < dx_end; dx++)
        {
            for (long dz = -reach, dz_end = reach + 1; dz < dz_end; dz++)
            {
                Vector2I k = new Vector2I((int)(key.X + dx), (int)(key.Y + dz));
                if (!_occupied.ContainsKey(k))
                {
                    continue;
                }
                foreach (Variant entry_item in G.Iter(_occupied[k]))
                {
                    Godot.Collections.Array entry = entry_item.AsGodotArray();
                    if (G.eq(entry[0], category) && p.DistanceTo(entry[1].AsVector2()) < spacing)
                    {
                        return false;
                    }
                }
            }
        }
        if (!_occupied.ContainsKey(key))
        {
            _occupied[key] = new Godot.Collections.Array();
        }
        G.Call(_occupied[key], "append", new Godot.Collections.Array { category, p });
        return true;
    }

    public bool _camera_clear(Vector2 p, double radius)
    {
        double ground = _field.height(p.X, p.Y);
        Vector2I key = new Vector2I((int)floori(p.X / GRID), (int)floori(p.Y / GRID));
        long reach = ceili(radius / GRID);
        for (long dx = -reach, dx_end = reach + 1; dx < dx_end; dx++)
        {
            for (long dz = -reach, dz_end = reach + 1; dz < dz_end; dz++)
            {
                Vector2I k = new Vector2I((int)(key.X + dx), (int)(key.Y + dz));
                if (!_camera_grid.ContainsKey(k))
                {
                    continue;
                }
                foreach (Variant s_item in G.Iter(_camera_grid[k]))
                {
                    Vector3 s = s_item.AsVector3();
                    if (s.Y > ground + 3.0)
                    {
                        continue;
                    }
                    if (new Vector2(s.X, s.Z).DistanceTo(p) < radius)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    public static List<Vector3> camera_samples(TerrainField field, long per_shot = 32)
    {
        /// Lens positions of every authored camera move: the films, the intro dolly and
        /// the benchmark path. Large scanned pieces stay out of their way.
        List<Vector3> @out = new List<Vector3>();
        foreach (Variant sequence_name in new Godot.Collections.Array { "showcase", "one_night", "arrival", "pond", "nightfall", "showreel", "afterglow", "storm" })
        {
            foreach (Cinematic.Shot shot in Cinematic.sequence(sequence_name.AsString()))
            {
                Spline path = new Spline(Cinematic.resolve(field, shot.path, shot.absolute));
                for (long i = 0; i < per_shot; i++)
                {
                    @out.Add(Cinematic.camera_position(shot, path, field, shot.duration * (double)i / (double)(per_shot - 1)));
                }
            }
        }
        Spline intro = new Spline(IntroDolly.PATH);
        for (long i2 = 0; i2 < 80; i2++)
        {
            @out.Add(intro.sample((double)i2 / 79.0));
        }
        Spline bench = new Spline(CaptureTool.BENCH_PATH);
        for (long i3 = 0; i3 < 64; i3++)
        {
            Vector3 p = bench.sample((double)i3 / 63.0);
            @out.Add(new Vector3(p.X, (float)(field.height(p.X, p.Z) + p.Y), p.Z));
        }
        return @out;
    }

    public void _add(string model, Vector2 p, double scale, double sink, double align, bool collide, double? yaw_opt = null)
    {
        double yaw = yaw_opt ?? NAN;
        // ------------------------------------------------------------------- upload
        Godot.Collections.Array list = G.get(_variants, model, new Godot.Collections.Array()).AsGodotArray();
        if ((list.Count == 0))
        {
            return;
        }
        ScannedDressing.ScanVariant v = list[(int)((long)_rng.Randi() % (long)list.Count)].As<ScannedDressing.ScanVariant>();
        double ground = _field.height(p.X, p.Y);
        Vector3 n = _field.normal(p.X, p.Y);
        Vector3 up = Vector3.Up.Lerp(n, (float)align).Normalized();
        double turn = !is_nan(yaw) ? yaw : _rng.Randf() * TAU;
        Basis basis = new Basis(Vector3.Up, (float)turn);
        if (up.DistanceTo(Vector3.Up) > 0.001)
        {
            basis = new Basis(new Quaternion(Vector3.Up, up)) * basis;
        }
        basis = basis.Scaled(Vector3.One * (float)scale);
        // Scans are authored on their own ground plane; sink them by a fraction of
        // their height so edges bed into the soil instead of hovering.
        Vector3 origin = new Vector3(p.X, (float)(ground - (v.bounds.Position.Y + sink * v.bounds.Size.Y) * scale), p.Y);
        Transform3D xform = new Transform3D(basis, origin);
        string key = G.format("%s|%d|%d|%d", new Godot.Collections.Array { model, (long)list.IndexOf(v), floori(p.X / CELL), floori(p.Y / CELL) });
        if (!_placements.ContainsKey(key))
        {
            _placements[key] = new Godot.Collections.Dictionary { { "variant", v }, { "transforms", new Godot.Collections.Array() } };
        }
        G.Call(G.Index(_placements[key], "transforms"), "append", xform);
        counts[model] = G.to_int(G.get(counts, model, 0)) + 1;
        if (collide && G.truthy(G.Call(LIBRARY[model], "has", "collide")))
        {
            _add_collision(v, xform, G.Index(LIBRARY[model], "collide").AsString(), G.Index(LIBRARY[model], "kind").AsString());
        }
    }

    public void _add_collision(ScannedDressing.ScanVariant v, Transform3D xform, string shape_kind, string kind)
    {
        StaticBody3D body = new StaticBody3D();
        body.Name = "ScannedCollision";
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", kind == "wood" ? "wood" : "rock");
        CollisionShape3D shape = new CollisionShape3D();
        Aabb b = v.bounds;
        switch (shape_kind)
        {
            case "cylinder":
                CylinderShape3D cyl = new CylinderShape3D();
                cyl.Radius = (float)(maxf(b.Size.X, b.Size.Z) * 0.42);
                cyl.Height = b.Size.Y;
                shape.Shape = cyl;
                shape.Position = b.GetCenter();
                break;
            case "capsule":
                CapsuleShape3D cap = new CapsuleShape3D();
                cap.Radius = (float)(maxf(b.Size.Y, b.Size.Z) * 0.42);
                cap.Height = (float)maxf(b.Size.X, cap.Radius * 2.1);
                shape.Shape = cap;
                shape.Position = b.GetCenter();
                shape.Rotation = new Vector3(0.0f, 0.0f, (float)(PI * 0.5));
                break;
            default:
                SphereShape3D sph = new SphereShape3D();
                sph.Radius = (float)(b.Size.Length() * 0.36);
                shape.Shape = sph;
                shape.Position = b.GetCenter();
                break;
        }
        body.Transform = xform;
        body.AddChild(shape);
        AddChild(body);
    }

    public void _upload()
    {
        foreach (Variant key_key in _placements.Keys)
        {
            string key = key_key.AsString();
            Godot.Collections.Dictionary group = _placements[key].AsGodotDictionary();
            if (group["variant"].VariantType == Variant.Type.Nil)
            {
                continue;
            }
            ScannedDressing.ScanVariant v = group["variant"].As<ScannedDressing.ScanVariant>();
            Godot.Collections.Array transforms = group["transforms"].AsGodotArray();
            Godot.Collections.Dictionary spec = LIBRARY[v.model].AsGodotDictionary();
            bool plant = G.eq(spec["kind"], "plant");
            MultiMeshInstance3D near = _make_batch(key, v, transforms, spec, true);
            if (plant)
            {
                MultiMeshInstance3D far = _make_batch(key + "|far", v, transforms, spec, false);
                _ranges.Add(new Godot.Collections.Dictionary { { "node", near }, { "fade", SHADOW_RANGE }, { "begin", 0.0 }, { "half", _half(near) } });
                _ranges.Add(new Godot.Collections.Dictionary { { "node", far }, { "fade", spec["fade"] }, { "begin", SHADOW_RANGE }, { "half", _half(far) } });
            }
            else
            {
                _ranges.Add(new Godot.Collections.Dictionary { { "node", near }, { "fade", spec["fade"] }, { "begin", 0.0 }, { "half", _half(near) } });
            }
        }
    }

    public MultiMeshInstance3D _make_batch(string key, ScannedDressing.ScanVariant v, Godot.Collections.Array transforms, Godot.Collections.Dictionary spec, bool caster)
    {
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.Mesh = v.mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        Aabb bounds = G.op("*", transforms[0], v.bounds.Grow(0.1f)).AsAabb();
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i].AsTransform3D());
            bounds = bounds.Merge(G.op("*", transforms[(int)i], v.bounds.Grow(0.1f)).AsAabb());
        }
        mm.CustomAabb = bounds;
        MultiMeshInstance3D node = new MultiMeshInstance3D();
        node.Name = "Scanned_" + key.Replace("|", "_");
        node.Multimesh = mm;
        node.CastShadow = caster ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        node.GIMode = !G.eq(spec["kind"], "plant") ? GeometryInstance3D.GIModeEnum.Static : GeometryInstance3D.GIModeEnum.Disabled;
        // Plants use the cheap grass layer (no mirror pass); solids reflect in the pond.
        node.Layers = unchecked((uint)(G.eq(spec["kind"], "plant") ? Pond.GRASS_LAYER : 1));
        AddChild(node);
        batches.Add(node);
        return node;
    }

    public static double _half(MultiMeshInstance3D node)
    {
        return node.Multimesh.CustomAabb.Size.Length() * 0.5 + 2.0;
    }

    public void _quality(QualityPreset p)
    {
        foreach (Godot.Collections.Dictionary entry in _ranges)
        {
            MultiMeshInstance3D node = entry["node"].As<MultiMeshInstance3D>();
            double half = entry["half"].AsDouble();
            node.VisibilityRangeBegin = (float)(G.op("<=", entry["begin"], 0.0).AsBool() ? 0.0 : G.to_float(entry["begin"]) * p.foliage_distance + half - 0.5);
            node.VisibilityRangeEnd = (float)(G.to_float(entry["fade"]) * p.foliage_distance + half);
            node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
        }
    }

    public override void _ExitTree()
    {
        if (Quality.Instance.IsConnected(Quality.SignalName.preset_changed, new Callable(this, ScannedDressing.MethodName._quality)))
        {
            Quality.Instance.Disconnect(Quality.SignalName.preset_changed, new Callable(this, ScannedDressing.MethodName._quality));
        }
    }
}
