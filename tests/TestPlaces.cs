#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using GdRuntime;
using LastCamp;

namespace LastCamp.Tests;

/// The micro cut's places and paths: where the probe puts Tom's Rock and what it
/// sees, the twenty steps and the loose one, grounding of everything set on the
/// land, and the paths' lengths and walkability. CPU only; the walk itself is
/// TraversalCheck --traverse-places.
public partial class TestPlaces : TestCase
{
    static TerrainFieldEnhanced _field;
    static ScenePlan _plan;

    static TerrainFieldEnhanced land => _field ??= new TerrainFieldEnhanced();

    static ScenePlan scene
    {
        get
        {
            if (_plan == null)
            {
                _plan = new ScenePlan(land);
                _plan.build();
            }
            return _plan;
        }
    }

    public void test_probe_places_toms_rock()
    {
        var natural = new TerrainFieldEnhanced { places_enabled = false };
        Vector2 found = PlacesProbe.find_toms_rock(natural);
        assert_lt(found.DistanceTo(PlacesLayout.TOMS_ROCK), 0.01, $"the probe finds Tom's Rock at {PlacesLayout.TOMS_ROCK} (got {found})");
        double from_camp = PlacesLayout.TOMS_ROCK.Length();
        assert_true(from_camp >= 60.0 && from_camp <= 110.0, $"Tom's Rock is 60 to 110 m from camp ({from_camp:F1})");
        assert_true(PlacesLayout.TOMS_ROCK.X > 0 && PlacesLayout.TOMS_ROCK.Y > 0, "Tom's Rock is south-east of camp");
        double summit = land.height(PlacesLayout.TOMS_ROCK.X, PlacesLayout.TOMS_ROCK.Y);
        for (int i = 0; i < 36; i++)
        {
            Vector2 q = PlacesLayout.TOMS_ROCK + PlacesLayout.bearing(i * 10.0) * 16.0f;
            assert_gt(summit, land.height(q.X, q.Y) + 2.0, $"the summit stands above the ground 16 m out on bearing {i * 10}");
        }
    }

    public void test_toms_rock_sees_the_pond()
    {
        Vector3 eye = Places.BenchSeat(land) + Vector3.Up * (float)(PlacesLayout.SEATED_EYE - PlacesLayout.BENCH_SEAT_HEIGHT);
        Vector2 from = new(eye.X, eye.Z);
        double terrain = PlacesProbe.pond_view(land, from, eye.Y);
        assert_gt(terrain, 0.7, $"from the bench the land hides little of the pond ({terrain:F2} seen)");
        // A fan of sight lines from the seated eye to the pond's water, past the land
        // and every planned tree (trunks, and crowns from their lowest limbs up).
        double view = PlacesProbe.lookout_view(land, scene.trees, eye);
        assert_gt(view, 0.6, $"from the bench the sight lines reach at least 60% of the pond past land and trees ({view:F2})");
        // The opening has an edge, not a wall: trees thin through it.
        int edge_trees = 0;
        foreach (ScenePlan.TreeEntry t in scene.trees)
        {
            double open = PlacesLayout.lookout_openness(t.position);
            if (open > 0.05 && open < 0.95) edge_trees++;
        }
        assert_gt(edge_trees, 5, $"trees stand thinned through the view's edge ({edge_trees})");
        double sunset = Math.Abs(PlacesLayout.LOOKOUT_BEARING_DEG - 289.0);
        assert_lt(sunset, PlacesLayout.LOOKOUT_CORE_DEG, "the sunset lies inside the open view");
        Vector2 to_pond = TerrainField.POND_CENTRE - PlacesLayout.BENCH;
        double off = Mathf.RadToDeg(Math.Abs(PlacesLayout.FACE.AngleTo(to_pond)));
        assert_lt(off, 25.0, $"the bench faces the pond ({off:F0} degrees off)");
    }

