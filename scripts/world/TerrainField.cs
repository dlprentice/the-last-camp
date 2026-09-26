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

/// The authored landscape as pure functions of (x, z).
///
/// Everything spatial in the scene (terrain mesh, collision, water, scatter,
/// player grounding) queries this class, so it is deliberately deterministic and
/// side-effect free. Coordinates: +X east, +Z south, -Z north. The camp fire is
/// the origin; the pond lies to the west so the evening sun sets across it.
///
/// `height()` is the authored landscape; `height_fast()` reads the fine grid.
/// `surface_height()` follows the actual rendered triangles for distant plants,
/// where sampling the analytic hills can put their roots above or below the mesh.
public partial class TerrainField
{
    public const double INNER_EXTENT = 260.0;
    public const double OUTER_EXTENT = 1400.0;

    public static readonly Vector2 FIRE = new Vector2(0.0f, 0.0f);
    public static readonly Vector2 TENT = new Vector2(7.5f, -4.5f);
    public static readonly Vector2 POND_CENTRE = new Vector2(-34.0f, 4.0f);
    /// Nominal radius; the real shoreline wanders around it (see pond_radius_at).
    public const double POND_RADIUS = 17.0;
    public const double POND_MAX_RADIUS = POND_RADIUS * 1.32;
    public const double WATER_LEVEL = -0.9;
    public static readonly Vector2 DOCK_START = new Vector2(-15.9f, 6.3f);
    public static readonly Vector2 TABLE = new Vector2(9.2f, 1.2f);
    public static readonly Vector2 WOODPILE = new Vector2(4.6f, -2.2f);
    public const double WOODPILE_YAW = -0.35;
    /// Tangential seats leave the southwest pond trail and east camp tracks open.
    public static readonly Godot.Collections.Array<Vector2> SEATS = new Godot.Collections.Array<Vector2> { new Vector2(2.1f, 2.4f), new Vector2(-2.3f, -1.8f), new Vector2(1.35f, -2.6f) };
    public const double CLEARING_RADIUS = 26.0;
    public const double TREELINE_INNER = 28.0;
    public const double TREELINE_OUTER = 78.0;

    /// Trail from the southern arrival point, past the fire, out to the dock.
    public static readonly Godot.Collections.Array<Vector2> TRAIL = new Godot.Collections.Array<Vector2> { new Vector2(4.0f, 70.0f), new Vector2(3.5f, 50.0f), new Vector2(2.0f, 34.0f), new Vector2(1.5f, 20.0f), new Vector2(1.0f, 9.0f), new Vector2(-1.5f, 2.5f), new Vector2(-6.0f, -2.5f), new Vector2(-12.0f, -0.5f), new Vector2(-14.5f, 3.0f), DOCK_START };
    public static readonly Godot.Collections.Array<Vector2> CAMP_TRACK = new Godot.Collections.Array<Vector2> { new Vector2(1.8f, 0.9f), new Vector2(3.3f, -1.6f), new Vector2(5.0f, -2.9f), new Vector2(6.3f, -3.8f), TENT };
    public static readonly Godot.Collections.Array<Vector2> TABLE_TRACK = new Godot.Collections.Array<Vector2> { new Vector2(3.7f, 0.5f), new Vector2(6.4f, 1.3f), TABLE };

    /// Anything this far from the trail's bounding box is untouched by it.
    public static readonly Rect2 TRAIL_BOUNDS = new Rect2(-18.0f, -5.0f, 25.0f, 78.0f);
    public const double TRAIL_INFLUENCE = 4.0;

    /// Ground level the meadow sits on; dips are compressed so dry land never
    /// drops to the water level anywhere but in the pond basin.
    public const double MEADOW_BASE = 0.45;
    public const double DIP_COMPRESSION = 0.25;

    public FastNoiseLite _rolling = new FastNoiseLite();
    public FastNoiseLite _medium = new FastNoiseLite();
    public FastNoiseLite _detail = new FastNoiseLite();
    public FastNoiseLite _ridges = new FastNoiseLite();
    public FastNoiseLite _variation = new FastNoiseLite();
    public FastNoiseLite _woodland = new FastNoiseLite();

