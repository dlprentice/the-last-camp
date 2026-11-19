using System;
using Godot;

namespace LastCamp;

/// The player's settings, kept in `user://settings.cfg` and applied the moment they
/// change. Tool runs never load or apply them: they keep the defaults, so captures,
/// benchmarks and checks are not changed by anyone's preferences.
public sealed class GameSettings
{
    public const string DefaultPath = "user://settings.cfg";
    public static readonly string[] Presets = { "auto", "low", "medium", "high", "ultra" };
    public const double FovMin = 60, FovMax = 90;
    public const double SensitivityMin = 0.25, SensitivityMax = 3.0;

    /// The settings in use. Defaults until the title screen loads the file.
    public static GameSettings Current { get; set; } = new();

    /// "auto" lets the game step the preset down to hold its frame rate.
    public string Preset = "auto";
    public double Fov = 70;
    public bool HeadBob = true;
    public double Master = 1, Ambience = 1, Effects = 1, Voice = 1;
    public bool Subtitles = true;
    /// Sets June's and Tom's hands in the serif.
    public bool PlainText;
    public double MouseSensitivity = 1;
    public bool InvertY;

    public event Action? Changed;

    public GameSettings Clone() => (GameSettings)MemberwiseClone();

    public static GameSettings Load(string path = DefaultPath)
    {
        var s = new GameSettings();
        var cfg = new ConfigFile();
        if (cfg.Load(path) != Error.Ok) return s;
        string preset = cfg.GetValue("graphics", "preset", s.Preset).AsString();
        s.Preset = Array.IndexOf(Presets, preset) >= 0 ? preset : "auto";
        s.Fov = Math.Clamp(cfg.GetValue("graphics", "fov", s.Fov).AsDouble(), FovMin, FovMax);
        s.HeadBob = cfg.GetValue("graphics", "head_bob", s.HeadBob).AsBool();
        s.Master = Unit(cfg.GetValue("audio", "master", s.Master).AsDouble());
        s.Ambience = Unit(cfg.GetValue("audio", "ambience", s.Ambience).AsDouble());
        s.Effects = Unit(cfg.GetValue("audio", "effects", s.Effects).AsDouble());
        s.Voice = Unit(cfg.GetValue("audio", "voice", s.Voice).AsDouble());
        s.Subtitles = cfg.GetValue("text", "subtitles", s.Subtitles).AsBool();
        s.PlainText = cfg.GetValue("text", "plain_text", s.PlainText).AsBool();
        s.MouseSensitivity = Math.Clamp(cfg.GetValue("controls", "mouse_sensitivity", s.MouseSensitivity).AsDouble(), SensitivityMin, SensitivityMax);
        s.InvertY = cfg.GetValue("controls", "invert_y", s.InvertY).AsBool();
        return s;
    }

    private static double Unit(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 1;

    public Error Save(string path = DefaultPath)
    {
        var cfg = new ConfigFile();
        cfg.SetValue("graphics", "preset", Preset);
        cfg.SetValue("graphics", "fov", Fov);
        cfg.SetValue("graphics", "head_bob", HeadBob);
        cfg.SetValue("audio", "master", Master);
        cfg.SetValue("audio", "ambience", Ambience);
        cfg.SetValue("audio", "effects", Effects);
        cfg.SetValue("audio", "voice", Voice);
        cfg.SetValue("text", "subtitles", Subtitles);
        cfg.SetValue("text", "plain_text", PlainText);
        cfg.SetValue("controls", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("controls", "invert_y", InvertY);
        Error err = cfg.Save(path);
        if (err != Error.Ok) GD.PushWarning($"Settings could not be saved to {path}: {err}");
        return err;
    }

    /// Records a change: applies it, saves it and tells listeners.
    public void Commit(string path = DefaultPath)
    {
        Apply();
        Save(path);
        Changed?.Invoke();
    }

    public static double VolumeDb(double linear) => linear < 1e-4 ? -80.0 : Mathf.LinearToDb(linear);

    /// Applies everything that lives outside the player: preset and audio levels.
    /// The player reads look, field of view and head-bob each frame; the subtitles
    /// and the hands read their settings when they draw.
    public void Apply()
    {
        if (Quality.Instance is Quality quality) ApplyPreset(quality, Game.Instance?.user_args ?? new Godot.Collections.Dictionary());
        AudioDirector.UserAmbienceDb = VolumeDb(Ambience);
        AudioDirector.UserEffectsDb = VolumeDb(Effects);
        AudioDirector.UserVoiceDb = VolumeDb(Voice);
        if (Game.Instance?.audio is AudioDirector audio)
        {
            audio.set_master_volume(Master);
            audio._apply_bus_levels();
        }
        else
        {
            int master = AudioServer.GetBusIndex(AudioDirector.BUS_MASTER);
            if (master >= 0) AudioServer.SetBusVolumeDb(master, (float)VolumeDb(Master));
        }
    }

    /// True when the command line chose the preset (`--quality=...`): it then
    /// wins over the saved one for the whole session, and auto-tune stays off.
    public static bool PresetFromCommandLine(Godot.Collections.Dictionary args) => args.ContainsKey("quality");

    /// Applies the saved preset, unless the command line chose one.
    public void ApplyPreset(Quality quality, Godot.Collections.Dictionary args)
    {
        if (PresetFromCommandLine(args))
        {
            quality.@explicit = true;
            return;
        }
        bool auto = Preset == "auto";
        QualityPreset.Tier tier = Quality.tier_from_name(auto ? "high" : Preset);
        bool wasExplicit = quality.@explicit;
        quality.@explicit = !auto;
        if (quality.current.tier != tier && (!auto || wasExplicit)) quality.apply(tier);
    }

    public string PresetName => Preset switch
    {
        "low" => "Low", "medium" => "Medium", "high" => "High", "ultra" => "Ultra", _ => "Automatic",
    };
}
