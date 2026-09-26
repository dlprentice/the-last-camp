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

/// Deterministic DSP toolbox for runtime sound synthesis.
///
/// Conventions:
/// - A buffer is a [PackedFloat32Array] of samples nominally in [-1, 1].
/// - Generators return a new buffer. Processors take a MONO buffer, modify it in
///   place and return the same buffer so calls can be chained. Stereo is produced
///   as the last step with [method interleave], [method pan], [method widen] or
///   [method stamp_stereo] (interleaved L/R frames).
/// - Every random function takes an explicit seed; equal inputs give identical output.
/// - Frequencies are in Hz and times in seconds unless the name ends in [code]_ms[/code].
public partial class Synth
{
    public const long SAMPLE_RATE = 44100;
    public const double MIN_FILTER_HZ = 10.0;
    /// Fraction of the sample rate above which filter frequencies are clamped.
    public const double MAX_FILTER_RATIO = 0.45;
    /// ln(1000): an exponential decay reaches -60 dB after this many time constants.
    public const double DECAY_60_DB = 6.907755;
    public const double INT16_MAX = 32767.0;
    public const double INT16_SCALE = 32768.0;

    public static long frames(double seconds, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Buffers and measurement
        // ---------------------------------------------------------------------------
        return maxi(0, (long)round(seconds * sample_rate));
    }

    public static List<float> silence(long frame_count)
    {
        List<float> @out = new List<float>();
        G.resize(@out, (int)maxi(0, frame_count));
        return @out;
    }

    public static List<float> constant(long frame_count, double value)
    {
        List<float> @out = silence(frame_count);
        G.fill(@out, (float)value);
        return @out;
    }

