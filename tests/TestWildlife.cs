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
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

public partial class TestWildlife : TestCase
{
    public void test_bird_perch_uses_the_real_post_cap()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Node3D holder = new Node3D();
        tree.Root.AddChild(holder);
        TerrainField field = new TerrainField();
        Dock dock = new Dock(field);
        holder.AddChild(dock);
        dock.build();
        Wildlife wildlife = new Wildlife(field);
        holder.AddChild(wildlife);
        wildlife._prepare_bird_perch(dock);
        assert_true(wildlife._perch_ready, "exposed post provides a usable real perch");
        Vector3 foot = dock.ToLocal(wildlife.bird_perch_position());
        TriangleMesh mesh = ((MeshInstance3D)dock.GetNode("Piles")).Mesh.GenerateTriangleMesh();
        Godot.Collections.Dictionary hit = mesh.IntersectRay(foot + Vector3.Up * 0.1f, Vector3.Down);
        assert_false((hit.Count == 0), "bird's feet have an actual timber face below");
        if (!(hit.Count == 0))
        {
            assert_near(foot.Y, G.Index(hit["position"], "y").AsDouble(), 0.00005, "foot anchor touches rendered cap");
        }
        assert_lt(new Vector2((float)((double)foot.X - dock._piles[4].X), (float)((double)foot.Z - dock._piles[4].Z)).Length(), 0.002, "perch stays on the cap centre");
        holder.Free();
    }

    public void test_bird_cue_is_shot_timed_quiet_elsewhere_and_rain_gated()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Wildlife wildlife = new Wildlife(new TerrainField());
        tree.Root.AddChild(wildlife);
        wildlife.SetProcess(false);
        wildlife._build_small_life();
        wildlife._perch_ready = true;
        wildlife._bird_perch = new Vector3(-19.7f, 0.2f, 6.6f);
        wildlife.set_film_cue(Wildlife.BIRD_CUE, 2.0);
        wildlife._update_small_life(1.0, 1.0, 0.4);
        assert_true(wildlife._birds[0].Visible, "first bird can be observed on the post");
        assert_true(wildlife._birds[0].GetNode("Perched").Get("visible").AsBool(), "resting bird uses folded wings and feet");
        assert_false(wildlife._birds[1].Visible, "companion does not start early");
        wildlife.set_film_cue(Wildlife.BIRD_CUE, 9.0);
        wildlife._update_small_life(1.0, 1.0, 0.4);
        Transform3D before = wildlife._birds[0].Transform;
        wildlife._life_clock = 731.0;
        wildlife._bird_wind_phase = 99.0;
        wildlife._update_small_life(1.0, 1.0, 0.4);
        assert_eq(wildlife._birds[0].Transform, before, "loading/autonomous clocks do not move the film bird");
        assert_true(wildlife._birds[1].Visible, "companion appears during its authored pass");
        assert_lt(wildlife._birds[0].Scale.DistanceTo(Vector3.One), 0.00001, "film does not enlarge the bird");
        wildlife.set_film_cue("", 9.0);
        wildlife._update_small_life(1.0, 1.0, 0.4);
        foreach (Node3D bird in wildlife._birds)
        {
            assert_false(bird.Visible, "quiet shots do not receive incidental flybys");
        }
        wildlife.set_film_cue(Wildlife.BIRD_CUE, 9.0);
        wildlife._update_small_life(1.0, 0.0, 0.4);
        foreach (Node3D bird2 in wildlife._birds)
        {
            assert_false(bird2.Visible, "rain suppresses the daytime birds");
        }
        wildlife.Free();
    }

    public void test_cued_insects_stay_small_local_and_out_of_quiet_shots()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Wildlife wildlife = new Wildlife(new TerrainField());
        tree.Root.AddChild(wildlife);
        wildlife.SetProcess(false);
        wildlife._build_small_life();
        assert_eq(wildlife._gnats.Multimesh.InstanceCount, 64, "eight habitat-local gnat clusters");
        assert_lt(Wildlife._butterfly_wing(1.0, 0).GetAabb().Size.X * 2.0, 0.075, "butterfly wingspan stays below 75 mm");
        assert_lt(Wildlife._dragonfly_wings(1.0).GetAabb().Size.X * 2.0, 0.09, "dragonfly wingspan stays below 90 mm");
        for (long seconds = 0; seconds < 13; seconds++)
        {
            wildlife.set_film_cue(Wildlife.MEADOW_CUE, (double)seconds);
            wildlife._update_small_life(1.0, 1.0, 0.4);
            foreach (Node3D butterfly in G.slice(wildlife._butterflies, 0, 2))
            {
                assert_true(butterfly.Visible, "sunlit meadow cue contains butterflies");
                assert_lt(butterfly.Position.DistanceTo(wildlife._butterfly_anchor), 1.0, "butterflies remain inside the authored flower patch");
            }
            foreach (Node3D butterfly2 in G.slice(wildlife._butterflies, 2))
            {
                assert_false(butterfly2.Visible, "distributed habitat actors stay out of the close film cue");
            }
            foreach (Node3D dragonfly in wildlife._dragonflies)
            {
                assert_false(dragonfly.Visible, "pond insects do not accompany the meadow cue");
            }
        }
        wildlife.set_film_cue("", 3.0);
        wildlife._update_small_life(1.0, 1.0, 0.4);
        foreach (Node3D butterfly3 in wildlife._butterflies)
        {
            assert_false(butterfly3.Visible, "quiet film view has no added butterfly action");
        }
        wildlife.set_film_cue(Wildlife.POND_CUE, 3.0);
        wildlife._update_small_life(1.0, 1.0, 0.4);
        assert_true(wildlife._dragonflies[0].Visible, "pond view carries one close dragonfly");
        assert_false(wildlife._dragonflies[1].Visible, "pond view stays free of a second competing insect");
        assert_gt(wildlife._dragonflies[0].Position.Y, TerrainField.WATER_LEVEL + 0.5, "dragonfly flight stays above water and lilies");
        wildlife._update_small_life(0.0, 1.0, 0.4);
        foreach (Node3D dragonfly2 in wildlife._dragonflies)
        {
            assert_false(dragonfly2.Visible, "day insects settle at night");
        }
        wildlife.Free();
    }
}
