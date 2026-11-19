using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Godot;

namespace LastCamp;

/// One heading of the credits and its lines.
public sealed record CreditSection(string Heading, List<string> Lines);

/// The credits, read from the same tables the repository credits from: the
/// texture, model and font sources, the audio manifest (grouped by id) and June's
/// voice script. These files ship in the build (export include filter).
public static class CreditsData
{
    public static List<CreditSection> Build()
    {
        var sections = new List<CreditSection>
        {
            new("The Last Camp", new() { "A game by David Prentice" }),
            new("Fonts", Table("res://fonts/SOURCES.md")),
            new("Photoscanned models", Table("res://models/SOURCES.md")),
            new("Materials", Table("res://textures/SOURCES.md")),
        };
        var sound = new List<string> { "All sound and June's voice generated with ElevenLabs, licensed for this game" };
        sound.AddRange(SoundGroups(Read("res://audio/generated/manifest.json")));
        int lines = VoiceLines(Read("res://audio/generated/voice.json"));
        if (lines > 0) sound.Add($"June's voice · {lines} paragraphs, seven entries and nine margin notes");
        sections.Add(new("Sound", sound));
        sections.Add(new("Engine and code", new()
        {
            "Godot Engine 4.8 · MIT licence · Godot Engine contributors",
            ".NET runtime · MIT licence · .NET Foundation and contributors",
            "Jolt Physics · MIT licence · Jorrit Rouwe",
            "Hash without Sine · MIT licence · David Hoskins",
        }));
        sections.RemoveAll(s => s.Lines.Count == 0);
        return sections;
    }

    private static string Read(string path) => FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : "";

    private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)");

    /// Rows of a sources table as "Asset · creator · provider · licence".
    public static List<string> Table(string path) => TableRows(Read(path));

    public static List<string> TableRows(string markdown)
    {
        var rows = new List<string>();
        string[]? header = null;
        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                header = null;
                continue;
            }
            string[] cells = line.Trim('|').Split('|').Select(c => Link.Replace(c.Trim(), "$1").Replace("`", "")).ToArray();
            if (header == null)
            {
                header = cells;
                continue;
            }
            if (cells.All(c => c.Trim('-', ':', ' ').Length == 0)) continue;
            string Cell(string name)
            {
                int i = Array.FindIndex(header, h => h.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i < cells.Length ? cells[i] : "";
            }
            string asset = Cell("Asset");
            if (asset.Length == 0) continue;
            string[] parts = { asset, Cell("Creator"), Cell("Provider"), Cell("Licen") };
            rows.Add(string.Join(" · ", parts.Where(p => p.Length > 0)));
        }
        return rows;
    }

    private static readonly Dictionary<string, string> GroupNames = new()
    {
        ["bed"] = "Ambience", ["birds"] = "Wildlife", ["owl"] = "Wildlife", ["loon"] = "Wildlife",
        ["thunder"] = "Weather", ["fire"] = "Fire", ["step"] = "Footsteps", ["water"] = "Water",
        ["log"] = "Firewood", ["stone"] = "Stones", ["lantern"] = "Lantern", ["tent"] = "Tent",
        ["tin"] = "Tom's tin", ["page"] = "Paper", ["letter"] = "Paper", ["ash"] = "Leaving",
        ["ring"] = "Leaving", ["sit"] = "Sitting", ["ui"] = "Interface", ["journal"] = "Interface",
        ["card"] = "Interface",
    };

    /// The manifest's sound ids grouped by their first word: "Ambience · day woods, evening, ...".
    public static List<string> SoundGroups(string manifest)
    {
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        if (manifest.Length == 0) return new();
        try
        {
            if (JsonNode.Parse(manifest)?["sounds"] is not JsonObject sounds) return new();
            foreach ((string id, JsonNode? _) in sounds)
            {
                string prefix = id.Split('_')[0];
                string group = GroupNames.TryGetValue(prefix, out string? name) ? name
                    : char.ToUpperInvariant(prefix[0]) + prefix[1..];
                if (!groups.TryGetValue(group, out List<string>? ids)) groups[group] = ids = new();
                ids.Add(id.Replace('_', ' '));
            }
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException)
        {
            return new();
        }
        return groups.Select(g => $"{g.Key} · {string.Join(", ", g.Value)}").ToList();
    }

    public static int VoiceLines(string voice)
    {
        try { return voice.Length == 0 ? 0 : (JsonNode.Parse(voice)?["paragraphs"] as JsonArray)?.Count ?? 0; }
        catch (System.Text.Json.JsonException) { return 0; }
    }
}

/// The credits roll in the kit: ink on a full page of paper, rising slowly.
/// Esc, a click or Enter ends it early. In the ending it is followed by June's
/// postcard and then the title.
public partial class CreditsRoll : Control
{
    public const double Speed = 72;
    public event Action? Finished;
    private VBoxContainer _roll = null!;
    private bool _done;