    public void test_twenty_steps_climb_to_the_bench()
    {
        List<TomsRock.Slab> steps = TomsRock.set_steps(land);
        assert_eq(steps.Count + 1, PlacesLayout.STEP_COUNT, "nineteen set steps and the loose twentieth make twenty");
        double previous_top = double.NegativeInfinity;
        double previous_along = -1.0;
        double max_riser = 0.0;
        foreach (TomsRock.Slab s in steps)
        {
            double top = s.xform.Origin.Y + s.size.Y * 0.5;
            assert_gt(top, previous_top - 0.005, $"step at {s.along:F1} m is no lower than the one before");
            if (previous_along >= 0.0)
            {
                double gap = s.along - previous_along;
                assert_true(gap > TomsRock.STEP_MIN_GAP - 0.01 && gap < 4.5, $"steps at {previous_along:F1} and {s.along:F1} m are a tread to a landing apart");
            }
            max_riser = Math.Max(max_riser, TomsRock.riser(land, s));
            previous_top = top;
            previous_along = s.along;
        }
        assert_lt(max_riser, 0.26, $"no step stands more than 26 cm above the ground before it ({max_riser:F2})");
        Transform3D loose = TomsRock.loose_pose(land);
        assert_gt(loose.Origin.Y + 0.05, previous_top - 0.3, "the twentieth step sits at the top of the flight");
        double total_rise = previous_top - (steps[0].xform.Origin.Y + steps[0].size.Y * 0.5);
        assert_gt(total_rise, 2.5, $"the steps climb the outcrop ({total_rise:F2} m)");
    }

