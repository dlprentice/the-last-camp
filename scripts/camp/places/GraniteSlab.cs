#nullable disable
using System;
using Godot;
using LastCamp.Construction;

namespace LastCamp;

/// Weathered granite stones in code: a rounded slab (a superellipsoid, boxy with
/// softly rounded arrises) whose edges are broken and chipped by seeded noise,
/// its faces gently uneven, and its colour channels baked from where it really
/// sits: soil (COLOR.r) and moss (COLOR.g) climb from the ground line, read from the
/// terrain under every vertex. The stone lies within its nominal box, so placement
/// and grounding can be reasoned about with that box.
public static class GraniteSlab
{
    public const int RINGS = 22;
    public const int SEGMENTS = 44;

    public struct Shape
    {
        /// Nominal full size (the stone fits inside it).
        public Vector3 size;
        /// 0.1 nearly square arrises .. 0.5 a pebble.
        public double roundness;
        /// Edge chipping and face unevenness as a share of the smallest dimension.
        public double rough;
        /// Keeps the top face flat (a tread or a seat) when true.
        public bool flat_top;
        public long seed;
        /// Optional: lifts the underside within this inset by `hollow` metres (the loose step).
        public double hollow;
        public double hollow_inset;
    }

    static double spow(double v, double e) => Math.Sign(v) * Math.Pow(Math.Abs(v), e);

    /// Height of the underside lift at local (x, z) for a hollowed stone.
    public static double hollow_lift(Shape s, double x, double z)
    {
        if (s.hollow <= 0.0) return 0.0;
        double ix = s.size.X * 0.5 - s.hollow_inset, iz = s.size.Z * 0.5 - s.hollow_inset;
        double fx = 1.0 - Math.Clamp((Math.Abs(x) - (ix - 0.05)) / 0.05, 0.0, 1.0);
        double fz = 1.0 - Math.Clamp((Math.Abs(z) - (iz - 0.05)) / 0.05, 0.0, 1.0);
        double f = fx * fz;
        return s.hollow * f * f * (3.0 - 2.0 * f);
    }

    /// Local surface point for a unit sphere direction.
    public static Vector3 surface(Shape s, FastNoiseLite noise, Vector3 dir)
    {
        double e = Math.Clamp(s.roundness, 0.05, 1.0);
        Vector3 h = s.size * 0.5f;
        double phi = Math.Asin(Math.Clamp(dir.Y, -1.0f, 1.0f));
        double theta = Math.Atan2(dir.Z, dir.X);
        double cp = Math.Cos(phi);
        Vector3 p = new(
            (float)(h.X * spow(cp * Math.Cos(theta), e)),
            (float)(h.Y * spow(Math.Sin(phi), e)),
            (float)(h.Z * spow(cp * Math.Sin(theta), e)));
        double smallest = Math.Min(s.size.X, Math.Min(s.size.Y, s.size.Z));
        // Broken arrises: near the edges the stone loses material, unevenly.
        Vector3 q = new(Math.Abs(p.X) / h.X, Math.Abs(p.Y) / h.Y, Math.Abs(p.Z) / h.Z);
        double edge = Math.Max(Math.Max(q.X + q.Y, q.Y + q.Z), q.X + q.Z) - 1.55;
        double edgeness = Math.Clamp(edge * 2.5, 0.0, 1.0);
        double n1 = noise.GetNoise3D(p.X * 3.1f, p.Y * 3.1f, p.Z * 3.1f) * 0.5 + 0.5;
        double n2 = noise.GetNoise3D(p.X * 9.0f + 11f, p.Y * 9.0f, p.Z * 9.0f) * 0.5 + 0.5;
        double chip = edgeness * (0.35 + 0.65 * n1) * s.rough * smallest * 1.6;
        double bump = (n2 - 0.5) * s.rough * smallest * 0.35;
        Vector3 inward = -p.Normalized();
        bool top = s.flat_top && dir.Y > 0.0;
        // Only ever inward, so the stone stays inside its nominal box.
        p += inward * (float)(chip + Math.Abs(bump) * (top ? 0.2 : 1.0));
        if (dir.Y < 0.0) p.Y += (float)hollow_lift(s, p.X, p.Z) * (float)Math.Min(1.0, -dir.Y * 4.0);
        return p;
    }

