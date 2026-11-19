#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// Finds Tom's Rock by the picture, not the map: the site whose seated view from
/// the bench, at FOV 60 and the story's hour, has the pond filling the lower frame
/// and the sun going down over its far shore, so its glitter path runs back across
/// the water to the bench. Each candidate is judged with the knob it would raise
/// (PlacesLayout.rock_rise_at) on a field built with `places_enabled = false`;
/// sites whose knob would bury the trail, the camp, the shore or a film camera are
/// left out. Trees are not tested: the site's own view corridor clears them.
public static class PlacesProbe
{
    public const double GRID = 2.0;
    /// Candidates lie this far from the pond's centre.
    public const double MIN_FROM_POND = 33.0;
    public const double MAX_FROM_POND = 62.0;
    /// The knob's base and the paths off it must stay inside the walking area.
    public const double BOUNDARY_MARGIN = 25.0;
    /// The hour the bench is first sat on in the story (day 3 evening).
    public const double STORY_HOUR = 19.9;
    public const double FOV = 60.0;
    public const double ASPECT = 16.0 / 9.0;
    /// The knob may lift the trail, the shore and the camp's pads no more than this.
    public const double KEEP_CLEAR_RISE = 0.25;
    /// A film camera must pass this far above the knob.
    public const double CAMERA_CLEARANCE = 2.5;
    public const int COLUMNS = 20;
    public const int LOWER_ROWS = 9;

    public struct Site
    {
        public Vector2 at;
        /// The view: compass bearing and how far it looks down, degrees.
        public double yaw, pitch;
        /// Share of the lower half of the frame that is open water seen past the land.
        public double cover;
        /// The sun's bearing crosses the pond on its way to the bench.
        public bool sun_over_pond;
        public double score;
    }

    public static Vector2 find_toms_rock(TerrainField natural) => best_site(natural).at;

    /// What a site must keep to; the defaults are the game's. Other values let a
    /// search show what each rule costs the picture.
    public class Rules
    {
        public double height = PlacesLayout.ROCK_RISE;
        public bool cameras = true, trail = true;
        public double max_sun_off = 40.0;
    }

    public static Site best_site(TerrainField natural, List<Site> all = null, Rules rules = null)
    {
        List<Vector3> cameras = ScannedDressing.camera_samples(natural);
        Site best = new() { at = Vector2.Inf, score = double.NegativeInfinity };
        Vector2 pond = TerrainField.POND_CENTRE;
        for (double x = pond.X - MAX_FROM_POND; x <= pond.X + MAX_FROM_POND; x += GRID)
        {
            for (double z = pond.Y - MAX_FROM_POND; z <= pond.Y + MAX_FROM_POND; z += GRID)
            {
                Vector2 p = new((float)x, (float)z);
                double d = p.DistanceTo(pond);
                if (d < MIN_FROM_POND || d > MAX_FROM_POND) continue;
                if (p.DistanceTo(Player.PLAY_AREA_CENTRE) > Player.PLAY_AREA_RADIUS - BOUNDARY_MARGIN) continue;
                Site s = judge(natural, p, cameras, rules ?? new Rules());
                if (double.IsNegativeInfinity(s.score)) continue;
                all?.Add(s);
                // Ties resolve by position so the answer never depends on order.
                if (s.score > best.score + 1e-9 || (Math.Abs(s.score - best.score) <= 1e-9 && (p.X < best.at.X || (p.X == best.at.X && p.Y < best.at.Y)))) best = s;
            }
        }
        return best;
    }

