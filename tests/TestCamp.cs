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
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

/// Placement and fire-intensity invariants for the authored camp.
public partial class TestCamp : TestCase
{
    public void test_firepit_feed_clamps_and_raises()
    {
        TerrainField field = new TerrainField();
        Firepit pit = new Firepit(field);
        pit.intensity = 1.0;
        pit.feed();
        assert_gt(pit.intensity, 1.0, "feeding the fire should raise intensity");
        assert_lt(pit.intensity, Firepit.MAX_INTENSITY + 0.001, "intensity should stay at or under the cap");
        pit.intensity = Firepit.MAX_INTENSITY;
        pit.feed();
        assert_near(pit.intensity, Firepit.MAX_INTENSITY, 0.001, "feeding a roaring fire should clamp");
        pit.Free();
    }

    public void test_camp_sits_above_the_water()
    {
        TerrainField field = new TerrainField();
        assert_gt(field.height(TerrainField.FIRE.X, TerrainField.FIRE.Y), TerrainField.WATER_LEVEL + 0.4, "fire pad should be dry");
        assert_gt(field.height(TerrainField.TENT.X, TerrainField.TENT.Y), TerrainField.WATER_LEVEL + 0.4, "tent pad should be dry");
        assert_lt(field.height(TerrainField.POND_CENTRE.X, TerrainField.POND_CENTRE.Y), TerrainField.WATER_LEVEL, "pond basin should be underwater");
    }

    public void test_shoreline_follows_the_authored_shape()
    {
        TerrainField field = new TerrainField();
        for (long i = 0; i < 16; i++)
        {
            double angle = (double)i / 16.0 * TAU;
            double r = TerrainField.pond_radius_at(angle);
            Vector2 inside = TerrainField.POND_CENTRE + new Vector2((float)cos(angle), (float)sin(angle)) * (float)(r * 0.93);
            Vector2 outside = TerrainField.POND_CENTRE + new Vector2((float)cos(angle), (float)sin(angle)) * (float)(r * 1.08);
            assert_lt(field.height(inside.X, inside.Y), TerrainField.WATER_LEVEL, G.format("just inside the shoreline is wet (angle %d)", i));
            assert_gt(field.height(outside.X, outside.Y), TerrainField.WATER_LEVEL, G.format("just outside the shoreline is dry (angle %d)", i));
        }
        assert_gt(field.height(TerrainField.DOCK_START.X, TerrainField.DOCK_START.Y), TerrainField.WATER_LEVEL, "dock starts on land");
        assert_lt(field.height(TerrainField.DOCK_START.X, TerrainField.DOCK_START.Y), TerrainField.WATER_LEVEL + 0.6, "dock starts on the beach");
    }

