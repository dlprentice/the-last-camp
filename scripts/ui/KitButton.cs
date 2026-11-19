using System;
using Godot;

namespace LastCamp;

/// The kit's one button: ink text on paper, underlined in rust on hover and focus
/// with a 120 ms ease. Hover takes focus, so the mouse and the keyboard agree on
/// which item is chosen. Each focus move plays `ui_move`, each press `ui_select`.
public partial class KitButton : Button
{
    private double _underline;
    private Tween? _tween;
    /// Set while a menu moves focus itself, so opening a menu is silent.
    public static bool Quiet;

    public KitButton() : this("") { }

    public KitButton(string text, int size = UiTheme.BODY)
    {
        Text = text;
        Alignment = HorizontalAlignment.Left;
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeFontSizeOverride("font_size", size);
    }

    public override void _Ready()
    {
        FocusEntered += () =>
        {
            Ease(1);
            if (!Quiet) Game.Instance?.audio?.play_interact(Sounds.UiMove);
        };
        FocusExited += () => Ease(0);
        MouseEntered += () =>
        {
            if (Disabled) return;
            if (FocusMode == FocusModeEnum.None) { Ease(1); Game.Instance?.audio?.play_interact(Sounds.UiMove); }
            else if (!HasFocus()) GrabFocus();
        };
        MouseExited += () => { if (!HasFocus()) Ease(0); };
        Pressed += () => Game.Instance?.audio?.play_interact(Sounds.UiSelect);
    }

    /// Focuses this button without the move sound (a menu opening).
    public void FocusQuietly()
    {
        Quiet = true;
        GrabFocus();
        Quiet = false;
    }

    private void Ease(double to)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<double>(v => { _underline = v; QueueRedraw(); }), _underline, to, UiTheme.BUTTON_EASE)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        if (_underline <= 0.001 || Disabled) return;
        Font font = GetThemeFont("font");
        int size = GetThemeFontSize("font_size");
        StyleBox style = GetThemeStylebox("normal");
        float width = font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
        float left = Alignment switch
        {
            HorizontalAlignment.Center => (Size.X - width) * 0.5f,
            HorizontalAlignment.Right => Size.X - style.ContentMarginRight - width,
            _ => style.ContentMarginLeft,
        };
        float baseline = (Size.Y + font.GetAscent(size) - font.GetDescent(size)) * 0.5f + 4;
        float drawn = (float)(width * _underline);
        DrawLine(new Vector2(left, baseline), new Vector2(left + drawn, baseline), UiTheme.RUST, 2f, true);
    }
}
