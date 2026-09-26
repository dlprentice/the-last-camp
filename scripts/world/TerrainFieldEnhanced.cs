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

/// Art-direction layer over TerrainField. The base field still owns authored
/// pads, trail, pond basin and distant hills; this layer adds broad mid-ground
/// landform structure and varied bank cross-sections while preserving the exact
/// waterline sign. Because Camp receives this field before build(), mesh,
/// collision, pond physics and all scatterers see the same surface.
public partial class TerrainFieldEnhanced : TerrainField
{
    public FastNoiseLite _macro_a = new FastNoiseLite();
    public FastNoiseLite _macro_b = new FastNoiseLite();

    public TerrainFieldEnhanced(long seed_value = 20260902) : base(seed_value)
    {
        _macro_a.Seed = (int)(seed_value + 731);
        _macro_a.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _macro_a.Frequency = 0.0065f;
        _macro_a.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _macro_a.FractalOctaves = 2;
        _macro_a.FractalLacunarity = 2.0f;
        _macro_a.FractalGain = 0.46f;
        _macro_b.Seed = (int)(seed_value + 991);
        _macro_b.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _macro_b.Frequency = 0.013f;
        _macro_b.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _macro_b.FractalOctaves = 2;
        _macro_b.FractalGain = 0.42f;
    }

    public override double height(double x, double z)
    {
        double h = base.height(x, z);
        Vector2 p = new Vector2((float)x, (float)z);
        double distance = p.Length();
        // Broad drainage folds and shoulders only. Never perturb the immediate camp,
        // the travelled trail or the fragile first few decimetres above the pond.
        // The material/detail-normal layers remain responsible for microrelief.

        if (h > WATER_LEVEL + 0.35)
        {
            double radial_envelope = smoothstep(16.0, 42.0, distance) * (1.0 - smoothstep(150.0, 235.0, distance));
            double trail_guard = smoothstep(2.0, 6.5, distance_to_polyline(p, TRAIL));
            double fire_guard = smoothstep(8.0, 15.0, p.DistanceTo(FIRE));
            double tent_guard = smoothstep(6.0, 12.0, p.DistanceTo(TENT));
            // Preserve the low sunset side across the pond; east/north shoulders can
            // carry more relief and make the clearing feel nested into actual terrain.
            double sunset_guard = lerpf(0.34, 1.0, smoothstep(-72.0, -12.0, x));
            double broad = _macro_a.GetNoise2D((float)x, (float)z) * 0.72 + _macro_b.GetNoise2D((float)x, (float)z) * 0.30;
            // Positive shoulders are allowed to rise more than drainage folds cut;
            // that protects the dry-land invariant while creating readable silhouettes.
            broad = broad >= 0.0 ? broad : broad * 0.58;
            broad = broad * 1.8 + sculpted_form(p);
            h += broad * radial_envelope * trail_guard * fire_guard * tent_guard * sunset_guard * smoothstep(WATER_LEVEL + 0.35, WATER_LEVEL + 1.1, h);
        }
        // Alternate subtly steeper and softer bank sectors. Scale the base profile
        // relative to WATER_LEVEL rather than adding height: negative stays negative,
        // positive stays positive, and the authored zero crossing cannot move.

        Vector2 delta = p - POND_CENTRE;
        double pond_distance = delta.Length();
        if (pond_distance > 0.001)
        {
            double angle = atan2(delta.Y, delta.X);
            double s = pond_distance / pond_radius_at(angle);
            if (s > 0.62 && s < 1.30)
            {
                double rel = h - WATER_LEVEL;
                double style = sin(angle * 2.0 + 0.45) * 0.58 + sin(angle * 5.0 - 1.10) * 0.27 + sin(angle * 7.0 + 2.2) * 0.15;
                double bank_band = smoothstep(0.62, 0.82, s) * (1.0 - smoothstep(1.16, 1.30, s));
                double strength = rel < 0.0 ? 0.15 : 0.27;
                double factor = maxf(0.62, 1.0 + style * strength * bank_band);
                h = WATER_LEVEL + rel * factor;
            }
        }
        return h;
    }

    public static double sculpted_form(Vector2 p)
    {
        /// Authored shoulders, not a uniform noise blanket. The valley stays open to
        /// the water; the northern/eastern forest gains several overlapping depth planes.
        double east = _shoulder(p, new Vector2(45, -18), new Vector2(24, 43), 3.8, -0.20);
        double north = _shoulder(p, new Vector2(-1, -68), new Vector2(40, 27), 3.1, 0.17);
        double bank = _shoulder(p, new Vector2(-68, 33), new Vector2(26, 20), 2.0, -0.40);
        double fold = _shoulder(p, new Vector2(23, -39), new Vector2(7, 30), 0.65, -0.65);
        return east + north + bank - fold;
    }

    public static double _shoulder(Vector2 p, Vector2 centre, Vector2 axes, double height, double yaw)
    {
        Vector2 q = (p - centre).Rotated((float)yaw) / axes;
        double distance = q.LengthSquared();
        if (distance >= 1.0)
        {
            return 0.0;
        }
        double support = 1.0 - distance;
        return height * support * support * support;
    }
}
