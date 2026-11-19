using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace LastCamp.Story;

/// The one story slot, `user://save/story.json`. Writes go to a temporary file
/// that is flushed, read back and then renamed over the slot, so a failed or
/// interrupted write leaves the previous slot as it was. A file that does not
/// parse, has the wrong version or misses a field is rejected and left alone.
public sealed class StorySave
{
    public const int Version = 1;
    public const string DefaultDirectory = "user://save";
    public const string RecordsPath = "user://camp-records.cfg";

    public readonly string Directory;
    public string SlotPath => Directory.TrimEnd('/') + "/story.json";
    public string TempPath => SlotPath + ".tmp";
    /// The last load or save problem, for the player-facing report.
    public string LastError { get; private set; } = "";

    public StorySave(string directory = DefaultDirectory) => Directory = directory;

    private static string Real(string path) => path.StartsWith("user://", StringComparison.Ordinal) || path.StartsWith("res://", StringComparison.Ordinal)
        ? ProjectSettings.GlobalizePath(path) : path;

    public bool Exists => File.Exists(Real(SlotPath));

    /// Writes the state. Returns false, keeps the old slot and sets `LastError` on failure.
    public bool Save(StoryState state)
    {
        string text = Serialize(state);
        string temp = Real(TempPath), slot = Real(SlotPath);
        try
        {
            System.IO.Directory.CreateDirectory(Real(Directory));
            using (var stream = new FileStream(temp, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // Never rename something that would not load back.
            if (Parse(File.ReadAllText(temp, Encoding.UTF8), out _) is null)
                throw new IOException("the written story did not read back");
            File.Move(temp, slot, true);
            LastError = "";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastError = $"Could not save the story: {error.Message}";
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    /// Reads the slot. Returns null and sets `LastError` when it is missing,
    /// corrupt, partial or from an unknown version; the file is not touched.
    public StoryState? Load()
    {
        string slot = Real(SlotPath);
        if (!File.Exists(slot))
        {
            LastError = "";
            return null;
        }
        string text;
        try { text = File.ReadAllText(slot, Encoding.UTF8); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not read the story save: {error.Message}";
            return null;
        }
        StoryState? state = Parse(text, out string problem);
        LastError = state == null ? $"The story save was not used: {problem}" : "";
        return state;
    }

    /// Copies a slot that failed to load beside it (`story.rejected-<stamp>.json`)
    /// before a fresh story can be saved over it. Returns the copy's path, or "".
    public string PreserveRejected()
    {
        string slot = Real(SlotPath);
        if (!File.Exists(slot)) return "";
        string copy = Real(Directory.TrimEnd('/') + $"/story.rejected-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        try
        {
            File.Copy(slot, copy, false);
            return copy;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not keep the unreadable story save: {error.Message}";
            return "";
        }
    }

    /// Reads the old stone-skipping record once into the story state.
    public static bool MigrateRecords(StoryState state, string recordsPath = RecordsPath)
    {
        if (state.RecordsMigrated) return false;
        state.RecordsMigrated = true;
        using var records = new ConfigFile();
        if (records.Load(recordsPath) != Error.Ok) return false;
        int best = Math.Clamp(records.GetValue("pond", "best_skips", 0).AsInt32(), 0, StoneFlight.MaxSkips);
        state.BestSkips = Math.Max(state.BestSkips, best);
        return true;
    }

    public static string Serialize(StoryState s)
    {
        WorldSnapshot w = s.World;
        var objectives = new JsonObject();
        foreach ((Chapter chapter, Band band) in StoryScript.AllBands())
            foreach (Objective objective in band.Objectives)
                objectives[StoryState.Key(chapter, band, objective)] = s.ProgressOf(chapter, band, objective);
        var root = new JsonObject
        {
            ["version"] = Version,
            ["chapter"] = s.Chapter.Id,
            ["band"] = s.Band.Id,
            ["finished"] = s.Finished,
            ["objectives"] = objectives,
            ["facts"] = new JsonArray(s.Facts.OrderBy(f => f, StringComparer.Ordinal).Select(f => (JsonNode)f).ToArray()),
            ["clock"] = w.Clock,
            ["band_seconds"] = s.BandSeconds,
            ["weather"] = new JsonObject
            {
                ["chapter"] = w.WeatherChapter, ["seconds"] = w.WeatherSeconds, ["duration"] = w.WeatherDuration,
                ["cloud"] = w.Cloud, ["rain"] = w.Rain, ["wet"] = w.Wet,
            },
            ["fire_intensity"] = w.FireIntensity,
            ["items"] = new JsonArray(s.Items.Select(i => (JsonNode)i).ToArray()),
            ["lines_played"] = new JsonArray(s.LinesPlayed.OrderBy(l => l, StringComparer.Ordinal).Select(l => (JsonNode)l).ToArray()),
            ["lines_heard"] = new JsonArray(s.LinesHeard.OrderBy(l => l, StringComparer.Ordinal).Select(l => (JsonNode)l).ToArray()),
            ["storm"] = new JsonObject
            {
                ["outcome"] = s.StormOutcome,
                ["min_fire"] = double.IsPositiveInfinity(s.StormMinFire) ? -1 : s.StormMinFire,
                ["shelter_seconds"] = s.ShelterSeconds,
                ["clearing_seconds"] = s.ClearingElapsed,
            },
            ["position"] = w.HasPosition ? new JsonObject { ["x"] = w.X, ["y"] = w.Y, ["z"] = w.Z, ["yaw"] = w.Yaw } : null,
            ["best_skips"] = s.BestSkips,
            ["records_migrated"] = s.RecordsMigrated,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static StoryState? Parse(string text, out string problem)
    {
        problem = "";
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) { problem = "not a JSON object"; return null; }
            int version = Need(root, "version").GetValue<int>();
            if (version != Version) { problem = $"unknown version {version}"; return null; }
            string chapterId = Need(root, "chapter").GetValue<string>();
            string bandId = Need(root, "band").GetValue<string>();
            int chapterIndex = StoryScript.ChapterIndex(chapterId);
            if (chapterIndex < 0) { problem = $"unknown chapter '{chapterId}'"; return null; }
            int bandIndex = Array.FindIndex(StoryScript.Chapters[chapterIndex].Bands, b => b.Id == bandId);
            if (bandIndex < 0) { problem = $"unknown band '{bandId}'"; return null; }
            var s = new StoryState();
            s.SetPosition(chapterIndex, bandIndex);
            s.SetFinished(Need(root, "finished").GetValue<bool>());
            JsonObject objectives = Need(root, "objectives").AsObject();
            foreach ((Chapter chapter, Band band) in StoryScript.AllBands())
                foreach (Objective objective in band.Objectives)
                {
                    string key = StoryState.Key(chapter, band, objective);
                    int value = Need(objectives, key).GetValue<int>();
                    if (value < 0 || value > objective.Count) { problem = $"objective {key} out of range"; return null; }
                    if (value > 0) s.Progress[key] = value;
                }
            foreach (JsonNode? fact in Need(root, "facts").AsArray()) s.Facts.Add(fact!.GetValue<string>());
            WorldSnapshot w = s.World;
            w.Clock = Finite(Need(root, "clock").GetValue<double>(), 0, 24);
            s.BandSeconds = Finite(Need(root, "band_seconds").GetValue<double>(), 0, 1e7);
            JsonObject weather = Need(root, "weather").AsObject();
            w.WeatherChapter = Need(weather, "chapter").GetValue<string>();
            w.WeatherSeconds = Finite(Need(weather, "seconds").GetValue<double>(), 0, 1e7);
            w.WeatherDuration = Finite(Need(weather, "duration").GetValue<double>(), 0, 1e7);
            w.Cloud = Finite(Need(weather, "cloud").GetValue<double>(), 0, 1);
            w.Rain = Finite(Need(weather, "rain").GetValue<double>(), 0, 1);
            w.Wet = Finite(Need(weather, "wet").GetValue<double>(), 0, 1);
            w.FireIntensity = Finite(Need(root, "fire_intensity").GetValue<double>(), 0, Firepit.MAX_INTENSITY);
            foreach (JsonNode? item in Need(root, "items").AsArray()) s.AddItem(item!.GetValue<string>());
            foreach (JsonNode? line in Need(root, "lines_played").AsArray())
            {
                string id = line!.GetValue<string>();
                if (!StoryScript.Lines.Any(l => l.Id == id)) { problem = $"unknown line '{id}'"; return null; }
                s.LinesPlayed.Add(id);
            }
            // Added after the first saves of this version: absent means nothing heard yet.
            if (root["lines_heard"] is JsonArray heard)
                foreach (JsonNode? line in heard)
                {
                    string id = line!.GetValue<string>();
                    if (!StoryScript.Lines.Any(l => l.Id == id)) { problem = $"unknown line '{id}'"; return null; }
                    s.LinesHeard.Add(id);
                }
            JsonObject storm = Need(root, "storm").AsObject();
            s.StormOutcome = Need(storm, "outcome").GetValue<string>();
            if (s.StormOutcome is not ("" or "survived" or "went_out")) { problem = $"unknown storm outcome '{s.StormOutcome}'"; return null; }
            double minFire = Need(storm, "min_fire").GetValue<double>();
            s.StormMinFire = minFire < 0 ? double.PositiveInfinity : Finite(minFire, 0, Firepit.MAX_INTENSITY);
            s.ShelterSeconds = Finite(Need(storm, "shelter_seconds").GetValue<double>(), 0, 1e7);
            s.ClearingElapsed = Finite(Need(storm, "clearing_seconds").GetValue<double>(), -1, 1e7);
            if (!root.ContainsKey("position")) { problem = "missing position"; return null; }
            if (root["position"] is JsonObject position)
            {
                w.HasPosition = true;
                w.X = Finite(Need(position, "x").GetValue<double>(), -1e5, 1e5);
                w.Y = Finite(Need(position, "y").GetValue<double>(), -1e5, 1e5);
                w.Z = Finite(Need(position, "z").GetValue<double>(), -1e5, 1e5);
                w.Yaw = Finite(Need(position, "yaw").GetValue<double>(), -1e3, 1e3);
            }
            s.BestSkips = Math.Clamp(Need(root, "best_skips").GetValue<int>(), 0, StoneFlight.MaxSkips);
            s.RecordsMigrated = Need(root, "records_migrated").GetValue<bool>();
            return s;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or FormatProblem or KeyNotFoundProblem or ArgumentException)
        {
            problem = error.Message;
            return null;
        }
    }

    private sealed class KeyNotFoundProblem(string key) : Exception($"missing field '{key}'");
    private sealed class FormatProblem(string message) : Exception(message);

    private static JsonNode Need(JsonObject node, string key) =>
        node.TryGetPropertyValue(key, out JsonNode? value) && value != null ? value : throw new KeyNotFoundProblem(key);

    private static double Finite(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max ? value : throw new FormatProblem($"value {value} outside {min}..{max}");
}
