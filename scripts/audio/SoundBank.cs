using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace LastCamp;

/// The game's sounds as the tracked manifest lists them. The files themselves are
/// licensed ElevenLabs audio that is not in the repository, so any of them can be
/// missing: a missing file is silence, never an error.
public sealed class SoundBank
{
    public const string ManifestPath = "res://audio/generated/manifest.json";
    public const string Root = "res://audio/generated/";

    public sealed record Entry(string Id, string Kind, string[] Files, double[] Lufs)
    {
        public bool IsBed => Kind == "bed";
        public int Variants => Files.Length;
    }

    public readonly Dictionary<string, Entry> Entries = new();
    public readonly string RootPath;
    private readonly Dictionary<string, AudioStream?> _streams = new();

    public SoundBank(string root = Root) => RootPath = root;

    /// Reads the manifest. Without one (or with an unreadable one) the bank is empty
    /// and the game plays silently.
    public static SoundBank Load(string path = ManifestPath, string root = Root)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.Print($"AUDIO_BANK manifest=missing path={path}");
            return new SoundBank(root);
        }
        return Parse(FileAccess.GetFileAsString(path), root);
    }

    public static SoundBank Parse(string json, string root = Root)
    {
        var bank = new SoundBank(root);
        try
        {
            if (JsonNode.Parse(json)?["sounds"] is not JsonObject sounds) return bank;
            foreach ((string id, JsonNode? node) in sounds)
            {
                if (node is not JsonObject entry) continue;
                string kind = entry["kind"]?.GetValue<string>() ?? "oneshot";
                string[] files = entry["files"]?.AsArray().Select(f => f!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
                double[] lufs = entry["lufs"]?.AsArray().Select(l => l!.GetValue<double>()).ToArray() ?? Array.Empty<double>();
                bank.Entries[id] = new Entry(id, kind, files, lufs);
            }
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            GD.PushWarning($"AudioDirector: unreadable sound manifest ({error.Message}); playing silently");
            bank.Entries.Clear();
        }
        return bank;
    }

    public bool Has(string id) => Entries.ContainsKey(id);

    public int Variants(string id) => Entries.TryGetValue(id, out Entry? entry) ? entry.Variants : 0;

    public string PathOf(string id, int variant) =>
        Entries.TryGetValue(id, out Entry? entry) && variant >= 0 && variant < entry.Files.Length ? RootPath + entry.Files[variant] : "";

    /// The measured integrated loudness of one file; the target is used when unmeasured.
    public double Lufs(string id, int variant, double fallback)
    {
        if (!Entries.TryGetValue(id, out Entry? entry) || variant < 0 || variant >= entry.Lufs.Length) return fallback;
        double value = entry.Lufs[variant];
        return double.IsFinite(value) ? value : fallback;
    }

    /// The gain that brings a file from its measured loudness to `target_lufs`.
    public double GainDb(string id, int variant, double target_lufs) =>
        Math.Clamp(target_lufs - Lufs(id, variant, target_lufs), -40.0, 40.0);

    /// The stream for one variant, or null when the file is not present. Beds come
    /// back looping over the whole file: the takes are generated as seamless loops.
    public AudioStream? Stream(string id, int variant)
    {
        string path = PathOf(id, variant);
        if (path.Length == 0) return null;
        if (_streams.TryGetValue(path, out AudioStream? cached)) return cached;
        AudioStream? stream = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as AudioStream : null;
        if (stream != null && Entries[id].IsBed) MakeLoop(stream);
        _streams[path] = stream;
        return stream;
    }

    public static void MakeLoop(AudioStream stream)
    {
        switch (stream)
        {
            case AudioStreamWav wav:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
                break;
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                break;
        }
    }

    /// Every manifest file, as a res:// path.
    public IEnumerable<string> AllFiles() => Entries.Values.SelectMany(e => e.Files.Select(f => RootPath + f));

    public int PresentFiles() => AllFiles().Count(path => ResourceLoader.Exists(path));
}
