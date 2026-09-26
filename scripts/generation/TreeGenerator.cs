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

/// Recursive parametric tree generator.
///
/// Produces a bark mesh (tapered, parallel-transported tubes with cylindrical
/// UVs and vertex AO at branch junctions) and a leaf mesh (crossed alpha cards
/// carrying their pivot, a wind phase, crown-depth AO and a LOD importance in
/// vertex attributes). Deterministic for a given species and seed.
public partial class TreeGenerator
{
    public partial class Result : RefCounted
    {
        public ArrayMesh bark;
        public ArrayMesh leaves;
        public double height = 0.0;
        public double trunk_radius = 0.0;
        public Vector3 crown_center = Vector3.Zero;
        public double crown_radius = 1.0;
        public long leaf_card_count = 0;
        public long branch_count = 0;
    }

    public partial class LeafPlacement
    {
        public Vector3 position;
        public Vector3 direction;
        public double size;
        public double importance;
    }

    public const double GOLDEN_ANGLE = 2.399963;
    public const double BARK_TILE = 1.2;
    public const double MIN_BRANCH_RADIUS = 0.008;
    public const double MIN_BRANCH_LENGTH = 0.22;

    public TreeSpecies species;
    public RandomNumberGenerator rng = new RandomNumberGenerator();
    /// 1.0 is the full tree. Lower values keep the same skeleton and crown but
    /// build the tubes with fewer sides and segments and emit only the most
    /// important leaf cards, grown to keep the canopy's coverage: the distant
    /// woodland uses the same species at a fraction of the triangles.
    public double detail = 1.0;
    public MeshBuilder bark = new MeshBuilder();
    public MeshBuilder leaves = new MeshBuilder();
    public double height = 0.0;
    public List<TreeGenerator.LeafPlacement> _placements = new List<TreeGenerator.LeafPlacement>();
    public long _branch_count = 0;
    public Godot.Collections.Array<Godot.Collections.Dictionary> _oak_terminal_limbs = new Godot.Collections.Array<Godot.Collections.Dictionary>();

    public TreeGenerator.Result generate(TreeSpecies p_species, long seed_value, double p_detail = 1.0)
    {
        species = p_species;
        detail = clampf(p_detail, 0.15, 1.0);
        rng.Seed = unchecked((ulong)(seed_value));
        bark = new MeshBuilder();
        leaves = new MeshBuilder();
        leaves.use_custom0 = true;
        _placements.Clear();
        _branch_count = 0;
        _oak_terminal_limbs.Clear();

        height = rng.RandfRange(species.height.X, species.height.Y);
        TreeGenerator.Result result = new TreeGenerator.Result();
        result.height = height;
        result.trunk_radius = rng.RandfRange(species.trunk_radius.X, species.trunk_radius.Y);
        _grow_trunk(result.trunk_radius);
        _refine_oak_terminal_limbs(seed_value);
        _emit_leaves(result);
        result.bark = bark.commit(null, true);
        result.leaves = !leaves.is_empty() ? leaves.commit(null, false) : null;
        result.leaf_card_count = leaves.triangle_count() / 2;
        result.branch_count = _branch_count;
        return result;
    }

