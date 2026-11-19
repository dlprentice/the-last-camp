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
using LastCamp.Construction;

namespace LastCamp;

/// Scripted walk through the camp for the acceptance record. Drives the real
/// player controller with the game's own input actions along waypoints (trail,
/// clearing, fire, table, tent, down to the shore, out along the dock and back,
/// into the shallows and out, back to the fire), then exercises photo mode with
/// a focus lock, the lantern, feeding the fire and a quality change. Each
/// waypoint records position, ground height, floor contact and water depth,
/// saves a screenshot when a rendering device is present, and the run fails on
/// falling through the ground, getting stuck or timing out.
///   godot --path . --fullscreen -- --skip-intro --quality=high --traverse="$PWD/local-data/traversal/run-1"
///   godot --headless --path . -- --skip-intro --traverse=/tmp/x    (physics only)
/// Add --traverse-places to walk the micro cut's places as well (see _walk_places),
/// and --traverse-story (with --session-check) to play the rest of the story to the
/// credits through the real interactions, logging the time spent in each band.
/// Straight legs between authored points; the route walks around the fire
/// ring and along the dock's centreline (start (-15.9, 6.3) towards the pond).
public partial class TraversalCheck : Node
{
    public static readonly Godot.Collections.Array<Godot.Collections.Dictionary> WAYPOINTS = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"name", "trail" }, { (StringName)"at", new Vector2(1.5f, 24.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "clearing" }, { (StringName)"at", new Vector2(1.0f, 9.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_edge" }, { (StringName)"at", new Vector2(2.6f, 3.4f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "table" }, { (StringName)"at", new Vector2(7.0f, 3.2f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "tent_front" }, { (StringName)"at", new Vector2(4.4f, -0.4f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "seat_gap" }, { (StringName)"at", new Vector2(3.8f, 3.8f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "west_of_fire" }, { (StringName)"at", new Vector2(-3.6f, 3.6f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "to_water" }, { (StringName)"at", new Vector2(-9.0f, 2.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "dock_root" }, { (StringName)"at", new Vector2(-16.0f, 6.3f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "dock_end" }, { (StringName)"at", new Vector2(-23.1f, 5.4f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "dock_root_back" }, { (StringName)"at", new Vector2(-16.0f, 6.3f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "wade_in" }, { (StringName)"at", new Vector2(-19.6f, 2.6f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "wade_out" }, { (StringName)"at", new Vector2(-13.5f, 0.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "west_of_fire_back" }, { (StringName)"at", new Vector2(-3.6f, 3.6f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_return" }, { (StringName)"at", new Vector2(2.4f, 3.2f) } } };
    public const double ARRIVE = 0.7;
    public const double WAYPOINT_TIMEOUT = 45.0;
    public const double STUCK_SECONDS = 6.0;
    public const double STUCK_PROGRESS = 0.35;

    public Player _player;
    public string _out_dir = "";
    public Godot.Collections.Dictionary _report = new Godot.Collections.Dictionary { { (StringName)"waypoints", new Godot.Collections.Array() }, { (StringName)"checks", new Godot.Collections.Array() }, { (StringName)"failures", new Godot.Collections.Array() } };
    public bool _running = false;

    public void run(Player player, string out_dir)
    {
        _player = player;
        if (out_dir.StripEdges() == "")
        {
            out_dir = "res://local-data/traversal/run";
        }
        _out_dir = ProjectSettings.GlobalizePath(out_dir);
        DirAccess.MakeDirRecursiveAbsolute(_out_dir);
        _running = true;
        _walk();
    }

    public async void _walk()
    {
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        Game.Instance.begin_capture_window("traversal");
        _report["start"] = _snapshot("start");
        foreach (Godot.Collections.Dictionary wp in WAYPOINTS)
        {
            bool ok = await _go_to(wp["name"].AsString(), wp["at"].AsVector2());
            Godot.Collections.Dictionary snap = _snapshot(wp["name"].AsString());
            snap["reached"] = ok;
            G.Call(_report["waypoints"], "append", snap);
            await _screenshot(wp["name"].AsString());
            G.print(G.format("TRAVERSAL waypoint=%s reached=%s pos=(%.2f %.2f %.2f) ground=%.2f floor=%s water=%.2f t=%.1f", new Godot.Collections.Array { wp["name"], ok, snap["x"], snap["y"], snap["z"], snap["ground"], snap["on_floor"], snap["water_depth"], snap["seconds"] }));
        }
        _release();
        _check("the walking route needed no fall recovery", _player.RecoveryCount == 0);
        await _check_woodland_collision();
        await _check_world_safety();
        if (Game.Instance.has_flag("traverse-places"))
        {
            await _walk_places();
        }
        if (Game.Instance.session is CampSession timed)
        {
            _start_band_clock(timed);
        }
        int expectedRecoveries = _player.RecoveryCount;
        await _interactions();
        _check("interactions needed no fall recovery", _player.RecoveryCount == expectedRecoveries);
        if (Game.Instance.has_flag("traverse-story"))
        {
            if (Game.Instance.session is CampSession session)
            {
                await _walk_story(session);
            }
            else
            {
                _check("the story walk needs the story session (--session-check)", false);
            }
        }
        if (Game.Instance.session != null)
        {
            _report_band_times();
        }
        bool passed = G.Call(_report["failures"], "is_empty").AsBool();
        _report["passed"] = passed;
        FileAccess file = FileAccess.Open(_out_dir.PathJoin("report.json"), FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(Json.Stringify(_report, "  "));
        }
        G.print(G.format("TRAVERSAL_DONE result=%s waypoints=%d failures=%d out=%s", new Godot.Collections.Array { passed ? "PASS" : "FAIL", G.Call(_report["waypoints"], "size"), G.Call(_report["failures"], "size"), _out_dir }));
        foreach (Variant f_item in G.Iter(_report["failures"]))
        {
            Variant f = f_item;
            G.print(G.format("TRAVERSAL_FAILURE %s", f));
        }
        Game.Instance.end_capture_window("traversal complete");
        Game.Instance.quit_cleanly(passed ? 0 : 1);
    }

    public async Task<bool> _go_to(string name_value, Vector2 target)
    {
        /// Steer the controller at the target with its own forward action until it
        /// arrives, gets stuck or times out. Falling below the ground is a failure.
        double elapsed = 0.0;
        double best = INF;
        double since_progress = 0.0;
        while (elapsed < WAYPOINT_TIMEOUT)
        {
            double delta = GetPhysicsProcessDeltaTime();
            Vector2 pos = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z);
            Vector2 to_target = target - pos;
            double distance = to_target.Length();
            if (distance < ARRIVE)
            {
                _release();
                return true;
            }
            _player.yaw = atan2(-to_target.X, -to_target.Y);
            Input.ActionPress("move_forward");
            if (distance < best - STUCK_PROGRESS)
            {
                best = distance;
                since_progress = 0.0;
            }
            else
            {
                since_progress += delta;
                if (since_progress > STUCK_SECONDS)
                {
                    G.Call(_report["failures"], "append", G.format("stuck %.1f m short of %s at (%.2f, %.2f)", new Godot.Collections.Array { distance, name_value, pos.X, pos.Y }));
                    _release();
                    return false;
                }
            }
            double ground = Game.Instance.camp.height_at(pos.X, pos.Y);
            if (_player.GlobalPosition.Y < ground - 0.6 && !_player.in_water)
            {
                G.Call(_report["failures"], "append", G.format("fell below the ground near %s at (%.2f, %.2f, %.2f), ground %.2f", new Godot.Collections.Array { name_value, pos.X, _player.GlobalPosition.Y, pos.Y, ground }));
                _release();
                return false;
            }
            elapsed += delta;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        G.Call(_report["failures"], "append", G.format("timed out walking to %s", name_value));
        _release();
        return false;
    }

    public void _release()
    {
        Input.ActionRelease("move_forward");
        Input.ActionRelease("sprint");
    }

    public Godot.Collections.Dictionary _snapshot(string label)
    {
        Vector3 p = _player.GlobalPosition;
        return new Godot.Collections.Dictionary { { (StringName)"label", label }, { (StringName)"x", p.X }, { (StringName)"y", p.Y }, { (StringName)"z", p.Z }, { (StringName)"ground", Game.Instance.camp.height_at(p.X, p.Z) }, { (StringName)"on_floor", _player.IsOnFloor() }, { (StringName)"water_depth", _player.water_depth }, { (StringName)"in_water", _player.in_water }, { (StringName)"seconds", (long)Time.GetTicksMsec() / 1000.0 } };
    }

    public async Task _screenshot(string label)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"TRAVERSAL_FRAME label={label} frame={Engine.GetFramesDrawn()}");
        Image image = GetViewport().GetTexture().GetImage();
        if (image != null)
        {
            image.SavePng(_out_dir.PathJoin(G.format("%s.png", label)));
        }
    }

    private async Task _check_woodland_collision()
    {
        var camp = Game.Instance.camp;
        bool supported = true;
        foreach (Vector2 p in new[] { new Vector2(240, 60), new Vector2(-320, 80), new Vector2(80, 450), new Vector2(-120, -620) })
        {
            float h = (float)camp.field.surface_height(p.X, p.Y);
            using var query = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, h + 0.3f, p.Y), new Vector3(p.X, h - 0.3f, p.Y), 1);
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            supported &= hit.Count > 0 && Math.Abs(hit["position"].AsVector3().Y - h) < 0.025;
        }
        _check("outer woodland collision matches rendered hills beyond the former 210 m limit", supported);

        var collision = camp.GetNode<WoodlandCollision>("WoodlandCollision");
        collision.SetPhysicsProcess(false);
        var tree = camp.forest.ridge_groups[0].Transforms[0];
        collision.Refresh(tree.Origin);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        using var trunkRay = PhysicsRayQueryParameters3D.Create(tree.Origin + new Vector3(3, 1, 0), tree.Origin + new Vector3(-3, 1, 0), 1);
        var trunkHit = _player.GetWorld3D().DirectSpaceState.IntersectRay(trunkRay);
        _check("ridge trunks receive pooled local collisions", trunkHit.Count > 0 &&
            trunkHit["collider"].AsGodotObject() is StaticBody3D body && body.GetParent() == collision);
        _check("ridge physics keeps a bounded local set instead of all trees", collision.ActiveBodies is > 0 and < 800);
        collision.Refresh(_player.GlobalPosition);
        collision.SetPhysicsProcess(true);
    }

    private async Task _check_world_safety()
    {
        Vector3 before = _player.GlobalPosition;
        double beforeYaw = _player.yaw;
        int recoveries = _player.RecoveryCount;
        foreach (Vector2 direction in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
        {
            Vector2 edge = Player.PLAY_AREA_CENTRE + direction * (Player.PLAY_AREA_RADIUS - 0.15f);
            float height = (float)Game.Instance.camp.field.surface_height(edge.X, edge.Y);
            _player.GlobalPosition = new Vector3(edge.X, height + 0.12f, edge.Y);
            _player.Velocity = Vector3.Zero;
            _player.yaw = Math.Atan2(-direction.X, -direction.Y);
            Input.ActionPress("move_forward");
            Input.ActionPress("sprint");
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            _release();
            Vector3 p = _player.GlobalPosition;
            _check($"sprinting toward woodland edge {direction} retains support", _player.IsOnFloor() &&
                Player.InsidePlayArea(p, 0.03f) &&
                _player.RecoveryCount == recoveries);
        }
        _player.GlobalPosition = before;
        _player.Velocity = Vector3.Zero;
        _player.yaw = beforeYaw;
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        _player.GlobalPosition -= Vector3.Up * 5;
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        _check("an injected fall-through recovers the last supported position", _player.RecoveryCount == recoveries + 1 &&
            _player.IsOnFloor() && _player.GlobalPosition.DistanceTo(before) < 0.25);
    }

    /// Opt-in (--traverse-places): the micro cut's walk through its places, on
    /// the real controller. June's footpath to the old ring, Tom's route by his
    /// blazes, up the twenty steps onto the loose one, June's painted path down to
    /// the trail, out to the trailhead post and back up the trail to the fire.
    private async Task _walk_places()
    {
        Places places = Game.Instance.camp.places;
        if (places == null)
        {
            _check("the places are built", false);
            return;
        }
        var arrived = new HashSet<string>();
        var seen = new HashSet<int>();
        int sat = 0;
        Action<string> on_arrive = id => arrived.Add(id);
        Action<int> on_blaze = i => seen.Add(i);
        Action on_sit = () => sat++;
        places.Arrived += on_arrive;
        places.BlazeInView += on_blaze;
        places.BenchSat += on_sit;
        int recoveries = _player.RecoveryCount;
        TerrainField field = Game.Instance.camp.field;

        // Round the fire and its seats to where the footpath leaves the trail.
        bool june = await _follow("to_footpath", new[] { new Vector2(-3.6f, 3.6f), TerrainField.TRAIL[6] });
        june &= await _follow("june_path", PlacesLayout.JUNE_FOOTPATH);
        await _screenshot("place_old_ring");
        _check("June's footpath reaches the old ring", june && arrived.Contains(Places.OLD_RING));

        Vector2[] route = PlacesLayout.TOMS_ROUTE;
        bool tom = await _follow("toms_route", route[..^1]);
        tom &= await _go_to("steps_foot", route[^1]);
        _record("steps_foot", tom);
        await _screenshot("place_steps_foot");
        _check("Tom's route reaches the foot of his steps", tom);
        _check($"Tom's blazes come into view along his route ({seen.Count} of {places.blazes.Count})", seen.Count * 2 >= places.blazes.Count);

        bool climbed = await _follow("steps", PlacesLayout.STEP_LINE[1..]);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        double summit = field.height(PlacesLayout.TOMS_ROCK.X, PlacesLayout.TOMS_ROCK.Y);
        _check($"the twenty steps climb to the summit (feet {_player.GlobalPosition.Y:F2}, summit {summit:F2})", climbed && _player.IsOnFloor() && _player.GlobalPosition.Y > summit + 0.04);
        _check("arriving at Tom's Rock is reported", arrived.Contains(Places.TOMS_ROCK));
        _check("the loose-step hook reports the player standing on it", places.IsPlayerOnLooseStep());
        Vector3 view = new Vector3(PlacesLayout.FACE.X, 0, PlacesLayout.FACE.Y);
        _player.yaw = Math.Atan2(-view.X, -view.Z);
        _player.pitch = -0.12;
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        await _screenshot("place_toms_rock_view");
        places.SitOnBench();
        _check("the bench hook reports sitting", sat == 1);

        bool down = await _go_to("bench_side", PlacesLayout.local(0.35, -1.35));
        down &= await _follow("painted_path", PlacesLayout.PAINTED_PATH);
        _check("June's painted path leads down to the trail", down);
        Godot.Collections.Array<Vector2> trail = TerrainField.TRAIL;
        bool out_walk = await _go_to("trail_south", trail[1]);
        out_walk &= await _go_to("trailhead", new Vector2(3.9f, 67.6f));
        await _screenshot("place_trailhead");
        _check("the trail leads out to the trailhead post", out_walk && arrived.Contains(Places.TRAILHEAD));
        bool back = await _follow("trail_back", new[] { trail[1], trail[2], trail[3], trail[4], new Vector2(2.4f, 3.2f) });
        _check("the trail leads back to the fire", back);
        _check("the places walk needed no fall recovery", _player.RecoveryCount == recoveries);
        places.Arrived -= on_arrive;
        places.BlazeInView -= on_blaze;
        places.BenchSat -= on_sit;
    }

    private async Task<bool> _follow(string label, IEnumerable<Vector2> points)
    {
        bool ok = true;
        int i = 0;
        foreach (Vector2 p in points)
        {
            string name = $"{label}_{i++}";
            bool reached = await _go_to(name, p);
            _record(name, reached);
            ok &= reached;
            if (!reached) break;
        }
        return ok;
    }

    private void _record(string name, bool reached)
    {
        Godot.Collections.Dictionary snap = _snapshot(name);
        snap["reached"] = reached;
        G.Call(_report["waypoints"], "append", snap);
        G.print(G.format("TRAVERSAL waypoint=%s reached=%s pos=(%.2f %.2f %.2f) ground=%.2f floor=%s water=%.2f t=%.1f", new Godot.Collections.Array { name, reached, snap["x"], snap["y"], snap["z"], snap["ground"], snap["on_floor"], snap["water_depth"], snap["seconds"] }));
    }

    public async Task _interactions()
    {
        /// Photo mode with focus lock and flight, the lantern, feeding the fire and a
        /// quality change: each is driven through the same code the player's keys use.
        PhotoRig rig = _player.photo;
        Vector3 before = _player.camera.GlobalPosition;
        rig.enter(_player.camera);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        bool entered = Game.Instance.mode == Game.Mode.PHOTO;
        Input.ActionPress("move_forward");
        Input.ActionPress("fly_up");
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease("move_forward");
        Input.ActionRelease("fly_up");
        double moved = rig.camera != null ? rig.camera.GlobalPosition.DistanceTo(before) : 0.0;
        rig.focus_locked = true;
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await _screenshot("photo_mode");
        rig.exit();
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        bool restored = Game.Instance.mode == Game.Mode.PLAY && _player.camera.Current;
        _check(G.format("photo mode entered, flew %.1f m, focus locked and restored the player camera", moved), entered && moved > 0.5 && restored);

        _player.toggle_lantern();
        bool lit = _player.lantern_on;
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        await _screenshot("lantern_held");
        _player.toggle_lantern();
        _check("lantern toggles on and off", lit && !_player.lantern_on);

        // Pick up actual firewood first. Calling any focused interactable used to
        // be reported as successful feeding, even with empty hands and no fuel change.
        await _go_to("woodpile_approach", new Vector2(4.6f, -0.05f));
        Interactable wood = Game.Instance.camp.campsite.woodpile;
        await _aim_and_interact(wood.GlobalPosition + Vector3.Up * 0.28f, wood);
        _check("woodpile pickup equips a log", _player.held_item == "log");
        await _screenshot("carrying_firewood");
        await _go_to("fire_feed_approach", new Vector2(1.7f, 1.8f));
        Firepit fire = Game.Instance.camp.campsite.firepit;
        double fuelBefore = fire.intensity;
        await _aim_and_interact(fire.GlobalPosition + Vector3.Up * 0.3f, fire.body);
        _check("feeding consumes the carried log and increases the fire", _player.held_item == "" && fire.intensity > fuelBefore + 0.3);
        await _screenshot("fire_fed");
        if (Game.Instance.session != null)
        {
            // The story's evening asks for three logs: carry the other two for real.
            await _carry_log_to_fire();
            await _carry_log_to_fire();
        }
        await _aim_and_interact(fire.GlobalPosition + Vector3.Up * 0.3f, fire.body);
        _check("empty-handed fire interaction starts resting", _player.resting);
        Input.ActionPress("move_back");
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease("move_back");
        _check("movement leaves the resting pose", !_player.resting);

        await _go_to("stones_around_fire", new Vector2(-3.6f, 3.6f));
        await _go_to("stones_shore", new Vector2(-9, 2));
        await _go_to("stones_dock_root", new Vector2(-16, 6.3f));
        await _go_to("stones_dock_end", new Vector2(-23.1f, 5.4f));
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        Vector3 supportedPosition = _player.GlobalPosition;
        Game.Instance.hud._toggle_pause();
        await ToSignal(GetTree().CreateTimer(0.2, true), SceneTreeTimer.SignalName.Timeout);
        Game.Instance.hud._toggle_pause();
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        _check("pausing and resuming preserves the dock support", _player.GlobalPosition.DistanceTo(supportedPosition) < 0.1 && _player.IsOnFloor());
        _player.photo.enter(_player.camera);
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        _player.photo.exit();
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        _check("photo mode restores the dock position without sinking", _player.GlobalPosition.DistanceTo(supportedPosition) < 0.1 && _player.IsOnFloor());
        Interactable stones = Game.Instance.camp.campsite.dock.GetNode<Interactable>("SkippingStones");
        await _aim_and_interact(stones.GlobalPosition, stones);
        _check("stone pickup equips a stone", _player.held_item == "stone");
        Vector3 throwDirection = -Game.Instance.camp.campsite.dock.GlobalBasis.Z;
        _player.yaw = Math.Atan2(-throwDirection.X, -throwDirection.Z);
        _player.pitch = -0.02;
        _player.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        await _screenshot("stone_aim");
        _check("holding throw charges the stone", _player.charging_stone && _player.stone_charge > 0.9);
        _player.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
        _check("release consumes the stone and produces water skips", _player.held_item == "" && Game.Instance.camp.pond.last_skips >= 2);
        await _screenshot("stone_result");

        QualityPreset.Tier start_tier = Quality.Instance.current.tier;
        QualityPreset.Tier test_tier = start_tier == QualityPreset.Tier.MEDIUM ? QualityPreset.Tier.LOW : QualityPreset.Tier.MEDIUM;
        Quality.Instance.apply(test_tier);
        while (Game.Instance.camp.understory._replant_thread != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        bool changed = Quality.Instance.current.tier == test_tier && test_tier != start_tier;
        Quality.Instance.apply(start_tier);
        while (Game.Instance.camp.understory._replant_thread != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _check("quality changes to a different preset and restores the original", changed && Quality.Instance.current.tier == start_tier);
        await _screenshot("after_quality_change");
        if (Game.Instance.session is CampSession evening)
        {
            // The three logs and the throw above went through the real carry and
            // throw; so does everything below: the letter on the table, a throw
            // of three, the sit at the pier end and the night. Only the plain
            // traversal (without --traverse-story) still injects three skips if
            // every real throw fell short.
            var story = evening.Story;
            var logs = story.Band.Objectives.First(o => o.Event == LastCamp.Story.StoryEvents.LogCarried);
            _check("story counts the three carried logs and the throw", story.ProgressOf(logs) == 3 &&
                story.Facts.Contains(LastCamp.Story.StoryEvents.StoneSkips));
            bool storyRun = Game.Instance.has_flag("traverse-story");
            await _throw_until_three();
            if (Game.Instance.camp.pond.last_skips < 3 && story.BestSkips < 3)
            {
                _check("a real throw reaches three skips", !storyRun);
                G.print(G.format("TRAVERSAL injected=stone_skips:3 thrown=%d", (long)Game.Instance.camp.pond.last_skips));
                if (!storyRun) evening.Story.Raise(LastCamp.Story.StoryEvents.StoneSkips + ":3");
            }
            await _follow("to_table", new[] { new Vector2(-16, 6.3f), new Vector2(-14.2f, 6.6f), new Vector2(-9, 2), new Vector2(-3.6f, 3.6f), new Vector2(3.8f, 3.8f), new Vector2(6.4f, 1.3f), new Vector2(7.6f, 0.1f) });
            CampKitchen kitchen = Game.Instance.camp.campsite.kitchen;
            Vector3 table_side = kitchen.ToGlobal(new Vector3(-0.07f, 0, -0.85f));
            await _go_to("table_side", new Vector2(table_side.X, table_side.Z));
            await _aim_and_interact(kitchen.letter_body.GlobalPosition + Vector3.Up * 0.03f, kitchen.letter_body);
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            _check("reading June's letter opens the sheet and raises letter_read", evening.Reading.IsOpen && story.Facts.Contains(LastCamp.Story.StoryEvents.LetterRead));
            await _screenshot("june_letter");
            await _read_through(evening);
            _check("the letter is taken from the table", !kitchen.letter.Visible && story.Items.Contains("june_letter"));
            _check("camp journal recognises completed evening activities", evening.BandDone);
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = true });
            await ToSignal(GetTree().CreateTimer(0.2, true), SceneTreeTimer.SignalName.Timeout);
            _check("journal input opens a paused readable journal", Game.Instance.hud.JournalVisible && GetTree().Paused);
            await _screenshot("journal");
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = false });
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = true });
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = false });
            _check("closing the journal resumes play", !GetTree().Paused && Game.Instance.mode == Game.Mode.PLAY);

            // The pier end: sitting there runs the sunset into the night.
            await _follow("to_pier", new[] { new Vector2(6.4f, 1.3f), new Vector2(3.8f, 3.8f), new Vector2(-3.6f, 3.6f), new Vector2(-9, 2), new Vector2(-14.2f, 6.6f), new Vector2(-16, 6.3f), new Vector2(-23.1f, 5.4f) });
            Interactable pier = evening.PierSeat;
            await _aim_and_interact(pier.GlobalPosition, pier);
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            _check("sitting at the pier end raises sat:pier and seats the player", story.Facts.Contains(LastCamp.Story.StoryEvents.SatPier) && _player.resting);
            double sunset_from = Game.Instance.world.hour;
            await _wait_for(() => story.Band.Id == "night", LastCamp.Story.StoryScript.SunsetSeconds + 20);
            _check(G.format("the sunset runs the clock from %.2f into the night (%.2f)", new Godot.Collections.Array { sunset_from, Game.Instance.world.hour }),
                story.Band.Id == "night" && Game.Instance.world.hour >= 20.45 && _player.enabled);
            await _screenshot("pier_sunset_done");

            // Night: light the fire with a real log, light the lantern, turn in.
            await _follow("night_walk", new[] { new Vector2(-16, 6.3f), new Vector2(-14.2f, 6.6f), new Vector2(-9, 2), new Vector2(-3.6f, 3.6f), new Vector2(3.8f, 3.8f), new Vector2(4.4f, -0.4f) });
            await _carry_log_to_fire();
            if (!_player.lantern_on) _player.toggle_lantern();
            _check("lighting the fire and the lantern completes the night", evening.BandDone);
            await _go_to_tent("night");
            Tent tent = Game.Instance.camp.campsite.tent;
            if (storyRun)
            {
                // Before the old fire ring Tom's tin does not offer itself at all.
                await _aim(tent.tin_body.GlobalPosition);
                _check("the tin is refused before the old ring (no interaction is offered)", _player.focus() != tent.tin_body && !story.TinAvailable);
            }
            var bed = tent.GetNode<Interactable>("BedrollRest");
            await _aim_and_interact(bed.GlobalPosition, bed);
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            _check("sleeping completes the evening and returns to playable dawn", evening.Story.Chapter.Id == LastCamp.Story.StoryScript.Marks && _player.enabled && Game.Instance.world.hour < 7);
            await _screenshot("morning_completed");
        }
    }

    /// Opt-in (--traverse-story): the rest of the story, from the second morning to
    /// the credits, through the real controller and the real interactions only.
    private async Task _walk_story(CampSession session)
    {
        var story = session.Story;
        Places places = Game.Instance.camp.places;
        Campsite site = Game.Instance.camp.campsite;
        Tent tent = site.tent;
        Firepit fire = site.firepit;
        int recoveries = _player.RecoveryCount;
        _check("the story walk starts on the second morning", story.Chapter.Id == LastCamp.Story.StoryScript.Marks && story.Band.Id == "morning");

        // Morning: June's footpath to the old ring; the notch before the tin is nothing.
        await _go_to_old_ring("morning");
        _check("arriving at the old ring raises place:old_ring and June's entry", story.Facts.Contains(LastCamp.Story.StoryEvents.PlaceOldRing) && story.LinesPlayed.Contains("e4"));
        await _look_at_notch();
        _check("looking at the notch before the tin raises nothing", !story.Facts.Contains(LastCamp.Story.StoryEvents.BlazeFound));
        await _screenshot("story_notch_before_tin");
        await _follow("ring_back", Enumerable.Reverse(PlacesLayout.JUNE_FOOTPATH).Skip(1).Append(TerrainField.TRAIL[6]).Append(new Vector2(-3.6f, 3.6f)).Append(new Vector2(3.8f, 3.8f)).Append(new Vector2(4.4f, -0.4f)));
        await _go_to_tent("tin");
        await _aim_and_interact(tent.tin_body.GlobalPosition + Vector3.Up * 0.02f, tent.tin_body);
        await _wait_for(() => session.Reading.IsOpen, 3);
        _check("opening Tom's tin raises tin_opened and shows his tally book", story.Facts.Contains(LastCamp.Story.StoryEvents.TinOpened) && session.Reading.IsOpen && session.Reading.PageCount == LastCamp.Story.StoryTexts.TallyBook.Length && tent.tin_open);
        await _screenshot("story_tally_book");
        await _read_through(session);
        _check("June falls silent once the tin is open", story.JuneSilent);
        var bed = tent.GetNode<Interactable>("BedrollRest");
        await _aim_and_interact(bed.GlobalPosition, bed);
        await _wait_for(() => story.Band.Id == "evening" && !session.Resting, 10);
        _check("resting after the tin carries the day to evening", story.Band.Id == "evening" && _player.enabled);

        // Evening: the notch now counts; Tom's route, his steps, the bench, the step.
        await _go_to_old_ring("evening");
        await _look_at_notch();
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        _check("looking at the notch within 2 m after the tin raises blaze_found", story.Facts.Contains(LastCamp.Story.StoryEvents.BlazeFound) &&
            story.IsDone(story.Band.Objectives.First(o => o.Event == LastCamp.Story.StoryEvents.BlazeFound)));
        Vector2[] route = PlacesLayout.TOMS_ROUTE;
        bool tom = await _follow("story_toms_route", route[..^1]);
        tom &= await _go_to("story_steps_foot", route[^1]);
        _check("the lift is not offered before the player has stood on the step", !places.loose_step_stood_on && session.StepLift.CollisionLayer == 0);
        bool climbed = await _follow("story_steps", PlacesLayout.STEP_LINE[1..]);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        _check(G.format("the loose step rocks under the player (%.2f deg) and is marked stood on", new Godot.Collections.Array { places.rock.rock_angle }),
            tom && climbed && places.IsPlayerOnLooseStep() && places.loose_step_stood_on && places.rock.rock_angle > 1.0);
        await _aim(session.StepLift.GlobalPosition);
        _check("standing on the step before the bench still offers no lift", _player.focus() != session.StepLift && !session.LiftOffered);
        await _aim_and_interact(session.BenchSeat.GlobalPosition, session.BenchSeat);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        _check("sitting on Tom's bench raises sat:lookout and June's entry", story.Facts.Contains(LastCamp.Story.StoryEvents.SatLookout) && story.LinesPlayed.Contains("e5") && places.bench_sat);
        double from = Game.Instance.world.hour;
        await _wait_for(() => Game.Instance.world.hour >= story.Band.Limit - 0.01, LastCamp.Story.StoryScript.SunsetSeconds + 20);
        _check(G.format("the lookout sunset runs the clock from %.2f to %.2f", new Godot.Collections.Array { from, Game.Instance.world.hour }), Game.Instance.world.hour >= story.Band.Limit - 0.01);
        await _screenshot("story_lookout_sunset");
        _check("after the bench the stood-on step offers Lift", session.LiftOffered);
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        await _aim_and_interact(session.StepLift.GlobalPosition, session.StepLift);
        await _wait_for(() => session.Reading.IsOpen, 6);
        _check("lifting the step sets the stone aside and shows Tom's letter", places.StepIsLifted && session.Reading.IsOpen &&
            places.LooseStep.GlobalPosition.DistanceTo(places.rock.loose_rest.Origin) > 0.5);
        await _screenshot("story_tom_letter");
        await _read_through(session);
        await _wait_for(() => story.Band.Storm, 3);
        _check("reading Tom's letter raises step_lifted and the walk down begins", story.Facts.Contains(LastCamp.Story.StoryEvents.StepLifted) && story.Band.Storm && story.Items.Contains("tom_letter"));

        // The walk down by lantern, then the storm.
        if (!_player.lantern_on) _player.toggle_lantern();
        bool down = await _go_to("story_bench_side", PlacesLayout.local(0.35, -1.35));
        down &= await _follow("story_painted_path", PlacesLayout.PAINTED_PATH);
        Godot.Collections.Array<Vector2> trail = TerrainField.TRAIL;
        Vector2 path_end = PlacesLayout.PAINTED_PATH[^1];
        int join = 0;
        for (int i = 0; i < 5; i++) if (trail[i].DistanceTo(path_end) < trail[join].DistanceTo(path_end)) join = i;
        var to_camp = new List<Vector2>();
        for (int i = join + 1; i <= 4; i++) to_camp.Add(trail[i]);
        to_camp.Add(new Vector2(2.4f, 3.2f));
        down &= await _follow("story_trail_down", to_camp);
        _check("the walk down by lantern reaches camp", down && _player.lantern_on);
        await _wait_for(() => Game.Instance.world.weather.rain > 0.3, 90);
        _check("the storm's rain arrives", Game.Instance.world.weather.rain > 0.3);
        await _screenshot("story_storm");
        await _go_to("story_woodpile_from_fire", new Vector2(3.4f, 0.9f));
        await _carry_log_to_fire();
        await _carry_log_to_fire();
        var fed = story.Band.Objectives.First(o => o.Event == LastCamp.Story.StoryEvents.FireFedInRain);
        _check("the fire is fed twice in the rain", story.IsDone(fed));
        await _go_to("story_storm_door", new Vector2(4.4f, -0.4f));
        await _go_to_tent("storm");
        await _aim_and_interact(bed.GlobalPosition, bed);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        _check("resting in the tent in the storm shelters the player", _player.resting);
        await _wait_for(() => story.Chapter.Id == LastCamp.Story.StoryScript.Epilogue && !session.Resting, LastCamp.Story.StoryScript.ShelterSeconds + 30);
        _check(G.format("sheltering ends the storm and the next morning comes (%s)", new Godot.Collections.Array { story.StormOutcome }),
            story.Chapter.Id == LastCamp.Story.StoryScript.Epilogue && story.StormOutcome.Length > 0 && story.LinesPlayed.Contains("e6") && _player.enabled);

        // The last morning: strike the tent, rake the ash, scatter the stones, walk out.
        var linesAfterAsh = new List<string>();
        bool ashDone = false;
        Action<string, LastCamp.Story.LineKind, string> watch = (id, _, _) => { if (ashDone) linesAfterAsh.Add(id); };
        session.LinePlayed += watch;
        await _aim_and_interact(tent.GlobalPosition + Vector3.Up * 0.8f, tent.strike_body);
        await _wait_for(() => story.Facts.Contains(LastCamp.Story.StoryEvents.TentStruck), Tent.PEG_SECONDS + Tent.FALL_SECONDS + Tent.PACK_SECONDS + 3);
        _check("striking the tent brings it down and packs it away", story.Facts.Contains(LastCamp.Story.StoryEvents.TentStruck) && !tent.Visible);
        await _screenshot("story_tent_struck");
        await _go_to("story_to_fire", new Vector2(4.6f, -0.05f));
        await _go_to("story_fire_side", new Vector2(1.7f, 1.8f));
        await _aim_and_interact(fire.GlobalPosition + Vector3.Up * 0.3f, fire.body);
        await ToSignal(GetTree().CreateTimer(Firepit.RAKE_SECONDS + 0.4), SceneTreeTimer.SignalName.Timeout);
        ashDone = story.Facts.Contains(LastCamp.Story.StoryEvents.AshRaked);
        _check(G.format("raking the ash changes it (%s) and plays June's last note", new Godot.Collections.Array { story.StormOutcome == "went_out" ? "cold and wet" : "warm embers" }),
            ashDone && fire.raked_ash != null && fire.raked_ash.Visible && !fire.coals.Visible && story.LinesPlayed.Contains("m9"));
        await _screenshot("story_ash_raked");
        int moves = 0;
        while (!fire.stones_scattered && moves < 6)
        {
            await _aim_and_interact(fire.GlobalPosition + Vector3.Up * 0.3f, fire.body);
            moves++;
            await ToSignal(GetTree().CreateTimer(Firepit.STONE_STAGGER * CampSession.StonesPerMove + Firepit.STONE_SECONDS + 0.3), SceneTreeTimer.SignalName.Timeout);
        }
        double nearest = fire.ring_stones.Min(st => new Vector2(st.Position.X, st.Position.Z).Length());
        _check(G.format("moving the ring stones sets all %d down off the site in %d moves (nearest %.2f m)", new Godot.Collections.Array { fire.ring_stones.Count, moves, nearest }),
            fire.stones_scattered && moves >= 3 && nearest >= Firepit.SCATTER_MIN - 0.3 && story.Facts.Contains(LastCamp.Story.StoryEvents.StonesScattered));
        await _screenshot("story_stones_scattered");
        _check("the last morning is done", session.BandDone);
        Vector2 out_at = CampSession.WalkOutPoint;
        bool walked = await _follow("story_walk_out", new[] { new Vector2(2.4f, 3.2f), trail[4], trail[3], trail[2], trail[1], new Vector2(3.9f, 67.6f), out_at });
        await _wait_for(() => story.Finished, 3);
        _check("walking out past the trailhead post raises walked_out", walked && story.Finished && story.Facts.Contains(LastCamp.Story.StoryEvents.WalkedOut));
        await _wait_for(() => story.Facts.Contains(LastCamp.Story.StoryEvents.CreditsFinished), CampSession.EndingFallbackSeconds + 60);
        _check("the ending runs and raises credits_finished, then June's postcard", story.Facts.Contains(LastCamp.Story.StoryEvents.CreditsFinished) && story.LinesPlayed.Contains("e7"));
        session.LinePlayed -= watch;
        _check($"nothing follows June's last note until the postcard ({string.Join(" ", linesAfterAsh)})", linesAfterAsh.All(id => id == "e7"));
        _check("the story walk needed no fall recovery", _player.RecoveryCount == recoveries);
    }

    /// The camp to the old ring: round the fire to the footpath, then June's path.
    private async Task _go_to_old_ring(string label)
    {
        bool ok = await _follow($"{label}_to_ring", new[] { new Vector2(3.6f, -2.8f), new Vector2(4.4f, -0.4f), new Vector2(3.8f, 3.8f), new Vector2(-3.6f, 3.6f), TerrainField.TRAIL[6] });
        ok &= await _follow($"{label}_june_path", PlacesLayout.JUNE_FOOTPATH);
        _check($"June's footpath reaches the old ring ({label})", ok);
    }

    /// Steps up to Tom's first notch and looks at it from a little over a metre.
    private async Task _look_at_notch()
    {
        OldFireRing ring = Game.Instance.camp.places.ring;
        Vector3 face = new Vector3(ring.notch_face.X, 0, ring.notch_face.Z).Normalized();
        Vector3 stand = ring.notch_world + face * 1.0f;
        await _go_to("notch_side", new Vector2(stand.X, stand.Z));
        await _aim(ring.notch_world);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        G.print(G.format("TRAVERSAL notch distance=%.2f", _player.camera.GlobalPosition.DistanceTo(ring.notch_world)));
    }

    /// From the camp's east side round the woodpile to the tent door.
    private async Task _go_to_tent(string label)
    {
        await _go_to($"{label}_around_woodpile", new Vector2(3.6f, -2.8f));
        Vector3 entrance = Game.Instance.camp.campsite.tent.ToGlobal(new Vector3(-0.3f, 0, -2.6f));
        await _go_to($"{label}_tent_entrance", new Vector2(entrance.X, entrance.Z));
    }

    /// Puts the reading sheet away page by page, with the interact key.
    private async Task _read_through(CampSession session)
    {
        for (int i = 0; i < 12 && session.Reading.IsOpen; i++)
        {
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            _player.GetViewport().PushInput(new InputEventAction { Action = "interact", Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _player.GetViewport().PushInput(new InputEventAction { Action = "interact", Pressed = false });
        }
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        _check("the reading sheet is put away and play resumes", !session.Reading.IsOpen && _player.enabled);
    }

    /// Throws again from the pier end until a throw makes three skips (a few tries).
    private async Task _throw_until_three()
    {
        Interactable stones = Game.Instance.camp.campsite.dock.GetNode<Interactable>("SkippingStones");
        double[] pitches = { -0.02, -0.05, 0.0, -0.08, 0.02, -0.035 };
        for (int i = 0; i < pitches.Length && Game.Instance.camp.pond.last_skips < 3 && Game.Instance.session.BestSkips < 3; i++)
        {
            await _aim_and_interact(stones.GlobalPosition, stones);
            Vector3 throwDirection = -Game.Instance.camp.campsite.dock.GlobalBasis.Z;
            _player.yaw = Math.Atan2(-throwDirection.X, -throwDirection.Z);
            _player.pitch = pitches[i];
            _player.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            _player.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
            G.print(G.format("TRAVERSAL rethrow pitch=%.3f skips=%d", new Godot.Collections.Array { pitches[i], (long)Game.Instance.camp.pond.last_skips }));
        }
    }

    private async Task _wait_for(Func<bool> done, double seconds)
    {
        double waited = 0;
        while (!done() && waited < seconds)
        {
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
            waited += 0.25;
        }
    }

    /// Looks at `target` from the settled eye, without interacting.
    private async Task _aim(Vector3 target)
    {
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        Vector3 direction = target - _player.eye_position();
        _player.yaw = Math.Atan2(-direction.X, -direction.Z);
        _player.pitch = Math.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        _player.interact_ray.ForceRaycastUpdate();
        _player._update_focus();
    }

    // ---- Time per band --------------------------------------------------------------

    private readonly List<(string band, double seconds)> _bandTimes = new();
    private string _bandName = "";
    private double _bandStart;

    private void _start_band_clock(CampSession session)
    {
        _bandName = $"{session.Story.Chapter.Id}/{session.Story.Band.Id}";
        _bandStart = Time.GetTicksMsec() / 1000.0;
        session.BandStarted += t => _close_band(t.ToChapter != null ? $"{t.ToChapter.Id}/{t.ToBand!.Id}" : "");
    }

    private void _close_band(string next)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_bandName.Length > 0)
        {
            _bandTimes.Add((_bandName, now - _bandStart));
            G.print(G.format("TRAVERSAL_BAND band=%s seconds=%.1f", new Godot.Collections.Array { _bandName, now - _bandStart }));
        }
        _bandName = next;
        _bandStart = now;
    }

    private void _report_band_times()
    {
        if (_bandName.Length > 0 && Game.Instance.session is CampSession s && !s.Story.Finished) _close_band("");
        var times = new Godot.Collections.Dictionary();
        foreach ((string band, double seconds) in _bandTimes) times[band] = seconds;
        _report["band_seconds"] = times;
        double total = _bandTimes.Sum(b => b.seconds);
        _report["story_seconds"] = total;
        G.print(G.format("TRAVERSAL_STORY_TIME seconds=%.1f bands=%d", new Godot.Collections.Array { total, _bandTimes.Count }));
    }
    /// Walks to the woodpile, takes a log, walks to the fire and adds it.
    private async Task _carry_log_to_fire()
    {
        await _go_to("woodpile_approach", new Vector2(4.6f, -0.05f));
        Interactable wood = Game.Instance.camp.campsite.woodpile;
        await _aim_and_interact(wood.GlobalPosition + Vector3.Up * 0.28f, wood);
        await _go_to("fire_feed_approach", new Vector2(1.7f, 1.8f));
        Firepit fire = Game.Instance.camp.campsite.firepit;
        await _aim_and_interact(fire.GlobalPosition + Vector3.Up * 0.3f, fire.body);
        _check("a carried log reaches the fire", _player.held_item == "");
    }

    private async Task _aim_and_interact(Vector3 target, Node expected)
    {
        // Walking decelerates after key release. Aim from the settled eye, not
        // the previous moving pose (small objects otherwise slip off the ray).
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        Vector3 direction = target - _player.eye_position();
        _player.yaw = Math.Atan2(-direction.X, -direction.Z);
        _player.pitch = Math.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        _player.interact_ray.ForceRaycastUpdate();
        _player._update_focus();
        if (_player.focus() != expected)
            GD.Print($"TRAVERSAL_AIM expected={expected.Name} eye={_player.eye_position()} target={target} distance={direction.Length():F2} hit={(_player.interact_ray.GetCollider() as Node)?.Name}");
        _check($"interaction ray reaches {expected.Name}", _player.focus() == expected);
        _player.GetViewport().PushInput(new InputEventAction { Action = "interact", Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _player.GetViewport().PushInput(new InputEventAction { Action = "interact", Pressed = false });
    }

    public void _check(string label, bool ok)
    {
        G.Call(_report["checks"], "append", new Godot.Collections.Dictionary { { (StringName)"label", label }, { (StringName)"ok", ok } });
        G.print(G.format("TRAVERSAL check=%s ok=%s", new Godot.Collections.Array { label, ok }));
        if (!ok)
        {
            G.Call(_report["failures"], "append", G.format("check failed: %s", label));
        }
    }
}
