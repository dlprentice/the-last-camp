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

/// Headless-friendly verification tool.
///
/// `--capture=DIR` renders a fixed set of viewpoints to PNG files so visual
/// changes can be reviewed without playing; `--benchmark[=low,medium,...]`
/// flies a camera path per quality preset and writes frame statistics as JSON.
/// SDFGI and the temporal volumetrics take a few seconds to converge after a
/// teleport; a still taken earlier shows distant canopies too pale.
public partial class CaptureTool : Node
{
    public const double SETTLE_SECONDS = 7.0;
    public const double BENCH_SECONDS = 22.0;
    public const double WARMUP_SECONDS = 3.0;

    /// Named viewpoints: position, look target and an optional hour override.
    public static readonly Godot.Collections.Array<Godot.Collections.Dictionary> VIEWPOINTS = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"name", "linen_dry" }, { (StringName)"pos", new Vector3(7.8f, 1.55f, 3.3f) }, { (StringName)"look", new Vector3(9.2f, 0.87f, 1.2f) }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 17.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "linen_wet" }, { (StringName)"pos", new Vector3(7.8f, 1.55f, 3.3f) }, { (StringName)"look", new Vector3(9.2f, 0.87f, 1.2f) }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 17.5 }, { (StringName)"weather", "dawn" } }, new Godot.Collections.Dictionary { { (StringName)"name", "bark_dry" }, { (StringName)"pos", new Vector3(14.0f, 1.7f, -10.0f) }, { (StringName)"look", new Vector3(18.0f, 5.0f, -22.0f) }, { (StringName)"fov", 42.0 }, { (StringName)"hour", 17.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "bark_wet" }, { (StringName)"pos", new Vector3(14.0f, 1.7f, -10.0f) }, { (StringName)"look", new Vector3(18.0f, 5.0f, -22.0f) }, { (StringName)"fov", 42.0 }, { (StringName)"hour", 17.5 }, { (StringName)"weather", "dawn" } }, new Godot.Collections.Dictionary { { (StringName)"name", "rain_deck" }, { (StringName)"pos", new Vector3(-20.65f, -0.02f, 6.07f) }, { (StringName)"look", new Vector3(-23.5f, -0.28f, 5.20f) }, { (StringName)"absolute", true }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 21.92 }, { (StringName)"weather", "rain_detail" } }, new Godot.Collections.Dictionary { { (StringName)"name", "dry_deck" }, { (StringName)"pos", new Vector3(-20.65f, -0.02f, 6.07f) }, { (StringName)"look", new Vector3(-23.5f, -0.28f, 5.20f) }, { (StringName)"absolute", true }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 21.92 } }, new Godot.Collections.Dictionary { { (StringName)"name", "rain_canvas" }, { (StringName)"pos", new Vector3(6.9f, 1.25f, 0.0f) }, { (StringName)"look", new Vector3(7.5f, 0.8f, -4.5f) }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 21.55 }, { (StringName)"weather", "storm" }, { (StringName)"weather_time", 33.08 }, { (StringName)"weather_duration", 40.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "rain_rocks" }, { (StringName)"pos", new Vector3(-14.0f, 0.70f, 8.4f) }, { (StringName)"look", new Vector3(-19.0f, 0.0f, 6.0f) }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 21.55 }, { (StringName)"weather", "storm" }, { (StringName)"weather_time", 33.02 }, { (StringName)"weather_duration", 40.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_detail" }, { (StringName)"pos", new Vector3(0.1f, 0.85f, 2.5f) }, { (StringName)"look", new Vector3(0, 0.67f, 0) }, { (StringName)"fov", 45.0 }, { (StringName)"hour", 20.2 } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_side" }, { (StringName)"pos", new Vector3(-2.2f, 0.75f, 0.3f) }, { (StringName)"look", new Vector3(0, 0.60f, 0) }, { (StringName)"fov", 44.0 }, { (StringName)"hour", 20.2 } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_fed" }, { (StringName)"pos", new Vector3(0.1f, 0.85f, 2.5f) }, { (StringName)"look", new Vector3(0, 0.67f, 0) }, { (StringName)"fov", 45.0 }, { (StringName)"hour", 20.2 }, { (StringName)"fire", 2.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "lilies" }, { (StringName)"pos", new Vector3(-27.5f, -0.19f, 11.5f) }, { (StringName)"look", new Vector3(-29.2f, -0.87f, 14.4f) }, { (StringName)"absolute", true }, { (StringName)"fov", 40.0 }, { (StringName)"hour", 19.15 } }, new Godot.Collections.Dictionary { { (StringName)"name", "table_join" }, { (StringName)"pos", new Vector3(7.8f, 0.65f, 2.7f) }, { (StringName)"look", new Vector3(9.2f, 0.74f, 1.2f) }, { (StringName)"fov", 43.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "tent_side" }, { (StringName)"pos", new Vector3(6.9f, 1.25f, 0.0f) }, { (StringName)"look", new Vector3(7.5f, 0.8f, -4.5f) }, { (StringName)"fov", 48.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "trail" }, { (StringName)"pos", new Vector3(2.7f, 1.55f, 15.0f) }, { (StringName)"look", new Vector3(1.0f, 0.08f, 5.0f) }, { (StringName)"fov", 64.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "kitchen" }, { (StringName)"pos", new Vector3(7.8f, 1.55f, 3.3f) }, { (StringName)"look", new Vector3(9.2f, 0.87f, 1.2f) }, { (StringName)"fov", 48.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "flowers" }, { (StringName)"pos", new Vector3(6.8f, 0.72f, 9.4f) }, { (StringName)"look", new Vector3(5.5f, 0.45f, 8.0f) }, { (StringName)"fov", 44.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "arrival" }, { (StringName)"pos", new Vector3(3.2f, 1.72f, 15.0f) }, { (StringName)"look", new Vector3(-14.0f, 0.9f, 3.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire" }, { (StringName)"pos", new Vector3(4.6f, 1.55f, 3.8f) }, { (StringName)"look", new Vector3(0.0f, 0.7f, 0.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "pond" }, { (StringName)"pos", new Vector3(-15.5f, 1.7f, 9.0f) }, { (StringName)"look", new Vector3(-42.0f, 0.5f, -2.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "dock" }, { (StringName)"pos", new Vector3(-21.0f, 1.5f, 6.3f) }, { (StringName)"look", new Vector3(-45.0f, 3.0f, 12.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "treeline" }, { (StringName)"pos", new Vector3(-6.0f, 1.7f, -6.0f) }, { (StringName)"look", new Vector3(12.0f, 7.0f, -40.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "tent" }, { (StringName)"pos", new Vector3(2.2f, 1.6f, -0.6f) }, { (StringName)"look", new Vector3(7.5f, 0.9f, -4.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_tent" }, { (StringName)"pos", new Vector3(2.2f, 1.6f, -0.6f) }, { (StringName)"look", new Vector3(7.5f, 0.9f, -4.5f) }, { (StringName)"hour", 23.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "camp" }, { (StringName)"pos", new Vector3(6.2f, 1.5f, 3.6f) }, { (StringName)"look", new Vector3(3.0f, 0.4f, -2.6f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "smoke" }, { (StringName)"pos", new Vector3(3.4f, 1.2f, 3.2f) }, { (StringName)"look", new Vector3(0.0f, 2.6f, 0.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "lantern" }, { (StringName)"pos", new Vector3(2.2f, 1.45f, 11.9f) }, { (StringName)"look", new Vector3(3.6f, 1.35f, 13.1f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "lantern_side" }, { (StringName)"pos", new Vector3(2.6f, 1.4f, 14.6f) }, { (StringName)"look", new Vector3(3.6f, 1.35f, 13.1f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "lantern_back" }, { (StringName)"pos", new Vector3(5.2f, 1.3f, 14.2f) }, { (StringName)"look", new Vector3(3.6f, 1.35f, 13.1f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "tent_post" }, { (StringName)"pos", new Vector3(4.2f, 1.4f, -0.4f) }, { (StringName)"look", new Vector3(5.9f, 1.2f, -3.1f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ground" }, { (StringName)"pos", new Vector3(1.5f, 0.7f, 12.0f) }, { (StringName)"look", new Vector3(-2.0f, -0.2f, 7.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "canopy" }, { (StringName)"pos", new Vector3(14.0f, 1.7f, -10.0f) }, { (StringName)"look", new Vector3(18.0f, 13.0f, -22.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire_smoke_day" }, { (StringName)"pos", new Vector3(2.6f, 1.25f, 2.3f) }, { (StringName)"look", new Vector3(0.0f, 1.15f, 0.0f) }, { (StringName)"fov", 44.0 }, { (StringName)"hour", 10.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "oak_base" }, { (StringName)"pos", new Vector3(20.5f, 1.1f, -9.2f) }, { (StringName)"look", new Vector3(17.5f, 0.6f, -11.5f) }, { (StringName)"absolute", true }, { (StringName)"fov", 46.0 }, { (StringName)"hour", 17.5 }, { (StringName)"weather", "dawn" } }, new Godot.Collections.Dictionary { { (StringName)"name", "aftermath2" }, { (StringName)"pos", new Vector3(20.8f, 1.3f, -10.3f) }, { (StringName)"look", new Vector3(12.0f, 1.9f, -5.8f) }, { (StringName)"absolute", true }, { (StringName)"fov", 50.0 }, { (StringName)"hour", 17.5 }, { (StringName)"weather", "dawn" } }, new Godot.Collections.Dictionary { { (StringName)"name", "aerial_west" }, { (StringName)"pos", new Vector3(12.0f, 42.0f, 34.0f) }, { (StringName)"look", new Vector3(-140.0f, 12.0f, -120.0f) }, { (StringName)"absolute", true }, { (StringName)"fov", 60.0 }, { (StringName)"hour", 9.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "aerial_ridge" }, { (StringName)"pos", new Vector3(-30.0f, 70.0f, 60.0f) }, { (StringName)"look", new Vector3(-60.0f, 30.0f, -420.0f) }, { (StringName)"absolute", true }, { (StringName)"fov", 55.0 }, { (StringName)"hour", 9.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "overview" }, { (StringName)"pos", new Vector3(48.0f, 26.0f, 54.0f) }, { (StringName)"look", new Vector3(-8.0f, 1.0f, -2.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "intro_start" }, { (StringName)"pos", new Vector3(16.3f, 8.0f, 52.0f) }, { (StringName)"look", new Vector3(-4.0f, 3.5f, 4.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "intro_quarter" }, { (StringName)"pos", new Vector3(6.4f, 3.9f, 40.0f) }, { (StringName)"look", new Vector3(-5.0f, 2.4f, 3.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "intro_half" }, { (StringName)"pos", new Vector3(7.9f, 4.2f, 28.0f) }, { (StringName)"look", new Vector3(-8.0f, 1.4f, 2.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "intro_mid" }, { (StringName)"pos", new Vector3(13.0f, 4.6f, 26.0f) }, { (StringName)"look", new Vector3(-9.0f, 1.2f, 2.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "canoe" }, { (StringName)"pos", new Vector3(-19.5f, 1.5f, 0.4f) }, { (StringName)"look", new Vector3(-25.0f, -0.6f, 4.8f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "dock_end" }, { (StringName)"pos", new Vector3(-24.5f, 1.6f, 6.9f) }, { (StringName)"look", new Vector3(-8.0f, 0.6f, 1.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "underwater" }, { (StringName)"pos", new Vector3(-24.0f, -1.6f, 4.0f) }, { (StringName)"look", new Vector3(-30.0f, -1.9f, 7.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "underwater_sun" }, { (StringName)"pos", new Vector3(-23.8f, -2.25f, 3.4f) }, { (StringName)"look", new Vector3(-30.8f, 1.5f, 2.8f) }, { (StringName)"absolute", true }, { (StringName)"fov", 60.0 }, { (StringName)"hour", 17.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "underwater_evening" }, { (StringName)"pos", new Vector3(-23.8f, -2.25f, 3.4f) }, { (StringName)"look", new Vector3(-30.8f, 1.5f, 2.8f) }, { (StringName)"absolute", true }, { (StringName)"fov", 60.0 }, { (StringName)"hour", 19.18 } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_fire" }, { (StringName)"pos", new Vector3(4.6f, 1.55f, 3.8f) }, { (StringName)"look", new Vector3(0.0f, 0.7f, 0.0f) }, { (StringName)"hour", 23.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_pond" }, { (StringName)"pos", new Vector3(-15.5f, 1.7f, 9.0f) }, { (StringName)"look", new Vector3(-42.0f, 0.5f, -2.0f) }, { (StringName)"hour", 23.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "noon" }, { (StringName)"pos", new Vector3(32.0f, 15.0f, 36.0f) }, { (StringName)"look", new Vector3(-4.0f, 2.0f, 0.0f) }, { (StringName)"hour", 13.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "aftermath" }, { (StringName)"pos", new Vector3(22.9f, 1.5f, -12.2f) }, { (StringName)"look", new Vector3(14.0f, 3.0f, -9.5f) }, { (StringName)"hour", 17.5 }, { (StringName)"weather", "dawn" } }, new Godot.Collections.Dictionary { { (StringName)"name", "hero_oak" }, { (StringName)"pos", new Vector3(24.5f, 1.6f, -5.5f) }, { (StringName)"look", new Vector3(17.5f, 5.5f, -11.5f) }, { (StringName)"fov", 55.0 }, { (StringName)"hour", 17.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "hero_oak_fork" }, { (StringName)"pos", new Vector3(21.0f, 3.0f, -8.5f) }, { (StringName)"look", new Vector3(17.5f, 7.0f, -11.5f) }, { (StringName)"fov", 50.0 }, { (StringName)"hour", 17.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "specimen_pine" }, { (StringName)"pos", new Vector3(-7.0f, 1.6f, -13.0f) }, { (StringName)"look", new Vector3(-13.0f, 6.0f, -19.0f) }, { (StringName)"fov", 55.0 }, { (StringName)"hour", 17.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "specimen_alder" }, { (StringName)"pos", new Vector3(15.0f, 1.6f, 8.0f) }, { (StringName)"look", new Vector3(21.0f, 5.5f, 13.5f) }, { (StringName)"fov", 55.0 }, { (StringName)"hour", 17.0 } }, new Godot.Collections.Dictionary { { (StringName)"name", "dawn" }, { (StringName)"pos", new Vector3(-15.5f, 1.7f, 9.0f) }, { (StringName)"look", new Vector3(-42.0f, 0.5f, -2.0f) }, { (StringName)"hour", 6.1 } }, new Godot.Collections.Dictionary { { (StringName)"name", "woodpile" }, { (StringName)"pos", new Vector3(3.8f, 0.85f, -0.2f) }, { (StringName)"look", new Vector3(4.6f, 0.30f, -2.2f) }, { (StringName)"fov", 42.0 }, { (StringName)"hour", 17.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "woodpile_night" }, { (StringName)"pos", new Vector3(3.8f, 0.85f, -0.2f) }, { (StringName)"look", new Vector3(4.6f, 0.30f, -2.2f) }, { (StringName)"fov", 42.0 }, { (StringName)"hour", 22.5 } }, new Godot.Collections.Dictionary { { (StringName)"name", "path_grazing" }, { (StringName)"pos", new Vector3(-6.0f, 0.9f, 1.0f) }, { (StringName)"look", new Vector3(-14.0f, 0.3f, 3.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "grass_near" }, { (StringName)"pos", new Vector3(9.0f, 1.1f, 8.0f) }, { (StringName)"look", new Vector3(-4.0f, 0.3f, 2.0f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "waterline" }, { (StringName)"pos", new Vector3(-22.0f, -0.88f, 9.5f) }, { (StringName)"look", new Vector3(-30.0f, -0.7f, 4.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "waterline_low" }, { (StringName)"pos", new Vector3(-22.0f, -0.93f, 9.5f) }, { (StringName)"look", new Vector3(-30.0f, -0.8f, 4.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "breach" }, { (StringName)"pos", new Vector3(-29.0f, 0.7f, 1.2f) }, { (StringName)"look", new Vector3(-16.0f, 1.2f, 6.0f) }, { (StringName)"absolute", true } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_dock" }, { (StringName)"pos", new Vector3(-14.0f, 1.6f, 9.0f) }, { (StringName)"look", new Vector3(-24.5f, 0.5f, 5.0f) }, { (StringName)"hour", 23.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_meadow" }, { (StringName)"pos", new Vector3(7.0f, 1.55f, 7.0f) }, { (StringName)"look", new Vector3(0.5f, 0.9f, -1.0f) }, { (StringName)"hour", 23.4 } }, new Godot.Collections.Dictionary { { (StringName)"name", "night_lantern" }, { (StringName)"pos", new Vector3(2.2f, 1.45f, 11.9f) }, { (StringName)"look", new Vector3(3.6f, 1.35f, 13.1f) }, { (StringName)"hour", 23.4 } } };

    // Elevated views of the whole woodland, for judging how filled-in the
    // distant forest reads and where the near trees hand over to the ridges.
    public static readonly Godot.Collections.Array<Vector3> BENCH_PATH = new Godot.Collections.Array<Vector3> { new Vector3(6.0f, 1.7f, 22.0f), new Vector3(4.0f, 1.7f, 10.0f), new Vector3(-4.0f, 1.7f, 4.0f), new Vector3(-12.0f, 1.7f, 3.0f), new Vector3(-20.0f, 1.6f, 6.0f), new Vector3(-14.0f, 1.7f, 12.0f), new Vector3(-2.0f, 1.7f, 10.0f), new Vector3(8.0f, 1.7f, -2.0f), new Vector3(4.0f, 1.7f, -10.0f) };
    public static readonly Godot.Collections.Array<Vector3> BENCH_LOOK = new Godot.Collections.Array<Vector3> { new Vector3(-10.0f, 1.0f, 2.0f), new Vector3(-6.0f, 1.0f, 0.0f), new Vector3(-30.0f, 1.0f, 0.0f), new Vector3(-40.0f, 2.0f, 4.0f), new Vector3(-45.0f, 3.0f, 10.0f), new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), new Vector3(2.0f, 2.0f, -20.0f), new Vector3(0.0f, 0.8f, 0.0f) };

    public Camera3D camera;

    public override void _Ready()
    {
        camera = new Camera3D();
        camera.Fov = 68.0f;
        camera.Near = 0.05f;
        camera.Far = 1600.0f;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        AddChild(camera);
        camera.MakeCurrent();
        if (Game.Instance.world != null)
        {
            camera.Attributes = Game.Instance.world.attributes;
        }
        Game.Instance.mode = Game.Mode.PHOTO;
        Game.Instance.hud_visible = false;
    }

    public async void run_capture(string dir, string filter)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        List<string> wanted = new List<string>();
        if (filter != "")
        {
            wanted = G.split(filter, ",", false);
        }
        double base_hour = Game.Instance.world.hour;
        Godot.Collections.Array<Godot.Collections.Dictionary> viewpoints = VIEWPOINTS.Duplicate();
        viewpoints.AddRange(_prop_viewpoints());
        if (Game.Instance.has_flag("capture-sequence"))
        {
            if (Game.Instance.player != null)
            {
                Game.Instance.player.SetPhysicsProcess(false);
                Game.Instance.player.GlobalPosition = new Vector3(10000, 10, 10000);
            }
            viewpoints.Clear();
            List<Cinematic.Shot> shots = Cinematic.sequence(Game.Instance.arg_value("capture-sequence", "afterglow"));
            Godot.Collections.Array<double> samples = new Godot.Collections.Array<double>();
            foreach (string value in G.split(Game.Instance.arg_value("capture-samples", "0.5"), ",", false))
            {
                if (!value.IsValidFloat() || G.to_float(value) < 0.0 || G.to_float(value) > 1.0)
                {
                    G.push_error("capture-samples expects fractions from 0 to 1");
                    GetTree().Quit(1);
                    return;
                }
                samples.Add(G.to_float(value));
            }
            double fire_level = 1.0;
            bool lanterns_on = true;
            double wind = 1.0;
            for (long i = 0, i_end = (long)shots.Count; i < i_end; i++)
            {
                Cinematic.Shot shot = shots[(int)i];
                Spline path = new Spline(Cinematic.resolve(Game.Instance.camp.field, shot.path, shot.absolute));
                Spline look = new Spline(Cinematic.resolve(Game.Instance.camp.field, shot.look, shot.absolute));
                if (!is_nan(shot.fire))
                {
                    fire_level = shot.fire;
                }
                if (shot.lantern_state >= 0)
                {
                    lanterns_on = shot.lantern_state == 1;
                }
                foreach (double progress in samples)
                {
                    double u = Cinematic.motion_progress(shot, shot.duration * progress);
                    Vector3 pose = Cinematic.camera_position(shot, path, Game.Instance.camp.field, shot.duration * progress);
                    double hour = !is_nan(shot.hour) ? shot.hour : base_hour;
                    if (!is_nan(shot.hour_end))
                    {
                        hour = lerpf(hour, shot.hour_end, progress);
                    }
                    bool lit = lanterns_on || !is_nan(shot.lantern_hour) && hour >= shot.lantern_hour;
                    double gust = wind;
                    if (!is_nan(shot.wind))
                    {
                        gust = lerpf(shot.wind, !is_nan(shot.wind_end) ? shot.wind_end : shot.wind, progress);
                    }
                    string @base = G.format("film_%02d", i + 1);
                    string name = @base;
                    if ((long)samples.Count > 1)
                    {
                        name += G.format("_p%03d", roundi(progress * 100));
                    }
                    viewpoints.Add(new Godot.Collections.Dictionary { { (StringName)"name", name }, { (StringName)"base", @base }, { (StringName)"pos", pose }, { (StringName)"look", Cinematic.aim_target(shot, pose, look.sample(u), shot.duration * progress) }, { (StringName)"absolute", true }, { (StringName)"hour", hour }, { (StringName)"fov", lerpf(shot.fov, !is_nan(shot.fov_end) ? shot.fov_end : shot.fov, u) }, { (StringName)"fire", _film_fire_at(shot, fire_level, progress) }, { (StringName)"lanterns", lit }, { (StringName)"wind", gust }, { (StringName)"focus", shot.focus }, { (StringName)"weather", shot.weather }, { (StringName)"weather_time", shot.duration * progress }, { (StringName)"weather_duration", shot.duration }, { (StringName)"wildlife_cue", (StringName)shot.wildlife_cue }, { (StringName)"wildlife_time", shot.duration * progress }, { (StringName)"exposure", lerpf(shot.exposure, !is_nan(shot.exposure_end) ? shot.exposure_end : shot.exposure, progress) }, { (StringName)"focus_distance", lerpf(shot.focus_distance, !is_nan(shot.focus_distance_end) ? shot.focus_distance_end : shot.focus_distance, smoothstep(0.25, 0.75, progress)) } });
                }
                if (!is_nan(shot.lantern_hour))
                {
                    lanterns_on = true;
                }
                fire_level = _film_fire_at(shot, fire_level, 1.0);
                if (!is_nan(shot.wind_end))
                {
                    wind = shot.wind_end;
                }
            }
        }
        long saved = 0;
        foreach (Godot.Collections.Dictionary vp in viewpoints)
        {
            string name2 = vp["name"].AsString();
            // --shots= names a viewpoint, or a film shot whatever samples it carries.
            if (!(wanted.Count == 0) && !wanted.Contains(name2) && !wanted.Contains(G.get(vp, "base", name2).AsString()))
            {
                continue;
            }
            Game.Instance.world.weather.apply_chapter(G.get(vp, "weather", "").AsString(), G.get(vp, "weather_time", 0.0).AsDouble(), G.get(vp, "weather_duration", 1.0).AsDouble(), false);
            Game.Instance.world.hour = G.get(vp, "hour", base_hour).AsDouble();
            if (Game.Instance.camp.wildlife != null && vp.ContainsKey("wildlife_cue"))
            {
                Game.Instance.camp.wildlife.set_film_cue(vp["wildlife_cue"].AsStringName(), vp["wildlife_time"].AsDouble());
            }
            Game.Instance.world.exposure_scale = G.get(vp, "exposure", 1.0).AsDouble();
            Game.Instance.camp.campsite.firepit.intensity = G.get(vp, "fire", 1.0).AsDouble();
            Cinematic._set_lanterns(G.get(vp, "lanterns", true).AsBool());
            Game.Instance.world.wind_scale = G.get(vp, "wind", 1.0).AsDouble();
            bool absolute = G.get(vp, "absolute", false).AsBool();
            camera.GlobalPosition = absolute ? vp["pos"].AsVector3() : ground_relative(vp["pos"].AsVector3());
            camera.LookAt(absolute ? vp["look"].AsVector3() : ground_relative(vp["look"].AsVector3()), Vector3.Up);
            camera.Fov = G.get(vp, "fov", 68.0).AsSingle();
            Game.Instance.world.reset_water_lens();
            Vector3 target = absolute ? vp["look"].AsVector3() : ground_relative(vp["look"].AsVector3());
            double focus_distance = G.get(vp, "focus_distance", NAN).AsDouble();
            bool tight = !is_nan(focus_distance);
            Cinematic.apply_focus(G.get(vp, "focus", false).AsBool(), tight ? focus_distance : camera.GlobalPosition.DistanceTo(target), tight);
            await _wait(SETTLE_SECONDS);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            Image img = GetViewport().GetTexture().GetImage();
            string path2 = G.format("%s/%s.png", new Godot.Collections.Array { dir, name2 });
            Error saved_error = img.SavePng(path2);
            if (saved_error != Error.Ok)
            {
                G.push_error("Screenshot write failed: " + path2);
                Game.Instance.quit_cleanly(1);
                return;
            }
            saved += 1;
            G.print(G.format("SHOT %s fps=%d", new Godot.Collections.Array { path2, Engine.GetFramesPerSecond() }));
            if (Game.Instance.has_flag("dump-environment"))
            {
                Environment env = Game.Instance.world.environment;
                G.print("ENV ", Json.Stringify(new Godot.Collections.Dictionary { { (StringName)"shot", name2 }, { (StringName)"hour", Game.Instance.world.hour }, { (StringName)"underwater", Game.Instance.world.underwater }, { (StringName)"camera", G.str(camera.GlobalPosition) }, { (StringName)"fog", G.str(env.FogLightColor) }, { (StringName)"density", env.FogDensity }, { (StringName)"volume_density", env.VolumetricFogDensity }, { (StringName)"volume_albedo", G.str(env.VolumetricFogAlbedo) }, { (StringName)"history", env.VolumetricFogTemporalReprojectionAmount } }));
            }
            if (Game.Instance.has_flag("dump-reflection") && Game.Instance.camp.pond != null)
            {
                Image refl = Game.Instance.camp.pond.reflection_viewport.GetTexture().GetImage();
                refl.SavePng(G.format("%s/%s_reflection.png", new Godot.Collections.Array { dir, name2 }));
                Game.Instance.camp.pond.underwater_viewport.GetTexture().GetImage().SavePng(G.format("%s/%s_underwater_reflection.png", new Godot.Collections.Array { dir, name2 }));
                Game.Instance.camp.pond.transmission_viewport.GetTexture().GetImage().SavePng(G.format("%s/%s_transmission.png", new Godot.Collections.Array { dir, name2 }));
            }
        }
        Game.Instance.world.hour = base_hour;
        G.print(G.format("CAPTURE_DONE images=%d", saved));
        Game.Instance.quit_cleanly(saved > 0 ? 0 : 1);
    }

    public async void run_pond_check(string dir)
    {
        /// A coupled scene check: native boat responds to a push and a nearby impact,
        /// then settles. The sparse GPU probes and actual hull are sampled in motion.
        DirAccess.MakeDirRecursiveAbsolute(dir);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Pond pond = Game.Instance.camp.pond;
        Canoe boat = Game.Instance.camp.campsite.dock.canoe;
        Game.Instance.player.SetPhysicsProcess(false);
        Game.Instance.player.GlobalPosition = new Vector3(10000, 10, 10000);
        Game.Instance.world.hour = 17.8;
        Game.Instance.world.wind_scale = 0.8;
        camera.GlobalPosition = new Vector3(-21.1f, 0.6f, 0.8f);
        camera.LookAt(boat.GlobalPosition + Vector3.Up * 0.20f, Vector3.Up);
        camera.Fov = 48.0f;
        await _wait(8.0);
        Vector3 original = boat.GlobalPosition;
        GetViewport().GetTexture().GetImage().SavePng(dir.PathJoin("boat-rest.png"));
        double before = pond.surface_time;
        boat.ApplyCentralImpulse(new Vector3(-7.0f, 1.0f, 0.0f));
        pond.ripple(boat.GlobalPosition + new Vector3(0.6f, 0, 0), 1.0);
        double maximum_heave = 0.0;
        double maximum_drift = 0.0;
        double maximum_tilt = 0.0;
        double maximum_probe_age = 0.0;
        double maximum_residual = 0.0;
        bool received_probe_sample = false;
        bool finite = true;
        bool image_taken = false;
        while (pond.surface_time - before < 16.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector3 delta = boat.GlobalPosition - original;
            maximum_heave = maxf(maximum_heave, absf(delta.Y));
            maximum_drift = maxf(maximum_drift, new Vector2(delta.X, delta.Z).Length());
            maximum_tilt = maxf(maximum_tilt, maxf(absf(boat.Rotation.X), absf(boat.Rotation.Z)));
            finite = finite && boat.GlobalPosition.IsFinite() && boat.LinearVelocity.IsFinite() && boat.Rotation.IsFinite() && boat.AngularVelocity.IsFinite();
            if (pond.simulation != null && pond.simulation.get_probe_sample_time() >= 0.0)
            {
                received_probe_sample = true;
                maximum_probe_age = maxf(maximum_probe_age, pond.simulation.get_simulation_time() - pond.simulation.get_probe_sample_time());
                foreach (float height in pond.simulation.get_probe_heights())
                {
                    finite = finite && is_finite(height);
                    maximum_residual = maxf(maximum_residual, absf(height));
                }
            }
            if (!image_taken && pond.surface_time - before >= 1.3)
            {
                GetViewport().GetTexture().GetImage().SavePng(dir.PathJoin("boat-disturbed.png"));
                image_taken = true;
            }
        }
        GetViewport().GetTexture().GetImage().SavePng(dir.PathJoin("boat-settled.png"));
        Godot.Collections.Dictionary report = new Godot.Collections.Dictionary { { (StringName)"finite", finite }, { (StringName)"gpu_ready", pond.simulation != null && pond.simulation.is_ready() }, { (StringName)"max_heave_m", maximum_heave }, { (StringName)"max_drift_m", maximum_drift }, { (StringName)"max_tilt_degrees", rad_to_deg(maximum_tilt) }, { (StringName)"max_probe_age_seconds", maximum_probe_age }, { (StringName)"received_probe_sample", received_probe_sample }, { (StringName)"max_sampled_residual_m", maximum_residual }, { (StringName)"settled_speed_m_s", boat.LinearVelocity.Length() }, { (StringName)"preset", Quality.Instance.current.display_name } };
        Rid rid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        Godot.Collections.Dictionary costs = new Godot.Collections.Dictionary();
        foreach (Variant enabled_item in new Godot.Collections.Array { true, false, true })
        {
            bool enabled = enabled_item.AsBool();
            pond.simulation_paused = !enabled;
            RenderingServer.GlobalShaderParameterSet("pond_interaction_enabled", enabled);
            await _wait(1.2);
            double frames = 0.0;
            double gpu = 0.0;
            for (long sample = 0; sample < 60; sample++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames += GetProcessDeltaTime() * 1000.0;
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            }
            string label = enabled && costs.ContainsKey("on") ? "on_repeat" : enabled ? "on" : "off";
            costs[label] = new Godot.Collections.Dictionary { { (StringName)"frame_ms", frames / 60.0 }, { (StringName)"main_viewport_gpu_ms", gpu / 60.0 } };
        }
        report["interaction_comparison"] = costs;
        bool ok = finite && G.truthy(report["gpu_ready"]) && received_probe_sample && maximum_probe_age < 0.5 && maximum_residual > 0.00001 && maximum_heave < 0.16 && maximum_drift < 0.65 && maximum_tilt < 0.26 && G.op("<", report["settled_speed_m_s"], 0.10).AsBool();
        report["passed"] = ok;
        FileAccess file = FileAccess.Open(dir.PathJoin("report.json"), FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(report, "\t"));
        G.print("POND_CHECK ", Json.Stringify(report));
        foreach (Godot.Collections.Dictionary view in _prop_viewpoints())
        {
            camera.GlobalPosition = view["pos"].AsVector3();
            camera.LookAt(view["look"].AsVector3(), Vector3.Up);
            await _wait(1.0);
            GetViewport().GetTexture().GetImage().SavePng(dir.PathJoin(G.op("+", view["name"], ".png").AsString()));
        }
        if (!ok)
        {
            G.push_error("Coupled pond check failed");
            GetTree().Quit(1);
        }
        else
        {
            Game.Instance.quit_cleanly();
        }
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> _prop_viewpoints()
    {
        Dock dock = Game.Instance.camp.campsite.dock;
        Canoe boat = dock.canoe;
        // Close views expose open stems, rim joints and seating that crosses a hull.
        Godot.Collections.Array<Godot.Collections.Dictionary> views = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"name", "boat-bow" }, { (StringName)"pos", new Vector3(0.75f, 1.15f, -3.35f) }, { (StringName)"look", new Vector3(0, 0.16f, -1.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "boat-stern" }, { (StringName)"pos", new Vector3(-0.75f, 1.15f, 3.35f) }, { (StringName)"look", new Vector3(0, 0.16f, 1.5f) } }, new Godot.Collections.Dictionary { { (StringName)"name", "boat-interior" }, { (StringName)"pos", new Vector3(0.55f, 2.3f, 0.5f) }, { (StringName)"look", new Vector3(0, 0.18f, 0) } } };
        foreach (Godot.Collections.Dictionary view in views)
        {
            view["pos"] = boat.ToGlobal(view["pos"].AsVector3());
            view["look"] = boat.ToGlobal(view["look"].AsVector3());
            view["absolute"] = true;
            view["fov"] = 48.0;
            view["hour"] = 17.8;
        }
        Node3D stones = dock.GetNode<Node3D>("SkippingStones");
        views.Add(new Godot.Collections.Dictionary { { (StringName)"name", "skipping_stones" }, { (StringName)"pos", dock.ToGlobal(stones.Position + new Vector3(0.24f, 0.38f, 0.36f)) }, { (StringName)"look", stones.GlobalPosition - Vector3.Up * 0.04f }, { (StringName)"absolute", true }, { (StringName)"fov", 48.0 }, { (StringName)"hour", 17.8 } });
        return views;
    }

    public static double _film_fire_at(Cinematic.Shot shot, double start, double progress)
    {
        if (!is_nan(shot.fire_end))
        {
            return lerpf(start, shot.fire_end, smoothstep(0.0, 1.0, progress));
        }
        double seconds = shot.duration * progress;
        if (shot.feed_fire && seconds >= shot.feed_at)
        {
            double fed = minf(maxf(0.0, start - shot.feed_at * Firepit.DECAY_PER_SECOND) + Firepit.FEED_AMOUNT, Firepit.MAX_INTENSITY);
            return maxf(0.0, fed - (seconds - shot.feed_at) * Firepit.DECAY_PER_SECOND);
        }
        return maxf(0.0, start - seconds * Firepit.DECAY_PER_SECOND);
    }

    public async void run_benchmark(string presets, string out_path)
    {
        Godot.Collections.Array<QualityPreset.Tier> tiers = new Godot.Collections.Array<QualityPreset.Tier>();
        if (presets == "")
        {
            tiers = Quality.TIER_ORDER.Duplicate();
        }
        else
        {
            foreach (string name in G.split(presets, ",", false))
            {
                tiers.Add(Quality.tier_from_name(name.StripEdges().ToLowerInvariant()));
            }
        }
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Godot.Collections.Dictionary results = new Godot.Collections.Dictionary();
        Spline path = new Spline(BENCH_PATH);
        Spline look = new Spline(BENCH_LOOK);
        foreach (QualityPreset.Tier tier in tiers)
        {
            Quality.Instance.apply(tier);
            QualityPreset preset = Quality.Instance.current;
            // Measure the complete layout after a preset change, not its worker build.
            while (Game.Instance.camp.understory._replant_thread != null)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            camera.GlobalPosition = ground_relative(path.sample(0.0));
            camera.LookAt(ground_relative(look.sample(0.0)), Vector3.Up);
            await _wait(WARMUP_SECONDS);
            List<float> frame_times = new List<float>();
            double elapsed = 0.0;
            while (elapsed < BENCH_SECONDS)
            {
                double dt = GetProcessDeltaTime();
                elapsed += dt;
                double u = elapsed / BENCH_SECONDS;
                camera.GlobalPosition = ground_relative(path.sample(u));
                camera.LookAt(ground_relative(look.sample(u)), Vector3.Up);
                frame_times.Add((float)(dt * 1000.0));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            results[preset.display_name.ToLowerInvariant()] = summarise(frame_times);
            G.print(G.format("BENCH %s: %s", new Godot.Collections.Array { preset.display_name, Json.Stringify(results[preset.display_name.ToLowerInvariant()]) }));
        }
        FileAccess file = FileAccess.Open(out_path, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            Godot.Collections.Dictionary payload = new Godot.Collections.Dictionary { { (StringName)"engine", Engine.GetVersionInfo()["string"] }, { (StringName)"gpu", RenderingServer.GetVideoAdapterName() }, { (StringName)"resolution", G.format("%dx%d", new Godot.Collections.Array { G.Index(GetViewport().Get("size"), "x"), G.Index(GetViewport().Get("size"), "y") }) }, { (StringName)"results", results } };
            file.StoreString(Json.Stringify(payload, "  "));
            G.print("BENCH_WRITTEN ", ProjectSettings.GlobalizePath(out_path));
        }
        G.print("BENCH_DONE");
        Game.Instance.quit_cleanly();
    }

    public async void run_profile(string out_path)
    {
        /// `--profile`: measures the GPU cost of each renderer feature at the two
        /// heaviest viewpoints by switching features off one at a time.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Rid rid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        WorldController world = Game.Instance.world;
        Camp camp = Game.Instance.camp;
        Node scene = GetTree().CurrentScene;
        Node floor_dressing = scene.GetNodeOrNull("ForestFloorDressing");
        Node biome_dressing = scene.GetNodeOrNull("BiomeDressing");
        Node habitat = camp.GetNodeOrNull("HabitatDiversity");
        Node drips = camp.GetNodeOrNull("CanopyDrips");
        _profile_snapshot(scene);
        _profile_refl_lod = camp.pond.reflection_viewport.MeshLodThreshold;
        _profile_refl_far = camp.pond.reflection_camera.Far;
        _profile_fire_casters = (long)camp.campsite.firepit.light.ShadowCasterMask;
        _profile_sun_casters = (long)world.sun.ShadowCasterMask;
        Godot.Collections.Array<Godot.Collections.Dictionary> cases = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"name", "all on" }, { (StringName)"apply", Callable.From(() =>
{

}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "sdfgi off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.SdfgiEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ssil off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.SsilEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ssao off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.SsaoEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "volumetric off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.VolumetricFogEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ssr off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.SsrEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "glow off" }, { (StringName)"apply", Callable.From(() =>
{
    world.environment.GlowEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "planar refl off" }, { (StringName)"apply", Callable.From(() =>
{
    camp.pond.planar_enabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "sun shadow off" }, { (StringName)"apply", Callable.From(() =>
{
    world.sun.ShadowEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "soft shadows low" }, { (StringName)"apply", Callable.From(() =>
{
    RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
    RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "shadow atlas 2048" }, { (StringName)"apply", Callable.From(() => RenderingServer.DirectionalShadowAtlasSetSize(2048, true)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "shadow 2 splits" }, { (StringName)"apply", Callable.From(() =>
{
    world.sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "shadow dist 60" }, { (StringName)"apply", Callable.From(() =>
{
    world.sun.DirectionalShadowMaxDistance = 60.0f;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire shadow off" }, { (StringName)"apply", Callable.From(() =>
{
    camp.campsite.firepit.light.ShadowEnabled = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "lantern shadows off" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (CampLantern l in camp.campsite.lanterns)
    {
        l.light.ShadowEnabled = false;
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "understory hidden" }, { (StringName)"apply", Callable.From(() =>
{
    camp.understory.Visible = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "grass hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.understory, new Godot.Collections.Array { "Grass_", "HillGrass_" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "near grass hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.understory, new Godot.Collections.Array { "Grass_" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "hill grass hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.understory, new Godot.Collections.Array { "HillGrass_" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ferns hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.understory, new Godot.Collections.Array { "Ferns" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "shrubs hidden" }, { (StringName)"apply", Callable.From(() =>
{
    _profile_hide(camp.understory, new Godot.Collections.Array { "Shrubs", "HillShrubs_" });
    if (biome_dressing != null)
    {
        _profile_hide(biome_dressing, new Godot.Collections.Array { "WetBankShrubs" });
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "meadow hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.understory, new Godot.Collections.Array { "MeadowTussocks_", "MeadowSeedHeads_" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "habitat hidden" }, { (StringName)"apply", Callable.From(() =>
{
    if (habitat != null)
    {
        habitat.Set("visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "floor dressing hidden" }, { (StringName)"apply", Callable.From(() =>
{
    if (floor_dressing != null)
    {
        floor_dressing.Set("visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "biome dressing hidden" }, { (StringName)"apply", Callable.From(() =>
{
    if (biome_dressing != null)
    {
        biome_dressing.Set("visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "scanned hidden" }, { (StringName)"apply", Callable.From(() =>
{
    Node scanned = camp.GetNodeOrNull("ScannedDressing");
    if (scanned != null)
    {
        scanned.Set("visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "drips hidden" }, { (StringName)"apply", Callable.From(() =>
{
    if (drips != null)
    {
        drips.Set("visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "ridges hidden" }, { (StringName)"apply", Callable.From(() =>
{
    camp.ridge_forest.Visible = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "forest hidden" }, { (StringName)"apply", Callable.From(() =>
{
    camp.forest.Visible = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "near trees hidden" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (Node n in camp.forest.GetChildren())
    {
        if (n is GeometryInstance3D && !((string)((GeometryInstance3D)n).Name).StartsWith("FarTrees_", StringComparison.Ordinal))
        {
            ((GeometryInstance3D)n).Visible = false;
        }
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "far trees hidden" }, { (StringName)"apply", Callable.From(() => _profile_hide(camp.forest, new Godot.Collections.Array { "FarTrees_" })) } }, new Godot.Collections.Dictionary { { (StringName)"name", "veg shadows off" }, { (StringName)"apply", Callable.From(() =>
{
    _profile_no_shadows(camp.understory);
    if (floor_dressing != null)
    {
        _profile_no_shadows(floor_dressing);
    }
    if (biome_dressing != null)
    {
        _profile_no_shadows(biome_dressing);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "forest shadows off" }, { (StringName)"apply", Callable.From(() => _profile_no_shadows(camp.forest)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "far tree shadows off" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (Node n2 in camp.forest.GetChildren())
    {
        if (n2 is GeometryInstance3D && ((string)((GeometryInstance3D)n2).Name).StartsWith("FarTrees_", StringComparison.Ordinal))
        {
            ((GeometryInstance3D)n2).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire fx hidden" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (Variant n3 in new Godot.Collections.Array { camp.campsite.firepit.flame_volume, camp.campsite.firepit.sparks, camp.campsite.firepit.smoke, camp.campsite.firepit.haze })
    {
        G.SetIndex(n3, "visible", false);
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire shadow parab" }, { (StringName)"apply", Callable.From(() =>
{
    camp.campsite.firepit.light.OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fire casters no grass" }, { (StringName)"apply", Callable.From(() =>
{
    camp.campsite.firepit.light.ShadowCasterMask = unchecked((uint)(0xFFFFF & ~Pond.GRASS_LAYER));
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "sun casters no grass" }, { (StringName)"apply", Callable.From(() =>
{
    world.sun.ShadowCasterMask = unchecked((uint)(0xFFFFF & ~Pond.GRASS_LAYER));
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "refl lod 8px" }, { (StringName)"apply", Callable.From(() =>
{
    camp.pond.reflection_viewport.MeshLodThreshold = 8.0f;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "refl far 250" }, { (StringName)"apply", Callable.From(() =>
{
    camp.pond.reflection_camera.Far = 250.0f;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "leaves hidden" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (Node n4 in camp.forest.GetChildren())
    {
        if (n4 is GeometryInstance3D && ((string)((GeometryInstance3D)n4).Name).EndsWith("_leaves", StringComparison.Ordinal) || (string)n4.Name == "FarTrees_leaves")
        {
            n4.Set("visible", false);
        }
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "bark hidden" }, { (StringName)"apply", Callable.From(() =>
{
    foreach (Node n5 in camp.forest.GetChildren())
    {
        if (n5 is GeometryInstance3D && ((string)((GeometryInstance3D)n5).Name).EndsWith("_bark", StringComparison.Ordinal) || (string)n5.Name == "FarTrees_bark")
        {
            n5.Set("visible", false);
        }
    }
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "terrain hidden" }, { (StringName)"apply", Callable.From(() =>
{
    camp.terrain.Visible = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "overdraw shot" }, { (StringName)"apply", Callable.From(() =>
{
    GetViewport().DebugDraw = Viewport.DebugDrawEnum.Overdraw;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "impostors off" }, { (StringName)"apply", Callable.From(() => camp.forest.set_switch_distance(100000.0)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "impostors at 30 m" }, { (StringName)"apply", Callable.From(() => camp.forest.set_switch_distance(30.0)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "leaf lod old" }, { (StringName)"apply", Callable.From(() => camp.forest.set_leaf_lod(new Godot.Collections.Array { 45.0, 120.0, 0.48, 0.12 }, new Godot.Collections.Array { 45.0, 120.0, 0.48, 0.12 }, Quality.Instance.current.foliage_distance)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "leaf lod strong" }, { (StringName)"apply", Callable.From(() => camp.forest.set_leaf_lod(new Godot.Collections.Array { 24.0, 80.0, 0.78, 0.6 }, new Godot.Collections.Array { 30.0, 90.0, 0.88, 1.0 }, Quality.Instance.current.foliage_distance)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "grass lod bias 0.5" }, { (StringName)"apply", Callable.From(() => _profile_lod_bias(camp.understory, new Godot.Collections.Array { "Grass_", "HillGrass_", "MeadowTussocks_" }, 0.5)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "grass lod bias 0.25" }, { (StringName)"apply", Callable.From(() => _profile_lod_bias(camp.understory, new Godot.Collections.Array { "Grass_", "HillGrass_", "MeadowTussocks_" }, 0.25)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "tree lod bias 0.5" }, { (StringName)"apply", Callable.From(() => _profile_lod_bias(camp.forest, new Godot.Collections.Array { "" }, 0.5)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "tree lod bias 0.25" }, { (StringName)"apply", Callable.From(() => _profile_lod_bias(camp.forest, new Godot.Collections.Array { "" }, 0.25)) } }, new Godot.Collections.Dictionary { { (StringName)"name", "taa off" }, { (StringName)"apply", Callable.From(() =>
{
    GetViewport().UseTaa = false;
}) } }, new Godot.Collections.Dictionary { { (StringName)"name", "fsr2 77%" }, { (StringName)"apply", Callable.From(() =>
{
    GetViewport().Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr2;
    GetViewport().Scaling3DScale = 0.77f;
}) } } };
        Godot.Collections.Dictionary report = new Godot.Collections.Dictionary();
        List<string> wanted = G.split(Game.Instance.arg_value("profile-cases", ""), ",", false);
        List<string> views = G.split(Game.Instance.arg_value("profile-views", "fire,pond"), ",", false);
        foreach (string vp_name in views)
        {
            Godot.Collections.Array<Godot.Collections.Dictionary> matches = G.filter(VIEWPOINTS, (Godot.Collections.Dictionary v) => G.eq(v["name"], vp_name));
            if ((matches.Count == 0))
            {
                G.push_warning(G.format("Unknown profile viewpoint %s", vp_name));
                continue;
            }
            Godot.Collections.Dictionary vp = matches[0];
            camera.GlobalPosition = ground_relative(vp["pos"].AsVector3());
            camera.LookAt(ground_relative(vp["look"].AsVector3()), Vector3.Up);
            G.print(G.format("PROFILE viewpoint %s", vp_name));
            Godot.Collections.Dictionary rows = new Godot.Collections.Dictionary();
            foreach (Godot.Collections.Dictionary c in cases)
            {
                if (!(wanted.Count == 0) && !wanted.Contains(c["name"].AsString()))
                {
                    continue;
                }
                Callable apply = c["apply"].AsCallable();
                apply.Call();
                await _wait(1.2);
                double gpu = 0.0;
                double cpu = 0.0;
                double frame = 0.0;
                long samples = 45;
                for (long i = 0; i < samples; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
                    cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
                    frame += GetProcessDeltaTime() * 1000.0;
                }
                gpu /= (double)samples;
                cpu /= (double)samples;
                frame /= (double)samples;
                if (G.eq(c["name"], "overdraw shot"))
                {
                    GetViewport().GetTexture().GetImage().SavePng(out_path.GetBaseDir().PathJoin(G.format("overdraw_%s.png", vp_name)));
                }
                long objects = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
                long primitives = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                long draw_calls = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                rows[c["name"]] = new Godot.Collections.Dictionary { { (StringName)"gpu_ms", snappedf(gpu, 0.01) }, { (StringName)"cpu_ms", snappedf(cpu, 0.01) }, { (StringName)"frame_ms", snappedf(frame, 0.01) }, { (StringName)"objects", objects }, { (StringName)"primitives", primitives }, { (StringName)"draw_calls", draw_calls } };
                G.print(G.format("PROFILE %-22s gpu %6.2f ms   cpu %6.2f ms   frame %6.2f ms   objects %6d   tris %9d   draws %5d", new Godot.Collections.Array { c["name"], gpu, cpu, frame, objects, primitives, draw_calls }));
                _restore_profile_state();
                await _wait(0.4);
            }
            report[vp_name] = rows;
        }
        FileAccess file = FileAccess.Open(out_path, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(Json.Stringify(report, "  "));
        }
        G.print("PROFILE_DONE");
        Game.Instance.quit_cleanly();
    }

    public Godot.Collections.Dictionary _profile_state = new Godot.Collections.Dictionary();
    public double _profile_refl_lod = 1.0;
    public double _profile_refl_far = 700.0;
    public long _profile_fire_casters = 0xFFFFF;
    public long _profile_sun_casters = 0xFFFFF;

    public void _profile_snapshot(Node root)
    {
        /// Remember visibility and shadow casting of every geometry node so each
        /// profile case starts from the same scene.
        _profile_state.Clear();
        Godot.Collections.Array<Node> stack = new Godot.Collections.Array<Node> { root };
        while (!(stack.Count == 0))
        {
            Node n = G.pop_back(stack);
            if (n is Node3D)
            {
                Godot.Collections.Dictionary entry = new Godot.Collections.Dictionary { { (StringName)"visible", ((Node3D)n).Visible } };
                if (((Node3D)n) is GeometryInstance3D)
                {
                    entry["cast_shadow"] = (long)((GeometryInstance3D)((Node3D)n)).CastShadow;
                    entry["lod_bias"] = ((GeometryInstance3D)((Node3D)n)).LodBias;
                }
                _profile_state[((Node3D)n)] = entry;
            }
            foreach (Node c in n.GetChildren())
            {
                stack.Add(c);
            }
        }
    }

    public void _profile_hide(Node parent, Godot.Collections.Array prefixes)
    {
        foreach (Node n in parent.GetChildren())
        {
            if (!(n is Node3D))
            {
                continue;
            }
            foreach (Variant prefix_item in prefixes)
            {
                string prefix = prefix_item.AsString();
                if (((string)n.Name).StartsWith(prefix, StringComparison.Ordinal))
                {
                    (n as Node3D).Visible = false;
                    break;
                }
            }
        }
    }

    public void _profile_lod_bias(Node parent, Godot.Collections.Array prefixes, double bias)
    {
        foreach (Node n in parent.GetChildren())
        {
            if (!(n is GeometryInstance3D))
            {
                continue;
            }
            foreach (Variant prefix_item in prefixes)
            {
                string prefix = prefix_item.AsString();
                if (((string)n.Name).StartsWith(prefix, StringComparison.Ordinal))
                {
                    (n as GeometryInstance3D).LodBias = (float)bias;
                    break;
                }
            }
        }
    }

    public void _profile_no_shadows(Node root)
    {
        Godot.Collections.Array<Node> stack = new Godot.Collections.Array<Node> { root };
        while (!(stack.Count == 0))
        {
            Node n = G.pop_back(stack);
            if (n is GeometryInstance3D)
            {
                ((GeometryInstance3D)n).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
            foreach (Node c in n.GetChildren())
            {
                stack.Add(c);
            }
        }
    }

    public void _restore_profile_state()
    {
        foreach (Variant n_key in _profile_state.Keys)
        {
            Node n = n_key.As<Node>();
            if (!GodotObject.IsInstanceValid(n))
            {
                continue;
            }
            Godot.Collections.Dictionary entry = _profile_state[n].AsGodotDictionary();
            (n as Node3D).Visible = entry["visible"].AsBool();
            if (entry.ContainsKey("cast_shadow"))
            {
                (n as GeometryInstance3D).CastShadow = (GeometryInstance3D.ShadowCastingSetting)entry["cast_shadow"].AsInt64();
                (n as GeometryInstance3D).LodBias = entry["lod_bias"].AsSingle();
            }
        }
        Camp camp = Game.Instance.camp;
        if (camp.campsite != null && camp.campsite.firepit != null)
        {
            camp.campsite.firepit.light.ShadowEnabled = true;
            camp.campsite.firepit.light.OmniShadowMode = OmniLight3D.ShadowMode.Cube;
            foreach (CampLantern l in camp.campsite.lanterns)
            {
                l.light.ShadowEnabled = Quality.Instance.current.lantern_shadows;
            }
        }
        camp.pond.planar_enabled = Quality.Instance.current.planar_reflections;
        camp.forest.set_switch_distance(-1.0);
        camp.pond.reflection_viewport.MeshLodThreshold = (float)_profile_refl_lod;
        camp.pond.reflection_camera.Far = (float)_profile_refl_far;
        camp.campsite.firepit.light.ShadowCasterMask = unchecked((uint)(_profile_fire_casters));
        Game.Instance.world.sun.ShadowEnabled = true;
        Game.Instance.world.sun.ShadowCasterMask = unchecked((uint)(_profile_sun_casters));
        GetViewport().DebugDraw = Viewport.DebugDrawEnum.Disabled;
        Quality.Instance.apply(Quality.Instance.current.tier);
    }

    public static Vector3 ground_relative(Vector3 p)
    {
        /// Viewpoint heights are authored relative to the ground under them.
        double ground = Game.Instance.camp.height_at(p.X, p.Z);
        double water = TerrainField.WATER_LEVEL;
        return new Vector3(p.X, (float)(maxf(ground, water) + p.Y), p.Z);
    }

    public static Godot.Collections.Dictionary summarise(List<float> frame_ms)
    {
        /// Frame-time statistics in milliseconds: mean, median, 1% worst and derived fps.
        if ((frame_ms.Count == 0))
        {
            return new Godot.Collections.Dictionary { { (StringName)"frames", 0 } };
        }
        List<float> sorted = new List<float>(frame_ms);
        G.sort(sorted);
        double total = 0.0;
        foreach (float f in sorted)
        {
            total += f;
        }
        double mean = total / (double)(long)sorted.Count;
        double p50 = sorted[(int)(long)((double)(long)sorted.Count * 0.5)];
        double p99 = sorted[(int)mini((long)((double)(long)sorted.Count * 0.99), (long)sorted.Count - 1)];
        return new Godot.Collections.Dictionary { { (StringName)"frames", (long)sorted.Count }, { (StringName)"mean_ms", snappedf(mean, 0.01) }, { (StringName)"median_ms", snappedf(p50, 0.01) }, { (StringName)"p99_ms", snappedf(p99, 0.01) }, { (StringName)"fps", snappedf(1000.0 / maxf(mean, 1e-3), 0.1) }, { (StringName)"low_1pct_fps", snappedf(1000.0 / maxf(p99, 1e-3), 0.1) } };
    }

    public async Task _wait(double seconds, long minimum_frames = 90)
    {
        /// Waits for the time and for enough frames: a hidden window can run at one
        /// frame per second, and GI and the temporal volumetrics settle per frame.
        double t = 0.0;
        long frames = 0;
        while (t < seconds || frames < minimum_frames)
        {
            t += GetProcessDeltaTime();
            frames += 1;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
