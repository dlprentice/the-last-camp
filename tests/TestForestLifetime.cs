using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace LastCamp.Tests;

/// Cached generation records must retain both their C# metadata and native
/// mesh resources across collections while later scene stages are constructed.
public partial class TestForestLifetime : TestCase
{
    public void test_lod_mesh_ownership_during_collection()
    {
        using var stop = new CancellationTokenSource();
        // The native importer creates and owns the mesh until GetMesh returns
        // its wrapper. Exercise that handoff while finalizers are active.
        Task collector = Task.Run(() => {
            while (!stop.IsCancellationRequested)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(1);
            }
        });
        try
        {
            for (int pass = 0; pass < 32; pass++)
            {
                using var builder = new MeshBuilder();
                builder.add_box(new Vector3(1 + pass * 0.01f, 2, 3));
                using ArrayMesh mesh = builder.commit(generate_lods: true);
                assert_true(GodotObject.IsInstanceValid(mesh), "LOD result owns a live mesh");
                assert_true(mesh.GetSurfaceCount() == 1, "LOD result retains its surface");
                assert_true(mesh.SurfaceGetArrayIndexLen(0) == 36, "Full-detail box indices survive the handoff");
            }
        }
        finally
        {
            stop.Cancel();
            collector.GetAwaiter().GetResult();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TreeGenerator.Result> Seed(RidgeForest ridge)
        => new(ridge._variant(0, TreeSpecies.Kind.OAK, 0));

    public void test_cached_tree_survives_managed_collections()
    {
        var ridge = new RidgeForest { _detail_scale = 0.4 };
        try
        {
            var weak = Seed(ridge);
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            assert_true(weak.TryGetTarget(out var retained), "Cached tree metadata remains strongly reachable");
            var result = ridge._variant(0, TreeSpecies.Kind.OAK, 0);
            assert_true(ReferenceEquals(retained, result), "Cache returns the original generated record");
            assert_true(GodotObject.IsInstanceValid(result.bark), "Cached bark remains a valid native mesh");
            assert_true(GodotObject.IsInstanceValid(result.leaves), "Cached leaves remain a valid native mesh");
            assert_true(result.bark.GetSurfaceCount() > 0 && result.leaf_card_count > 0 && result.height > 0,
                "Collection does not lose generated surfaces or metadata");
        }
        finally { ridge.Free(); }
    }
}
