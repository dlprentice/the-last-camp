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

/// One complete, immutable rendering configuration. Presets are data; the
/// `Quality` autoload applies them. Budgets were set from the built-in
/// profiler (`--profile`) on an RTX 4060 Laptop at 1080p: shadow-casting
/// point lights and the planar mirror dominate, so those scale first.
public partial class QualityPreset : RefCounted
{
    public enum Tier
    {
        LOW,
        MEDIUM,
        HIGH,
        ULTRA,
    }

    public QualityPreset.Tier tier;
    public string display_name = "";

    // Resolution / anti-aliasing
    public double render_scale = 1.0;
    public Viewport.Scaling3DModeEnum upscaler = Viewport.Scaling3DModeEnum.Bilinear;
    public bool taa = true;
    public double fsr_sharpness = 0.2;

    // Shadows
    public long directional_shadow_size = 4096;
    public double directional_shadow_distance = 120.0;
    public long directional_shadow_splits = 4;
    public RenderingServer.ShadowQuality soft_shadow_quality = RenderingServer.ShadowQuality.SoftHigh;
    public long positional_shadow_atlas = 4096;
    public bool fire_shadows = true;
    /// Layers the fire's omni shadow rasterises; presets drop the small-plant layer.
    public long fire_shadow_casters = 0xFFFFF;
    public bool lantern_shadows = false;

    // Screen-space and GI
    public bool ssao = true;
    public RenderingServer.EnvironmentSsaoQuality ssao_quality = RenderingServer.EnvironmentSsaoQuality.High;
    public bool ssil = true;
    public RenderingServer.EnvironmentSsilQuality ssil_quality = RenderingServer.EnvironmentSsilQuality.High;
    public bool screen_space_half_size = true;
    public bool sdfgi = true;
    public long sdfgi_cascades = 6;
    public RenderingServer.EnvironmentSdfgiRayCount sdfgi_ray_count = RenderingServer.EnvironmentSdfgiRayCount.Count16;
    public bool ssr = true;
    public long ssr_steps = 32;

    // Volumetrics and post
    public bool volumetric_fog = true;
    public long fog_volume_size = 128;
    public long fog_volume_depth = 128;
    public bool glow = true;
    public bool dof = true;
    public bool motion_blur = true;
    public bool lens_effects = true;
    public Sky.RadianceSizeEnum sky_radiance = Sky.RadianceSizeEnum.Size256;

    // World density
    public bool planar_reflections = true;
    public double reflection_scale = 0.45;
    public bool reflection_half_rate = false;
    public double grass_density = 0.9;
    public double grass_distance = 72.0;
    public double foliage_distance = 1.0;
    public double particle_scale = 1.0;
    public double lod_bias = 1.0;
    /// Wooded ridges: generator detail multiplier for the hill trees and whether
    /// the far band (300-655 m, only visible from elevated views or through gaps)
    /// is planted at all. Film and Ultra keep everything; the play presets trim
    /// what no ground-level shot can see. --ridge=full|light|near overrides.
    public double ridge_detail = 1.0;
    public bool ridge_far_band = true;
    public Viewport.AnisotropicFiltering anisotropy = Viewport.AnisotropicFiltering.Anisotropy16X;
    public long parallax_steps = 16;

    public static QualityPreset ultra()
    {
        QualityPreset p = new QualityPreset();
        p.tier = QualityPreset.Tier.ULTRA;
        p.display_name = "Ultra";
        // Sun shadows reach the wooded ridges, so the distant canopy shades
        // itself instead of turning into a pale, flat mass past the near forest.
        p.directional_shadow_distance = 480.0;
        p.ssao_quality = RenderingServer.EnvironmentSsaoQuality.Ultra;
        p.ssil_quality = RenderingServer.EnvironmentSsilQuality.Ultra;
        p.sdfgi_ray_count = RenderingServer.EnvironmentSdfgiRayCount.Count32;
        p.sky_radiance = Sky.RadianceSizeEnum.Size512;
        p.lantern_shadows = true;
        p.grass_density = 1.0;
        p.grass_distance = 300.0;
        p.foliage_distance = 1.25;
        p.lod_bias = 2.0;
        p.parallax_steps = 24;
        p.ssr_steps = 48;
        return p;
    }

    public static QualityPreset film()
    {
        /// Offline capture spends extra GPU time on contact, shadow and fog detail.
        /// Playable Ultra remains available independently of these movie settings.
        QualityPreset p = ultra();
        p.display_name = "Film";
        p.render_scale = 1.5;
        p.directional_shadow_size = 8192;
        p.soft_shadow_quality = RenderingServer.ShadowQuality.SoftUltra;
        p.screen_space_half_size = false;
        p.sdfgi_ray_count = RenderingServer.EnvironmentSdfgiRayCount.Count64;
        p.fog_volume_size = 192;
        p.fog_volume_depth = 192;
        p.reflection_scale = 1.0;
        return p;
    }

