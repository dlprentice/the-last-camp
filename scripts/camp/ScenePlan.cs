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

/// Deterministic layout of everything placed on the landscape: trees (species,
/// seed, scale), boulders and shrubs. Also paints the canopy coverage into the
/// terrain field so ground materials and grass respond to the tree cover.
public partial class ScenePlan
{
    public partial class TreeEntry : RefCounted
    {
        public Vector2 position;
        public TreeSpecies.Kind kind;
        public long seed_value;
        public double scale;
        public double rotation;
        public bool far = false;
    }

    public partial class RockEntry
    {
        public Vector2 position;
        public double scale;
        public double rotation;
        public double sink;
        public long variant;
    }

    public partial class ShrubEntry
    {
        public Vector2 position;
        public double scale;
        public double rotation;
    }

    public const double NEAR_TREE_MIN_SPACING = 4.6;
    public const double FAR_TREE_MIN_SPACING = 5.5;
    public const double NEAR_LIMIT = 88.0;
    public const double FAR_LIMIT = 210.0;
    /// Irregular woodland stands, with the wet western bank carrying several
    /// ages of broadleaf growth. Z is the patch radius, not a placement grid.
    public static readonly Godot.Collections.Array<Vector3> WOODLAND_GROVES = new Godot.Collections.Array<Vector3> { new Vector3(-61, -22, 13), new Vector3(-73, 19, 15), new Vector3(-66, 43, 17), new Vector3(-43, -43, 14), new Vector3(-96, -52, 20), new Vector3(-117, 20, 22), new Vector3(-108, 65, 18), new Vector3(-29, 63, 15), new Vector3(35, 67, 19), new Vector3(61, 13, 20), new Vector3(35, -46, 18), new Vector3(84, -53, 21), new Vector3(124, 35, 24), new Vector3(129, -61, 23), new Vector3(73, 111, 22), new Vector3(-164, 74, 26), new Vector3(-149, -101, 24), new Vector3(80, -168, 25) };

    public Godot.Collections.Array<ScenePlan.TreeEntry> trees = new Godot.Collections.Array<ScenePlan.TreeEntry>();
    public List<ScenePlan.RockEntry> rocks = new List<ScenePlan.RockEntry>();
    public List<ScenePlan.ShrubEntry> shrubs = new List<ScenePlan.ShrubEntry>();

    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public TerrainField _field;
    public FastNoiseLite _grove_noise = new FastNoiseLite();
    public Godot.Collections.Array<Vector2> _intro_lane = new Godot.Collections.Array<Vector2>();

    public ScenePlan(TerrainField field, long seed_value = 1337)
    {
        _field = field;
        _rng.Seed = unchecked((ulong)(seed_value));
        _grove_noise.Seed = (int)(seed_value + 412);
        _grove_noise.Frequency = 0.034f;
        _grove_noise.FractalOctaves = 2;
        foreach (Vector3 p in IntroDolly.PATH)
        {
            _intro_lane.Add(new Vector2(p.X, p.Z));
        }
    }

    public void build()
    {
        _place_specimens();
        _place_treeline();
        _place_far_forest();
        _place_saplings();
        _place_rocks();
        _place_shrubs();
        _paint_canopy();
        _place_pond_enclosure();
        _place_dense_stands();
        _paint_canopy();
    }

    public Godot.Collections.Array<ScenePlan.TreeEntry> near_trees()
    {
        return G.filter(trees, (ScenePlan.TreeEntry t) => !t.far);
    }

    public Godot.Collections.Array<ScenePlan.TreeEntry> far_trees()
    {
        return G.filter(trees, (ScenePlan.TreeEntry t) => t.far);
    }

    public ScenePlan.TreeEntry _add_tree(Vector2 pos, TreeSpecies.Kind kind, double scale, bool far = false)
    {
        // ------------------------------------------------------------------- trees
        ScenePlan.TreeEntry t = new ScenePlan.TreeEntry();
        t.position = pos;
        t.kind = kind;
        t.seed_value = (long)_rng.Randi();
        t.scale = scale;
        t.rotation = _rng.Randf() * TAU;
        t.far = far;
        trees.Add(t);
        return t;
    }

