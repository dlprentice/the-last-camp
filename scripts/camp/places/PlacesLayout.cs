#nullable disable
using System;
using Godot;

namespace LastCamp;

/// Where the micro cut's three places stand and the paths between them, as pure
/// data. The terrain field reads the paths (as it reads the main trail), the scene
/// plan keeps trees off the places, and `Places` builds what stands there.
/// Coordinates follow TerrainField: +X east, +Z south, the camp fire at the origin.
public static class PlacesLayout
{
    // ------------------------------------------------------------ trailhead
    /// The carved post stands just east of the trail a few metres in from its
    /// south end (TRAIL[0]), its lettered face turned west-south-west to the trail,
    /// so it is passed walking in and seen ahead walking out.
    public static readonly Vector2 TRAILHEAD_POST = new(5.5f, 67.4f);
    public static readonly Vector2 TRAILHEAD_FACE_TOWARDS = new(3.6f, 68.5f);
    public const double ARRIVE_TRAILHEAD = 4.5;

    // ------------------------------------------------------------ old fire ring
    public static readonly Vector2 OLD_RING = new(-30.0f, -45.0f);
    public const double OLD_RING_RADIUS = 0.78;
    /// The fallen log seat lies along the ring's east side, facing the stones.
    public static readonly Vector2 OLD_LOG_A = new(-28.9f, -47.55f);
    public static readonly Vector2 OLD_LOG_B = new(-26.75f, -45.35f);
    public const double OLD_LOG_RADIUS = 0.19;
    public const double ARRIVE_OLD_RING = 5.0;

    // ------------------------------------------------------------ Tom's Rock
    /// The outcrop's summit. PlacesProbe.find_toms_rock returns this point from
    /// the field without the places; a test keeps the two in step.
    public static readonly Vector2 TOMS_ROCK = new(26.0f, 70.0f);
    /// The knob lifts the ground this far above the surrounding slope.
    public const double ROCK_RISE = 10.0;
    public const double ROCK_TOP_RADIUS = 2.0;
    /// Its foot on the gentle side, where the steps climb; towards the view it
    /// falls more steeply, by up to FRONT_STEEPEN of that.
    public const double ROCK_BASE_RADIUS = 20.0;
    public const double FRONT_STEEPEN = 0.3;
    public const double FRONT_DROP = 0.45;
    /// A long, gentler spur runs out to the south-west, June's way down.
    public const double SPUR_BEARING_DEG = 242.0;
    public const double SPUR_LENGTHEN = 0.2;
    /// No part of the knob reaches farther than this from its summit.
    public const double ROCK_REACH = ROCK_BASE_RADIUS * 1.15 * (1.0 + SPUR_LENGTHEN);
    /// The summit is levelled out to this radius for the bench and its footstone.
    public const double SUMMIT_PAD_INNER = 1.4;
    public const double SUMMIT_PAD_OUTER = 2.6;
    /// Eye height of someone sitting on the bench, above the summit.
    public const double SEATED_EYE = 1.15;
    /// The bench looks north-west: between the pond (about 319 degrees from here)
    /// and the late-August sunset (about 289 degrees).
    public const double BENCH_BEARING_DEG = 305.0;
    public static readonly Vector2 FACE = bearing(BENCH_BEARING_DEG);
    /// To the right of someone sitting on the bench (north-east).
    public static readonly Vector2 RIGHT = new(-FACE.Y, FACE.X);
    public const double BENCH_SEAT_HEIGHT = 0.45;
    public static readonly Vector2 BENCH = local(-0.45, 0.0);
    /// The twentieth step, the footstone under the bench.
    public static readonly Vector2 LOOSE_STEP = local(0.27, 0.0);
    public const long STEP_COUNT = 20;
    public const double ARRIVE_TOMS_ROCK = 3.6;

    /// The steps wind up the outcrop: a long rising traverse round its east and
    /// south flanks, one switchback, a shorter climb back round inside it, and the
    /// last few onto the summit, ending on the twentieth step, which they reach from
    /// the bench's right-hand side. The two legs stand well apart on the slope.
    /// Listed from the bottom step to the top. The ground along the line is cut
    /// level across it, as a dug-in step path would be.
    public static readonly Vector2[] STEP_LINE =
    {
        polar(18.5, 26.0), polar(16.6, 52.0), polar(14.8, 80.0), polar(13.0, 108.0), polar(11.4, 136.0),
        polar(10.0, 164.0), polar(7.8, 140.0), polar(6.3, 110.0), polar(4.9, 80.0), polar(3.4, 52.0),
        polar(1.05, 35.0), LOOSE_STEP,
    };
    public const double STEP_CUT_INNER = 0.8;
    public const double STEP_CUT_OUTER = 2.4;
    /// Worn ground across the bench and its footstone, so nothing grows through
    /// the seat or the twentieth step.
    public static readonly Vector2[] BENCH_LINE = { BENCH - RIGHT * 0.75f, BENCH + RIGHT * 0.75f };

