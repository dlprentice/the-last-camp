using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace LastCamp.Tests;

/// Smoke test for MeshCommit, not a regression test for godotengine/godot#125198. It rebuilds
/// the game's main kinds of mesh under forced GC pressure and checks every surface arrives
/// whole, with MeshCommit in strict mode. It does NOT reproduce the bug: with the keep-alives
/// and empties reverted it still passes, because the engine loses a surface only a few times
/// in 200,000 calls (and those runs usually end in a crash), far beyond a suite-sized test.
public partial class TestMeshCommit : TestCase
{
    private const int Rounds = 40;

    private sealed record Shape(int Surfaces, int[] Vertices, int[] Indices);

    private static Shape Measure(ArrayMesh mesh)
    {
        int count = mesh.GetSurfaceCount();
        var vertices = new int[count];
        var indices = new int[count];
        for (int s = 0; s < count; s++)
        {
            using Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            if (arrays.Count != (int)Mesh.ArrayType.Max)
            {
                vertices[s] = -1;
                continue;
            }
            vertices[s] = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            Variant index = arrays[(int)Mesh.ArrayType.Index];
            indices[s] = index.VariantType == Variant.Type.Nil ? 0 : index.AsInt32Array().Length;
        }
        return new Shape(count, vertices, indices);
    }

    private void Expect(string what, Shape reference, ArrayMesh mesh, int round)
    {
        Shape got = Measure(mesh);
        bool same = got.Surfaces == reference.Surfaces
            && got.Vertices.AsSpan().SequenceEqual(reference.Vertices)
            && got.Indices.AsSpan().SequenceEqual(reference.Indices);
        if (!same)
            _fail($"{what} round {round}: {got.Surfaces} surfaces, vertices [{string.Join(",", got.Vertices)}], indices [{string.Join(",", got.Indices)}]; expected {reference.Surfaces} surfaces, vertices [{string.Join(",", reference.Vertices)}], indices [{string.Join(",", reference.Indices)}]");
    }

    private static ArrayMesh TerrainGrid(int n)
    {
        var positions = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var uv = new Vector2[n * n];
        var colors = new Color[n * n];
        var indices = new List<int>();
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                positions[i] = new Vector3(x * 0.5f, MathF.Sin(x * 0.3f + z * 0.2f), z * 0.5f);
                normals[i] = Vector3.Up;
                uv[i] = new Vector2(x, z);
                colors[i] = new Color(x / (float)n, z / (float)n, 0.25f, 0.7f);
                if (x < n - 1 && z < n - 1) indices.AddRange(new[] { i, i + 1, i + n + 1, i, i + n + 1, i + n });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        MeshCommit.AddSurface(mesh, arrays);
        return mesh;
    }

    public void test_smoke_surfaces_survive_gc_pressure()
    {
        bool strict = MeshCommit.Strict;
        MeshCommit.Strict = true;
        int stop = 0;
        long collections = 0;
        // Another managed thread allocates and forces collections, so finalizers run while the
        // main thread is inside native mesh calls.
        var noise = new Thread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long lastMinor = 0, lastFull = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                GC.KeepAlive(new byte[4096]);
                long now = clock.ElapsedMilliseconds;
                // Forced collections every 2 ms and a full one every 50 ms, on top of the
                // collections the allocation itself triggers.
                if (now - lastMinor >= 2) { GC.Collect(0); lastMinor = now; Interlocked.Increment(ref collections); }
                if (now - lastFull >= 50) { GC.Collect(); lastFull = now; }
            }
        }) { IsBackground = true, Name = "gc-pressure" };
        int gen0 = GC.CollectionCount(0);
        noise.Start();
        try
        {
            var builds = new List<(string what, Func<ArrayMesh> build, int rounds)>
            {
                ("terrain chunks", () => { using ArrayMesh grid = TerrainGrid(17); var chunks = TerrainBuilder.split_render_mesh(grid, 4); var merged = new ArrayMesh(); foreach (ArrayMesh c in chunks) { using var a = c.SurfaceGetArrays(0); MeshCommit.AddSurface(merged, a); } return merged; }, Rounds),
                ("grass clump", () => GrassPlanter.clump_mesh(7, 5, 0.018, 77), Rounds),
                ("reed clump", () => GrassPlanter.reed_mesh(91), Rounds),
                ("trailhead post", () => TrailheadPost.build_post_mesh(), Rounds),
                ("fire-ring log", () => OldFireRing.log_mesh(), Rounds),
                ("rounded board", () => FieldKit.rounded_box(new Vector3(1.55f, 0.042f, 0.11f), 0.007), Rounds),
                ("impostor quad", () => TreeImpostors._quad(2.0, 6.0, 0.0), Rounds),
                ("oak bark (ImporterMesh LODs)", () => new TreeGenerator().generate(TreeSpecies.by_kind(TreeSpecies.Kind.OAK), 21037, 0.4).bark, 3),
            };
            foreach (var (what, build, rounds) in builds)
            {
                ulong started = Time.GetTicksMsec();
                ArrayMesh first = build();
                Shape reference = Measure(first);
                assert_gt(reference.Surfaces, 0, $"{what} has surfaces");
                assert_true(Array.TrueForAll(reference.Vertices, v => v > 0), $"{what} surfaces all have vertices");
                for (int round = 0; round < rounds; round++)
                {
                    ArrayMesh mesh = build();
                    Expect(what, reference, mesh, round);
                    mesh.Dispose();
                }
                GD.Print($"MESH_COMMIT_PRESSURE {what}: {rounds + 1} builds in {Time.GetTicksMsec() - started} ms");
            }
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            noise.Join();
            MeshCommit.Strict = strict;
        }
        GD.Print($"MESH_COMMIT_PRESSURE total gen0={GC.CollectionCount(0) - gen0} forced={Interlocked.Read(ref collections)}");
        assert_gt(GC.CollectionCount(0) - gen0, 10, "the pressure thread really collected during the builds");
    }
}
