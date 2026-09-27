using Godot;

namespace LastCamp.Tests;

public partial class TestStoneFlight : TestCase
{
    private static StoneFlight Fly(Vector3 direction, float strength)
    {
        var stone = new StoneFlight(new Vector3(0, 1, 0), direction, strength);
        for (int i = 0; i < 1200 && stone.Active; i++) stone.Step(1f / 120, 0, -4);
        return stone;
    }

    public void test_release_angle_and_strength_control_skips()
    {
        StoneFlight flat = Fly(new Vector3(1, -0.02f, 0), 1);
        StoneFlight steep = Fly(new Vector3(1, -1, 0), 1);
        StoneFlight weak = Fly(new Vector3(1, -0.02f, 0), 0);
        assert_gt(flat.Skips, 2, "a strong flat throw skips repeatedly");
        assert_eq(steep.Skips, 0, "a steep throw sinks on contact");
        assert_gt(flat.Skips, weak.Skips, "release strength changes the achievable score");
        assert_false(flat.Active || steep.Active || weak.Active, "all throws finish without leaving an input lock");
    }

    public void test_ballistic_contact_loses_energy_and_land_has_no_skips()
    {
        var stone = new StoneFlight(new Vector3(0, 0.03f, 0), new Vector3(1, -0.1f, 0), 1);
        float initial = stone.Velocity.LengthSquared();
        for (int i = 0; i < 40 && stone.Skips == 0; i++) stone.Step(1f / 120, 0, -4);
        assert_eq(stone.Skips, 1, "first water contact registers once");
        assert_lt(stone.Velocity.LengthSquared(), initial, "a rebound cannot add energy");
        var land = new StoneFlight(new Vector3(0, 0.01f, 0), Vector3.Down, 1);
        assert_eq((int)land.Step(1f / 120, -4, 0), (int)StoneFlight.Contact.Ground, "dry ground stops a throw");
        assert_eq(land.Skips, 0, "land cannot award a water skip");
    }
}
