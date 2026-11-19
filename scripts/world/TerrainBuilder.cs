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

/// Turns a `TerrainField` into renderable and collidable geometry.
///
/// One continuous grid: a uniform 0.5 m lattice under the camp (its heights come straight
/// from the field's baked grid, so mesh, collision and scattered plants agree
/// exactly) that grows geometrically towards the hills 700 m away with no LOD
/// seams. Render chunks retain the exact border attributes; one static triangle
/// collision covers the same grid, including the distant hills.
public partial class TerrainBuilder
{
    public const double INNER_SPACING = 0.5;
    public const double INNER_UNIFORM_HALF = 64.0;
    public const double OUTER_GROWTH = 1.08;
    /// Fine noise needs supporting geometry: unbounded growth reached 47 m cells,
    /// leaving distant plants several metres above or below the visible hills.
    public const double OUTER_MAX_SPACING = 4.0;
    public const double COLLISION_HALF = 85.0;

    public TerrainField field;
    public List<float> _axis = new List<float>();

    public TerrainBuilder(TerrainField p_field)
    {
        field = p_field;
        _axis = axis_coordinates();
        field.bake_surface_grid(_axis);
    }

    public static List<float> axis_coordinates()
    {
        /// Symmetric vertex coordinates along one axis: 0.5 m steps out to
        /// INNER_UNIFORM_HALF, then steps growing by OUTER_GROWTH to the world edge.
        List<float> positive = new List<float>();
        double x = 0.0;
        while (x < INNER_UNIFORM_HALF - 1e-4)
        {
            positive.Add((float)x);
            x += INNER_SPACING;
        }
        positive.Add((float)INNER_UNIFORM_HALF);
        double step = INNER_SPACING;
        double edge = TerrainField.OUTER_EXTENT * 0.5;
        while (x < edge)
        {
            step = minf(step * OUTER_GROWTH, OUTER_MAX_SPACING);
            x = minf(x + step, edge);
            positive.Add((float)x);
        }
        List<float> axis = new List<float>();
        for (long i = (long)positive.Count - 1; i > 0; i += -1)
        {
            axis.Add(-positive[(int)i]);
        }
        axis.AddRange(positive);
        return axis;
    }

    public long resolution()
    {
        return (long)_axis.Count;
    }

    /// The terrain grid as plain arrays: safe to build on a worker thread, since it
    /// creates no engine object. The main thread turns it into meshes and shapes.
    public sealed class Grid
    {
        public Vector3[] positions = Array.Empty<Vector3>();
        public Vector3[] normals = Array.Empty<Vector3>();
        public Vector2[] uvs = Array.Empty<Vector2>();
        public Color[] colors = Array.Empty<Color>();
        public int[] indices = Array.Empty<int>();