    public CreditsRoll()
    {
        Name = "Credits";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        var page = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        page.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiTheme.PAPER });
        AddChild(page);
        _roll = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(1200, 0) };
        _roll.AddThemeConstantOverride("separation", 8);
        AddChild(_roll);
        foreach (CreditSection section in CreditsData.Build())
        {
            _roll.AddChild(UiTheme.label(section.Heading, section.Heading == "The Last Camp" ? UiTheme.TITLE : UiTheme.HEADING,
                section.Heading == "The Last Camp" ? UiTheme.INK : UiTheme.RUST));
            foreach (string line in section.Lines)
            {
                Label l = UiTheme.label(line, UiTheme.SMALL, UiTheme.INK);
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                l.CustomMinimumSize = new Vector2(1200, 0);
                _roll.AddChild(l);
            }
            _roll.AddChild(new Control { CustomMinimumSize = new Vector2(0, 48), MouseFilter = MouseFilterEnum.Ignore });
        }
        _roll.AddChild(UiTheme.label("Thank you for coming up.", UiTheme.BODY, UiTheme.FADED));
        Modulate = new Color(1, 1, 1, 0);
        UiTheme.Fade(this, 1.0, 0.4);
        Callable.From(Place).CallDeferred();
    }

    private void Place()
    {
        _roll.Position = new Vector2((Size.X - _roll.Size.X) * 0.5f, Size.Y);
    }

    /// The roll's lines, for checks.
    public IEnumerable<string> Lines => _roll.GetChildren().OfType<Label>().Select(l => l.Text);

    public override void _Process(double delta)
    {
        if (_done || _roll.Size.Y <= 0) return;
        _roll.Position = new Vector2((Size.X - _roll.Size.X) * 0.5f, _roll.Position.Y - (float)(Speed * delta));
        if (_roll.Position.Y + _roll.Size.Y < Size.Y * 0.5f) End();
    }

    /// Moves the roll to `fraction` of its length (for stills).
    public void Seek(double fraction) =>
        _roll.Position = new Vector2(_roll.Position.X, (float)(Size.Y - fraction * (_roll.Size.Y + Size.Y * 0.5)));

    public void End()
    {
        if (_done) return;
        _done = true;
        Tween t = UiTheme.Fade(this, 0.0, 0.4);
        t.TweenCallback(Callable.From(() => Finished?.Invoke()));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true }) { End(); AcceptEvent(); }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(NativeArgs.Name("ui_cancel")) || @event.IsActionPressed(NativeArgs.Name("ui_accept")) || @event.IsActionPressed(NativeArgs.Name("pause")))
        {
            Game.Instance?.audio?.play_interact(Sounds.UiSelect);
            End();
            GetViewport().SetInputAsHandled();
        }
    }
}

/// June's postcard (entry 7): her text in her hand on a card, voiced by the voice
/// player. When she has finished, the way back to the title appears.
public partial class Postcard : Control
{
    public event Action? Finished;
    private readonly string _variant;
    private KitButton _home = null!;
    private Control _card = null!;
    private double _wait;
    private bool _heard;

    public Postcard(bool fireKept)
    {
        Name = "Postcard";
        _variant = fireKept ? "survived" : "went_out";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
    }

    public Postcard() : this(true) { }

    public override void _Ready()
    {
        var dark = new ColorRect { Color = UiTheme.INK, MouseFilter = MouseFilterEnum.Ignore };
        dark.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dark);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(1180, 700), MouseFilter = MouseFilterEnum.Ignore };
        centre.AddChild(card);
        _card = card;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 40);
        card.AddChild(row);
        VoiceScript voice = VoiceScript.Load();
        string text = JournalContent.LineText(voice, "e7", _variant);
        Label words = UiTheme.hand(text.Length > 0 ? text : "Love. You found it.", june: true);
        words.CustomMinimumSize = new Vector2(700, 0);
        words.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(words);
        // The card's back: a rule down the middle and the address side.
        var rule = new ColorRect { Color = new Color(UiTheme.FADED, 0.5f), CustomMinimumSize = new Vector2(1, 560), MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(rule);
        var address = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        address.AddThemeConstantOverride("separation", 22);
        row.AddChild(address);
        foreach (string line in new[] { "For you,", "care of the camp", "" })
        {
            Label l = UiTheme.hand(line, june: true);
            l.CustomMinimumSize = new Vector2(300, 0);
            address.AddChild(l);
        }
        _home = new KitButton("Return to the title", UiTheme.SMALL) { Visible = false, Alignment = HorizontalAlignment.Right };
        _home.SetAnchorsPreset(LayoutPreset.BottomRight);
        _home.Position = new Vector2(-340, -90);
        _home.AddThemeColorOverride("font_color", UiTheme.PAPER);
        _home.AddThemeColorOverride("font_focus_color", UiTheme.PAPER);
        _home.AddThemeColorOverride("font_hover_color", UiTheme.PAPER);
        _home.Pressed += () => Finished?.Invoke();
        AddChild(_home);
        card.PivotOffset = card.CustomMinimumSize * 0.5f;
        card.Rotation = Mathf.DegToRad(-1.2f);
        Modulate = new Color(1, 1, 1, 0);
        UiTheme.Fade(this, 1.0, 0.4);
    }

    /// June has finished the postcard (the voice player says so).
    public void Heard()
    {
        if (_heard) return;
        _heard = true;
        _home.Visible = true;
        _home.FocusQuietly();
    }

    public string Variant => _variant;

    public override void _Process(double delta)
    {
        // Without a voice player the card still offers the way home after a reading.
        _wait += delta;
        if (!_heard && _wait > 45) Heard();
    }
}
