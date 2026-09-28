using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp.Tests;

public partial class TestGrassBatching : TestCase
{
    public void test_quality_round_trip_retains_uploaded_instances()
    {
        var field = new TerrainField();
        var planter = new GrassPlanter(field, 32, 0.7);
        planter.plan();
        var plants = new Understory();
        try
        {
            plants._grass_density = 0.7;
            plants._grass_distance = 24;
            plants.grass_material = new ShaderMaterial();
            plants.add_grass(planter);
            var identities = new List<ulong>();
            long full = plants.blade_count;
            foreach (var cell in plants.grass_chunks) identities.Add(cell.Multimesh.GetInstanceId());
            var lower = QualityPreset.high();
            lower.grass_density = 0.3;
            lower.grass_distance = 20;
            plants.apply_quality(lower);
            assert_true(plants._replant_thread == null, "lowering quality does not launch a rebuild");
            assert_lt(plants.blade_count, full * 0.5, "lower density reduces submitted instances");
            var original = QualityPreset.high();
            original.grass_density = 0.7;
            original.grass_distance = 24;
            plants.apply_quality(original);
            assert_eq(plants.blade_count, full, "returning to the prepared tier restores every plant");
            assert_true(plants._replant_thread == null, "restoring quality reuses the allocation");
            for (int i = 0; i < plants.grass_chunks.Count; i++)
                assert_true(plants.grass_chunks[i].Multimesh.GetInstanceId() == identities[i], "native buffers retain their identity");
        }
        finally { plants.Free(); }
    }

    public void test_density_prefixes_cover_every_merged_cell()
    {
        var chunk = new GrassPlanter.Chunk { origin = new Vector2(-16, 32), count = 400 };
        for (int i = 0; i < chunk.count; i++)
            for (int field = 0; field < GrassPlanter.FLOATS_PER_INSTANCE; field++)
                chunk.buffer.Add(i * 100 + field);
        float[] upload = GrassPlanter.upload_buffer(chunk);
        float[] repeat = GrassPlanter.upload_buffer(chunk);
        var ids = new HashSet<int>();
        int[] quadrants = new int[4];
        for (int i = 0; i < chunk.count; i++)
        {
            int offset = i * (int)GrassPlanter.FLOATS_PER_INSTANCE;
            int id = (int)upload[offset] / 100;
            ids.Add(id);
            if (i < 100) quadrants[id / 100]++;
            for (int field = 0; field < GrassPlanter.FLOATS_PER_INSTANCE; field++)
            {
                assert_true(upload[offset + field] == id * 100 + field, "transform, tint and wind stay together");
                assert_true(upload[offset + field] == repeat[offset + field], "upload order is deterministic");
            }
        }
        assert_eq(ids.Count, 400, "no instances lost or duplicated");
        foreach (int count in quadrants) assert_true(count > 10 && count < 40, "a quarter-density prefix spans every source cell");
    }

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
