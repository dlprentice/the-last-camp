#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// Finds Tom's Rock the way the design describes it: the highest walkable ground
/// in the south-east sector, 60 to 110 m from camp and inside the walking area,
/// from which a seated eye on the outcrop sees most of the pond over the land.
/// It reads a field built with `places_enabled = false`, because the outcrop and
/// the paths it chose are themselves part of the finished field.
public static class PlacesProbe
{
    public const double GRID = 2.0;
    public const double MIN_FROM_CAMP = 60.0;
    public const double MAX_FROM_CAMP = 110.0;
    /// The outcrop's base and the paths off it must stay inside the walking area.
    public const double BOUNDARY_MARGIN = 20.0;
    public const double MAX_SLOPE = 0.30;
    /// The south-east sector as bearings from camp (north 0, east 90), centred on
    /// the design's (40, 60) at 146 degrees.
    public const double SECTOR_FROM_DEG = 120.0;
    public const double SECTOR_TO_DEG = 170.0;
    /// A little more than half, so the bench itself (just behind the summit
    /// centre) still sees half the pond.
    public const double MIN_POND_VIEW = 0.55;

    public static Vector2 find_toms_rock(TerrainField natural)
    {
        var candidates = new List<(double h, Vector2 p)>();
        for (double x = 0.0; x <= MAX_FROM_CAMP; x += GRID)
        {
            for (double z = 0.0; z <= MAX_FROM_CAMP; z += GRID)
            {
                Vector2 p = new((float)x, (float)z);
                double d = p.Length();
                if (d < MIN_FROM_CAMP || d > MAX_FROM_CAMP) continue;
                double bearing = Mathf.RadToDeg(Math.Atan2(x, -z));
                if (bearing < SECTOR_FROM_DEG || bearing > SECTOR_TO_DEG) continue;
                if (p.DistanceTo(Player.PLAY_AREA_CENTRE) > Player.PLAY_AREA_RADIUS - BOUNDARY_MARGIN) continue;
                if (natural.slope(x, z) > MAX_SLOPE) continue;
                candidates.Add((natural.height(x, z), p));
            }
        }
        // Highest first; ties resolve by position so the answer never depends on sort stability.
        candidates.Sort((a, b) => a.h != b.h ? b.h.CompareTo(a.h) : a.p.X != b.p.X ? a.p.X.CompareTo(b.p.X) : a.p.Y.CompareTo(b.p.Y));
        foreach (var (h, p) in candidates)
        {
            if (pond_view(natural, p, h + PlacesLayout.ROCK_RISE + PlacesLayout.SEATED_EYE) >= MIN_POND_VIEW) return p;
        }
        return Vector2.Inf;
    }

    /// Fraction of points spread over the pond's open water that a viewer at `eye`
    /// metres above `from` sees past the land. Trees are kept out of the view by the
    /// scene plan's lookout corridor, so only the ground is tested here.
    public static double pond_view(TerrainField field, Vector2 from, double eye)
    {
        int seen = 0, total = 0;
        for (int i = 0; i < 24; i++)
        {
            double a = i / 24.0 * Math.Tau;
            for (double r = 0.2; r < 0.85; r += 0.3)
            {
                Vector2 target = TerrainField.POND_CENTRE + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (float)(TerrainField.pond_radius_at(a) * r);
                total++;
                if (sight_clear(field, from, eye, target, TerrainField.WATER_LEVEL)) seen++;
            }
        }
        return (double)seen / total;
    }

    public static bool sight_clear(TerrainField field, Vector2 from, double eye, Vector2 to, double to_height)
    {
        double length = from.DistanceTo(to);
        for (double t = 1.5; t < length - 1.0; t += 0.75)
        {
            Vector2 q = from.Lerp(to, (float)(t / length));
            double line = Mathf.Lerp(eye, to_height, t / length);
            if (field.height(q.X, q.Y) > line) return false;
        }
        return true;
    }
}
