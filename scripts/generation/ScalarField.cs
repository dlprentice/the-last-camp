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

/// A square grid of floats covering a world-space extent centred on the origin,
/// with bilinear sampling and cheap "painting" of soft discs. Used to rasterise
/// slow-to-evaluate spatial data (canopy coverage, trample maps) once so that
/// per-vertex / per-instance queries stay O(1).
public partial class ScalarField
{
    public long resolution;
    public double extent;
    public List<float> data = new();

    public ScalarField(long p_resolution, double p_extent, double fill = 0.0)
    {
        resolution = maxi(p_resolution, 2);
        extent = p_extent;
        data = new List<float>();
        G.resize(data, (int)(resolution * resolution));
        G.fill(data, (float)fill);
    }

    public double cell_size()
    {
        return extent / (double)(resolution - 1);
    }

    public Vector2 _to_grid(double x, double z)
    {
        double half = extent * 0.5;
        double u = (x + half) / extent * (double)(resolution - 1);
        double v = (z + half) / extent * (double)(resolution - 1);
        return new Vector2((float)u, (float)v);
    }

    public double get_cell(long ix, long iz)
    {
        ix = clampi(ix, 0, resolution - 1);
        iz = clampi(iz, 0, resolution - 1);
        return data[(int)(iz * resolution + ix)];
    }

    public void set_cell(long ix, long iz, double value)
    {
        if (ix < 0 || iz < 0 || ix >= resolution || iz >= resolution)
        {
            return;
        }
        data[(int)(iz * resolution + ix)] = (float)value;
    }

    public double sample(double x, double z)
    {
        /// Bilinear sample at a world position; outside the extent the edge is clamped.
        Vector2 g = _to_grid(x, z);
        double fx = clampf(g.X, 0.0, (double)(resolution - 1));
        double fz = clampf(g.Y, 0.0, (double)(resolution - 1));
        long ix = (long)floor(fx);
        long iz = (long)floor(fz);
        double tx = fx - (double)ix;
        double tz = fz - (double)iz;
        double a = get_cell(ix, iz);
        double b = get_cell(ix + 1, iz);
        double c = get_cell(ix, iz + 1);
        double d = get_cell(ix + 1, iz + 1);
        return lerpf(lerpf(a, b, tx), lerpf(c, d, tx), tz);
    }

    public void paint_disc(double cx, double cz, double inner_radius, double outer_radius, double strength = 1.0)
    {
        /// Paints a smooth disc: full strength inside `inner_radius`, falling to zero at
        /// `outer_radius`. Values combine with `max`, so overlapping discs saturate
        /// instead of exceeding `strength`.
        Vector2 g = _to_grid(cx, cz);
        double cell = cell_size();
        long r_cells = (long)ceil(outer_radius / cell) + 1;
        long gx = (long)round(g.X);
        long gz = (long)round(g.Y);
        for (long iz = gz - r_cells, iz_end = gz + r_cells + 1; iz < iz_end; iz++)
        {
            if (iz < 0 || iz >= resolution)
            {
                continue;
            }
            for (long ix = gx - r_cells, ix_end = gx + r_cells + 1; ix < ix_end; ix++)
            {
                if (ix < 0 || ix >= resolution)
                {
                    continue;
                }
                double dx = ((double)ix - g.X) * cell;
                double dz = ((double)iz - g.Y) * cell;
                double d = sqrt(dx * dx + dz * dz);
                double w = 1.0 - smoothstep(inner_radius, outer_radius, d);
                if (w <= 0.0)
                {
                    continue;
                }
                long idx = iz * resolution + ix;
                data[(int)idx] = (float)maxf(data[(int)idx], w * strength);
            }
        }
    }

    public void fill_with(Callable fn)
    {
        /// Fills the grid by evaluating `fn(x, z) -> float` at every cell.
        double half = extent * 0.5;
        double cell = cell_size();
        for (long iz = 0; iz < resolution; iz++)
        {
            double z = -half + (double)iz * cell;
            for (long ix = 0; ix < resolution; ix++)
            {
                double x = -half + (double)ix * cell;
                data[(int)(iz * resolution + ix)] = fn.Call(x, z).AsSingle();
            }
        }
    }

    public double max_value()
    {
        double m = -INF;
        foreach (float v in data)
        {
            m = maxf(m, v);
        }
        return m;
    }
}