    // ------------------------------------------------------------ paths
    /// June's footpath from the north edge of the camp clearing to the old ring:
    /// east of the north-bank boulders, past the big pine and the snag, and along
    /// the dry ground. Design length 70 m. It starts at the clearing's north
    /// edge, clear of the ground the arrival film's walk and orbit follow.
    public static readonly Vector2[] JUNE_FOOTPATH =
    {
        new(-5.6f, -8.8f), new(-4.2f, -11.6f), new(-4.2f, -13.8f), new(-6.6f, -18.4f), new(-4.6f, -23.0f),
        new(2.4f, -28.6f), new(-3.6f, -33.8f), new(-8.2f, -37.8f), new(-14.2f, -42.0f), new(-19.4f, -38.0f),
        new(-23.4f, -40.4f), new(-25.6f, -43.0f), new(-27.4f, -43.8f),
    };
    public const double JUNE_FOOTPATH_TARGET = 70.0;

    /// Tom's route from the old ring through the north woods, over the east
    /// shoulder and up the south slope to the foot of his steps. The design's
    /// ratio for his route (230 m of path for 140 m straight) on the 127 m from the
    /// ring to the Rock gives the target, steps included.
    public static readonly Vector2[] TOMS_ROUTE =
    {
        new(-28.6f, -49.2f), new(-22.4f, -51.6f), new(-14.0f, -57.0f), new(-4.0f, -57.6f), new(5.4f, -53.6f),
        new(13.0f, -47.6f), new(18.6f, -40.2f), new(24.8f, -33.6f), new(32.4f, -27.6f), new(38.4f, -18.6f),
        new(43.0f, -7.6f), new(44.0f, 4.4f), new(41.6f, 15.6f), new(37.6f, 26.0f), new(35.8f, 36.4f),
        new(35.4f, 46.2f), STEP_LINE[0],
    };
    public const double TOMS_ROUTE_TARGET = 208.0;

    /// June's painted path from the summit down the knob's long south-west spur
    /// (the gentlest way off it) to the trail's end by the trailhead, where the
    /// trail takes it on to camp. Design length, Rock to camp: 110 m.
    public static readonly Vector2[] PAINTED_PATH =
    {
        local(-0.9, -1.4), polar(5.5, 228.0), polar(10.5, 240.0), polar(16.0, 238.0), new(10.2f, 79.6f),
        new(6.0f, 80.2f), new(3.0f, 76.0f), new(3.98f, 69.6f),
    };
    public const double PAINTED_PATH_TARGET = 110.0;

    /// Path lines in the terrain's trail field, each with the distance added to
    /// it: a footpath reads narrower than the trail, Tom's faint route narrower
    /// still, and the steps and bench are worn bare like the trail itself.
    public static readonly (Vector2[] line, double offset)[] TRAIL_LINES =
    {
        (JUNE_FOOTPATH, 0.15), (TOMS_ROUTE, 0.35), (STEP_LINE, 0.0), (BENCH_LINE, 0.0), (PAINTED_PATH, 0.15),
        // Where people sat on the old log, the grass is trodden short along it.
        (new[] { OLD_LOG_A, OLD_LOG_B }, 0.5),
    };
    public static readonly Rect2[] TRAIL_LINE_BOUNDS = bounds_of(TRAIL_LINES);
    public static readonly Rect2 ALL_TRAIL_BOUNDS = merge(TRAIL_LINE_BOUNDS);

    // ------------------------------------------------------------ blazes
    /// Tom's knife notches after the first (on the log seat): one on a tree beside
    /// his route every 15 to 25 m, at chest height, cut on the side that faces
    /// someone coming from the ring.
    public const double BLAZE_SPACING = 20.0;
    public const double BLAZE_JITTER = 4.0;
    public const double BLAZE_TREE_OFFSET = 1.9;
    public const double BLAZE_HEIGHT = 1.35;
    public const double BLAZE_FIRST = 15.0;

    public struct BlazeSite
    {
        /// Where the notched tree stands.
        public Vector2 tree;
        /// Unit direction the notch faces: back down the route towards the ring.
        public Vector2 face;
        /// Arc length along TOMS_ROUTE where the tree stands beside it.
        public double along;
    }

