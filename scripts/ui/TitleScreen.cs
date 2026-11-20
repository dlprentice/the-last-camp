using System;
using Godot;
using LastCamp.Story;

namespace LastCamp;

/// The title's lettering and menu: Continue (with the chapter and the saved time,
/// only when a save exists), New Game (asking before it replaces a save),
/// Settings, Credits and Quit. Continue and New Game wait for the world.
public partial class TitleScreen : Control
{
    private readonly LoadingScreen _owner;
    public Label Status { get; }
    private readonly StorySave _slot = new();
    private StoryState? _saved;
    private VBoxContainer _main = null!, _confirm = null!;
    private PanelContainer _panel = null!;
    private KitButton? _continue;
    private KitButton _newGame = null!, _settingsButton = null!;
    private Label? _continueNote;
    private SettingsPanel _settings = null!;
    private CreditsRoll? _credits;
    private Control _titleBlock = null!;
    private Control _statusGap = null!;
    private Camera3D? _camera;
    private Spline? _path, _look;
    private double _viewTime;

    public TitleScreen(LoadingScreen owner)
    {
        _owner = owner;
        Name = "Title";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Status = UiTheme.label("Making camp", UiTheme.SMALL, UiTheme.FADED, HorizontalAlignment.Left);
    }

    public TitleScreen() : this(null!) { }

    public override void _Ready()
    {
        _saved = _slot.Exists ? _slot.Load() : null;

        _titleBlock = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _titleBlock.SetAnchorsPreset(LayoutPreset.TopLeft);
        _titleBlock.Position = new Vector2(150, 150);
        AddChild(_titleBlock);
        Label title = UiTheme.label("The Last Camp", 96, UiTheme.PAPER, HorizontalAlignment.Left, shadow: true);
        _titleBlock.AddChild(title);

        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(440, 0) };
        _panel.SetAnchorsPreset(LayoutPreset.TopLeft);
        _panel.Position = new Vector2(128, 380);
        AddChild(_panel);
        var stack = new VBoxContainer();
        _panel.AddChild(stack);

        _main = new VBoxContainer();
        _main.AddThemeConstantOverride("separation", 4);
        stack.AddChild(_main);
        if (_saved != null)
        {
            _continue = new KitButton("Continue");
            _continue.Pressed += () => _owner.Begin(false);
            _main.AddChild(_continue);
            _continueNote = UiTheme.label($"{_saved.Chapter.Title}, {_saved.Band.Title.ToLowerInvariant()} · {UiTheme.ClockText(_saved.World.Clock)}",
                UiTheme.SMALL, UiTheme.FADED, HorizontalAlignment.Left);
            var indent = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            indent.AddThemeConstantOverride("margin_left", 10);
            indent.AddThemeConstantOverride("margin_bottom", 6);
            indent.AddChild(_continueNote);
            _main.AddChild(indent);
        }
        _newGame = new KitButton("New Game");
        _newGame.Pressed += NewGame;
        _main.AddChild(_newGame);
        _settingsButton = new KitButton("Settings");
        _settingsButton.Pressed += OpenSettings;
        _main.AddChild(_settingsButton);
        var credits = new KitButton("Credits");
        credits.Pressed += OpenCredits;
        _main.AddChild(credits);
        var quit = new KitButton("Quit");
        quit.Pressed += () => Game.Instance?.quit_cleanly();
        _main.AddChild(quit);

