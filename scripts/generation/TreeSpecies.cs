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

/// Parameter set for the recursive tree generator. All lengths are metres,
/// angles are radians. Ranges are [min, max] pairs sampled per tree.
public partial class TreeSpecies
{
    public enum Kind
    {
        OAK,
        ALDER,
        SPRUCE,
        PINE,
        SNAG,
        SAPLING,
    }

    public TreeSpecies.Kind kind;
    public string name = "";
    public string bark_set = "";
    public string leaf_atlas = "";

    public Vector2 height = new Vector2(12.0f, 16.0f);
    public Vector2 trunk_radius = new Vector2(0.32f, 0.45f);
    public long trunk_segments = 7;
    public double trunk_taper = 0.55;
    public double trunk_wobble = 0.08;
    public double trunk_lean = 0.06;
    public double root_flare = 1.45;

    /// Where along the trunk the first branches appear (fraction of height).
    public double branch_start = 0.35;
    public double branch_end = 0.92;
    /// Branches per metre of trunk in the branching zone.
    public double branch_density = 1.6;
    public Vector2 branch_angle = new Vector2((float)deg_to_rad(38.0), (float)deg_to_rad(62.0));
    public Vector2 branch_length = new Vector2(0.28f, 0.42f);
    public double branch_radius_ratio = 0.42;
    public long branch_segments = 4;
    /// Positive lifts branch tips towards the sky, negative droops them.
    public double gravitropism = 0.35;
    public double branch_wobble = 0.12;
    /// Number of recursion levels below the trunk (1 = branches only).
    public long levels = 3;
    public Vector2I child_count = new Vector2I(3, 5);
    public Vector2 child_angle = new Vector2((float)deg_to_rad(30.0), (float)deg_to_rad(55.0));
    public Vector2 child_length_ratio = new Vector2(0.55f, 0.75f);
    public double child_radius_ratio = 0.55;
    /// Trunk splits into several leaders at this height fraction (0 = no split).
    public double split_height = 0.0;
    public Vector2I split_count = new Vector2I(3, 4);
    public double split_angle = deg_to_rad(28.0);
    /// Conifer whorls: branches shrink towards the top to form a cone.
    public bool conical = false;
    public double crown_base = 0.0;

    /// Leaf cards.
    public Vector2 leaf_card_size = new Vector2(0.9f, 1.4f);
    public long leaf_cards_per_tip = 2;
    public long leaves_along_branch = 0;
    public long leaf_level_min = 2;
    public double leaf_color_variation = 0.08;
    public Color leaf_tint = new Color(1.0f, 1.0f, 1.0f);
    public double leaf_droop = 0.35;
    public double canopy_scale = 1.0;

    public static TreeSpecies oak()
    {
        TreeSpecies s = new TreeSpecies();
        s.kind = TreeSpecies.Kind.OAK;
        s.name = "oak";
        s.bark_set = "bark_oak";
        s.leaf_atlas = "leaves_oak";
        s.height = new Vector2(12.5f, 17.5f);
        s.trunk_radius = new Vector2(0.40f, 0.58f);
        // Mature broadleaf trunks and scaffold limbs are hero geometry. The six
        // cached variants amortize this extra roundness across every instance.
        s.trunk_segments = 9;
        s.trunk_taper = 0.43;
        s.trunk_wobble = 0.14;
        s.trunk_lean = 0.09;
        s.root_flare = 1.9;
        s.split_height = 0.42;
        s.split_count = new Vector2I(2, 3);
        s.split_angle = deg_to_rad(34.0);
        s.branch_start = 0.25;
        s.branch_end = 0.96;
        s.branch_density = 1.12;
        s.branch_angle = new Vector2((float)deg_to_rad(38.0), (float)deg_to_rad(72.0));
        s.branch_length = new Vector2(0.31f, 0.47f);
        s.branch_radius_ratio = 0.48;
        s.branch_segments = 5;
        s.gravitropism = 0.15;
        s.branch_wobble = 0.18;
        s.levels = 3;
        s.child_count = new Vector2I(3, 5);
        s.child_angle = new Vector2((float)deg_to_rad(26.0), (float)deg_to_rad(60.0));
        s.child_length_ratio = new Vector2(0.48f, 0.74f);
        s.child_radius_ratio = 0.58;
        // Slightly smaller clusters with more branch coverage read as leaves on a
        // branching crown rather than a handful of large green cards.
        s.leaf_card_size = new Vector2(0.70f, 1.04f);
        s.leaf_cards_per_tip = 2;
        s.leaves_along_branch = 5;
        s.leaf_level_min = 2;
        s.leaf_tint = new Color(0.98f, 1.0f, 0.92f);
        s.leaf_color_variation = 0.13;
        s.leaf_droop = 0.38;
        return s;
    }