    public void _grow_trunk(double base_radius)
    {
        // --------------------------------------------------------------------- trunk
        double split_at = species.split_height > 0.0 ? clampf(species.split_height + rng.RandfRange(-0.025f, 0.025f), species.branch_start + 0.06, 0.75) : 0.0;
        double trunk_len = height * (split_at > 0.0 ? split_at : 1.0);
        Vector3 lean = new Vector3(rng.RandfRange(-1.0f, 1.0f), 0.0f, rng.RandfRange(-1.0f, 1.0f)).Normalized() * (float)species.trunk_lean;
        Vector3 dir = (Vector3.Up + lean).Normalized();
        Godot.Collections.Array<Vector3> points = _polyline(Vector3.Zero, dir, trunk_len, species.trunk_segments, species.trunk_wobble, 0.15);
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        long n = (long)points.Count;
        for (long i = 0; i < n; i++)
        {
            double t = (double)i / (double)(n - 1);
            double r = base_radius * pow(1.0 - species.trunk_taper * t * (split_at > 0.0 ? split_at : 1.0), 0.95);
            // Basal flare: most of the widening happens in the bottom half metre,
            // so the trunk meets the ground as a spreading base rather than a pipe.
            if (t < 0.1)
            {
                double flare = 1.0 - t / 0.1;
                r *= 1.0 + (species.root_flare - 1.0) * flare * flare * flare;
            }
            radii.Add(maxf(r, 0.02));
        }
        _add_bark_tube(points, radii, 0, 1.0);
        _branch_count += 1;
        // Side branches along the trunk.

        double zone_start = species.branch_start * height;
        double zone_end = minf(species.branch_end * height, trunk_len);
        if (zone_end > zone_start)
        {
            long count = (long)round((zone_end - zone_start) * species.branch_density);
            double azimuth = rng.Randf() * TAU;
            for (long i2 = 0; i2 < count; i2++)
            {
                double h = zone_start + ((double)i2 + rng.RandfRange(0.2f, 0.8f)) / (double)count * (zone_end - zone_start);
                double t2 = h / trunk_len;
                Vector3 origin = _point_along(points, t2);
                Vector3 tangent = _tangent_along(points, t2);
                azimuth += GOLDEN_ANGLE + rng.RandfRange(-0.3f, 0.3f);
                double angle = rng.RandfRange(species.branch_angle.X, species.branch_angle.Y);
                Vector3 child_dir = _rotate_away(tangent, azimuth, angle);
                double rel = h / height;
                double length_scale = _branch_length_scale(rel);
                double length = rng.RandfRange(species.branch_length.X, species.branch_length.Y) * height * length_scale;
                double parent_r = _radius_along(radii, t2);
                double r2 = parent_r * species.branch_radius_ratio * (0.7 + 0.6 * length_scale);
                _grow_branch(origin, child_dir, length, r2, 1, species.gravitropism);
            }
        }
        // Leaders where the trunk splits into a crown. Each leader is rooted a
        // little way down inside the trunk, so it emerges through the trunk's
        // capped top instead of hanging off its rim, and the fork reads as one
        // piece of wood.

        if (split_at > 0.0)
        {
            long leaders = rng.RandiRange(species.split_count.X, species.split_count.Y);
            double az = rng.Randf() * TAU;
            for (long i3 = 0; i3 < leaders; i3++)
            {
                // One dominant continuation covers the trunk cap. Other scaffold
                // limbs emerge lower and at unequal angles, avoiding a candelabra.
                double attachment = i3 == 0 ? 1.0 : rng.RandfRange(0.72f, 0.92f);
                Vector3 top = _point_along(points, attachment);
                Vector3 top_dir = _tangent_along(points, attachment);
                double r_top = _radius_along(radii, attachment);
                az += GOLDEN_ANGLE + rng.RandfRange(-0.38f, 0.38f);
                double angle_scale = i3 == 0 ? rng.RandfRange(0.40f, 0.70f) : rng.RandfRange(0.8f, 1.15f);
                Vector3 d = _rotate_away(top_dir, az, species.split_angle * angle_scale);
                double length2 = height * (1.0 - split_at) * rng.RandfRange(0.80f, 1.08f);
                double r3 = r_top * (i3 == 0 ? 0.72 : rng.RandfRange(0.50f, 0.66f));
                Vector3 root = top - top_dir * (float)(r_top * 1.4) + _rotate_away(top_dir, az, PI * 0.5) * (float)(r_top * 0.20);
                // Leaders leave along the trunk and curve outwards.
                _grow_branch(root, d, length2 + r_top * 1.4, r3, 1, species.gravitropism, true, top_dir);
            }
        }
    }

    public double _branch_length_scale(double rel_height)
    {
        /// Branches shorten towards the top of a cone (conifers) or keep a rounded
        /// crown (broadleaf) so the silhouette reads as the species.
        if (species.conical)
        {
            double t = (rel_height - species.branch_start) / maxf(species.branch_end - species.branch_start, 1e-3);
            return lerpf(1.0, 0.12, pow(clampf(t, 0.0, 1.0), 0.85));
        }
        double t2 = (rel_height - species.branch_start) / maxf(species.branch_end - species.branch_start, 1e-3);
        return 0.55 + 0.45 * sin(clampf(t2, 0.0, 1.0) * PI);
    }

