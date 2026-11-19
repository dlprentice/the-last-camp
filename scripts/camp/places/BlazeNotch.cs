#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using LastCamp.Construction;

namespace LastCamp;

/// Tom's blaze: a knife notch cut into the wood itself, never paint or a decal.
/// The host's own mesh is carved. Its triangles over a small patch round the notch
/// are taken out and rebuilt finely (with the host's UVs, colours and normals, so
/// the bark round the cut carries on unbroken), and inside the patch the surface
/// is pushed into the wood along the notch's two knife faces: flat, meeting in a
/// crease about 2.4 cm in, running out to points about 13 cm apart, each face cut
/// in two overlapping strokes so small facets show. The bark at the rim is torn
/// ragged, flaked off in places and lifted a little. The faces go on their own
/// surface with the weathered-wood shader (shaders/blaze_cut.gdshader); the rest
/// stays on the host's surface and material. Everything is in the host's local
/// space, so on a tree the cut bends with the trunk exactly as the bark round it.
public static class BlazeNotch
{
    /// The patch rebuilt round the notch, metres: half its width across, and how
    /// far it reaches above and below the crease.
    public const double HALF_WIDTH = 0.1;
    public const double TOP = 0.05;
    public const double BOTTOM = -0.04;
    /// The notch's two faces, as tangents of their angle from the surface normal:
    /// the upper stroke shallow, the lower one steep. Its depth at the middle.
    public const double TOP_SLOPE = 0.85;
    public const double BOTTOM_SLOPE = 0.45;
    public const double DEPTH = 0.024;
    /// Bark thickness at the rim, how high the torn bark lifts beside the cut,
    /// and how far round the opening the tear reaches.
    public const double BARK = 0.007;
    public const double LIFT = 0.0016;
    public const double TEAR = 0.0011;
    /// The crease is not quite level (radians round the notch's normal).
    public const double TILT = 0.035;
    /// Grid spacing inside the cut and in the bark round it, metres.
    public const double FINE = 0.0025;
    public const double FINE_ROW = 0.0014;
    public const double COARSE = 0.006;
    /// The patch may span at most this share of the host's radius.
    public const double MAX_SPAN = 0.7;

    /// One knife stroke of a face: where along the crease it starts and ends, and
    /// how deep and at what slope it cuts.
    public readonly struct Stroke
    {
        public readonly double from, to, depth, slope;
        public Stroke(double p_from, double p_to, double p_depth, double p_slope) { from = p_from; to = p_to; depth = p_depth; slope = p_slope; }
    }

    public static readonly Stroke[] TOP_STROKES = { new(-0.066, 0.03, 0.0235, TOP_SLOPE * 0.97), new(-0.045, 0.061, DEPTH, TOP_SLOPE * 1.03) };
    public static readonly Stroke[] BOTTOM_STROKES = { new(-0.064, 0.02, 0.0235, BOTTOM_SLOPE * 1.03), new(-0.035, 0.059, 0.0232, BOTTOM_SLOPE * 0.97) };

    /// The carved host: its surfaces in order with the carved one rebuilt, then the
    /// cut's faces as one more surface.
    public sealed class Carved
    {
        public ArrayMesh mesh;
        public int cut_surface;
        /// The notch's middle on the host's original surface, host-local.
        public Vector3 surface_centre;
        /// The notch's outward direction, host-local.
        public Vector3 outward;
    }

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
        int[] idx = indices_of(arrays, v.Length);
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

