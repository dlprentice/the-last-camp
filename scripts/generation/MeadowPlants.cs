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

/// Temperate summer meadow plants, modelled at their real scale. Small curved
/// petals hold up in a close dolly without the crossed-card flower illusion.
public partial class MeadowPlants
{
    public enum Kind
    {
        YARROW,
        BUTTERCUP,
        CLOVER,
        DAISY,
    }

    public static ArrayMesh flower(long kind, long seed_value)
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        Color green = new Color(0.06f, 0.18f, 0.045f);
        double h = new Godot.Collections.Array { 0.64, 0.50, 0.36, 0.61 }[(int)kind].AsDouble();
        for (long stalk = 0, stalk_end = kind == (long)MeadowPlants.Kind.CLOVER ? 2 : 3; stalk < stalk_end; stalk++)
        {
            double a = (double)stalk * 2.39996 + rng.Randf();
            Vector3 @out = new Vector3((float)cos(a), 0, (float)sin(a));
            Vector3 head = @out * rng.RandfRange(0.035f, 0.12f) + Vector3.Up * (float)h * rng.RandfRange(0.70f, 1.0f);
            mb.add_tube(new Godot.Collections.Array<Vector3> { Vector3.Zero, head * 0.55f - @out * 0.02f, head }, new Godot.Collections.Array<double> { 0.0025, 0.0018, 0.001 }, 4, green);
            for (long k = 0; k < 3; k++)
            {
                Vector3 origin = head * (float)(0.20 + (double)k * 0.16);
                Vector3 dir = @out.Rotated(Vector3.Up, (float)((double)k * 2.39));
                if (kind == (long)MeadowPlants.Kind.YARROW)
                {
                    for (long leaflet = 0; leaflet < 8; leaflet++)
                    {
                        double t = (double)leaflet / 8.0;
                        Vector3 at = origin + dir * (float)t * 0.085f + Vector3.Up * (float)t * 0.018f;
                        foreach (Variant sign_value_item in new Godot.Collections.Array { -1.0, 1.0 })
                        {
                            double sign_value = sign_value_item.AsDouble();
                            _petal(mb, at, dir.Rotated(Vector3.Up, (float)(sign_value * 0.95)), 0.022 * (1.0 - t * 0.65), 0.003, 0.003, green);
                        }
                    }
                }
                else if (kind == (long)MeadowPlants.Kind.CLOVER)
                {
                    for (long leaflet2 = 0; leaflet2 < 3; leaflet2++)
                    {
                        _petal(mb, origin + dir * 0.025f, dir.Rotated(Vector3.Up, (float)((leaflet2 - 1) * 1.05)), 0.028, 0.026, 0.004, green);
                    }
                }
                else
                {
                    _petal(mb, origin, dir, 0.065, 0.016, 0.017, green);
                }
            }
            long bloom_start = mb.vertex_count();
            switch (kind)
            {
                case (long)MeadowPlants.Kind.YARROW:
                    for (long lobe = 0; lobe < 7; lobe++)
                    {
                        Vector3 centre = head + new Vector3((float)cos((double)lobe * 2.4), rng.RandfRange(-0.08f, 0.16f), (float)sin((double)lobe * 2.4)) * rng.RandfRange(0.019f, 0.029f);
                        mb.add_tube(new Godot.Collections.Array<Vector3> { head - Vector3.Up * 0.035f, centre }, new Godot.Collections.Array<double> { 0.001, 0.0005 }, 3, green);
                        for (long floret = 0; floret < 6; floret++)
                        {
                            Vector3 p = centre + new Vector3((float)cos((double)floret * 2.4), 0, (float)sin((double)floret * 2.4)) * 0.007f;
                            _yarrow_floret(mb, p);
                        }
                    }
                    break;
                case (long)MeadowPlants.Kind.CLOVER:
                    _flower_core(mb, head + Vector3.Up * 0.014f, 0.015, 1.0, new Color(0.38f, 0.09f, 0.19f));
                    for (long floret2 = 0; floret2 < 64; floret2++)
                    {
                        double a2 = (double)floret2 * 2.39996;
                        double y = (double)floret2 / 64.0;
                        double r = sqrt(1.0 - pow(y * 2.0 - 1.0, 2.0)) * 0.018;
                        Vector3 p2 = head + new Vector3((float)(cos(a2) * r), (float)(y * 0.028), (float)(sin(a2) * r));
                        _petal(mb, p2, new Vector3((float)cos(a2), 0.6f, (float)sin(a2)).Normalized(), 0.009, 0.003, 0.004, new Color(0.48f, 0.14f, 0.28f).Lerp(new Color(0.72f, 0.38f, 0.47f), (float)y));
                    }
                    break;
                default:
                    long count = kind == (long)MeadowPlants.Kind.BUTTERCUP ? 5 : 13;
                    Color color = kind == (long)MeadowPlants.Kind.BUTTERCUP ? new Color(0.84f, 0.54f, 0.035f) : new Color(0.83f, 0.81f, 0.68f);
                    for (long petal = 0; petal < count; petal++)
                    {
                        Vector3 dir2 = new Vector3((float)cos((double)petal / count * TAU), 0, (float)sin((double)petal / count * TAU));
                        _petal(mb, head, dir2, (count == 5 ? 0.021 : 0.03) * rng.RandfRange(0.88f, 1.12f), count == 5 ? 0.016 : 0.006, count == 5 ? rng.RandfRange(0.012f, 0.018f) : rng.RandfRange(-0.002f, 0.006f), color);
                    }
                    _flower_core(mb, head + Vector3.Up * 0.005f, 0.0075, count == 5 ? 0.80 : 0.48, new Color(0.72f, 0.44f, 0.055f));
                    break;
            }
            // Stems do not all hold their blooms exactly level. Preserve yarrow's
            // corymb, with stronger nods and differing cups in individual daisies.
            Basis tilt = new Basis(@out.Rotated(Vector3.Up, (float)(PI * 0.5)), kind == (long)MeadowPlants.Kind.YARROW ? rng.RandfRange(0.04f, 0.16f) : rng.RandfRange(0.08f, 0.40f));
            for (long i2 = bloom_start, i_end = mb.vertex_count(); i2 < i_end; i2++)
            {
                mb.vertices[(int)i2] = head + tilt * (mb.vertices[(int)i2] - head);
                mb.normals[(int)i2] = tilt * mb.normals[(int)i2];
            }
        }

