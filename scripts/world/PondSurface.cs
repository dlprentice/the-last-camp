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

/// CPU companion to inc/pond_waves.gdshaderinc. Physics queries the same
/// analytic surface as rendering; the GPU adds its sampled residual height.
public partial class PondSurface
{
    public const double WAVE_HEIGHT = 0.012;
    public const double WAVE_LENGTH = 3.2;
    public const double RESIDUAL_BOUND = 0.055;

    public static Vector3 displacement(Vector2 p, double time, Vector2 wind, double strength)
    {
        Vector2 direction = (wind + Vector2.One * 0.0001f).Normalized();
        Vector3 result = Vector3.Zero;
        for (long i = 0; i < 3; i++)
        {
            double fi = (double)i;
            Vector2 d = (direction + new Vector2((float)sin(fi * 2.1), (float)cos(fi * 1.7)) * 0.55f).Normalized();
            double k = TAU / (WAVE_LENGTH * (1.0 - fi * 0.28));
            double c = sqrt(9.8 / k);
            double a = WAVE_HEIGHT * (1.0 - fi * 0.3) * (0.4 + 0.6 * clampf(strength, 0.0, 1.5));
            double phase = k * (d.Dot(p) - c * time);
            result += new Vector3((float)(d.X * a * cos(phase) * 0.6), (float)(a * sin(phase)), (float)(d.Y * a * cos(phase) * 0.6));
        }
        return result;
    }

    public static double height_at(Vector2 p, double time, Vector2 wind, double strength)
    {
        Vector3 first = displacement(p, time, wind, strength);
        return TerrainField.WATER_LEVEL + displacement(p - new Vector2(first.X, first.Z), time, wind, strength).Y;
    }

    public static double buoyancy(double immersion, double vertical_speed, double mass, double draft, long points)
    {
        // Archimedes force for the probe's effective submerged column. The waterplane
        // area is calibrated to the boat's resting draft. Drag opposes point velocity.
        double area = mass / (1000.0 * draft * (double)points);
        double stiffness = 1000.0 * 9.8 * area;
        double damping = 1.4 * sqrt(stiffness * mass / (double)points);
        double wetted = clampf(immersion / draft, 0.0, 1.0);
        return maxf(0.0, stiffness * clampf(immersion, 0.0, draft * 3.0) - damping * vertical_speed * wetted);
    }
}