        /// The triangles as a flat face list, in index order (what a trimesh shape takes).
        public Vector3[] faces()
        {
            var result = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++) result[i] = positions[indices[i]];
            return result;
        }
    }

    /// One mesh of the whole grid (main thread).
    public ArrayMesh build_mesh(Material material) => mesh_from(build_grid(), material);

    public static ArrayMesh mesh_from(Grid grid, Material material)
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = grid.positions;
        arrays[(int)Mesh.ArrayType.Normal] = grid.normals;
        arrays[(int)Mesh.ArrayType.TexUV] = grid.uvs;
        arrays[(int)Mesh.ArrayType.Color] = grid.colors;
        arrays[(int)Mesh.ArrayType.Index] = grid.indices;
        ArrayMesh mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null)
        {
            mesh.SurfaceSetMaterial(0, material);
        }
        return mesh;
    }

    /// The terrain's collision (main thread): the same triangles as the render grid.
    public static ConcavePolygonShape3D collision_from(Grid grid) => new ConcavePolygonShape3D { Data = grid.faces() };

    public Grid build_grid()
    {
        long n = (long)_axis.Count;
        long count = n * n;
        List<float> heights = field.surface_heights;
        List<Vector3> vertices = new List<Vector3>();
        G.resize(vertices, (int)count);
        for (long iz = 0; iz < n; iz++)
        {
            double z = _axis[(int)iz];
            long row = iz * n;
            for (long ix = 0; ix < n; ix++)
            {
                double x = _axis[(int)ix];
                double h = heights[(int)(row + ix)];
                vertices[(int)(row + ix)] = new Vector3((float)x, (float)h, (float)z);
            }
        }

        List<Vector3> normals = new List<Vector3>();
        G.resize(normals, (int)count);
        for (long iz2 = 0; iz2 < n; iz2++)
        {
            long iz0 = maxi(iz2 - 1, 0);
            long iz1 = mini(iz2 + 1, n - 1);
            double dz = (double)_axis[(int)iz1] - _axis[(int)iz0];
            for (long ix2 = 0; ix2 < n; ix2++)
            {
                long ix0 = maxi(ix2 - 1, 0);
                long ix1 = mini(ix2 + 1, n - 1);
                double dx = (double)_axis[(int)ix1] - _axis[(int)ix0];
                double hl = heights[(int)(iz2 * n + ix0)];
                double hr = heights[(int)(iz2 * n + ix1)];
                double hd = heights[(int)(iz0 * n + ix2)];
                double hu = heights[(int)(iz1 * n + ix2)];
                normals[(int)(iz2 * n + ix2)] = new Vector3((float)((hl - hr) / dx), 1.0f, (float)((hd - hu) / dz)).Normalized();
            }
        }
        // Material weights need the slope, which the normals now provide.

        List<Color> colors = new List<Color>();
        G.resize(colors, (int)count);
        List<Vector2> uvs = new List<Vector2>();
        G.resize(uvs, (int)count);
        for (long iz3 = 0; iz3 < n; iz3++)
        {
            double z2 = _axis[(int)iz3];
            for (long ix3 = 0; ix3 < n; ix3++)
            {
                long idx = iz3 * n + ix3;
                double slope = acos(clampf(normals[(int)idx].Y, -1.0, 1.0));
                colors[(int)idx] = field.material_mask(_axis[(int)ix3], z2, heights[(int)idx], slope);
                uvs[(int)idx] = new Vector2(_axis[(int)ix3], (float)z2);
            }
        }

        List<int> indices = new List<int>();
        G.resize(indices, (int)((n - 1) * (n - 1) * 6));
        long k = 0;
        for (long iz4 = 0, iz_end = n - 1; iz4 < iz_end; iz4++)
        {
            for (long ix4 = 0, ix_end = n - 1; ix4 < ix_end; ix4++)
            {
                long a = iz4 * n + ix4;
                long b = a + 1;
                long c = a + n;
                long d = c + 1;
                // Clockwise as seen from above (+Y): a -> b -> d -> c.
                indices[(int)k] = (int)a;
                indices[(int)(k + 1)] = (int)b;
                indices[(int)(k + 2)] = (int)d;
                indices[(int)(k + 3)] = (int)a;
                indices[(int)(k + 4)] = (int)d;
                indices[(int)(k + 5)] = (int)c;
                k += 6;
            }
        }

        return new Grid { positions = vertices.ToArray(), normals = normals.ToArray(), uvs = uvs.ToArray(), colors = colors.ToArray(), indices = indices.ToArray() };
    }

    /// Split the existing grid without resampling. Shared border vertices keep
    /// identical normals, material weights and heights, while each render pass
    /// can reject whole chunks outside its frustum and shadow cascades.
    public static Godot.Collections.Array<ArrayMesh> split_render_mesh(ArrayMesh mesh, int cells = 64)
    {
        using var source = mesh.SurfaceGetArrays(0);
        var grid = new Grid
        {
            positions = source[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
            normals = source[(int)Mesh.ArrayType.Normal].AsVector3Array(),
            uvs = source[(int)Mesh.ArrayType.TexUV].AsVector2Array(),
            colors = source[(int)Mesh.ArrayType.Color].AsColorArray(),
        };
        return split_render_grid(grid, mesh.SurfaceGetMaterial(0), cells);
    }

    /// The grid split into render chunks (main thread: it creates the meshes).
    public static Godot.Collections.Array<ArrayMesh> split_render_grid(Grid grid, Material material, int cells = 64)
    {
        if (cells < 1) throw new ArgumentOutOfRangeException(nameof(cells));
        Vector3[] positions = grid.positions;
        Vector3[] normals = grid.normals;
        Vector2[] uv = grid.uvs;
        Color[] colors = grid.colors;
        int n = (int)Math.Sqrt(positions.Length);
        if (n * n != positions.Length || n < 2) throw new ArgumentException("Expected a square terrain grid", nameof(grid));
        var result = new Godot.Collections.Array<ArrayMesh>();
        for (int z = 0; z < n - 1; z += cells)
            for (int x = 0; x < n - 1; x += cells)
            {
                int width = Math.Min(cells, n - 1 - x) + 1;
                int depth = Math.Min(cells, n - 1 - z) + 1;
                var p = new Vector3[width * depth];
                var normal = new Vector3[p.Length];
                var tex = new Vector2[p.Length];
                var color = new Color[p.Length];
                for (int row = 0; row < depth; row++)
                {
                    int first = (z + row) * n + x, dest = row * width;
                    Array.Copy(positions, first, p, dest, width);
                    Array.Copy(normals, first, normal, dest, width);
                    Array.Copy(uv, first, tex, dest, width);
                    Array.Copy(colors, first, color, dest, width);
                }
                var indices = new int[(width - 1) * (depth - 1) * 6];
                int k = 0;
                for (int row = 0; row < depth - 1; row++)
                    for (int col = 0; col < width - 1; col++)
                    {
                        int a = row * width + col, b = a + 1, c = a + width, d = c + 1;
                        indices[k++] = a; indices[k++] = b; indices[k++] = d;
                        indices[k++] = a; indices[k++] = d; indices[k++] = c;
                    }
                using var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = p;
                arrays[(int)Mesh.ArrayType.Normal] = normal;
                arrays[(int)Mesh.ArrayType.TexUV] = tex;
                arrays[(int)Mesh.ArrayType.Color] = color;
                arrays[(int)Mesh.ArrayType.Index] = indices;
                var chunk = new ArrayMesh();
                chunk.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                chunk.SurfaceSetMaterial(0, material);
                result.Add(chunk);
            }
        return result;
    }

}
