using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp.Tests;

public partial class TestTerrainChunks : TestCase
{
    public void test_chunks_keep_winding_coverage_and_identical_border_attributes()
    {
        const int n = 6;
        var positions = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var uv = new Vector2[n * n];
        var colors = new Color[n * n];
        var indices = new List<int>();
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                positions[i] = new Vector3(x * x * 0.5f, MathF.Sin(x + z), z * z * 0.8f);
                normals[i] = new Vector3(x * 0.03f, 1, z * 0.02f).Normalized();
                uv[i] = new Vector2(x, z);
                colors[i] = new Color(x / 6f, z / 6f, 0.25f, 0.7f);
                if (x < n - 1 && z < n - 1) indices.AddRange(new[] { i, i + 1, i + n + 1, i, i + n + 1, i + n });
            }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        using var source = new ArrayMesh();
        source.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var chunks = TerrainBuilder.split_render_mesh(source, 2);
        using var canonical = source.SurfaceGetArrays(0);
        positions = canonical[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        normals = canonical[(int)Mesh.ArrayType.Normal].AsVector3Array();
        colors = canonical[(int)Mesh.ArrayType.Color].AsColorArray();
        var expected = new HashSet<string>();
        for (int i = 0; i < indices.Count; i += 3) expected.Add($"{indices[i]},{indices[i + 1]},{indices[i + 2]}");
        int triangleCount = 0;
        var borderNormals = new Dictionary<int, Vector3>();
        foreach (var chunk in chunks)
        {
            using var a = chunk.SurfaceGetArrays(0);
            var p = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normal = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var tex = a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var color = a[(int)Mesh.ArrayType.Color].AsColorArray();
            var faces = a[(int)Mesh.ArrayType.Index].AsInt32Array();
            var ids = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                int id = (int)tex[i].Y * n + (int)tex[i].X;
                ids[i] = id;
                // ArrayMesh packs normals into 16-bit octahedral coordinates.
                // A second encode may shift one quantization step; adjoining
                // chunks must nevertheless have bit-identical decoded normals.
                assert_true(p[i] == positions[id] && normal[i].DistanceTo(normals[id]) < 0.00008f && color[i] == colors[id],
                    "positions and material weights stay exact; normal error stays within packing precision");
                if (borderNormals.TryGetValue(id, out Vector3 other))
                    assert_true(normal[i] == other, "adjoining chunks have identical border normals");
                else borderNormals[id] = normal[i];
            }
            for (int i = 0; i < faces.Length; i += 3)
            {
                assert_true(expected.Remove($"{ids[faces[i]]},{ids[faces[i + 1]]},{ids[faces[i + 2]]}"),
                    "each original triangle appears once with its original winding");
                triangleCount++;
            }
        }
        assert_eq(expected.Count, 0, "no triangles lost at chunk boundaries");
        assert_eq(triangleCount, indices.Count / 3, "no new overlapping triangles");
        assert_eq(chunks.Count, 9, "partial edge chunks are included");
    }
}
