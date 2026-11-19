#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using LastCamp.Construction;

namespace LastCamp;

/// Tom's blaze: a small knife notch, never paint. Bark cannot be cut away from a
/// shared tree mesh, so the notch is built as a thin patch that lies on the host's
/// own surface (sampled from its triangles, with its UVs and vertex colour), rising
/// into a slight healed callus round the cut. Over the cut's opening lies a second
/// patch whose shader traces the notch itself, a horizontal V about 13 cm long and
/// 2.5 cm deep, into the wood: the view ray is followed down to the two knife
/// faces, so the cut shows its depth, its walls and the dark band of bark at its
/// rim from any angle. The patches are in the host's local space, so they take the
/// same transform and, on a tree, the same trunk bend as the bark under them.
public static class BlazeNotch
{
    /// Patch half-extents across and up the surface, and its grid.
    public const double HALF_WIDTH = 0.095;
    public const double HALF_HEIGHT = 0.07;
    public const int COLUMNS = 35;
    public const int ROWS = 29;
    /// The notch: a lens-shaped opening about 13 cm across and 3.5 cm tall, its
    /// two faces meeting in a crease 2.5 cm in, nearer the bottom.
    public const double CUT_HALF_WIDTH = 0.065;
    public const double CUT_TOP = 0.022;
    public const double CUT_BOTTOM = -0.013;
    public const double CUT_DEPTH = 0.025;
    /// Share of the opening's height from its bottom edge to the crease.
    public const double CREASE_AT = 0.36;
    /// Bark thickness at the cut's rim, where its dark section shows.
    public const double BARK = 0.008;
    /// Height of the healed callus above the bark round the cut.
    public const double LIP = 0.005;
    /// Everything stands this far off the host so the two never fight in depth;
    /// the cut's patch lies this far again above the callus.
    public const double SKIN = 0.0012;

    public struct Triangle
    {
        public Vector3 a, b, c;
        public Vector2 ua, ub, uc;
        public Color ca, cb, cc;
    }

