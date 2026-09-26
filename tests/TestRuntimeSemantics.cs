using System;
using System.Collections.Generic;
using Godot;
using GdRuntime;

namespace LastCamp.Tests;

/// <summary>Use the engine's Expression API as the oracle, not another C# formula.</summary>
public partial class TestRuntimeSemantics : TestCase
{
    private Variant EngineValue(string source, params Variant[] values)
    {
        using var expression = new Expression();
        if (expression.Parse(source, new[] { "a", "b", "c" }) != Error.Ok)
            throw new InvalidOperationException(expression.GetErrorText());
        var result = expression.Execute(new Godot.Collections.Array(values), null, false, false);
        if (expression.HasExecuteFailed()) throw new InvalidOperationException(expression.GetErrorText());
        return result;
    }

    public void test_hash_preserves_unsigned_engine_bits()
    {
        int highBit = 0;
        for (int i = 0; i < 128; i++)
        {
            Variant value = new StringName($"Lantern_{i}");
            long expected = EngineValue("hash(a)", value).AsInt64();
            if (expected > int.MaxValue) highBit++;
            assert_eq(G.hash(value), expected, "lantern seed hash uses all unsigned bits");
            string key = $"ridge_{i}";
            assert_eq((long)key.Hash(), EngineValue("a.hash()", key), "ridge material string hash");
        }
        assert_gt(highBit, 0, "hash cases exercise the sign bit");
    }

    public void test_scalars_and_negative_indices_match_engine()
    {
        for (int i = -51; i <= 51; i++)
        {
            double value = i * 0.125;
            assert_eq(G.snapped(value, 0.3), EngineValue("snappedf(a, b)", value, 0.3), "double snapping");
            assert_eq(G.wrapf(value, -1.5, 2.5), EngineValue("wrapf(a, b, c)", value, -1.5, 2.5), "wrapped scalar");
            assert_eq(G.round(value), EngineValue("roundf(a)", value), "rounding at midpoints");
        }
        var array = new Godot.Collections.Array { 3, 5, 11 };
        assert_eq(G.Index(array, -1), EngineValue("a[b]", array, -1), "negative indexing");
        assert_eq(G.Index(array, -3), EngineValue("a[b]", array, -3), "negative first index");
    }

    public void test_detached_scene_is_complete_and_world_is_not_started()
    {
        var main = new Main();
        assert_eq(main.GetChildCount(), 9, "all original root children come from the builder");
        assert_true(main.GetNode<Camp>("Camp").terrain == null, "construction does not build the world");
        assert_false(main.IsInsideTree(), "no gameplay tree entered");
        main.Free();
    }

    public void test_cinematic_overlay_builds_without_entering_scene()
    {
        var cinematic = new Cinematic();
        cinematic._build_overlay();
        foreach (Label label in new[] { cinematic._title, cinematic._subtitle, cinematic._end_title, cinematic._end_note })
            assert_eq(label.Modulate.A, 0.0, "title cards start transparent");
        assert_false(cinematic._clock.Visible, "clock starts hidden");
        assert_false(cinematic.IsInsideTree(), "overlay check does not run a cinematic");
        cinematic.Free();
    }
}