    public void _place_specimens()
    {
        _add_tree(new Vector2(17.5f, -11.5f), TreeSpecies.Kind.OAK, 1.18);
        _add_tree(new Vector2(-13.0f, -19.0f), TreeSpecies.Kind.PINE, 1.1);
        _add_tree(new Vector2(21.0f, 13.5f), TreeSpecies.Kind.ALDER, 1.05);
        _add_tree(new Vector2(-20.0f, 23.0f), TreeSpecies.Kind.OAK, 1.0);
        _add_tree(new Vector2(10.0f, -26.0f), TreeSpecies.Kind.SPRUCE, 1.05);
        _add_tree(new Vector2(-9.0f, -31.0f), TreeSpecies.Kind.SNAG, 1.0);
        _add_tree(new Vector2(29.0f, 22.0f), TreeSpecies.Kind.SNAG, 0.9);
    }

    public bool _blocked(Vector2 pos, double spacing)
    {
        if (_shore_offset(pos) < 3.5)
        {
            return true;
        }
        if (_field.trail_distance(pos.X, pos.Y) < 3.6)
        {
            return true;
        }
        if (pos.DistanceTo(TerrainField.FIRE) < 9.0 || pos.DistanceTo(TerrainField.TENT) < 6.0)
        {
            return true;
        }
        if (_field.is_underwater(pos.X, pos.Y))
        {
            return true;
        }
        // Preserve the authored crane into the clearing, including the wider
        // crowns of mature trees along its high opening section.
        if (TerrainField.distance_to_polyline(pos, _intro_lane) < 8.5)
        {
            return true;
        }
        foreach (ScenePlan.TreeEntry t in trees)
        {
            if (t.position.DistanceTo(pos) < spacing)
            {
                return true;
            }
        }
        return false;
    }

    public static double _shore_offset(Vector2 pos)
    {
        /// Signed distance from the shoreline: negative inside the pond.
        Vector2 to_centre = pos - TerrainField.POND_CENTRE;
        return to_centre.Length() - TerrainField.pond_radius_at(atan2(to_centre.Y, to_centre.X));
    }

    public TreeSpecies.Kind _species_for(Vector2 pos)
    {
        /// Species mix depends on where the tree stands: conifers dominate the higher
        /// north and east ground, broadleaf the south and the low western shore.
        /// Species by place. The bank line and the wood behind the camp read as a
        /// wall of rounded crowns from the arrival and pond views when every tree
        /// there is broadleaf, so a share of dark spruce spires is mixed in everywhere
        /// and the conifer belt to the north-east stays denser.
        double shore = _shore_offset(pos);
        if (shore < 12.0)
        {
            double bank = _rng.Randf();
            if (bank < 0.13)
            {
                return TreeSpecies.Kind.SPRUCE;
            }
            return bank < 0.74 ? TreeSpecies.Kind.ALDER : TreeSpecies.Kind.OAK;
        }
        double angle = atan2(pos.X, -pos.Y);
        double north_east = clampf(cos(angle - PI * 0.25) * 0.5 + 0.5, 0.0, 1.0);
        double roll = _rng.Randf();
        double conifer_chance = lerpf(0.16, 0.44, north_east);
        if (shore < 25.0)
        {
            conifer_chance *= 0.6;
        }
        if (roll < conifer_chance)
        {
            return _rng.Randf() < 0.62 ? TreeSpecies.Kind.SPRUCE : TreeSpecies.Kind.PINE;
        }
        return _rng.Randf() < 0.55 ? TreeSpecies.Kind.OAK : TreeSpecies.Kind.ALDER;
    }

    public double _tree_scale(double low, double high)
    {
        /// Most trees share a canopy height; a few emergents stand a third taller so
        /// the skyline is not a level hedge.
        if (_rng.Randf() < 0.09)
        {
            return _rng.RandfRange(1.30f, 1.50f);
        }
        return _rng.RandfRange((float)low, (float)high);
    }

    public static double sunset_opening(Vector2 pos)
    {
        /// Regenerating ground remains inside the western woodland. A separately
        /// composed bank canopy encloses its former broad entrance from the pond.
        return TerrainField.woodland_opening(pos);
    }

    public double _density_bias(Vector2 pos)
    {
        double patch = _grove_noise.GetNoise2D(pos.X, pos.Y);
        return clampf(0.66 + patch * 0.95, 0.18, 1.0) * (1.0 - sunset_opening(pos));
    }

