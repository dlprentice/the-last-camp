#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using LastCamp.Story;

namespace LastCamp;

/// The play screen, almost empty (docs/direction.md): an interaction dot with one
/// verb, and the chapter cards. Behind Esc, the pause menu and its
/// settings; behind J, the journal. Also the photo-mode line, the cinematic
/// letterbox, the rest fade and the developer quality and frame panels.
public partial class Hud : CanvasLayer
{
    public Control _root;
    public InteractionDot _dot;
    public Label _verb;
    private JournalBook _journal;
    private bool _journalFromPause;
    public bool JournalVisible => _journal.Visible;
    public JournalBook Journal => _journal;
    private ColorRect _restFade;
    public PanelContainer _quality_panel;
    public Label _quality_label;
    public Label _stats;
    public PanelContainer _stats_panel;
    public Control _menu;
    private VBoxContainer _menuButtons;
    private KitButton _resume;
    public SettingsPanel _settings;
    public Godot.Collections.Array<ColorRect> _bars = new Godot.Collections.Array<ColorRect>();
    public Label _photo_label;
    public Control _card;
    private Label _cardNumber, _cardTitle;
    private Tween _cardTween;
    public string CardShown { get; private set; } = "";
    private Control _ending;
    public bool EndingShown => _ending != null;
    private CampSession _attached;

    public bool _stats_visible = false;

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
        _root = UiTheme.Root("HudRoot");
        AddChild(_root);

        _dot = new InteractionDot { Name = "Dot" };
        _place(_dot, new Vector2(0.5f, 0.5f), new Vector2(-14, -14), new Vector2(14, 14));
        _root.AddChild(_dot);
        _verb = UiTheme.label("", UiTheme.BODY, UiTheme.PAPER, HorizontalAlignment.Center, shadow: true);
        _place(_verb, new Vector2(0.5f, 0.5f), new Vector2(-200, 22), new Vector2(200, 56));
        _root.AddChild(_verb);


        _photo_label = UiTheme.label("Photo mode    WASD, Space and C fly    wheel changes speed    F12 saves    P leaves",
            UiTheme.SMALL, UiTheme.PAPER, HorizontalAlignment.Center, shadow: true);
        _place(_photo_label, new Vector2(0.5f, 1.0f), new Vector2(-560, -44), new Vector2(560, -16));
        _photo_label.Visible = false;
        _root.AddChild(_photo_label);

        for (long i = 0; i < 2; i++)
        {
            ColorRect bar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _root.AddChild(bar);
            _bars.Add(bar);
        }
        _bars[0].SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _bars[0].OffsetBottom = 0.0f;
        _bars[1].SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _bars[1].OffsetTop = 0.0f;

        _quality_panel = UiTheme.panel(new Vector2(380, 0));
        _place(_quality_panel, new Vector2(1.0f, 0.0f), new Vector2(-420, 32), new Vector2(-32, 32));
        _quality_panel.Visible = false;
        VBoxContainer qbox = new VBoxContainer();
        _quality_panel.AddChild(qbox);
        qbox.AddChild(UiTheme.label("Quality", UiTheme.HEADING, UiTheme.INK, HorizontalAlignment.Left));
        _quality_label = UiTheme.label("", UiTheme.BODY, UiTheme.INK, HorizontalAlignment.Left);
        qbox.AddChild(_quality_label);
        qbox.AddChild(UiTheme.label("1 Low    2 Medium    3 High    4 Ultra", UiTheme.SMALL, UiTheme.FADED, HorizontalAlignment.Left));
        _root.AddChild(_quality_panel);

        _stats_panel = UiTheme.panel(new Vector2(340, 0));
        _place(_stats_panel, new Vector2(0.0f, 0.0f), new Vector2(32, 32), new Vector2(380, 32));
        _stats_panel.Visible = false;
        _stats = UiTheme.label("", UiTheme.SMALL, UiTheme.INK, HorizontalAlignment.Left);
        _stats_panel.AddChild(_stats);
        _root.AddChild(_stats_panel);

        _build_card();
        _build_menu();

        _journal = new JournalBook { Visible = false };
        _journal.CloseRequested += () => _close_journal(true);
        _root.AddChild(_journal);

        _restFade = new ColorRect { Name = "RestFade", Color = Colors.Black, Modulate = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _restFade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Rest transitions remain visible when the player has hidden the HUD.
        var fadeRoot = UiTheme.Root("FadeRoot");
        fadeRoot.AddChild(_restFade);
        AddChild(fadeRoot);
    }

