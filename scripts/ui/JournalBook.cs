using System;
using System.Collections.Generic;
using Godot;
using LastCamp.Story;

namespace LastCamp;

/// The journal: a two-page book on paper, the game's one interface. The left page
/// is June's list for the chapter and the entries heard; the right page is her
/// margin notes, then Tom's tally book and his letter once they are in hand.
/// Pages turn with A and D (or the arrow keys) and ease over 300 ms.
public partial class JournalBook : Control
{
    public static readonly Vector2 PageSize = new(760, 860);
    private const float ContentWidth = 760 - 88;
    private const float ContentHeight = 860 - 72 - 64;
    private const int BlockGap = 14;

    private VBoxContainer _leftBody = null!, _rightBody = null!;
    private Label _leftFolio = null!, _rightFolio = null!;
    private KitButton _back = null!, _over = null!;
    private Control _book = null!;
    private List<List<JournalBlock>> _left = new(), _right = new();
    private int _spread;
    private VoiceScript? _voice;
    private Tween? _turn;

    public int Spread => _spread;
    public int Spreads => Math.Max(_left.Count, _right.Count);
    public event Action? CloseRequested;

    public JournalBook()
    {
        Name = "Journal";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Ready()
    {
        // The game is paused behind the book; the world dims under ink.
        var dim = new ColorRect { Color = new Color(UiTheme.INK, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var spread = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        spread.AddThemeConstantOverride("separation", 0);
        centre.AddChild(spread);
        _book = spread;
        (_leftBody, _leftFolio, _back) = Page(spread, "Turn back", HorizontalAlignment.Left);
        (_rightBody, _rightFolio, _over) = Page(spread, "Turn over", HorizontalAlignment.Right);
        _back.Pressed += () => Turn(-1);
        _over.Pressed += () => Turn(1);
    }

    private (VBoxContainer body, Label folio, KitButton button) Page(HBoxContainer spread, string turn, HorizontalAlignment side)
    {
        var page = new PanelContainer { CustomMinimumSize = PageSize, MouseFilter = MouseFilterEnum.Ignore };
        spread.AddChild(page);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        page.AddChild(column);
        var body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", BlockGap);
        column.AddChild(body);
        var foot = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 48) };
        column.AddChild(foot);
        var folio = UiTheme.label("", UiTheme.SMALL, UiTheme.FADED, side);
        folio.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // The keyboard turns pages with A and D; the buttons are for the mouse.
        var button = new KitButton(turn, UiTheme.SMALL) { FocusMode = FocusModeEnum.None };
        if (side == HorizontalAlignment.Left) { foot.AddChild(button); foot.AddChild(folio); }
        else { button.Alignment = HorizontalAlignment.Right; foot.AddChild(folio); foot.AddChild(button); }
        return (body, folio, button);
    }

    /// Fills the book from the story and opens it at the first spread.
    public void Open(StoryState story)
    {
        Refresh(story);
        _spread = 0;
        Show(_spread);
        Visible = true;
        Modulate = new Color(1, 1, 1, 0);
        _book.PivotOffset = _book.Size * 0.5f;
        _book.Scale = new Vector2(0.97f, 0.97f);
        Tween t = CreateTween().SetParallel();
        t.TweenProperty(this, "modulate:a", 1.0, UiTheme.EASE).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_book, "scale", Vector2.One, UiTheme.EASE).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    public void Refresh(StoryState story)
    {
        _voice ??= VoiceScript.Load();
        _left = JournalContent.Paginate(JournalContent.LeftPages(story, _voice), Measure, ContentHeight);
        _right = JournalContent.Paginate(JournalContent.RightPages(story, _voice), Measure, ContentHeight);
    }

    public void Turn(int step)
    {
        int next = Math.Clamp(_spread + step, 0, Math.Max(Spreads - 1, 0));
        if (next == _spread) return;
        _spread = next;
        Game.Instance?.audio?.play_interact(Sounds.PageTurn);
        _turn?.Kill();
        _turn = CreateTween();
        foreach (Control body in new Control[] { _leftBody, _rightBody })
            _turn.Parallel().TweenProperty(body, "modulate:a", 0.0, 0.12).SetTrans(Tween.TransitionType.Sine);
        _turn.TweenCallback(Callable.From(() => Show(_spread)));
        foreach (Control body in new Control[] { _leftBody, _rightBody })
            _turn.Parallel().TweenProperty(body, "modulate:a", 1.0, 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private void Show(int spread)
    {
        Fill(_leftBody, spread < _left.Count ? _left[spread] : null);
        Fill(_rightBody, spread < _right.Count ? _right[spread] : null);
        _leftFolio.Text = (spread * 2 + 1).ToString();
        _rightFolio.Text = (spread * 2 + 2).ToString();
        _back.Visible = spread > 0;
        _over.Visible = spread < Spreads - 1;
    }

    private static void Fill(VBoxContainer body, List<JournalBlock>? blocks)
    {
        foreach (Node child in body.GetChildren())
        {
            body.RemoveChild(child);
            child.QueueFree();
        }
        if (blocks == null) return;
        foreach (JournalBlock block in blocks) body.AddChild(Render(block));
    }

    private static (Font font, int size, Color color) Face(JournalFace face) => face switch
    {
        JournalFace.Heading => (UiTheme.Serif, UiTheme.HEADING, UiTheme.INK),
        JournalFace.Small => (UiTheme.SerifItalic, UiTheme.SMALL, UiTheme.FADED),
        JournalFace.June => (UiTheme.JuneHand, UiTheme.JuneHandSize, UiTheme.INK),
        JournalFace.Tom => (UiTheme.TomHand, UiTheme.TomHandSize, UiTheme.FADED),
        _ => (UiTheme.Serif, UiTheme.BODY, UiTheme.INK),
    };

    private static float Measure(JournalBlock block)
    {
        (Font font, int size, _) = Face(block.Face);
        return font.GetMultilineStringSize(block.Text, HorizontalAlignment.Left, ContentWidth, size).Y + BlockGap + 4;
    }

    private static Control Render(JournalBlock block)
    {
        (Font font, int size, Color color) = Face(block.Face);
        if (block.Struck)
        {
            // A done task: June's line through it.
            var struck = new RichTextLabel
            {
                BbcodeEnabled = true, FitContent = true, ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(ContentWidth, 0), Text = "[s]" + block.Text + "[/s]",
            };
            struck.AddThemeFontOverride("normal_font", font);
            struck.AddThemeFontSizeOverride("normal_font_size", size);
            struck.AddThemeColorOverride("default_color", new Color(color, 0.75f));
            return struck;
        }
        Label label = UiTheme.label(block.Text, size, color, HorizontalAlignment.Left);
        label.AddThemeFontOverride("font", font);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(ContentWidth, 0);
        label.VerticalAlignment = VerticalAlignment.Top;
        return label;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event.IsActionPressed("ui_left") || @event.IsActionPressed("move_left")) { Turn(-1); GetViewport().SetInputAsHandled(); }
        else if (@event.IsActionPressed("ui_right") || @event.IsActionPressed("move_right")) { Turn(1); GetViewport().SetInputAsHandled(); }
        else if (@event.IsActionPressed("pause") || @event.IsActionPressed("ui_cancel"))
        {
            CloseRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }
}