    public void _grow_branch(Vector3 origin, Vector3 dir, double length, double radius, long level, double gravitropism, bool is_leader = false, Vector3? from_dir_opt = null)
    {
        Vector3 from_dir = from_dir_opt ?? Vector3.Zero;
        // ------------------------------------------------------------------ branches
        if (radius < MIN_BRANCH_RADIUS || length < MIN_BRANCH_LENGTH || level > species.levels)
        {
            return;
        }
        _branch_count += 1;
        long segments = maxi(species.branch_segments - (level - 1), 2);
        if (is_leader)
        {
            // Thick limbs bend out of the trunk; enough rings keep the bend a curve.
            segments += 5;
        }
        double wobble = species.branch_wobble * (1.0 + 0.35 * (double)level);
        Godot.Collections.Array<Vector3> points = _polyline(origin, dir, length, segments, wobble, gravitropism, from_dir);
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        long n = (long)points.Count;
        double tip_ratio = is_leader ? 0.35 : 0.2;
        for (long i = 0; i < n; i++)
        {
            double t = (double)i / (double)(n - 1);
            radii.Add(maxf(radius * lerpf(1.0, tip_ratio, t), 0.004));
        }
        double junction_ao = !is_leader ? 0.55 : 0.8;
        _add_bark_tube(points, radii, level, junction_ao);
        // Children.

        if (level < species.levels)
        {
            long count = rng.RandiRange(species.child_count.X, species.child_count.Y);
            if (is_leader)
            {
                count += 2;
            }
            double az = rng.Randf() * TAU;
            for (long i2 = 0; i2 < count; i2++)
            {
                double t2 = lerpf(0.3, 0.95, ((double)i2 + rng.RandfRange(0.1f, 0.9f)) / (double)count);
                Vector3 p = _point_along(points, t2);
                Vector3 tangent = _tangent_along(points, t2);
                az += GOLDEN_ANGLE + rng.RandfRange(-0.4f, 0.4f);
                double angle = rng.RandfRange(species.child_angle.X, species.child_angle.Y);
                Vector3 child_dir = _rotate_away(tangent, az, angle);
                // Keep children from diving straight down.
                if (child_dir.Y < -0.35)
                {
                    child_dir = (child_dir + Vector3.Up * 0.5f).Normalized();
                }
                double child_len = length * rng.RandfRange(species.child_length_ratio.X, species.child_length_ratio.Y);
                double child_r = _radius_along(radii, t2) * species.child_radius_ratio;
                bool broadleaf = species.kind == TreeSpecies.Kind.OAK || species.kind == TreeSpecies.Kind.ALDER || species.kind == TreeSpecies.Kind.SAPLING;
                _grow_branch(p, child_dir, child_len, child_r, level + 1, gravitropism * (broadleaf ? 0.88 : 1.15));
            }
        }
        // Leaves.

        if (species.has_leaves() && level >= species.leaf_level_min)
        {
            Vector3 tip_dir = _tangent_along(points, 1.0);
            double base_size = rng.RandfRange(species.leaf_card_size.X, species.leaf_card_size.Y);
            for (long i3 = 0, i_end = species.leaf_cards_per_tip; i3 < i_end; i3++)
            {
                Vector3 jitter = new Vector3(rng.RandfRange(-0.15f, 0.15f), rng.RandfRange(-0.1f, 0.15f), rng.RandfRange(-0.15f, 0.15f)) * (float)base_size;
                Vector3 tip = _point_along(points, rng.RandfRange(0.83f, 1.0f));
                _place_leaf(tip + jitter, _jitter_dir(tip_dir, 0.35), base_size * rng.RandfRange(0.9f, 1.15f), rng.RandfRange(0.72f, 1.0f));
            }
            long along_first = (long)_placements.Count;
            for (long i4 = 0, i_end2 = species.leaves_along_branch; i4 < i_end2; i4++)
            {
                double t3 = lerpf(0.4, 0.92, ((double)i4 + rng.Randf()) / (double)species.leaves_along_branch);
                Vector3 p2 = _point_along(points, t3);
                Vector3 d = _jitter_dir(_tangent_along(points, t3), 0.7);
                _place_leaf(p2, d, base_size * rng.RandfRange(0.80f, 1.05f), rng.RandfRange(0.35f, 0.95f));
            }
            if (species.kind == TreeSpecies.Kind.OAK && level == 3 && length >= 0.9)
            {
                _oak_terminal_limbs.Add(new Godot.Collections.Dictionary { { (StringName)"points", points }, { (StringName)"radii", radii }, { (StringName)"first", along_first }, { (StringName)"count", species.leaves_along_branch }, { (StringName)"length", length } });
            }
        }
    }

