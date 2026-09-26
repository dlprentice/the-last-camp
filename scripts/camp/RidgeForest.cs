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

/// A continuation of the camp's temperate woodland on the surrounding hills:
/// the same generated oak, alder, spruce and pine models the near forest
/// uses, planted to a closed canopy out to 655 m. There are no simplified
/// crowns or billboards out there; the forest thins leaf cards only once
/// they are smaller than a pixel and the bark carries mesh LODs.
public partial class RidgeForest : Node3D
{
    public const double CELL = 160.0;
    public const double INNER = 140.0;
    public const double OUTER = 655.0;
    public const long TARGET = 30000;
    /// The same species and generator as the near woodland, built at a lower
    /// detail level per distance band: fewer tube sides and the most important
    /// leaf cards grown to keep coverage. No billboards, no simplified crowns.
    public static readonly Godot.Collections.Array BANDS = new Godot.Collections.Array { new Godot.Collections.Dictionary { { (StringName)"limit", 300.0 }, { (StringName)"detail", 0.50 }, { (StringName)"seed", 12000 }, { (StringName)"spacing", 1.0 } }, new Godot.Collections.Dictionary { { (StringName)"limit", 700.0 }, { (StringName)"detail", 0.25 }, { (StringName)"seed", 13000 }, { (StringName)"spacing", 1.22 } } };
    public const long VARIANTS = 6;
    public long tree_count = 0;
    public Godot.Collections.Dictionary species_counts = new Godot.Collections.Dictionary();
    public TerrainField field;
    public Forest forest;
    public Godot.Collections.Dictionary _variants = new Godot.Collections.Dictionary();
    public double _detail_scale = 1.0;
    public bool _far_band = true;

    public RidgeForest(TerrainField p_field, Forest p_forest)
    {
        field = p_field;
        forest = p_forest;
        Name = "WoodedRidges";
    }

    public RidgeForest()
    {
    }