    /// Canopy coverage (0 open sky .. 1 dense canopy) painted by the tree planner.
    public ScalarField canopy = new ScalarField(2, INNER_EXTENT, 0.0);
    /// Baked heights over the walkable area (see bake_height_grid).
    public ScalarField height_grid;
    public double height_grid_half = 0.0;
    /// Immutable after TerrainBuilder prepares it, before worker scatter begins.
    public List<float> surface_axis = new List<float>();
    public List<float> surface_heights = new List<float>();

    public TerrainField(long seed_value = 20260902)
    {
        _rolling.Seed = (int)seed_value;
        _rolling.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _rolling.Frequency = 0.011f;
        _rolling.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _rolling.FractalOctaves = 2;

        _medium.Seed = (int)(seed_value + 1);
        _medium.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _medium.Frequency = 0.045f;
        _medium.FractalOctaves = 2;

        _detail.Seed = (int)(seed_value + 2);
        _detail.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _detail.Frequency = 0.19f;
        _detail.FractalOctaves = 3;

        _ridges.Seed = (int)(seed_value + 3);
        _ridges.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _ridges.Frequency = 0.0032f;
        _ridges.FractalType = FastNoiseLite.FractalTypeEnum.Ridged;
        _ridges.FractalOctaves = 4;

        _variation.Seed = (int)(seed_value + 4);
        _variation.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _variation.Frequency = 0.03f;
        _variation.FractalOctaves = 2;
        // Same broad stand field as the wooded ridges, beyond the painted canopy.
        _woodland.Seed = 1927;
        _woodland.Frequency = 0.016f;
    }

    public virtual double height(double x, double z)
    {
        // --------------------------------------------------------------------- geometry
        /// Ground height in metres.
        Vector2 p = new Vector2((float)x, (float)z);
        double d = p.Length();

        double rolling = _rolling.GetNoise2D((float)x, (float)z) * 2.6;
        double medium = _medium.GetNoise2D((float)x, (float)z) * 0.75;
        double detail = _detail.GetNoise2D((float)x, (float)z) * 0.22;
        double undulation = rolling + medium + detail;
        double meadow = MEADOW_BASE + (undulation > 0.0 ? undulation : undulation * DIP_COMPRESSION);
        // The compressed meadow gives way to the raw landscape beyond the treeline.
        double wild = smoothstep(70.0, 130.0, d);
        double h = lerpf(meadow, undulation, wild);
        // The camp sits on a low shelf; the ground climbs gently to the east and
        // north, and stays low to the west so the sunset is visible over the pond.

        double west = smoothstep(-10.0, -60.0, x);
        h += smoothstep(30.0, 95.0, d) * 5.5 * (1.0 - west * 0.8);
        // Distant hills close the horizon behind the treeline without hiding the sky.

        if (d > 140.0)
        {
            double far = smoothstep(140.0, 520.0, d);
            h += far * (18.0 + (_ridges.GetNoise2D((float)x, (float)z) * 0.5 + 0.5) * 42.0);
        }
        h += _hill(p, new Vector2(210.0f, -170.0f), 150.0, 46.0);
        h += _hill(p, new Vector2(-320.0f, -260.0f), 190.0, 60.0);
        h += _hill(p, new Vector2(260.0f, 300.0f), 170.0, 38.0);
        // Pond: carved relative to the water level so the shoreline always lands
        // where the shape says, with a shelving beach on the outside.

        Vector2 to_centre = p - POND_CENTRE;
        double pd = to_centre.Length();
        if (pd < POND_MAX_RADIUS * 1.6)
        {
            double s = pd / pond_radius_at(atan2(to_centre.Y, to_centre.X));
            double w = 1.0 - smoothstep(1.15, 1.55, s);
            if (w > 0.0)
            {
                double bed = WATER_LEVEL + _pond_profile(s) + detail * 1.4 * (1.0 - smoothstep(0.8, 1.0, s));
                h = lerpf(h, bed, w);
            }
        }
        // Trail: blend towards the smooth large-scale surface and sink slightly.

        if (TRAIL_BOUNDS.Grow((float)TRAIL_INFLUENCE).HasPoint(p))
        {
            double trail_mask = 1.0 - smoothstep(1.2, 3.4, trail_distance(x, z));
            // The pond carve already replaced the original detail near its shore.
            // Subtracting that detail again used to move the eastern waterline and
            // cut a dry outside sample below water. Keep the zero crossing exact.
            trail_mask *= smoothstep(0.015, 0.32, h - WATER_LEVEL);
            if (trail_mask > 0.0)
            {
                double smooth_h = maxf(h - detail - 0.06, WATER_LEVEL + 0.015);
                h = lerpf(h, smooth_h, trail_mask * 0.85);
            }
        }
        // Level pads for the fire circle and the tent.

        if (p.DistanceTo(FIRE) < 6.5)
        {
            h = _flatten(h, p, FIRE, 2.5, 6.5, _pad_height(FIRE));
        }
        if (p.DistanceTo(TENT) < 4.5)
        {
            h = _flatten(h, p, TENT, 2.2, 4.5, _pad_height(TENT));
        }
        return h;
    }