    public void test_dry_land_stays_above_water_across_the_meadow()
    {
        TerrainField field = new TerrainField();
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(5));
        for (long i = 0; i < 400; i++)
        {
            Vector2 p = new Vector2(rng.RandfRange(-70.0f, 70.0f), rng.RandfRange(-70.0f, 70.0f));
            Vector2 to_centre = p - TerrainField.POND_CENTRE;
            if (to_centre.Length() < TerrainField.pond_radius_at(atan2(to_centre.Y, to_centre.X)) * 1.2)
            {
                continue;
            }
            assert_gt(field.height(p.X, p.Y), TerrainField.WATER_LEVEL + 0.2, G.format("meadow point %s is dry", p));
        }
    }

    public void test_height_grid_matches_the_function()
    {
        TerrainField field = new TerrainField();
        field.bake_height_grid(20.0, 0.5);
        assert_near(field.height_fast(3.25, -7.75), field.height(3.25, -7.75), 0.03, "grid interpolates the function closely");
        assert_near(field.height_fast(0.0, 0.0), field.height(0.0, 0.0), 1e-4, "grid is exact on lattice points");
        assert_near(field.height_fast(50.0, 50.0), field.height(50.0, 50.0), 1e-6, "outside the grid falls back to the function");
        List<float> axis = TerrainBuilder.axis_coordinates();
        assert_eq(axis[(int)((long)axis.Count / 2)], 0.0, "axis is centred");
        assert_near(axis[(int)((long)axis.Count / 2 + 1)], TerrainBuilder.INNER_SPACING, 1e-6, "inner spacing");
        assert_near(axis[(int)((long)axis.Count - 1)], TerrainField.OUTER_EXTENT * 0.5, 1e-3, "axis reaches the world edge");
    }

    public void test_distant_plants_sample_the_rendered_terrain_triangles()
    {
        TerrainField field = new TerrainField();
        field.bake_height_grid(TerrainBuilder.COLLISION_HALF, TerrainBuilder.INNER_SPACING);
        TerrainBuilder builder = new TerrainBuilder(field);
        ArrayMesh mesh = builder.build_mesh(null);
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        List<Vector3> vertices = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
        List<int> indices = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
        List<float> axis = TerrainBuilder.axis_coordinates();
        foreach (Variant location in new Godot.Collections.Array { new Vector2(-95, 18), new Vector2(-175, -36), new Vector2(-265, 84), new Vector2(-410, -130), new Vector2(-630, 50), new Vector2(590, 110) })
        {
            long ix = G.bsearch(axis, G.Index(location, "x").AsSingle()) - 1;
            long iz = G.bsearch(axis, G.Index(location, "y").AsSingle()) - 1;
            long start = (iz * ((long)axis.Count - 1) + ix) * 6;
            for (long triangle = 0; triangle < 2; triangle++)
            {
                Vector3 a = vertices[indices[(int)(start + triangle * 3)]];
                Vector3 b = vertices[indices[(int)(start + triangle * 3 + 1)]];
                Vector3 c = vertices[indices[(int)(start + triangle * 3 + 2)]];
                // Sample the mesh itself, independently of the field's grid formula.
                Vector3 point = a * 0.29f + b * 0.23f + c * 0.48f;
                assert_near(field.surface_height(point.X, point.Z), point.Y, 0.0001, G.format("grass roots follow the actual rendered triangle at %s", point));
            }
        }
        for (long i = 0, i_end = (long)axis.Count - 1; i < i_end; i++)
        {
            assert_lt((double)axis[(int)(i + 1)] - axis[(int)i], TerrainBuilder.OUTER_MAX_SPACING + 0.001, "outer cells remain small enough to support hill vegetation");
        }
    }

    public void test_scene_plan_keeps_the_clearing_open()
    {
        TerrainField field = new TerrainField();
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        assert_gt((long)plan.trees.Count, 80.0, "the forest should be populated");
        assert_gt((long)plan.rocks.Count, 40.0, "rocks should be planned");
        assert_gt((long)plan.shrubs.Count, 20.0, "shrubs should be planned");
        foreach (ScenePlan.TreeEntry t in plan.near_trees())
        {
            assert_gt(t.position.DistanceTo(TerrainField.FIRE), 8.5, "near trees must stay out of the fire circle");
            assert_gt(t.position.DistanceTo(TerrainField.TENT), 5.5, "near trees must stay off the tent pad");
        }
    }

    public void test_prop_meshes_are_nonempty()
    {
        ArrayMesh log = PropMeshes.log_mesh(1.0, 0.1, 1);
        assert_gt(log.GetSurfaceCount(), 0.0, "log mesh should have a surface");
        assert_gt(log.SurfaceGetArrayLen(0), 8.0, "log mesh should have vertices");
        ArrayMesh rock = PropMeshes.rock(3, 1.0);
        assert_gt(rock.SurfaceGetArrayLen(0), 20.0, "rock mesh should have vertices");
        ArrayMesh box = PropMeshes.box_mesh(Vector3.One);
        assert_eq(box.SurfaceGetArrayLen(0), 24, "a box is six faces of four verts");
    }

    public void test_credit_roll_reads_every_source_table()
    {
        Godot.Collections.Array surfaces = Cinematic._credit_rows("res://textures/SOURCES.md", 1, 2, 4);
        assert_eq((long)surfaces.Count, 9, "all nine surface sets are credited");
        assert_eq(G.Index(surfaces[0], 0), "Leafy Grass", "asset slugs read as names");
        assert_eq(G.Index(surfaces[0], 1), "Charlotte Baglioni", "CC0 creators carry no licence note");
        Godot.Collections.Array models = Cinematic._credit_rows("res://models/SOURCES.md", 1, 2, 4);
        assert_gt((long)models.Count, 30, "every photoscanned model is credited");
        bool has_two_creators = false;
        foreach (Variant row in models)
        {
            if ((long)G.str(G.Index(row, 1)).IndexOf(",", StringComparison.Ordinal) != -1 && (long)G.str(G.Index(row, 1)).IndexOf("(", StringComparison.Ordinal) == -1)
            {
                has_two_creators = true;
            }
        }
        assert_true(has_two_creators, "multiple creators are listed without their role notes");
        Godot.Collections.Array sounds = Cinematic._audio_credit_rows();
        assert_eq((long)sounds.Count, 6, "six recordings are credited once each");
        bool by = false;
        foreach (Variant row2 in sounds)
        {
            if ((long)G.str(G.Index(row2, 1)).IndexOf("SoundBible", StringComparison.Ordinal) != -1 && (long)G.str(G.Index(row2, 1)).IndexOf("CC BY 3.0", StringComparison.Ordinal) != -1)
            {
                by = true;
            }
        }
        assert_true(by, "attribution licences name the provider and the licence");
    }

    public void test_ridges_plant_the_forest_species_to_a_closed_canopy()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        Forest forest = new Forest(field, plan);
        tree.Root.AddChild(forest);
        forest.build();
        RidgeForest ridges = new RidgeForest(field, forest);
        tree.Root.AddChild(ridges);
        ridges.build();
        // 5.4 m spacing near, 6.6 m in the far band: about 21,000 trees with touching crowns.
        assert_gt(ridges.tree_count, 18000, "the hills carry a closed canopy, not a sparse scatter");
        assert_true(ridges.species_counts.ContainsKey((long)TreeSpecies.Kind.OAK) && ridges.species_counts.ContainsKey((long)TreeSpecies.Kind.SPRUCE), "ridges mix the forest's broadleaf and conifer species");
        assert_gt((long)forest.ridge_multimeshes.Count, 40, "ridge trees batch per species, variant and cell");
        long lod_levels = 0;
        foreach (MultiMeshInstance3D mmi in forest.ridge_multimeshes)
        {
            if (((string)mmi.Name).StartsWith("Ridge_bark", StringComparison.Ordinal) && mmi.Multimesh.Mesh.GetSurfaceCount() > 0)
            {
                Godot.Collections.Dictionary surface = RenderingServer.MeshGetSurface(mmi.Multimesh.Mesh.GetRid(), 0);
                lod_levels = maxi(lod_levels, (long)G.get(surface, "lods", new Godot.Collections.Array()).AsGodotArray().Count);
            }
        }
        assert_gt(lod_levels, 0, "distant bark carries generated mesh LODs");
        ridges.Free();
        forest.Free();
    }

    public void test_dock_and_canoe_build()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        field.bake_height_grid(60.0, 0.5);
        Node3D holder = new Node3D();
        tree.Root.AddChild(holder);
        Dock dock = new Dock(field);
        holder.AddChild(dock);
        dock.build();
        assert_true(dock.canoe != null, "dock moors a canoe");
        assert_true(dock.lantern != null, "dock carries a lantern");
        assert_gt(((ArrayMesh)((MeshInstance3D)dock.GetNode("Deck")).Mesh).SurfaceGetArrayLen(0), 500.0, "deck has many plank vertices");
        assert_gt(((ArrayMesh)((MeshInstance3D)dock.GetNode("Piles")).Mesh).SurfaceGetArrayLen(0), 100.0, "piles have vertices");
        assert_gt(((ArrayMesh)((MeshInstance3D)dock.GetNode("Mooring")).Mesh).SurfaceGetArrayLen(0), 20.0, "mooring line exists");
        assert_near(dock.deck_y, TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER, 1e-6, "deck sits above the water");
        Canoe canoe = dock.canoe;
        assert_near(canoe.Position.Y, TerrainField.WATER_LEVEL - Canoe.DRAFT, 1e-6, "canoe floats at its draft");
        Vector2 to_centre = new Vector2(canoe.Position.X, canoe.Position.Z) - TerrainField.POND_CENTRE;
        assert_lt(to_centre.Length(), TerrainField.pond_radius_at(atan2(to_centre.Y, to_centre.X)), "canoe is on the water");
        assert_gt(field.water_depth(canoe.Position.X, canoe.Position.Z), Canoe.DRAFT + 0.2, "canoe has water under its keel");
        ArrayMesh hull = (ArrayMesh)((MeshInstance3D)canoe.GetNode("Hull")).Mesh;
        assert_gt(hull.SurfaceGetArrayLen(0), 300.0, "hull is lofted");
        foreach (Vector3 top in dock._piles)
        {
            Vector3 pile = dock.ToGlobal(top);
            double gap = new Vector2(pile.X, pile.Z).DistanceTo(new Vector2(canoe.Position.X, canoe.Position.Z));
            assert_gt(gap, Canoe.HALF_BEAM + Dock.PILE_RADIUS + 0.3, G.format("canoe clears pile at %s", pile));
        }
        assert_gt(Canoe.FLOOR_HEIGHT, Canoe.DRAFT, "canoe floor sits above the waterline");
        TriangleMesh deck_surface = ((MeshInstance3D)dock.GetNode("Deck")).Mesh.GenerateTriangleMesh();
        Node3D stones = dock.GetNode<Node3D>("SkippingStones");
        Godot.Collections.Array<Aabb> stone_bounds = new Godot.Collections.Array<Aabb>();
        foreach (Node pebble in stones.GetChildren())
        {
            if (!(pebble is MeshInstance3D))
            {
                continue;
            }
            double clearance = INF;
            Aabb bounds = new Aabb();
            bool first = true;
            foreach (Variant vertex_item in G.Iter(((MeshInstance3D)pebble).Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]))
            {
                Vector3 vertex = vertex_item.AsVector3();
                Vector3 p = G.op("*", G.op("*", stones.Transform, pebble.Get("transform")), vertex).AsVector3();
                bounds = first ? new Aabb(p, Vector3.Zero) : bounds.Expand(p);
                first = false;
                Godot.Collections.Dictionary hit = deck_surface.IntersectRay(new Vector3(p.X, 0.25f, p.Z), Vector3.Down);
                if (!(hit.Count == 0))
                {
                    clearance = minf(clearance, G.op("-", p.Y, G.Index(hit["position"], "y")).AsDouble());
                }
            }
            assert_gt(clearance, -0.0001, "a skipping stone does not penetrate a plank");
            assert_lt(clearance, 0.0005, "every skipping stone touches an actual plank");
            foreach (Aabb other in stone_bounds)
            {
                assert_false(bounds.Intersects(other), "the loose stones do not overlap");
            }
            stone_bounds.Add(bounds);
        }
        assert_eq((long)stone_bounds.Count, 5, "all five skipping stones remain");
        holder.Free();
    }

    public void test_lantern_posts_stand_beside_the_trail()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        field.bake_height_grid(60.0, 0.5);
        Node3D holder = new Node3D();
        tree.Root.AddChild(holder);
        Campsite site = new Campsite(field);
        holder.AddChild(site);
        site.build();
        long posted = 0;
        foreach (CampLantern lantern in site.lanterns)
        {
            if (lantern.GetNodeOrNull("Post") == null)
            {
                continue;
            }
            posted += 1;
            Vector3 p = lantern.GlobalPosition;
            assert_gt(field.trail_distance(p.X, p.Z), 1.4, G.format("post at %s stands off the trail", p));
            assert_gt(new Vector2(p.X, p.Z).DistanceTo(TerrainField.FIRE), 3.0, "post keeps clear of the fire circle");
        }
        assert_eq(posted, 2, "two hook posts are placed");
        holder.Free();
    }

    public void test_intro_dolly_path_is_clear_of_trees()
    {
        TerrainField field = new TerrainField();
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        List<string> hits = IntroDolly.obstructions(field, plan);
        assert_eq((long)hits.Count, 0, G.format("dolly path is obstructed: %s", string.Join(", ", G.slice(hits, 0, 3))));
    }

    public void test_cinematic_shots_are_clear_of_props_and_trees()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        field.bake_height_grid(60.0, 0.5);
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        Node3D holder = new Node3D();
        tree.Root.AddChild(holder);
        Dock dock = new Dock(field);
        holder.AddChild(dock);
        dock.build();
        Godot.Collections.Array<Vector3> piles = new Godot.Collections.Array<Vector3>();
        foreach (Vector3 top in dock._piles)
        {
            piles.Add(dock.ToGlobal(top));
        }
        List<string> hits = Cinematic.obstructions(field, plan, "showreel", piles, dock.canoe.Position, dock.canoe.Rotation.Y);
        hits.AddRange(Cinematic.obstructions(field, plan, "afterglow", piles, dock.canoe.Position, dock.canoe.Rotation.Y));
        hits.AddRange(Cinematic.obstructions(field, plan, "one_night", piles, dock.canoe.Position, dock.canoe.Rotation.Y));
        assert_eq((long)hits.Count, 0, G.format("cinematic shots obstructed: %s", string.Join(", ", G.slice(hits, 0, 5))));
        holder.Free();
    }

    public void test_player_item_and_lantern_state()
    {
        Player player = new Player();
        assert_eq((StringName)player.held_item, (StringName)"", "player starts empty-handed");
        player.held_item = "log";
        assert_eq((StringName)player.held_item, (StringName)"log", "held item should stick");
        assert_false(player.lantern_on, "hand lantern starts off");
        player.Free();
    }

    public void test_meadow_cover_and_camp_clearance()
    {
        TerrainField field = new TerrainField();
        assert_gt(field.grass_suitability(9, 8), 0.7, "open meadow supports a continuous grass stand");
        assert_near(field.grass_suitability(0, 0), 0, 0.001, "fire pad stays bare");
        assert_near(field.grass_suitability(7.5, -4.5), 0, 0.001, "tent stays clear of grass");
        assert_near(field.grass_suitability(-34, 4), 0, 0.001, "grass cannot grow through the pond");
        assert_lt(field.grass_suitability(1.0, 9.0), 0.1, "walked trail stays legible");
    }

    public void test_lilies_live_in_shallows_outside_the_dock_lane()
    {
        TerrainField field = new TerrainField();
        ShoreLife shore = new ShoreLife(field);
        shore.build();
        assert_gt((long)shore.pad_positions.Count, 30, "sheltered banks carry lily colonies");
        foreach (Vector3 p in shore.pad_positions)
        {
            assert_gt(field.water_depth(p.X, p.Z), 0.2, "every lily is over water");
            assert_lt(field.water_depth(p.X, p.Z), 2.4, "lilies only grow in the shallows");
            assert_near(p.Y, TerrainField.WATER_LEVEL + 0.01, 0.01, "pads stay within two centimetres of the water");
            Transform3D stem = shore._stem_transform(p);
            assert_near((stem * Vector3.Zero).DistanceTo(p), 0.0, 1e-6, "a leaning petiole stays attached to its leaf");
            Vector3 root = stem * new Vector3(0.08f, -1.0f, -0.06f);
            assert_near(root.Y, field.height(root.X, root.Z) - 0.012, 0.0001, "rhizome roots meet the bed after bending");
            Godot.Collections.Array<Vector2> lane = new Godot.Collections.Array<Vector2> { TerrainField.DOCK_START, TerrainField.POND_CENTRE };
            assert_gt(TerrainField.distance_to_polyline(new Vector2(p.X, p.Z), lane), 3.0, "the dock approach remains open");
        }
        shore.Free();
    }

    public void test_stone_skips_finish_and_leave_ripples()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Pond pond = new Pond(new TerrainField());
        tree.Root.AddChild(pond);
        pond.build();
        assert_false(pond.skip_stone(new Vector3(10, 1, 10), Vector3.Right), "dry ground rejects a skip");
        assert_true(pond.skip_stone(new Vector3(-25, 0, 4), Vector3.Left), "open water accepts a skip");
        assert_false(pond.skip_stone(new Vector3(-25, 0, 4), Vector3.Left), "a running skip rejects repeated input");
        for (long i = 0; i < 180; i++)
        {
            pond._ripple_clock += 1.0 / 60.0;
            pond._update_skip(1.0 / 60.0);
        }
        assert_eq(pond._ripple_cursor, 3, "three surface contacts leave three impulses");
        assert_false(pond._stone.Visible, "stone disappears after the final impact");
        assert_lt(pond._skip_age, 0.0, "skip releases its input lock");
        pond.Free();
    }

    public void test_kitchen_props_and_feet_have_support()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        CampKitchen kitchen = new CampKitchen(field);
        tree.Root.AddChild(kitchen);
        kitchen.build();
        assert_eq((long)kitchen.feet.Count, 4, "the table has four measured feet");
        foreach (Vector3 foot in kitchen.feet)
        {
            assert_near(foot.Y, field.height(foot.X, foot.Z) - 0.025, 0.002, "every foot meets the ground");
        }
        foreach (Variant node_name in new Godot.Collections.Array { "CoffeePot", "EnamelMug", "FieldJournal" })
        {
            assert_near(G.Index(kitchen.GetNode(node_name.AsNodePath()).Get("position"), "y").AsDouble(), CampKitchen.CLOTH_TOP, 0.001, G.format("cloth is the mount for %s", node_name));
        }
        assert_lt(field.grass_suitability(TerrainField.TABLE.X, TerrainField.TABLE.Y), 0.01, "the kitchen has a clear working pad");
        BoxShape3D support = kitchen.GetNode("ClothSupport").GetChild(0).Get("shape").As<BoxShape3D>();
        assert_lt(support.Size.Y, 0.05, "cloth collides with the thin tabletop, not the rounded player blocker");
        assert_near(CampKitchen._linen_crease(-0.36, -0.02), 0, 1e-6, "kettle has a flat fabric footprint");
        kitchen.Free();
    }

    public void test_cloth_has_connected_faces_and_noncollapsed_corners()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        CampKitchen kitchen = new CampKitchen(new TerrainField());
        tree.Root.AddChild(kitchen);
        kitchen.build();
        Godot.Collections.Array arrays = kitchen.cloth.Mesh.SurfaceGetArrays(0);
        List<Vector3> verts = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
        List<int> indices = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
        Godot.Collections.Dictionary directed_edges = new Godot.Collections.Dictionary();
        for (long triangle = 0, triangle_end = (long)indices.Count; triangle < triangle_end; triangle += 3)
        {
            long a = indices[(int)triangle];
            long b = indices[(int)(triangle + 1)];
            long c = indices[(int)(triangle + 2)];
            assert_gt((verts[(int)b] - verts[(int)a]).Cross(verts[(int)c] - verts[(int)a]).Length(), 1e-7, "no cloth triangle collapses into a line");
            foreach (Variant edge_item in new Godot.Collections.Array { new Vector2I((int)a, (int)b), new Vector2I((int)b, (int)c), new Vector2I((int)c, (int)a) })
            {
                Vector2I edge = edge_item.AsVector2I();
                assert_false(directed_edges.ContainsKey(edge), "adjacent cloth faces must wind consistently");
                directed_edges[edge] = true;
            }
        }
        kitchen.Free();
    }

    public void test_film_demonstrates_live_changes_and_a_continuous_water_crossing()
    {
        List<Cinematic.Shot> shots = Cinematic.sequence("one_night");
        long lapses = 0;
        long feed_cues = 0;
        foreach (Cinematic.Shot shot in shots)
        {
            if (!is_nan(shot.hour_end) && absf(shot.hour_end - shot.hour) > 1.0)
            {
                lapses += 1;
            }
            if (shot.feed_fire && shot.feed_at > 0.0)
            {
                feed_cues += 1;
            }
        }
        assert_eq(lapses, 2, "sunset and sunrise carry the major visible time transitions");
        Cinematic.Shot dawn = null;
        foreach (Cinematic.Shot shot2 in shots)
        {
            if (shot2.weather == "dawn")
            {
                dawn = shot2;
            }
        }
        assert_true(dawn != null, "the film contains dawn");
        assert_lt(dawn.hour, 29.75, "dawn opens before sunrise");
        assert_gt(dawn.hour_end, 29.75, "the first rays arrive on screen");
        assert_gt(feed_cues, 0, "a visible fire shot contains a timed feed response");
        assert_near(Cinematic.duration("one_night"), 337.0, 1e-6, "five minutes of film plus the title, closing card and credit roll");
        Cinematic.Shot crossing = null;
        foreach (Cinematic.Shot shot3 in shots)
        {
            if (shot3.label == "Beneath the reflections")
            {
                crossing = shot3;
            }
        }
        assert_true(crossing != null, "one uninterrupted shot contains the water crossing");
        assert_gt(G.front(crossing.path).Y, TerrainField.WATER_LEVEL + 0.2, "crossing begins in air");
        assert_gt(G.back(crossing.path).Y, TerrainField.WATER_LEVEL + 0.2, "crossing returns to air");
        assert_lt(crossing.path[3].Y, TerrainField.WATER_LEVEL - 0.5, "crossing visits the pond bed");
        assert_near(new Spline(crossing.path).sample(crossing.rest_at).DistanceTo(crossing.path[3]), 0.0, 1e-5, "the underwater hold stays at its composed viewpoint when the exit path changes");
        for (long i = 0, i_end = (long)shots.Count; i < i_end; i++)
        {
            assert_true(shots[(int)i].clock, "every scene shows the world clock");
            if (shots[(int)i].continuous_in)
            {
                assert_gt(i, 0, "a continuous move has a preceding shot");
                Cinematic.Shot previous = shots[(int)(i - 1)];
                assert_eq(previous.fade_out, 0.0, "a continuous move stays visible");
                assert_eq(shots[(int)i].fade_in, 0.0, "a continuous move has no dissolve");
                TerrainField field = new TerrainField();
                assert_near(G.back(Cinematic.resolve(field, previous.path, previous.absolute)).DistanceTo(G.front(Cinematic.resolve(field, shots[(int)i].path, shots[(int)i].absolute))), 0, 1e-5, "no camera cut");
                assert_near(G.back(Cinematic.resolve(field, previous.look, previous.absolute)).DistanceTo(G.front(Cinematic.resolve(field, shots[(int)i].look, shots[(int)i].absolute))), 0, 1e-5, "no aim cut");
                assert_near(!is_nan(previous.fov_end) ? previous.fov_end : previous.fov, shots[(int)i].fov, 1e-5, "no lens cut");
                assert_near(!is_nan(previous.exposure_end) ? previous.exposure_end : previous.exposure, shots[(int)i].exposure, 1e-5, "no exposure cut");
            }
            else
            {
                assert_gt(shots[(int)i].fade_in, 0.5, "a new viewpoint must fade in");
            }
            if (i == (long)shots.Count - 1 || !shots[(int)(i + 1)].continuous_in)
            {
                assert_gt(shots[(int)i].fade_out, 0.5, "every separate viewpoint fades out");
            }
            assert_gt(shots[(int)i].hour_end, shots[(int)i].hour, "time progresses through every scene");
            if (i > 0)
            {
                if (shots[(int)i].weather == "dawn")
                {
                    assert_gt(shots[(int)i].hour, shots[(int)(i - 1)].hour_end, "the overnight time jump moves forward under a fade");
                }
                else
                {
                    assert_near(shots[(int)i].hour, shots[(int)(i - 1)].hour_end, 1e-6, "the clock stays continuous within each part of the evening");
                }
            }
        }
        assert_eq(Cinematic.clock_text(24.0), "00:00", "midnight wraps cleanly");
        assert_eq(Cinematic.clock_text(28.7), "04:42", "unwrapped dawn time displays normally");
    }

    public void test_film_camera_moves_slowly_without_speed_steps()
    {
        TerrainField field = new TerrainField();
        foreach (Cinematic.Shot shot in Cinematic.one_night())
        {
            Spline path = new Spline(Cinematic.resolve(field, shot.path, shot.absolute));
            Spline look = new Spline(Cinematic.resolve(field, shot.look, shot.absolute));
            Vector3 previous = Cinematic.camera_position(shot, path, field, 0.0);
            Vector3 direction = (look.sample(0) - previous).Normalized();
            double max_speed = 0.0;
            double max_turn = 0.0;
            for (long frame = 1, frame_end = roundi(shot.duration * 60.0) + 1; frame < frame_end; frame++)
            {
                double u = Cinematic.motion_progress(shot, frame / 60.0);
                Vector3 p = Cinematic.camera_position(shot, path, field, frame / 60.0);
                Vector3 next_direction = (Cinematic.aim_target(shot, p, look.sample(u), frame / 60.0) - p).Normalized();
                max_speed = maxf(max_speed, p.DistanceTo(previous) * 60.0);
                max_turn = maxf(max_turn, rad_to_deg(direction.AngleTo(next_direction)) * 60.0);
                previous = p;
                direction = next_direction;
            }
            double speed_limit = shot.walk ? 1.4 : 0.55;
            assert_lt(max_speed, speed_limit, G.format("%s camera respects the walking/observation pace (%.3f m/s)", new Godot.Collections.Array { shot.label, max_speed }));
            assert_lt(max_turn, shot.walk || !(shot.pitch_envelope.Count == 0) ? 8.0 : 5.5, G.format("%s has a gentle turn (%.3f deg/s)", new Godot.Collections.Array { shot.label, max_turn }));
            assert_near(Cinematic.motion_progress(shot, shot.hold_start), 0.0, 1e-6, "opening composition is held");
            assert_near(Cinematic.motion_progress(shot, shot.duration - shot.hold_end), 1.0, 1e-6, "closing composition is held");
        }
        Spline uneven = new Spline(new Godot.Collections.Array<Vector3> { Vector3.Zero, new Vector3(0.8f, 1.5f, 0), new Vector3(10, 0, 2), new Vector3(12, 3, 4) });
        double expected = uneven.total_length() / 1000.0;
        for (long i = 1; i < 1000; i++)
        {
            double distance = uneven.sample(i / 1000.0).DistanceTo(uneven.sample((i - 1) / 1000.0));
            assert_near(distance, expected, expected * 0.06, "arc-length sampling avoids speed changes between unequal segments");
        }
    }

    public void test_camper_camera_follows_ground_and_stops_its_gait()
    {
        TerrainField field = new TerrainField();
        long walking_shots = 0;
        foreach (Cinematic.Shot shot in Cinematic.one_night())
        {
            if (!shot.walk)
            {
                continue;
            }
            walking_shots += 1;
            Spline path = new Spline(Cinematic.resolve(field, shot.path, shot.absolute));
            for (long frame = 0, frame_end = roundi(shot.duration * 60.0) + 1; frame < frame_end; frame += 5)
            {
                double seconds = frame / 60.0;
                Vector3 pos = Cinematic.camera_position(shot, path, field, seconds);
                double height = pos.Y - Cinematic.walking_support(field, pos);
                assert_near(height, shot.eye_height, 0.025, "eye follows terrain/deck rather than floating between spline control points");
            }
            Vector3 start = Cinematic.camera_position(shot, path, field, 0.0);
            assert_near(start.DistanceTo(Cinematic.camera_position(shot, path, field, shot.hold_start * 0.5)), 0.0, 1e-6, "no gait while waiting to walk");
        }
        assert_eq(walking_shots, 4, "arrival, pond approach, night return and dawn are grounded walks");
        Vector3 deck_pos = new Vector3(-18.5f, 0, 5.97f);
        assert_true(Cinematic.on_dock(deck_pos), "the pond walk actually reaches the boards");
        assert_near(Cinematic.walking_support(field, deck_pos), TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER, 0.01, "deck height supports the walker above the water");
    }

    public void test_fire_ring_is_partially_embedded_across_each_footprint()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        Firepit pit = new Firepit(field);
        tree.Root.AddChild(pit);
        pit.Position = new Vector3(TerrainField.FIRE.X, (float)field.height(0, 0), TerrainField.FIRE.Y);
        pit._build_ring();
        assert_eq(pit.GetChildCount(), 15, "the complete stone ring remains");
        foreach (Node stone_item in pit.GetChildren())
        {
            MeshInstance3D stone = (MeshInstance3D)stone_item;
            double low = INF;
            double high = -INF;
            foreach (Variant vertex_item in G.Iter(stone.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]))
            {
                Vector3 vertex = vertex_item.AsVector3();
                Vector3 p = stone.ToGlobal(vertex);
                double clearance = p.Y - field.height(p.X, p.Z);
                low = minf(low, clearance);
                high = maxf(high, clearance);
            }
            assert_lt(low, -0.025, "the base enters the soil");
            assert_gt(high, 0.08, "the stone still has a visible crown");
            assert_gt(-low / (high - low), 0.27, "lower stone mass is embedded");
            assert_lt(-low / (high - low), 0.37, "the ring is not buried out of sight");
        }
        pit.Free();
    }

    public void test_seats_face_the_fire_and_leave_the_paths_open()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        TerrainField field = new TerrainField();
        Campsite site = new Campsite(field);
        tree.Root.AddChild(site);
        site._build_seats();
        for (long i = 0, i_end = (long)TerrainField.SEATS.Count; i < i_end; i++)
        {
            Node3D bench = site.GetNode<Node3D>(G.format("SplitLogBench_%d", i));
            Vector2 centre = TerrainField.SEATS[(int)i];
            Vector3 inward = new Vector3(-centre.X, 0, -centre.Y).Normalized();
            assert_gt((-bench.Basis.Z).Dot(inward), 0.999, "the sitting side faces the hearth");
            foreach (Variant x_item in new Godot.Collections.Array { -0.925, 0.0, 0.925 })
            {
                double x = x_item.AsDouble();
                foreach (Variant z_item in new Godot.Collections.Array { -0.20, 0.20 })
                {
                    double z = z_item.AsDouble();
                    Vector3 p = bench.ToGlobal(new Vector3((float)x, 0, (float)z));
                    assert_gt(field.walking_distance(new Vector2(p.X, p.Z)), 0.80, "seating keeps the walked routes clear");
                    assert_gt(TerrainField.camp_wear(new Vector2(p.X, p.Z)), 0.94, "grass cannot grow through the seat");
                }
            }
            Vector2 footwell = centre - centre.Normalized() * 0.32f;
            assert_gt(TerrainField.camp_wear(footwell), 0.9, "feet have a worn patch of earth");
        }
        site.Free();
    }

    public void test_water_fog_waits_for_the_whole_near_plane()
    {
        double tan_half = tan(deg_to_rad(62.0) * 0.5);
        foreach (Variant basis in new Godot.Collections.Array { Basis.Identity, Basis.FromEuler(new Vector3(0.35f, 0, 0.4f)) })
        {
            Transform3D frame = new Transform3D(basis.AsBasis(), new Vector3(0, (float)TerrainField.WATER_LEVEL, 0));
            assert_near(WorldController.water_fog_blend(frame, 0.05, tan_half, 16.0 / 9.0), 0.0, 1e-6, "global fog must not obscure the above-water part of the lens");
            frame.Origin.Y = (float)(frame.Origin.Y - 0.25);
            assert_near(WorldController.water_fog_blend(frame, 0.05, tan_half, 16.0 / 9.0), 1.0, 1e-6, "deeply submerged lens uses native fog");
        }
        // Include the bounded interaction waves as well as the analytic wind swell.
        Transform3D crossing = new Transform3D(Basis.Identity, new Vector3(0, (float)(TerrainField.WATER_LEVEL - WorldController.WATER_WAVE_ENVELOPE - 0.055), 0));
        double blend = WorldController.water_fog_blend(crossing, 0.05, tan_half, 16.0 / 9.0);
        assert_gt(blend, 0.0, "fog eases in after the lens clears the waves");
        assert_lt(blend, 1.0, "the handoff covers a range of depths");
    }

    public void test_water_lens_crossings_hysteresis_and_drying()
    {
        WorldController world = new WorldController();
        world.update_water_lens(-0.3, 0.1);
        assert_eq(world.lens_entry_age, 100.0, "opening pose does not invent a splash");
        world.update_water_lens(0.10, 0.1);
        assert_eq(world.lens_entry_age, 0.0, "diving starts one bubble burst");
        world.update_water_lens(0.01, 0.5);
        world.update_water_lens(-0.02, 0.5);
        assert_near(world.lens_entry_age, 1.0, 1e-6, "small waves do not repeat the entry");
        assert_eq(world.lens_exit_age, 100.0, "small waves do not fabricate an exit");
        world.update_water_lens(-0.10, 0.1);
        assert_eq(world.lens_exit_age, 0.0, "surfacing starts the draining film");
        world.update_water_lens(-0.30, 3.0);
        assert_near(world.lens_exit_age, 3.0, 1e-6, "runoff keeps drying above water");
        world.reset_water_lens();
        world.update_water_lens(0.8, 0.1);
        assert_eq(world.lens_entry_age, 100.0, "a capture teleport starts with a settled lens");
        world.Free();
    }

    public void test_meadow_planning_keeps_walked_routes_bare()
    {
        TerrainField field = new TerrainField();
        field.bake_height_grid(16.0, 0.5);
        GrassPlanter planter = new GrassPlanter(field, 16.0, 0.2);
        planter.plan();
        foreach (GrassPlanter.Chunk chunk in planter.chunks)
        {
            for (long i = 0, i_end = chunk.count; i < i_end; i++)
            {
                long index = i * GrassPlanter.FLOATS_PER_INSTANCE;
                Vector2 p = new Vector2(chunk.buffer[(int)(index + 3)], chunk.buffer[(int)(index + 11)]);
                assert_gt(field.walking_distance(p), 0.519, "grass must leave a clear walking ribbon");
                assert_lt(TerrainField.camp_wear(p), 0.941, "dense grass must stay out of the working pads");
            }
        }
    }

    public void test_reflection_culling_preserves_water_crossing_the_view_edge()
    {
        Godot.Collections.Array<Plane> planes = new Godot.Collections.Array<Plane> { new Plane(Vector3.Right, 1.0f) };
        assert_true(Pond.bounds_in_frustum(new Aabb(new Vector3(0.5f, 0, 0), Vector3.One), planes), "a partially visible surface must still render");
        assert_true(Pond.bounds_in_frustum(new Aabb(new Vector3(-3, 0, 0), Vector3.One), planes), "water inside the plane remains visible");
        assert_true(!Pond.bounds_in_frustum(new Aabb(new Vector3(1.01f, 0, 0), Vector3.One), planes), "a completely excluded surface can suspend the mirror");
    }

    public void test_waterline_effect_is_excluded_from_the_mirror()
    {
        WorldController world = new WorldController();
        world._build_post();
        world._build_water_volume();
        Pond pond = new Pond(new TerrainField());
        pond.material = new ShaderMaterial();
        pond._build_reflection();
        assert_eq((long)world.waterline.Layers & (long)pond.reflection_camera.CullMask, 0, "the mirrored camera must not bake its underwater lens fog into the pond reflection");
        assert_eq((long)world.waterline.Layers & (long)pond.underwater_camera.CullMask, 0, "the underside mirror must also exclude the screen waterline and surface");
        foreach (Variant camera in new Godot.Collections.Array { pond.reflection_camera, pond.underwater_camera, pond.transmission_camera })
        {
            assert_eq(G.op("&", (long)world.water_volume.Layers, G.Index(camera, "cull_mask")), 0, "crossing light scattering must not enter an auxiliary water view");
        }
        assert_eq((long)pond.reflection_camera.CullMask & Pond.UNDERWATER_REFLECTION_LAYER, 0, "the upper mirror must preserve the air halfspace");
        assert_gt((long)pond.underwater_camera.CullMask & Pond.UNDERWATER_REFLECTION_LAYER, 0, "the underside mirror must preserve the submerged halfspace");
        assert_eq((long)pond.underwater_camera.CullMask & Pond.AIR_FOG_LAYER, 0, "the reflected water must not integrate air mist below its physical halfspace");
        FogVolume reflected_volume = pond.GetNode<FogVolume>("ReflectedWaterVolume");
        assert_gt((long)reflected_volume.Layers & (long)pond.underwater_camera.CullMask, 0, "the underside reflection must include its own bounded light scattering");
        foreach (Variant camera2 in new Godot.Collections.Array { pond.reflection_camera, pond.transmission_camera })
        {
            assert_eq(G.op("&", (long)reflected_volume.Layers, G.Index(camera2, "cull_mask")), 0, "reflected water scattering must not enter an air view");
        }
        MeshInstance3D mirror_fog = pond.GetNode<MeshInstance3D>("ReflectedWaterPath");
        assert_eq((long)mirror_fog.Layers & (long)pond.reflection_camera.CullMask, 0, "reflected-path fog must never affect the upper mirror");
        assert_eq((long)mirror_fog.Layers & (long)pond.transmission_camera.CullMask, 0, "reflected-path fog must never affect air transmission");
        world.Free();
        pond.Free();
    }

    public void test_smooth_normals_retain_the_authored_outward_direction()
    {
        MeshBuilder mb = new MeshBuilder();
        long a = mb.add_vertex(Vector3.Zero, Vector3.Up, Vector2.Zero);
        long b = mb.add_vertex(Vector3.Right, Vector3.Up, Vector2.Right);
        long c = mb.add_vertex(new Vector3(1, 0, 1), Vector3.Up, Vector2.One);
        long d = mb.add_vertex(Vector3.Back, Vector3.Up, Vector2.Down);
        mb.add_quad_facing(a, b, c, d, Vector3.Up);
        mb.recompute_normals();
        foreach (Vector3 normal in mb.normals)
        {
            assert_gt(normal.Dot(Vector3.Up), 0.99, "clockwise faces must retain upward lighting normals");
        }
    }

    public void test_grass_lods_keep_every_blade_root_and_tip()
    {
        foreach (Variant rows in new Godot.Collections.Array { Variant.From(new List<int>(new List<int> { 0, 2, 4 }).ToArray()), Variant.From(new List<int>(new List<int> { 0, 3 }).ToArray()), Variant.From(new List<int>(new List<int> { 0 }).ToArray()) })
        {
            List<int> indices = GrassPlanter.clump_lod_indices(7, 5, G.ListFromVariant<int>(rows));
            assert_lt((long)indices.Count, 7 * 11 * 3, "each level reduces geometry");
            for (long blade = 0; blade < 7; blade++)
            {
                assert_true(indices.Contains((int)(blade * 13)), "each blade retains its root");
                assert_true(indices.Contains((int)(blade * 13 + 12)), "each blade retains its full height");
            }
        }
    }

    public void test_surfacing_restores_the_sky_and_air_fog()
    {
        WorldController world = new WorldController();
        world.environment = new Environment();
        world._build_post();
        foreach (Variant blend_item in new Godot.Collections.Array { 0.0, 0.25, 0.75, 1.0 })
        {
            double blend = blend_item.AsDouble();
            world.underwater_blend = blend;
            world._apply_fog();
            double partial = world.waterline_material.GetShaderParameter("density").AsDouble();
            assert_near(world.environment.FogDensity + partial, WorldController.WATER_FOG_DENSITY, 1e-6, "the lens handoff must not add or lose water density");
        }
        world.underwater = true;
        world.underwater_blend = 1.0;
        world._apply_fog();
        assert_gt(world.environment.FogDensity, WorldController.AIR_FOG_DENSITY * 10.0, "water remains denser than clear air");
        assert_near(world.environment.AmbientLightEnergy, 0.6, 1e-6, "water receives subdued ambient light");
        world.underwater = false;
        world.underwater_blend = 0.0;
        world._apply_fog();
        assert_eq(world.environment.BackgroundColor, Colors.Black, "water colour must not remain behind the sky");
        assert_lt(world.environment.FogDensity, 0.001, "surfacing restores clear air");
        assert_lt(world.environment.VolumetricFogDensity, 0.001, "the water volume must not follow the camera onto land");
        world.Free();
    }

    public void test_storm_cues_keep_flashes_brief_and_ground_wet()
    {
        Vector4 before = CampWeather.conditions("storm", 2.0, 40.0);
        Vector4 first = CampWeather.conditions("storm", 9.0, 40.0);
        Vector4 strike = CampWeather.conditions("storm", 33.0, 40.0);
        Vector4 between = CampWeather.conditions("storm", 12.0, 40.0);
        assert_eq(before.Y, 0.0, "the rain arrives after the first distant thunderhead");
        assert_gt(first.W, 0.5, "first flash exists");
        assert_gt(strike.W, first.W, "the closer strike is stronger");
        assert_eq(between.W, 0.0, "darkness separates strikes");
        assert_gt(CampWeather.conditions("storm_short", 16.5, 20.0).W, 0.9, "the short edit retains the main lightning event");
        assert_gt(CampWeather.conditions("storm_short", 18.0, 20.0).Z, 0.99, "surfaces are wet before the close rain shot");
        assert_eq(CampWeather.strike_flash(-0.01), 0.0, "no light precedes a strike");
        assert_eq(CampWeather.strike_flash(0.6), 0.0, "no lingering lightning");
        assert_eq(CampWeather.conditions("shelter_rain", 2.0, 30.0).Y, 1.0, "rain continues outside the shelter");
        Vector4 clearing = CampWeather.conditions("clearing", 5.0, 24.0);
        assert_eq(clearing.Y, 0.0, "rain stops for the clearing stars");
        assert_eq(clearing.Z, 1.0, "surfaces stay wet after rainfall");
        assert_gt(CampWeather.conditions("dawn", 20.0, 20.0).Z, 0.25, "dawn does not instantly dry the camp");
        CampWeather weather = new CampWeather();
        foreach (Variant seconds in new Godot.Collections.Array { 8.0, 12.0, 16.0, 20.0 })
        {
            weather.apply_chapter("clearing", seconds.AsDouble(), 24.0, false);
            assert_eq(weather.cloud_coverage(0.0), 0.0, "advected cumulus cannot hide the star reveal");
        }
        weather.apply_chapter("", 0.0, 1.0, false);
        assert_eq(weather.cloud_coverage(1.0), 0.36, "the approved daylight cloud cover stays unchanged");
        weather.Free();
    }
}
