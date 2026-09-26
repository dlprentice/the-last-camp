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

/// Accumulates triangle geometry in packed arrays and commits it as an
/// ArrayMesh surface. Much faster than SurfaceTool for large procedural meshes
/// and supports the CUSTOM0 attribute (used for foliage pivots and wind data).
public partial class MeshBuilder : RefCounted
{
    public List<Vector3> vertices = new List<Vector3>();
    public List<Vector3> normals = new List<Vector3>();
    public List<float> tangents = new List<float>();
    public List<Vector2> uvs = new List<Vector2>();
    public List<Vector2> uv2s = new List<Vector2>();
    public List<Color> colors = new List<Color>();
    public List<float> custom0 = new List<float>();
    public List<int> indices = new List<int>();

    public bool use_tangents = true;
    public bool use_uv2 = false;
    public bool use_colors = true;
    public bool use_custom0 = false;

    public long vertex_count()
    {
        return (long)vertices.Count;
    }

    public long triangle_count()
    {
        return (long)indices.Count / 3;
    }

    public long add_vertex(Vector3 position, Vector3 normal, Vector2 uv, Color? color_opt = null, Vector4? tangent_opt = null, Color? custom_opt = null, Vector2? uv2_opt = null)
    {
        Color color = color_opt ?? Colors.White;
        Vector4 tangent = tangent_opt ?? new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        Color custom = custom_opt ?? new Color(0, 0, 0, 0);
        Vector2 uv2 = uv2_opt ?? Vector2.Zero;
        /// Adds one vertex and returns its index.
        vertices.Add(position);
        normals.Add(normal);
        uvs.Add(uv);
        if (use_colors)
        {
            colors.Add(color);
        }
        if (use_tangents)
        {
            tangents.Add(tangent.X);
            tangents.Add(tangent.Y);
            tangents.Add(tangent.Z);
            tangents.Add(tangent.W);
        }
        if (use_custom0)
        {
            custom0.Add(custom.R);
            custom0.Add(custom.G);
            custom0.Add(custom.B);
            custom0.Add(custom.A);
        }
        if (use_uv2)
        {
            uv2s.Add(uv2);
        }
        return (long)vertices.Count - 1;
    }

    public void add_triangle(long a, long b, long c)
    {
        indices.Add((int)a);
        indices.Add((int)b);
        indices.Add((int)c);
    }

    public void add_quad_indices(long a, long b, long c, long d)
    {
        /// Corners are given counter-clockwise as seen from the front; Godot treats
        /// clockwise triangles as front-facing, so the emitted order is reversed.
        add_triangle(a, c, b);
        add_triangle(a, d, c);
    }

    public void add_quad_facing(long a, long b, long c, long d, Vector3 outward)
    {
        /// Quad a-b-c-d (a ring order, either winding) whose front should face
        /// `outward`; the winding is chosen accordingly.
        Vector3 geometric = (vertices[(int)b] - vertices[(int)a]).Cross(vertices[(int)c] - vertices[(int)a]);
        if (geometric.Dot(outward) > 0.0)
        {
            add_quad_indices(a, b, c, d);
        }
        else
        {
            add_quad_indices(a, d, c, b);
        }
    }

