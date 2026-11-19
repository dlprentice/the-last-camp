#nullable disable
using System;
using Godot;

namespace LastCamp;

/// The game's one UI kit (docs/direction.md, "Typeface and UI kit"): paper, ink,
/// one rust accent and faded ink; Noto Serif at 18/22/30/56 on the 1080p canvas
/// (the project stretches the canvas with the window); one paper panel; one
/// button. Every interface control sits under a Control whose `Theme` is `Kit`.
public static class UiTheme
{
    public static readonly Color PAPER = Color.FromHtml("#EFE7D6");
    public static readonly Color INK = Color.FromHtml("#2A2622");
    public static readonly Color RUST = Color.FromHtml("#9A4A2A");
    public static readonly Color FADED = Color.FromHtml("#6E655A");

    /// The rendered film's own titles (Cinematic) keep their look; they are footage, not the game's UI.
    public static readonly Color FILM_ACCENT = new Color(1.0f, 0.80f, 0.55f);
    public static readonly Color FILM_TEXT = new Color(0.93f, 0.93f, 0.95f);
    public static readonly Color FILM_MUTED = new Color(0.72f, 0.74f, 0.80f, 0.9f);

    public static Label film_label(string text, long size, Color? color_opt = null, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        Label l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", color_opt ?? FILM_TEXT);
        l.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.6f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        l.AddThemeConstantOverride("shadow_outline_size", 2);
        return l;
    }

    /// The type scale at 1080p.
    public const int SMALL = 18, BODY = 22, HEADING = 30, TITLE = 56;
    /// June's script face is small on its body; this size reads like the serif's body.
    public const int JUNE_SIZE = 30;
    public const int TOM_SIZE = 22;

    /// Eases (seconds): menus and fades, cards and the journal, the button underline.
    public const double EASE = 0.3, BUTTON_EASE = 0.12;

    public const string SerifPath = "res://fonts/NotoSerif.ttf";
    public const string SerifItalicPath = "res://fonts/NotoSerif-Italic.ttf";
    public const string JunePath = "res://fonts/Caveat.ttf";
    public const string TomPath = "res://fonts/ArchitectsDaughter.ttf";

    private static Font _serif, _serifItalic, _june, _tom;
    private static Theme _kit;
    private static StyleBoxTexture _panel;

    public static Font Serif => _serif ??= LoadFont(SerifPath, "Noto Serif");
    public static Font SerifItalic => _serifItalic ??= LoadFont(SerifItalicPath, "Noto Serif", italic: true);
    public static Font JuneFace => _june ??= LoadFont(JunePath, "Caveat");
    public static Font TomFace => _tom ??= LoadFont(TomPath, "Architects Daughter");

    /// June's hand, or the italic serif when the plain-text setting is on.
    public static Font JuneHand => GameSettings.Current.PlainText ? SerifItalic : JuneFace;
    public static int JuneHandSize => GameSettings.Current.PlainText ? BODY : JUNE_SIZE;
    /// Tom's pencil, or the serif when the plain-text setting is on.
    public static Font TomHand => GameSettings.Current.PlainText ? Serif : TomFace;
    public static int TomHandSize => GameSettings.Current.PlainText ? BODY : TOM_SIZE;