    public double height_fast(double x, double z)
    {
        /// Height from the baked grid where it exists (bilinear), else the function.
        if (height_grid != null && absf(x) < height_grid_half && absf(z) < height_grid_half)
        {
            return height_grid.sample(x, z);
        }
        return height(x, z);
    }

    public void bake_surface_grid(List<float> axis)
    {
        surface_axis = axis;
        long n = (long)axis.Count;
        G.resize(surface_heights, (int)(n * n));
        for (long iz = 0; iz < n; iz++)
        {
            for (long ix = 0; ix < n; ix++)
            {
                surface_heights[(int)(iz * n + ix)] = (float)height_fast(axis[(int)ix], axis[(int)iz]);
            }
        }
    }

    public double surface_height(double x, double z)
    {
        /// Barycentric interpolation follows TerrainBuilder's a-b-d / a-d-c diagonal.
        /// Bilinear interpolation is not the surface of a non-planar rendered quad.
        if ((surface_axis.Count == 0))
        {
            return height_fast(x, z);
        }
        long n = (long)surface_axis.Count;
        long ix = clampi(G.bsearch(surface_axis, (float)x) - 1, 0, n - 2);
        long iz = clampi(G.bsearch(surface_axis, (float)z) - 1, 0, n - 2);
        double tx = clampf((x - surface_axis[(int)ix]) / ((double)surface_axis[(int)(ix + 1)] - surface_axis[(int)ix]), 0.0, 1.0);
        double tz = clampf((z - surface_axis[(int)iz]) / ((double)surface_axis[(int)(iz + 1)] - surface_axis[(int)iz]), 0.0, 1.0);
        double a = surface_heights[(int)(iz * n + ix)];
        double b = surface_heights[(int)(iz * n + ix + 1)];
        double c = surface_heights[(int)((iz + 1) * n + ix)];
        double d = surface_heights[(int)((iz + 1) * n + ix + 1)];
        return tx >= tz ? a * (1.0 - tx) + b * (tx - tz) + d * tz : a * (1.0 - tz) + c * (tz - tx) + d * tx;
    }

    public double surface_slope(double x, double z)
    {
        double dx = surface_height(x - 1.0, z) - surface_height(x + 1.0, z);
        double dz = surface_height(x, z - 1.0) - surface_height(x, z + 1.0);
        return atan(sqrt(dx * dx + dz * dz) * 0.5);
    }

    public void bake_height_grid(double half, double spacing)
    {
        /// Bakes `height()` on a square grid of `spacing` metres covering ±`half`.
        /// The grid doubles as the collision heightfield, so it is uniform.
        long resolution = (long)round(half * 2.0 / spacing) + 1;
        ScalarField grid = new ScalarField(resolution, (double)(resolution - 1) * spacing, 0.0);
        double origin = -(double)(resolution - 1) * spacing * 0.5;
        List<float> data = grid.data;
        for (long iz = 0; iz < resolution; iz++)
        {
            double z = origin + (double)iz * spacing;
            long row = iz * resolution;
            for (long ix = 0; ix < resolution; ix++)
            {
                data[(int)(row + ix)] = (float)height(origin + (double)ix * spacing, z);
            }
        }
        grid.data = data;
        height_grid = grid;
        height_grid_half = (double)(resolution - 1) * spacing * 0.5;
    }

