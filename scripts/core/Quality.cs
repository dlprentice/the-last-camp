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

    /// Keep the cinematic scene at a minimum 30 fps during ordinary play.
    public const double AUTO_TUNE_LIMIT_MS = 33.3;
    public const double AUTO_TUNE_SAMPLE_SECONDS = 3.0;

    public bool @explicit = false;

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;
        @explicit = Game.Instance.has_flag("quality") || Game.Instance.has_flag("cinematic") || Game.Instance.has_flag("capture");
        string requested = Game.Instance.arg_value("quality", Game.Instance.has_flag("cinematic") || Game.Instance.has_flag("capture") ? "ultra" : "high").ToLowerInvariant();
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
            if (mean <= AUTO_TUNE_LIMIT_MS || current.tier == QualityPreset.Tier.LOW)
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
        if (@event.IsActionPressed("quality_1") || @event.IsActionPressed("quality_2") || @event.IsActionPressed("quality_3") || @event.IsActionPressed("quality_4"))
        {
            @explicit = true;
        }
        if (@event.IsActionPressed("quality_1"))
        {
            apply(QualityPreset.Tier.LOW);
        }
        else if (@event.IsActionPressed("quality_2"))
        {
            apply(QualityPreset.Tier.MEDIUM);
        }
        else if (@event.IsActionPressed("quality_3"))
        {
            apply(QualityPreset.Tier.HIGH);
        }
        else if (@event.IsActionPressed("quality_4"))
        {
            apply(QualityPreset.Tier.ULTRA);
        }
    }

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
        current = movie && tier == QualityPreset.Tier.ULTRA ? QualityPreset.film() : QualityPreset.for_tier(tier);
        _apply_viewport(GetTree().Root, current);
        _apply_renderer(current);
        EmitSignal(SignalName.preset_changed, current);
    }

    public void _apply_viewport(Viewport viewport, QualityPreset p)
    {
        viewport.UseTaa = false;
        viewport.Scaling3DMode = p.upscaler;
        viewport.Scaling3DScale = (float)p.render_scale;
        viewport.FsrSharpness = (float)p.fsr_sharpness;
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
