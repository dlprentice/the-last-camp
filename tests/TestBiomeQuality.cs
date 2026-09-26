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

/// Structural contracts for the ecological/art-direction passes.
public partial class TestBiomeQuality : TestCase
{
    public void test_main_scene_wires_biome_layers()
    {
        PackedScene packed = Content.Load<PackedScene>("res://scenes/main.tscn");
        assert_true(packed != null, "main scene loads with biome scripts");
        if (packed == null)
        {
            return;
        }
        Node scene = packed.Instantiate();
        assert_true(scene.GetNodeOrNull("BiomeDressing") != null, "moisture-zoned biome dressing is wired");
        assert_true(scene.GetNodeOrNull("ForestFloorDressing") != null, "forest-floor dressing is wired");
        scene.Free();
    }

    public void test_mature_tree_geometry_exceeds_saplings()
    {
        TreeSpecies oak = TreeSpecies.oak();
        TreeSpecies alder = TreeSpecies.alder();
        TreeSpecies spruce = TreeSpecies.spruce();
        TreeSpecies pine = TreeSpecies.pine();
        TreeSpecies sapling = TreeSpecies.sapling();
        assert_gt(oak.trunk_segments, sapling.trunk_segments, "mature oak trunks use higher radial/vertical detail");
        assert_gt(pine.trunk_segments, sapling.trunk_segments, "mature pine trunks use higher detail");
        assert_gt(oak.root_flare, sapling.root_flare, "mature oak has stronger root flare");
        assert_gt(alder.root_flare, sapling.root_flare, "wet-bank alder has stronger root flare");
        assert_gt(spruce.height.Y, oak.height.Y, "spruce retains its tall conifer silhouette");
        assert_gt(pine.height.Y, spruce.height.Y, "pine remains the tallest species envelope");
    }

    public void test_growth_habits_are_materially_distinct()
    {
        TreeSpecies low_fork = TreeSpecies.variant(TreeSpecies.Kind.OAK, 0);
        TreeSpecies high_fork = TreeSpecies.variant(TreeSpecies.Kind.OAK, 5);
        assert_lt(low_fork.split_height, high_fork.split_height, "oak variants span low- and high-fork habits");
        assert_gt(low_fork.split_angle, high_fork.split_angle, "low-fork oak spreads more broadly");
        TreeSpecies wind_pine = TreeSpecies.variant(TreeSpecies.Kind.PINE, 3);
        TreeSpecies upright_pine = TreeSpecies.variant(TreeSpecies.Kind.PINE, 4);
        assert_gt(wind_pine.trunk_lean, upright_pine.trunk_lean * 2.0, "pine variants include visibly wind-shaped and upright habits");
    }

    public void test_floor_detail_meshes_are_real_geometry()
    {
        ArrayMesh root = PropMeshes.log_mesh(1.0, 0.085, 9400, 0.12);
        ArrayMesh deadwood = PropMeshes.bark_log_mesh(1.0, 0.13, 9600, 0.13);
        ArrayMesh stone = PropMeshes.rock(9800, 1.0);
        assert_gt(root.SurfaceGetArrayLen(0), 20, "buttress-root mesh has real cylindrical geometry");
        assert_gt(deadwood.GetSurfaceCount(), 1, "fallen log carries bark plus end geometry");
        assert_gt(stone.SurfaceGetArrayLen(0), 20, "moss-stone source has real geometry");
    }

    public void test_shore_distance_has_expected_sign()
    {
        double east_angle = 0.0;
        Vector2 shore = TerrainField.shore_point(east_angle);
        Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
        assert_lt(BiomeDressing._shore_offset(shore - radial * 0.5f), 0.0, "inside sample is water-side of shore");
        assert_gt(BiomeDressing._shore_offset(shore + radial * 0.5f), 0.0, "outside sample is land-side of shore");
    }
}
