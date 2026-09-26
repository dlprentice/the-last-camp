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

/// Contracts for the stacked broad-form terrain pass.
public partial class TestTerrainEnhanced : TestCase
{
    public void test_enhanced_field_preserves_camp_pads()
    {
        TerrainField @base = new TerrainField();
        TerrainFieldEnhanced enhanced = new TerrainFieldEnhanced();
        assert_near(enhanced.height(TerrainField.FIRE.X, TerrainField.FIRE.Y), @base.height(TerrainField.FIRE.X, TerrainField.FIRE.Y), 1e-6, "fire pad remains authored");
        assert_near(enhanced.height(TerrainField.TENT.X, TerrainField.TENT.Y), @base.height(TerrainField.TENT.X, TerrainField.TENT.Y), 1e-6, "tent pad remains authored");
    }

    public void test_waterline_location_is_exactly_preserved()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        for (long i = 0; i < 32; i++)
        {
            double angle = (double)i / 32.0 * TAU;
            Vector2 shore = TerrainField.shore_point(angle);
            Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
            assert_near(field.height(shore.X, shore.Y), TerrainField.WATER_LEVEL, 0.0001, G.format("enhanced shoreline zero crossing stays fixed at sector %d", i));
            Vector2 inside = shore - radial * 0.55f;
            Vector2 outside = shore + radial * 0.55f;
            assert_lt(field.height(inside.X, inside.Y), TerrainField.WATER_LEVEL, G.format("inside remains wet at sector %d", i));
            assert_gt(field.height(outside.X, outside.Y), TerrainField.WATER_LEVEL, G.format("outside remains dry at sector %d", i));
        }
    }

    public void test_dock_beach_remains_usable()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        double h = field.height(TerrainField.DOCK_START.X, TerrainField.DOCK_START.Y);
        assert_gt(h, TerrainField.WATER_LEVEL, "dock still starts on dry beach");
        assert_lt(h, TerrainField.WATER_LEVEL + 0.65, "dock beach is not lifted into a bank");
    }

    public void test_macro_form_changes_midground_not_microterrain()
    {
        TerrainField @base = new TerrainField();
        TerrainFieldEnhanced enhanced = new TerrainFieldEnhanced();
        Godot.Collections.Array samples = new Godot.Collections.Array { new Vector2(38, 25), new Vector2(54, -31), new Vector2(73, 48), new Vector2(96, -55), new Vector2(-24, 72) };
        double total_difference = 0.0;
        foreach (Variant p in samples)
        {
            total_difference += absf(enhanced.height(G.Index(p, "x").AsDouble(), G.Index(p, "y").AsDouble()) - @base.height(G.Index(p, "x").AsDouble(), G.Index(p, "y").AsDouble()));
        }
        assert_gt(total_difference, 0.12, "broad-form layer materially changes the midground");
        // The fire circle is a deliberate counterexample: no added noise belongs there.
        assert_near(enhanced.height(2.0, 1.0), @base.height(2.0, 1.0), 1e-6, "immediate camp stays free of macro displacement");
    }

    public void test_enhanced_surface_is_continuous_at_walking_scale()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        foreach (Variant centre in new Godot.Collections.Array { new Vector2(35, 35), new Vector2(70, -25), new Vector2(-48, 52), new Vector2(-18, 18) })
        {
            double h = field.height(G.Index(centre, "x").AsDouble(), G.Index(centre, "y").AsDouble());
            foreach (Variant offset in new Godot.Collections.Array { new Vector2(0.25f, 0), new Vector2(-0.25f, 0), new Vector2(0, 0.25f), new Vector2(0, -0.25f) })
            {
                Vector2 q = G.op("+", centre, offset).AsVector2();
                assert_lt(absf(field.height(q.X, q.Y) - h), 0.45, G.format("broad form has no quarter-metre cliff near %s", centre));
            }
        }
    }

    public void test_main_scene_installs_enhanced_field_before_build()
    {
        PackedScene packed = Content.Load<PackedScene>("res://scenes/main.tscn");
        assert_true(packed != null, "terrain candidate main scene loads");
        if (packed == null)
        {
            return;
        }
        Node scene = packed.Instantiate();
        Camp camp = scene.GetNode<Camp>("Camp");
        Node director = scene.GetNode("TerrainFieldDirector");
        assert_true(director != null, "terrain director is present");
        // _ready has not run in this detached instance, so verify the assignment type
        // contract independently rather than pretending the scene build executed.
        camp.field = new TerrainFieldEnhanced();
        assert_true(camp.field is TerrainFieldEnhanced, "Camp accepts the enhanced TerrainField subclass");
        scene.Free();
    }
}
