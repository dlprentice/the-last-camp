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

/// The photoscanned dressing is deterministic, sits on the ground, keeps off
/// the walked routes and the water, and its large pieces stay clear of every
/// authored camera path.
public partial class TestScannedDressing : TestCase
{
    public ScannedDressing _build()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        ScannedDressing dressing = new ScannedDressing();
        dressing.setup_from(field, plan);
        return dressing;
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> _placements(ScannedDressing dressing)
    {
        /// Placements are read from the dressing's own records: the headless dummy
        /// renderer does not store MultiMesh instance data, so reading transforms
        /// back from the batches would test nothing.
        Godot.Collections.Array<Godot.Collections.Dictionary> @out = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (Variant key_key in dressing._placements.Keys)
        {
            string key = key_key.AsString();
            string model = G.split(key, "|")[0];
            foreach (Variant xform_item in G.Iter(G.Index(dressing._placements[key], "transforms")))
            {
                Transform3D xform = xform_item.AsTransform3D();
                @out.Add(new Godot.Collections.Dictionary { { (StringName)"model", model }, { (StringName)"key", key }, { (StringName)"origin", xform.Origin } });
            }
        }
        return @out;
    }

    public Godot.Collections.Array<string> _origins(ScannedDressing dressing)
    {
        Godot.Collections.Array<string> @out = new Godot.Collections.Array<string>();
        foreach (Godot.Collections.Dictionary item in _placements(dressing))
        {
            Vector3 o = item["origin"].AsVector3();
            @out.Add(G.format("%s %.3f %.3f %.3f", new Godot.Collections.Array { item["key"], o.X, o.Y, o.Z }));
        }
        G.sort(@out);
        return @out;
    }

    public void test_placement_is_deterministic_and_populated()
    {
        ScannedDressing a = _build();
        ScannedDressing b = _build();
        assert_eq(_origins(a), _origins(b), "two builds place every scanned instance identically");
        assert_gt(G.get(a.counts, "fern_02", 0).AsDouble(), 400, "scanned ferns fill the shaded woodland");
        assert_gt(G.op("+", G.get(a.counts, "rock_moss_set_01", 0), G.get(a.counts, "rock_moss_set_02", 0)).AsDouble(), 20, "mossy rock sets are present");
        assert_gt(G.op("+", G.get(a.counts, "tree_stump_01", 0), G.get(a.counts, "tree_stump_02", 0)).AsDouble(), 8, "stumps are present");
        // Scanned root balls read as mounds of dirt in the camp shots and are no longer placed.
        assert_eq(G.op("+", G.op("+", G.get(a.counts, "root_cluster_02", 0), G.get(a.counts, "single_root", 0)), G.get(a.counts, "root_cluster_01", 0)), 0, "no scanned root balls");
        assert_gt((long)a.batches.Count, 100, "instances are batched per cell");
        a.Free();
        b.Free();
    }

    public void test_instances_are_grounded_off_routes_and_finite()
    {
        ScannedDressing dressing = _build();
        TerrainField field = dressing._field;
        long @checked = 0;
        long far_from_ground = 0;
        foreach (Godot.Collections.Dictionary item in _placements(dressing))
        {
            Vector3 o = item["origin"].AsVector3();
            string model = item["model"].AsString();
            assert_true(is_finite(o.X) && is_finite(o.Y) && is_finite(o.Z), G.format("finite origin in %s", model));
            double ground = field.height(o.X, o.Z);
            if (absf(o.Y - ground) > 1.2)
            {
                far_from_ground += 1;
            }
            assert_gt(ground, TerrainField.WATER_LEVEL + 0.04, G.format("%s stays out of the pond at %s", new Godot.Collections.Array { model, o }));
            assert_gt(field.walking_distance(new Vector2(o.X, o.Z)), 0.6, G.format("%s keeps off the walked routes at %s", new Godot.Collections.Array { model, o }));
            @checked += 1;
        }
        assert_eq(far_from_ground, 0, "every scanned instance sits within reach of the ground");
        assert_gt(@checked, 2000, "the whole dressing was checked");
        dressing.Free();
    }

    public void test_large_pieces_clear_every_camera_path()
    {
        ScannedDressing dressing = _build();
        List<Vector3> samples = ScannedDressing.camera_samples(dressing._field);
        Godot.Collections.Array large = new Godot.Collections.Array { "tree_stump", "dead_tree_trunk", "boulder_01", "rock_moss_set" };
        long @checked = 0;
        foreach (Godot.Collections.Dictionary item in _placements(dressing))
        {
            string model = item["model"].AsString();
            bool is_large = false;
            foreach (Variant prefix in large)
            {
                if (model.StartsWith(prefix.AsString(), StringComparison.Ordinal))
                {
                    is_large = true;
                }
            }
            if (!is_large)
            {
                continue;
            }
            Vector3 o = item["origin"].AsVector3();
            double ground = dressing._field.height(o.X, o.Z);
            double nearest = INF;
            foreach (Vector3 s in samples)
            {
                if (s.Y > ground + 3.0)
                {
                    continue;
                }
                nearest = minf(nearest, new Vector2(s.X, s.Z).DistanceTo(new Vector2(o.X, o.Z)));
            }
            assert_gt(nearest, 2.0, G.format("%s at %s is %.2f m from a camera path", new Godot.Collections.Array { model, o, nearest }));
            @checked += 1;
        }
        assert_gt(@checked, 30, "large scanned pieces were checked against the camera paths");
        dressing.Free();
    }

    public void test_camp_props_are_placed_whole_and_grounded()
    {
        ScannedDressing dressing = _build();
        List<Vector3> samples = ScannedDressing.camera_samples(dressing._field);
        Godot.Collections.Array expected = new Godot.Collections.Array { "hatchet", "wooden_bucket_01", "wooden_crate_01", "wicker_basket_01", "pot_enamel_01", "brass_pot_01", "handsaw_wood", "modified_thermos", "wooden_lantern_01" };
        foreach (Variant model in expected)
        {
            assert_eq(G.get(dressing.counts, model, 0), 1, G.format("%s is placed exactly once", model));
        }
        assert_eq((long)dressing.props.Count, (long)expected.Count, "every camp prop is a whole scene instance");
        foreach (Node3D prop in dressing.props)
        {
            Vector3 o = prop.Transform.Origin;
            double ground = dressing._field.height(o.X, o.Z);
            assert_lt(absf(o.Y - ground), 0.9, G.format("%s rests near the ground (%.2f vs %.2f)", new Godot.Collections.Array { (StringName)prop.Name, o.Y, ground }));
            double nearest = INF;
            foreach (Vector3 s in samples)
            {
                if (s.Y > ground + 2.5)
                {
                    continue;
                }
                nearest = minf(nearest, new Vector2(s.X, s.Z).DistanceTo(new Vector2(o.X, o.Z)));
            }
            assert_gt(nearest, 0.6, G.format("%s is %.2f m from a camera path", new Godot.Collections.Array { (StringName)prop.Name, nearest }));
        }
        dressing.Free();
    }
}