    public static QualityPreset high()
    {
        QualityPreset p = new QualityPreset();
        p.tier = QualityPreset.Tier.HIGH;
        p.display_name = "High";
        // Measured on the RTX 4060 Laptop (2026-09-10): the frame is fragment-bound
        // in vegetation, so the atlas and internal resolution are the levers that
        // pay without changing the layout. Ultra keeps the full-size settings.
        p.render_scale = 0.77;
        p.upscaler = Viewport.Scaling3DModeEnum.Fsr2;
        p.directional_shadow_size = 2048;
        // Three cascades to 80 m: the near cascade gets half the atlas instead of a
        // quarter, and the fourth cascade only ever covered the last ten metres.
        p.directional_shadow_distance = 80.0;
        p.directional_shadow_splits = 3;
        p.fire_shadow_casters = 0xFFFFF & ~Pond.GRASS_LAYER;
        p.soft_shadow_quality = RenderingServer.ShadowQuality.SoftMedium;
        p.sdfgi_cascades = 5;
        p.ssr_steps = 24;
        p.fog_volume_size = 96;
        p.fog_volume_depth = 96;
        p.reflection_scale = 0.4;
        p.reflection_half_rate = true;
        p.grass_density = 0.7;
        p.grass_distance = 285.0;
        p.parallax_steps = 10;
        p.ridge_detail = 0.75;
        return p;
    }

    public static QualityPreset medium()
    {
        QualityPreset p = new QualityPreset();
        p.tier = QualityPreset.Tier.MEDIUM;
        p.display_name = "Medium";
        p.render_scale = 0.67;
        p.upscaler = Viewport.Scaling3DModeEnum.Fsr2;
        p.directional_shadow_size = 2048;
        p.directional_shadow_distance = 80.0;
        p.directional_shadow_splits = 3;
        p.fire_shadow_casters = 0xFFFFF & ~Pond.GRASS_LAYER;
        p.soft_shadow_quality = RenderingServer.ShadowQuality.SoftLow;
        p.positional_shadow_atlas = 2048;
        p.ssao_quality = RenderingServer.EnvironmentSsaoQuality.Medium;
        p.ssil = false;
        p.sdfgi_cascades = 4;
        p.sdfgi_ray_count = RenderingServer.EnvironmentSdfgiRayCount.Count8;
        p.ssr = false;
        p.fog_volume_size = 80;
        p.fog_volume_depth = 80;
        p.dof = false;
        p.sky_radiance = Sky.RadianceSizeEnum.Size128;
        p.reflection_scale = 0.3;
        p.reflection_half_rate = true;
        p.grass_density = 0.5;
        p.grass_distance = 260.0;
        p.foliage_distance = 0.8;
        p.ridge_detail = 0.5;
        p.ridge_far_band = false;
        p.particle_scale = 0.7;
        p.anisotropy = Viewport.AnisotropicFiltering.Anisotropy8X;
        p.parallax_steps = 8;
        return p;
    }

    public static QualityPreset low()
    {
        QualityPreset p = new QualityPreset();
        p.tier = QualityPreset.Tier.LOW;
        p.display_name = "Low";
        p.render_scale = 0.5;
        p.upscaler = Viewport.Scaling3DModeEnum.Fsr2;
        p.directional_shadow_size = 2048;
        p.directional_shadow_distance = 55.0;
        p.directional_shadow_splits = 2;
        p.soft_shadow_quality = RenderingServer.ShadowQuality.SoftVeryLow;
        p.positional_shadow_atlas = 1024;
        p.fire_shadows = false;
        p.ssao = true;
        p.ssao_quality = RenderingServer.EnvironmentSsaoQuality.VeryLow;
        p.ssil = false;
        p.sdfgi = false;
        p.ssr = false;
        p.volumetric_fog = true;
        p.fog_volume_size = 64;
        p.fog_volume_depth = 64;
        p.dof = false;
        p.motion_blur = false;
        p.sky_radiance = Sky.RadianceSizeEnum.Size128;
        p.planar_reflections = false;
        p.grass_density = 0.3;
        p.grass_distance = 230.0;
        p.foliage_distance = 0.65;
        p.particle_scale = 0.5;
        p.lod_bias = 0.75;
        p.anisotropy = Viewport.AnisotropicFiltering.Anisotropy4X;
        p.parallax_steps = 0;
        p.ridge_detail = 0.4;
        p.ridge_far_band = false;
        return p;
    }

    public static QualityPreset for_tier(QualityPreset.Tier tier)
    {
        switch (tier)
        {
            case QualityPreset.Tier.LOW:
                return low();
            case QualityPreset.Tier.MEDIUM:
                return medium();
            case QualityPreset.Tier.HIGH:
                return high();
            case QualityPreset.Tier.ULTRA:
                return ultra();
            default:
                G.assert(false, G.format("Unhandled quality tier %s", (long)tier));
                return ultra();
        }
    }
}