    public void test_steps_are_grounded_level_stones()
    {
        foreach (TomsRock.Slab s in TomsRock.set_steps(land))
        {
            Vector3 h = s.size * 0.5f;
            for (int i = -2; i <= 2; i++)
            {
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 top = s.xform * new Vector3(h.X * i / 2.0f, h.Y, h.Z * j / 2.0f);
                    Vector3 bottom = s.xform * new Vector3(h.X * i / 2.0f, -h.Y, h.Z * j / 2.0f);
                    double g = land.height(top.X, top.Z);
                    // The tread and front stand proud; the back edge may sit into the bank.
                    if (j >= 0) assert_gt(top.Y, g + 0.005, $"step at {s.along:F1} m: its tread stands above the ground");
                    else assert_gt(top.Y, g - 0.15, $"step at {s.along:F1} m: its back is set only a little into the bank");
                    assert_lt(bottom.Y, g - 0.01, $"step at {s.along:F1} m: its underside is buried (no floating)");
                }
            }
            assert_gt(s.xform.Basis.Y.Normalized().Dot(Vector3.Up), 0.9999, $"step at {s.along:F1} m is level");
            Vector3[] hull = TomsRock.step_collision(land, s);
            assert_eq(hull.Length, 10, "each step collides as its stone plus a riser ramp");
        }
    }

    public void test_loose_step_sits_a_little_wrong()
    {
        Transform3D neutral = TomsRock.loose_neutral(land);
        Transform3D pose = TomsRock.loose_pose(land);
        double tilt = Mathf.RadToDeg(pose.Basis.Y.Normalized().AngleTo(Vector3.Up));
        assert_true(tilt > 1.5 && tilt < 5.0, $"the loose step tips a few degrees ({tilt:F2})");
        Vector3 nx = neutral.Basis.X.Normalized(), px = pose.Basis.X.Normalized();
        double yaw = Mathf.RadToDeg(new Vector2(nx.X, nx.Z).AngleTo(new Vector2(px.X, px.Z)));
        assert_true(Math.Abs(yaw) > 1.0 && Math.Abs(yaw) < 4.0, $"and sits a little askew ({yaw:F2} degrees)");
        assert_lt(pose.Origin.DistanceTo(neutral.Origin), 0.03, "but it stays in its place");
        Vector3 h = TomsRock.LOOSE_SIZE * 0.5f;
        // The back edge is settled in the soil; the front lifts to a hairline gap.
        foreach (float x in new[] { -h.X, h.X })
        {
            Vector3 back = pose * new Vector3(x, -h.Y, h.Z);
            Vector3 front = pose * new Vector3(x, -h.Y, -h.Z);
            assert_lt(back.Y, land.height(back.X, back.Z) - 0.005, "its back edge rests in the ground");
            double gap = front.Y - land.height(front.X, front.Z);
            assert_true(gap > 0.002 && gap < 0.012, $"its front edge shows a hairline gap ({gap * 1000:F1} mm)");
        }
        // The tin is under it, closed, bedded in the soil and clear of the stone.
        Transform3D tin = TomsRock.tin_pose(land);
        Vector3 t = TomsRock.TIN_SIZE * 0.5f;
        assert_lt(tin.Origin.Y - t.Y, land.height(tin.Origin.X, tin.Origin.Z), "the tin is bedded in the ground");
        foreach (float x in new[] { -t.X, t.X })
        {
            foreach (float z in new[] { -t.Z, t.Z })
            {
                Vector3 corner = pose.AffineInverse() * (tin * new Vector3(x, t.Y, z));
                double lift = GraniteSlab.hollow_lift(TomsRock.loose_shape(), corner.X, corner.Z);
                assert_gt(lift, TomsRock.LOOSE_HOLLOW * 0.95, "the tin lies inside the stone's hollow");
                assert_lt(corner.Y, -h.Y + lift - 0.003, "the hollow clears the tin");
            }
        }
    }

    public void test_bench_faces_the_view_on_level_ground()
    {
        Vector3 seat = Places.BenchSeat(land);
        double ground = land.height(PlacesLayout.BENCH.X, PlacesLayout.BENCH.Y);
        assert_near(seat.Y - ground, PlacesLayout.BENCH_SEAT_HEIGHT, 0.001, "the seat is at Tom's height for June");
        Basis b = TomsRock.summit_basis();
        double spread = 0.0;
        foreach (float x in new[] { -0.62f, 0.62f })
        {
            foreach (float z in new[] { -0.2f, 0.2f })
            {
                Vector3 c = b * new Vector3(x, 0, z);
                spread = Math.Max(spread, Math.Abs(land.height(PlacesLayout.BENCH.X + c.X, PlacesLayout.BENCH.Y + c.Z) - ground));
            }
        }
        assert_lt(spread, TomsRock.BENCH_SUPPORT_SINK - 0.02, $"the summit pad is level under the bench ({spread * 1000:F0} mm)");
        Vector3 view = -b.Z;
        assert_near(new Vector2(view.X, view.Z).AngleTo(PlacesLayout.FACE), 0.0, 0.001, "the bench looks out along FACE");
        double sunset = PlacesLayout.BENCH_BEARING_DEG - 289.0;
        assert_true(Math.Abs(sunset) < 25.0, "the sunset is within the bench's view");
    }

    public void test_outcrop_boulders_and_old_ring_are_grounded()
    {
        List<TomsRock.Ledge> ledges = TomsRock.ledges(land);
        assert_gt(ledges.Count, 14, $"the outcrop shows its granite bedrock ({ledges.Count} ledges)");
        foreach (TomsRock.Ledge l in ledges)
        {
            // The lower part of every ledge is in the ground all round (no floating).
            for (int k = 0; k < 16; k++)
            {
                double a = k / 16.0 * Math.Tau;
                Vector3 w = l.pose * new Vector3((float)(Math.Cos(a) * l.shape.size.X * 0.45), -l.shape.size.Y * 0.2f, (float)(Math.Sin(a) * l.shape.size.Z * 0.45));
                assert_lt(w.Y, land.height(w.X, w.Z) + 0.001, "a ledge's lower part is buried");
            }
            Vector2 at = new(l.pose.Origin.X, l.pose.Origin.Z);
            double reach = Math.Max(l.shape.size.X, l.shape.size.Z) * 0.5;
            assert_gt(PlacesLayout.distance_to(at, PlacesLayout.STEP_LINE, out _), reach + 0.6, "ledges keep off the steps");
        }
        assert_lt(land.grass_suitability(PlacesLayout.TOMS_ROCK.X + 4.0, PlacesLayout.TOMS_ROCK.Y + 3.0), 0.2, "little grass grows on the outcrop");
        foreach (OldFireRing.Stone s in OldFireRing.stones(land))
        {
            double g = land.height(s.pose.Origin.X, s.pose.Origin.Z);
            assert_true(s.size >= 0.17 && s.size <= 0.33, $"a ring stone is two fists to a loaf across ({s.size:F2} m)");
            assert_true(s.pose.Origin.Y < g && s.pose.Origin.Y + s.shape.size.Y * 0.5 > g + s.shape.size.Y * 0.3, "ring stones are half sunk, and show");
        }
        assert_eq(OldFireRing.stones(land).Count, OldFireRing.STONES - 1, "one ring stone is long gone");
        foreach (Transform3D c in OldFireRing.charcoal(land))
            assert_lt(new Vector2(c.Origin.X, c.Origin.Z).DistanceTo(PlacesLayout.OLD_RING), PlacesLayout.OLD_RING_RADIUS, "old charcoal lies inside the ring");
        double grass = land.grass_suitability(PlacesLayout.OLD_RING.X, PlacesLayout.OLD_RING.Y);
        assert_gt(grass, 0.3, $"grass grows through the old ring ({grass:F2})");

        ArrayMesh log = OldFireRing.log_mesh();
        Transform3D lp = OldFireRing.log_pose(land, log);
        Vector3[] verts = log.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        double highest_gap = double.NegativeInfinity, lowest_top = double.PositiveInfinity;
        foreach (Vector3 v in verts)
        {
            Vector3 w = lp * v;
            double g = land.height(w.X, w.Z);
            if (v.Y < -PlacesLayout.OLD_LOG_RADIUS * 0.5) highest_gap = Math.Max(highest_gap, w.Y - g);
            if (v.Y > PlacesLayout.OLD_LOG_RADIUS * 0.8) lowest_top = Math.Min(lowest_top, w.Y - g);
        }
        assert_lt(highest_gap, -0.01, $"the fallen log's underside is everywhere in the ground ({highest_gap:F3})");
        assert_gt(lowest_top, 0.18, $"and the log still lies on top of it ({lowest_top:F2})");

        double post_ground = land.height(PlacesLayout.TRAILHEAD_POST.X, PlacesLayout.TRAILHEAD_POST.Y);
        foreach (Vector2 c in TrailheadPost.footprint(PlacesLayout.TRAILHEAD_POST, (float)TrailheadPost.WIDTH * 0.5f))
        {
            assert_lt(post_ground - TrailheadPost.BURIED, land.height(c.X, c.Y), "the post is set in the ground");
        }
        assert_gt(land.trail_distance(PlacesLayout.TRAILHEAD_POST.X, PlacesLayout.TRAILHEAD_POST.Y), 1.2, "the post stands off the trail");
    }

    public void test_post_reads_halloway_1974_with_a_backwards_seven()
    {
        var strokes = TrailheadPost.strokes();
        assert_gt(strokes.Count, 25, "the inscription has every letter's strokes");
        Vector2[][] seven = TrailheadPost.glyph('7');
        assert_true(seven[0][0].X > seven[0][1].X, "the seven's bar runs right to left");
        assert_true(seven[0][2].X > 0.5f, "and its stem falls to the right: it is backwards");
        // Grooves are cut into the face, not painted on.
        var (a, b) = strokes[0];
        assert_gt(TrailheadPost.carve_depth((a + b) * 0.5f, strokes), TrailheadPost.GROOVE_DEPTH * 0.9, "a stroke is a groove");
        assert_near(TrailheadPost.carve_depth(new Vector2(0.075f, 0.1f), strokes), 0.0, 1e-9, "the face is whole between letters");
        // A squared cedar post, about 1.3 m by 15 cm, lettered large enough to read
        // from 3 m: cap height subtends well over a degree there.
        assert_near(TrailheadPost.ABOVE_GROUND, 1.3, 0.05, "the post stands about 1.3 m");
        assert_near(TrailheadPost.WIDTH, 0.15, 0.01, "the post is about 15 cm across");
        assert_gt(Mathf.RadToDeg(Math.Atan2(TrailheadPost.LETTER_HEIGHT, 3.0)), 1.6, "the letters read from 3 m");
        foreach (var (p, q) in strokes)
        {
            assert_lt(Math.Max(Math.Abs(p.X), Math.Abs(q.X)), TrailheadPost.WIDTH * 0.5 - 0.01, "the letters fit the face");
            assert_gt(Math.Min(p.Y, q.Y), 0.25, "the letters stand clear of the grass at the post's foot");
        }
    }

    public void test_paths_lengths_match_the_design()
    {
        double june = PlacesLayout.length(PlacesLayout.JUNE_FOOTPATH);
        assert_within(june, PlacesLayout.JUNE_FOOTPATH_TARGET, "June's footpath, camp to the old ring");
        double tom = PlacesLayout.length(PlacesLayout.TOMS_ROUTE) + PlacesLayout.length(PlacesLayout.STEP_LINE);
        assert_within(tom, PlacesLayout.TOMS_ROUTE_TARGET, "Tom's route, the old ring to the bench");
        // June's painted path joins the trail, which takes it on to the fire.
        Vector2 join = PlacesLayout.PAINTED_PATH[^1];
        Godot.Collections.Array<Vector2> trail = TerrainField.TRAIL;
        double down = PlacesLayout.length(PlacesLayout.PAINTED_PATH) + PlacesLayout.BENCH.DistanceTo(PlacesLayout.PAINTED_PATH[0]);
        bool joined = false;
        for (int i = 0; i + 1 < trail.Count; i++)
        {
            Vector2 c = PlacesLayout.closest_on_segment(join, trail[i], trail[i + 1]);
            if (!joined && c.DistanceTo(join) < 0.5)
            {
                joined = true;
                down += c.DistanceTo(trail[i + 1]);
                for (int k = i + 1; k + 1 < trail.Count && trail[k].DistanceTo(TerrainField.FIRE) > 3.0; k++) down += trail[k].DistanceTo(trail[k + 1]);
                break;
            }
        }
        assert_true(joined, "the painted path ends on the trail");
        assert_within(down, PlacesLayout.PAINTED_PATH_TARGET, "Tom's Rock down to camp by the painted path");
        assert_lt(PlacesLayout.JUNE_FOOTPATH[0].DistanceTo(TerrainField.FIRE), 11.0, "June's footpath leaves from the camp clearing");
        assert_lt(PlacesLayout.JUNE_FOOTPATH[^1].DistanceTo(PlacesLayout.OLD_RING), 3.2, "and arrives at the old ring");
        assert_lt(PlacesLayout.TOMS_ROUTE[0].DistanceTo(PlacesLayout.OLD_RING), 5.0, "Tom's route starts at the old ring");
    }

    void assert_within(double value, double target, string what)
    {
        assert_true(Math.Abs(value - target) <= target * 0.2, $"{what} is within 20% of {target:F0} m ({value:F1} m)");
    }

    public void test_paths_are_walkable_and_in_the_trail_field()
    {
        foreach (var (line, offset) in PlacesLayout.TRAIL_LINES)
        {
            double len = PlacesLayout.length(line);
            for (double s = 0.0; s <= len; s += 0.5)
            {
                Vector2 p = PlacesLayout.point_at(line, s, out _);
                assert_lt(land.walking_distance(p), offset + 0.01, "the trail field carries the path");
                assert_true(Player.InsidePlayArea(new Vector3(p.X, 0, p.Y), -2.0f), $"path point {p} is inside the walking area");
                assert_gt(land.height(p.X, p.Y), TerrainField.WATER_LEVEL + 0.2, "paths stay dry");
                double slope = Mathf.RadToDeg(land.surface_slope(p.X, p.Y));
                assert_lt(slope, 40.0, $"path at {p} is walkable ({slope:F0} degrees)");
            }
        }
        // Nothing planted or dropped stands in the way.
        var blaze = new HashSet<ScenePlan.TreeEntry>(scene.blaze_trees);
        foreach (ScenePlan.TreeEntry t in scene.trees)
        {
            double clear = blaze.Contains(t) ? 1.4 : 1.3;
            foreach (Vector2[] line in new[] { PlacesLayout.JUNE_FOOTPATH, PlacesLayout.TOMS_ROUTE, PlacesLayout.STEP_LINE, PlacesLayout.PAINTED_PATH })
                assert_gt(PlacesLayout.distance_to(t.position, line, out _), clear, $"no tree trunk at {t.position} on a path");
        }
        foreach (ScenePlan.RockEntry r in scene.rocks)
        {
            if (r.scale < 0.45) continue;
            foreach (Vector2[] line in new[] { PlacesLayout.JUNE_FOOTPATH, PlacesLayout.TOMS_ROUTE, PlacesLayout.STEP_LINE, PlacesLayout.PAINTED_PATH })
                assert_gt(PlacesLayout.distance_to(r.position, line, out _), r.scale * 0.6 + 0.5, $"no boulder at {r.position} on a path");
        }
    }

    public void test_tom_blazes_his_route_every_fifteen_to_twenty_five_metres()
    {
        PlacesLayout.BlazeSite[] sites = PlacesLayout.blaze_sites();
        assert_gt(sites.Length, 6, "Tom's route carries his blazes");
        assert_eq(sites.Length, scene.blaze_trees.Count, "each blaze has its tree in the plan");
        double previous = 0.0;
        foreach (PlacesLayout.BlazeSite b in sites)
        {
            double gap = b.along - previous;
            assert_true(gap >= 14.9 && gap <= 25.1, $"blaze at {b.along:F1} m is 15 to 25 m after the last ({gap:F1})");
            previous = b.along;
            Vector2 on_route = PlacesLayout.point_at(PlacesLayout.TOMS_ROUTE, b.along, out _);
            assert_gt(b.face.Dot((on_route - b.tree).Normalized()), 0.0, "the notch faces the route");
        }
        double rest = PlacesLayout.length(PlacesLayout.TOMS_ROUTE) - previous;
        assert_lt(rest, 25.1, "the last blaze is within reach of the first step");
    }
}
