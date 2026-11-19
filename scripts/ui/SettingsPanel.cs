using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// The settings page, shared by the title and the pause menu. Every change is
/// applied at once and written to `user://settings.cfg`.
public partial class SettingsPanel : PanelContainer
{
    public event Action? Closed;
    public string SavePath = GameSettings.DefaultPath;
    private readonly List<Control> _focusables = new();
    private KitButton _first = null!;

    public SettingsPanel()
    {
        Name = "Settings";
        CustomMinimumSize = new Vector2(760, 0);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        GameSettings s = GameSettings.Current;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        box.AddChild(UiTheme.label("Settings", UiTheme.HEADING, UiTheme.INK, HorizontalAlignment.Left));
        box.AddChild(Gap(6));

        _first = Cycle(box, "Quality", () => s.PresetName, () =>
        {
            int i = Array.IndexOf(GameSettings.Presets, s.Preset);
            s.Preset = GameSettings.Presets[(i + 1) % GameSettings.Presets.Length];
        });
        Slider(box, "Field of view", GameSettings.FovMin, GameSettings.FovMax, 1, () => s.Fov, v => s.Fov = v, v => $"{v:0}°");
        Cycle(box, "Head-bob", () => s.HeadBob ? "On" : "Off", () => s.HeadBob = !s.HeadBob);
        box.AddChild(Gap(4));
        Slider(box, "Master volume", 0, 1, 0.05, () => s.Master, v => s.Master = v, Percent);
        Slider(box, "Ambience", 0, 1, 0.05, () => s.Ambience, v => s.Ambience = v, Percent);
        Slider(box, "Effects", 0, 1, 0.05, () => s.Effects, v => s.Effects = v, Percent);
        Slider(box, "Voice", 0, 1, 0.05, () => s.Voice, v => s.Voice = v, Percent);
        box.AddChild(Gap(4));
        Cycle(box, "Subtitles", () => s.Subtitles ? "On" : "Off", () => s.Subtitles = !s.Subtitles);
        Cycle(box, "Handwriting", () => s.PlainText ? "Plain text" : "Handwritten", () => s.PlainText = !s.PlainText);
        box.AddChild(Gap(4));
        Slider(box, "Mouse sensitivity", GameSettings.SensitivityMin, GameSettings.SensitivityMax, 0.05,
            () => s.MouseSensitivity, v => s.MouseSensitivity = v, v => $"{v:0.00}×");
        Cycle(box, "Invert look", () => s.InvertY ? "On" : "Off", () => s.InvertY = !s.InvertY);
        box.AddChild(Gap(10));
        var back = new KitButton("Back");
        back.Pressed += Close;
        box.AddChild(back);
        _focusables.Add(back);
        // Up and down walk the rows in order, wrapping at the ends.
        for (int i = 0; i < _focusables.Count; i++)
        {
            Control c = _focusables[i];
            c.FocusNeighborBottom = c.GetPathTo(_focusables[(i + 1) % _focusables.Count]);
            c.FocusNeighborTop = c.GetPathTo(_focusables[(i + _focusables.Count - 1) % _focusables.Count]);
        }
    }

    public void Open()
    {
        Visible = true;
        Modulate = new Color(1, 1, 1, 0);
        UiTheme.Fade(this, 1);
        _first.FocusQuietly();
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("pause")))
        {
            Game.Instance?.audio?.play_interact(Sounds.UiSelect);
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private static string Percent(double v) => $"{Math.Round(v * 100):0}%";

    private static Control Gap(int h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };

    private HBoxContainer Row(VBoxContainer box, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        var label = UiTheme.label(name, UiTheme.BODY, UiTheme.INK, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(280, 40);
        row.AddChild(label);
        box.AddChild(row);
        return row;
    }

    private void Commit()
    {
        GameSettings.Current.Commit(SavePath);
    }

    /// A row whose value is a button: pressing it moves to the next value.
    private KitButton Cycle(VBoxContainer box, string name, Func<string> value, Action next)
    {
        HBoxContainer row = Row(box, name);
        var button = new KitButton(value()) { CustomMinimumSize = new Vector2(320, 40) };
        button.Pressed += () =>
        {
            next();
            button.Text = value();
            button.QueueRedraw();
            Commit();
        };
        row.AddChild(button);
        _focusables.Add(button);
        return button;
    }

    private void Slider(VBoxContainer box, string name, double min, double max, double step,
        Func<double> get, Action<double> set, Func<double, string> show)
    {
        HBoxContainer row = Row(box, name);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = get(),
            CustomMinimumSize = new Vector2(240, 40), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
        };
        var shown = UiTheme.label(show(get()), UiTheme.SMALL, UiTheme.FADED, HorizontalAlignment.Left);
        shown.CustomMinimumSize = new Vector2(70, 40);
        slider.ValueChanged += v =>
        {
            set(v);
            shown.Text = show(v);
            Commit();
        };
        slider.FocusEntered += () => { if (!KitButton.Quiet) Game.Instance?.audio?.play_interact(Sounds.UiMove); };
        slider.MouseEntered += () => slider.GrabFocus();
        row.AddChild(slider);
        row.AddChild(shown);
        _focusables.Add(slider);
    }
}
