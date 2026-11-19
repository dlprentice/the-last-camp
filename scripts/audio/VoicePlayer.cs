using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using LastCamp.Story;

namespace LastCamp;

/// One paragraph of one of June's lines. `Variant` is empty, or the storm outcome
/// ("survived", "went_out") the paragraph belongs to.
public sealed record VoiceParagraph(string Id, string Line, string Variant, string Text, string File, double Lufs, double TruePeak);

/// June's lines as `audio/generated/voice.json` lists them, paragraph by paragraph
/// in playing order. The text is tracked; the voice files are licensed and may be
/// missing, in which case the paragraph is a subtitle only.
public sealed class VoiceScript
{
    public const string ScriptPath = "res://audio/generated/voice.json";

    public readonly List<VoiceParagraph> Paragraphs = new();
    public readonly string RootPath;

    public VoiceScript(string root = SoundBank.Root) => RootPath = root;

    public static VoiceScript Load(string path = ScriptPath, string root = SoundBank.Root)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"VoicePlayer: no voice script at {path}; June's lines will not show");
            return new VoiceScript(root);
        }
        return Parse(FileAccess.GetFileAsString(path), root);
    }

    public static VoiceScript Parse(string json, string root = SoundBank.Root)
    {
        var script = new VoiceScript(root);
        try
        {
            if (JsonNode.Parse(json)?["paragraphs"] is not JsonArray paragraphs) return script;
            foreach (JsonNode? node in paragraphs)
            {
                if (node is not JsonObject p) continue;
                string id = p["id"]?.GetValue<string>() ?? "";
                if (id.Length == 0) continue;
                script.Paragraphs.Add(new VoiceParagraph(id,
                    p["line"]?.GetValue<string>() ?? id.Split('-')[0],
                    p["variant"]?.GetValue<string>() ?? "",
                    p["text"]?.GetValue<string>() ?? "",
                    p["file"]?.GetValue<string>() ?? "",
                    p["lufs"] is JsonValue lufs ? lufs.GetValue<double>() : double.NaN,
                    p["true_peak"] is JsonValue peak ? peak.GetValue<double>() : double.NaN));
            }
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            GD.PushWarning($"VoicePlayer: unreadable voice script ({error.Message})");
            script.Paragraphs.Clear();
        }
        return script;
    }

    /// The paragraphs of `line` in order, with only the storm variant asked for.
    public List<VoiceParagraph> For(string line, string variant) =>
        Paragraphs.Where(p => p.Line == line && (p.Variant.Length == 0 || p.Variant == variant)).ToList();

    public string PathOf(VoiceParagraph paragraph) => paragraph.File.Length == 0 ? "" : RootPath + paragraph.File;

    public int PresentFiles() => Paragraphs.Count(p => PathOf(p).Length > 0 && ResourceLoader.Exists(PathOf(p)));

    /// How long a subtitle stays up when there is no voice file: an unhurried
    /// reading pace, never shorter than a glance.
    /// Levelled alike: each take is brought to the voice target (-16 LUFS) from its
    /// measured loudness, never above the true-peak ceiling (an unmeasured peak counts
    /// as full scale).
    public static double GainDb(VoiceParagraph paragraph)
    {
        double gain = double.IsFinite(paragraph.Lufs) ? Math.Clamp(AudioDirector.TARGET_LUFS["voice"] - paragraph.Lufs, -20, 20) : 0.0;
        double peak = double.IsFinite(paragraph.TruePeak) ? paragraph.TruePeak : 0.0;
        return Math.Min(gain, AudioDirector.PEAK_CEILING_DBTP - peak);
    }

    public static double ReadingSeconds(string text)
    {
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(2.5, words / 2.6 + 1.0);
    }
}

/// Plays June's lines when the story spine says so: each line's paragraphs in order,
/// voiced where the file exists and always subtitled at the bottom of the screen.
/// It keeps playing while the player walks and pauses only with the tree (menus and
/// the journal). A finished line is recorded as heard for the journal.
public partial class VoicePlayer : Node
{
    /// Silence between paragraphs, and before a queued line starts.
    public const double ParagraphGap = 0.6;
    public static readonly Color SubtitleColor = UiTheme.PAPER;
    /// Set while the line's own text is on screen (June's postcard): no subtitle under it.
    public static bool SubtitlesHeld;

    /// A line finished playing (its last paragraph ended).
    public event Action<string>? LineHeard;

    public VoiceScript Script = new();
    public readonly Queue<(VoiceParagraph paragraph, bool last)> Pending = new();
    public VoiceParagraph? Current;
    public bool CurrentIsLast;
    public double Remaining;
    public double Gap;
    public readonly List<string> Heard = new();

    private AudioStreamPlayer _player = null!;
    private CanvasLayer _layer = null!;
    private Label _subtitle = null!;
    private CampSession? _session;
    private bool _voiced;