    public double _pad_height(Vector2 at)
    {
        double undulation = _rolling.GetNoise2D(at.X, at.Y) * 2.6 + _medium.GetNoise2D(at.X, at.Y) * 0.75;
        return MEADOW_BASE + (undulation > 0.0 ? undulation : undulation * DIP_COMPRESSION);
    }

    public double _flatten(double h, Vector2 p, Vector2 centre, double inner, double outer, double target)
    {
        double w = 1.0 - smoothstep(inner, outer, p.DistanceTo(centre));
        return lerpf(h, target, w);
    }

    public double _hill(Vector2 p, Vector2 centre, double radius, double amplitude)
    {
        double d = p.DistanceTo(centre) / radius;
        return amplitude * exp(-d * d * 1.8);
    }

    public static double pond_radius_at(double angle)
    {
        /// Shoreline distance from the pond centre in direction `angle` (radians,
        /// atan2(z, x)). Smaller towards the camp in the east, wider to the west.
        return POND_RADIUS * (1.0 - 0.16 * cos(angle) + 0.10 * sin(2.0 * angle + 0.7) + 0.07 * sin(3.0 * angle + 2.1) + 0.05 * sin(5.0 * angle + 1.0));
    }

    public static Vector2 shore_point(double angle)
    {
        /// Point on the shoreline in direction `angle`.
        return POND_CENTRE + new Vector2((float)cos(angle), (float)sin(angle)) * (float)pond_radius_at(angle);
    }

    public static double _pond_profile(double s)
    {
        /// Bed height relative to the water level as a function of the normalised
        /// distance from the centre (1 = shoreline): a flat deep bottom, a steeper
        /// underwater bank, gentle shallows and a shelving beach above the waterline.
        if (s < 0.55)
        {
            return -3.4;
        }
        if (s < 0.85)
        {
            return lerpf(-3.4, -0.6, smoothstep(0.55, 0.85, s));
        }
        if (s < 1.0)
        {
            return lerpf(-0.6, 0.0, (s - 0.85) / 0.15);
        }
        if (s < 1.15)
        {
            return lerpf(0.0, 0.45, (s - 1.0) / 0.15);
        }
        return 0.45;
    }

    public Vector3 normal(double x, double z, double step = 0.5)
    {
        /// Smooth surface normal from central differences.
        double hl = height(x - step, z);
        double hr = height(x + step, z);
        double hd = height(x, z - step);
        double hu = height(x, z + step);
        return new Vector3((float)(hl - hr), (float)(2.0 * step), (float)(hd - hu)).Normalized();
    }

    public double slope(double x, double z)
    {
        /// Slope in radians.
        return acos(clampf(normal(x, z).Y, -1.0, 1.0));
    }

    public double trail_distance(double x, double z)
    {
        Vector2 p = new Vector2((float)x, (float)z);
        if (!TRAIL_BOUNDS.Grow((float)TRAIL_INFLUENCE).HasPoint(p))
        {
            return TRAIL_INFLUENCE + 1.0;
        }
        return distance_to_polyline(p, TRAIL);
    }

    public static double distance_to_polyline(Vector2 p, Godot.Collections.Array<Vector2> points)
    {
        double best = INF;
        for (long i = 0, i_end = (long)points.Count - 1; i < i_end; i++)
        {
            Vector2 a = points[(int)i];
            Vector2 b = points[(int)(i + 1)];
            Vector2 ab = b - a;
            double t = clampf((p - a).Dot(ab) / maxf(ab.LengthSquared(), 1e-6), 0.0, 1.0);
            best = minf(best, p.DistanceTo(a + ab * (float)t));
        }
        return best;
    }

    public double pond_distance(double x, double z)
    {
        return new Vector2((float)x, (float)z).DistanceTo(POND_CENTRE);
    }

    public double water_depth(double x, double z)
    {
        /// Depth of water above the ground (0 on dry land).
        return maxf(WATER_LEVEL - height_fast(x, z), 0.0);
    }