        _confirm = new VBoxContainer { Visible = false };
        _confirm.AddThemeConstantOverride("separation", 4);
        stack.AddChild(_confirm);
        Label ask = UiTheme.label("Begin again? The saved story will be replaced.", UiTheme.BODY, UiTheme.INK, HorizontalAlignment.Left);
        ask.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ask.CustomMinimumSize = new Vector2(352, 0);
        _confirm.AddChild(ask);
        _confirm.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore });
        var yes = new KitButton("Begin again");
        yes.Pressed += BeginAgain;
        _confirm.AddChild(yes);
        var no = new KitButton("Keep my story");
        no.Pressed += () => ShowMain(_newGame);
        _confirm.AddChild(no);

        _statusGap = new Control { CustomMinimumSize = new Vector2(0, 14), MouseFilter = MouseFilterEnum.Ignore };
        stack.AddChild(_statusGap);
        stack.AddChild(Status);

        _settings = new SettingsPanel { Visible = false };
        _settings.SetAnchorsPreset(LayoutPreset.TopLeft);
        _settings.Position = new Vector2(128, 110);
        _settings.Closed += () => ShowMain(_settingsButton);
        AddChild(_settings);

        SetPlayable(false);
        Callable.From(() => { if (IsInsideTree()) (_continue ?? _newGame).FocusQuietly(); }).CallDeferred();
        // The title is up and answering input: the time from launch, for the 15 s target.
        GD.Print($"TITLE_SHOWN ms={Time.GetTicksMsec()}");
    }

    private void SetPlayable(bool playable)
    {
        if (_continue != null) _continue.Disabled = !playable;
        _newGame.Disabled = !playable;
    }

    public void SetProgress(string stage) => Status.Text = $"Making camp · {stage.ToLowerInvariant()}";

    public void SetReady()
    {
        Status.Text = "";
        Status.Visible = false;
        _statusGap.Visible = false;
        SetPlayable(true);
        Control focus = GetViewport()?.GuiGetFocusOwner()!;
        if (focus == null || focus is BaseButton { Disabled: true }) (_continue ?? _newGame).FocusQuietly();
        GD.Print($"TITLE_READY ms={Time.GetTicksMsec()}");
    }

    public void ShowFailure()
    {
        _panel.Visible = false;
        Status.Reparent(_titleBlock);
        Status.Visible = true;
        Status.AddThemeColorOverride("font_color", UiTheme.PAPER);
    }

    private void NewGame()
    {
        if (_saved == null)
        {
            _owner.Begin(true);
            return;
        }
        _main.Visible = false;
        _confirm.Visible = true;
        ((KitButton)_confirm.GetChild(3)).FocusQuietly();
    }

    private void BeginAgain()
    {
        // The fresh story replaces the slot before the session reads it.
        if (!_slot.Save(new StoryState()))
        {
            GD.PushWarning(_slot.LastError);
            Status.Text = "The new story could not be saved; your old one is kept.";
            Status.Visible = true;
            _statusGap.Visible = true;
            ShowMain(_newGame);
            return;
        }
        _owner.Begin(true);
    }

    private void ShowMain(KitButton focus)
    {
        _confirm.Visible = false;
        _main.Visible = true;
        _panel.Visible = true;
        _titleBlock.Visible = true;
        focus.FocusQuietly();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_confirm != null && _confirm.Visible && @event.IsActionPressed("ui_cancel"))
        {
            ShowMain(_newGame);
            GetViewport().SetInputAsHandled();
        }
    }

    public void OpenSettings()
    {
        _panel.Visible = false;
        _titleBlock.Visible = false;
        _settings.Open();
    }

    public void OpenCredits()
    {
        _panel.Visible = false;
        _titleBlock.Visible = false;
        _credits = new CreditsRoll();
        _credits.Finished += () =>
        {
            _credits.QueueFree();
            _credits = null;
            ShowMain(_newGame);
        };
        AddChild(_credits);
    }

    public SettingsPanel SettingsPanel => _settings;
    public CreditsRoll? Credits => _credits;

    // ---- The live view: the intro dolly's path, slowly, back and forth at dusk ----

    public void StartLiveView()
    {
        if (_owner?.GetParent() is not Node3D scene) return;
        WorldController? world = Game.Instance?.world;
        if (world != null)
        {
            world.cycle_running = false;
            world.hour = 19.95;
        }
        _path = new Spline(IntroDolly.PATH);
        _look = new Spline(IntroDolly.LOOK);
        _camera = new Camera3D
        {
            Name = "TitleCamera", Fov = 46, Near = 0.05f, Far = 1600,
            CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER))),
        };
        if (world != null) _camera.Attributes = world.attributes;
        scene.AddChild(_camera);
        _camera.MakeCurrent();
        ApplyView(0);
    }

    public void StopLiveView()
    {
        _camera?.QueueFree();
        _camera = null;
    }

    public override void _Process(double delta)
    {
        if (_camera == null) return;
        _viewTime += delta;
        ApplyView(_viewTime);
    }

    private void ApplyView(double t)
    {
        // A minute out along the first part of the dolly and a minute back.
        double u = 0.5 - 0.5 * Math.Cos(t / 60.0 * Math.PI);
        u = 0.18 + u * 0.42;
        Vector3 pos = _path!.sample(u);
        _camera!.GlobalPosition = pos;
        _camera.LookAt(_look!.sample(u), Vector3.Up);
    }

    public override void _ExitTree() => StopLiveView();
}