    public void _refine_oak_terminal_limbs(long seed_value)
    {
        /// Fine oak shoots widen the leaf-bearing lobes inside the established crown.
        /// Run after the complete scaffold with a separate RNG: adding a shoot must not
        /// change later branches, existing tip sprays, or their atlas/wind random values.
        if ((_oak_terminal_limbs.Count == 0))
        {
            return;
        }
        RandomNumberGenerator detail_rng = new RandomNumberGenerator();
        detail_rng.Seed = unchecked((ulong)(seed_value ^ 0x5EED0A17));
        Vector3 center = Vector3.Zero;
        foreach (TreeGenerator.LeafPlacement lp in _placements)
        {
            center += lp.position;
        }
        center /= (float)(double)(long)_placements.Count;
        double crown_radius = 0.0;
        Aabb crown_bounds = _leaf_bounds(_placements[0], center);
        foreach (TreeGenerator.LeafPlacement lp2 in _placements)
        {
            crown_radius = maxf(crown_radius, lp2.position.DistanceTo(center));
            crown_bounds = crown_bounds.Merge(_leaf_bounds(lp2, center));
        }
        crown_bounds = crown_bounds.Grow(-0.03f);
        List<TreeGenerator.LeafPlacement> extra_leaves = new List<TreeGenerator.LeafPlacement>();
        foreach (Godot.Collections.Dictionary limb in _oak_terminal_limbs)
        {
            if (G.op("<", limb["count"], 3).AsBool() || detail_rng.Randf() > 0.78)
            {
                continue;
            }
            Godot.Collections.Array<Vector3> points = limb["points"].AsGodotArray<Vector3>();
            Godot.Collections.Array<double> radii = limb["radii"].AsGodotArray<double>();
            long shoot_count = mini(detail_rng.RandiRange(2, 3), G.to_int(limb["count"]) - 1);
            double azimuth = detail_rng.Randf() * TAU;
            for (long shoot = 0; shoot < shoot_count; shoot++)
            {
                // Reuse outer along-branch sprays; terminal tip cards remain intact.
                long slot = 1 + roundi((double)shoot / (double)(shoot_count - 1) * G.to_float(G.op("-", limb["count"], 2)));
                TreeGenerator.LeafPlacement lp3 = _placements[(int)(G.to_int(limb["first"]) + slot)];
                Vector3 root = lp3.position;
                if (root.Y < height * 0.30 || root.DistanceTo(center) > crown_radius * 0.86)
                {
                    continue;
                }
                double t = lerpf(0.4, 0.92, ((double)slot + 0.5) / G.to_float(limb["count"]));
                Vector3 tangent = _tangent_along(points, t);
                azimuth += GOLDEN_ANGLE + detail_rng.RandfRange(-0.25f, 0.25f);
                Vector3 lateral = _rotate_away(tangent, azimuth, detail_rng.RandfRange(0.95f, 1.38f));
                Vector3 inward = (center - root).Normalized();
                Vector3 direction = (lateral + inward * 0.32f + Vector3.Up * 0.12f).Normalized();
                double shoot_length = clampf(G.to_float(limb["length"]) * detail_rng.RandfRange(0.14f, 0.22f), 0.3, 0.7);
                Vector3 tip = root + direction * (float)shoot_length;
                // Fill spaces between the limbs without growing a new outer envelope.
                if ((tip + direction * (float)lp3.size).DistanceTo(center) > crown_radius * 0.97)
                {
                    continue;
                }
                Vector3 old_direction = lp3.direction;
                lp3.position = root.Lerp(tip, 0.72f);
                lp3.direction = direction;
                if (!crown_bounds.Encloses(_leaf_bounds(lp3, center)))
                {
                    lp3.position = root;
                    lp3.direction = old_direction;
                    continue;
                }
                double radius = clampf(_radius_along(radii, t) * 0.32, 0.006, 0.018);
                bark.add_tube(new Godot.Collections.Array<Vector3> { root, tip }, new Godot.Collections.Array<double> { radius, 0.003 }, 4, new Color(0.9f, 1.0f, 0.0f, 1.0f), 1.0, 1.0 / BARK_TILE, detail_rng.Randf(), true);
                _branch_count += 1;
                // A few smaller inner sprays add lobe depth; preserve the old array
                // ordering so untouched cards retain their atlas, hue and wind phase.
                if (detail_rng.Randf() < 0.62)
                {
                    TreeGenerator.LeafPlacement inner = new TreeGenerator.LeafPlacement();
                    inner.position = root.Lerp(tip, 0.38f);
                    inner.direction = (tangent * 0.5f + direction * 0.3f + Vector3.Up * 0.2f).Normalized();
                    inner.size = lp3.size * detail_rng.RandfRange(0.76f, 0.9f);
                    inner.importance = detail_rng.RandfRange(0.50f, 0.78f);
                    if (crown_bounds.Encloses(_leaf_bounds(inner, center)))
                    {
                        extra_leaves.Add(inner);
                    }
                }
            }
        }
        _placements.AddRange(extra_leaves);
    }