    private static Font LoadFont(string path, string family, bool italic = false)
    {
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is Font font) return font;
        GD.PushWarning($"UiTheme: {path} is not imported; using the system {family}");
        return new SystemFont { FontNames = new[] { family, "serif" }, FontItalic = italic };
    }

    /// The kit's theme, built once in code.
    public static Theme Kit => _kit ??= BuildKit();

    private static Theme BuildKit()
    {
        var t = new Theme { DefaultFont = Serif, DefaultFontSize = BODY };
        t.SetColor("font_color", "Label", INK);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0));
        t.SetConstant("line_spacing", "Label", 4);
        t.SetStylebox("normal", "Label", new StyleBoxEmpty());

        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            t.SetStylebox(state, "Button", Empty(10, 4));
        t.SetColor("font_color", "Button", INK);
        t.SetColor("font_hover_color", "Button", INK);
        t.SetColor("font_focus_color", "Button", INK);
        t.SetColor("font_pressed_color", "Button", RUST);
        t.SetColor("font_hover_pressed_color", "Button", RUST);
        t.SetColor("font_disabled_color", "Button", new Color(FADED, 0.7f));
        t.SetConstant("outline_size", "Button", 0);

        t.SetStylebox("panel", "PanelContainer", Panel);
        t.SetStylebox("panel", "Panel", Panel);

        t.SetColor("default_color", "RichTextLabel", INK);
        t.SetFont("normal_font", "RichTextLabel", Serif);
        t.SetFont("italics_font", "RichTextLabel", SerifItalic);
        t.SetFontSize("normal_font_size", "RichTextLabel", BODY);
        t.SetFontSize("italics_font_size", "RichTextLabel", BODY);
        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());

        // The slider: a faded ink rule, rust where it is filled, an ink grain for the grabber.
        var rule = new StyleBoxFlat { BgColor = new Color(FADED, 0.55f) };
        rule.ContentMarginTop = rule.ContentMarginBottom = 1;
        var fill = new StyleBoxFlat { BgColor = RUST };
        fill.ContentMarginTop = fill.ContentMarginBottom = 1;
        t.SetStylebox("slider", "HSlider", rule);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fill);
        var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = RUST, BorderWidthBottom = 2 };
        focus.ExpandMarginBottom = 6;
        t.SetStylebox("focus", "HSlider", focus);
        t.SetIcon("grabber", "HSlider", Dot(7, INK));
        t.SetIcon("grabber_highlight", "HSlider", Dot(8, RUST));
        t.SetIcon("grabber_disabled", "HSlider", Dot(7, FADED));
        t.SetIcon("tick", "HSlider", Dot(1, new Color(0, 0, 0, 0)));
        t.SetConstant("center_grabber", "HSlider", 0);

        t.SetConstant("separation", "VBoxContainer", 10);
        t.SetConstant("separation", "HBoxContainer", 16);
        return t;
    }

    private static StyleBoxEmpty Empty(float h, float v) => new()
    {
        ContentMarginLeft = h, ContentMarginRight = h, ContentMarginTop = v, ContentMarginBottom = v,
    };

    /// A small filled circle with a soft edge.
    public static ImageTexture Dot(int radius, Color color)
    {
        int size = radius * 2 + 2;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        Vector2 centre = new(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(centre);
                float a = Mathf.Clamp(radius + 0.5f - d, 0, 1);
                image.SetPixel(x, y, new Color(color, color.A * a));
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// The one panel: paper with a soft deckled edge and a faint ink border.
    public static StyleBoxTexture Panel => _panel ??= new StyleBoxTexture
    {
        Texture = ImageTexture.CreateFromImage(PaperImage(256, 256, 1974)),
        TextureMarginLeft = 40, TextureMarginRight = 40, TextureMarginTop = 40, TextureMarginBottom = 40,
        AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit,
        AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit,
        ContentMarginLeft = 44, ContentMarginRight = 44, ContentMarginTop = 36, ContentMarginBottom = 36,
    };

    /// Paper with a faint fibre grain, an irregular soft edge that fades out over
    /// the outer pixels, and a thin wavering ink rule inset from it. Seeded, so it
    /// is the same every run.
    public static Image PaperImage(int w, int h, int seed)
    {
        var grain = new FastNoiseLite { Seed = seed, Frequency = 0.09f, FractalOctaves = 3, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex };
        var edge = new FastNoiseLite { Seed = seed + 1, Frequency = 0.035f, FractalOctaves = 2 };
        Image image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        const float fade = 9f, inset = 22f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float distance = Mathf.Min(Mathf.Min(x + 0.5f, w - x - 0.5f), Mathf.Min(y + 0.5f, h - y - 0.5f));
                // Edge noise sampled along the perimeter so opposite sides tile alike.
                float n = edge.GetNoise2D(x, y) * 4.5f;
                float alpha = Mathf.SmoothStep(0, 1, (distance - 2.5f + n) / fade);
                float g = grain.GetNoise2D(x, y) * 0.022f;
                Color c = new(PAPER.R + g, PAPER.G + g, PAPER.B + g * 1.3f, alpha);
                // The faint ink border, slightly uneven in weight.
                float rule = Mathf.Abs(distance - inset - edge.GetNoise2D(x * 0.5f, y * 0.5f) * 1.2f);
                float ink = Mathf.Clamp(1.1f - rule, 0, 1) * (0.20f + 0.06f * grain.GetNoise2D(y, x));
                c = c.Lerp(new Color(INK, alpha), ink);
                image.SetPixel(x, y, c);
            }
        return image;
    }

    /// A label in the serif at a kit size. On the paper it is ink; over the world
    /// pass PAPER and `shadow: true` for a soft dark shadow.
    public static Label label(string text, long size, Color? color_opt = null, HorizontalAlignment align = HorizontalAlignment.Center, bool shadow = false)
    {
        Label l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", (int)size);
        if (color_opt is Color color) l.AddThemeColorOverride("font_color", color);
        if (shadow) SoftShadow(l);
        return l;
    }

    /// A label in a hand: `june` (her script, ink) or Tom's pencil (faded ink).
    public static Label hand(string text, bool june, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        Label l = label(text, june ? JuneHandSize : TomHandSize, june ? INK : FADED, align);
        l.AddThemeFontOverride("font", june ? JuneHand : TomHand);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static void SoftShadow(Control c)
    {
        c.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.55f));
        c.AddThemeConstantOverride("shadow_offset_x", 0);
        c.AddThemeConstantOverride("shadow_offset_y", 2);
        c.AddThemeConstantOverride("shadow_outline_size", 7);
    }

    public static PanelContainer panel(Vector2 min_size)
    {
        var p = new PanelContainer { CustomMinimumSize = min_size, MouseFilter = Control.MouseFilterEnum.Ignore };
        return p;
    }

    /// A full-rect root for a layer, carrying the kit's theme for everything under it.
    public static Control Root(string name)
    {
        var root = new Control { Name = name, Theme = Kit, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return root;
    }

    /// The theme a control actually draws with: its own, or its nearest Control ancestor's.
    public static Theme EffectiveTheme(Control c)
    {
        for (Node n = c; n != null; n = n.GetParent())
        {
            if (n is Control control && control.Theme != null) return control.Theme;
            if (n is Window) break;
        }
        return null;
    }

    public static Tween Fade(CanvasItem item, double to, double seconds = EASE)
    {
        Tween tween = item.CreateTween();
        tween.TweenProperty(item, "modulate:a", to, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        return tween;
    }

    public static string key_hint(Godot.Collections.Array<string> parts) => string.Join("    ", parts);

    public static string ClockText(double hour)
    {
        int h = (int)Math.Floor(hour), m = (int)Math.Floor((hour - h) * 60.0);
        return $"{h:00}:{m:00}";
    }
}