    public bool is_underwater(double x, double z)
    {
        return height_fast(x, z) < WATER_LEVEL;
    }

    public static double camp_wear(Vector2 p)
    {
        // ------------------------------------------------------------------- materials
        /// Bare, trodden ground around the fire circle and the tent.
        double fire = 1.0 - smoothstep(2.3, 4.2, p.DistanceTo(FIRE));
        double tent = 1.0 - smoothstep(2.0, 3.2, p.DistanceTo(TENT));
        double table = 1.0 - smoothstep(1.0, 1.8, p.DistanceTo(TABLE));
        double irregular = sin(p.X * 2.8 + sin(p.Y * 3.1)) * sin(p.Y * 1.9 + 1.7) * 0.14;
        double wear = maxf(maxf(fire, tent), table);
        if (p.DistanceSquaredTo(WOODPILE) < 9.0)
        {
            // Shared with the prop placement: the pile, loose splits and chopping
            // block occupy a worked patch, with a feathered edge into the meadow.
            Vector2 local = (p - WOODPILE).Rotated((float)WOODPILE_YAW);
            double pile_radius = ((local - new Vector2(0.12f, 0.25f)) / new Vector2(1.04f, 1.14f)).Length();
            double block_radius = ((local - new Vector2(0.95f, 0.5f)) / new Vector2(0.70f, 0.78f)).Length();
            double wood_work = 1.0 - smoothstep(0.90, 1.40, minf(pile_radius, block_radius));
            wear = maxf(wear, wood_work);
        }
        if (p.DistanceSquaredTo(FIRE) < 20.25)
        {
            foreach (Vector2 seat in SEATS)
            {
                Vector2 outward = (seat - FIRE).Normalized();
                Vector2 tangent = new Vector2(outward.Y, -outward.X);
                Vector2 footwell = p - (seat - outward * 0.32f);
                double along = 1.0 - smoothstep(0.76, 1.12, absf(footwell.Dot(tangent)));
                double across = 1.0 - smoothstep(0.32, 0.65, absf(footwell.Dot(outward)));
                wear = maxf(wear, along * across);
                // Keep the entire seat and its wind-swept margin clear. A footwell
                // alone leaves tall blades growing through the back of the log.
                Vector2 beneath = p - seat;
                double seat_along = 1.0 - smoothstep(1.10, 1.32, absf(beneath.Dot(tangent)));
                double seat_across = 1.0 - smoothstep(0.43, 0.68, absf(beneath.Dot(outward)));
                wear = maxf(wear, seat_along * seat_across);
            }
        }
        return clampf(wear + irregular * wear * (1.0 - wear), 0.0, 1.0);
    }

    public Color material_mask(double x, double z, double? known_height_opt = null, double? known_slope_opt = null)
    {
        double known_height = known_height_opt ?? NAN;
        double known_slope = known_slope_opt ?? NAN;
        /// Ground material weights: r = grass, g = leaf litter, b = mud, a = trail.
        /// `known_height` / `known_slope` let callers that already sampled the surface
        /// (mesh builders, scatterers) skip the expensive re-evaluation.
        double h = is_nan(known_height) ? height_fast(x, z) : known_height;
        double s = is_nan(known_slope) ? slope(x, z) : known_slope;
        double cover = woodland_cover(x, z);
        double steep = smoothstep(0.42, 0.9, s);
        double wobble = _variation.GetNoise2D((float)(x * 3.0), (float)(z * 3.0)) * 0.35;
        Vector2 p = new Vector2((float)x, (float)z);

        double trail = 1.0 - smoothstep(0.32 + wobble, 1.22 + wobble, walking_distance(p));
        trail = maxf(trail, camp_wear(p) * (0.85 + wobble));
        double mud = 1.0 - smoothstep(0.05, 0.55, h - WATER_LEVEL);
        double litter = clampf(cover * 1.2 + steep * 0.8 + wobble * 0.6, 0.0, 1.0);
        double grass = clampf((1.0 - litter) * (1.0 - steep), 0.0, 1.0);

        grass *= 1.0 - mud;
        litter *= 1.0 - mud;
        grass *= 1.0 - trail;
        litter *= 1.0 - trail * 0.9;
        return new Color((float)grass, (float)litter, (float)mud, (float)trail);
    }