    public Vector2 _woodland_position(double inner, double outer)
    {
        double a = _rng.Randf() * TAU;
        if (_rng.Randf() < 0.65)
        {
            Vector3 grove = WOODLAND_GROVES[(int)((long)_rng.Randi() % (long)WOODLAND_GROVES.Count)];
            Vector2 offset = new Vector2((float)(cos(a) * 1.12), (float)(sin(a) * 0.84)) * (float)sqrt(_rng.Randf()) * grove.Z;
            return new Vector2(grove.X, grove.Y) + offset.Rotated((float)(grove.X * 0.027));
        }
        double radius = sqrt(lerpf(inner * inner, outer * outer, _rng.Randf()));
        return new Vector2((float)cos(a), (float)sin(a)) * (float)radius;
    }

    public void _place_treeline()
    {
        long attempts = 0;
        long target = 156;
        long placed = 0;
        while (placed < target && attempts < target * 40)
        {
            attempts += 1;
            Vector2 pos = _woodland_position(TerrainField.TREELINE_INNER, NEAR_LIMIT);
            if (pos.Length() < TerrainField.TREELINE_INNER || pos.Length() > NEAR_LIMIT)
            {
                continue;
            }
            if (_rng.Randf() > _density_bias(pos))
            {
                continue;
            }
            double scale = _tree_scale(0.78, 1.23);
            if (_blocked(pos, NEAR_TREE_MIN_SPACING * scale * _rng.RandfRange(0.78f, 1.18f)))
            {
                continue;
            }
            _add_tree(pos, _species_for(pos), scale);
            placed += 1;
        }
    }

    public void _place_far_forest()
    {
        long attempts = 0;
        long target = 300;
        long placed = 0;
        while (placed < target && attempts < target * 30)
        {
            attempts += 1;
            Vector2 pos = _woodland_position(NEAR_LIMIT, FAR_LIMIT);
            if (pos.Length() < NEAR_LIMIT || pos.Length() > FAR_LIMIT)
            {
                continue;
            }
            if (_rng.Randf() > _density_bias(pos) * 0.95)
            {
                continue;
            }
            double scale = _tree_scale(0.78, 1.28);
            if (_blocked(pos, FAR_TREE_MIN_SPACING * scale * _rng.RandfRange(0.70f, 1.20f)))
            {
                continue;
            }
            _add_tree(pos, _species_for(pos), scale, true);
            placed += 1;
        }
    }

    public void _place_saplings()
    {
        long attempts = 0;
        long placed = 0;
        while (placed < 26 && attempts < 600)
        {
            attempts += 1;
            double angle = _rng.Randf() * TAU;
            double radius = _rng.RandfRange((float)(TerrainField.CLEARING_RADIUS - 2.0), (float)(TerrainField.TREELINE_INNER + 8.0));
            Vector2 pos = new Vector2((float)(cos(angle) * radius), (float)(sin(angle) * radius));
            if (pos.DistanceTo(new Vector2(7.8f, 28.3f)) < 3.5)
            {
                // Keep the opening dolly's low pass through saplings clear.
                continue;
            }
            if (_blocked(pos, 2.2))
            {
                continue;
            }
            _add_tree(pos, TreeSpecies.Kind.SAPLING, _rng.RandfRange(0.8f, 1.2f));
            placed += 1;
        }
        // Regeneration belongs to the woodland too, not only a ring around camp.
        // Taller juveniles sit among low saplings; a few openings remain legible.
        placed = 0;
        attempts = 0;
        while (placed < 112 && attempts < 2400)
        {
            attempts += 1;
            Vector2 pos2 = _woodland_position(38.0, 150.0);
            if (pos2.Length() < 38.0 || pos2.Length() > 150.0 || _blocked(pos2, 1.7))
            {
                continue;
            }
            if (_rng.Randf() > _density_bias(pos2) + sunset_opening(pos2) * 0.18)
            {
                continue;
            }
            bool juvenile = _rng.Randf() < 0.20 && sunset_opening(pos2) < 0.2;
            TreeSpecies.Kind kind = juvenile ? _species_for(pos2) : TreeSpecies.Kind.SAPLING;
            if (juvenile && (kind == TreeSpecies.Kind.SPRUCE || kind == TreeSpecies.Kind.PINE))
            {
                kind = TreeSpecies.Kind.OAK;
            }
            double scale = juvenile ? _rng.RandfRange(0.40f, 0.61f) : _rng.RandfRange(0.8f, 1.5f);
            _add_tree(pos2, kind, scale, pos2.Length() > NEAR_LIMIT);
            placed += 1;
        }
    }