        mb.recompute_normals();
        return mb.commit(null, true);
    }

    public static void _yarrow_floret(MeshBuilder mb, Vector3 p)
    {
        /// Millimetre-scale yarrow florets need a scalloped, gently convex surface,
        /// not hundreds of strip triangles per bloom. This keeps the meadow cheap.
        long center = mb.add_vertex(p + Vector3.Up * 0.001f, Vector3.Up, Vector2.One * 0.5f, new Color(0.70f, 0.66f, 0.48f));
        for (long i = 0; i < 16; i++)
        {
            double a = (double)i / 15.0 * TAU;
            double r = 0.0047 * (0.86 + 0.14 * cos(a * 5.0));
            // Off-white with a green cast: pure white florets bloomed into paper discs.
            mb.add_vertex(p + new Vector3((float)(cos(a) * r), 0, (float)(sin(a) * r)), Vector3.Up, new Vector2((float)cos(a), (float)sin(a)) * 0.5f + Vector2.One * 0.5f, new Color(0.72f, 0.72f, 0.58f));
            if (i > 0)
            {
                mb.add_triangle(center, center + i, center + i + 1);
            }
        }
    }

    public static void _flower_core(MeshBuilder mb, Vector3 at, double radius, double vertical, Color color)
    {
        long start = mb.vertex_count();
        mb.add_displaced_sphere(8, 20, radius, Callable.From((Vector3 _dir) => 1.0), color);
        for (long i = start, i_end = mb.vertex_count(); i < i_end; i++)
        {
            Vector3 _t1 = mb.vertices[(int)i];
            _t1.Y = (float)(mb.vertices[(int)i].Y * vertical);
            mb.vertices[(int)i] = _t1;
            mb.vertices[(int)i] += at;
            mb.normals[(int)i] = (mb.normals[(int)i] / new Vector3(1, (float)vertical, 1)).Normalized();
        }
    }

    public static void _petal(MeshBuilder mb, Vector3 p, Vector3 @out, double length, double width, double lift, Color color)
    {
        Vector3 side = @out.Cross(Vector3.Up).Normalized();
        if (side.LengthSquared() < 0.01)
        {
            side = Vector3.Right;
        }
        List<int> prev = new List<int>();
        for (long row = 0; row < 7; row++)
        {
            double t = (double)row / 6.0;
            Vector3 centre = p + @out * (float)t * (float)length + Vector3.Up * (float)lift * (float)sin(t * PI * 0.8);
            double w = width * (0.015 + pow(maxf(sin(t * PI), 0.0), 0.7) * 0.985) * 0.5;
            Vector3 curl = Vector3.Up * (float)sin(t * PI) * (float)width * 0.12f;
            long left = mb.add_vertex(centre - side * (float)w + curl, Vector3.Up, new Vector2(0, (float)t), color);
            long mid = mb.add_vertex(centre, Vector3.Up, new Vector2(0.5f, (float)t), color);
            long right = mb.add_vertex(centre + side * (float)w + curl, Vector3.Up, new Vector2(1, (float)t), color);
            if (row > 0)
            {
                mb.add_quad_indices(prev[0], left, mid, prev[1]);
                mb.add_quad_indices(prev[1], mid, right, prev[2]);
            }
            prev = new List<int>(new List<int> { (int)left, (int)mid, (int)right });
        }
    }

    public static ArrayMesh seed_heads(long kind = 0, long seed_value = 177)
    {
        /// Loose, whorled panicles and compact tapered heads sit above the basal
        /// grass leaves. Fine terminal spikelets leave air between the branches;
        /// enlarged flower petals would turn the panicle into a solid ornament.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        Color stalk_color = new Color(0.24f, 0.29f, 0.11f);
        Color seed_color = new Color(0.52f, 0.49f, 0.30f);
        for (long stem = 0; stem < 3; stem++)
        {
            double a = (double)stem * 2.39996 + rng.RandfRange(-0.18f, 0.18f);
            Vector3 @out = new Vector3((float)cos(a), 0, (float)sin(a));
            double h = rng.RandfRange(0.69f, 0.96f);
            Vector3 head = @out * rng.RandfRange(0.07f, 0.14f) + Vector3.Up * (float)h;
            mb.add_tube(new Godot.Collections.Array<Vector3> { Vector3.Zero, head * 0.42f - @out * 0.03f, head }, new Godot.Collections.Array<double> { 0.0017, 0.0012, 0.00065 }, 4, stalk_color);
            for (long leaf = 0; leaf < 2; leaf++)
            {
                Vector3 origin = head * (float)(0.28 + (double)leaf * 0.21);
                _petal(mb, origin, @out.Rotated(Vector3.Up, (float)((double)leaf * 2.39996)), 0.13, 0.0045, 0.031, stalk_color);
            }
            if (kind == 0)
            {
                Vector3 crown = head + Vector3.Up * 0.22f + @out * 0.015f;
                mb.add_tube(new Godot.Collections.Array<Vector3> { head, crown }, new Godot.Collections.Array<double> { 0.00065, 0.00025 }, 3, stalk_color);
                for (long tier = 0; tier < 4; tier++)
                {
                    double t = (double)tier / 4.0;
                    Vector3 at = head.Lerp(crown, (float)t);
                    for (long branch = 0; branch < 3; branch++)
                    {
                        double angle = a + (double)branch * TAU / 3.0 + (double)tier * 0.77;
                        Vector3 dir = new Vector3((float)cos(angle), rng.RandfRange(0.35f, 0.75f), (float)sin(angle)).Normalized();
                        Vector3 tip = at + dir * (float)(0.13 - t * 0.10) * rng.RandfRange(0.88f, 1.12f);
                        mb.add_tube(new Godot.Collections.Array<Vector3> { at, tip }, new Godot.Collections.Array<double> { 0.00050, 0.00024 }, 3, stalk_color);
                        for (long spike = 0; spike < 2; spike++)
                        {
                            Vector3 p = at.Lerp(tip, (float)(0.70 + (double)spike * 0.30));
                            _seed_spikelet(mb, p, (dir + Vector3.Up * 0.4f).Normalized(), 0.012, 0.0032, seed_color);
                        }
                    }
                }
                _seed_spikelet(mb, crown, Vector3.Up, 0.011, 0.0026, seed_color);
            }
            else
            {
                Vector3 crown2 = head + Vector3.Up * 0.13f + @out * 0.012f;
                mb.add_tube(new Godot.Collections.Array<Vector3> { head, crown2 }, new Godot.Collections.Array<double> { 0.00065, 0.00024 }, 3, stalk_color);
                for (long tier2 = 0; tier2 < 7; tier2++)
                {
                    double t2 = (double)tier2 / 7.0;
                    for (long spike2 = 0; spike2 < 3; spike2++)
                    {
                        double angle2 = a + (double)spike2 * TAU / 3.0 + (double)tier2 * 2.39996;
                        Vector3 dir2 = new Vector3((float)(cos(angle2) * 0.36), 1.0f, (float)(sin(angle2) * 0.36)).Normalized();
                        _seed_spikelet(mb, head.Lerp(crown2, (float)t2), dir2, 0.023 * (1.0 - t2 * 0.48), 0.0045 * (1.0 - t2 * 0.55), seed_color);
                    }
                }
            }
        }
        return mb.commit(null, true);
    }

    public static void _seed_spikelet(MeshBuilder mb, Vector3 at, Vector3 direction, double length, double width, Color color)
    {
        /// Two tiny folded blades keep a seed visible from oblique views without
        /// alpha cards or the vertex cost of a full seven-row flower petal.
        Vector3 side = direction.Cross(Vector3.Right).Normalized();
        if (side.LengthSquared() < 0.01)
        {
            side = Vector3.Forward;
        }
        for (long fold = 0; fold < 2; fold++)
        {
            Vector3 across = side.Rotated(direction, (float)((double)fold * PI * 0.5));
            Vector3 mid = at + direction * (float)length * 0.42f;
            mb.add_quad(at, mid + across * (float)width * 0.5f, at + direction * (float)length, mid - across * (float)width * 0.5f, color);
        }
    }
}
