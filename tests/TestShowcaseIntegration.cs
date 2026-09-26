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

public partial class TestShowcaseIntegration : TestCase
{
    public void test_new_plant_forms_are_deterministic_finite_and_distinct()
    {
        Godot.Collections.Array<long> sizes = new Godot.Collections.Array<long>();
        for (long form = 0; form < 4; form++)
        {
            ArrayMesh first = HabitatDiversity.plant_mesh(form, 61000 + form);
            ArrayMesh again = HabitatDiversity.plant_mesh(form, 61000 + form);
            List<Vector3> vertices = G.ListFromVariant<Vector3>(first.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]);
            List<Vector3> normals = G.ListFromVariant<Vector3>(first.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal]);
            assert_eq(Variant.From(vertices.ToArray()), again.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex], "plant meshes reproduce exactly");
            assert_gt((long)vertices.Count, 100, "form has authored leaf/stem geometry");
            for (long i = 0, i_end = (long)vertices.Count; i < i_end; i++)
            {
                assert_true(vertices[(int)i].IsFinite() && normals[(int)i].IsFinite(), "plant geometry has finite positions/normals");
                assert_gt(normals[(int)i].LengthSquared(), 0.3, "plant surface has a usable normal");
            }
            if (!sizes.Contains((long)vertices.Count))
            {
                sizes.Add((long)vertices.Count);
            }
        }
        assert_eq((long)sizes.Count, 4, "four different topologies, not four tints of a grass card");
    }

    public void test_habitat_plan_is_repeatable_and_navigation_stays_clear()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        ScenePlan scene = new ScenePlan(field);
        scene.build();
        Godot.Collections.Dictionary first = HabitatDiversity.plan(field, 6000);
        Godot.Collections.Dictionary second = HabitatDiversity.plan(field, 6000);
        assert_eq(first, second, "seeded habitat layout is deterministic");
        Godot.Collections.Array counts = new Godot.Collections.Array { 0, 0, 0, 0 };
        foreach (Variant key_key in first.Keys)
        {
            Vector4I key = key_key.AsVector4I();
            foreach (Variant item_item in G.Iter(first[key]))
            {
                Godot.Collections.Dictionary item = item_item.AsGodotDictionary();
                Transform3D frame = item["frame"].AsTransform3D();
                Vector2 p = new Vector2(frame.Origin.X, frame.Origin.Z);
                double width = frame.Basis.X.Length();
                assert_true(HabitatDiversity.placement_allowed(field, p, 0.48 * width), "the whole small-plant footprint clears travelled routes");
                assert_eq(floori(p.X / HabitatDiversity.CELL_SIZE), key.X, "spatial batch X matches root");
                assert_eq(floori(p.Y / HabitatDiversity.CELL_SIZE), key.Y, "spatial batch Z matches root");
                counts[key.Z] = G.op("+", counts[key.Z], 1);
            }
        }
        foreach (Variant count in counts)
        {
            assert_gt(count.AsDouble(), 5, "each habitat form is actually represented");
        }
    }

    public void test_root_profiles_taper_and_meet_the_actual_surface()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        Godot.Collections.Dictionary curve = ForestFloorDressing.root_curve(field, new Vector2(42, -28), new Vector2(0.6f, 0.8f), 1.6, 0.19, 0.13);
        assert_eq(G.Call(curve["points"], "size"), 11, "curved root sample count");
        foreach (Variant i_item in G.Iter(G.Call(curve["points"], "size")))
        {
            Variant i = i_item;
            Vector3 point = G.Index(curve["points"], i).AsVector3();
            assert_true(point.IsFinite(), "finite root point");
            assert_lt(point.Y, field.surface_height(point.X, point.Z), "root centre is embedded, not hovering");
            if (G.op(">", i, 0).AsBool())
            {
                assert_lt(G.Index(curve["radii"], i).AsDouble(), G.Index(curve["radii"], G.op("-", i, 1)).AsDouble(), "root radius continuously tapers");
            }
        }
        assert_lt(G.Index(curve["radii"], -1).AsDouble(), 0.004, "tip disappears into earth");
        assert_eq(PropMaterials.bark().GetShaderParameter("wind_response"), 0.0, "deadwood/roots do not sway like upright trees");
    }

    public void test_fractured_timber_keeps_bark_and_end_grain_surfaces()
    {
        ArrayMesh mesh = ForestFloorDressing.fractured_log(0.14, 9801);
        assert_eq(mesh.GetSurfaceCount(), 2, "bark and exposed wood keep separate material slots");
        for (long surface = 0, surface_end = mesh.GetSurfaceCount(); surface < surface_end; surface++)
        {
            foreach (Variant point_item in G.Iter(mesh.SurfaceGetArrays((int)surface)[(int)Mesh.ArrayType.Vertex]))
            {
                Vector3 point = point_item.AsVector3();
                assert_true(point.IsFinite(), "fractured timber has finite geometry");
            }
            assert_gt(mesh.SurfaceGetArrayIndexLen((int)surface), 20, "surface is renderable");
        }
    }

    public void test_photo_movement_and_focus_have_consistent_direction_and_timing()
    {
        assert_eq(PhotoRig.move_direction(Basis.Identity, new Vector3(0, 0, -1)), Vector3.Forward, "W moves along Godot -Z");
        assert_eq(PhotoRig.move_direction(Basis.Identity, new Vector3(0, 0, 1)), Vector3.Back, "S moves backward");
        assert_near(PhotoRig.move_direction(Basis.Identity, new Vector3(1, 1, -1)).Length(), 1.0, 1e-6, "diagonal flight has no speed bonus");
        foreach (Variant fps in new Godot.Collections.Array { 24, 60, 144 })
        {
            double distance = 2.0;
            foreach (Variant frame_item in G.Iter(fps))
            {
                Variant frame = frame_item;
                distance = PhotoRig.focus_step(distance, 12.0, G.op("/", 1.0, fps).AsDouble());
            }
            assert_near(distance, 12.0 - 10.0 * exp(-5.0), 0.00001, "focus pull is frame-rate independent");
        }
        assert_eq(PhotoRig.focus_step(2, 12, 0), 2.0, "paused focus does not advance");
    }

    public void test_shelter_audio_uses_the_visible_roof_volume()
    {
        assert_true(CampWeather.tent_shelter(new Vector3(0, 0.7f, 0)), "listener under roof is sheltered");
        assert_false(CampWeather.tent_shelter(new Vector3(0, 1.8f, 0)), "above ridge is exposed");
        assert_false(CampWeather.tent_shelter(new Vector3(1.3f, 0.4f, 0)), "beside tent is exposed");
        assert_false(CampWeather.tent_shelter(new Vector3(0, 0.5f, 1.7f)), "beyond tent end is exposed");
    }

    public void test_canopy_shedding_is_a_post_rain_effect()
    {
        assert_near(CanopyDrips.post_rain_strength(1, 0), 1, 1e-6, "wet canopy drips after rain");
        assert_near(CanopyDrips.post_rain_strength(1, 1), 0, 1e-6, "full rain does not double with after-rain drops");
        assert_near(CanopyDrips.post_rain_strength(0, 0), 0, 1e-6, "dry canopy stays quiet");
    }

    public void test_wildlife_habitats_are_distributed_but_remain_real_scale()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Wildlife life = new Wildlife(new TerrainFieldEnhanced());
        tree.Root.AddChild(life);
        life.SetProcess(false);
        life._build_small_life();
        assert_eq((long)life._butterflies.Count, 12, "six butterfly habitat pairs");
        assert_eq((long)life._dragonflies.Count, 12, "six pond-edge dragonfly pairs");
        assert_gt((long)life.fish_routes.Count, 11, "multiple schools have safe sampled routes");
        assert_gt(life._butterfly_habitats[0].DistanceTo(life._butterfly_habitats[^1]), 15, "habitats are not all in the same tiny patch");
        life._life_clock = 7.0;
        life._update_small_life(1, 1, 0.4);
        foreach (Node3D bird in life._birds)
        {
            assert_near(bird.Scale.DistanceTo(Vector3.One), 0, 0.00001, "autonomous birds no longer grow/shrink on arrival");
        }
        life.Free();
    }

    public void test_showcase_camera_path_clears_enhanced_scene()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Dock dock = new Dock(field);
        tree.Root.AddChild(dock);
        dock.build();
        Godot.Collections.Array<Vector3> piles = new Godot.Collections.Array<Vector3>();
        foreach (Vector3 top in dock._piles)
        {
            piles.Add(dock.ToGlobal(top));
        }
        List<string> hits = Cinematic.obstructions(field, plan, "showcase", piles, dock.canoe.Position, dock.canoe.Rotation.Y);
        assert_eq((long)hits.Count, 0, G.format("showcase camera obstruction: %s", string.Join(", ", G.slice(hits, 0, 5))));
        // The dock parents its canoe beside itself rather than under the jetty.
        dock.canoe.Free();
        dock.Free();
    }
}