    /// Tom's tree blazes in route order, every 15 to 25 m, alternating sides
    /// irregularly, ending a few metres short of his first step.
    public static BlazeSite[] blaze_sites()
    {
        var sites = new System.Collections.Generic.List<BlazeSite>();
        double total = length(TOMS_ROUTE);
        var along = new System.Collections.Generic.List<double>();
        double s = BLAZE_FIRST;
        for (int i = 0; s < total - 5.0; i++)
        {
            along.Add(s);
            s += BLAZE_SPACING + Math.Sin(i * 12.9898 + 4.1) * BLAZE_JITTER;
        }
        // The last notch stands within sight of the first step.
        double rest = total - along[^1];
        if (rest > BLAZE_SPACING + BLAZE_JITTER) along.Add(along[^1] + Math.Clamp(rest - 4.0, BLAZE_SPACING - BLAZE_JITTER, BLAZE_SPACING + BLAZE_JITTER));
        for (int i = 0; i < along.Count; i++)
        {
            Vector2 on_route = point_at(TOMS_ROUTE, along[i], out Vector2 tangent);
            Vector2 normal = new(-tangent.Y, tangent.X);
            double side = Math.Sin(i * 2.399 + 0.7) >= 0.0 ? 1.0 : -1.0;
            Vector2 tree = on_route + normal * (float)(side * BLAZE_TREE_OFFSET);
            Vector2 behind = point_at(TOMS_ROUTE, Math.Max(0.0, along[i] - 7.0), out _);
            sites.Add(new BlazeSite { tree = tree, face = (behind - tree).Normalized(), along = along[i] });
        }
        return sites.ToArray();
    }

    // ------------------------------------------------------------ reserves
    /// Clear ground kept around each place for scattered props and rocks (the
    /// ring's grass still grows; only placed objects stay out).
    public static readonly Vector3[] PROP_RESERVES =
    {
        new(TRAILHEAD_POST.X, TRAILHEAD_POST.Y, 1.0f), new(OLD_RING.X, OLD_RING.Y, 2.2f),
        new((OLD_LOG_A.X + OLD_LOG_B.X) * 0.5f, (OLD_LOG_A.Y + OLD_LOG_B.Y) * 0.5f, 2.0f),
        new(TOMS_ROCK.X, TOMS_ROCK.Y, (float)(ROCK_BASE_RADIUS * 0.8)),
    };

    public static Vector2 bearing(double degrees)
    {
        double a = Mathf.DegToRad(degrees);
        return new Vector2((float)Math.Sin(a), (float)-Math.Cos(a));
    }

    /// A point on the summit: `u` metres along the bench's view, `v` to its right.
    public static Vector2 local(double u, double v) => TOMS_ROCK + FACE * (float)u + RIGHT * (float)v;

    /// A point `r` metres from the summit centre on the given compass bearing.
    public static Vector2 polar(double r, double bearing_deg) => TOMS_ROCK + bearing(bearing_deg) * (float)r;

    public static double length(Vector2[] line)
    {
        double total = 0.0;
        for (int i = 0; i + 1 < line.Length; i++) total += line[i].DistanceTo(line[i + 1]);
        return total;
    }