    /// The bench's view from `at`: looking between the pond and the sun, and the
    /// pitch from a few that fills the lower frame best.
    public static Site judge(TerrainField natural, Vector2 at, List<Vector3> cameras, Rules rules)
    {
        Site s = new() { at = at, score = double.NegativeInfinity };
        Vector2 pond = TerrainField.POND_CENTRE;
        Vector3 sun = Atmosphere.sun_direction(STORY_HOUR);
        double sun_bearing = compass(new Vector2(sun.X, sun.Z));
        double pond_bearing = compass(pond - at);
        double off = wrap(sun_bearing - pond_bearing);
        // The sun and the pond must both fit the frame with room to spare.
        if (Math.Abs(off) > rules.max_sun_off) return s;
        double yaw = pond_bearing + off * 0.4;
        Vector2 face = PlacesLayout.bearing(yaw);
        Vector2 spur = -face;
        double rise(Vector2 q) => PlacesLayout.rock_rise_at(q, at, face, spur) * rules.height / PlacesLayout.ROCK_RISE;
        if (!keeps_clear(at, rise, natural, cameras, rules)) return s;
        double eye = natural.height(at.X, at.Y) + rules.height + PlacesLayout.SEATED_EYE;
        double ground(Vector2 q) => natural.height(q.X, q.Y) + rise(q);
        s.yaw = yaw;
        s.sun_over_pond = sun_crosses_pond(at);
        foreach (double pitch in new[] { 6.0, 10.0, 14.0, 18.0, 22.0 })
        {
            double cover = lower_frame_cover(new Vector3(at.X, (float)eye, at.Y), yaw, pitch, ground);
            if (cover > s.cover || s.pitch == 0.0) { s.cover = cover; s.pitch = pitch; }
        }
        // Coverage counts most; a sun over the water decides between near equals.
        s.score = s.cover + (s.sun_over_pond ? 0.15 : 0.0) - Math.Abs(off) * 0.001;
        return s;
    }

    /// The knob leaves the trail, the camp's ground, the jetty, the shore and the
    /// film cameras where they are.
    static bool keeps_clear(Vector2 at, Func<Vector2, double> rise, TerrainField natural, List<Vector3> cameras, Rules rules)
    {
        var trail = rules.trail ? TerrainField.TRAIL : new Godot.Collections.Array<Vector2>();
        for (int i = 0; i + 1 < trail.Count; i++)
        {
            double len = trail[i].DistanceTo(trail[i + 1]);
            for (double t = 0.0; t <= len; t += 1.0)
            {
                Vector2 q = trail[i].Lerp(trail[i + 1], (float)(t / len));
                if (q.DistanceTo(at) < 30.0 && rise(q) > KEEP_CLEAR_RISE) return false;
            }
        }
        foreach (Vector2 q in new[] { TerrainField.FIRE, TerrainField.TENT, TerrainField.TABLE, TerrainField.WOODPILE, TerrainField.DOCK_START })
        {
            if (q.DistanceTo(at) < 34.0 && rise(q) > 0.0) return false;
            if (q.DistanceTo(at) < 34.0 && rise(q + (q - at).Normalized() * -6.0f) > KEEP_CLEAR_RISE) return false;
        }
        for (int k = 0; k < 72; k++)
        {
            Vector2 shore = TerrainField.shore_point(k / 72.0 * Math.Tau);
            if (shore.DistanceTo(at) < 30.0 && rise(shore) > KEEP_CLEAR_RISE) return false;
        }
        foreach (Vector3 c in rules.cameras ? cameras : new List<Vector3>())
        {
            Vector2 q = new(c.X, c.Z);
            if (q.DistanceTo(at) > 30.0) continue;
            double r = rise(q);
            if (r > 0.0 && c.Y < natural.height(q.X, q.Y) + r + CAMERA_CLEARANCE) return false;
        }
        return true;
    }

    /// Share of the lower half of a FOV-60 frame, from `eye` looking along `yaw`
    /// tipped down `pitch` degrees, that shows open water past the land.
    public static double lower_frame_cover(Vector3 eye, double yaw, double pitch, Func<Vector2, double> ground)
    {
        double y = Mathf.DegToRad((float)yaw), p = Mathf.DegToRad((float)pitch);
        Vector3 forward = new((float)(Math.Sin(y) * Math.Cos(p)), (float)-Math.Sin(p), (float)(-Math.Cos(y) * Math.Cos(p)));
        Vector3 right = new((float)Math.Cos(y), 0, (float)Math.Sin(y));
        Vector3 up = right.Cross(forward);
        double tv = Math.Tan(Mathf.DegToRad((float)(FOV * 0.5))), th = tv * ASPECT;
        int seen = 0, total = 0;
        for (int j = 0; j < LOWER_ROWS; j++)
        {
            double v = -(j + 0.5) / LOWER_ROWS;
            for (int i = 0; i < COLUMNS; i++)
            {
                double u = (i + 0.5) / COLUMNS * 2.0 - 1.0;
                Vector3 dir = (forward + right * (float)(u * th) + up * (float)(v * tv)).Normalized();
                total++;
                if (dir.Y >= -1e-4f) continue;
                float t = (float)((eye.Y - TerrainField.WATER_LEVEL) / -dir.Y);
                Vector2 q = new(eye.X + dir.X * t, eye.Z + dir.Z * t);
                Vector2 from_centre = q - TerrainField.POND_CENTRE;
                if (from_centre.Length() > TerrainField.pond_radius_at(Math.Atan2(from_centre.Y, from_centre.X)) * 0.95) continue;
                Vector2 from = new(eye.X, eye.Z);
                double length = from.DistanceTo(q);
                bool clear = true;
                for (double s = 2.0; s < length - 1.0; s += 2.0)
                {
                    Vector2 g = from.Lerp(q, (float)(s / length));
                    if (ground(g) > Mathf.Lerp(eye.Y, (float)TerrainField.WATER_LEVEL, (float)(s / length))) { clear = false; break; }
                }
                if (clear) seen++;
            }
        }
        return (double)seen / total;
    }