    public void _place_pond_enclosure()
    {
        /// Staggered wet-bank alders, spreading oaks behind, and younger growth below
        /// close the former sunset corridor without forming a single row of crowns.
        /// This pass runs after the original layout and canopy paint: its private RNG
        /// preserves the camp, existing trees, rocks, ferns, flowers and shore reeds.
        /// The enclosed bank retains its established grassy ground layer.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)((long)_grove_noise.Seed + 67031));
        // X/Z, scale and one of the six existing growth habits. These anchors
        // overlap in depth as well as screen space; displaced roots still respect
        // existing trees and boulders instead of moving the authored frame edges.
        Godot.Collections.Array<Vector4> alders = new Godot.Collections.Array<Vector4> { new Vector4(-59, -5, 1.04f, 0), new Vector4(-61, -14, 1.14f, 4), new Vector4(-65, 1, 1.03f, 2), new Vector4(-68, -20, 1.16f, 1), new Vector4(-70, -7, 0.96f, 5), new Vector4(-75, 7, 1.03f, 0), new Vector4(-82, -3, 0.89f, 3), new Vector4(-86, -16, 1.03f, 4) };
        Godot.Collections.Array<Vector4> oaks = new Godot.Collections.Array<Vector4> { new Vector4(-68, -1, 1.12f, 0), new Vector4(-73, -16, 1.08f, 3), new Vector4(-76, -6, 1.28f, 1), new Vector4(-79, 11, 0.98f, 4), new Vector4(-81, -25, 1.10f, 2), new Vector4(-85, -11, 1.16f, 5), new Vector4(-89, 2, 1.23f, 3), new Vector4(-91, -23, 0.92f, 0), new Vector4(-98, -3, 1.10f, 4), new Vector4(-101, -16, 1.26f, 1), new Vector4(-107, 10, 1.08f, 2), new Vector4(-111, -30, 0.96f, 0) };
        foreach (Variant group in new Godot.Collections.Array { alders, oaks })
        {
            TreeSpecies.Kind kind = G.eq(group, alders) ? TreeSpecies.Kind.ALDER : TreeSpecies.Kind.OAK;
            foreach (Variant anchor_item in G.Iter(group))
            {
                Vector4 anchor = anchor_item.AsVector4();
                for (long attempt = 0; attempt < 18; attempt++)
                {
                    Vector2 pos = new Vector2(anchor.X, anchor.Y);
                    if (attempt > 0)
                    {
                        double angle = rng.Randf() * TAU;
                        double radius = sqrt(rng.Randf()) * lerpf(2.0, 5.5, (double)attempt / 17.0);
                        pos += new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
                    }
                    if (_enclosure_blocked(pos, 3.2 + anchor.Z * 1.2, 0.9))
                    {
                        continue;
                    }
                    _add_enclosure_tree(pos, kind, anchor.Z, (long)anchor.W, rng);
                    break;
                }
            }
        }
        // The lower canopy is uneven regeneration, including some taller young
        // alders among the small saplings, rather than an identical shrub hedge.
        Godot.Collections.Array<Vector3> patches = new Godot.Collections.Array<Vector3> { new Vector3(-61, -10, 7), new Vector3(-70, 4, 8), new Vector3(-74, -22, 8), new Vector3(-88, -8, 11) };
        for (long patch_index = 0, patch_index_end = (long)patches.Count; patch_index < patch_index_end; patch_index++)
        {
            Vector3 patch = patches[(int)patch_index];
            long placed = 0;
            for (long attempt2 = 0; attempt2 < 100; attempt2++)
            {
                if (placed >= 8)
                {
                    break;
                }
                double angle2 = rng.Randf() * TAU;
                double radius2 = sqrt(rng.Randf()) * patch.Z;
                Vector2 pos2 = new Vector2(patch.X, patch.Y) + new Vector2((float)cos(angle2), (float)(sin(angle2) * 0.74)) * (float)radius2;
                if (_enclosure_blocked(pos2, 1.55, 0.4))
                {
                    continue;
                }
                bool juvenile = placed % 4 == patch_index;
                TreeSpecies.Kind kind2 = juvenile ? TreeSpecies.Kind.ALDER : TreeSpecies.Kind.SAPLING;
                double scale = juvenile ? rng.RandfRange(0.43f, 0.64f) : rng.RandfRange(0.55f, 1.24f);
                _add_enclosure_tree(pos2, kind2, scale, (long)rng.Randi() % 6, rng);
                placed += 1;
            }
        }
    }