    /// Distance from `p` to the polyline, and the nearest point on it.
    public static double distance_to(Vector2 p, Vector2[] line, out Vector2 nearest)
    {
        double best = double.PositiveInfinity;
        nearest = line[0];
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 c = closest_on_segment(p, line[i], line[i + 1]);
            double d = p.DistanceTo(c);
            if (d < best)
            {
                best = d;
                nearest = c;
            }
        }
        return best;
    }

    public static Vector2 closest_on_segment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / Math.Max(ab.LengthSquared(), 1e-6f), 0.0f, 1.0f);
        return a + ab * t;
    }

    /// Point at arc length `s` along the polyline, with its unit tangent.
    public static Vector2 point_at(Vector2[] line, double s, out Vector2 tangent)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            double seg = line[i].DistanceTo(line[i + 1]);
            tangent = (line[i + 1] - line[i]).Normalized();
            if (s <= seg || i + 2 == line.Length) return line[i] + tangent * (float)Math.Clamp(s, 0.0, seg);
            s -= seg;
        }
        tangent = Vector2.Right;
        return line[0];
    }

    /// Distance from `p` to the nearest place reserve's edge (negative inside).
    public static double prop_reserve(Vector2 p)
    {
        double best = double.PositiveInfinity;
        foreach (Vector3 r in PROP_RESERVES) best = Math.Min(best, p.DistanceTo(new Vector2(r.X, r.Y)) - r.Z);
        return best;
    }

    /// Trees keep off the outcrop's top and flanks, the ring and the post, and
    /// out of the lookout's view down to the pond.
    public static bool keeps_trees_out(Vector2 p)
    {
        if (p.DistanceTo(TOMS_ROCK) < 11.0 || p.DistanceTo(OLD_RING) < 4.6 || p.DistanceTo(TRAILHEAD_POST) < 2.6) return true;
        // Nothing stands on the knob's front flank, whose crowns would rise into
        // the seated view either side of it.
        Vector2 off = p - TOMS_ROCK;
        if (off.Length() < ROCK_BASE_RADIUS * 1.05 && FACE.Dot(off.Normalized()) > 0.1) return true;
        return in_lookout_view(p, 0.0);
    }

    /// The bench's view: a cone from the seat over the slope to the pond and the
    /// western sky, from the sunset (about 289 degrees) across the whole pond
    /// (about 305 to 331). Inside its core no tree or tall shrub stands; through its
    /// edge they thin out by chance, so the opening has no cut wall. `margin` widens
    /// the core by that many degrees.
    public static bool in_lookout_view(Vector2 p, double margin)
    {
        double keep = lookout_openness(p, margin);
        return keep >= 1.0 || (keep > 0.0 && hash01(p) < keep);
    }

    /// 1 inside the view cone's core, falling to 0 across its edge, 0 outside.
    public static double lookout_openness(Vector2 p, double margin = 0.0)
    {
        Vector2 d = p - BENCH;
        double along = d.Length();
        if (along < 0.5 || along > LOOKOUT_REACH) return along < 0.5 ? 1.0 : 0.0;
        double angle = Math.Abs(Mathf.RadToDeg(bearing(LOOKOUT_BEARING_DEG).AngleTo(d)));
        double core = LOOKOUT_CORE_DEG + margin;
        // Far out the edge also thins towards the pond's bank, where the trees stand anyway.
        double far = 1.0 - Math.Clamp((along - (LOOKOUT_REACH - 6.0)) / 6.0, 0.0, 1.0);
        double open = 1.0 - Math.Clamp((angle - core) / (LOOKOUT_EDGE_DEG - LOOKOUT_CORE_DEG), 0.0, 1.0);
        return open * open * (3.0 - 2.0 * open) * (far >= 1.0 ? 1.0 : far);
    }

    /// How far the knob lifts the ground at `p`: a rounded top, a convex shoulder
    /// and a foot that runs out into the slope, steeper on the side that looks
    /// over the pond, its outline lobed so it is no cone.
    public static double rock_rise(Vector2 p)
    {
        Vector2 d = p - TOMS_ROCK;
        double dist = d.Length();
        if (dist <= ROCK_TOP_RADIUS) return ROCK_RISE;
        double a = Math.Atan2(d.Y, d.X);
        Vector2 dir = d / (float)dist;
        double facing = Math.Max(0.0, FACE.Dot(dir));
        double spur = Math.Max(0.0, bearing(SPUR_BEARING_DEG).Dot(dir));
        facing *= facing;
        spur *= spur * spur;
        double foot = ROCK_BASE_RADIUS * (1.0 - FRONT_STEEPEN * facing * facing) * (1.0 + SPUR_LENGTHEN * spur)
            * (1.0 + 0.06 * Math.Sin(3.0 * a + 1.3) + 0.04 * Math.Sin(5.0 * a + 0.4));
        double t = Math.Clamp((dist - ROCK_TOP_RADIUS) / (foot - ROCK_TOP_RADIUS), 0.0, 1.0);
        // Towards the view the shoulder is short: the ground falls away close in
        // front of the bench.
        t = Math.Pow(t, 1.0 - FRONT_DROP * facing);
        return ROCK_RISE * (1.0 - t * t * (3.0 - 2.0 * t));
    }

    /// How much of the ground is the outcrop's bare, stony skin (1 on its upper
    /// flanks, 0 beyond its foot): grass thins there and the soil shows.
    public static double outcrop_cover(Vector2 p)
    {
        double d = p.DistanceTo(TOMS_ROCK);
        if (d >= ROCK_BASE_RADIUS * 0.85) return 0.0;
        double t = Math.Clamp((d - ROCK_BASE_RADIUS * 0.55) / (ROCK_BASE_RADIUS * 0.3), 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    public static double hash01(Vector2 p)
    {
        double h = Math.Sin(p.X * 12.9898 + p.Y * 78.233) * 43758.5453;
        return h - Math.Floor(h);
    }

    public const double LOOKOUT_BEARING_DEG = 305.0;
    public const double LOOKOUT_CORE_DEG = 33.0;
    public const double LOOKOUT_EDGE_DEG = 48.0;
    /// Across the near bank to the water: beyond it only the far bank's trees stand.
    public const double LOOKOUT_REACH = 100.0;

    static Rect2[] bounds_of((Vector2[] line, double offset)[] lines)
    {
        var result = new Rect2[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            Rect2 r = new(lines[i].line[0], Vector2.Zero);
            foreach (Vector2 p in lines[i].line) r = r.Expand(p);
            result[i] = r;
        }
        return result;
    }

    static Rect2 merge(Rect2[] rects)
    {
        Rect2 r = rects[0];
        foreach (Rect2 o in rects) r = r.Merge(o);
        return r;
    }
}
