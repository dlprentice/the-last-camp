using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp.Tests;

public partial class TestGrassBatching : TestCase
{
    public void test_upload_batches_preserve_every_plant_and_its_bounds()
    {
        var field = new TerrainField();
        var planter = new GrassPlanter(field, 24, 0.12);
        planter.plan();
        var merged = GrassPlanter.merge_chunks(planter.chunks, 16);
        var before = Records(planter.chunks);
        var after = Records(merged);
        before.Sort(StringComparer.Ordinal);
        after.Sort(StringComparer.Ordinal);
        assert_eq(string.Join("\n", before), string.Join("\n", after), "upload grouping preserves the complete transform, tint and wind data");
        assert_lt(merged.Count, planter.chunks.Count, "neighbouring draws combine");
        foreach (var chunk in merged)
            for (int i = 0; i < chunk.count; i++)
                assert_true(chunk.aabb.HasPoint(GrassPlanter.read_transform(chunk.buffer, i).Origin), "merged cull bounds contain every root");
    }

    private static List<string> Records(Godot.Collections.Array<GrassPlanter.Chunk> chunks)
    {
        var result = new List<string>();
        foreach (var chunk in chunks)
            for (int i = 0; i < chunk.count; i++)
            {
                var fields = new string[GrassPlanter.FLOATS_PER_INSTANCE];
                for (int j = 0; j < fields.Length; j++) fields[j] = BitConverter.SingleToInt32Bits(chunk.buffer[i * fields.Length + j]).ToString("X8");
                result.Add(string.Join("", fields));
            }
        return result;
    }
}