    /// The stone's mesh, with soil and moss baked for its world pose `place`.
    public static ArrayMesh build(Shape s, Transform3D place, Func<double, double, double> ground)
    {
        var noise = new FastNoiseLite { Seed = (int)s.seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1.0f, FractalOctaves = 2 };
        int cols = SEGMENTS + 1;
        var pts = new Vector3[(RINGS + 1) * cols];
        for (int j = 0; j <= RINGS; j++)
        {
            double phi = -Math.PI * 0.5 + Math.PI * j / RINGS;
            for (int i = 0; i <= SEGMENTS; i++)
            {
                double theta = Math.Tau * i / SEGMENTS;
                Vector3 dir = new((float)(Math.Cos(phi) * Math.Cos(theta)), (float)Math.Sin(phi), (float)(Math.Cos(phi) * Math.Sin(theta)));
                pts[j * cols + i] = surface(s, noise, dir);
            }
        }
        var mb = new MeshBuilder();
        var index = new long[pts.Length];
        for (int j = 0; j <= RINGS; j++)
        {
            for (int i = 0; i <= SEGMENTS; i++)
            {
                int k = j * cols + i;
                Vector3 n = normal_at(pts, cols, i, j);
                Vector3 w = place * pts[k];
                double above = w.Y - ground(w.X, w.Z);
                float soil = (float)(1.0 - smoothstep(-0.01, 0.06, above));
                float moss = (float)(1.0 - smoothstep(0.0, 0.22, above)) * (float)(0.5 + 0.5 * Math.Max(0.0, n.Y));
                index[k] = mb.add_vertex(pts[k], n, new Vector2(i / (float)SEGMENTS, j / (float)RINGS), new Color(soil, moss, 0, 1), new Vector4(1, 0, 0, 1));
            }
        }
        for (int j = 0; j < RINGS; j++)
        {
            for (int i = 0; i < SEGMENTS; i++)
            {
                long a = index[j * cols + i], b = index[j * cols + i + 1], c = index[(j + 1) * cols + i + 1], d = index[(j + 1) * cols + i];
                // a-b-c-d runs clockwise seen from outside: Godot's front face.
                mb.add_triangle(a, c, d);
                mb.add_triangle(a, b, c);
            }
        }
        return mb.commit(null, true);
    }

    static Vector3 normal_at(Vector3[] p, int cols, int i, int j)
    {
        if (j == 0) return Vector3.Down;
        if (j == RINGS) return Vector3.Up;
        int il = (i - 1 + SEGMENTS) % SEGMENTS, ir = (i + 1) % SEGMENTS;
        Vector3 du = p[j * cols + ir] - p[j * cols + il];
        Vector3 dv = p[(j + 1) * cols + i] - p[(j - 1) * cols + i];
        Vector3 n = dv.Cross(du);
        if (n.LengthSquared() < 1e-12f) return p[j * cols + i].Normalized();
        n = n.Normalized();
        return n.Dot(p[j * cols + i]) < 0 ? -n : n;
    }

    static double smoothstep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    static ShaderMaterial _material;
    /// The shared weathered-granite material.
    public static ShaderMaterial material()
    {
        if (_material != null && GodotObject.IsInstanceValid(_material)) return _material;
        _material = new ShaderMaterial { Shader = Content.Load<Shader>("res://shaders/granite.gdshader") };
        Camp.bind_prop_pbr(_material, "rock");
        Camp.bind_texture(_material, "noise_tex", PropMaterials.NOISE);
        return _material;
    }

    /// Convex collision for a stone at `place`, in its body's frame (origin at place.Origin).
    public static ConvexPolygonShape3D hull(ArrayMesh mesh, Basis basis)
    {
        var shape = (ConvexPolygonShape3D)mesh.CreateConvexShape(true, true);
        Vector3[] points = shape.Points;
        for (int k = 0; k < points.Length; k++) points[k] = basis * points[k];
        return new ConvexPolygonShape3D { Points = points };
    }
}
