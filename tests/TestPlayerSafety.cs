using System;
using Godot;

namespace LastCamp.Tests;

public partial class TestPlayerSafety : TestCase
{
    public void test_world_edge_preserves_return_and_sideways_movement_at_different_tick_rates()
    {
        foreach (double delta in new[] { 1.0 / 240, 1.0 / 60, 1.0 / 15, 0.25 })
            for (int i = 0; i < 8; i++)
            {
                Vector2 direction = Vector2.FromAngle(i * MathF.PI / 4);
                Vector2 point = Player.PLAY_AREA_CENTRE + direction * (Player.PLAY_AREA_RADIUS - 0.015f);
                var start = new Vector3(point.X, 8, point.Y);
                var normal = new Vector3(direction.X, 0, direction.Y);
                var tangent = new Vector3(-direction.Y, 0, direction.X);
                var outward = normal * 45 + tangent * 2.9f;
                Vector3 limited = Player.LimitWorldTravel(start, outward, delta);
                Vector3 end = start + limited * (float)delta;
                assert_true(Player.InsidePlayArea(end, 0.0001f), "even a long tick cannot leave the camping area");
                assert_true(limited.Dot(tangent) > 2.6f, "sideways movement stays available at the boundary");
                var inward = -normal * 5.6f;
                assert_true(Player.LimitWorldTravel(start, inward, delta) == inward, "returning from the edge keeps full movement speed");
            }
        var ordinary = new Vector3(3, 0, -2);
        assert_true(Player.LimitWorldTravel(new Vector3(2, 0, 30), ordinary, 1.0 / 60) == ordinary, "camp movement is unchanged");
    }
}
