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

/// Diegetic-light overlay: title card, control hints, interaction prompt,
/// quality panel, performance stats, pause menu and cinematic letterbox.
public partial class Hud : CanvasLayer
{
    public const string HINTS = "WASD move    SHIFT run    C crouch    E interact    L hand lantern    P photo mode    T day cycle    [ ] time    1-4 quality    F3 stats    ESC menu";

    public Control _root;
    public Label _title;
    public Label _hints;
    /// The title card plays once, when play first begins; leaving photo mode or
    /// the menu later must not bring it back over the game.
    public bool title_played = false;
    public Label _prompt;
    public Label _clock;
    public PanelContainer _quality_panel;
    public Label _quality_label;
    public Label _stats;
    public PanelContainer _stats_panel;
    public PanelContainer _menu;
    public Godot.Collections.Array<ColorRect> _bars = new Godot.Collections.Array<ColorRect>();
    public Label _photo_label;

    public double _clock_fade = 0.0;
    public bool _stats_visible = false;
    public double _prev_hour = -1.0;

    public override void _Ready()
    {
        Layer = 15;
        ProcessMode = Node.ProcessModeEnum.Always;
        Game.Instance.hud = this;
        _build();
        Game.Instance.Connect(Game.SignalName.mode_changed, new Callable(this, Hud.MethodName._on_mode_changed));
        Game.Instance.Connect(Game.SignalName.hud_visibility_changed, Callable.From((bool v) =>
{
    _root.Visible = v;
}));
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Hud.MethodName._on_quality_changed));
        _on_quality_changed(Quality.Instance.current);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
    }

    public void _build()
    {
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);

        _title = UiTheme.label("THE LAST CAMP", 52, UiTheme.ACCENT);
        _title.AddThemeConstantOverride("outline_size", 0);
        _place(_title, new Vector2(0.5f, 0.5f), new Vector2(-400, -190), new Vector2(400, -110));
        Color _t1 = _title.Modulate;
        _t1.A = 0.0f;
        _title.Modulate = _t1;
        _root.AddChild(_title);

        _hints = UiTheme.label(HINTS, 15, UiTheme.MUTED);
        _place(_hints, new Vector2(0.5f, 1.0f), new Vector2(-700, -46), new Vector2(700, -20));
        Color _t2 = _hints.Modulate;
        _t2.A = 0.0f;
        _hints.Modulate = _t2;
        _root.AddChild(_hints);

        _prompt = UiTheme.label("", 20, UiTheme.TEXT);
        _place(_prompt, new Vector2(0.5f, 1.0f), new Vector2(-300, -120), new Vector2(300, -80));
        _root.AddChild(_prompt);

        _clock = UiTheme.label("", 22, UiTheme.ACCENT);
        _place(_clock, new Vector2(0.5f, 0.0f), new Vector2(-120, 28), new Vector2(120, 60));
        Color _t3 = _clock.Modulate;
        _t3.A = 0.0f;
        _clock.Modulate = _t3;
        _root.AddChild(_clock);

        _photo_label = UiTheme.label("PHOTO MODE      WASD/Space/C fly    wheel speed    F12 save    P exit", 16, UiTheme.ACCENT);
        _place(_photo_label, new Vector2(0.5f, 1.0f), new Vector2(-500, -40), new Vector2(500, -14));
        _photo_label.Visible = false;
        _root.AddChild(_photo_label);

        for (long i = 0; i < 2; i++)
        {
            ColorRect bar = new ColorRect();
            bar.Color = Colors.Black;
            bar.MouseFilter = Control.MouseFilterEnum.Ignore;
            bar.Visible = false;
            _root.AddChild(bar);
            _bars.Add(bar);
        }
        _bars[0].SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _bars[0].OffsetBottom = 0.0f;
        _bars[1].SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _bars[1].OffsetTop = 0.0f;

        _quality_panel = UiTheme.panel(new Vector2(360, 0));
        _place(_quality_panel, new Vector2(1.0f, 0.0f), new Vector2(-400, 32), new Vector2(-32, 32));
        _quality_panel.Visible = false;
        VBoxContainer qbox = new VBoxContainer();
        qbox.AddThemeConstantOverride("separation", 8);
        _quality_panel.AddChild(qbox);
        qbox.AddChild(UiTheme.label("QUALITY", 20, UiTheme.ACCENT));
        _quality_label = UiTheme.label("", 16, UiTheme.TEXT);
        qbox.AddChild(_quality_label);
        qbox.AddChild(UiTheme.label("1 Low    2 Medium    3 High    4 Ultra", 14, UiTheme.MUTED));
        _root.AddChild(_quality_panel);

        _stats_panel = UiTheme.panel(new Vector2(300, 0));
        _place(_stats_panel, new Vector2(0.0f, 0.0f), new Vector2(32, 32), new Vector2(340, 32));
        _stats_panel.Visible = false;
        _stats = UiTheme.label("", 14, UiTheme.TEXT, HorizontalAlignment.Left);
        _stats_panel.AddChild(_stats);
        _root.AddChild(_stats_panel);

        _menu = UiTheme.panel(new Vector2(540, 0));
        _place(_menu, new Vector2(0.5f, 0.5f), new Vector2(-270, -190), new Vector2(270, 190));
        _menu.Visible = false;
        VBoxContainer mbox = new VBoxContainer();
        mbox.AddThemeConstantOverride("separation", 12);
        _menu.AddChild(mbox);
        mbox.AddChild(UiTheme.label("PAUSED", 30, UiTheme.ACCENT));
        mbox.AddChild(UiTheme.label("ESC resume        Q quit        F11 fullscreen", 16, UiTheme.TEXT));
        Label controls = UiTheme.label("WASD  walk        SHIFT  run        C  crouch\n" + "E / click  take a log, feed the fire, light a lantern\n" + "L  hand lantern        P  photo mode (F12 saves a shot)\n" + "T  run the day cycle        [ ]  scrub time\n" + "TAB  quality panel        1-4  presets        F3  stats        H  hide HUD", 15, UiTheme.MUTED);
        mbox.AddChild(controls);
        _root.AddChild(_menu);
    }

    public static void _place(Control c, Vector2 anchor, Vector2 top_left, Vector2 bottom_right)
    {
        c.AnchorLeft = anchor.X;
        c.AnchorRight = anchor.X;
        c.AnchorTop = anchor.Y;
        c.AnchorBottom = anchor.Y;
        c.OffsetLeft = top_left.X;
        c.OffsetTop = top_left.Y;
        c.OffsetRight = bottom_right.X;
        c.OffsetBottom = bottom_right.Y;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("quality_menu"))
        {
            _quality_panel.Visible = !_quality_panel.Visible;
        }
        else if (@event.IsActionPressed("toggle_stats"))
        {
            _stats_visible = !_stats_visible;
            _stats_panel.Visible = _stats_visible;
        }
        else if (@event.IsActionPressed("pause"))
        {
            _toggle_pause();
        }
        else if (Game.Instance.mode == Game.Mode.PAUSED && @event is InputEventKey && ((InputEventKey)@event).IsPressed())
        {
            InputEventKey key = ((InputEventKey)@event);
            if (key.Keycode == Key.Q)
            {
                GetTree().Quit();
            }
        }
    }

    public void _toggle_pause()
    {
        if (Game.Instance.mode == Game.Mode.PAUSED)
        {
            GetTree().Paused = false;
            Game.Instance.mode = Game.Mode.PLAY;
            if (Game.Instance.player != null)
            {
                Game.Instance.player.begin();
            }
        }
        else if (Game.Instance.mode == Game.Mode.PLAY)
        {
            GetTree().Paused = true;
            Game.Instance.mode = Game.Mode.PAUSED;
        }
        _menu.Visible = Game.Instance.mode == Game.Mode.PAUSED;
    }

    public override void _Process(double delta)
    {
        if (_stats_visible)
        {
            _update_stats();
        }
        if (Game.Instance.world != null)
        {
            double hour = Game.Instance.world.hour;
            if (_prev_hour >= 0.0 && absf(hour - _prev_hour) > 0.0005)
            {
                _clock_fade = 2.2;
                _clock.Text = _format_hour(hour);
            }
            _prev_hour = hour;
        }
        _clock_fade = maxf(_clock_fade - delta, 0.0);
        Color _t1 = _clock.Modulate;
        _t1.A = (float)clampf(_clock_fade, 0.0, 1.0);
        _clock.Modulate = _t1;
        if (Game.Instance.player != null)
        {
            Node focus = Game.Instance.player.focus();
            if (focus != null && focus.HasMethod("prompt") && Game.Instance.mode == Game.Mode.PLAY)
            {
                _prompt.Text = "[E]  " + G.str(G.str(focus.Call("prompt")));
            }
            else if (Game.Instance.player.held_item == "log" && Game.Instance.mode == Game.Mode.PLAY)
            {
                _prompt.Text = "Carrying a log";
            }
            else
            {
                _prompt.Text = "";
            }
        }
        _update_letterbox(delta);
    }

    public void _update_letterbox(double delta)
    {
        double target = 0.0;
        if (Game.Instance.mode == Game.Mode.INTRO)
        {
            target = 0.09;
        }
        else if (Game.Instance.mode == Game.Mode.PHOTO)
        {
            target = 0.07;
        }
        double viewport_h = _root.Size.Y;
        double current = _bars[0].OffsetBottom / maxf(viewport_h, 1.0);
        double next = lerpf(current, target, 1.0 - exp(-delta * 3.0));
        double px = next * viewport_h;
        _bars[0].Visible = px > 0.5;
        _bars[1].Visible = px > 0.5;
        _bars[0].OffsetBottom = (float)px;
        _bars[1].OffsetTop = (float)-px;
    }

    public static string _format_hour(double hour)
    {
        long h = (long)floor(hour);
        long m = (long)floor((hour - (double)h) * 60.0);
        return G.format("%02d:%02d", new Godot.Collections.Array { h, m });
    }

    public void _update_stats()
    {
        Rid rid = GetViewport().GetViewportRid();
        double gpu_ms = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        double cpu_ms = RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
        double frame_ms = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);
        Vector2 size = GetViewport().GetVisibleRect().Size * (float)Quality.Instance.current.render_scale;
        _stats.Text = G.format("%d fps   %.2f ms frame\nGPU %.2f ms   CPU %.2f ms\n%d draw calls   %.2fM triangles\nVRAM %.0f MB\n%s   %dx%d %s\n%s", new Godot.Collections.Array { Engine.GetFramesPerSecond(), frame_ms, gpu_ms, cpu_ms, (long)draws, prims / 1e6, vram, Quality.Instance.current.display_name, (long)size.X, (long)size.Y, Quality.Instance.current.upscaler == Viewport.Scaling3DModeEnum.Fsr2 ? "FSR2" : "TAA", Game.Instance.world != null ? _format_hour(Game.Instance.world.hour) : "" });
    }

    public void _on_mode_changed(Game.Mode mode)
    {
        _photo_label.Visible = mode == Game.Mode.PHOTO;
        _menu.Visible = mode == Game.Mode.PAUSED;
        switch (mode)
        {
            case Game.Mode.PLAY:
                if (!title_played)
                {
                    _play_title();
                }
                break;
            case Game.Mode.LOADING:
            case Game.Mode.INTRO:
            case Game.Mode.PHOTO:
            case Game.Mode.PAUSED:
                break;
            default:
                G.assert(false, G.format("Unhandled mode %s", (long)mode));
                break;
        }
    }

    public void _play_title()
    {
        title_played = true;
        Tween t = CreateTween();
        t.TweenProperty(_title, "modulate:a", 1.0, 1.8).SetTrans(Tween.TransitionType.Sine);
        t.TweenInterval(3.0);
        t.TweenProperty(_title, "modulate:a", 0.0, 2.0).SetTrans(Tween.TransitionType.Sine);
        Tween t2 = CreateTween();
        t2.TweenInterval(1.0);
        t2.TweenProperty(_hints, "modulate:a", 1.0, 1.5);
        t2.TweenInterval(14.0);
        t2.TweenProperty(_hints, "modulate:a", 0.0, 2.5);
    }

    public void _on_quality_changed(QualityPreset p)
    {
        _quality_label.Text = G.format("%s  -  %d%% render scale", new Godot.Collections.Array { p.display_name, (long)round(p.render_scale * 100.0) });
    }
    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.hud, this)) Game.Instance.hud = null;
    }

}
