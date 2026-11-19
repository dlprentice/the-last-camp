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

/// Applies `QualityPreset`s to the engine at runtime.
///
/// Viewport-level settings (scaling, TAA, anisotropy) are applied directly;
/// renderer-wide settings go through the RenderingServer so they take effect
/// without a restart. World systems listen to `preset_changed` for the parts
/// only they can change (environment flags, foliage density, particle counts).
public partial class Quality : Node
{
    public static Quality Instance { get; private set; }
    [Signal]
    public delegate void preset_changedEventHandler(QualityPreset preset);

    public static readonly Godot.Collections.Array<QualityPreset.Tier> TIER_ORDER = new Godot.Collections.Array<QualityPreset.Tier> { QualityPreset.Tier.LOW, QualityPreset.Tier.MEDIUM, QualityPreset.Tier.HIGH, QualityPreset.Tier.ULTRA };

    public QualityPreset current = QualityPreset.ultra();

    /// Automatic quality steps down when the mean frame misses the preset's
    /// own target (its frame cap, else 60 fps) by more than this share.
    public const double AUTO_TUNE_SLACK = 1.15;
    public const double AUTO_TUNE_SAMPLE_SECONDS = 3.0;

    public bool @explicit = false;

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;
        @explicit = Game.Instance.has_flag("quality") || Game.Instance.has_flag("cinematic") || Game.Instance.has_flag("capture");
        string requested = Game.Instance.arg_value("quality", Game.Instance.has_flag("cinematic") || Game.Instance.has_flag("capture") ? "ultra" : QualityPreset.DEFAULT_TIER_NAME).ToLowerInvariant();
        apply(tier_from_name(requested));
    }

    public async void auto_tune()
    {
        /// Measures the frame time for a few seconds and steps the preset down (at
        /// most twice) if the machine cannot hold it. Skipped when the tier was
        /// chosen on the command line.
        if (@explicit)
        {
            return;
        }
        for (long step = 0; step < 2; step++)
        {
            while (Game.Instance.camp != null && Game.Instance.camp.understory != null && Game.Instance.camp.understory._replant_thread != null)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            double total = 0.0;
            long frames = 0;
            double elapsed = 0.0;
            while (elapsed < AUTO_TUNE_SAMPLE_SECONDS)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                double dt = GetProcessDeltaTime();
                elapsed += dt;
                total += dt * 1000.0;
                frames += 1;
            }
            if (@explicit)
            {
                return;
            }
            double mean = total / maxf((double)frames, 1.0);
            if (mean <= auto_tune_limit_ms(current) || current.tier == QualityPreset.Tier.LOW)
            {
                return;
            }
            QualityPreset.Tier lower = next_tier(-1);
            G.print(G.format("Auto quality: %.1f ms/frame at %s, switching to %s", new Godot.Collections.Array { mean, current.display_name, QualityPreset.for_tier(lower).display_name }));
            apply(lower);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(NativeArgs.Name("quality_1")) || @event.IsActionPressed(NativeArgs.Name("quality_2")) || @event.IsActionPressed(NativeArgs.Name("quality_3")) || @event.IsActionPressed(NativeArgs.Name("quality_4")))
        {
            @explicit = true;
        }
        if (@event.IsActionPressed(NativeArgs.Name("quality_1")))
        {
            apply(QualityPreset.Tier.LOW);
        }
        else if (@event.IsActionPressed(NativeArgs.Name("quality_2")))
        {
            apply(QualityPreset.Tier.MEDIUM);
        }
        else if (@event.IsActionPressed(NativeArgs.Name("quality_3")))
        {
            apply(QualityPreset.Tier.HIGH);
        }
        else if (@event.IsActionPressed(NativeArgs.Name("quality_4")))
        {
            apply(QualityPreset.Tier.ULTRA);
        }
    }

    public static double auto_tune_limit_ms(QualityPreset p) => 1000.0 / (p.max_fps > 0 ? p.max_fps : 60) * AUTO_TUNE_SLACK;

    public static QualityPreset.Tier tier_from_name(string name)
    {
        switch (name)
        {
            case "low":
                return QualityPreset.Tier.LOW;
            case "medium":
                return QualityPreset.Tier.MEDIUM;
            case "high":
                return QualityPreset.Tier.HIGH;
            default:
                return QualityPreset.Tier.ULTRA;
        }
    }

    public void apply(QualityPreset.Tier tier)
    {
        bool movie = Game.Instance.has_flag("film-quality") || Game.Instance.has_flag("cinematic") && !Game.Instance.has_flag("capture-quality");
        apply_preset(movie && tier == QualityPreset.Tier.ULTRA ? QualityPreset.film() : QualityPreset.for_tier(tier));
    }

    /// Applies a complete preset object. Profile cases pass modified copies of
    /// the current preset; ordinary play goes through `apply(tier)`.
    public void apply_preset(QualityPreset preset)
    {
        current = preset;
        // Explicit diagnostic overrides also survive in-game quality changes.
        if (Game.Instance.has_flag("sdfgi")) current.sdfgi = true;
        if (Game.Instance.has_flag("no-sdfgi")) current.sdfgi = false;
        // Grass review: `--dense-distant-grass` restores the older 0.18 floor;
        // `--grass-floor=<share>` sets any floor for a side-by-side comparison.
        if (Game.Instance.has_flag("dense-distant-grass")) current.grass_distance_floor = 0.18;
        if (Game.Instance.has_flag("grass-floor") && double.TryParse(Game.Instance.arg_value("grass-floor", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floor))
            current.grass_distance_floor = Math.Clamp(floor, 0.01, 1.0);
        _apply_viewport(Game.Instance.render_viewport ?? GetTree().Root, current);
        _apply_renderer(current);
        _apply_frame_cap(current);
        EmitSignal(SignalName.preset_changed, current);
    }

    /// The preset's frame cap holds in play and in route benchmarks (which
    /// measure the pacing a player gets); `--uncapped` benchmarks run free.
    /// Other tool runs and headless runs keep whatever cap they started with
    /// (-1: leave `Engine.MaxFps` alone).
    public static long frame_cap(QualityPreset p, bool headless, bool tool_run, bool benchmark, bool uncapped)
    {
        if (headless || (tool_run && !benchmark)) return -1;
        return uncapped ? 0 : Math.Max(p.max_fps, 0);
    }

    private void _apply_frame_cap(QualityPreset p)
    {
        long cap = frame_cap(p, DisplayServer.GetName() == "headless", Game.Instance.is_tool_run(), Game.Instance.has_flag("benchmark"), Game.Instance.has_flag("uncapped"));
        if (cap >= 0) Engine.MaxFps = (int)cap;
    }

    public void _apply_viewport(Viewport viewport, QualityPreset p)
    {
        viewport.UseTaa = false;
        viewport.Scaling3DMode = p.upscaler;
        viewport.Scaling3DScale = (float)p.render_scale;
        viewport.FsrSharpness = (float)p.fsr_sharpness;
        // Compensate part of the upscaler's negative mip bias. Fine turf and
        // litter should retain detail without turning into sparkling pixels.
        viewport.TextureMipmapBias = p.upscaler == Viewport.Scaling3DModeEnum.Fsr2 ? 0.35f : 0.0f;
        viewport.UseTaa = p.taa && p.upscaler != Viewport.Scaling3DModeEnum.Fsr2;
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseDebanding = true;
        viewport.AnisotropicFilteringLevel = p.anisotropy;
        viewport.PositionalShadowAtlasSize = (int)p.positional_shadow_atlas;
        viewport.MeshLodThreshold = (float)(1.0 / maxf(p.lod_bias, 0.1));
    }

    public void _apply_renderer(QualityPreset p)
    {
        RenderingServer.DirectionalShadowAtlasSetSize((int)p.directional_shadow_size, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(p.soft_shadow_quality);
        RenderingServer.PositionalSoftShadowFilterSetQuality(p.soft_shadow_quality);
        RenderingServer.EnvironmentSetSsaoQuality(p.ssao_quality, p.screen_space_half_size, 0.5f, 2, 50.0f, 300.0f);
        RenderingServer.EnvironmentSetSsilQuality(p.ssil_quality, p.screen_space_half_size, 0.5f, 4, 50.0f, 300.0f);
        RenderingServer.EnvironmentSetSdfgiRayCount(p.sdfgi_ray_count);
        RenderingServer.EnvironmentSetSdfgiFramesToConverge(RenderingServer.EnvironmentSdfgiFramesToConverge.In10Frames);
        RenderingServer.EnvironmentSetSdfgiFramesToUpdateLight(RenderingServer.EnvironmentSdfgiFramesToUpdateLight.In2Frames);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize((int)p.fog_volume_size, (int)p.fog_volume_depth);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(true);
    }

    public QualityPreset.Tier next_tier(long step)
    {
        long index = (long)TIER_ORDER.IndexOf(current.tier);
        return TIER_ORDER[(int)clampi(index + step, 0, (long)TIER_ORDER.Count - 1)];
    }

    public Quality()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        current = null;
        if (Instance == this) Instance = null;
    }
}