    public Aabb _leaf_bounds(TreeGenerator.LeafPlacement lp, Vector3 center)
    {
        /// Bounds of the same two crossed cards emitted below, without consuming RNG.
        Vector3 up = (lp.direction - Vector3.Up * (float)species.leaf_droop * 0.6f).Normalized();
        Vector3 outward = (lp.position - center) * new Vector3(1.0f, 0.5f, 1.0f);
        if (outward.LengthSquared() < 1e-4)
        {
            return new Aabb(lp.position - Vector3.One * (float)lp.size, Vector3.One * (float)lp.size * 2.0f);
        }
        Vector3 right = up.Cross(outward.Normalized());
        if (right.LengthSquared() < 1e-4)
        {
            right = up.Cross(Vector3.Right);
        }
        right = right.Normalized();
        Vector3 right2 = right.Rotated(up, (float)(PI * 0.5));
        Vector3 spread = right.Abs().Max(right2.Abs()) * (float)lp.size * 0.425f;
        Vector3 end = lp.position + up * (float)lp.size;
        Vector3 lower = lp.position.Min(end) - spread;
        return new Aabb(lower, lp.position.Max(end) + spread - lower);
    }

    public void _place_leaf(Vector3 position, Vector3 direction, double size, double importance)
    {
        TreeGenerator.LeafPlacement lp = new TreeGenerator.LeafPlacement();
        lp.position = position;
        lp.direction = direction;
        lp.size = size;
        lp.importance = importance;
        _placements.Add(lp);
    }

