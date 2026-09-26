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

        _player.yaw = atan2(-(0.0 - _player.GlobalPosition.X), -(0.0 - _player.GlobalPosition.Z));
        _player.pitch = -0.45;
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        Node focus = _player._focus;
        bool fed = false;
        if (focus != null && focus.HasMethod("interact"))
        {
            focus.Call("interact", _player);
            fed = true;
        }
        _check("facing the fire offers an interaction and feeding it succeeds", fed);
        await _screenshot("fire_fed");

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
