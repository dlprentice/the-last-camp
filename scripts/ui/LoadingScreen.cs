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

/// Full-screen cover shown while the world generates. Fades out on finish.
public partial class LoadingScreen : CanvasLayer
{
    public ColorRect _backdrop;
    public Label _title;
    public Label _stage;
    public ProgressBar _bar;
    public StyleBoxFlat _fill;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = Node.ProcessModeEnum.Always;
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0.015f, 0.016f, 0.022f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_backdrop);

        VBoxContainer box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.GrowVertical = Control.GrowDirection.Both;
        box.CustomMinimumSize = new Vector2(520, 0);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddThemeConstantOverride("separation", 18);
        _backdrop.AddChild(box);

        _title = UiTheme.label("THE LAST CAMP", 44, UiTheme.ACCENT);
        _title.AddThemeConstantOverride("outline_size", 0);
        box.AddChild(_title);
        Label sub = UiTheme.label("A Godot 4.8 rendering study", 16, UiTheme.MUTED);
        box.AddChild(sub);

        _bar = new ProgressBar();
        _bar.CustomMinimumSize = new Vector2(520, 6);
        _bar.ShowPercentage = false;
        _bar.MinValue = 0.0;
        _bar.MaxValue = 1.0;
        StyleBoxFlat bg = new StyleBoxFlat();
        bg.BgColor = new Color(1, 1, 1, 0.08f);
        bg.SetCornerRadiusAll(3);
        _fill = new StyleBoxFlat();
        _fill.BgColor = UiTheme.ACCENT;
        _fill.SetCornerRadiusAll(3);
        _bar.AddThemeStyleboxOverride("background", bg);
        _bar.AddThemeStyleboxOverride("fill", _fill);
        box.AddChild(_bar);

        _stage = UiTheme.label("Preparing", 15, UiTheme.MUTED);
        box.AddChild(_stage);
    }

    public void set_progress(string stage_name, double fraction)
    {
        _stage.Text = stage_name;
        _bar.Value = clampf(fraction, 0.0, 1.0);
    }

    public void finish()
    {
        _bar.Value = 1.0;
        _stage.Text = "Ready";
        Tween tween = CreateTween();
        tween.TweenInterval(0.25);
        tween.TweenProperty(_backdrop, "modulate:a", 0.0, 1.2).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(new Callable(this, Node.MethodName.QueueFree));
    }
}