    public override void _Ready()
    {
        if (Script.Paragraphs.Count == 0) Script = VoiceScript.Load();
        _player = new AudioStreamPlayer { Name = "June", Bus = AudioDirector.BUS_VOICE };
        AddChild(_player);
        _layer = new CanvasLayer { Name = "Subtitles", Layer = 5 };
        AddChild(_layer);
        // The kit's serif at body size, paper with a soft shadow over the world.
        _subtitle = UiTheme.label("", UiTheme.BODY, SubtitleColor, shadow: true);
        _subtitle.Theme = UiTheme.Kit;
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _subtitle.VerticalAlignment = VerticalAlignment.Bottom;
        _subtitle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _subtitle.GrowHorizontal = Control.GrowDirection.Both;
        _subtitle.GrowVertical = Control.GrowDirection.Begin;
        _subtitle.CustomMinimumSize = new Vector2(1100, 0);
        _subtitle.OffsetLeft = -550;
        _subtitle.OffsetRight = 550;
        _subtitle.OffsetBottom = -72;
        _subtitle.Visible = false;
        _layer.AddChild(_subtitle);
        Attach(Game.Instance?.session);
    }

    /// Listens to a session's story lines. Called again lazily if the session starts later.
    public void Attach(CampSession? session)
    {
        if (session == null || ReferenceEquals(session, _session)) return;
        if (_session != null) _session.LinePlayed -= OnLine;
        _session = session;
        _session.LinePlayed += OnLine;
    }

    private void OnLine(string id, LineKind kind, string variant) => Enqueue(id, variant);

    /// Queues every paragraph of a line; lines that arrive while June is speaking wait their turn.
    public void Enqueue(string line, string variant)
    {
        List<VoiceParagraph> paragraphs = Script.For(line, variant);
        if (paragraphs.Count == 0)
        {
            GD.PushWarning($"VoicePlayer: no paragraphs for line '{line}'");
            return;
        }
        int voiced = paragraphs.Count(p => ResourceLoader.Exists(Script.PathOf(p)));
        GD.Print($"VOICE_LINE id={line} paragraphs={paragraphs.Count} voiced={voiced}{(variant.Length > 0 ? " variant=" + variant : "")}");
        for (int i = 0; i < paragraphs.Count; i++) Pending.Enqueue((paragraphs[i], i == paragraphs.Count - 1));
        if (Current == null && Gap <= 0) Gap = 0.05;
    }

    public bool Speaking => Current != null;

    public override void _Process(double delta)
    {
        if (_session == null) Attach(Game.Instance?.session);
        Step(delta);
        // The subtitles setting applies at once, also to a line already showing.
        if (_subtitle != null && Current != null) _subtitle.Visible = GameSettings.Current.Subtitles && !SubtitlesHeld;
    }

    /// Advances the line playback by `delta` seconds (the tree's pause stops it).
    public void Step(double delta)
    {
        if (Current != null)
        {
            Remaining -= delta;
            bool done = _voiced ? !_player.Playing && Remaining <= 0 : Remaining <= 0;
            if (!done) return;
            Finish();
        }
        if (Pending.Count == 0) return;
        Gap -= delta;
        if (Gap > 0) return;
        Begin(Pending.Dequeue());
    }

    private void Begin((VoiceParagraph paragraph, bool last) next)
    {
        Current = next.paragraph;
        CurrentIsLast = next.last;
        string path = Script.PathOf(Current);
        AudioStream? stream = path.Length > 0 && ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as AudioStream : null;
        _voiced = stream != null && _player.IsInsideTree();
        if (_voiced)
        {
            _player.Stream = stream;
            _player.VolumeDb = (float)VoiceScript.GainDb(Current);
            _player.Play();
            // A short floor so a stream that has not started yet is not taken for finished.
            Remaining = 0.2;
            Game.Instance?.audio?.set_voice_ducking(true);
        }
        else
        {
            Remaining = VoiceScript.ReadingSeconds(Current.Text);
        }
        if (_subtitle != null)
        {
            _subtitle.Text = Current.Text;
            _subtitle.Visible = GameSettings.Current.Subtitles && !SubtitlesHeld;
        }
    }

    private void Finish()
    {
        VoiceParagraph finished = Current!;
        if (_voiced) _player.Stop();
        _voiced = false;
        Current = null;
        if (_subtitle != null) _subtitle.Visible = false;
        Gap = ParagraphGap;
        if (Pending.Count == 0) Game.Instance?.audio?.set_voice_ducking(false);
        if (!CurrentIsLast) return;
        Heard.Add(finished.Line);
        _session?.Story.LinesHeard.Add(finished.Line);
        LineHeard?.Invoke(finished.Line);
    }

    public override void _ExitTree()
    {
        if (_session != null) _session.LinePlayed -= OnLine;
        _session = null;
        Game.Instance?.audio?.set_voice_ducking(false);
    }
}
