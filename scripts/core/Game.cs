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

/// Global session state and cross-system references.
///
/// Systems register themselves here when they enter the tree so that loosely
/// coupled parts (HUD, player, world, audio) can find each other without deep
/// node paths. Nothing here does rendering or simulation work itself.
public partial class Game : Node
{
    public static Game Instance { get; private set; }
    [Signal]
    public delegate void mode_changedEventHandler(Game.Mode mode);
    [Signal]
    public delegate void hud_visibility_changedEventHandler(bool visible);

    public enum Mode
    {
        LOADING,
        INTRO,
        PLAY,
        PHOTO,
        PAUSED,
    }

    public const string SCREENSHOT_DIR = "user://screenshots";

    private Game.Mode _mode_value = Game.Mode.LOADING;
    public Game.Mode mode
    {
        get => _mode_value;
        set
        {
            if (value == _mode_value)
            {
                return;
            }
            _mode_value = value;
            _apply_mouse_mode();
            EmitSignal(SignalName.mode_changed, (long)_mode_value);
        }
    }

    private bool _hud_visible_value = true;
    public bool hud_visible
    {
        get => _hud_visible_value;
        set
        {
            _hud_visible_value = value;
            EmitSignal(SignalName.hud_visibility_changed, value);
        }
    }

    public Player player;
    public WorldController world;
    public Camp camp;
    public AudioDirector audio;
    public Hud hud;
    public Camera3D photo_camera;

    /// Parsed `--key=value` / `--flag` arguments passed after `--` on the command line.
    public Godot.Collections.Dictionary user_args = new Godot.Collections.Dictionary();
    public bool _quitting = false;

    public async void quit_cleanly(long code = 0)
    {
        /// Imported Ogg/WAV playbacks release their decoder data on the audio mixer
        /// thread. Let it finish after stopping players, before shutting down Godot.
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        GetTree().Paused = true;
        _stop_audio(GetTree().Root);
        await ToSignal(GetTree().CreateTimer(0.25, true, false, true), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit((int)code);
    }

    public void _stop_audio(Node node)
    {
        if (node is AudioStreamPlayer || node is AudioStreamPlayer3D || node is AudioStreamPlayer2D)
        {
            node.Call("stop");
        }
        foreach (Node child in node.GetChildren())
        {
            _stop_audio(child);
        }
    }

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;
        user_args = parse_user_args(new List<string>(OS.GetCmdlineUserArgs()));
        _apply_movie_size();
        _apply_mouse_mode();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_fullscreen"))
        {
            toggle_fullscreen();
        }
        else if (@event.IsActionPressed("screenshot"))
        {
            save_screenshot();
        }
        else if (@event.IsActionPressed("toggle_hud"))
        {
            hud_visible = !hud_visible;
        }
    }

    public void _apply_movie_size()
    {
        /// `--movie-size=WxH` renders the root viewport at that size and scales it to
        /// the window, so Movie Maker mode writes frames at that resolution whatever
        /// the screen is: 4K captures from a 1080p display. Applied before the first
        /// frame because the movie writer fixes its frame size on frame one.
        string spec = arg_value("movie-size", "");
        if (!spec.Contains("x"))
        {
            return;
        }
        List<string> parts = G.split(spec, "x");
        Vector2I size = new Vector2I((int)G.to_int(parts[0]), (int)G.to_int(parts[1]));
        if (size.X < 16 || size.Y < 16)
        {
            return;
        }
        Window window = GetWindow();
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        window.ContentScaleSize = size;
        G.print(G.format("MOVIE_SIZE %s", size));
    }

    public static Godot.Collections.Dictionary parse_user_args(List<string> args)
    {
        /// Parses argument lists such as ["--benchmark", "--capture=/tmp/out"] into a
        /// dictionary {"benchmark": true, "capture": "/tmp/out"}.
        Godot.Collections.Dictionary @out = new Godot.Collections.Dictionary();
        foreach (string raw in args)
        {
            string arg = raw.StripEdges();
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            arg = G.substr(arg, 2);
            long eq = (long)arg.IndexOf("=", StringComparison.Ordinal);
            if (eq == -1)
            {
                @out[arg] = true;
            }
            else
            {
                @out[G.substr(arg, 0, eq)] = G.substr(arg, eq + 1);
            }
        }
        return @out;
    }

    public bool has_flag(string flag)
    {
        return user_args.ContainsKey(flag);
    }

    public string arg_value(string flag, string @default)
    {
        Variant value = G.get(user_args, flag, @default);
        return value.VariantType != Variant.Type.Bool ? G.str(value) : @default;
    }

    public bool is_headless_capture()
    {
        return has_flag("capture") || has_flag("benchmark");
    }

    public bool is_tool_run()
    {
        /// Any automated run that must finish and quit on its own.
        foreach (Variant flag in new Godot.Collections.Array { "capture", "benchmark", "profile", "traverse", "cinematic", "review-assets", "scene-smoke", "check-pond" })
        {
            if (has_flag(flag.AsString()))
            {
                return true;
            }
        }
        return false;
    }

    public void toggle_fullscreen()
    {
        Window window = GetWindow();
        if (window.Mode == Window.ModeEnum.Fullscreen || window.Mode == Window.ModeEnum.ExclusiveFullscreen)
        {
            window.Mode = Window.ModeEnum.Windowed;
        }
        else
        {
            window.Mode = Window.ModeEnum.Fullscreen;
        }
    }

    public string save_screenshot()
    {
        DirAccess.MakeDirRecursiveAbsolute(SCREENSHOT_DIR);
        Image image = GetViewport().GetTexture().GetImage();
        string stamp = Time.GetDatetimeStringFromSystem(false, true).Replace(":", "-").Replace(" ", "_");
        string path = G.format("%s/last_camp_%s.png", new Godot.Collections.Array { SCREENSHOT_DIR, stamp });
        Error err = image.SavePng(path);
        if (err != Error.Ok)
        {
            G.push_warning(G.format("Screenshot failed: %s", error_string((long)err)));
            return "";
        }
        return ProjectSettings.GlobalizePath(path);
    }

    public void _apply_mouse_mode()
    {
        switch (mode)
        {
            case Game.Mode.LOADING:
            case Game.Mode.PAUSED:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
            case Game.Mode.INTRO:
            case Game.Mode.PLAY:
            case Game.Mode.PHOTO:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            default:
                G.assert(false, G.format("Unhandled mode %s", (long)mode));
                break;
        }
    }

    public Game()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        player = null;
        world = null;
        camp = null;
        audio = null;
        hud = null;
        photo_camera = null;
        if (Instance == this) Instance = null;
        G.drain_finalizers();
    }
}
