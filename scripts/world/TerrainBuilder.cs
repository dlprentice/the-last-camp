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
/// One mesh: a uniform 0.5 m lattice under the camp (its heights come straight
/// from the field's baked grid, so mesh, collision and scattered plants agree
/// exactly) that grows geometrically towards the hills 700 m away with no LOD
/// seams. Collision is the same grid as a HeightMapShape3D, plus a coarse
/// outer heightfield so the walkable world does not end at the fine grid.
public partial class TerrainBuilder
{
    public const double INNER_SPACING = 0.5;
    public const double INNER_UNIFORM_HALF = 64.0;
    public const double OUTER_GROWTH = 1.08;
    /// Fine noise needs supporting geometry: unbounded growth reached 47 m cells,
    /// leaving distant plants several metres above or below the visible hills.
    public const double OUTER_MAX_SPACING = 4.0;
    public const double COLLISION_HALF = 85.0;
    public const double OUTER_COLLISION_HALF = 210.0;
    public const double OUTER_COLLISION_SPACING = 2.0;

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

    public ArrayMesh build_mesh(Material material)
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

        Godot.Collections.Array arrays = new Godot.Collections.Array();
        G.resize(arrays, (int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Variant.From(vertices.ToArray());
        arrays[(int)Mesh.ArrayType.Normal] = Variant.From(normals.ToArray());
        arrays[(int)Mesh.ArrayType.TexUV] = Variant.From(uvs.ToArray());
        arrays[(int)Mesh.ArrayType.Color] = Variant.From(colors.ToArray());
        arrays[(int)Mesh.ArrayType.Index] = Variant.From(indices.ToArray());
        ArrayMesh mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null)
        {
            mesh.SurfaceSetMaterial(0, material);
        }
        return mesh;
    }

    public CollisionShape3D build_collision()
    {
        /// Fine collision straight from the baked height grid.
        G.assert(field.height_grid != null, "bake_height_grid() must run before build_collision()");
        ScalarField grid = field.height_grid;
        HeightMapShape3D shape = new HeightMapShape3D();
        shape.MapWidth = (int)grid.resolution;
        shape.MapDepth = (int)grid.resolution;
        shape.MapData = grid.data.ToArray();
        CollisionShape3D collider = new CollisionShape3D();
        collider.Name = "TerrainCollision";
        collider.Shape = shape;
        double cell = grid.cell_size();
        collider.Scale = new Vector3((float)cell, 1.0f, (float)cell);
        return collider;
    }

    public CollisionShape3D build_outer_collision()
    {
        /// Coarse collision for the forest beyond the fine grid. Inside the fine
        /// grid it is sunk so it can never poke through the precise surface.
        long cells = (long)(OUTER_COLLISION_HALF * 2.0 / OUTER_COLLISION_SPACING) + 1;
        List<float> data = new List<float>();
        G.resize(data, (int)(cells * cells));
        double half = (double)(cells - 1) * OUTER_COLLISION_SPACING * 0.5;
        double sink_inside = COLLISION_HALF - 6.0;
        for (long iz = 0; iz < cells; iz++)
        {
            double z = -half + (double)iz * OUTER_COLLISION_SPACING;
            for (long ix = 0; ix < cells; ix++)
            {
                double x = -half + (double)ix * OUTER_COLLISION_SPACING;
                double h = field.height_fast(x, z);
                if (absf(x) < sink_inside && absf(z) < sink_inside)
                {
                    h -= 1.0;
                }
                data[(int)(iz * cells + ix)] = (float)h;
            }
        }
        HeightMapShape3D shape = new HeightMapShape3D();
        shape.MapWidth = (int)cells;
        shape.MapDepth = (int)cells;
        shape.MapData = data.ToArray();
        CollisionShape3D collider = new CollisionShape3D();
        collider.Name = "OuterTerrainCollision";
        collider.Shape = shape;
        collider.Scale = new Vector3((float)OUTER_COLLISION_SPACING, 1.0f, (float)OUTER_COLLISION_SPACING);
        return collider;
    }
}
