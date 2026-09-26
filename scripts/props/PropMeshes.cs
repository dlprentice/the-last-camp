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

/// Shared procedural meshes for camp props. Deterministic for a given seed so
/// captures and tests stay stable.
public partial class PropMeshes
{
    public const long WOOD_SIDES = 8;

    public static ArrayMesh log_mesh(double length, double radius, long seed_value, double bend = 0.08)
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        double half = length * 0.5;
        Vector3 lean = new Vector3(rng.RandfRange(-1.0f, 1.0f), 0.0f, rng.RandfRange(-1.0f, 1.0f));
        if (lean.LengthSquared() < 1e-6)
        {
            lean = Vector3.Right;
        }
        lean = lean.Normalized() * (float)bend * (float)length;
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3> { new Vector3((float)-half, 0.0f, 0.0f), new Vector3(0.0f, (float)((double)lean.Y + rng.RandfRange(-0.02f, 0.04f)), (float)(lean.Z * 0.5)), new Vector3((float)half, 0.0f, 0.0f) };
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double> { radius * rng.RandfRange(0.92f, 1.08f), radius * rng.RandfRange(0.88f, 1.0f), radius * rng.RandfRange(0.9f, 1.06f) };
        mb.add_tube(points, radii, WOOD_SIDES, Colors.White, 1.0, 1.4, rng.Randf(), true);
        return mb.commit(null, true);
    }

    public static ArrayMesh bark_log_mesh(double length, double radius, long seed_value, double bend = 0.05, double flat_top = 1.0)
    {
        /// Round timber with bark on the outside and sawn end grain: two surfaces,
        /// bark first (bark shader, v along the log) then the ends (wood shader with
        /// concentric-ring UVs). `flat_top` < 1 hews the upper side flat for a seat.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        double half = length * 0.5;
        double flat_y = radius * flat_top;
        MeshBuilder bark = new MeshBuilder();
        long segments = 5;
        long sides = 12;
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        Vector3 lean = new Vector3(0.0f, rng.RandfRange(-1.0f, 1.0f), rng.RandfRange(-1.0f, 1.0f)).Normalized() * (float)bend * (float)length;
        for (long i = 0, i_end = segments + 1; i < i_end; i++)
        {
            double t = (double)i / (double)segments;
            points.Add(new Vector3((float)(-half + length * t), 0.0f, 0.0f) + lean * (float)sin(t * PI));
            radii.Add(radius * rng.RandfRange(0.93f, 1.07f));
        }
        bark.add_tube(points, radii, sides, Colors.White, 1.0, 1.0 / 1.2, rng.Randf(), false);
        if (flat_top < 1.0)
        {
            for (long i2 = 0, i_end2 = (long)bark.vertices.Count; i2 < i_end2; i2++)
            {
                Vector3 v = bark.vertices[(int)i2];
                if (v.Y > flat_y)
                {
                    bark.vertices[(int)i2] = new Vector3(v.X, (float)flat_y, v.Z);
                }
            }
            bark.recompute_normals();
        }
        ArrayMesh mesh = bark.commit(null, true);

        MeshBuilder ends = new MeshBuilder();
        if (flat_top < 1.0)
        {
            // Sawn face along the top, a hair above the clipped bark.
            double chord = radius * sqrt(maxf(1.0 - flat_top * flat_top, 0.0)) * 0.98;
            long strip = (long)rng.Randi() % 4;
            double u0 = (double)strip * 0.25 + 0.02;
            double y = flat_y + 0.004;
            long quads = 6;
            for (long q = 0; q < quads; q++)
            {
                double t0 = (double)q / (double)quads;
                double t1 = (double)(q + 1) / (double)quads;
                Vector3 p0 = points[0].Lerp(points[(int)((long)points.Count - 1)], (float)t0);
                Vector3 p1 = points[0].Lerp(points[(int)((long)points.Count - 1)], (float)t1);
                ends.add_quad(new Vector3(p0.X, (float)y, (float)-chord), new Vector3(p1.X, (float)y, (float)-chord), new Vector3(p1.X, (float)y, (float)chord), new Vector3(p0.X, (float)y, (float)chord), new Color(0.98f, 0.92f, 0.8f), new Color(0, 0, 0, 0), new Rect2((float)(u0 + 0.02), (float)(t0 * length), 0.18f, (float)((t1 - t0) * length)));
            }
        }
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            Vector3 centre = side < 0.0 ? points[0] : points[(int)((long)points.Count - 1)];
            double r = side < 0.0 ? radii[0] : radii[(int)((long)radii.Count - 1)];
            Vector3 n = new Vector3((float)side, 0.0f, 0.0f);
            long strip2 = (long)rng.Randi() % 4;
            double u02 = (double)strip2 * 0.25 + 0.02;
            long ci = ends.add_vertex(centre, n, new Vector2((float)u02, 0.0f), new Color(0.95f, 0.9f, 0.82f), new Vector4(0.0f, 0.0f, (float)side, 1.0f));
            Godot.Collections.Array<long> rim = new Godot.Collections.Array<long>();
            for (long s = 0, s_end = sides + 1; s < s_end; s++)
            {
                double a = (double)s / (double)sides * TAU;
                Vector3 p = centre + new Vector3(0.0f, (float)minf(sin(a) * r, flat_y), (float)(cos(a) * r));
                double ring = (p - centre).Length() / r;
                rim.Add(ends.add_vertex(p, n, new Vector2((float)(u02 + ring * 0.2), (float)(a / TAU * 3.0)), new Color(0.95f, 0.9f, 0.82f), new Vector4(0.0f, 0.0f, (float)side, 1.0f)));
            }
            for (long s2 = 0; s2 < sides; s2++)
            {
                if (side < 0.0)
                {
                    ends.add_triangle(ci, rim[(int)s2], rim[(int)(s2 + 1)]);
                }
                else
                {
                    ends.add_triangle(ci, rim[(int)(s2 + 1)], rim[(int)s2]);
                }
            }
        }
        ends.commit(null, false, mesh);
        return mesh;
    }

    public static ArrayMesh split_log_mesh(double length, double radius, long seed_value)
    {
        /// Hand-split firewood: an irregular bark sector and two fractured inner
        /// faces. Every surface shares the same ring positions, including the ends.
        /// The grain runs along local X; the pointed split edge faces approximately +Y.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        double half = length * 0.5;
        long SIDES = 6;
        long RINGS = 5;
        double arc = rng.RandfRange(2.02f, 2.65f);
        double taper = rng.RandfRange(-0.09f, 0.09f);
        double split_offset = rng.RandfRange(-0.14f, 0.14f);
        Vector2 bow = new Vector2(rng.RandfRange(-0.035f, 0.035f), rng.RandfRange(-0.035f, 0.035f)) * (float)radius;
        List<List<Vector3>> profiles = new List<List<Vector3>>();
        for (long ring = 0; ring < RINGS; ring++)
        {
            double t = (double)ring / (double)(RINGS - 1);
            Vector3 centre = new Vector3((float)lerpf(-half, half, t), (float)(bow.X * sin(t * PI)), (float)(bow.Y * sin(t * PI)));
            double r = radius * (1.0 + taper * (t - 0.5)) * rng.RandfRange(0.97f, 1.03f);
            List<Vector3> points = new List<Vector3>();
            for (long s = 0, s_end = SIDES + 1; s < s_end; s++)
            {
                double angle = PI * 1.5 + ((double)s / SIDES - 0.5) * arc;
                double irregular = s == 0 || s == SIDES ? 1.0 : rng.RandfRange(0.97f, 1.03f);
                points.Add(centre + new Vector3(0.0f, (float)(sin(angle) * r * irregular), (float)(cos(angle) * r * irregular)));
            }
            // A slightly wandering cleft makes two split faces, not one broad shelf.
            points.Add(centre + new Vector3(0.0f, (float)(r * rng.RandfRange(0.04f, 0.13f)), (float)(r * split_offset)));
            profiles.Add(points);
        }
        MeshBuilder bark = new MeshBuilder();
        MeshBuilder wood = new MeshBuilder();
        long strip = (long)rng.Randi() % 4;
        double u0 = (double)strip * 0.25 + 0.02;
        double v0 = rng.Randf();
        Color timber_tint = new Color(0.98f, 0.94f, 0.86f) * rng.RandfRange(0.88f, 1.03f);
        for (long ring2 = 0, ring_end = RINGS - 1; ring2 < ring_end; ring2++)
        {
            List<Vector3> a = profiles[(int)ring2];
            List<Vector3> b = profiles[(int)(ring2 + 1)];
            double along = (double)ring2 / (double)(RINGS - 1) * length;
            double run = length / (double)(RINGS - 1);
            for (long s2 = 0; s2 < SIDES; s2++)
            {
                _firewood_quad(bark, a[(int)s2], b[(int)s2], b[(int)(s2 + 1)], a[(int)(s2 + 1)], Colors.White, new Rect2((float)((double)s2 / SIDES * 0.45), (float)(along / 1.2), (float)(0.45 / SIDES), (float)(run / 1.2)));
            }
            // Preserve the same endpoints as the bark and cap; no radius mismatch
            // can leave a hairline opening along the edge of a split piece.
            _firewood_quad(wood, a[(int)(SIDES + 1)], b[(int)(SIDES + 1)], b[0], a[0], timber_tint, new Rect2((float)u0, (float)(v0 + along), 0.105f, (float)run));
            _firewood_quad(wood, a[(int)SIDES], b[(int)SIDES], b[(int)(SIDES + 1)], a[(int)(SIDES + 1)], timber_tint, new Rect2((float)(u0 + 0.105), (float)(v0 + along), 0.105f, (float)run));
        }
        for (long end = 0; end < 2; end++)
        {
            List<Vector3> points2 = profiles[(int)(end == 0 ? 0 : RINGS - 1)];
            Vector3 centre2 = Vector3.Zero;
            foreach (Vector3 point in points2)
            {
                centre2 += point;
            }
            centre2 /= (float)(double)(long)points2.Count;
            double side = end == 0 ? -1.0 : 1.0;
            Vector3 n = new Vector3((float)side, 0.0f, 0.0f);
            long ci = wood.add_vertex(centre2, n, new Vector2((float)u0, 0.0f), timber_tint, new Vector4(0.0f, 0.0f, (float)side, 1.0f));
            Godot.Collections.Array<long> rim = new Godot.Collections.Array<long>();
            foreach (Vector3 point2 in points2)
            {
                Vector2 direction = new Vector2((float)((double)point2.Z - centre2.Z), (float)((double)point2.Y - centre2.Y));
                rim.Add(wood.add_vertex(point2, n, new Vector2((float)(u0 + minf(direction.Length() / radius, 1.0) * 0.2), (float)(atan2(direction.Y, direction.X) / TAU * 3.0)), timber_tint, new Vector4(0.0f, 0.0f, (float)side, 1.0f)));
            }
            for (long s3 = 0, s_end2 = (long)rim.Count; s3 < s_end2; s3++)
            {
                long next = (s3 + 1) % (long)rim.Count;
                if ((points2[(int)s3] - centre2).Cross(points2[(int)next] - centre2).Dot(n) < 0.0)
                {
                    wood.add_triangle(ci, rim[(int)s3], rim[(int)next]);
                }
                else
                {
                    wood.add_triangle(ci, rim[(int)next], rim[(int)s3]);
                }
            }
        }
        bark.recompute_tangents();
        wood.recompute_tangents();
        ArrayMesh mesh = bark.commit(null, false);
        wood.commit(null, false, mesh);
        return mesh;
    }

    public static void _firewood_quad(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint, Rect2 uv)
    {
        /// These strips run lengthwise along p0-p1: V follows the scanned wood grain.
        long first = builder.vertex_count();
        builder.add_quad(a, b, c, d, tint);
        builder.uvs[(int)first] = uv.Position;
        builder.uvs[(int)(first + 1)] = new Vector2(uv.Position.X, uv.End.Y);
        builder.uvs[(int)(first + 2)] = uv.End;
        builder.uvs[(int)(first + 3)] = new Vector2(uv.End.X, uv.Position.Y);
    }

    public static ArrayMesh stump_mesh(double radius, double height, long seed_value)
    {
        /// A chopping block: a short upright round with ringed top grain.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder bark = new MeshBuilder();
        long sides = 14;
        bark.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, -0.1f, 0.0f), new Vector3(0.0f, (float)(height * 0.5), 0.0f), new Vector3(0.0f, (float)height, 0.0f) }, new Godot.Collections.Array<double> { radius * 1.08, radius, radius * 0.97 }, sides, Colors.White, 1.0, 1.0 / 1.2, rng.Randf(), false);
        ArrayMesh mesh = bark.commit(null, true);
        MeshBuilder top = new MeshBuilder();
        double u0 = (double)((long)rng.Randi() % 4) * 0.25 + 0.02;
        Vector3 centre = new Vector3(0.0f, (float)height, 0.0f);
        long ci = top.add_vertex(centre, Vector3.Up, new Vector2((float)u0, 0.0f), new Color(0.95f, 0.9f, 0.82f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
        Godot.Collections.Array<long> rim = new Godot.Collections.Array<long>();
        for (long s = 0, s_end = sides + 1; s < s_end; s++)
        {
            double a = (double)s / (double)sides * TAU;
            rim.Add(top.add_vertex(centre + new Vector3((float)(cos(a) * radius * 0.97), 0.0f, (float)(sin(a) * radius * 0.97)), Vector3.Up, new Vector2((float)(u0 + 0.2), (float)(a / TAU * 4.0)), new Color(0.95f, 0.9f, 0.82f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
        }
        for (long s2 = 0; s2 < sides; s2++)
        {
            top.add_triangle(ci, rim[(int)s2], rim[(int)(s2 + 1)]);
        }
        top.commit(null, false, mesh);
        return mesh;
    }

    public static ArrayMesh axe_mesh()
    {
        /// A felling axe: hickory handle and a wedge head, origin at the head's edge.
        MeshBuilder handle = new MeshBuilder();
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        for (long i = 0; i < 19; i++)
        {
            double t = (double)i / 18.0;
            points.Add(new Vector3(0, (float)(t * 0.72), (float)(sin(t * PI) * 0.018 - t * t * 0.022)));
            radii.Add(0.016 + sin(t * PI) * 0.0025 + pow(t, 8.0) * 0.006);
        }
        handle.add_tube(points, radii, 14, new Color(0.92f, 0.85f, 0.7f), 1, 0.8, 0, true);
        for (long i2 = 0, i_end = handle.vertex_count(); i2 < i_end; i2++)
        {
            Vector3 _t1 = handle.vertices[(int)i2];
            _t1.X = (float)(handle.vertices[(int)i2].X * 0.78);
            handle.vertices[(int)i2] = _t1;
        }
        ArrayMesh mesh = handle.commit(null, true);
        MeshBuilder head = new MeshBuilder();
        // Curved cutting edge, narrowed cheek and poll around the handle eye.
        // The silhouette is extruded with a thickness taper towards the bit.
        List<Vector2> outline = new List<Vector2>(new List<Vector2> { new Vector2(-0.045f, -0.007f), new Vector2(-0.045f, 0.065f), new Vector2(0.005f, 0.064f), new Vector2(0.07f, 0.079f), new Vector2(0.125f, 0.108f), new Vector2(0.148f, 0.074f), new Vector2(0.156f, 0.034f), new Vector2(0.148f, -0.007f), new Vector2(0.13f, -0.034f), new Vector2(0.06f, -0.002f) });
        List<int> triangles = new List<int>(Geometry2D.TriangulatePolygon(outline.ToArray()));
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            long start = head.vertex_count();
            foreach (Vector2 p in outline)
            {
                double thickness = lerpf(0.024, 0.0012, smoothstep(0.015, 0.145, p.X));
                Color color = new Color(0.35f, 0.37f, 0.38f).Lerp(new Color(0.83f, 0.85f, 0.84f), (float)smoothstep(0.105, 0.145, p.X));
                head.add_vertex(new Vector3((float)(side * thickness), p.Y, p.X), Vector3.Right * (float)side, p * 4.0f, color);
            }
            for (long i4 = 0, i_end2 = (long)triangles.Count; i4 < i_end2; i4 += 3)
            {
                long x = start + triangles[(int)i4];
                long y = start + triangles[(int)(i4 + 1)];
                long z = start + triangles[(int)(i4 + 2)];
                if ((head.vertices[(int)y] - head.vertices[(int)x]).Cross(head.vertices[(int)z] - head.vertices[(int)x]).X * side > 0)
                {
                    head.add_triangle(x, z, y);
                }
                else
                {
                    head.add_triangle(x, y, z);
                }
            }
        }
        for (long i5 = 0, i_end3 = (long)outline.Count; i5 < i_end3; i5++)
        {
            long next = (i5 + 1) % (long)outline.Count;
            Vector3 n = new Vector3(0, (float)((double)outline[(int)next].X - outline[(int)i5].X), (float)((double)outline[(int)i5].Y - outline[(int)next].Y)).Normalized();
            head.add_quad_facing(i5, next, next + (long)outline.Count, i5 + (long)outline.Count, n);
        }
        head.recompute_normals();
        head.commit(null, false, mesh);
        return mesh;
    }

    /// Metal parts of a hurricane lantern (base, tank, cage, cap, bail), origin at
    /// the bottom of the base. The bail's top sits at HURRICANE_HEIGHT.
    public const double HURRICANE_HEIGHT = 0.5;

    public static ArrayMesh hurricane_metal()
    {
        MeshBuilder mb = new MeshBuilder();
        Color grey = new Color(0.9f, 0.9f, 0.9f);
        Color dark = new Color(0.55f, 0.52f, 0.5f);
        // Fuel font: a squat can with a domed shoulder, a filler cap on one side.
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.012f, 0.0f), new Vector3(0.0f, 0.06f, 0.0f), new Vector3(0.0f, 0.095f, 0.0f), new Vector3(0.0f, 0.108f, 0.0f) }, new Godot.Collections.Array<double> { 0.06, 0.066, 0.066, 0.05, 0.03 }, 16, grey, 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.042f, 0.092f, 0.0f), new Vector3(0.042f, 0.106f, 0.0f) }, new Godot.Collections.Array<double> { 0.011, 0.012 }, 8, grey, 1.0, 2.0, 0.0, true);
        // Burner collar and the wick tube inside the globe, with the raiser knob.
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.108f, 0.0f), new Vector3(0.0f, 0.126f, 0.0f) }, new Godot.Collections.Array<double> { 0.03, 0.028 }, 10, grey, 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.126f, 0.0f), new Vector3(0.0f, 0.15f, 0.0f), new Vector3(0.0f, 0.172f, 0.0f) }, new Godot.Collections.Array<double> { 0.018, 0.014, 0.007 }, 8, dark, 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.028f, 0.118f, 0.0f), new Vector3(0.05f, 0.118f, 0.0f) }, new Godot.Collections.Array<double> { 0.004, 0.004 }, 5, grey, 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.05f, 0.118f, 0.0f), new Vector3(0.058f, 0.118f, 0.0f) }, new Godot.Collections.Array<double> { 0.012, 0.012 }, 8, grey, 1.0, 2.0, 0.0, true);
        // Side air tubes: the tubular lantern's signature, rising from the font's
        // shoulder and curving in to feed the cap.
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            Vector3 foot = new Vector3((float)(side * 0.06), 0.07f, 0.0f);
            Vector3 mid = new Vector3((float)(side * 0.078), 0.2f, 0.0f);
            Vector3 top = new Vector3((float)(side * 0.05), 0.315f, 0.0f);
            mb.add_tube(new Godot.Collections.Array<Vector3> { foot, new Vector3((float)(side * 0.077), 0.13f, 0.0f), mid, new Vector3((float)(side * 0.07), 0.275f, 0.0f), top }, new Godot.Collections.Array<double> { 0.012, 0.012, 0.012, 0.011, 0.01 }, 8, grey, 1.0, 4.0, 0.0, true);
        }
        // Wire guard: two rings around the globe joined by thin uprights fore and aft.
        foreach (Variant spec_item in new Godot.Collections.Array { new Godot.Collections.Array { 0.15, 0.067 }, new Godot.Collections.Array { 0.245, 0.064 } })
        {
            Godot.Collections.Array spec = spec_item.AsGodotArray();
            Godot.Collections.Array<Vector3> ring = new Godot.Collections.Array<Vector3>();
            Godot.Collections.Array<double> ring_r = new Godot.Collections.Array<double>();
            for (long k = 0; k < 21; k++)
            {
                double a = (double)k / 20.0 * TAU;
                ring.Add(new Vector3(G.op("*", cos(a), spec[1]).AsSingle(), spec[0].AsSingle(), G.op("*", sin(a), spec[1]).AsSingle()));
                ring_r.Add(0.0032);
            }
            mb.add_tube(ring, ring_r, 5, grey, 1.0, 4.0, 0.0, false);
        }
        foreach (Variant side_item2 in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side2 = side_item2.AsDouble();
            mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.118f, (float)(side2 * 0.06)), new Vector3(0.0f, 0.29f, (float)(side2 * 0.062)) }, new Godot.Collections.Array<double> { 0.0032, 0.0032 }, 5, grey, 1.0, 4.0, 0.0, true);
        }
        // Cap: a vented crown with a dark slot band under the brim, and the chimney.
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.285f, 0.0f), new Vector3(0.0f, 0.3f, 0.0f), new Vector3(0.0f, 0.318f, 0.0f), new Vector3(0.0f, 0.33f, 0.0f) }, new Godot.Collections.Array<double> { 0.06, 0.07, 0.066, 0.05 }, 16, grey, 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.33f, 0.0f), new Vector3(0.0f, 0.344f, 0.0f) }, new Godot.Collections.Array<double> { 0.046, 0.046 }, 16, new Color(0.25f, 0.25f, 0.25f), 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.344f, 0.0f), new Vector3(0.0f, 0.36f, 0.0f), new Vector3(0.0f, 0.378f, 0.0f), new Vector3(0.0f, 0.388f, 0.0f) }, new Godot.Collections.Array<double> { 0.05, 0.036, 0.024, 0.0 }, 12, grey, 1.0, 2.0, 0.0, true);
        // Bail and its pivots on the cap.
        Godot.Collections.Array<Vector3> bail = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> bail_r = new Godot.Collections.Array<double>();
        for (long i4 = 0; i4 < 13; i4++)
        {
            double a2 = (double)i4 / 12.0 * PI;
            bail.Add(new Vector3((float)(-cos(a2) * 0.066), (float)(0.335 + sin(a2) * (HURRICANE_HEIGHT - 0.335)), 0.0f));
            bail_r.Add(0.0048);
        }
        mb.add_tube(bail, bail_r, 5, grey, 1.0, 8.0, 0.0, true);
        foreach (Variant side_item3 in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side3 = side_item3.AsDouble();
            mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3((float)(side3 * 0.058), 0.335f, 0.0f), new Vector3((float)(side3 * 0.072), 0.335f, 0.0f) }, new Godot.Collections.Array<double> { 0.006, 0.006 }, 5, grey, 1.0, 4.0, 0.0, true);
        }
        return mb.commit(null, true);
    }

    public static ArrayMesh hurricane_glass()
    {
        /// The glass globe of a hurricane lantern.
        MeshBuilder mb = new MeshBuilder();
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.12f, 0.0f), new Vector3(0.0f, 0.155f, 0.0f), new Vector3(0.0f, 0.205f, 0.0f), new Vector3(0.0f, 0.255f, 0.0f), new Vector3(0.0f, 0.29f, 0.0f) }, new Godot.Collections.Array<double> { 0.044, 0.058, 0.062, 0.054, 0.04 }, 16, Colors.White, 1.0, 2.0, 0.0, false);
        return mb.commit();
    }

    public static ArrayMesh cloth_sheet_mesh(Vector2 size, long seed_value, double thickness = 0.006)
    {
        /// A canvas ground sheet lying on level ground: a subdivided plane with low
        /// wrinkles, edges that curl up slightly the way laid cloth does, and a thin
        /// skirt so the edge reads as fabric rather than a slab.
        MeshBuilder mb = new MeshBuilder();
        FastNoiseLite noise = new FastNoiseLite();
        noise.Seed = (int)seed_value;
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        noise.Frequency = 1.0f;
        noise.FractalOctaves = 3;
        long nx = 14;
        long nz = 18;
        List<List<int>> rows = new List<List<int>>();
        for (long j = 0, j_end = nz + 1; j < j_end; j++)
        {
            double v = (double)j / (double)nz;
            List<int> row = new List<int>();
            for (long i = 0, i_end = nx + 1; i < i_end; i++)
            {
                double u = (double)i / (double)nx;
                double x = (u - 0.5) * size.X;
                double z = (v - 0.5) * size.Y;
                double edge = minf(minf(u, 1.0 - u), minf(v, 1.0 - v));
                double curl = (1.0 - smoothstep(0.0, 0.06, edge)) * 0.014;
                double wrinkle = noise.GetNoise2D((float)(x * 2.6), (float)(z * 2.6)) * 0.011 * smoothstep(0.0, 0.12, edge);
                double y = thickness + maxf(wrinkle, -thickness * 0.7) + curl;
                row.Add((int)mb.add_vertex(new Vector3((float)x, (float)y, (float)z), Vector3.Up, new Vector2((float)x, (float)z) * 0.9f, Colors.White, new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
            }
            rows.Add(row);
        }
        for (long j2 = 0; j2 < nz; j2++)
        {
            for (long i2 = 0; i2 < nx; i2++)
            {
                mb.add_quad_facing(rows[(int)j2][(int)i2], rows[(int)j2][(int)(i2 + 1)], rows[(int)(j2 + 1)][(int)(i2 + 1)], rows[(int)(j2 + 1)][(int)i2], Vector3.Up);
            }
        }
        mb.recompute_normals();
        // Skirt: the cloth edge down to the ground, facing outward.
        Godot.Collections.Array edges = new Godot.Collections.Array { new Godot.Collections.Array { Variant.From(rows[0].ToArray()), Vector3.Forward }, new Godot.Collections.Array { Variant.From(rows[(int)nz].ToArray()), Vector3.Back } };
        List<int> left = new List<int>();
        List<int> right = new List<int>();
        for (long j3 = 0, j_end2 = nz + 1; j3 < j_end2; j3++)
        {
            left.Add(rows[(int)j3][0]);
            right.Add(rows[(int)j3][(int)nx]);
        }
        edges.Add(new Godot.Collections.Array { Variant.From(left.ToArray()), Vector3.Left });
        edges.Add(new Godot.Collections.Array { Variant.From(right.ToArray()), Vector3.Right });
        foreach (Variant e in edges)
        {
            List<int> line = G.ListFromVariant<int>(G.Index(e, 0));
            Vector3 @out = G.Index(e, 1).AsVector3();
            List<int> feet = new List<int>();
            foreach (int idx in line)
            {
                Vector3 top = mb.vertices[idx];
                feet.Add((int)mb.add_vertex(new Vector3(top.X, 0.0f, top.Z), @out, new Vector2((float)((double)top.X + top.Z), 0.0f) * 0.9f, Colors.White, new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
            }
            for (long k = 0, k_end = (long)line.Count - 1; k < k_end; k++)
            {
                mb.add_quad_facing(line[(int)k], line[(int)(k + 1)], feet[(int)(k + 1)], feet[(int)k], @out);
            }
        }
        return mb.commit(null, true);
    }

    public static void add_hang_link(MeshBuilder mb, Vector3 from, Vector3 to, double radius = 0.006)
    {
        /// A short hanging link: a rope or wire from `from` down to `to`.
        mb.add_tube(new Godot.Collections.Array<Vector3> { from, from.Lerp(to, 0.5f), to }, new Godot.Collections.Array<double> { radius, radius, radius }, 5, Colors.White, 1.0, 6.0, 0.0, true);
    }

    public static ArrayMesh hook_post_mesh(double height, long seed_value)
    {
        /// A shepherd's-hook post: a pole with an arc that ends in a hanging point at
        /// `hook_point(height)`.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        Vector3 top = new Vector3(0.0f, (float)height, 0.0f);
        Godot.Collections.Array<Vector3> pts = new Godot.Collections.Array<Vector3> { new Vector3(0.0f, -0.2f, 0.0f), new Vector3(0.0f, (float)(height * 0.5), 0.0f), top };
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double> { 0.034, 0.03, 0.026 };
        double arc_r = 0.2;
        Vector3 centre = top + new Vector3((float)arc_r, 0.0f, 0.0f);
        for (long i = 1; i < 10; i++)
        {
            double a = (double)i / 9.0 * PI;
            pts.Add(centre + new Vector3((float)(-cos(a) * arc_r), (float)(sin(a) * arc_r), 0.0f));
            radii.Add(0.024 - (double)i * 0.0008);
        }
        pts.Add(centre + new Vector3((float)arc_r, -0.06f, 0.0f));
        radii.Add(0.016);
        add_timber(mb, pts, radii, 7, seed_value % 4, new Color(0.9f, 0.86f, 0.8f), 1.4);
        return mb.commit(null, true);
    }

    public static Vector3 hook_point(double height)
    {
        return new Vector3(0.4f, (float)(height - 0.05), 0.0f);
    }

    public static ArrayMesh box_mesh(Vector3 size)
    {
        MeshBuilder mb = new MeshBuilder();
        mb.add_box(size, Colors.White, 0.8);
        return mb.commit();
    }

    public static ArrayMesh pole_mesh(double height, double radius)
    {
        MeshBuilder mb = new MeshBuilder();
        mb.add_tube(new Godot.Collections.Array<Vector3> { Vector3.Zero, new Vector3(0.0f, (float)height, 0.0f) }, new Godot.Collections.Array<double> { radius, radius * 0.85 }, 6, Colors.White, 1.0, 1.6, 0.0, true);
        return mb.commit();
    }

    public static ArrayMesh rock(long seed_value, double radius = 1.0)
    {
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        MeshBuilder mb = new MeshBuilder();
        double f1 = rng.RandfRange(2.4f, 5.2f);
        double f2 = rng.RandfRange(3.1f, 7.4f);
        double f3 = rng.RandfRange(5.0f, 9.5f);
        double a1 = rng.RandfRange(0.08f, 0.18f);
        double a2 = rng.RandfRange(0.04f, 0.12f);
        double a3 = rng.RandfRange(0.02f, 0.07f);
        double flatten = rng.RandfRange(0.18f, 0.38f);
        Vector3 phase = new Vector3((float)(rng.Randf() * TAU), (float)(rng.Randf() * TAU), (float)(rng.Randf() * TAU));
        Vector3 stretch = new Vector3(rng.RandfRange(0.82f, 1.22f), rng.RandfRange(0.55f, 0.92f), rng.RandfRange(0.8f, 1.2f));
        // Enough rings and segments that the silhouette curves instead of showing
        // flat facets in a close shot, plus a fine fourth term for surface grain.
        double f4 = rng.RandfRange(11.0f, 17.0f);
        double a4 = rng.RandfRange(0.012f, 0.03f);
        mb.add_displaced_sphere(22, 34, radius, Callable.From((Vector3 dir) =>
{
    Vector3 d = dir * stretch;
    double n = 1.0;
    n += sin(d.X * f1 + phase.X) * cos(d.Y * f1 + phase.Y) * a1;
    n += sin(d.Y * f2 + phase.Y) * cos(d.Z * f2 + phase.Z) * a2;
    n += sin(d.Z * f3 + dir.X * f3) * a3;
    n += sin(d.X * f4 + phase.Z) * sin(d.Y * f4 * 0.8 + phase.X) * cos(d.Z * f4 * 1.1) * a4;
    n *= 1.0 - maxf(0.0, -dir.Y) * flatten;
    return maxf(n, 0.35);
}));
        return mb.commit(null, true);
    }

    public static void add_pot(MeshBuilder mb, Vector3 @base)
    {
        /// A cast-iron cooking pot with a lid and a wire bail, bottom at `base`.
        Godot.Collections.Array<Vector3> profile = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        foreach (Variant spec_item in new Godot.Collections.Array { new Godot.Collections.Array { 0.26, 0.145 }, new Godot.Collections.Array { 0.22, 0.15 }, new Godot.Collections.Array { 0.15, 0.155 }, new Godot.Collections.Array { 0.07, 0.14 }, new Godot.Collections.Array { 0.0, 0.1 } })
        {
            Godot.Collections.Array spec = spec_item.AsGodotArray();
            profile.Add(@base + new Vector3(0.0f, spec[0].AsSingle(), 0.0f));
            radii.Add(spec[1].AsDouble());
        }
        mb.add_tube(profile, radii, 12, new Color(0.9f, 0.9f, 0.9f), 1.0, 2.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { @base + new Vector3(0.0f, 0.26f, 0.0f), @base + new Vector3(0.0f, 0.285f, 0.0f), @base + new Vector3(0.0f, 0.3f, 0.0f) }, new Godot.Collections.Array<double> { 0.15, 0.12, 0.03 }, 12, new Color(0.8f, 0.8f, 0.8f), 1.0, 2.0, 0.0, true);
        Godot.Collections.Array<Vector3> bail = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> bail_r = new Godot.Collections.Array<double>();
        for (long i2 = 0; i2 < 13; i2++)
        {
            double a = (double)i2 / 12.0 * PI;
            bail.Add(@base + new Vector3((float)(-cos(a) * 0.15), (float)(0.24 + sin(a) * 0.22), 0.0f));
            bail_r.Add(0.008);
        }
        mb.add_tube(bail, bail_r, 5, Colors.White, 1.0, 12.0, 0.0, true);
    }

    public static void add_board(MeshBuilder mb, Transform3D xform, Vector3 size, long strip, double v0, Color? tint_opt = null, double metres_per_tile = 1.0)
    {
        Color tint = tint_opt ?? Colors.White;
        /// Appends a sawn board to `mb`. `size` is (across, thickness, length) in the
        /// board's own frame, length along local -Z..+Z; `xform` places it. Grain runs
        /// along the length: v follows the length and u is confined to one of the four
        /// plank strips of the wood texture so the texture's own seams never show.
        Vector3 h = size * 0.5f;
        double u0 = (double)posmod(strip, 4) * 0.25 + 0.018;
        double u_span = 0.25 - 0.036;
        double v_len = size.Z / metres_per_tile;
        double u_thick = u0 + minf(size.Y / metres_per_tile, u_span);
        Godot.Collections.Array faces = new Godot.Collections.Array { new Godot.Collections.Array { new Vector3(-h.X, h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z), new Rect2((float)u0, (float)v0, (float)u_span, (float)v_len) }, new Godot.Collections.Array { new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(-h.X, -h.Y, h.Z), new Rect2((float)u0, (float)v0, (float)u_span, (float)v_len) }, new Godot.Collections.Array { new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Rect2((float)u0, (float)v0, (float)(u_thick - u0), (float)v_len) }, new Godot.Collections.Array { new Vector3(-h.X, -h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, -h.Z), new Vector3(-h.X, -h.Y, -h.Z), new Rect2((float)u0, (float)v0, (float)(u_thick - u0), (float)v_len) }, new Godot.Collections.Array { new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z), new Rect2((float)u0, (float)v0, (float)u_span, (float)(size.Y / metres_per_tile)) }, new Godot.Collections.Array { new Vector3(h.X, -h.Y, -h.Z), new Vector3(-h.X, -h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Rect2((float)u0, (float)v0, (float)u_span, (float)(size.Y / metres_per_tile)) } };
        // +Y top, -Y bottom: u across, v along.
        // +X / -X long sides: u through the thickness, v along.
        // End grain.
        foreach (Variant face_item in faces)
        {
            Godot.Collections.Array face = face_item.AsGodotArray();
            mb.add_quad(G.op("*", xform, face[0]).AsVector3(), G.op("*", xform, face[1]).AsVector3(), G.op("*", xform, face[2]).AsVector3(), G.op("*", xform, face[3]).AsVector3(), tint, new Color(0, 0, 0, 0), face[4].AsRect2());
        }
    }

    public static void add_timber(MeshBuilder mb, Godot.Collections.Array<Vector3> points, Godot.Collections.Array<double> radii, long sides, long strip, Color? tint_opt = null, double metres_per_tile = 1.0)
    {
        Color tint = tint_opt ?? Colors.White;
        /// A round timber (pile, pole, rail) whose bark-free surface uses one plank
        /// strip of the wood texture. `points` run along the timber.
        long first = mb.vertex_count();
        mb.add_tube(points, radii, sides, tint, 1.0, 1.0 / metres_per_tile, 0.0, true);
        mb.remap_uv(first, 0.25 - 0.036, (double)posmod(strip, 4) * 0.25 + 0.018);
    }

    public static void add_rope(MeshBuilder mb, Vector3 from, Vector3 to, double sag, double radius = 0.012, long segments = 12)
    {
        /// Catenary-like rope between two points with `sag` metres of droop.
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        for (long i = 0, i_end = segments + 1; i < i_end; i++)
        {
            double t = (double)i / (double)segments;
            points.Add(from.Lerp(to, (float)t) - Vector3.Up * (float)(sag * 4.0 * t * (1.0 - t)));
            radii.Add(radius);
        }
        mb.add_tube(points, radii, 5, Colors.White, 1.0, 8.0, 0.0, true);
    }

    public static void add_rope_coil(MeshBuilder mb, Vector3 centre, double post_radius, long turns, double pitch, double radius = 0.012)
    {
        /// Rope wrapped `turns` times around a vertical post of `post_radius` at `centre`.
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
        long steps = turns * 14;
        for (long i = 0, i_end = steps + 1; i < i_end; i++)
        {
            double a = (double)i / 14.0 * TAU;
            double y = centre.Y + (double)i / (double)steps * pitch * (double)turns;
            points.Add(new Vector3((float)(centre.X + cos(a) * (post_radius + radius * 0.9)), (float)y, (float)(centre.Z + sin(a) * (post_radius + radius * 0.9))));
            radii.Add(radius);
        }
        mb.add_tube(points, radii, 5, Colors.White, 1.0, 8.0, 0.0, true);
    }

    public static SphereMesh sphere_mesh(double radius, long rings = 8, long segments = 10)
    {
        SphereMesh mesh = new SphereMesh();
        mesh.Radius = (float)radius;
        mesh.Height = (float)(radius * 2.0);
        mesh.RadialSegments = (int)segments;
        mesh.Rings = (int)rings;
        mesh.IsHemisphere = false;
        return mesh;
    }
}