    /// True when the line from the bench towards the sun's bearing crosses the
    /// pond: the sun sets over its far shore and its glitter runs back to the bench.
    public static bool sun_crosses_pond(Vector2 at)
    {
        Vector3 sun = Atmosphere.sun_direction(STORY_HOUR);
        Vector2 dir = new Vector2(sun.X, sun.Z).Normalized();
        for (double s = 5.0; s < 160.0; s += 1.0)
        {
            Vector2 q = at + dir * (float)s;
            Vector2 from_centre = q - TerrainField.POND_CENTRE;
            if (from_centre.Length() < TerrainField.pond_radius_at(Math.Atan2(from_centre.Y, from_centre.X)) * 0.8) return true;
        }
        return false;
    }

    public static double compass(Vector2 d)
    {
        double b = Mathf.RadToDeg(Math.Atan2(d.X, -d.Y));
        return b < 0.0 ? b + 360.0 : b;
    }

    static double wrap(double deg) => deg - 360.0 * Math.Round(deg / 360.0);

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

    /// Fraction of sight lines from `eye` to points spread over the pond's water
    /// that pass both the land and the trees. A tree blocks a line where it passes
    /// its trunk, or inside its crown: a column from the tree's lowest branches to
    /// its top, as wide as its longest limbs reach.
    public static double lookout_view(TerrainField field, IEnumerable<ScenePlan.TreeEntry> trees, Vector3 eye)
    {
        Vector2 from = new(eye.X, eye.Z);
        var near = new List<ScenePlan.TreeEntry>();
        foreach (ScenePlan.TreeEntry t in trees)
        {
            if (t.position.DistanceTo(TerrainField.POND_CENTRE) < from.DistanceTo(TerrainField.POND_CENTRE) + 2.0) near.Add(t);
        }
        int seen = 0, total = 0;
        for (int i = 0; i < 24; i++)
        {
            double a = i / 24.0 * Math.Tau;
            for (double r = 0.2; r < 0.85; r += 0.3)
            {
                Vector2 target = TerrainField.POND_CENTRE + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (float)(TerrainField.pond_radius_at(a) * r);
                total++;
                if (!sight_clear(field, from, eye.Y, target, TerrainField.WATER_LEVEL)) continue;
                bool clear = true;
                double length = from.DistanceTo(target);
                foreach (ScenePlan.TreeEntry t in near)
                {
                    Vector2 c = PlacesLayout.closest_on_segment(t.position, from, target);
                    double off = c.DistanceTo(t.position);
                    TreeSpecies species = TreeSpecies.by_kind(t.kind);
                    double h = species.height.Y * t.scale;
                    double crown = species.branch_length.Y * h * 0.85;
                    if (off > Math.Max(crown, 0.6)) continue;
                    double line = Mathf.Lerp(eye.Y, (float)TerrainField.WATER_LEVEL, (float)(from.DistanceTo(c) / length));
                    double above = line - field.height(t.position.X, t.position.Y);
                    bool trunk = off < 0.6 * t.scale && above < h;
                    bool leaves = above > species.branch_start * h && above < h;
                    if (trunk || leaves) { clear = false; break; }
                }
                if (clear) seen++;
            }
        }
        return (double)seen / total;
    }
}