    public static TreeSpecies alder()
    {
        TreeSpecies s = new TreeSpecies();
        s.kind = TreeSpecies.Kind.ALDER;
        s.name = "alder";
        s.bark_set = "bark_oak";
        s.leaf_atlas = "leaves_birch";
        s.height = new Vector2(11.0f, 15.5f);
        s.trunk_radius = new Vector2(0.22f, 0.31f);
        s.trunk_segments = 9;
        s.trunk_taper = 0.70;
        s.trunk_wobble = 0.065;
        s.trunk_lean = 0.065;
        s.root_flare = 1.55;
        s.split_height = 0.0;
        s.branch_start = 0.27;
        s.branch_end = 0.98;
        s.branch_density = 2.65;
        s.branch_angle = new Vector2((float)deg_to_rad(28.0), (float)deg_to_rad(57.0));
        s.branch_length = new Vector2(0.21f, 0.34f);
        s.branch_radius_ratio = 0.38;
        s.branch_segments = 5;
        s.gravitropism = 0.23;
        s.branch_wobble = 0.12;
        s.levels = 3;
        s.child_count = new Vector2I(3, 4);
        s.child_angle = new Vector2((float)deg_to_rad(23.0), (float)deg_to_rad(52.0));
        s.child_length_ratio = new Vector2(0.48f, 0.70f);
        s.child_radius_ratio = 0.54;
        s.leaf_card_size = new Vector2(0.58f, 0.90f);
        s.leaf_cards_per_tip = 2;
        s.leaves_along_branch = 5;
        s.leaf_level_min = 2;
        s.leaf_tint = new Color(1.0f, 1.0f, 0.9f);
        s.leaf_color_variation = 0.15;
        s.leaf_droop = 0.53;
        return s;
    }

    public static TreeSpecies spruce()
    {
        TreeSpecies s = new TreeSpecies();
        s.kind = TreeSpecies.Kind.SPRUCE;
        s.name = "spruce";
        s.bark_set = "bark_pine";
        s.leaf_atlas = "needles_spruce";
        s.height = new Vector2(17.5f, 25.0f);
        s.trunk_radius = new Vector2(0.32f, 0.45f);
        s.trunk_segments = 11;
        s.trunk_taper = 0.92;
        s.trunk_wobble = 0.035;
        s.trunk_lean = 0.025;
        s.root_flare = 1.5;
        s.conical = true;
        s.branch_start = 0.12;
        s.branch_end = 0.985;
        s.branch_density = 4.45;
        s.branch_angle = new Vector2((float)deg_to_rad(77.0), (float)deg_to_rad(98.0));
        s.branch_length = new Vector2(0.20f, 0.27f);
        s.branch_radius_ratio = 0.35;
        s.branch_segments = 4;
        s.gravitropism = -0.30;
        s.branch_wobble = 0.09;
        s.levels = 2;
        s.child_count = new Vector2I(3, 5);
        s.child_angle = new Vector2((float)deg_to_rad(34.0), (float)deg_to_rad(62.0));
        s.child_length_ratio = new Vector2(0.39f, 0.56f);
        s.child_radius_ratio = 0.50;
        s.leaf_card_size = new Vector2(0.68f, 1.04f);
        s.leaf_cards_per_tip = 2;
        s.leaves_along_branch = 7;
        s.leaf_level_min = 1;
        s.leaf_tint = new Color(0.95f, 1.0f, 0.95f);
        s.leaf_color_variation = 0.07;
        s.leaf_droop = 0.58;
        return s;
    }

