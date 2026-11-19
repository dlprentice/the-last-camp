using System;
using System.Threading.Tasks;
using Godot;

namespace LastCamp;

/// A sheet of paper held up to read: June's letter in her hand, Tom's tally book
/// and his letter in his pencil. Drawn in the UI kit (the one paper panel, the
/// kit's faces and sizes; the plain-text setting sets both hands in the serif).
/// While it is up the player stands still; the interact key, a click or Enter
/// turns the page and puts the last one away, Esc puts it away.
public partial class ReadingSheet : CanvasLayer
{
    public bool IsOpen { get; private set; }
    public int Page { get; private set; }
    public int PageCount => _pages.Length;
    /// True while the sheet is written in June's hand, false for Tom's.
    public bool JuneHand { get; private set; }
    /// Raised each time a sheet is put away, with the number of pages it had.
    public event Action<int>? Closed;

    private string[] _pages = Array.Empty<string>();
    private int _sketchPage = -1;
    private Control _root = null!;
    private Label _text = null!;
    private Label _footer = null!;
    private Control _sketch = null!;
    private TaskCompletionSource<bool>? _done;
    private Player? _reader;
    private ulong _openedFrame;

    public override void _Ready()
    {
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.Root("Reading");
        _root.Visible = false;
        _root.Modulate = new Color(1, 1, 1, 0);
        AddChild(_root);
        var paper = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        paper.AnchorLeft = paper.AnchorRight = 0.5f;
        paper.AnchorTop = paper.AnchorBottom = 0.5f;
        paper.OffsetLeft = -340;
        paper.OffsetRight = 340;
        paper.OffsetTop = -410;
        paper.OffsetBottom = 410;
        _root.AddChild(paper);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        paper.AddChild(column);
        _text = UiTheme.hand("", june: true);
        _text.VerticalAlignment = VerticalAlignment.Top;
        _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(_text);
        _sketch = new RouteSketch { CustomMinimumSize = new Vector2(0, 170), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(_sketch);
        _footer = UiTheme.label("", UiTheme.SMALL, UiTheme.FADED, HorizontalAlignment.Right);
        column.AddChild(_footer);
    }

    /// Shows `pages` in June's hand or Tom's and completes once the reader has put
    /// the sheet away. `sketchPage` is the page that carries Tom's route sketch.
    public Task Read(string[] pages, Player? reader, bool june, int sketchPage = -1)
    {
        if (IsOpen || pages.Length == 0) return Task.CompletedTask;
        _pages = pages;
        _sketchPage = sketchPage;
        JuneHand = june;
        Page = 0;
        IsOpen = true;
        _openedFrame = Engine.GetProcessFrames();
        _reader = reader;
        _reader?.suspend();
        _done = new TaskCompletionSource<bool>();
        _show();
        _root.Visible = true;
        UiTheme.Fade(_root, 1.0);
        return _done.Task;
    }

    private void _show()
    {
        // The hand is read from the settings each page, so the plain-text toggle holds.
        _text.AddThemeFontOverride("font", JuneHand ? UiTheme.JuneHand : UiTheme.TomHand);
        _text.AddThemeFontSizeOverride("font_size", JuneHand ? UiTheme.JuneHandSize : UiTheme.TomHandSize);
        _text.AddThemeColorOverride("font_color", JuneHand ? UiTheme.INK : UiTheme.FADED);
        _text.Text = _pages[Page];
        _sketch.Visible = Page == _sketchPage;
        _footer.Text = _pages.Length > 1 ? $"{Page + 1} / {_pages.Length}" : "";
    }

    /// The font the page text is drawn in (for checks).
    public Font TextFont => _text.GetThemeFont("font");

    /// The next page, or away after the last one.
    public void Next()
    {
        if (!IsOpen) return;
        if (Page + 1 < _pages.Length)
        {
            Page++;
            Game.Instance?.audio?.play_interact(Sounds.PageTurn);
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
        Tween tween = UiTheme.Fade(_root, 0.0);
        tween.TweenCallback(Callable.From(() => { if (!IsOpen) _root.Visible = false; }));
        if (_reader != null && IsInstanceValid(_reader) && Game.Instance?.mode == Game.Mode.PLAY) _reader.begin();
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

/// Tom's pencil sketch in the tally book: the old fire ring, his line up the
/// south slope in a few switchbacks, and the notch at its start. Plain strokes.
public partial class RouteSketch : Control
{
    public override void _Draw()
    {
        Color pencil = new(UiTheme.FADED, 0.85f);
        Vector2 size = Size;
        Vector2 ring = new(size.X * 0.22f, size.Y * 0.78f);
        float r = Math.Min(size.Y * 0.12f, 22f);
        DrawArc(ring, r, 0.3f, Mathf.Tau + 0.1f, 28, pencil, 1.6f, true);
        Vector2[] route =
        {
            ring + new Vector2(r * 1.1f, -r * 0.4f), new(size.X * 0.40f, size.Y * 0.62f), new(size.X * 0.34f, size.Y * 0.45f),
            new(size.X * 0.55f, size.Y * 0.34f), new(size.X * 0.50f, size.Y * 0.20f), new(size.X * 0.70f, size.Y * 0.10f),
        };
        DrawPolyline(route, pencil, 1.4f, true);
        // The notch at the start: a small V cut beside the ring.
        Vector2 n = route[0] + new Vector2(4, -6);
        DrawPolyline(new[] { n + new Vector2(-5, -6), n, n + new Vector2(5, -6) }, pencil, 1.6f, true);
        // Tom's bench at the top, a short level stroke.
        Vector2 top = route[^1];
        DrawLine(top + new Vector2(-10, -4), top + new Vector2(10, -4), pencil, 1.8f, true);
    }
}