    public void _emit_leaves(TreeGenerator.Result result)
    {
        // ------------------------------------------------------------- leaf emission
        if ((_placements.Count == 0))
        {
            return;
        }
        Vector3 center = Vector3.Zero;
        foreach (TreeGenerator.LeafPlacement lp in _placements)
        {
            center += lp.position;
        }
        center /= (float)(double)(long)_placements.Count;
        double radius = 0.0;
        foreach (TreeGenerator.LeafPlacement lp2 in _placements)
        {
            radius = maxf(radius, lp2.position.DistanceTo(center));
        }
        result.crown_center = center;
        result.crown_radius = maxf(radius, 0.5);

        List<TreeGenerator.LeafPlacement> placements = _placements;
        if (detail < 1.0)
        {
            // Keep the most important cards (tips first) and grow the survivors so
            // the crown covers the same area with fewer, larger cards.
            // A stratified pick through the importance order keeps inner, shaded
            // cards as well as the tips, so the thinned crown still has a dark
            // interior between its lit outer sprays instead of becoming a bright
            // shell of oversized cards.
            List<TreeGenerator.LeafPlacement> sorted = new List<TreeGenerator.LeafPlacement>(_placements);
            G.sort_custom(sorted, (TreeGenerator.LeafPlacement a, TreeGenerator.LeafPlacement b) => a.importance > b.importance);
            double keep = clampf(detail, 0.2, 1.0);
            long count = clampi(roundi((double)(long)sorted.Count * keep), mini(8, (long)sorted.Count), (long)sorted.Count);
            placements = new List<TreeGenerator.LeafPlacement>();
            double step = (double)(long)sorted.Count / (double)count;
            for (long k = 0; k < count; k++)
            {
                placements.Add(sorted[(int)mini((long)floor((double)k * step), (long)sorted.Count - 1)]);
            }
            double grow = pow(1.0 / keep, 0.35);
            foreach (TreeGenerator.LeafPlacement lp3 in placements)
            {
                lp3.size *= grow;
            }
        }
        foreach (TreeGenerator.LeafPlacement lp4 in placements)
        {
            double depth = lp4.position.DistanceTo(center) / result.crown_radius;
            double ao = lerpf(0.5, 1.0, smoothstep(0.1, 0.95, depth));
            double phase = rng.Randf();
            double hue = rng.Randf();
            Vector2I cell = new Vector2I((int)((long)rng.Randi() % 2), (int)((long)rng.Randi() % 2));
            Rect2 uv_rect = new Rect2((float)((double)cell.X * 0.5), (float)((double)cell.Y * 0.5), 0.5f, 0.5f);
            Vector3 outward = lp4.position - center;
            outward.Y = (float)(outward.Y * 0.5);
            if (outward.LengthSquared() < 1e-4)
            {
                outward = new Vector3(rng.RandfRange(-1, 1), 0.2f, rng.RandfRange(-1, 1));
            }
            outward = outward.Normalized();
            // Cards grow along the twig, drooping under their own weight.
            Vector3 up = (lp4.direction - Vector3.Up * (float)species.leaf_droop * 0.6f).Normalized();
            Vector3 right = up.Cross(outward);
            if (right.LengthSquared() < 1e-4)
            {
                right = up.Cross(Vector3.Right);
            }
            right = right.Normalized();
            Color color = new Color((float)ao, (float)hue, (float)lp4.importance, 1.0f);
            Color custom = new Color(lp4.position.X, lp4.position.Y, lp4.position.Z, (float)phase);
            double w = lp4.size * 0.85;
            double h = lp4.size;
            _emit_card(lp4.position, right, up, w, h, color, custom, uv_rect);
            // Second card crossed at 90 degrees so the cluster has volume.
            Vector3 right2 = right.Rotated(up, (float)(PI * 0.5));
            Vector2I cell2 = new Vector2I((int)((long)rng.Randi() % 2), (int)((long)rng.Randi() % 2));
            Rect2 uv_rect2 = new Rect2((float)((double)cell2.X * 0.5), (float)((double)cell2.Y * 0.5), 0.5f, 0.5f);
            _emit_card(lp4.position, right2, up, w, h, color, custom, uv_rect2);
        }
    }

    public void _emit_card(Vector3 pivot, Vector3 right, Vector3 up, double w, double h, Color color, Color custom, Rect2 uv_rect)
    {
        Vector3 p0 = pivot - right * (float)(w * 0.5);
        Vector3 p1 = pivot + right * (float)(w * 0.5);
        Vector3 p2 = p1 + up * (float)h;
        Vector3 p3 = p0 + up * (float)h;
        leaves.add_quad(p0, p1, p2, p3, color, custom, uv_rect);
    }

    public void _add_bark_tube(Godot.Collections.Array<Vector3> points, Godot.Collections.Array<double> radii, long level, double junction_ao)
    {
        // ------------------------------------------------------------------- helpers
        /// Vertex colour: r = junction AO (dark where a branch leaves its parent),
        /// g = branch level / 4 (wind stiffness), b = unused.
        double r0 = radii[0];
        long sides = r0 > 0.18 ? 14 : r0 > 0.08 ? 10 : r0 > 0.035 ? 6 : 4;
        sides = maxi(r0 <= 0.035 ? 3 : 4, roundi((double)sides * lerpf(0.4, 1.0, detail)));
        double u_repeats = maxf(1.0, round(TAU * r0 / BARK_TILE));
        double level_code = (double)level / 4.0;
        Callable ring_color = Callable.From((long i) =>
{
    double ao = lerpf(junction_ao, 1.0, clampf((double)i / 1.5, 0.0, 1.0));
    return new Color((float)ao, (float)level_code, 0.0f, 1.0f);
});
        bark.add_tube(points, radii, sides, new Color(1.0f, (float)level_code, 0.0f, 1.0f), u_repeats, 1.0 / BARK_TILE, rng.Randf(), true, ring_color);
    }