    static int[] indices_of(Godot.Collections.Array arrays, int vertex_count)
    {
        if (arrays[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.Nil) return arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var idx = new int[vertex_count];
        for (int i = 0; i < vertex_count; i++) idx[i] = i;
        return idx;
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

    // ------------------------------------------------------------ the cut's shape

    /// How deep one stroke cuts at `a` along the crease: nearly full along its
    /// middle, running out to a point at each end where the blade left the wood,
    /// and below the surface (negative) past its ends so the rim stays continuous.
    static double stroke_depth(in Stroke s, double a)
    {
        double half = (s.to - s.from) * 0.5;
        double t = Math.Abs(a - (s.from + half)) / half;
        if (t < 1.0) return s.depth * (1.0 - t * t * t);
        return -s.depth * (t - 1.0) * 3.0;
    }

    /// Signed depth of the knife's cut below the surface at (a across, b up from
    /// the crease), metres: positive inside the opening. The upper face is the
    /// deeper of its strokes, as is the lower; the notch is where both cut.
    public static double cut_depth(double a, double b)
    {
        double top = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (Stroke s in TOP_STROKES) top = Math.Max(top, stroke_depth(s, a) - b / s.slope);
        foreach (Stroke s in BOTTOM_STROKES) bottom = Math.Max(bottom, stroke_depth(s, a) + b / s.slope);
        return Math.Min(top, bottom);
    }

    /// The crease's depth at `a`: where the two faces meet.
    public static double crease_depth(double a)
    {
        double d = 0.0;
        foreach (Stroke s in TOP_STROKES) d = Math.Max(d, stroke_depth(s, a));
        return d;
    }

    /// The finished surface at (a, b), metres: how far the cut takes it in
    /// (`removed`, with the rim torn ragged and bark flaked off beside it) and how
    /// far the torn bark there stands out (`lift`).
    public static void shape(double a, double b, ulong seed, out double removed, out double lift)
    {
        double raw = cut_depth(a, b);
        double rim = 1.0 - smoothstep(0.0, 0.006, Math.Abs(raw));
        double cut = raw + (fbm(a * 300.0, b * 300.0, seed) * 2.0 - 1.0) * TEAR * rim;
        double window = (1.0 - smoothstep(0.7, 0.92, Math.Abs(a) / HALF_WIDTH)) * (1.0 - smoothstep(0.7, 0.92, b > 0.0 ? b / TOP : b / BOTTOM));
        // Bark flaked away in a few places along the rim, down to the inner bark.
        double flake = BARK * 0.65 * smoothstep(0.58, 0.74, noise(a * 90.0, b * 110.0, seed + 11)) * (1.0 - smoothstep(0.002, 0.008, -raw)) * window;
        removed = Math.Max(Math.Max(cut, 0.0), flake);
        // The knife levered the bark up along its edge, most above the cut.
        double side = b > 0.0 ? 1.0 : 0.55;
        lift = LIFT * side * (0.45 + 0.55 * noise(a * 140.0, b * 60.0, seed + 23)) * (1.0 - smoothstep(0.0, 0.01, Math.Max(-raw, 0.0)))
            * (1.0 - smoothstep(0.0, BARK, removed)) * window;
    }

    // ------------------------------------------------------------ carving

    /// One vertex's attributes, as the host surface carries them.
    struct Attr
    {
        public Vector3 pos, normal;
        public Vector4 tangent;
        public Vector2 uv, uv2;
        public Color color;

        public static Attr mix(in Attr a, in Attr b, in Attr c, double wa, double wb, double wc)
        {
            float fa = (float)wa, fb = (float)wb, fc = (float)wc;
            var r = new Attr
            {
                pos = a.pos * fa + b.pos * fb + c.pos * fc,
                normal = (a.normal * fa + b.normal * fb + c.normal * fc).Normalized(),
                tangent = a.tangent * fa + b.tangent * fb + c.tangent * fc,
                uv = a.uv * fa + b.uv * fb + c.uv * fc,
                uv2 = a.uv2 * fa + b.uv2 * fb + c.uv2 * fc,
                color = a.color * fa + b.color * fb + c.color * fc,
            };
            var t = new Vector3(r.tangent.X, r.tangent.Y, r.tangent.Z).Normalized();
            r.tangent = new Vector4(t.X, t.Y, t.Z, a.tangent.W);
            return r;
        }
    }

    /// Carves the notch into `host`'s surface `surface` around the point seen from
    /// `centre` (inside the host, on its axis) looking back along `outward`, with
    /// `up` the notch's vertical (the crease runs square to it). `metres_per_unit`
    /// is the host's scale, so the cut keeps its size in metres. Returns null if the
    /// host has no clean surface there.
    public static Carved carve(ArrayMesh host, int surface, Vector3 centre, Vector3 outward, Vector3 up, double metres_per_unit, ulong seed)
    {
        outward = outward.Normalized();
        Vector3 across = up.Cross(outward).Normalized();
        Vector3 upd = outward.Cross(across).Normalized();
        across = across.Rotated(outward, (float)TILT);
        upd = upd.Rotated(outward, (float)TILT);
        List<Triangle> near = triangles_near(host, surface, centre, 1.2);
        if (!cast(near, centre + outward * 1.5f, -outward, out Vector3 sc, out _, out _, out _)) return null;
        // Lengths below are in host units: metres / k.
        double k = Math.Max(metres_per_unit, HALF_WIDTH / (MAX_SPAN * Math.Max(sc.DistanceTo(centre), 1e-3)));
        double hw = HALF_WIDTH / k, top = TOP / k, bottom = BOTTOM / k;

        Godot.Collections.Array arrays = host.SurfaceGetArrays(surface);
        for (int ch = 0; ch < (int)Mesh.ArrayType.Max; ch++)
        {
            bool supported = ch == (int)Mesh.ArrayType.Vertex || ch == (int)Mesh.ArrayType.Normal || ch == (int)Mesh.ArrayType.Tangent
                || ch == (int)Mesh.ArrayType.TexUV || ch == (int)Mesh.ArrayType.TexUV2 || ch == (int)Mesh.ArrayType.Color || ch == (int)Mesh.ArrayType.Index;
            if (!supported && arrays[ch].VariantType != Variant.Type.Nil) return null;
        }
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        int n_verts = v.Length;
        Vector3[] nrm = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        bool has_tan = arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil;
        bool has_uv = arrays[(int)Mesh.ArrayType.TexUV].VariantType != Variant.Type.Nil;
        bool has_uv2 = arrays[(int)Mesh.ArrayType.TexUV2].VariantType != Variant.Type.Nil;
        bool has_col = arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil;
        float[] tan = has_tan ? arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array() : null;
        Vector2[] uv = has_uv ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : null;
        Vector2[] uv2 = has_uv2 ? arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array() : null;
        Color[] col = has_col ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : null;
        int[] idx = indices_of(arrays, n_verts);
        Attr attr(int i) => new Attr
        {
            pos = v[i], normal = nrm[i],
            tangent = has_tan ? new Vector4(tan[i * 4], tan[i * 4 + 1], tan[i * 4 + 2], tan[i * 4 + 3]) : new Vector4(1, 0, 0, 1),
            uv = has_uv ? uv[i] : Vector2.Zero, uv2 = has_uv2 ? uv2[i] : Vector2.Zero, color = has_col ? col[i] : Colors.White,
        };
        Vector2 flat(Vector3 p) { Vector3 q = p - sc; return new Vector2(q.Dot(across), q.Dot(upd)); }

        // The host's triangles over the patch: facing out, near the surface there.
        var region = new List<int>();
        double depth_reach = 0.15 / k;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int ia = idx[t], ib = idx[t + 1], ic = idx[t + 2];
            if ((nrm[ia] + nrm[ib] + nrm[ic]).Normalized().Dot(outward) < 0.3f) continue;
            bool close = true;
            foreach (int i in new[] { ia, ib, ic }) close &= Math.Abs((v[i] - sc).Dot(outward)) < depth_reach;
            if (!close) continue;
            Vector2 pa = flat(v[ia]), pb = flat(v[ib]), pc = flat(v[ic]);
            double x0 = Math.Min(pa.X, Math.Min(pb.X, pc.X)), x1 = Math.Max(pa.X, Math.Max(pb.X, pc.X));
            double y0 = Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)), y1 = Math.Max(pa.Y, Math.Max(pb.Y, pc.Y));
            if (x1 <= -hw || x0 >= hw || y1 <= bottom || y0 >= top) continue;
            region.Add(t);
        }
        if (region.Count == 0) return null;

