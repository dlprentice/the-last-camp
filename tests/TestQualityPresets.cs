using System;
using Godot;

namespace LastCamp.Tests;

/// The preset contract: a 60 fps default, a capped full-detail High, and the
/// view distance David's direction keeps on High and Ultra.
public partial class TestQualityPresets : TestCase
{
    public void test_default_is_the_uncapped_sixty_fps_preset()
    {
        QualityPreset p = QualityPreset.for_tier(QualityPreset.DEFAULT_TIER);
        assert_eq((long)Quality.tier_from_name(QualityPreset.DEFAULT_TIER_NAME), (long)QualityPreset.DEFAULT_TIER, "the default's name and tier agree");
        assert_eq(p.max_fps, 0L, "the default runs uncapped (vsync paces it)");
        assert_near(Quality.auto_tune_limit_ms(p), 1000.0 / 60 * Quality.AUTO_TUNE_SLACK, 1e-9, "automatic quality holds the default to 60 fps");
    }

    public void test_high_is_full_detail_at_an_even_thirty()
    {
        QualityPreset high = QualityPreset.high();
        assert_eq(high.max_fps, 30L, "High caps at 30 so its pacing is even");
        assert_near(Quality.auto_tune_limit_ms(high), 1000.0 / 30 * Quality.AUTO_TUNE_SLACK, 1e-9, "automatic quality judges High against its cap");
        QualityPreset fallback = QualityPreset.for_tier(QualityPreset.DEFAULT_TIER);
        assert_true(high.render_scale >= fallback.render_scale, "High renders at least the default's resolution");
        assert_true(high.grass_distance >= fallback.grass_distance, "High keeps at least the default's grass range");
        assert_eq(QualityPreset.ultra().max_fps, 0L, "Ultra stays uncapped for faster machines");
        assert_true(QualityPreset.ultra().grass_distance >= high.grass_distance, "Ultra keeps the full grass range");
    }

    public void test_frame_cap_applies_to_play_and_benchmarks_only()
    {
        QualityPreset high = QualityPreset.high();
        assert_eq(Quality.frame_cap(high, false, false, false, false), 30L, "ordinary play takes the preset's cap");
        assert_eq(Quality.frame_cap(high, false, true, true, false), 30L, "a route benchmark measures the pacing a player gets");
        assert_eq(Quality.frame_cap(high, false, true, true, true), 0L, "--uncapped benchmarks run free");
        assert_eq(Quality.frame_cap(high, false, true, false, false), -1L, "other tool runs keep their own frame cap");
        assert_eq(Quality.frame_cap(high, true, false, false, false), -1L, "headless runs keep the headless cap");
    }

    public void test_copy_is_independent_and_complete()
    {
        QualityPreset high = QualityPreset.high();
        QualityPreset copy = high.copy();
        copy.render_scale = 0.1;
        copy.grass_distance_floor = 0.5;
        assert_true(high.render_scale != 0.1 && high.grass_distance_floor != 0.5, "changing the copy leaves the original alone");
        assert_eq(copy.max_fps, high.max_fps, "the copy carries every field");
        assert_eq((long)copy.tier, (long)high.tier, "the copy keeps its tier");
        assert_true(copy.GetInstanceId() != high.GetInstanceId(), "the copy is its own native object");
    }
}
