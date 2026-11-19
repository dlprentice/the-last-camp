using System;
using System.Threading.Tasks;
using Godot;

namespace LastCamp;

/// A sheet of paper held up to read: June's letter, Tom's tally book, Tom's
/// letter. A plain placeholder panel in the paper and ink colours; the shell's
/// UI restyles it. While it is up the player stands still; the interact key,
/// a click or Enter turns the page and closes the last one, Esc closes it.
public partial class ReadingSheet : CanvasLayer
{
    public static readonly Color PAPER = new(0.937f, 0.906f, 0.839f);
    public static readonly Color INK = new(0.165f, 0.149f, 0.133f);
    public static readonly Color FADED = new(0.431f, 0.396f, 0.353f);

    public bool IsOpen { get; private set; }
    public int Page { get; private set; }
    public int PageCount => _pages.Length;
    /// Raised each time a sheet is put away, with the number of pages it had.
    public event Action<int>? Closed;

    private string[] _pages = Array.Empty<string>();
    private Control _root = null!;
    private Label _text = null!;
    private Label _footer = null!;
    private TaskCompletionSource<bool>? _done;
    private Player? _reader;
    private ulong _openedFrame;

    public override void _Ready()
    {
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Modulate = new Color(1, 1, 1, 0) };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var paper = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = PAPER, BorderColor = new Color(INK, 0.35f),
            ContentMarginLeft = 56, ContentMarginRight = 56, ContentMarginTop = 48, ContentMarginBottom = 40,
        };
        style.SetBorderWidthAll(1);
        paper.AddThemeStyleboxOverride("panel", style);
        paper.AnchorLeft = paper.AnchorRight = 0.5f;
        paper.AnchorTop = paper.AnchorBottom = 0.5f;
        paper.OffsetLeft = -330;
        paper.OffsetRight = 330;
        paper.OffsetTop = -400;
        paper.OffsetBottom = 400;
        _root.AddChild(paper);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        paper.AddChild(column);
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _text.AddThemeColorOverride("font_color", INK);
        _text.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(_text);
        _footer = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _footer.AddThemeColorOverride("font_color", FADED);
        _footer.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_footer);
    }

    /// Shows `pages` and completes once the reader has put the sheet away.
    public Task Read(string[] pages, Player? reader)
    {
        if (IsOpen || pages.Length == 0) return Task.CompletedTask;
        _pages = pages;
        Page = 0;
        IsOpen = true;
        _openedFrame = Engine.GetProcessFrames();
        _reader = reader;
        _reader?.suspend();
        _done = new TaskCompletionSource<bool>();
        _show();
        _root.Visible = true;
        CreateTween().TweenProperty(_root, "modulate:a", 1.0, 0.3).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        return _done.Task;
    }

    private void _show()
    {
        _text.Text = _pages[Page];
        _footer.Text = _pages.Length > 1 ? $"{Page + 1} / {_pages.Length}    [E] {(Page + 1 < _pages.Length ? "Turn the page" : "Put it away")}" : "[E] Put it away";
    }

    /// The next page, or away after the last one.
    public void Next()
    {
        if (!IsOpen) return;
        if (Page + 1 < _pages.Length)
        {
            Page++;
            Game.Instance.audio?.play_interact(Sounds.PageTurn);
            _show();
            return;
        }
        Close();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        int pages = _pages.Length;
        Tween tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 0.0, 0.3).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(() => { if (!IsOpen) _root.Visible = false; }));
        if (_reader != null && IsInstanceValid(_reader) && Game.Instance.mode == Game.Mode.PLAY) _reader.begin();
        _reader = null;
        Closed?.Invoke(pages);
        _done?.TrySetResult(true);
        _done = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || GetTree().Paused) return;
        // The key press that opened the sheet must not also turn its first page.
        if (Engine.GetProcessFrames() == _openedFrame) return;
        bool next = @event.IsActionPressed("interact") || @event.IsActionPressed("ui_accept")
            || @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
        if (next)
        {
            Next();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