        // The grid inside the patch: fine across the cut, coarser in the bark round it.
        double fine_a = 0.075 / k, fine_lo = -0.017 / k, fine_hi = 0.031 / k, crease_band = 0.003 / k;
        List<double> cols = grid_lines(-hw, hw, new[] { (-fine_a, fine_a, FINE / k) }, COARSE / k);
        List<double> rows = grid_lines(bottom, top, new[] { (fine_lo, fine_hi, FINE_ROW / k), (-crease_band, crease_band, FINE_ROW * 0.5 / k) }, COARSE / k);

        // The finished surface at a patch point, host units: in along -outward, and
        // the lift's slope for the bark's normal there.
        double ks = k;
        void finish(double a, double b, out double removed, out double lift, out double ga, out double gb)
        {
            shape(a * ks, b * ks, seed, out removed, out lift);
            removed /= ks;
            lift /= ks;
            double e = 0.0005;
            shape(a * ks + e, b * ks, seed, out _, out double l1);
            shape(a * ks - e, b * ks, seed, out _, out double l2);
            shape(a * ks, b * ks + e, seed, out _, out double l3);
            shape(a * ks, b * ks - e, seed, out _, out double l4);
            ga = (l1 - l2) / (2.0 * e);
            gb = (l3 - l4) / (2.0 * e);
        }