    private void _build_card()
    {
        _card = new Control { Name = "ChapterCard", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _card.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _card.AddChild(black);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 14);
        _card.AddChild(box);
        _cardNumber = UiTheme.label("", UiTheme.BODY, new Color(UiTheme.PAPER, 0.7f));
        _cardTitle = UiTheme.label("", UiTheme.TITLE, UiTheme.PAPER);
        _cardTitle.AddThemeFontOverride("font", UiTheme.SerifItalic);
        box.AddChild(_cardNumber);
        box.AddChild(_cardTitle);
        _root.AddChild(_card);
    }

    private void _build_menu()
    {
        _menu = new Control { Name = "Pause", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _menu.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(UiTheme.INK, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menu.AddChild(dim);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menu.AddChild(centre);

        var panel = new PanelContainer { Name = "PausePanel", CustomMinimumSize = new Vector2(460, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        _menuButtons = new VBoxContainer();
        _menuButtons.AddThemeConstantOverride("separation", 4);
        panel.AddChild(_menuButtons);
        _menuButtons.AddChild(UiTheme.label("Paused", UiTheme.HEADING, UiTheme.INK, HorizontalAlignment.Left));
        _menuButtons.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
        _resume = _menu_button("Resume", () => { if (Game.Instance.mode == Game.Mode.PAUSED) _toggle_pause(); });
        _menu_button("Journal", () => _open_journal(fromPause: true));
        _menu_button("Settings", _open_settings);
        _menu_button("Return to camp", ReturnToCamp);
        _menu_button("Save and quit to title", () =>
        {
            Game.Instance.session?.Save();
            LoadingScreen.ReturnToTitle(GetTree());
        });
        _menu_button("Quit to desktop", () =>
        {
            if (Game.Instance.session != null) Game.Instance.session.SaveAndQuit();
            else Game.Instance.quit_cleanly();
        });
        centre.AddChild(panel);

        _settings = new SettingsPanel { Visible = false };
        _settings.Closed += () =>
        {
            panel.Visible = true;
            _resume.FocusQuietly();
        };
        centre.AddChild(_settings);
        _root.AddChild(_menu);
    }

    private KitButton _menu_button(string text, Action action)
    {
        var b = new KitButton(text);
        b.Pressed += action;
        _menuButtons.AddChild(b);
        return b;
    }

    public void _open_settings()
    {
        _menuButtons.GetParent<Control>().Visible = false;
        _settings.Open();
    }

    /// "Return to camp": through black to standing by the fire, facing it. The
    /// story is not touched; this is only the way back from anywhere.
    public async void ReturnToCamp()
    {
        Player player = Game.Instance.player;
        if (player == null || Game.Instance.session?.Resting == true) return;
        if (Game.Instance.mode == Game.Mode.PAUSED) _toggle_pause();
        player.suspend();
        await FadeRest(true);
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(player)) return;
        player.return_to_camp();
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        await FadeRest(false);
        if (Game.Instance.mode == Game.Mode.PLAY && GodotObject.IsInstanceValid(player)) player.begin();
    }

    public async Task FadeRest(bool dark)
    {
        Tween tween = CreateTween();
        tween.TweenProperty(_restFade, "modulate:a", dark ? 1.0 : 0.0, 1.0).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(tween, Tween.SignalName.Finished);
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
        if (Game.Instance.session?.Resting == true || _ending != null) return;
        if (@event.IsActionPressed(NativeArgs.Name("camp_journal")) && Game.Instance.session != null
            && (Game.Instance.mode == Game.Mode.PLAY || _journal.Visible))
        {
            if (_journal.Visible) _close_journal(false);
            else _open_journal(fromPause: false);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(NativeArgs.Name("quality_menu")))
        {
            _quality_panel.Visible = !_quality_panel.Visible;
            Game.Instance.audio?.play_interact(Sounds.UiMove);
        }
        else if (@event.IsActionPressed(NativeArgs.Name("toggle_stats")))
        {
            _stats_visible = !_stats_visible;
            _stats_panel.Visible = _stats_visible;
            Game.Instance.audio?.play_interact(Sounds.UiMove);
        }
        else if (@event.IsActionPressed(NativeArgs.Name("pause")))
        {
            bool opening = Game.Instance.mode == Game.Mode.PLAY;
            _toggle_pause();
            Game.Instance.audio?.play_interact(opening ? Sounds.UiMove : Sounds.UiSelect);
            GetViewport().SetInputAsHandled();
        }
    }

    /// Opens the journal over play (pausing it) or over the pause menu.
    public void _open_journal(bool fromPause)
    {
        CampSession session = Game.Instance.session;
        if (session == null || _journal.Visible) return;
        _journalFromPause = fromPause && Game.Instance.mode == Game.Mode.PAUSED;
        if (!_journalFromPause) _toggle_pause();
        if (Game.Instance.mode != Game.Mode.PAUSED) return;
        _menu.Visible = false;
        _journal.Open(session.Story);
        Game.Instance.audio?.play_interact(Sounds.JournalOpen);
    }

    /// Closes the journal: back to the pause menu it came from, or to play.
    public void _close_journal(bool viaCancel)
    {
        if (!_journal.Visible) return;
        _journal.Visible = false;
        Game.Instance.audio?.play_interact(Sounds.JournalClose);
        if (_journalFromPause && Game.Instance.mode == Game.Mode.PAUSED)
        {
            _menu.Visible = true;
            _resume.FocusQuietly();
        }
        else if (Game.Instance.mode == Game.Mode.PAUSED) _toggle_pause();
    }

    public void _toggle_pause()
    {
        _journal.Visible = false;
        _settings.Visible = false;
        _menuButtons.GetParent<Control>().Visible = true;
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
        _show_menu(Game.Instance.mode == Game.Mode.PAUSED);
    }

    private void _show_menu(bool show)
    {
        _menu.Visible = show;
        if (!show) return;
        _menu.Modulate = new Color(1, 1, 1, 0);
        UiTheme.Fade(_menu, 1.0);
        if (_menu.IsInsideTree()) _resume.FocusQuietly();
    }

    /// The verb an interaction prompt offers, as one word.
    public static string Verb(string prompt)
    {
        string first = prompt.Trim().Split(' ', 2)[0].ToLowerInvariant();
        return first switch
        {
            "take" or "pick" => "Take",
            "add" or "feed" or "tend" => "Feed",
            "read" => "Read",
            "sit" => "Sit",
            "lift" => "Lift",
            "rest" or "turn" or "sleep" or "the" => "Rest",
            "light" => "Light",
            "douse" => "Douse",
            "open" => "Open",
            "strike" => "Strike",
            "rake" => "Rake",
            "scatter" => "Scatter",
            "walk" or "leave" => "Leave",
            "" => "",
            _ => char.ToUpperInvariant(first[0]) + first[1..],
        };
    }

    /// What the dot says now: a verb when something can be used, else nothing.
    public static string VerbFor(Player player, Node focus, bool playing)
    {
        if (!playing || player == null) return "";
        if (player.held_item == "stone") return player.charging_stone ? "Release" : "Throw";
        if (focus != null && focus.HasMethod(NativeArgs.Name("prompt"))) return Verb(G.str(focus.Call(NativeArgs.Name("prompt"))));
        return "";
    }

    public override void _Process(double delta)
    {
        bool playing = Game.Instance.mode == Game.Mode.PLAY && _ending == null;
        CampSession session = Game.Instance.session;
        if (!ReferenceEquals(session, _attached)) _attach(session);
        if (!playing && Game.Instance.mode != Game.Mode.PAUSED) _journal.Visible = false;
        Player player = Game.Instance.player;
        string verb = VerbFor(player, player?.focus(), playing);
        _verb.Text = verb;
        _dot.Shown = verb.Length > 0;
        _dot.Charge = player?.charging_stone == true ? player.stone_charge : 0;
        if (_stats_visible)
        {
            _update_stats();
        }
        _update_letterbox(delta);
    }

    private void _attach(CampSession session)
    {
        if (_attached != null && GodotObject.IsInstanceValid(_attached)) _attached.BandStarted -= _on_band_started;
        _attached = session;
        if (session != null) session.BandStarted += _on_band_started;
    }

    private void _on_band_started(BandTransition transition)
    {
        if (transition.ChapterChanged && transition.ToChapter != null)
            ShowChapterCard(StoryScript.ChapterIndex(transition.ToChapter.Id), transition.ToChapter.Title, false);
    }

    /// The chapter's card: its number and name, eased in and out over black, 3 s.
    public void ShowChapterCard(int index, string title, bool sound)
    {
        _cardNumber.Text = JournalContent.ChapterNumber(index);
        _cardTitle.Text = title;
        CardShown = title;
        _card.Visible = true;
        _card.Modulate = new Color(1, 1, 1, 0);
        if (sound) Game.Instance.audio?.play_interact(Sounds.CardIn);
        _cardTween?.Kill();
        _cardTween = CreateTween();
        _cardTween.TweenProperty(_card, "modulate:a", 1.0, 0.4).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _cardTween.TweenInterval(1.8);
        _cardTween.TweenProperty(_card, "modulate:a", 0.0, 0.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _cardTween.TweenCallback(Callable.From(() => _card.Visible = false));
    }

    /// The end of the game, for the story to call once the player has walked out:
    /// the credits roll, then June's postcard (entry 7, voiced), then the title.
    public void ShowEnding(bool fireKept)
    {
        if (_ending != null) return;
        if (Game.Instance.mode == Game.Mode.PAUSED) _toggle_pause();
        Game.Instance.player?.suspend();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var holder = new Control { Name = "Ending", MouseFilter = Control.MouseFilterEnum.Stop };
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ending = holder;
        _root.AddChild(holder);
        _root.Visible = true;
        var roll = new CreditsRoll();
        roll.Finished += () =>
        {
            roll.QueueFree();
            _show_postcard(holder, fireKept);
        };
        holder.AddChild(roll);
    }

    private void _show_postcard(Control holder, bool fireKept)
    {
        var card = new Postcard(fireKept);
        holder.AddChild(card);
        VoicePlayer voice = GetTree().Root.FindChild("Voice", true, false) as VoicePlayer;
        VoicePlayer.SubtitlesHeld = true;
        void heard(string line)
        {
            if (line != "e7") return;
            card.Heard();
        }
        if (voice != null) voice.LineHeard += heard;
        card.Finished += () =>
        {
            if (voice != null && GodotObject.IsInstanceValid(voice)) voice.LineHeard -= heard;
            VoicePlayer.SubtitlesHeld = false;
            LoadingScreen.ReturnToTitle(GetTree());
        };
        CampSession session = Game.Instance.session;
        bool played = session != null && session.Story.LinesPlayed.Contains("e7");
        session?.FinishCredits();
        bool triggered = session != null && !played && session.Story.LinesPlayed.Contains("e7");
        if (!triggered && voice != null) voice.Enqueue("e7", fireKept ? "survived" : "went_out");
    }

    public void notify(string message)
    {
        // The play screen carries no hint or status text (docs/direction.md); teaching
        // lives in the journal's list. Notices are kept for the log only.
        GD.Print($"NOTICE {message}");
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

    public static string _format_hour(double hour) => UiTheme.ClockText(hour);

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
        if (mode != Game.Mode.PAUSED)
        {
            _menu.Visible = false;
            _settings.Visible = false;
        }
        switch (mode)
        {
            case Game.Mode.PLAY:
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

    public void _on_quality_changed(QualityPreset p)
    {
        _quality_label.Text = G.format("%s  ·  %d%% render scale", new Godot.Collections.Array { p.display_name, (long)round(p.render_scale * 100.0) });
    }

    public override void _ExitTree()
    {
        _attach(null);
        VoicePlayer.SubtitlesHeld = false;
        if (Game.Instance != null && ReferenceEquals(Game.Instance.hud, this)) Game.Instance.hud = null;
    }
}

/// The interaction dot: a small paper dot with a soft shadow, shown when something
/// can be used. While a stone is charged, a thin ring fills around it.
public partial class InteractionDot : Control
{
    private double _alpha;
    public bool Shown;
    public double Charge;

    public InteractionDot()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        double target = Shown || Charge > 0 ? 1 : 0;
        double next = Mathf.MoveToward(_alpha, target, delta / 0.25);
        if (Math.Abs(next - _alpha) > 1e-4 || Charge > 0) QueueRedraw();
        _alpha = next;
    }

    public override void _Draw()
    {
        if (_alpha <= 0.001) return;
        Vector2 c = Size * 0.5f;
        float a = (float)_alpha;
        DrawCircle(c + new Vector2(0, 1), 4.5f, new Color(0, 0, 0, 0.35f * a), true, -1, true);
        DrawCircle(c, 3.5f, new Color(UiTheme.PAPER, 0.95f * a), true, -1, true);
        if (Charge > 0)
        {
            DrawArc(c, 11, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.25f * a), 2.5f, true);
            DrawArc(c, 11, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)Math.Clamp(Charge, 0, 1), 48, new Color(UiTheme.PAPER, 0.9f * a), 1.5f, true);
        }
    }
}