    public static TreeSpecies pine()
    {
        TreeSpecies s = new TreeSpecies();
        s.kind = TreeSpecies.Kind.PINE;
        s.name = "pine";
        s.bark_set = "bark_pine";
        s.leaf_atlas = "needles_spruce";
        s.height = new Vector2(19.0f, 27.0f);
        s.trunk_radius = new Vector2(0.36f, 0.50f);
        s.trunk_segments = 11;
        s.trunk_taper = 0.58;
        s.trunk_wobble = 0.12;
        s.trunk_lean = 0.10;
        s.root_flare = 1.52;
        s.branch_start = 0.42;
        s.branch_end = 0.98;
        s.branch_density = 2.35;
        s.branch_angle = new Vector2((float)deg_to_rad(43.0), (float)deg_to_rad(82.0));
        s.branch_length = new Vector2(0.26f, 0.40f);
        s.branch_radius_ratio = 0.47;
        s.branch_segments = 5;
        s.gravitropism = 0.36;
        s.branch_wobble = 0.24;
        s.levels = 3;
        s.child_count = new Vector2I(2, 4);
        s.child_angle = new Vector2((float)deg_to_rad(28.0), (float)deg_to_rad(62.0));
        s.child_length_ratio = new Vector2(0.44f, 0.70f);
        s.child_radius_ratio = 0.55;
        // Dense needle clusters hide the branch rods; sparse fans showed bare sticks.
        s.leaf_card_size = new Vector2(0.74f, 1.05f);
        s.leaf_cards_per_tip = 5;
        s.leaves_along_branch = 5;
        s.leaf_level_min = 1;
        s.leaf_tint = new Color(0.92f, 1.0f, 0.9f);
        s.leaf_color_variation = 0.08;
        s.leaf_droop = 0.22;
        return s;
    }

    public static TreeSpecies snag()
    {
        TreeSpecies s = oak();
        s.kind = TreeSpecies.Kind.SNAG;
        s.name = "snag";
        s.leaf_atlas = "";
        s.height = new Vector2(8.0f, 12.0f);
        s.trunk_radius = new Vector2(0.32f, 0.46f);
        s.trunk_segments = 9;
        s.split_height = 0.55;
        s.split_count = new Vector2I(2, 3);
        s.branch_density = 0.72;
        s.levels = 2;
        s.child_count = new Vector2I(1, 2);
        s.leaf_cards_per_tip = 0;
        s.leaves_along_branch = 0;
        return s;
    }

    public static TreeSpecies sapling()
    {
        TreeSpecies s = alder();
        s.kind = TreeSpecies.Kind.SAPLING;
        s.name = "sapling";
        s.height = new Vector2(2.6f, 4.2f);
        s.trunk_radius = new Vector2(0.04f, 0.07f);
        s.trunk_segments = 5;
        s.root_flare = 1.22;
        s.branch_start = 0.24;
        s.branch_density = 3.0;
        s.branch_length = new Vector2(0.30f, 0.45f);
        s.branch_segments = 3;
        s.levels = 2;
        s.child_count = new Vector2I(2, 3);
        s.leaf_card_size = new Vector2(0.42f, 0.64f);
        s.leaves_along_branch = 3;
        s.leaf_level_min = 1;
        return s;
    }

    public static TreeSpecies by_kind(TreeSpecies.Kind kind)
    {
        switch (kind)
        {
            case TreeSpecies.Kind.OAK:
                return oak();
            case TreeSpecies.Kind.ALDER:
                return alder();
            case TreeSpecies.Kind.SPRUCE:
                return spruce();
            case TreeSpecies.Kind.PINE:
                return pine();
            case TreeSpecies.Kind.SNAG:
                return snag();
            case TreeSpecies.Kind.SAPLING:
                return sapling();
            default:
                G.assert(false, G.format("Unhandled species %s", (long)kind));
                return oak();
        }
    }