    public static double peak(List<float> samples)
    {
        double highest = 0.0;
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            double magnitude = absf(samples[(int)i]);
            if (magnitude > highest)
            {
                highest = magnitude;
            }
        }
        return highest;
    }

    public static double rms(List<float> samples)
    {
        if ((samples.Count == 0))
        {
            return 0.0;
        }
        double total = 0.0;
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            double s = samples[(int)i];
            total += s * s;
        }
        return sqrt(total / (long)samples.Count);
    }

    public static List<float> concat(List<float> a, List<float> b)
    {
        List<float> @out = new List<float>(a);
        @out.AddRange(b);
        return @out;
    }

    public static List<float> gain(List<float> samples, double linear)
    {
        // ---------------------------------------------------------------------------
        // In-place arithmetic
        // ---------------------------------------------------------------------------
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            samples[(int)i] = (float)(samples[(int)i] * linear);
        }
        return samples;
    }

    public static List<float> offset(List<float> samples, double amount)
    {
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            samples[(int)i] = (float)(samples[(int)i] + amount);
        }
        return samples;
    }

    public static List<float> power(List<float> samples, double exponent)
    {
        /// Raises every sample to [param exponent]; intended for shaping positive
        /// control curves (e.g. making a random walk sparser).
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            samples[(int)i] = (float)pow(maxf(samples[(int)i], 0.0), exponent);
        }
        return samples;
    }

    public static List<float> multiply(List<float> samples, List<float> other)
    {
        /// Multiplies [param samples] by [param other] sample-wise (envelope/AM). Any
        /// samples beyond the end of [param other] are left untouched.
        long count = mini((long)samples.Count, (long)other.Count);
        for (long i = 0; i < count; i++)
        {
            samples[(int)i] = (float)((double)samples[(int)i] * other[(int)i]);
        }
        return samples;
    }

    public static void mix_into(List<float> dest, List<float> source, long frame_offset = 0, double level = 1.0, bool wrap_around = false)
    {
        /// Adds [param source] into [param dest] starting at [param frame_offset].
        /// With [param wrap_around] the write wraps past the end of [param dest], which
        /// keeps loops seamless when a grain straddles the loop point.
        long dest_size = (long)dest.Count;
        if (dest_size == 0)
        {
            return;
        }
        if (wrap_around)
        {
            for (long i = 0, i_end = (long)source.Count; i < i_end; i++)
            {
                dest[(int)posmod(frame_offset + i, dest_size)] = (float)(dest[(int)posmod(frame_offset + i, dest_size)] + source[(int)i] * level);
            }
            return;
        }
        long first = maxi(0, -frame_offset);
        long last = mini((long)source.Count, dest_size - frame_offset);
        for (long i2 = first; i2 < last; i2++)
        {
            dest[(int)(frame_offset + i2)] = (float)(dest[(int)(frame_offset + i2)] + source[(int)i2] * level);
        }
    }

    public static List<float> normalize(List<float> samples, double target_peak = 0.9)
    {
        double current = peak(samples);
        if (current < 1e-9)
        {
            return samples;
        }
        return gain(samples, target_peak / current);
    }

    public static List<float> soft_clip(List<float> samples, double drive = 1.0)
    {
        /// tanh soft clipper: output is strictly inside (-1, 1) whatever the input level.
        /// [param drive] > 1 pushes the signal harder into saturation; follow with
        /// [method normalize] when a specific peak is wanted.
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            samples[(int)i] = (float)tanh(samples[(int)i] * drive);
        }
        return samples;
    }

    public static List<float> fade_edges(List<float> samples, double fade_in_ms, double fade_out_ms, long channels = 1, long sample_rate = SAMPLE_RATE)
    {
        /// Raised-cosine fades on both ends of a (possibly interleaved) buffer to remove
        /// edge clicks from one-shots.
        long total_frames = _frame_count_of(samples, channels);
        long in_frames = mini(frames(fade_in_ms * 0.001, sample_rate), total_frames);
        long out_frames = mini(frames(fade_out_ms * 0.001, sample_rate), total_frames);
        for (long i = 0; i < in_frames; i++)
        {
            double g = 0.5 - 0.5 * cos(PI * (double)i / in_frames);
            for (long c = 0; c < channels; c++)
            {
                samples[(int)(i * channels + c)] = (float)(samples[(int)(i * channels + c)] * g);
            }
        }
        for (long i2 = 0; i2 < out_frames; i2++)
        {
            double g2 = 0.5 - 0.5 * cos(PI * (double)i2 / out_frames);
            long frame = total_frames - 1 - i2;
            for (long c2 = 0; c2 < channels; c2++)
            {
                samples[(int)(frame * channels + c2)] = (float)(samples[(int)(frame * channels + c2)] * g2);
            }
        }
        return samples;
    }

    public static List<float> make_seamless(List<float> samples, double crossfade_ms, long channels = 1, long sample_rate = SAMPLE_RATE, bool equal_power = true)
    {
        /// Returns a shorter copy of [param samples] whose tail has been crossfaded into
        /// its head so that playing it as a loop produces no discontinuity. The result is
        /// [code]crossfade_ms[/code] shorter than the input. Equal-power fading suits
        /// noisy textures; linear fading suits correlated (tonal) material.
        long total_frames = _frame_count_of(samples, channels);
        long fade_frames = frames(crossfade_ms * 0.001, sample_rate);
        if (fade_frames <= 0 || fade_frames * 2 > total_frames)
        {
            return new List<float>(samples);
        }
        long out_frames = total_frames - fade_frames;
        List<float> @out = G.slice(samples, 0, out_frames * channels);
        long tail_start = out_frames;
        for (long i = 0; i < fade_frames; i++)
        {
            double t = (double)i / fade_frames;
            double fade_in = t;
            double fade_out = 1.0 - t;
            if (equal_power)
            {
                fade_in = sin(t * PI * 0.5);
                fade_out = cos(t * PI * 0.5);
            }
            for (long c = 0; c < channels; c++)
            {
                double head = samples[(int)(i * channels + c)];
                double tail = samples[(int)((tail_start + i) * channels + c)];
                @out[(int)(i * channels + c)] = (float)(head * fade_in + tail * fade_out);
            }
        }
        return @out;
    }

    public static List<float> white_noise(long frame_count, long seed_value)
    {
        // ---------------------------------------------------------------------------
        // Noise
        // ---------------------------------------------------------------------------
        RandomNumberGenerator rng = _rng(seed_value);
        List<float> @out = silence(frame_count);
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)(rng.Randf() * 2.0 - 1.0);
        }
        return @out;
    }

    public static List<float> pink_noise(long frame_count, long seed_value)
    {
        /// 1/f noise using Paul Kellet's refined filter method, normalised to 0.9 peak.
        RandomNumberGenerator rng = _rng(seed_value);
        List<float> @out = silence(frame_count);
        double b0 = 0.0;
        double b1 = 0.0;
        double b2 = 0.0;
        double b3 = 0.0;
        double b4 = 0.0;
        double b5 = 0.0;
        double b6 = 0.0;
        for (long i = 0; i < frame_count; i++)
        {
            double white = rng.Randf() * 2.0 - 1.0;
            b0 = 0.99886 * b0 + white * 0.0555179;
            b1 = 0.99332 * b1 + white * 0.0750759;
            b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856;
            b4 = 0.55000 * b4 + white * 0.5329522;
            b5 = -0.7616 * b5 - white * 0.0168980;
            @out[(int)i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362) * 0.11);
            b6 = white * 0.115926;
        }
        return normalize(@out, 0.9);
    }

    public static List<float> brown_noise(long frame_count, long seed_value, double leak = 0.002)
    {
        /// Leaky-integrated white noise (-6 dB/oct), normalised to 0.9 peak.
        /// [param leak] sets how quickly the integrator forgets; 0.002 gives a ~14 Hz corner.
        RandomNumberGenerator rng = _rng(seed_value);
        List<float> @out = silence(frame_count);
        double keep = 1.0 - clampf(leak, 0.0, 1.0);
        double acc = 0.0;
        for (long i = 0; i < frame_count; i++)
        {
            acc = (acc + 0.02 * (rng.Randf() * 2.0 - 1.0)) * keep;
            @out[(int)i] = (float)acc;
        }
        return normalize(@out, 0.9);
    }

    public static List<float> sine(long frame_count, double frequency, double amplitude = 1.0, double phase = 0.0, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Oscillators
        // ---------------------------------------------------------------------------
        List<float> @out = silence(frame_count);
        double increment = TAU * frequency / sample_rate;
        double current = phase;
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)(sin(current) * amplitude);
            current += increment;
            if (current >= TAU)
            {
                current -= TAU;
            }
        }
        return @out;
    }

    public static List<float> saw(long frame_count, double frequency, double amplitude = 1.0, double phase = 0.0, long sample_rate = SAMPLE_RATE)
    {
        List<float> @out = silence(frame_count);
        double increment = frequency / sample_rate;
        double current = fposmod(phase / TAU, 1.0);
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)((current * 2.0 - 1.0) * amplitude);
            current += increment;
            if (current >= 1.0)
            {
                current -= 1.0;
            }
        }
        return @out;
    }

    public static List<float> triangle(long frame_count, double frequency, double amplitude = 1.0, double phase = 0.0, long sample_rate = SAMPLE_RATE)
    {
        List<float> @out = silence(frame_count);
        double increment = frequency / sample_rate;
        double current = fposmod(phase / TAU, 1.0);
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)((4.0 * absf(current - 0.5) - 1.0) * amplitude);
            current += increment;
            if (current >= 1.0)
            {
                current -= 1.0;
            }
        }
        return @out;
    }

    public static List<float> sine_from_curve(List<float> frequency_curve, double amplitude = 1.0, double phase = 0.0, long sample_rate = SAMPLE_RATE)
    {
        /// Sine whose instantaneous frequency follows [param frequency_curve] (Hz per sample).
        long count = (long)frequency_curve.Count;
        List<float> @out = silence(count);
        double scale = TAU / sample_rate;
        double current = phase;
        for (long i = 0; i < count; i++)
        {
            @out[(int)i] = (float)(sin(current) * amplitude);
            current += frequency_curve[(int)i] * scale;
            if (current >= TAU)
            {
                current -= TAU;
            }
        }
        return @out;
    }

    public static List<float> additive(long frame_count, List<float> frequencies, List<float> amplitudes, long sample_rate = SAMPLE_RATE)
    {
        /// Sum of sine partials. [param frequencies] and [param amplitudes] are paired by index.
        List<float> @out = silence(frame_count);
        long partials = mini((long)frequencies.Count, (long)amplitudes.Count);
        for (long p = 0; p < partials; p++)
        {
            mix_into(@out, sine(frame_count, frequencies[(int)p], amplitudes[(int)p], 0.0, sample_rate));
        }
        return @out;
    }

    public static List<float> adsr(long frame_count, double attack, double decay, double sustain_level, double release, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Envelopes and control curves
        // ---------------------------------------------------------------------------
        /// Linear attack, curved decay to [param sustain_level], hold, curved release to 0.
        /// Segments are scaled down proportionally when they do not fit in [param frame_count].
        List<float> @out = silence(frame_count);
        long a = frames(attack, sample_rate);
        long d = frames(decay, sample_rate);
        long r = frames(release, sample_rate);
        long needed = a + d + r;
        if (needed > frame_count && needed > 0)
        {
            double ratio = (double)frame_count / needed;
            a = (long)(a * ratio);
            d = (long)(d * ratio);
            r = frame_count - a - d;
        }
        long sustain_end = frame_count - r;
        for (long i = 0; i < frame_count; i++)
        {
            double level = sustain_level;
            if (i < a)
            {
                level = (double)i / a;
            }
            else if (i < a + d)
            {
                double t = (double)(i - a) / d;
                level = sustain_level + (1.0 - sustain_level) * (1.0 - t) * (1.0 - t);
            }
            else if (i >= sustain_end)
            {
                double t2 = (double)(i - sustain_end) / maxi(r, 1);
                level = sustain_level * (1.0 - t2) * (1.0 - t2);
            }
            @out[(int)i] = (float)level;
        }
        return @out;
    }

    public static List<float> exp_decay(long frame_count, double decay_seconds, double attack_seconds = 0.0, long sample_rate = SAMPLE_RATE)
    {
        /// Exponential decay reaching -60 dB after [param decay_seconds], with an optional
        /// linear attack ramp so percussive sounds do not start with a click.
        List<float> @out = silence(frame_count);
        long attack_frames = frames(attack_seconds, sample_rate);
        double rate = DECAY_60_DB / (maxf(decay_seconds, 1e-4) * sample_rate);
        for (long i = 0; i < frame_count; i++)
        {
            double level = exp(-(double)maxi(i - attack_frames, 0) * rate);
            if (i < attack_frames)
            {
                level *= (double)i / attack_frames;
            }
            @out[(int)i] = (float)level;
        }
        return @out;
    }

    public static List<float> linear_curve(long frame_count, double start_value, double end_value)
    {
        List<float> @out = silence(frame_count);
        long span = maxi(frame_count - 1, 1);
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)lerpf(start_value, end_value, (double)i / span);
        }
        return @out;
    }

    public static List<float> exp_curve(long frame_count, double start_value, double end_value)
    {
        /// Geometric interpolation; natural for pitch sweeps. Both values must be > 0.
        List<float> @out = silence(frame_count);
        long span = maxi(frame_count - 1, 1);
        double safe_start = maxf(start_value, 1e-6);
        double ratio = maxf(end_value, 1e-6) / safe_start;
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)(safe_start * pow(ratio, (double)i / span));
        }
        return @out;
    }

    public static List<float> hann(long frame_count)
    {
        /// Hann (raised-cosine) window: a smooth bump for grains and swells.
        List<float> @out = silence(frame_count);
        long span = maxi(frame_count - 1, 1);
        for (long i = 0; i < frame_count; i++)
        {
            @out[(int)i] = (float)(0.5 - 0.5 * cos(TAU * (double)i / span));
        }
        return @out;
    }

    public static List<float> lfo(long frame_count, double rate_hz, double @base, double depth, double phase = 0.0, long sample_rate = SAMPLE_RATE)
    {
        /// [code]base + depth * sin(...)[/code] at [param rate_hz]; a slow modulation source.
        return offset(sine(frame_count, rate_hz, depth, phase, sample_rate), @base);
    }

    public static List<float> random_walk(long frame_count, long seed_value, double rate_hz, double min_value, double max_value, long sample_rate = SAMPLE_RATE)
    {
        /// Smooth random control curve: new targets are drawn [param rate_hz] times per
        /// second and joined with smoothstep interpolation.
        RandomNumberGenerator rng = _rng(seed_value);
        List<float> @out = silence(frame_count);
        long segment = maxi(1, (long)(sample_rate / maxf(rate_hz, 1e-3)));
        double current = rng.RandfRange((float)min_value, (float)max_value);
        double next = rng.RandfRange((float)min_value, (float)max_value);
        long position_in_segment = 0;
        for (long i = 0; i < frame_count; i++)
        {
            if (position_in_segment >= segment)
            {
                position_in_segment = 0;
                current = next;
                next = rng.RandfRange((float)min_value, (float)max_value);
            }
            double t = (double)position_in_segment / segment;
            t = t * t * (3.0 - 2.0 * t);
            @out[(int)i] = (float)(current + (next - current) * t);
            position_in_segment += 1;
        }
        return @out;
    }

    public static List<float> one_pole_low_pass(List<float> samples, double cutoff_hz, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Filters (mono, in place)
        // ---------------------------------------------------------------------------
        double alpha = _one_pole_alpha(cutoff_hz, sample_rate);
        double state = 0.0;
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            state += alpha * (samples[(int)i] - state);
            samples[(int)i] = (float)state;
        }
        return samples;
    }

    public static List<float> one_pole_high_pass(List<float> samples, double cutoff_hz, long sample_rate = SAMPLE_RATE)
    {
        double alpha = _one_pole_alpha(cutoff_hz, sample_rate);
        double state = 0.0;
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            double x = samples[(int)i];
            state += alpha * (x - state);
            samples[(int)i] = (float)(x - state);
        }
        return samples;
    }

    public static List<float> biquad(List<float> samples, List<float> coefficients)
    {
        /// Direct form I biquad. [param coefficients] is [b0, b1, b2, a1, a2] with a0 normalised to 1.
        double b0 = coefficients[0];
        double b1 = coefficients[1];
        double b2 = coefficients[2];
        double a1 = coefficients[3];
        double a2 = coefficients[4];
        double x1 = 0.0;
        double x2 = 0.0;
        double y1 = 0.0;
        double y2 = 0.0;
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            double x = samples[(int)i];
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            samples[(int)i] = (float)y;
        }
        return samples;
    }

    public static List<float> low_pass(List<float> samples, double cutoff_hz, double q = 0.7071, long sample_rate = SAMPLE_RATE)
    {
        return biquad(samples, low_pass_coefficients(cutoff_hz, q, sample_rate));
    }

    public static List<float> high_pass(List<float> samples, double cutoff_hz, double q = 0.7071, long sample_rate = SAMPLE_RATE)
    {
        return biquad(samples, high_pass_coefficients(cutoff_hz, q, sample_rate));
    }

    public static List<float> band_pass(List<float> samples, double center_hz, double q = 1.0, long sample_rate = SAMPLE_RATE)
    {
        return biquad(samples, band_pass_coefficients(center_hz, q, sample_rate));
    }

    public static List<float> band_pass_sweep(List<float> samples, List<float> center_curve, double q = 1.0, long sample_rate = SAMPLE_RATE, long block_size = 32)
    {
        /// Band-pass whose centre follows [param center_curve] (Hz per sample). Coefficients
        /// are refreshed every [param block_size] samples, which is inaudible for slow
        /// modulation and several times cheaper than per-sample recomputation.
        long count = (long)samples.Count;
        if (count == 0 || (center_curve.Count == 0))
        {
            return samples;
        }
        long last_curve_index = (long)center_curve.Count - 1;
        double x1 = 0.0;
        double x2 = 0.0;
        double y1 = 0.0;
        double y2 = 0.0;
        long block_start = 0;
        long step = maxi(block_size, 1);
        while (block_start < count)
        {
            long block_end = mini(block_start + step, count);
            List<float> c = band_pass_coefficients(center_curve[(int)mini(block_start, last_curve_index)], q, sample_rate);
            double b0 = c[0];
            double b2 = c[2];
            double a1 = c[3];
            double a2 = c[4];
            for (long i = block_start; i < block_end; i++)
            {
                double x = samples[(int)i];
                double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
                samples[(int)i] = (float)y;
            }
            block_start = block_end;
        }
        return samples;
    }

    public static List<float> low_pass_coefficients(double cutoff_hz, double q = 0.7071, long sample_rate = SAMPLE_RATE)
    {
        double w0 = _angular(cutoff_hz, sample_rate);
        double cos_w0 = cos(w0);
        double alpha = sin(w0) / (2.0 * maxf(q, 1e-3));
        double a0 = 1.0 + alpha;
        return new List<float>(new List<float> { (float)((1.0 - cos_w0) * 0.5 / a0), (float)((1.0 - cos_w0) / a0), (float)((1.0 - cos_w0) * 0.5 / a0), (float)(-2.0 * cos_w0 / a0), (float)((1.0 - alpha) / a0) });
    }

    public static List<float> high_pass_coefficients(double cutoff_hz, double q = 0.7071, long sample_rate = SAMPLE_RATE)
    {
        double w0 = _angular(cutoff_hz, sample_rate);
        double cos_w0 = cos(w0);
        double alpha = sin(w0) / (2.0 * maxf(q, 1e-3));
        double a0 = 1.0 + alpha;
        return new List<float>(new List<float> { (float)((1.0 + cos_w0) * 0.5 / a0), (float)(-(1.0 + cos_w0) / a0), (float)((1.0 + cos_w0) * 0.5 / a0), (float)(-2.0 * cos_w0 / a0), (float)((1.0 - alpha) / a0) });
    }

    public static List<float> band_pass_coefficients(double center_hz, double q = 1.0, long sample_rate = SAMPLE_RATE)
    {
        /// Constant 0 dB peak-gain band-pass (RBJ cookbook).
        double w0 = _angular(center_hz, sample_rate);
        double cos_w0 = cos(w0);
        double alpha = sin(w0) / (2.0 * maxf(q, 1e-3));
        double a0 = 1.0 + alpha;
        return new List<float>(new List<float> { (float)(alpha / a0), 0.0f, (float)(-alpha / a0), (float)(-2.0 * cos_w0 / a0), (float)((1.0 - alpha) / a0) });
    }

    public static List<float> noise_burst(long frame_count, long seed_value, double center_hz, double q, double decay_seconds, double attack_seconds = 0.001, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Composite building blocks
        // ---------------------------------------------------------------------------
        /// Band-passed white noise with an exponential decay: the basis of pops, splashes,
        /// crunches and hisses.
        List<float> @out = band_pass(white_noise(frame_count, seed_value), center_hz, q, sample_rate);
        return multiply(@out, exp_decay(frame_count, decay_seconds, attack_seconds, sample_rate));
    }

    public static List<float> click(long seed_value, double duration_seconds, double high_pass_hz = 2000.0, long sample_rate = SAMPLE_RATE)
    {
        /// Broadband transient a few milliseconds long, high-passed so it reads as a "tick".
        long count = maxi(frames(duration_seconds, sample_rate), 4);
        List<float> @out = high_pass(white_noise(count, seed_value), high_pass_hz, 0.7071, sample_rate);
        return multiply(@out, exp_decay(count, duration_seconds * 0.5, 0.0, sample_rate));
    }

    public static List<float> decaying_tone(long frame_count, double frequency_start, double frequency_end, double decay_seconds, double amplitude = 1.0, double attack_seconds = 0.001, long sample_rate = SAMPLE_RATE)
    {
        /// Sine gliding exponentially from [param frequency_start] to [param frequency_end]
        /// under an exponential decay: thumps, knocks, blips and drips.
        List<float> @out = sine_from_curve(exp_curve(frame_count, frequency_start, frequency_end), amplitude, 0.0, sample_rate);
        return multiply(@out, exp_decay(frame_count, decay_seconds, attack_seconds, sample_rate));
    }

    public static List<float> interleave(List<float> left, List<float> right)
    {
        // ---------------------------------------------------------------------------
        // Stereo
        // ---------------------------------------------------------------------------
        long count = mini((long)left.Count, (long)right.Count);
        List<float> @out = silence(count * 2);
        for (long i = 0; i < count; i++)
        {
            @out[(int)(i * 2)] = left[(int)i];
            @out[(int)(i * 2 + 1)] = right[(int)i];
        }
        return @out;
    }

    public static Vector2 pan_gains(double pan_position)
    {
        /// Constant-power gains for [param pan_position] in [-1 (left), 1 (right)].
        double angle = (clampf(pan_position, -1.0, 1.0) + 1.0) * PI * 0.25;
        return new Vector2((float)cos(angle), (float)sin(angle));
    }

    public static List<float> pan(List<float> mono, double pan_position)
    {
        Vector2 gains = pan_gains(pan_position);
        List<float> @out = silence((long)mono.Count * 2);
        for (long i = 0, i_end = (long)mono.Count; i < i_end; i++)
        {
            double s = mono[(int)i];
            @out[(int)(i * 2)] = (float)(s * gains.X);
            @out[(int)(i * 2 + 1)] = (float)(s * gains.Y);
        }
        return @out;
    }

    public static List<float> widen(List<float> mono, double delay_ms = 12.0, double width = 0.5, long sample_rate = SAMPLE_RATE)
    {
        /// Mid/side widening: the side signal is a wrapped delay of the mono input so the
        /// result stays loop-safe and sums back to mono without colouration.
        long count = (long)mono.Count;
        List<float> @out = silence(count * 2);
        if (count == 0)
        {
            return @out;
        }
        long delay = posmod(frames(delay_ms * 0.001, sample_rate), count);
        double side_gain = clampf(width, 0.0, 1.0);
        double scale = 1.0 / (1.0 + side_gain);
        for (long i = 0; i < count; i++)
        {
            double mid = mono[(int)i];
            double side = mono[(int)posmod(i - delay, count)] * side_gain;
            @out[(int)(i * 2)] = (float)((mid + side) * scale);
            @out[(int)(i * 2 + 1)] = (float)((mid - side) * scale);
        }
        return @out;
    }

    public static void stamp_stereo(List<float> dest_stereo, List<float> mono, long frame_offset, double pan_position, double level = 1.0, bool wrap_around = true)
    {
        /// Adds a mono grain into an interleaved stereo buffer at a pan position.
        long dest_frames = _frame_count_of(dest_stereo, 2);
        if (dest_frames == 0)
        {
            return;
        }
        Vector2 gains = pan_gains(pan_position) * (float)level;
        for (long i = 0, i_end = (long)mono.Count; i < i_end; i++)
        {
            long frame = frame_offset + i;
            if (wrap_around)
            {
                frame = posmod(frame, dest_frames);
            }
            else if (frame < 0 || frame >= dest_frames)
            {
                continue;
            }
            double s = mono[(int)i];
            dest_stereo[(int)(frame * 2)] = (float)(dest_stereo[(int)(frame * 2)] + s * gains.X);
            dest_stereo[(int)(frame * 2 + 1)] = (float)(dest_stereo[(int)(frame * 2 + 1)] + s * gains.Y);
        }
    }

    public static AudioStreamWav encode(List<float> samples, bool stereo, long sample_rate = SAMPLE_RATE)
    {
        // ---------------------------------------------------------------------------
        // Encoding
        // ---------------------------------------------------------------------------
        /// Packs samples into a 16-bit PCM [AudioStreamWAV]. Samples are hard-clamped to
        /// [-1, 1]; callers are expected to have normalised or soft-clipped beforehand.
        List<byte> bytes = new List<byte>();
        G.resize(bytes, (int)((long)samples.Count * 2));
        for (long i = 0, i_end = (long)samples.Count; i < i_end; i++)
        {
            G.encode_s16(bytes, i * 2, (long)(clampf(samples[(int)i], -1.0, 1.0) * INT16_MAX));
        }
        AudioStreamWav stream = new AudioStreamWav();
        stream.Format = AudioStreamWav.FormatEnum.Format16Bits;
        stream.MixRate = (int)sample_rate;
        stream.Stereo = stereo;
        stream.Data = bytes.ToArray();
        return stream;
    }

    public static AudioStreamWav loop_stream(List<float> samples, bool stereo, double crossfade_ms = 0.0, long sample_rate = SAMPLE_RATE, bool equal_power = true)
    {
        /// Encodes a forward-looping stream. When [param crossfade_ms] > 0 the tail is first
        /// blended into the head with [method make_seamless]; pass 0 for material that is
        /// already loop-synchronous or was built with wrap-around stamping.
        long channels = stereo ? 2 : 1;
        List<float> data = samples;
        if (crossfade_ms > 0.0)
        {
            data = make_seamless(samples, crossfade_ms, channels, sample_rate, equal_power);
        }
        AudioStreamWav stream = encode(data, stereo, sample_rate);
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        stream.LoopEnd = (int)_frame_count_of(data, channels);
        return stream;
    }

    public static List<float> decode(AudioStreamWav stream)
    {
        /// Reads a 16-bit [AudioStreamWAV] back into float samples (interleaved if stereo).
        List<byte> bytes = new List<byte>(stream.Data);
        List<float> @out = silence((long)bytes.Count >> 1);
        for (long i = 0, i_end = (long)@out.Count; i < i_end; i++)
        {
            @out[(int)i] = (float)(G.decode_s16(bytes, i * 2) / INT16_SCALE);
        }
        return @out;
    }

    public static long stream_frame_count(AudioStreamWav stream)
    {
        /// Number of frames (samples per channel) held by a 16-bit stream.
        long bytes_per_frame = stream.Stereo ? 4 : 2;
        return (long)stream.Data.Length / bytes_per_frame;
    }

    public static RandomNumberGenerator _rng(long seed_value)
    {
        // ---------------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------------
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        return rng;
    }

    public static long _frame_count_of(List<float> samples, long channels)
    {
        return (long)samples.Count / maxi(channels, 1);
    }

    public static double _angular(double frequency_hz, long sample_rate)
    {
        double clamped = clampf(frequency_hz, MIN_FILTER_HZ, sample_rate * MAX_FILTER_RATIO);
        return TAU * clamped / sample_rate;
    }

    public static double _one_pole_alpha(double cutoff_hz, long sample_rate)
    {
        double clamped = clampf(cutoff_hz, MIN_FILTER_HZ, sample_rate * MAX_FILTER_RATIO);
        return 1.0 - exp(-TAU * clamped / sample_rate);
    }
}
