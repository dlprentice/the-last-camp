using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp.Tests;

public partial class TestCanopyBatching : TestCase
{
    public void test_spatial_split_preserves_placement_and_original_tints()
    {
        var forest = new Forest();
        using var builder = new MeshBuilder();
        builder.add_box(Vector3.One);
        using ArrayMesh mesh = builder.commit();
        var result = new TreeGenerator.Result { bark = mesh, height = 10,
            crown_center = new Vector3(0, 8, 0), crown_radius = 3 };
        var transforms = new Godot.Collections.Array<Transform3D>();
        float[] x = { -65, -63, 64, 65, 1, -1 };
        for (int i = 0; i < x.Length; i++)
            transforms.Add(new Transform3D(new Basis(Vector3.Up, i * 0.3f), new Vector3(x[i], i, 8)));
        const string key = "ridge_test";
        var kind = TreeSpecies.Kind.OAK;
        forest.far_bark_materials[TreeSpecies.by_kind(kind).bark_set] = new StandardMaterial3D();
        try
        {
            // The headless dummy renderer does not retain native MultiMesh
            // buffers. The same case also runs in the hardware shader probe.
            bool nativeReadback = DisplayServer.GetName() != "headless";
            forest.add_ridge_group(kind, result, key, transforms);
            assert_eq(forest.ridge_groups.Count, 4, "Signed cell boundaries split the interleaved inputs into four groups");
            var seen = new HashSet<Transform3D>();
            foreach (var group in forest.ridge_groups)
            {
                MultiMesh batch = forest.ridge_multimeshes[group.First].Multimesh;
                assert_eq(batch.InstanceCount, group.Transforms.Count, "The culling group references its own draw buffer");
                for (int i = 0; i < group.Transforms.Count; i++)
                {
                    Transform3D placement = group.Transforms[i];
                    int sourceIndex = transforms.IndexOf(placement);
                    assert_true(sourceIndex >= 0 && seen.Add(placement), "Every source tree appears exactly once");
                    if (nativeReadback)
                    {
                        assert_eq(batch.GetInstanceTransform(i), placement, "Native placement preserves rotation, height and position");
                        float tint = (float)Forest.hash_unit(sourceIndex * 7919L + (long)key.Hash() % 1000 + (long)kind * 17);
                        assert_true(Math.Abs(batch.GetInstanceCustomData(i).A - tint) < 0.00001f,
                            "Splitting a group retains the tint from its original index");
                    }
                }
            }
            assert_eq(seen.Count, transforms.Count, "No tree is lost at a cell boundary");
            GD.Print($"CANOPY_PARTITION native_buffer_readback={nativeReadback}");
        }
        finally { forest.Free(); }
    }
}
