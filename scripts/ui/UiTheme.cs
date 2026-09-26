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

/// Small factory for the demo's UI look: warm accent, quiet greys, soft panels.
public partial class UiTheme
{
    public static readonly Color ACCENT = new Color(1.0f, 0.80f, 0.55f);
    public static readonly Color TEXT = new Color(0.93f, 0.93f, 0.95f);
    public static readonly Color MUTED = new Color(0.72f, 0.74f, 0.80f, 0.9f);
    public static readonly Color PANEL = new Color(0.03f, 0.03f, 0.045f, 0.78f);

    public static Label label(string text, long size, Color? color_opt = null, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        Color color = color_opt ?? TEXT;
        Label l = new Label();
        l.Text = text;
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.6f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        l.AddThemeConstantOverride("shadow_outline_size", 2);
        l.HorizontalAlignment = align;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    public static PanelContainer panel(Vector2 min_size)
    {
        PanelContainer p = new PanelContainer();
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = PANEL;
        style.SetCornerRadiusAll(10);
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(ACCENT, 0.22f);
        style.SetContentMarginAll(18);
        p.AddThemeStyleboxOverride("panel", style);
        p.CustomMinimumSize = min_size;
        p.MouseFilter = Control.MouseFilterEnum.Ignore;
        return p;
    }

    public static string key_hint(Godot.Collections.Array<string> parts)
    {
        return string.Join("    ", parts);
    }
}