    public Godot.Collections.Array<Vector3> _polyline(Vector3 origin, Vector3 dir, double length, long segments, double wobble, double gravitropism, Vector3? from_dir_opt = null)
    {
        Vector3 from_dir = from_dir_opt ?? Vector3.Zero;
        /// from_dir, when given, is the parent's direction: the limb leaves along it
        /// and bends towards dir over its first half, so a fork is a curve of wood
        /// rather than two straight cylinders meeting at an angle.
        Godot.Collections.Array<Vector3> pts = new Godot.Collections.Array<Vector3> { origin };
        Vector3 target = dir.Normalized();
        Vector3 d = from_dir == Vector3.Zero ? target : from_dir.Normalized();
        segments = maxi(2, roundi((double)segments * lerpf(0.5, 1.0, detail)));
        double seg = length / (double)segments;
        for (long i = 0; i < segments; i++)
        {
            double t = (double)(i + 1) / (double)segments;
            if (from_dir != Vector3.Zero)
            {
                double bend = smoothstep(0.0, 0.55, t);
                d = d.Slerp(target, (float)bend).Normalized();
            }
            double lift = gravitropism * 0.42 * (0.6 + 0.8 * t);
            // Conifer branches droop then curl up at the very tip.
            if (gravitropism < 0.0 && i == segments - 1)
            {
                lift = 0.35;
            }
            d = (d + Vector3.Up * (float)lift + _random_unit() * (float)wobble).Normalized();
            pts.Add(pts[(int)((long)pts.Count - 1)] + d * (float)seg);
        }
        return pts;
    }

    public Vector3 _random_unit()
    {
        Vector3 v = new Vector3(rng.RandfRange(-1.0f, 1.0f), rng.RandfRange(-1.0f, 1.0f), rng.RandfRange(-1.0f, 1.0f));
        return v.LengthSquared() > 1e-6 ? v.Normalized() : Vector3.Up;
    }

    public Vector3 _jitter_dir(Vector3 dir, double amount)
    {
        return (dir + _random_unit() * (float)amount).Normalized();
    }

    public static Vector3 _rotate_away(Vector3 tangent, double azimuth, double angle)
    {
        /// Rotates `tangent` away from itself by `angle` towards a perpendicular
        /// direction chosen by `azimuth`.
        Vector3 side = tangent.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-6)
        {
            side = tangent.Cross(Vector3.Right);
        }
        side = side.Normalized();
        Vector3 up = tangent.Cross(side).Normalized();
        Vector3 perp = side * (float)cos(azimuth) + up * (float)sin(azimuth);
        return (tangent * (float)cos(angle) + perp * (float)sin(angle)).Normalized();
    }

    public static Vector3 _point_along(Godot.Collections.Array<Vector3> points, double t)
    {
        double scaled = clampf(t, 0.0, 1.0) * (double)((long)points.Count - 1);
        long i = clampi((long)floor(scaled), 0, (long)points.Count - 2);
        return points[(int)i].Lerp(points[(int)(i + 1)], (float)(scaled - (double)i));
    }

    public static Vector3 _tangent_along(Godot.Collections.Array<Vector3> points, double t)
    {
        double scaled = clampf(t, 0.0, 1.0) * (double)((long)points.Count - 1);
        long i = clampi((long)floor(scaled), 0, (long)points.Count - 2);
        return (points[(int)(i + 1)] - points[(int)i]).Normalized();
    }

    public static double _radius_along(Godot.Collections.Array<double> radii, double t)
    {
        double scaled = clampf(t, 0.0, 1.0) * (double)((long)radii.Count - 1);
        long i = clampi((long)floor(scaled), 0, (long)radii.Count - 2);
        return lerpf(radii[(int)i], radii[(int)(i + 1)], scaled - (double)i);
    }
}