    public double dryness(double x, double z)
    {
        /// Low-frequency colour variation for foliage (0 = cool/lush, 1 = warm/dry).
        double shore_damp = 1.0 - smoothstep(0.3, 2.0, height_fast(x, z) - WATER_LEVEL);
        double cover = woodland_cover(x, z);
        return clampf((_variation.GetNoise2D((float)x, (float)z) * 0.7 + 0.42) * (1.0 - cover * 0.45 - shore_damp * 0.48), 0.0, 1.0);
    }

    public double woodland_cover(double x, double z)
    {
        /// Open hills keep turf; litter belongs to actual woodland stands. The old
        /// distance-only litter ring and clamped canopy-map edge made a bare band.
        Vector2 p = new Vector2((float)x, (float)z);
        double transition = smoothstep(95.0, 155.0, p.Length());
        double painted = canopy.sample(x, z);
        if (transition <= 0.0)
        {
            return painted;
        }
        double habitat = _woodland.GetNoise2D((float)x, (float)z);
        double distant = smoothstep(-0.25, 0.35, habitat) * 0.82;
        double opening = woodland_opening(p) * (1.0 - smoothstep(245.0, 325.0, p.Length()));
        distant *= 1.0 - opening * 0.60;
        return lerpf(painted, distant, transition);
    }

    public static double woodland_opening(Vector2 p)
    {
        /// Pure field equivalent of the ScenePlan sunset corridor; no scene/autoload
        /// dependency is allowed while a terrain/grass worker evaluates habitat.
        Vector2 from_bank = p - new Vector2(-15.5f, 9.0f);
        Vector2 axis = new Vector2(-0.958f, -0.287f).Normalized();
        double along = from_bank.Dot(axis);
        double across = absf(from_bank.Dot(new Vector2(-axis.Y, axis.X)));
        double half_width = 3.0 + maxf(along, 0.0) * 0.13;
        return smoothstep(22.0, 38.0, along) * (1.0 - smoothstep(half_width, half_width + 9.0, across));
    }

    public double walking_distance(Vector2 p)
    {
        /// Narrow desire lines link the places campers actually use. The route also
        /// drives ground cover and scatter clearance; it is never a floating decal.
        double main = trail_distance(p.X, p.Y);
        if (p.X > -1.0 && p.X < 12.0 && p.Y > -7.0 && p.Y < 4.0)
        {
            main = minf(main, distance_to_polyline(p, CAMP_TRACK) + 0.26);
            main = minf(main, distance_to_polyline(p, TABLE_TRACK) + 0.30);
        }
        return main;
    }

    public double meadow_height(double x, double z)
    {
        /// Coherent tussocks break up the meadow without random bare holes.
        double n = _detail.GetNoise2D((float)(x * 0.8), (float)(z * 0.8));
        return lerpf(0.28, 0.63, smoothstep(-0.26, 0.32, n));
    }

    public double grass_suitability(double x, double z, double? known_height_opt = null, double? known_slope_opt = null)
    {
        double known_height = known_height_opt ?? NAN;
        double known_slope = known_slope_opt ?? NAN;
        /// Suitability for grass blades: open, gentle, dry ground away from the trail.
        double h = is_nan(known_height) ? height_fast(x, z) : known_height;
        double s = is_nan(known_slope) ? slope(x, z) : known_slope;
        double dry_land = smoothstep(0.03, 0.30, h - WATER_LEVEL);
        double trail_edge = smoothstep(0.34, 1.22, walking_distance(new Vector2((float)x, (float)z)));
        double pad_edge = 1.0 - smoothstep(0.1, 0.85, camp_wear(new Vector2((float)x, (float)z)));
        // The material mask already suppressed grass at the trail; multiplying it
        // twice left wide bald bands. Plant from habitat instead: shorter woodland
        // grass continues under trees, with only the walked route and pads bare.
        double habitat = lerpf(0.98, 0.48, woodland_cover(x, z));
        return habitat * dry_land * trail_edge * pad_edge * (1.0 - smoothstep(0.48, 0.95, s));
    }
}