    public bool _enclosure_blocked(Vector2 pos, double spacing, double trunk_clearance)
    {
        if (pos.X > -56.0 || _blocked(pos, spacing))
        {
            return true;
        }
        foreach (ScenePlan.RockEntry rock in rocks)
        {
            if (pos.DistanceTo(rock.position) < rock.scale + trunk_clearance)
            {
                return true;
            }
        }
        return false;
    }

    public void _add_enclosure_tree(Vector2 pos, TreeSpecies.Kind kind, double scale, long variant, RandomNumberGenerator rng)
    {
        ScenePlan.TreeEntry entry = new ScenePlan.TreeEntry();
        entry.position = pos;
        entry.kind = kind;
        long seed_value = (long)rng.Randi();
        entry.seed_value = seed_value - posmod(seed_value, 6) + variant;
        entry.scale = scale;
        entry.rotation = rng.Randf() * TAU;
        entry.far = pos.Length() > NEAR_LIMIT;
        trees.Add(entry);
    }

    public void _add_rock(Vector2 pos, double scale, double sink)
    {
        // ------------------------------------------------------------------- rocks
        ScenePlan.RockEntry r = new ScenePlan.RockEntry();
        r.position = pos;
        r.scale = scale;
        r.rotation = _rng.Randf() * TAU;
        r.sink = sink;
        r.variant = (long)_rng.Randi() % 6;
        rocks.Add(r);
    }

    public void _place_rocks()
    {
        // Anchoring boulders: a cluster on the north bank and a few in the clearing.
        Godot.Collections.Array authored = new Godot.Collections.Array { new Godot.Collections.Array { new Vector2(-9.5f, -10.5f), 1.6 }, new Godot.Collections.Array { new Vector2(-7.8f, -12.6f), 1.0 }, new Godot.Collections.Array { new Vector2(-11.4f, -12.0f), 0.8 }, new Godot.Collections.Array { new Vector2(12.5f, 7.0f), 1.3 }, new Godot.Collections.Array { new Vector2(-15.0f, 16.5f), 1.1 }, new Godot.Collections.Array { new Vector2(22.0f, -3.5f), 1.9 }, new Godot.Collections.Array { new Vector2(-24.5f, -9.0f), 1.5 }, new Godot.Collections.Array { new Vector2(6.5f, 31.0f), 1.2 }, new Godot.Collections.Array { new Vector2(-8.0f, 25.0f), 0.9 }, new Godot.Collections.Array { new Vector2(-38.0f, 24.0f), 2.2 }, new Godot.Collections.Array { new Vector2(-42.0f, -14.0f), 1.7 }, new Godot.Collections.Array { new Vector2(-40.5f, -11.0f), 1.0 } };
        foreach (Variant entry in authored)
        {
            _add_rock(G.Index(entry, 0).AsVector2(), G.Index(entry, 1).AsDouble(), 0.38);
        }
        // Shore pebbles and stones.
        for (long i = 0; i < 60; i++)
        {
            double angle = _rng.Randf() * TAU;
            double radius = TerrainField.pond_radius_at(angle) + _rng.RandfRange(0.2f, 4.0f);
            Vector2 pos = TerrainField.POND_CENTRE + new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            if (_field.trail_distance(pos.X, pos.Y) < 1.5 || pos.DistanceTo(TerrainField.DOCK_START) < 3.0)
            {
                continue;
            }
            _add_rock(pos, _rng.RandfRange(0.14f, 0.42f), 0.45);
        }
        // Scattered stones through the wood.
        long placed = 0;
        long attempts = 0;
        while (placed < 55 && attempts < 1200)
        {
            attempts += 1;
            double angle2 = _rng.Randf() * TAU;
            double radius2 = _rng.RandfRange(12.0f, 85.0f);
            Vector2 pos2 = new Vector2((float)(cos(angle2) * radius2), (float)(sin(angle2) * radius2));
            if (_field.trail_distance(pos2.X, pos2.Y) < 2.5 || _field.is_underwater(pos2.X, pos2.Y))
            {
                continue;
            }
            if (_shore_offset(pos2) < 2.0)
            {
                continue;
            }
            if (pos2.DistanceTo(TerrainField.FIRE) < 7.0 || pos2.DistanceTo(TerrainField.TENT) < 5.0)
            {
                continue;
            }
            bool near_tree = false;
            foreach (ScenePlan.TreeEntry t in trees)
            {
                if (t.position.DistanceTo(pos2) < 1.6)
                {
                    near_tree = true;
                    break;
                }
            }
            if (near_tree)
            {
                continue;
            }
            _add_rock(pos2, _rng.RandfRange(0.25f, 1.3f), 0.42);
            placed += 1;
        }
    }