    public void build()
    {
        QualityPreset preset = Quality.Instance.current;
        _detail_scale = preset != null ? preset.ridge_detail : 1.0;
        _far_band = preset != null ? preset.ridge_far_band : true;
        switch (Game.Instance.arg_value("ridge", ""))
        {
            case "full":
                _detail_scale = 1.0;
                _far_band = true;
                break;
            case "light":
                _detail_scale = 0.5;
                _far_band = true;
                break;
            case "near":
                _detail_scale = 0.5;
                _far_band = false;
                break;
        }
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(90265));
        FastNoiseLite groves = new FastNoiseLite();
        groves.Seed = 1927;
        groves.Frequency = 0.016f;
        FastNoiseLite frontier_noise = new FastNoiseLite();
        frontier_noise.Seed = 3801;
        frontier_noise.Frequency = 0.008f;
        FastNoiseLite age_noise = new FastNoiseLite();
        age_noise.Seed = 824;
        age_noise.Frequency = 0.010f;
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary occupied = new Godot.Collections.Dictionary();
        double SPACE_CELL = 3.0;
        // Random disc sampling with local spacing avoids visible planting rows
        // on distant slopes and in elevated camera views.
        for (long attempt = 0, attempt_end = TARGET * 6; attempt < attempt_end; attempt++)
        {
            if (tree_count >= TARGET)
            {
                break;
            }
            double angle = rng.Randf() * TAU;
            double radius = sqrt(lerpf(INNER * INNER, OUTER * OUTER, rng.Randf()));
            Vector2 p = new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            double western = smoothstep(0.45, 0.90, -p.X / radius);
            // The sunset opening formerly exposed a circular wall of mature ridge
            // trees exactly at 180 m. Stagger that frontier into overlapping stands.
            double frontier = lerpf(180.0, 163.0 + frontier_noise.GetNoise2D(p.X, p.Y) * 48.0, western);
            double arrival = smoothstep(frontier, frontier + lerpf(0.1, 72.0, western), radius);
            double habitat = groves.GetNoise2D(p.X, p.Y);
            // Closed canopy almost everywhere: the habitat noise only opens the
            // odd glade, and the regenerating west stays thinner but never bare.
            double density = lerpf(lerpf(0.62, 1.0, smoothstep(-0.32, 0.30, habitat)), lerpf(0.18, 1.0, smoothstep(-0.28, 0.28, habitat)), western);
            // Keep low regeneration in the front of the sun gap, with mature
            // woodland beyond it. This is not an empty wedge to the horizon.
            double opening = ScenePlan.sunset_opening(p) * (1.0 - smoothstep(245.0, 325.0, radius));
            if (rng.Randf() > density * arrival * (1.0 - opening * 0.40))
            {
                continue;
            }
            double age = clampf(0.50 + age_noise.GetNoise2D(p.X, p.Y) * 1.30 + rng.RandfRange(-0.10f, 0.10f), 0.0, 1.0);
            age = lerpf(age, 0.10, opening * 0.95);
            bool young = western > 0.45 && age < 0.34;
            bool middle = western > 0.45 && age >= 0.34 && age < 0.64;
            double spacing = young ? 2.6 : middle ? 4.0 : 5.4;
            spacing = lerpf(5.0, spacing * rng.RandfRange(0.86f, 1.12f), western);
            // Crowns still touch in the far band; its trees are simply larger apart
            // than the eye can separate at that range.
            if (radius > G.to_float(G.Index(BANDS[0], "limit")))
            {
                spacing *= G.to_float(G.Index(BANDS[1], "spacing"));
            }
            Vector2I cell = new Vector2I((int)floori(p.X / SPACE_CELL), (int)floori(p.Y / SPACE_CELL));
            bool crowded = false;
            // Three cells cover the largest pair spacing, including mixed ages.
            for (long z = -3; z < 4; z++)
            {
                for (long x = -3; x < 4; x++)
                {
                    foreach (Variant other_item in G.Iter(G.get(occupied, cell + new Vector2I((int)x, (int)z), new Godot.Collections.Array())))
                    {
                        Vector3 other = other_item.AsVector3();
                        double gap = (spacing + other.Z) * 0.5;
                        if (p.DistanceSquaredTo(new Vector2(other.X, other.Y)) < gap * gap)
                        {
                            crowded = true;
                        }
                    }
                }
            }
            if (crowded)
            {
                continue;
            }
            if (!occupied.ContainsKey(cell))
            {
                occupied[cell] = new Godot.Collections.Array();
            }
            G.Call(occupied[cell], "append", new Vector3(p.X, p.Y, (float)spacing));
            double y = field.surface_height(p.X, p.Y);
            // The same mix as the woodland around the camp: a third conifers in
            // stands that follow the habitat noise, the rest oak-led broadleaf.
            // Alder carries the palest leaf atlas, so it stays a minority here.
            bool conifer = !young && rng.Randf() < lerpf(0.18, 0.48, smoothstep(-0.25, 0.30, habitat));
            TreeSpecies.Kind kind = default;
            double scale_value = 0;
            if (young)
            {
                kind = rng.Randf() < 0.55 ? TreeSpecies.Kind.ALDER : TreeSpecies.Kind.OAK;
                scale_value = rng.RandfRange(0.50f, 0.72f);
            }
            else if (middle)
            {
                kind = rng.Randf() < 0.62 ? TreeSpecies.Kind.OAK : TreeSpecies.Kind.ALDER;
                scale_value = rng.RandfRange(0.70f, 0.90f);
            }
            else if (conifer)
            {
                kind = rng.Randf() < 0.65 ? TreeSpecies.Kind.SPRUCE : TreeSpecies.Kind.PINE;
                scale_value = rng.RandfRange(0.80f, 1.10f);
            }
            else
            {
                kind = rng.Randf() < 0.7 ? TreeSpecies.Kind.OAK : TreeSpecies.Kind.ALDER;
                scale_value = rng.RandfRange(0.92f, 1.28f);
            }
            long variant = (long)rng.Randi() % VARIANTS;
            long band = 0;
            while (band < (long)BANDS.Count - 1 && radius > G.to_float(G.Index(BANDS[(int)band], "limit")))
            {
                band += 1;
            }
            if (band > 0 && !_far_band)
            {
                continue;
            }
            string key = G.format("%d_%d_%d_%d_%d", new Godot.Collections.Array { band, (long)kind, variant, floori(p.X / CELL), floori(p.Y / CELL) });
            if (!groups.ContainsKey(key))
            {
                groups[key] = new Godot.Collections.Dictionary { { (StringName)"band", band }, { (StringName)"kind", (long)kind }, { (StringName)"variant", variant }, { (StringName)"transforms", new Godot.Collections.Array<Transform3D>() } };
            }
            // Preserve the seeded draw order: scale before rotation.
            Vector3 treeScale = new Vector3((float)scale_value, (float)(scale_value * rng.RandfRange(0.92f, 1.12f)), (float)scale_value);
            Basis basis = new Basis(Vector3.Up, (float)(rng.Randf() * TAU)).Scaled(treeScale);
            G.Call(G.Index(groups[key], "transforms"), "append", new Transform3D(basis, new Vector3(p.X, (float)(y - 0.12 * scale_value), p.Y)));
            species_counts[(long)kind] = G.op("+", G.get(species_counts, (long)kind, 0), 1);
            tree_count += 1;
        }
        foreach (Variant key2 in groups.Keys)
        {
            Godot.Collections.Dictionary group = groups[key2].AsGodotDictionary();
            string variant_key = G.format("%d_%d_%d", new Godot.Collections.Array { group["band"], G.to_int(group["kind"]), group["variant"] });
            forest.add_ridge_group((TreeSpecies.Kind)group["kind"].AsInt64(), _variant(group["band"].AsInt64(), (TreeSpecies.Kind)group["kind"].AsInt64(), group["variant"].AsInt64()), variant_key, group["transforms"].AsGodotArray<Transform3D>());
        }
        List<string> summary = new List<string>();
        foreach (Variant kind2 in species_counts.Keys)
        {
            summary.Add(G.format("%s=%d", new Godot.Collections.Array { G.enum_keys<TreeSpecies.Kind>()[kind2.AsInt32()].ToLowerInvariant(), species_counts[kind2] }));
        }
        G.print(G.format("Wooded ridges: %d trees out to %.0f m (%s) in %d batches", new Godot.Collections.Array { tree_count, OUTER, string.Join(" ", summary), (long)groups.Count }));
    }

    public TreeGenerator.Result _variant(long band, TreeSpecies.Kind kind, long index)
    {
        string key = G.format("%d_%d_%d", new Godot.Collections.Array { band, (long)kind, index });
        if (!_variants.ContainsKey(key))
        {
            Godot.Collections.Dictionary spec = BANDS[(int)band].AsGodotDictionary();
            TreeGenerator gen = new TreeGenerator();
            _variants[key] = gen.generate(TreeSpecies.variant(kind, index), G.to_int(spec["seed"]) + (long)kind * 100 + index * 17, G.to_float(spec["detail"]) * _detail_scale);
        }
        return _variants[key].As<TreeGenerator.Result>();
    }
}
