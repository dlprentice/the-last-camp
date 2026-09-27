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
        await _interactions();
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
        Quality.Instance.apply(QualityPreset.Tier.MEDIUM);
        while (Game.Instance.camp.understory._replant_thread != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        bool medium = Quality.Instance.current.tier == QualityPreset.Tier.MEDIUM;
        Quality.Instance.apply(start_tier);
        while (Game.Instance.camp.understory._replant_thread != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _check("quality steps down to Medium and back without errors", medium && Quality.Instance.current.tier == start_tier);
        await _screenshot("after_quality_change");
        if (Game.Instance.session is CampSession evening)
        {
            _check("camp journal recognises completed evening activities", evening.ReadyForNight);
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = true });
            await ToSignal(GetTree().CreateTimer(0.2, true), SceneTreeTimer.SignalName.Timeout);
            _check("journal input opens a paused readable journal", Game.Instance.hud.JournalVisible && GetTree().Paused);
            await _screenshot("journal");
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = false });
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = true });
            _player.GetViewport().PushInput(new InputEventAction { Action = "camp_journal", Pressed = false });
            _check("closing the journal resumes play", !GetTree().Paused && Game.Instance.mode == Game.Mode.PLAY);
            await _go_to("tent_return_dock", new Vector2(-16, 6.3f));
            await _go_to("tent_return_bank", new Vector2(-14.2f, 6.6f));
            await _go_to("tent_return_shore", new Vector2(-9, 2));
            await _go_to("tent_return_clearing", new Vector2(-3.6f, 3.6f));
            await _go_to("tent_return_seats", new Vector2(3.8f, 3.8f));
            await _go_to("tent_return_door", new Vector2(4.4f, -0.4f));
            await _go_to("tent_around_woodpile", new Vector2(3.6f, -2.8f));
            Vector3 entrance = Game.Instance.camp.campsite.tent.ToGlobal(new Vector3(-0.3f, 0, -2.6f));
            await _go_to("tent_entrance", new Vector2(entrance.X, entrance.Z));
            var bed = Game.Instance.camp.campsite.tent.GetNode<Interactable>("BedrollRest");
            Game.Instance.world.hour = 19;
            await _aim_and_interact(bed.GlobalPosition, bed);
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            _check("resting at the tent advances to dusk and restores control", !evening.Resting && _player.enabled && Game.Instance.world.hour > 20);
            await _aim_and_interact(bed.GlobalPosition, bed);
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            _check("sleeping completes the evening and returns to playable dawn", evening.MorningReached && evening.Completed == 4 && _player.enabled && Game.Instance.world.hour < 7);
            await _screenshot("morning_completed");
        }
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