    public void _place_shrubs()
    {
        // ------------------------------------------------------------------ shrubs
        long placed = 0;
        long attempts = 0;
        while (placed < 70 && attempts < 2000)
        {
            attempts += 1;
            double angle = _rng.Randf() * TAU;
            double radius = _rng.RandfRange((float)(TerrainField.CLEARING_RADIUS - 4.0), 70.0f);
            Vector2 pos = new Vector2((float)(cos(angle) * radius), (float)(sin(angle) * radius));
            if (_blocked(pos, 1.4))
            {
                continue;
            }
            ScenePlan.ShrubEntry s = new ScenePlan.ShrubEntry();
            s.position = pos;
            s.scale = _rng.RandfRange(0.7f, 1.5f);
            s.rotation = _rng.Randf() * TAU;
            shrubs.Add(s);
            placed += 1;
        }
        // Low woody cover softens the exposed middle-distance trunks. This
        // shares the existing shrub mesh/material and adds no per-frame work.
        placed = 0;
        attempts = 0;
        while (placed < 110 && attempts < 1800)
        {
            attempts += 1;
            Vector2 pos2 = _woodland_position(38.0, 128.0);
            if (pos2.Length() < 38.0 || pos2.Length() > 128.0 || _blocked(pos2, 1.25))
            {
                continue;
            }
            if (_rng.Randf() > _density_bias(pos2))
            {
                continue;
            }
            ScenePlan.ShrubEntry shrub = new ScenePlan.ShrubEntry();
            shrub.position = pos2;
            shrub.scale = _rng.RandfRange(1.0f, 1.9f);
            shrub.rotation = _rng.Randf() * TAU;
            shrubs.Add(shrub);
            placed += 1;
        }
    }

    public void _paint_canopy()
    {
        // ------------------------------------------------------------------ canopy
        _field.canopy = new ScalarField(320, TerrainField.INNER_EXTENT, 0.0);
        foreach (ScenePlan.TreeEntry t in trees)
        {
            if (t.position.Length() > TerrainField.INNER_EXTENT * 0.5 + 10.0)
            {
                continue;
            }
            double spread = crown_footprint(t.kind) * t.scale;
            _field.canopy.paint_disc(t.position.X, t.position.Y, spread * 0.45, spread * 1.05, 1.0);
        }
    }

    public static double crown_footprint(TreeSpecies.Kind kind)
    {
        switch (kind)
        {
            case TreeSpecies.Kind.OAK:
                return 6.5;
            case TreeSpecies.Kind.ALDER:
                return 4.5;
            case TreeSpecies.Kind.SPRUCE:
                return 3.6;
            case TreeSpecies.Kind.PINE:
                return 5.0;
            case TreeSpecies.Kind.SNAG:
                return 1.5;
            case TreeSpecies.Kind.SAPLING:
                return 1.2;
            default:
                G.assert(false, G.format("Unhandled species %s", (long)kind));
                return 4.0;
        }
    }