    public static TreeSpecies variant(TreeSpecies.Kind kind, long index)
    {
        /// Six growth habits, rather than six seeds of one perfectly upright tree.
        /// Each call returns a fresh parameter set; cached meshes and source species
        /// are never mutated by an individual instance.
        TreeSpecies s = by_kind(kind);
        long v = posmod(index, 6);
        if (kind == TreeSpecies.Kind.OAK)
        {
            Godot.Collections.Array forks = new Godot.Collections.Array { 0.31, 0.40, 0.51, 0.35, 0.46, 0.55 };
            // Fork angles stay under 45 degrees to keep leaders from forming
            // rigid, widely splayed V-shaped silhouettes.
            Godot.Collections.Array angles = new Godot.Collections.Array { 42.0, 36.0, 28.0, 44.0, 38.0, 31.0 };
            Godot.Collections.Array spreads = new Godot.Collections.Array { 1.18, 0.98, 0.84, 1.12, 1.03, 0.89 };
            s.split_height = forks[(int)v].AsDouble();
            s.branch_start = G.op("-", forks[(int)v], new Godot.Collections.Array { 0.10, 0.15, 0.19, 0.11, 0.16, 0.20 }[(int)v]).AsDouble();
            s.split_angle = deg_to_rad(angles[(int)v].AsDouble());
            s.branch_length = G.op("*", s.branch_length, spreads[(int)v]).AsVector2();
            s.gravitropism = new Godot.Collections.Array { 0.08, 0.16, 0.25, 0.10, 0.15, 0.22 }[(int)v].AsDouble();
            s.trunk_lean = new Godot.Collections.Array { 0.15, 0.055, 0.025, 0.11, 0.075, 0.040 }[(int)v].AsDouble();
            s.trunk_wobble = G.op("*", s.trunk_wobble, new Godot.Collections.Array { 1.18, 0.86, 0.72, 1.08, 0.94, 0.80 }[(int)v]).AsDouble();
            s.height = G.op("*", s.height, new Godot.Collections.Array { 0.90, 1.0, 1.10, 0.95, 1.05, 1.12 }[(int)v]).AsVector2();
        }
        else if (kind == TreeSpecies.Kind.ALDER || kind == TreeSpecies.Kind.SAPLING)
        {
            s.branch_start = new Godot.Collections.Array { 0.20, 0.34, 0.27, 0.40, 0.23, 0.31 }[(int)v].AsDouble();
            s.branch_length = G.op("*", s.branch_length, new Godot.Collections.Array { 1.16, 0.90, 1.08, 0.82, 1.12, 0.95 }[(int)v]).AsVector2();
            s.gravitropism = new Godot.Collections.Array { 0.17, 0.31, 0.22, 0.33, 0.18, 0.27 }[(int)v].AsDouble();
            s.trunk_lean = new Godot.Collections.Array { 0.09, 0.03, 0.11, 0.025, 0.07, 0.045 }[(int)v].AsDouble();
            s.branch_wobble = G.op("*", s.branch_wobble, new Godot.Collections.Array { 1.15, 0.80, 1.05, 0.74, 1.12, 0.90 }[(int)v]).AsDouble();
            s.height = G.op("*", s.height, new Godot.Collections.Array { 0.88, 1.07, 0.95, 1.12, 0.92, 1.03 }[(int)v]).AsVector2();
        }
        else
        {
            // Conifers vary in live-crown depth, branch reach and wind-shaped lean;
            // this keeps species identity while preventing repeated identical cones.
            s.branch_length = G.op("*", s.branch_length, new Godot.Collections.Array { 0.88, 1.12, 0.96, 1.06, 0.84, 1.02 }[(int)v]).AsVector2();
            s.branch_start = G.op("*", s.branch_start, new Godot.Collections.Array { 0.90, 1.08, 0.98, 1.13, 0.86, 1.03 }[(int)v]).AsDouble();
            s.trunk_lean = G.op("*", s.trunk_lean, new Godot.Collections.Array { 1.30, 0.65, 1.0, 1.45, 0.55, 0.90 }[(int)v]).AsDouble();
            s.height = G.op("*", s.height, new Godot.Collections.Array { 1.05, 0.94, 1.0, 1.10, 0.90, 1.02 }[(int)v]).AsVector2();
        }
        return s;
    }

    public bool has_leaves()
    {
        return leaf_atlas != "" && (leaf_cards_per_tip > 0 || leaves_along_branch > 0);
    }
}
