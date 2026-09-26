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

/// Catmull-Rom interpolation with a measured arc-length lookup. Equal steps
/// of u travel equal distances, including within and across control segments.
public partial class Spline
{
    public Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
    public const long BAKE_STEPS = 64;
    public List<double> _distances = new List<double>();
    public double _total = 0.0;

    public Spline(Godot.Collections.Array<Vector3> p_points)
    {
        points = p_points.Duplicate();
        _measure();
    }

    public double total_length()
    {
        return _total;
    }

    public double progress_at_point(long index)
    {
        /// An authored hold remains at its actual control point when the rest of
        /// the path changes length.
        if ((long)points.Count < 2 || _total < 1e-8)
        {
            return 0.0;
        }
        return _distances[(int)(clampi(index, 0, (long)points.Count - 1) * BAKE_STEPS)] / _total;
    }

    public Vector3 _control(long i)
    {
        return points[(int)clampi(i, 0, (long)points.Count - 1)];
    }

    public Vector3 segment_point(long i, double t)
    {
        /// Position on segment `i` at local parameter `t`.
        Vector3 p0 = _control(i - 1);
        Vector3 p1 = _control(i);
        Vector3 p2 = _control(i + 1);
        Vector3 p3 = _control(i + 2);
        double t2 = t * t;
        double t3 = t2 * t;
        return 0.5f * (2.0f * p1 + (-p0 + p2) * (float)t + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * (float)t2 + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * (float)t3);
    }

    public void _measure()
    {
        _distances = new List<double>(new List<double> { 0.0 });
        _total = 0.0;
        for (long i = 0, i_end = (long)points.Count - 1; i < i_end; i++)
        {
            Vector3 prev = segment_point(i, 0.0);
            for (long k = 1, k_end = BAKE_STEPS + 1; k < k_end; k++)
            {
                Vector3 cur = segment_point(i, (double)k / BAKE_STEPS);
                _total += prev.DistanceTo(cur);
                _distances.Add(_total);
                prev = cur;
            }
        }
    }

    public Vector3 sample(double u)
    {
        /// Point at normalised arc length `u` in [0, 1].
        if ((points.Count == 0))
        {
            return Vector3.Zero;
        }
        if ((long)points.Count == 1 || _total < 1e-8)
        {
            return points[0];
        }
        double target = clampf(u, 0.0, 1.0) * _total;
        long low = 0;
        long high = (long)_distances.Count - 1;
        while (high - low > 1)
        {
            long mid = (low + high) / 2;
            if (_distances[(int)mid] < target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }
        double fraction = (target - _distances[(int)low]) / maxf(_distances[(int)high] - _distances[(int)low], 1e-10);
        double parameter = ((double)low + fraction) / BAKE_STEPS;
        long segment = mini(floori(parameter), (long)points.Count - 2);
        return segment_point(segment, clampf(parameter - segment, 0.0, 1.0));
    }
}
