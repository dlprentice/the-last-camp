#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GdRuntime;

namespace LastCamp;

/// The first screen. For a player it is the title: the lettering and the menu over
/// ink while the world builds, then over a slow live view of the camp at dusk. Tool
/// runs (`TitleBypassFlags`) see only a plain cover with the build's progress, which
/// fades out when the world is ready, exactly as before the title existed.
public partial class LoadingScreen : CanvasLayer
{
    /// Command-line flags that start straight into the world, without the title.
    public static readonly string[] TitleBypassFlags =
    {
        "session-check", "benchmark", "profile", "traverse", "cinematic", "capture", "review-route",
        "review-assets", "package-check", "check-pond", "scene-smoke",
    };

    /// True when a run with these parsed arguments shows the title.
    public static bool ShowsTitle(Godot.Collections.Dictionary args) => !TitleBypassFlags.Any(f => args.ContainsKey(f));

    /// The player chose Continue (false) or New Game (true) and the world is ready.
    public event Action<bool> PlayRequested;

    public Control _root;
    public ColorRect _backdrop;
    public Label _title;
    public Label _stage;
    public ColorRect _bar;
    public TitleScreen _menu;
    public bool TitleMode { get; private set; }
    public bool WorldReady { get; private set; }

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = Node.ProcessModeEnum.Always;
        TitleMode = Game.Instance != null && ShowsTitle(Game.Instance.user_args);
        _root = UiTheme.Root("LoadingRoot");
        AddChild(_root);
        _backdrop = new ColorRect { Name = "Backdrop", Color = UiTheme.INK, MouseFilter = Control.MouseFilterEnum.Ignore };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_backdrop);
        if (TitleMode)
        {
            GameSettings.Current = GameSettings.Load();
            GameSettings.Current.Apply();
            _menu = new TitleScreen(this);
            _root.AddChild(_menu);
            _stage = _menu.Status;
            if (Game.Instance.has_flag("ui-shots")) UiShots.ShotAfter(this, "01_title_building", 4.0);
            return;
        }
        // The tool runs' cover: the name, the stage and a thin paper rule for progress.
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 18);
        _backdrop.AddChild(box);
        _title = UiTheme.label("The Last Camp", UiTheme.TITLE, UiTheme.PAPER);
        box.AddChild(_title);
        var track = new Control { CustomMinimumSize = new Vector2(520, 2), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddChild(new ColorRect { Color = new Color(UiTheme.PAPER, 0.15f), Size = new Vector2(520, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
        _bar = new ColorRect { Color = new Color(UiTheme.PAPER, 0.8f), Size = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddChild(_bar);
        box.AddChild(track);
        _stage = UiTheme.label("Preparing", UiTheme.SMALL, new Color(UiTheme.PAPER, 0.7f));
        box.AddChild(_stage);
    }

    public void set_progress(string stage_name, double fraction)
    {
        if (TitleMode) _menu.SetProgress(stage_name);
        else
        {
            _stage.Text = stage_name;
            _bar.Size = new Vector2(520f * (float)Math.Clamp(fraction, 0.0, 1.0), 2);
        }
    }

    /// Tool runs: the world is ready, the cover fades away.
    public void finish()
    {
        if (TitleMode)
        {
            world_ready();
            return;
        }
        _bar.Size = new Vector2(520, 2);
        _stage.Text = "Ready";
        Tween tween = CreateTween();
        tween.TweenInterval(0.25);
        tween.TweenProperty(_backdrop, "modulate:a", 0.0, 1.2).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(new Callable(this, Node.MethodName.QueueFree));
    }

    /// The title: the world is built, so the live camp fades in behind the menu.
    public void world_ready()
    {
        WorldReady = true;
        _menu.StartLiveView();
        UiTheme.Fade(_backdrop, 0.0, 1.6);
        _menu.SetReady();
    }

    /// Called by the menu: fade to black, hand the world to play, then fade in.
    public async void Begin(bool newGame)
    {
        _backdrop.Color = Colors.Black;
        Tween dark = UiTheme.Fade(_backdrop, 1.0, 0.4);
        UiTheme.Fade(_menu, 0.0, 0.4);
        await ToSignal(dark, Tween.SignalName.Finished);
        _menu.StopLiveView();
        PlayRequested?.Invoke(newGame);
        await ToSignal(GetTree().CreateTimer(0.3, true), SceneTreeTimer.SignalName.Timeout);
        if (newGame) Game.Instance?.hud?.ShowChapterCard(0, Story.StoryScript.Chapters[0].Title, true);
        Tween light = UiTheme.Fade(_backdrop, 0.0, 1.0);
        await ToSignal(light, Tween.SignalName.Finished);
        QueueFree();
    }

    public void show_failure()
    {
        _stage.Text = "The campsite could not load. Details are in the game log.";
        _stage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _stage.CustomMinimumSize = new Vector2(600, 0);
        if (_bar != null) _bar.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _menu?.ShowFailure();
        var close = new KitButton("Close the game") { Alignment = HorizontalAlignment.Center };
        close.AddThemeColorOverride("font_color", UiTheme.PAPER);
        close.AddThemeColorOverride("font_focus_color", UiTheme.PAPER);
        close.AddThemeColorOverride("font_hover_color", UiTheme.PAPER);
        close.Pressed += () => Game.Instance.quit_cleanly(1);
        _stage.GetParent().AddChild(close);
        close.GrabFocus();
    }

    private static bool _returning;

    /// "Save and quit to title" and the end of the credits: the scene is unloaded
    /// the way a normal quit unloads it, then built again, which opens on the title.
    public static async void ReturnToTitle(SceneTree tree)
    {
        if (_returning || Game.Instance == null) return;
        _returning = true;
        try
        {
            tree.Paused = true;
            Game.Instance._stop_audio(tree.Root);
            await Game.Instance.ToSignal(tree.CreateTimer(0.25, true, false, true), SceneTreeTimer.SignalName.Timeout);
            tree.Paused = false;
            Game.Instance.mode = Game.Mode.LOADING;
            tree.UnloadCurrentScene();
            G.drain_finalizers();
            RenderingServer.ForceSync();
            await Game.Instance.ToSignal(tree.CreateTimer(0.25, true, false, true), SceneTreeTimer.SignalName.Timeout);
            GD.Print("TITLE_RETURN");
            tree.ChangeSceneToFile(ProjectSettings.GetSetting("application/run/main_scene").AsString());
        }
        finally
        {
            _returning = false;
        }
    }
}