    /// Triangles of `mesh` (surface `surface`) within `radius` of `centre`.
    public static List<Triangle> triangles_near(ArrayMesh mesh, int surface, Vector3 centre, double radius)
    {
        var result = new List<Triangle>();
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Color[] col = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? null : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        int[] idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        float r2 = (float)(radius * radius);
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            int a = idx[i], b = idx[i + 1], c = idx[i + 2];
            Vector3 mid = (v[a] + v[b] + v[c]) / 3.0f;
            if (mid.DistanceSquaredTo(centre) > r2) continue;
            result.Add(new Triangle
            {
                a = v[a], b = v[b], c = v[c],
                ua = uv != null ? uv[a] : Vector2.Zero, ub = uv != null ? uv[b] : Vector2.Zero, uc = uv != null ? uv[c] : Vector2.Zero,
                ca = col != null ? col[a] : Colors.White, cb = col != null ? col[b] : Colors.White, cc = col != null ? col[c] : Colors.White,
            });
        }
        return result;
    }

    /// The outermost surface hit by a ray from `from` along `dir`.
    public static bool cast(List<Triangle> tris, Vector3 from, Vector3 dir, out Vector3 point, out Vector3 normal, out Vector2 uv, out Color color)
    {
        double best = double.PositiveInfinity;
        point = normal = Vector3.Zero;
        uv = Vector2.Zero;
        color = Colors.White;
        foreach (Triangle t in tris)
        {
            Vector3 e1 = t.b - t.a, e2 = t.c - t.a;
            Vector3 pv = dir.Cross(e2);
            float det = e1.Dot(pv);
            if (Math.Abs(det) < 1e-9f) continue;
            float inv = 1.0f / det;
            Vector3 tv = from - t.a;
            float u = tv.Dot(pv) * inv;
            if (u < 0.0f || u > 1.0f) continue;
            Vector3 qv = tv.Cross(e1);
            float w = dir.Dot(qv) * inv;
            if (w < 0.0f || u + w > 1.0f) continue;
            float dist = e2.Dot(qv) * inv;
            if (dist <= 0.0f || dist >= best) continue;
            best = dist;
            point = from + dir * dist;
            Vector3 n = e1.Cross(e2).Normalized();
            normal = n.Dot(dir) > 0.0f ? -n : n;
            float s = 1.0f - u - w;
            uv = t.ua * s + t.ub * u + t.uc * w;
            color = t.ca * s + t.cb * u + t.cc * w;
        }
        return !double.IsPositiveInfinity(best);
    }

    /// Height of the patch above the host at (across, up) on the notch's face,
    /// and whether that point lies in the cut's opening.
    public static double profile(double across, double up, out bool cut) => profile(across, up, out cut, out _);

    /// The patch's height is the callus, which swells round the opening and falls
    /// back to the bark at the patch's edge. `depth` is how far in the cut's
    /// surface lies under this point, metres (0 outside the opening).
    public static double profile(double across, double up, out bool cut, out double depth)
    {
        double edge = Math.Max(Math.Abs(across) / HALF_WIDTH, Math.Abs(up) / HALF_HEIGHT);
        double lip = LIP * (1.0 - smoothstep(0.55, 1.0, edge));
        depth = cut_depth(across, up);
        cut = depth > 0.0;
        return SKIN + lip;
    }

    /// The notch is a knife's two strokes: a V, widest at its middle and
    /// running out to points at both ends, its faces meeting in a crease nearer the
    /// bottom (the upper stroke cut at a shallower angle). Depth of the cut surface
    /// below the opening at (across, up), metres; 0 outside it. The cut shader
    /// (shaders/blaze_cut.gdshader) carries the same function.
    public static double cut_depth(double across, double up)
    {
        double t = Math.Abs(across) / CUT_HALF_WIDTH;
        double spread = 1.0 - Math.Pow(t, 1.4);
        if (spread <= 0.0) return 0.0;
        double crease = CUT_BOTTOM + (CUT_TOP - CUT_BOTTOM) * CREASE_AT;
        double top = crease + (CUT_TOP - crease) * spread;
        double bottom = crease - (crease - CUT_BOTTOM) * spread;
        if (up >= top || up <= bottom) return 0.0;
        double d = up >= crease ? (top - up) / Math.Max(top - crease, 1e-6) : (up - bottom) / Math.Max(crease - bottom, 1e-6);
        return CUT_DEPTH * Math.Clamp(d, 0.0, 1.0) * Math.Sqrt(spread);
    }

    static double smoothstep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// Builds the notch on `host` around the surface point seen from `centre`
    /// looking back along `outward`, with `up` the notch's vertical. Returns a
    /// two-surface mesh (the callus on the host's material, then the cut, whose UV
    /// is the point's (across, up) on the notch in metres), or null if the host
    /// has no surface there.
    public static ArrayMesh build(ArrayMesh host, int surface, Vector3 centre, Vector3 outward, Vector3 up, out Vector3 surface_centre)
    {
        outward = outward.Normalized();
        Vector3 across_dir = up.Cross(outward).Normalized();
        Vector3 up_dir = outward.Cross(across_dir).Normalized();
        List<Triangle> tris = triangles_near(host, surface, centre, 0.9);
        surface_centre = centre;
        if (cast(tris, centre + outward * 1.5f, -outward, out Vector3 middle, out _, out _, out _)) surface_centre = middle;
        var points = new Vector3[COLUMNS * ROWS];
        var normals = new Vector3[COLUMNS * ROWS];
        var uvs = new Vector2[COLUMNS * ROWS];
        var patch = new Vector2[COLUMNS * ROWS];
        var colors = new Color[COLUMNS * ROWS];
        var on_cut = new bool[COLUMNS * ROWS];
        for (int j = 0; j < ROWS; j++)
        {
            for (int i = 0; i < COLUMNS; i++)
            {
                double a = (i / (double)(COLUMNS - 1) * 2.0 - 1.0) * HALF_WIDTH;
                double b = (j / (double)(ROWS - 1) * 2.0 - 1.0) * HALF_HEIGHT;
                Vector3 from = centre + across_dir * (float)a + up_dir * (float)b + outward * 1.5f;
                if (!cast(tris, from, -outward, out Vector3 hit, out Vector3 n, out Vector2 uv, out Color color)) return null;
                double lift = profile(a, b, out bool cut);
                int k = j * COLUMNS + i;
                points[k] = hit + n * (float)lift;
                normals[k] = n;
                uvs[k] = uv;
                patch[k] = new Vector2((float)a, (float)b);
                colors[k] = color;
                on_cut[k] = cut;
            }
        }
        var lip = new MeshBuilder();
        var wood = new MeshBuilder();
        var lip_index = new long[points.Length];
        var wood_index = new long[points.Length];
        Array.Fill(lip_index, -1L);
        Array.Fill(wood_index, -1L);
        long L(int k) => lip_index[k] >= 0 ? lip_index[k] : lip_index[k] = lip.add_vertex(points[k], grid_normal(points, k), uvs[k], colors[k], tangent(points, k));
        long W(int k) => wood_index[k] >= 0 ? wood_index[k] : wood_index[k] = wood.add_vertex(points[k] + normals[k] * (float)SKIN, grid_normal(points, k), patch[k], colors[k], tangent(points, k));
        for (int j = 0; j + 1 < ROWS; j++)
        {
            for (int i = 0; i + 1 < COLUMNS; i++)
            {
                int k00 = j * COLUMNS + i, k10 = k00 + 1, k01 = k00 + COLUMNS, k11 = k01 + 1;
                // The callus is whole. Wound so the face looks out along `outward`.
                long a0 = L(k00), a1 = L(k10), a2 = L(k11), a3 = L(k01);
                lip.add_triangle(a0, a2, a1);
                lip.add_triangle(a0, a3, a2);
                // The cut's patch covers every cell near the opening; its shader
                // trims it to the opening's exact edge.
                if (!near_cut(on_cut, i, j)) continue;
                long w0 = W(k00), w1 = W(k10), w2 = W(k11), w3 = W(k01);
                wood.add_triangle(w0, w2, w1);
                wood.add_triangle(w0, w3, w2);
            }
        }
        ArrayMesh mesh = lip.commit();
        if (!wood.is_empty()) wood.commit(null, false, mesh);
        return mesh;
    }

    /// True when the cell at (i, j) or one next to it has a corner in the opening.
    static bool near_cut(bool[] on_cut, int i, int j)
    {
        for (int dj = -1; dj <= 2; dj++)
        {
            for (int di = -1; di <= 2; di++)
            {
                int ii = i + di, jj = j + dj;
                if (ii < 0 || jj < 0 || ii >= COLUMNS || jj >= ROWS) continue;
                if (on_cut[jj * COLUMNS + ii]) return true;
            }
        }
        return false;
    }

    public static ShaderMaterial cut_material(bool bends_with_tree)
    {
        var mat = new ShaderMaterial { Shader = Content.Load<Shader>("res://shaders/blaze_cut.gdshader") };
        Camp.bind_prop_pbr(mat, "wood");
        mat.SetShaderParameter("wind_response", bends_with_tree ? 1.0 : 0.0);
        mat.SetShaderParameter("cut_half_width", CUT_HALF_WIDTH);
        mat.SetShaderParameter("cut_top", CUT_TOP);
        mat.SetShaderParameter("cut_bottom", CUT_BOTTOM);
        mat.SetShaderParameter("cut_depth", CUT_DEPTH);
        mat.SetShaderParameter("crease_at", CREASE_AT);
        mat.SetShaderParameter("bark_thickness", BARK);
        return mat;
    }

    /// The trunk's centre at local height `y`: the mean of the bark vertices in a
    /// thin band there, ignoring anything more than `reach` from the tree's axis.
    public static Vector3 trunk_axis(ArrayMesh bark, double y, double reach = 1.2)
    {
        Vector3[] v = bark.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (Vector3 p in v)
        {
            if (Math.Abs(p.Y - y) > 0.08 || new Vector2(p.X, p.Z).Length() > reach) continue;
            sum += p;
            count++;
        }
        return count > 0 ? new Vector3(sum.X / count, (float)y, sum.Z / count) : new Vector3(0, (float)y, 0);
    }

    static Vector3 grid_normal(Vector3[] p, int k)
    {
        int i = k % COLUMNS, j = k / COLUMNS;
        Vector3 dx = p[j * COLUMNS + Math.Min(i + 1, COLUMNS - 1)] - p[j * COLUMNS + Math.Max(i - 1, 0)];
        Vector3 dy = p[Math.Min(j + 1, ROWS - 1) * COLUMNS + i] - p[Math.Max(j - 1, 0) * COLUMNS + i];
        return dx.Cross(dy).Normalized();
    }

    static Vector4 tangent(Vector3[] p, int k)
    {
        int i = k % COLUMNS, j = k / COLUMNS;
        Vector3 dx = (p[j * COLUMNS + Math.Min(i + 1, COLUMNS - 1)] - p[j * COLUMNS + Math.Max(i - 1, 0)]).Normalized();
        return new Vector4(dx.X, dx.Y, dx.Z, 1.0f);
    }
}