    public void add_quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color? color_opt = null, Color? custom_opt = null, Rect2? uv_rect_opt = null)
    {
        Color color = color_opt ?? Colors.White;
        Color custom = custom_opt ?? new Color(0, 0, 0, 0);
        Rect2 uv_rect = uv_rect_opt ?? new Rect2(0.0f, 0.0f, 1.0f, 1.0f);
        /// Adds a planar quad (p0..p3 counter-clockwise as seen from the front) with a
        /// flat normal and tangent along the p0->p1 edge. UVs: p0=(0,1) p1=(1,1)
        /// p2=(1,0) p3=(0,0) so the image top maps to the p2/p3 edge.
        Vector3 n = (p1 - p0).Cross(p3 - p0).Normalized();
        Vector3 t = (p1 - p0).Normalized();
        Vector4 tangent = new Vector4(t.X, t.Y, t.Z, 1.0f);
        double u0 = uv_rect.Position.X;
        double v0 = uv_rect.Position.Y;
        double u1 = uv_rect.End.X;
        double v1 = uv_rect.End.Y;
        long a = add_vertex(p0, n, new Vector2((float)u0, (float)v1), color, tangent, custom);
        long b = add_vertex(p1, n, new Vector2((float)u1, (float)v1), color, tangent, custom);
        long c = add_vertex(p2, n, new Vector2((float)u1, (float)v0), color, tangent, custom);
        long d = add_vertex(p3, n, new Vector2((float)u0, (float)v0), color, tangent, custom);
        add_quad_indices(a, b, c, d);
    }

    public void add_box(Vector3 size, Color? color_opt = null, double uv_scale = 1.0)
    {
        Color color = color_opt ?? Colors.White;
        /// Axis-aligned box centred on the origin. Faces are authored from the outside
        /// so the existing clockwise winding stays consistent.
        Vector3 h = size * 0.5f;
        double ux = size.X * uv_scale;
        double uy = size.Y * uv_scale;
        double uz = size.Z * uv_scale;
        // +Y
        add_quad(new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)ux, (float)uz));
        // -Y
        add_quad(new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(-h.X, -h.Y, -h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)ux, (float)uz));
        // +X
        add_quad(new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(h.X, h.Y, -h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)uz, (float)uy));
        // -X
        add_quad(new Vector3(-h.X, -h.Y, h.Z), new Vector3(-h.X, -h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)uz, (float)uy));
        // +Z
        add_quad(new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)ux, (float)uy));
        // -Z
        add_quad(new Vector3(h.X, -h.Y, -h.Z), new Vector3(-h.X, -h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), color, new Color(0, 0, 0, 0), new Rect2(0, 0, (float)ux, (float)uy));
    }

    public Godot.Collections.Dictionary add_tube(Godot.Collections.Array<Vector3> points, Godot.Collections.Array<double> radii, long sides, Color? color_opt = null, double u_repeats = 1.0, double v_per_metre = 1.0, double start_v = 0.0, bool cap_end = false, Callable? ring_color_fn_opt = null)
    {
        Color color = color_opt ?? Colors.White;
        Callable ring_color_fn = ring_color_fn_opt ?? new Callable();
        /// Sweeps a ring of `sides` vertices along a polyline with per-point radii,
        /// using parallel-transport frames so the texture never twists. `v_scale`
        /// controls texture repeats along the length (in metres per repeat, inverted).
        /// Returns the frame (side, up vectors) at the last point for continuing tubes.
        G.assert((long)points.Count >= 2 && (long)radii.Count == (long)points.Count);
        long n = (long)points.Count;
        Godot.Collections.Array<Vector3> tangents_along = new Godot.Collections.Array<Vector3>();
        for (long i = 0; i < n; i++)
        {
            Vector3 t = default;
            if (i == 0)
            {
                t = points[1] - points[0];
            }
            else if (i == n - 1)
            {
                t = points[(int)i] - points[(int)(i - 1)];
            }
            else
            {
                t = points[(int)(i + 1)] - points[(int)(i - 1)];
            }
            tangents_along.Add(t.LengthSquared() > 1e-10 ? t.Normalized() : Vector3.Up);
        }
        // Initial right-handed frame (side, up, tangent) perpendicular to the first
        // tangent; right-handedness makes the ring winding come out clockwise.

        Vector3 t0 = tangents_along[0];
        Vector3 side = t0.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-6)
        {
            side = t0.Cross(Vector3.Right);
        }
        side = side.Normalized();
        Vector3 up = t0.Cross(side).Normalized();

        Godot.Collections.Array<long> ring_start = new Godot.Collections.Array<long>();
        double v = start_v;
        for (long i2 = 0; i2 < n; i2++)
        {
            if (i2 > 0)
            {
                // Parallel transport: rotate the previous frame by the rotation that
                // takes the previous tangent to the current one.
                Vector3 prev_t = tangents_along[(int)(i2 - 1)];
                Vector3 cur_t = tangents_along[(int)i2];
                Vector3 axis = prev_t.Cross(cur_t);
                double s = axis.Length();
                if (s > 1e-6)
                {
                    double angle = asin(clampf(s, -1.0, 1.0));
                    if (prev_t.Dot(cur_t) < 0.0)
                    {
                        angle = PI - angle;
                    }
                    side = side.Rotated(axis / (float)s, (float)angle);
                    up = up.Rotated(axis / (float)s, (float)angle);
                }
                v += points[(int)i2].DistanceTo(points[(int)(i2 - 1)]) * v_per_metre;
            }
            ring_start.Add((long)vertices.Count);
            double r = radii[(int)i2];
            Color ring_color = color;
            if (G.is_valid(ring_color_fn))
            {
                ring_color = ring_color_fn.Call(i2).AsColor();
            }
            for (long sIdx = 0, sIdx_end = sides + 1; sIdx < sIdx_end; sIdx++)
            {
                double a = (double)sIdx / (double)sides * TAU;
                Vector3 normal = (side * (float)cos(a) + up * (float)sin(a)).Normalized();
                Vector3 tangent_vec = (-side * (float)sin(a) + up * (float)cos(a)).Normalized();
                // cross(normal, tangent) points along +v here; Godot's binormal must
                // point towards the image top (-v), hence the negative sign.
                add_vertex(points[(int)i2] + normal * (float)r, normal, new Vector2((float)((double)sIdx / (double)sides * u_repeats), (float)v), ring_color, new Vector4(tangent_vec.X, tangent_vec.Y, tangent_vec.Z, -1.0f));
            }
        }

        for (long i3 = 0, i_end = n - 1; i3 < i_end; i3++)
        {
            long r0 = ring_start[(int)i3];
            long r1 = ring_start[(int)(i3 + 1)];
            for (long sIdx2 = 0; sIdx2 < sides; sIdx2++)
            {
                long a2 = r0 + sIdx2;
                long b = r0 + sIdx2 + 1;
                long c = r1 + sIdx2 + 1;
                long d = r1 + sIdx2;
                add_triangle(a2, c, b);
                add_triangle(a2, d, c);
            }
        }

        if (cap_end)
        {
            Vector3 centre = points[(int)(n - 1)];
            Vector3 cn = tangents_along[(int)(n - 1)];
            long ci = add_vertex(centre, cn, new Vector2(0.5f, 0.5f), color, new Vector4(side.X, side.Y, side.Z, 1.0f));
            long last_ring = ring_start[(int)(n - 1)];
            for (long sIdx3 = 0; sIdx3 < sides; sIdx3++)
            {
                long a3 = last_ring + sIdx3;
                long b2 = last_ring + sIdx3 + 1;
                add_triangle(ci, b2, a3);
            }
        }
        return new Godot.Collections.Dictionary { { (StringName)"side", side }, { (StringName)"up", up }, { (StringName)"v", v } };
    }

    public void add_displaced_sphere(long rings, long segments, double radius, Callable displace, Color? color_opt = null, double uv_scale = 1.0)
    {
        Color color = color_opt ?? Colors.White;
        /// Adds a UV sphere deformed by `displace(dir: Vector3) -> float` (radius
        /// multiplier), with smooth normals recomputed from the displaced surface.
        long @base = (long)vertices.Count;
        Godot.Collections.Array<Vector3> positions = new Godot.Collections.Array<Vector3>();
        for (long r = 0, r_end = rings + 1; r < r_end; r++)
        {
            double phi = PI * (double)r / (double)rings;
            for (long s = 0, s_end = segments + 1; s < s_end; s++)
            {
                double theta = TAU * (double)s / (double)segments;
                Vector3 dir = new Vector3((float)(sin(phi) * cos(theta)), (float)cos(phi), (float)(sin(phi) * sin(theta)));
                Vector3 p = dir * (float)radius * (float)G.to_float(displace.Call(dir));
                positions.Add(p);
            }
        }
        for (long r2 = 0, r_end2 = rings + 1; r2 < r_end2; r2++)
        {
            for (long s2 = 0, s_end2 = segments + 1; s2 < s_end2; s2++)
            {
                long idx = r2 * (segments + 1) + s2;
                Vector3 p2 = positions[(int)idx];
                Vector3 n = _sphere_grid_normal(positions, rings, segments, r2, s2);
                Vector2 uv = new Vector2((float)((double)s2 / (double)segments), (float)((double)r2 / (double)rings)) * (float)uv_scale;
                Vector3 tangent = n.Cross(Vector3.Up);
                if (tangent.LengthSquared() < 1e-6)
                {
                    tangent = Vector3.Right;
                }
                tangent = tangent.Normalized();
                add_vertex(p2, n, uv, color, new Vector4(tangent.X, tangent.Y, tangent.Z, -1.0f));
            }
        }
        for (long r3 = 0; r3 < rings; r3++)
        {
            for (long s3 = 0; s3 < segments; s3++)
            {
                long a = @base + r3 * (segments + 1) + s3;
                long b = a + 1;
                long c = a + segments + 1;
                long d = c + 1;
                // Clockwise from outside: a -> c -> d -> b. Skip the degenerate
                // triangle at each pole.
                if (r3 < rings - 1)
                {
                    add_triangle(a, c, d);
                }
                if (r3 > 0)
                {
                    add_triangle(a, d, b);
                }
            }
        }
    }

    public Vector3 _sphere_grid_normal(Godot.Collections.Array<Vector3> positions, long rings, long segments, long r, long s)
    {
        Callable idx = Callable.From((long rr, long ss) =>
{
    rr = clampi(rr, 0, rings);
    ss = posmod(ss, segments);
    return positions[(int)(rr * (segments + 1) + ss)];
});
        Vector3 p = idx.Call(r, s).AsVector3();
        Vector3 right = idx.Call(r, s + 1).AsVector3();
        Vector3 left = idx.Call(r, s - 1).AsVector3();
        Vector3 down = idx.Call(r + 1, s).AsVector3();
        Vector3 up = idx.Call(r - 1, s).AsVector3();
        if (r == 0 || r == rings)
        {
            return p.Normalized();
        }
        Vector3 du = right - left;
        Vector3 dv = down - up;
        Vector3 n = dv.Cross(du);
        if (n.LengthSquared() < 1e-12 || n.Dot(p) < 0.0)
        {
            n = n.Dot(p) < 0.0 ? -n : p;
        }
        return n.Normalized();
    }

    public void recompute_normals()
    {
        /// Recomputes smooth per-vertex normals from the triangle list (area weighted).
        List<Vector3> acc = new List<Vector3>();
        G.resize(acc, (int)(long)vertices.Count);
        G.fill(acc, Vector3.Zero);
        for (long i = 0, i_end = (long)indices.Count; i < i_end; i += 3)
        {
            long a = indices[(int)i];
            long b = indices[(int)(i + 1)];
            long c = indices[(int)(i + 2)];
            Vector3 n = (vertices[(int)b] - vertices[(int)a]).Cross(vertices[(int)c] - vertices[(int)a]);
            acc[(int)a] += n;
            acc[(int)b] += n;
            acc[(int)c] += n;
        }
        for (long i2 = 0, i_end2 = (long)acc.Count; i2 < i_end2; i2++)
        {
            Vector3 n2 = acc[(int)i2];
            // Retain the authored outward direction. Clockwise front faces have
            // the opposite cross-product sign from the usual mathematical normal.
            if (n2.Dot(normals[(int)i2]) < 0.0)
            {
                n2 = -n2;
            }
            normals[(int)i2] = n2.LengthSquared() > 1e-12 ? n2.Normalized() : normals[(int)i2];
        }
    }

    public bool is_empty()
    {
        return (vertices.Count == 0);
    }

    public void recompute_tangents()
    {
        /// UV-derived tangent frames, including mirrored fabric panels. Call after
        /// smoothing normals so normal maps follow the finished surface.
        List<Vector3> us = new List<Vector3>();
        List<Vector3> vs = new List<Vector3>();
        G.resize(us, (int)(long)vertices.Count);
        G.resize(vs, (int)(long)vertices.Count);
        for (long i = 0, i_end = (long)indices.Count; i < i_end; i += 3)
        {
            long a = indices[(int)i];
            long b = indices[(int)(i + 1)];
            long c = indices[(int)(i + 2)];
            Vector3 e1 = vertices[(int)b] - vertices[(int)a];
            Vector3 e2 = vertices[(int)c] - vertices[(int)a];
            Vector2 uv1 = uvs[(int)b] - uvs[(int)a];
            Vector2 uv2 = uvs[(int)c] - uvs[(int)a];
            double determinant = (double)uv1.X * uv2.Y - (double)uv1.Y * uv2.X;
            if (absf(determinant) < 1e-10)
            {
                continue;
            }
            Vector3 u = (e1 * uv2.Y - e2 * uv1.Y) / (float)determinant;
            Vector3 v = (e2 * uv1.X - e1 * uv2.X) / (float)determinant;
            foreach (Variant index_item in new Godot.Collections.Array { a, b, c })
            {
                long index = index_item.AsInt64();
                us[(int)index] += u;
                vs[(int)index] += v;
            }
        }
        G.resize(tangents, (int)((long)vertices.Count * 4));
        for (long i3 = 0, i_end2 = (long)vertices.Count; i3 < i_end2; i3++)
        {
            Vector4 t = tangent_for(normals[(int)i3], us[(int)i3], vs[(int)i3]);
            for (long axis = 0; axis < 4; axis++)
            {
                tangents[(int)(i3 * 4 + axis)] = t[(int)axis];
            }
        }
    }

    public void remap_uv(long from_index, double u_scale, double u_offset, double v_scale = 1.0, double v_offset = 0.0)
    {
        /// Rescales the UVs of every vertex added since `from_index`, e.g. to confine a
        /// tube's u range to one plank strip of the wood texture.
        for (long i = from_index, i_end = (long)uvs.Count; i < i_end; i++)
        {
            Vector2 uv = uvs[(int)i];
            uvs[(int)i] = new Vector2((float)(uv.X * u_scale + u_offset), (float)(uv.Y * v_scale + v_offset));
        }
    }

    public void tint_from(long from_index, Color color)
    {
        /// Tints every vertex added since `from_index`.
        if (!use_colors)
        {
            return;
        }
        for (long i = from_index, i_end = (long)colors.Count; i < i_end; i++)
        {
            colors[(int)i] = color;
        }
    }

    public static Vector4 tangent_for(Vector3 normal, Vector3 u_dir, Vector3 v_dir)
    {
        /// Tangent for a surface point given its normal, the direction in which the
        /// texture's u grows and the direction in which v grows. The sign is chosen so
        /// that Godot's binormal points towards the image top (decreasing v).
        Vector3 t = u_dir - normal * normal.Dot(u_dir);
        if (t.LengthSquared() < 1e-10)
        {
            t = normal.Cross(Vector3.Up);
            if (t.LengthSquared() < 1e-10)
            {
                t = Vector3.Right;
            }
        }
        t = t.Normalized();
        double w = normal.Cross(t).Dot(-v_dir) >= 0.0 ? 1.0 : -1.0;
        return new Vector4(t.X, t.Y, t.Z, (float)w);
    }

    public Godot.Collections.Array _arrays()
    {
        Godot.Collections.Array arrays = new Godot.Collections.Array();
        G.resize(arrays, (int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Variant.From(vertices.ToArray());
        arrays[(int)Mesh.ArrayType.Normal] = Variant.From(normals.ToArray());
        arrays[(int)Mesh.ArrayType.TexUV] = Variant.From(uvs.ToArray());
        arrays[(int)Mesh.ArrayType.Index] = Variant.From(indices.ToArray());
        if (use_tangents)
        {
            arrays[(int)Mesh.ArrayType.Tangent] = Variant.From(tangents.ToArray());
        }
        if (use_colors)
        {
            arrays[(int)Mesh.ArrayType.Color] = Variant.From(colors.ToArray());
        }
        if (use_custom0)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = Variant.From(custom0.ToArray());
        }
        if (use_uv2)
        {
            arrays[(int)Mesh.ArrayType.TexUV2] = Variant.From(uv2s.ToArray());
        }
        return arrays;
    }

    public long _format_flags()
    {
        long flags = 0;
        if (use_custom0)
        {
            flags |= (long)Mesh.ArrayCustomFormat.RgbaFloat << (int)(long)Mesh.ArrayFormat.FormatCustom0Shift;
        }
        return flags;
    }

    public ArrayMesh commit(Material material = null, bool generate_lods = false, ArrayMesh existing = null)
    {
        /// Commits the geometry as a single surface. With `generate_lods` the mesh is
        /// routed through ImporterMesh so the renderer gets automatic discrete LODs.
        if (generate_lods)
        {
            ImporterMesh importer = new ImporterMesh();
            if (existing != null)
            {
                for (long s = 0, s_end = existing.GetSurfaceCount(); s < s_end; s++)
                {
                    importer.AddSurface(Mesh.PrimitiveType.Triangles, existing.SurfaceGetArrays((int)s), new Godot.Collections.Array<Godot.Collections.Array>(), new Godot.Collections.Dictionary(), existing.SurfaceGetMaterial((int)s), existing.SurfaceGetName((int)s), (uint)existing.Call("surface_get_format", s).AsInt64());
                }
            }
            importer.AddSurface(Mesh.PrimitiveType.Triangles, _arrays(), new Godot.Collections.Array<Godot.Collections.Array>(), new Godot.Collections.Dictionary(), material, "", (uint)_format_flags());
            importer.GenerateLods(25.0f, 60.0f, new Godot.Collections.Array());
            return importer.GetMesh();
        }
        ArrayMesh mesh = existing != null ? existing : new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _arrays(), new Godot.Collections.Array<Godot.Collections.Array>(), new Godot.Collections.Dictionary(), (Mesh.ArrayFormat)_format_flags());
        if (material != null)
        {
            mesh.SurfaceSetMaterial((int)((long)mesh.GetSurfaceCount() - 1), material);
        }
        return mesh;
    }
}