        // Rebuild the surface: the host's vertices stay, the patch's are appended.
        var out_v = new List<Vector3>(v);
        var out_n = new List<Vector3>(nrm);
        var out_t = new List<float>(has_tan ? tan : new float[0]);
        var out_uv = new List<Vector2>(has_uv ? uv : new Vector2[0]);
        var out_uv2 = new List<Vector2>(has_uv2 ? uv2 : new Vector2[0]);
        var out_c = new List<Color>(has_col ? col : new Color[0]);
        var out_i = new List<int>();
        var in_region = new HashSet<int>(region);
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            if (in_region.Contains(t)) continue;
            out_i.Add(idx[t]); out_i.Add(idx[t + 1]); out_i.Add(idx[t + 2]);
        }
        var bark_keys = new Dictionary<(long, long, long, long), int>();
        int bark_vertex(in Attr at, Vector2 p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(at.uv.X * 1e5), (long)Math.Round(at.uv.Y * 1e5));
            if (bark_keys.TryGetValue(key, out int found)) return found;
            Attr o = at;
            bool inside = p.X > -hw && p.X < hw && p.Y > bottom && p.Y < top;
            if (inside)
            {
                finish(p.X, p.Y, out _, out double lift, out double ga, out double gb);
                o.pos += outward * (float)lift;
                o.normal = (o.normal - across * (float)ga - upd * (float)gb).Normalized();
            }
            int index = out_v.Count;
            out_v.Add(o.pos); out_n.Add(o.normal);
            if (has_tan) { out_t.Add(o.tangent.X); out_t.Add(o.tangent.Y); out_t.Add(o.tangent.Z); out_t.Add(o.tangent.W); }
            if (has_uv) out_uv.Add(o.uv);
            if (has_uv2) out_uv2.Add(o.uv2);
            if (has_col) out_c.Add(o.color);
            bark_keys[key] = index;
            return index;
        }

        var wood = new MeshBuilder { use_uv2 = true };
        double covered = 0.0;
        foreach (int t in region)
        {
            int ia = idx[t], ib = idx[t + 1], ic = idx[t + 2];
            Attr aa = attr(ia), ab = attr(ib), ac = attr(ic);
            Vector2 pa = flat(v[ia]), pb = flat(v[ib]), pc = flat(v[ic]);
            double area = signed_area(pa, pb, pc);
            if (Math.Abs(area) < 1e-12) continue;
            Attr at(Vector2 p)
            {
                double wb = signed_area(pa, p, pc) / area, wc = signed_area(pa, pb, p) / area;
                return Attr.mix(aa, ab, ac, 1.0 - wb - wc, wb, wc);
            }
            var tri = new List<Vector2> { pa, pb, pc };
            // Outside the patch the triangle's pieces stay as they were.
            foreach (List<Vector2> piece in new[]
            {
                clip(tri, 0, -hw, true),
                clip(tri, 0, hw, false),
                clip(clip(clip(tri, 0, -hw, false), 0, hw, true), 1, bottom, true),
                clip(clip(clip(tri, 0, -hw, false), 0, hw, true), 1, top, false),
            })
            {
                for (int i = 1; i + 1 < piece.Count; i++)
                {
                    emit(piece[0], piece[i], piece[i + 1], area, (p0, p1, p2) =>
                    {
                        out_i.Add(bark_vertex(at(p0), p0)); out_i.Add(bark_vertex(at(p1), p1)); out_i.Add(bark_vertex(at(p2), p2));
                    });
                }
            }
            // Inside it, the grid's cells clipped to the triangle.
            double x0 = Math.Min(pa.X, Math.Min(pb.X, pc.X)), x1 = Math.Max(pa.X, Math.Max(pb.X, pc.X));
            double y0 = Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)), y1 = Math.Max(pa.Y, Math.Max(pb.Y, pc.Y));
            for (int j = 0; j + 1 < rows.Count; j++)
            {
                if (rows[j + 1] <= y0 || rows[j] >= y1) continue;
                for (int i = 0; i + 1 < cols.Count; i++)
                {
                    if (cols[i + 1] <= x0 || cols[i] >= x1) continue;
                    // Each cell is two triangles on a fixed diagonal, so every cell
                    // splits the same way whichever host triangle holds it.
                    var c00 = new Vector2((float)cols[i], (float)rows[j]);
                    var c10 = new Vector2((float)cols[i + 1], (float)rows[j]);
                    var c11 = new Vector2((float)cols[i + 1], (float)rows[j + 1]);
                    var c01 = new Vector2((float)cols[i], (float)rows[j + 1]);
                    foreach (List<Vector2> half in new[] { new List<Vector2> { c00, c10, c11 }, new List<Vector2> { c00, c11, c01 } })
                    {
                        List<Vector2> piece = clip_to_triangle(half, pa, pb, pc, area);
                        for (int q = 1; q + 1 < piece.Count; q++)
                        {
                            covered += Math.Abs(signed_area(piece[0], piece[q], piece[q + 1]));
                            emit(piece[0], piece[q], piece[q + 1], area, (p0, p1, p2) => patch_triangle(p0, p1, p2));
                        }
                    }
                }
            }

            void patch_triangle(Vector2 p0, Vector2 p1, Vector2 p2)
            {
                var pts = new[] { p0, p1, p2 };
                var removed = new double[3];
                var lifted = new Vector3[3];
                var at3 = new Attr[3];
                bool cut = false;
                for (int q = 0; q < 3; q++)
                {
                    at3[q] = at(pts[q]);
                    finish(pts[q].X, pts[q].Y, out removed[q], out double lift, out _, out _);
                    lifted[q] = at3[q].pos + outward * (float)(lift - removed[q]);
                    cut |= removed[q] > 0.0;
                }
                if (!cut)
                {
                    out_i.Add(bark_vertex(at3[0], p0)); out_i.Add(bark_vertex(at3[1], p1)); out_i.Add(bark_vertex(at3[2], p2));
                    return;
                }
                // The cut's faces are flat-shaded: each stroke's face is a plane, and
                // the facets and the crease between them stay crisp.
                Vector3 n = (lifted[2] - lifted[0]).Cross(lifted[1] - lifted[0]);
                if (n.LengthSquared() < 1e-16f) return;
                n = n.Normalized();
                if (n.Dot(outward) < 0.0f) n = -n;
                Vector3 u_dir = (across - n * n.Dot(across)).Normalized();
                Vector3 v_dir = (upd - n * n.Dot(upd)).Normalized();
                Vector4 tg = MeshBuilder.tangent_for(n, u_dir, v_dir);
                for (int q = 0; q < 3; q++)
                {
                    double am = pts[q].X * ks, bm = pts[q].Y * ks;
                    wood.add_vertex(lifted[q], n, new Vector2((float)am, (float)bm), at3[q].color, tg, null, new Vector2((float)(removed[q] * ks), (float)crease_depth(am)));
                }
                long base_index = wood.vertex_count() - 3;
                wood.add_triangle(base_index, base_index + 1, base_index + 2);
            }
        }
        // The patch must be covered once over: no gap left by the host and no second
        // layer of it (a branch, the far side) folded in.
        double patch_area = 2.0 * hw * (top - bottom);
        if (Math.Abs(covered - patch_area) > patch_area * 0.002)
        {
            GD.PushWarning($"BlazeNotch: host covers {covered / patch_area:P1} of the notch's patch; not carved");
            return null;
        }

        var carved = new Godot.Collections.Array();
        carved.Resize((int)Mesh.ArrayType.Max);
        carved[(int)Mesh.ArrayType.Vertex] = out_v.ToArray();
        carved[(int)Mesh.ArrayType.Normal] = out_n.ToArray();
        if (has_tan) carved[(int)Mesh.ArrayType.Tangent] = out_t.ToArray();
        if (has_uv) carved[(int)Mesh.ArrayType.TexUV] = out_uv.ToArray();
        if (has_uv2) carved[(int)Mesh.ArrayType.TexUV2] = out_uv2.ToArray();
        if (has_col) carved[(int)Mesh.ArrayType.Color] = out_c.ToArray();
        carved[(int)Mesh.ArrayType.Index] = out_i.ToArray();

        // Same surfaces, same materials, fresh LODs (as the host was committed).
        ArrayMesh mesh;
        using (ImporterMesh importer = new ImporterMesh())
        {
            for (int s = 0; s < host.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array data = s == surface ? carved : host.SurfaceGetArrays(s);
                uint format = (uint)host.Call("surface_get_format", s).AsInt64() & (uint)Mesh.ArrayFormat.FormatCustomMask;
                importer.AddSurface(Mesh.PrimitiveType.Triangles, data, new Godot.Collections.Array<Godot.Collections.Array>(), new Godot.Collections.Dictionary(), host.SurfaceGetMaterial(s), host.SurfaceGetName(s), format);
            }
            importer.GenerateLods(25.0f, 60.0f, new Godot.Collections.Array());
            mesh = importer.GetMesh();
        }
        if (wood.is_empty()) return null;
        wood.commit(null, false, mesh);
        return new Carved { mesh = mesh, cut_surface = mesh.GetSurfaceCount() - 1, surface_centre = sc, outward = outward };
    }

    /// Emits (p0, p1, p2) wound as the host triangle was (the sign of `area`).
    static void emit(Vector2 p0, Vector2 p1, Vector2 p2, double area, Action<Vector2, Vector2, Vector2> add)
    {
        double s = signed_area(p0, p1, p2);
        if (Math.Abs(s) < 1e-14) return;
        if (Math.Sign(s) == Math.Sign(area)) add(p0, p1, p2);
        else add(p0, p2, p1);
    }

    static List<double> grid_lines(double lo, double hi, (double from, double to, double step)[] bands, double coarse)
    {
        var lines = new List<double>();
        double x = lo;
        lines.Add(lo);
        while (x < hi - 1e-9)
        {
            double step = coarse;
            foreach (var band in bands)
            {
                if (x >= band.from - 1e-9 && x < band.to - 1e-9) step = Math.Min(step, band.step);
                else if (x < band.from && x + step > band.from) step = Math.Min(step, band.from - x);
            }
            x = Math.Min(x + step, hi);
            lines.Add(x);
        }
        // The crease (and the patch's middle) always falls on a line.
        int nearest = 0;
        for (int i = 1; i < lines.Count; i++) if (Math.Abs(lines[i]) < Math.Abs(lines[nearest])) nearest = i;
        if (nearest > 0 && nearest < lines.Count - 1) lines[nearest] = 0.0;
        return lines;
    }

    static double signed_area(Vector2 a, Vector2 b, Vector2 c) => 0.5 * ((double)(b.X - a.X) * (c.Y - a.Y) - (double)(c.X - a.X) * (b.Y - a.Y));

    /// Sutherland-Hodgman against one axis-aligned line: keeps the part with
    /// coordinate `axis` below `value` (`keep_below`) or above it.
    static List<Vector2> clip(List<Vector2> poly, int axis, double value, bool keep_below)
    {
        var result = new List<Vector2>();
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
            double dp = (axis == 0 ? p.X : p.Y) - value, dq = (axis == 0 ? q.X : q.Y) - value;
            if (!keep_below) { dp = -dp; dq = -dq; }
            if (dp <= 0.0) result.Add(p);
            if ((dp < 0.0 && dq > 0.0) || (dp > 0.0 && dq < 0.0))
            {
                Vector2 x = p + (q - p) * (float)(dp / (dp - dq));
                if (axis == 0) x.X = (float)value; else x.Y = (float)value;
                result.Add(x);
            }
        }
        return result.Count >= 3 ? result : new List<Vector2>();
    }

    /// Clips a convex polygon to the triangle (a, b, c).
    static List<Vector2> clip_to_triangle(List<Vector2> poly, Vector2 a, Vector2 b, Vector2 c, double area)
    {
        double sign = Math.Sign(area);
        foreach ((Vector2 p, Vector2 q) in new[] { (a, b), (b, c), (c, a) })
        {
            var result = new List<Vector2>();
            for (int i = 0; i < poly.Count && poly.Count >= 3; i++)
            {
                Vector2 s = poly[i], e = poly[(i + 1) % poly.Count];
                double ds = sign * signed_area(p, q, s), de = sign * signed_area(p, q, e);
                if (ds >= 0.0) result.Add(s);
                if ((ds > 0.0 && de < 0.0) || (ds < 0.0 && de > 0.0)) result.Add(s + (e - s) * (float)(ds / (ds - de)));
            }
            poly = result;
            if (poly.Count < 3) return new List<Vector2>();
        }
        return poly;
    }

    // ------------------------------------------------------------ noise

    static double hash(long x, long y, ulong seed)
    {
        ulong h = (ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)y * 0xC2B2AE3D27D4EB4FUL ^ seed * 0x165667B19E3779F9UL;
        h ^= h >> 29;
        h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 32;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }

    /// Smooth value noise in [0, 1].
    public static double noise(double x, double y, ulong seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        long ix = (long)fx, iy = (long)fy;
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * (3.0 - 2.0 * tx);
        ty = ty * ty * (3.0 - 2.0 * ty);
        double a = hash(ix, iy, seed), b = hash(ix + 1, iy, seed), c = hash(ix, iy + 1, seed), d = hash(ix + 1, iy + 1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    static double fbm(double x, double y, ulong seed) => noise(x, y, seed) * 0.65 + noise(x * 2.3 + 7.1, y * 2.3 - 3.7, seed + 1) * 0.35;

    static double smoothstep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    public static ShaderMaterial cut_material(bool bends_with_tree)
    {
        var mat = new ShaderMaterial { Shader = Content.Load<Shader>("res://shaders/blaze_cut.gdshader") };
        Camp.bind_prop_pbr(mat, "wood");
        mat.SetShaderParameter("wind_response", bends_with_tree ? 1.0 : 0.0);
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
}
