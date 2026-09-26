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

/// CPU twin of shaders/inc/atmosphere.gdshaderinc.
///
/// Pure functions mapping the time of day to sun/moon geometry and evaluating
/// the same single-scattering model the sky shader uses, so that the
/// DirectionalLight colour, fog colour and sky stay physically consistent.
public partial class Atmosphere
{
    public const double PLANET_RADIUS = 6360000.0;
    public const double TOP_RADIUS = 6420000.0;
    public static readonly Vector3 RAYLEIGH_BETA = new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f);
    public const double RAYLEIGH_H = 8000.0;
    public const double MIE_BETA = 3.996e-6;
    public const double MIE_ABSORB = 4.4e-6;
    public const double MIE_H = 1200.0;
    public static readonly Vector3 OZONE_BETA = new Vector3(0.650e-6f, 1.881e-6f, 0.085e-6f);
    public const double OZONE_CENTRE = 25000.0;
    public const double OZONE_WIDTH = 15000.0;

    public const double SUN_INTENSITY = 22.0;
    public const double SUN_ENERGY_SCALE = 4.2;
    public const double MOON_ENERGY = 0.2;
    public static readonly Color MOON_COLOR = new Color(0.62f, 0.72f, 1.0f);

    /// Solar noon (hours). The sun culminates at +62 degrees and sets around 20:15.
    public const double NOON = 13.0;
    public const double SUNRISE = 5.75;
    public const double SUNSET = 20.25;

    public static Vector3 sun_direction(double hour)
    {
        /// Direction *towards* the sun for a given hour of day (0..24).
        double theta = (hour - NOON) / 24.0 * TAU;
        double elevation = deg_to_rad(47.0 * cos(theta) + 15.0);
        double azimuth = deg_to_rad(180.0 + (hour - NOON) * 15.0);
        return _dir_from_angles(azimuth, elevation);
    }

    public static Vector3 moon_direction(double hour)
    {
        /// Direction towards the moon: roughly opposite the sun, offset so both are
        /// never exactly antipodal (which would look artificial).
        double theta = (hour - NOON) / 24.0 * TAU + PI;
        double elevation = deg_to_rad(40.0 * cos(theta) + 12.0);
        double azimuth = deg_to_rad(180.0 + (hour - NOON) * 15.0 + 180.0 - 28.0);
        return _dir_from_angles(azimuth, elevation);
    }

    public static Vector3 _dir_from_angles(double azimuth, double elevation)
    {
        /// Azimuth measured clockwise from north (-Z); elevation above the horizon.
        return new Vector3((float)(sin(azimuth) * cos(elevation)), (float)sin(elevation), (float)(-cos(azimuth) * cos(elevation))).Normalized();
    }

    public static double sun_elevation_deg(double hour)
    {
        return rad_to_deg(asin(clampf(sun_direction(hour).Y, -1.0, 1.0)));
    }

    public static double daylight(double hour)
    {
        /// 0 at deep night, 1 in full daylight, with a smooth twilight band.
        double elev = sun_direction(hour).Y;
        return smoothstep(-0.12, 0.12, elev);
    }

    public static Vector2 _ray_sphere(Vector3 origin, Vector3 dir, double radius)
    {
        // ------------------------------------------------------------- scattering
        double b = origin.Dot(dir);
        double c = origin.Dot(origin) - radius * radius;
        double disc = b * b - c;
        if (disc < 0.0)
        {
            return new Vector2(1e9f, -1e9f);
        }
        double s = sqrt(disc);
        return new Vector2((float)(-b - s), (float)(-b + s));
    }

    public static Vector3 _densities(Vector3 p)
    {
        double h = maxf(p.Length() - PLANET_RADIUS, 0.0);
        double r = exp(-h / RAYLEIGH_H);
        double m = exp(-h / MIE_H);
        double o = maxf(0.0, 1.0 - absf(h - OZONE_CENTRE) / OZONE_WIDTH) * r;
        return new Vector3((float)r, (float)m, (float)o);
    }

    public static Vector3 _extinction(Vector3 d, double haze)
    {
        return RAYLEIGH_BETA * d.X + Vector3.One * (float)((MIE_BETA + MIE_ABSORB) * haze * d.Y) + OZONE_BETA * d.Z;
    }

    public static Vector3 optical_depth(Vector3 p, Vector3 dir, long steps, double haze)
    {
        Vector2 hit = _ray_sphere(p, dir, TOP_RADIUS);
        Vector2 ground = _ray_sphere(p, dir, PLANET_RADIUS);
        if (ground.Y > 0.0 && ground.X > 0.0)
        {
            return Vector3.One * 1e6f;
        }
        double len = maxf(hit.Y, 0.0);
        double ds = len / (double)steps;
        Vector3 depth = Vector3.Zero;
        for (long i = 0; i < steps; i++)
        {
            Vector3 s = p + dir * (float)(((double)i + 0.5) * ds);
            depth += _extinction(_densities(s), haze) * (float)ds;
        }
        return depth;
    }

    public static Vector3 observer(double altitude = 120.0)
    {
        return new Vector3(0.0f, (float)(PLANET_RADIUS + altitude), 0.0f);
    }

    public static Vector3 _exp3(Vector3 v)
    {
        return new Vector3((float)exp(v.X), (float)exp(v.Y), (float)exp(v.Z));
    }

    public static Vector3 transmittance(Vector3 dir, double haze, long steps = 8)
    {
        /// Fraction of sunlight that reaches the observer along `dir`.
        return _exp3(-optical_depth(observer(), dir, steps, haze));
    }

    public static Vector3 scatter(Vector3 dir, Vector3 sun, double haze, long view_steps = 12, long light_steps = 5, double mie_g = 0.78)
    {
        /// In-scattered radiance towards `dir` for the sun in `sun`.
        Vector3 origin = observer();
        Vector2 hit = _ray_sphere(origin, dir, TOP_RADIUS);
        Vector2 ground = _ray_sphere(origin, dir, PLANET_RADIUS);
        double len = hit.Y;
        if (ground.X > 0.0)
        {
            len = minf(len, ground.X);
        }
        len = maxf(len, 0.0);
        double ds = len / (double)view_steps;
        double mu = dir.Dot(sun);
        double phase_r = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
        double g2 = mie_g * mie_g;
        double denom = 1.0 + g2 - 2.0 * mie_g * mu;
        double phase_m = 3.0 / (8.0 * PI) * (1.0 - g2) * (1.0 + mu * mu) / ((2.0 + g2) * pow(maxf(denom, 1e-4), 1.5));
        Vector3 sum_r = Vector3.Zero;
        Vector3 sum_m = Vector3.Zero;
        Vector3 depth = Vector3.Zero;
        for (long i = 0; i < view_steps; i++)
        {
            Vector3 p = origin + dir * (float)(((double)i + 0.5) * ds);
            Vector3 dens = _densities(p);
            depth += _extinction(dens, haze) * (float)ds;
            Vector3 light_depth = optical_depth(p, sun, light_steps, haze);
            Vector3 attenuation = _exp3(-(depth + light_depth));
            sum_r += attenuation * (float)(dens.X * ds);
            sum_m += attenuation * (float)(dens.Y * ds);
        }
        Vector3 rayleigh = new Vector3((float)((double)sum_r.X * RAYLEIGH_BETA.X), (float)((double)sum_r.Y * RAYLEIGH_BETA.Y), (float)((double)sum_r.Z * RAYLEIGH_BETA.Z)) * (float)phase_r;
        Vector3 mie = sum_m * (float)(MIE_BETA * haze * phase_m);
        return (rayleigh + mie) * (float)SUN_INTENSITY;
    }

    public static double luminance(Vector3 c)
    {
        return c.X * 0.2126 + c.Y * 0.7152 + c.Z * 0.0722;
    }

    public static Godot.Collections.Dictionary sun_light(Vector3 sun_dir, double haze)
    {
        /// Colour and energy for the sun DirectionalLight. Energy fades to zero as the
        /// disc dips below the horizon so the moon can take over.
        Vector3 t = transmittance(sun_dir, haze);
        double horizon_fade = smoothstep(-0.02, 0.06, sun_dir.Y);
        double lum = luminance(t);
        double peak = maxf(maxf(t.X, t.Y), maxf(t.Z, 1e-4));
        Color color = new Color((float)(t.X / peak), (float)(t.Y / peak), (float)(t.Z / peak));
        return new Godot.Collections.Dictionary { { (StringName)"color", color }, { (StringName)"energy", lum * SUN_ENERGY_SCALE * horizon_fade } };
    }

    public static Godot.Collections.Dictionary moon_light(Vector3 sun_dir, Vector3 moon_dir)
    {
        double night = smoothstep(0.05, -0.1, sun_dir.Y);
        double up = smoothstep(-0.05, 0.15, moon_dir.Y);
        return new Godot.Collections.Dictionary { { (StringName)"color", MOON_COLOR }, { (StringName)"energy", MOON_ENERGY * night * up } };
    }

    public static Color fog_color(Vector3 sun_dir, double haze)
    {
        /// Average sky colour near the horizon, used for distance fog.
        Vector3 flat = new Vector3(sun_dir.X, 0.0f, sun_dir.Z);
        if (flat.LengthSquared() < 1e-4)
        {
            flat = Vector3.Forward;
        }
        flat = flat.Normalized();
        Vector3 toward = scatter(new Vector3(flat.X, 0.05f, flat.Z).Normalized(), sun_dir, haze, 8, 4);
        Vector3 away = scatter(new Vector3(-flat.X, 0.05f, -flat.Z).Normalized(), sun_dir, haze, 8, 4);
        Vector3 side = scatter(new Vector3(-flat.Z, 0.05f, flat.X).Normalized(), sun_dir, haze, 8, 4);
        Vector3 avg = (toward + away * 1.5f + side * 2.0f) / 4.5f;
        return new Color(avg.X, avg.Y, avg.Z);
    }
}