    public void _place_dense_stands()
    {
        /// Add successive woodland age layers without moving existing authored trees.
        /// They enter the same plan as original trees: canopy paint, collision, camera
        /// clearance, culling and wildlife all see them. Never a visual-only overlay.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(226091));
        Godot.Collections.Dictionary cells = new Godot.Collections.Dictionary();
        double CELL = 5.0;
        foreach (ScenePlan.TreeEntry tree in trees)
        {
            Vector2I cell = new Vector2I((int)floori(tree.position.X / CELL), (int)floori(tree.position.Y / CELL));
            if (!cells.ContainsKey(cell))
            {
                cells[cell] = new Godot.Collections.Array();
            }
            G.Call(cells[cell], "append", tree.position);
        }
        // Mature near stand, overlapping middle distance, then juvenile regeneration.
        Godot.Collections.Array budgets = new Godot.Collections.Array { 150, 310, 165 };
        long added = 0;
        for (long layer = 0; layer < 3; layer++)
        {
            long placed = 0;
            foreach (Variant attempt_item in G.Iter(G.op("*", budgets[(int)layer], 65)))
            {
                Variant attempt = attempt_item;
                if (G.op(">=", placed, budgets[(int)layer]).AsBool())
                {
                    break;
                }
                double a = rng.Randf() * TAU;
                double inner = layer == 0 ? 50.0 : layer == 1 ? NEAR_LIMIT : 40.0;
                double outer = layer == 0 ? NEAR_LIMIT : layer == 1 ? 205.0 : 155.0;
                double r = sqrt(lerpf(inner * inner, outer * outer, rng.Randf()));
                Vector2 at = new Vector2((float)cos(a), (float)sin(a)) * (float)r;
                double patch = _grove_noise.GetNoise2D(at.X, at.Y);
                if (rng.Randf() > smoothstep(-0.48, 0.25, patch) * 0.9)
                {
                    continue;
                }
                if (_shore_offset(at) < 3.2 || _field.is_underwater(at.X, at.Y))
                {
                    continue;
                }
                if (_field.slope(at.X, at.Y) > 0.70 || _field.walking_distance(at) < 3.8)
                {
                    continue;
                }
                if (TerrainField.distance_to_polyline(at, _intro_lane) < 10.0)
                {
                    continue;
                }
                Vector2I cell2 = new Vector2I((int)floori(at.X / CELL), (int)floori(at.Y / CELL));
                double spacing = layer < 2 ? 3.45 : 1.95;
                bool clear = true;
                for (long dz = -1; dz < 2; dz++)
                {
                    for (long dx = -1; dx < 2; dx++)
                    {
                        foreach (Variant other_item in G.Iter(G.get(cells, cell2 + new Vector2I((int)dx, (int)dz), new Godot.Collections.Array())))
                        {
                            Vector2 other = other_item.AsVector2();
                            if (at.DistanceSquaredTo(other) < spacing * spacing)
                            {
                                clear = false;
                            }
                        }
                    }
                }
                if (!clear)
                {
                    continue;
                }
                foreach (ScenePlan.RockEntry rock in rocks)
                {
                    if (at.DistanceTo(rock.position) < rock.scale * 0.65 + 0.55)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                {
                    continue;
                }
                TreeSpecies.Kind kind = TreeSpecies.Kind.OAK;
                if (layer == 2)
                {
                    kind = TreeSpecies.Kind.SAPLING;
                }
                else if (_shore_offset(at) < 22.0)
                {
                    kind = TreeSpecies.Kind.ALDER;
                }
                else if ((double)at.X - at.Y > 35.0 && rng.Randf() < 0.45)
                {
                    kind = rng.Randf() < 0.60 ? TreeSpecies.Kind.SPRUCE : TreeSpecies.Kind.PINE;
                }
                else if (rng.Randf() < 0.35)
                {
                    kind = TreeSpecies.Kind.ALDER;
                }
                ScenePlan.TreeEntry tree2 = new ScenePlan.TreeEntry();
                tree2.position = at;
                tree2.kind = kind;
                tree2.scale = layer < 2 ? rng.RandfRange(0.76f, 1.18f) : rng.RandfRange(0.65f, 1.55f);
                tree2.rotation = rng.Randf() * TAU;
                tree2.seed_value = (long)rng.Randi();
                tree2.far = at.Length() > NEAR_LIMIT;
                trees.Add(tree2);
                if (!cells.ContainsKey(cell2))
                {
                    cells[cell2] = new Godot.Collections.Array();
                }
                G.Call(cells[cell2], "append", at);
                placed += 1;
                added += 1;
            }
        }
        G.print(G.format("SHOWCASE_FOREST added=%d total=%d", new Godot.Collections.Array { added, (long)trees.Count }));
    }
}
