class_name Synth
extends RefCounted
## Deterministic DSP toolbox for runtime sound synthesis.
##
## Conventions:
## - A buffer is a [PackedFloat32Array] of samples nominally in [-1, 1].
## - Generators return a new buffer. Processors take a MONO buffer, modify it in
##   place and return the same buffer so calls can be chained. Stereo is produced
##   as the last step with [method interleave], [method pan], [method widen] or
##   [method stamp_stereo] (interleaved L/R frames).
## - Every random function takes an explicit seed; equal inputs give identical output.
## - Frequencies are in Hz and times in seconds unless the name ends in [code]_ms[/code].

const SAMPLE_RATE: int = 44100
const MIN_FILTER_HZ := 10.0
## Fraction of the sample rate above which filter frequencies are clamped.
const MAX_FILTER_RATIO := 0.45
## ln(1000): an exponential decay reaches -60 dB after this many time constants.
const DECAY_60_DB := 6.907755
const INT16_MAX := 32767.0
const INT16_SCALE := 32768.0

# ---------------------------------------------------------------------------
# Buffers and measurement
# ---------------------------------------------------------------------------


static func frames(seconds: float, sample_rate: int = SAMPLE_RATE) -> int:
	return maxi(0, int(round(seconds * sample_rate)))


static func silence(frame_count: int) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(maxi(0, frame_count))
	return out


static func constant(frame_count: int, value: float) -> PackedFloat32Array:
	var out := silence(frame_count)
	out.fill(value)
	return out


static func peak(samples: PackedFloat32Array) -> float:
	var highest := 0.0
	for i in samples.size():
		var magnitude: float = absf(samples[i])
		if magnitude > highest:
			highest = magnitude
	return highest


static func rms(samples: PackedFloat32Array) -> float:
	if samples.is_empty():
		return 0.0
	var total := 0.0
	for i in samples.size():
		var s: float = samples[i]
		total += s * s
	return sqrt(total / samples.size())


static func concat(a: PackedFloat32Array, b: PackedFloat32Array) -> PackedFloat32Array:
	var out := a.duplicate()
	out.append_array(b)
	return out


# ---------------------------------------------------------------------------
# In-place arithmetic
# ---------------------------------------------------------------------------


static func gain(samples: PackedFloat32Array, linear: float) -> PackedFloat32Array:
	for i in samples.size():
		samples[i] *= linear
	return samples


static func offset(samples: PackedFloat32Array, amount: float) -> PackedFloat32Array:
	for i in samples.size():
		samples[i] += amount
	return samples


## Raises every sample to [param exponent]; intended for shaping positive
## control curves (e.g. making a random walk sparser).
static func power(samples: PackedFloat32Array, exponent: float) -> PackedFloat32Array:
	for i in samples.size():
		samples[i] = pow(maxf(samples[i], 0.0), exponent)
	return samples


## Multiplies [param samples] by [param other] sample-wise (envelope/AM). Any
## samples beyond the end of [param other] are left untouched.
static func multiply(samples: PackedFloat32Array, other: PackedFloat32Array) -> PackedFloat32Array:
	var count := mini(samples.size(), other.size())
	for i in count:
		samples[i] *= other[i]
	return samples


## Adds [param source] into [param dest] starting at [param frame_offset].
## With [param wrap_around] the write wraps past the end of [param dest], which
## keeps loops seamless when a grain straddles the loop point.
static func mix_into(dest: PackedFloat32Array, source: PackedFloat32Array, frame_offset: int = 0, level: float = 1.0, wrap_around: bool = false) -> void:
	var dest_size := dest.size()
	if dest_size == 0:
		return
	if wrap_around:
		for i in source.size():
			dest[posmod(frame_offset + i, dest_size)] += source[i] * level
		return
	var first := maxi(0, -frame_offset)
	var last := mini(source.size(), dest_size - frame_offset)
	for i in range(first, last):
		dest[frame_offset + i] += source[i] * level


static func normalize(samples: PackedFloat32Array, target_peak: float = 0.9) -> PackedFloat32Array:
	var current := peak(samples)
	if current < 1e-9:
		return samples
	return gain(samples, target_peak / current)


## tanh soft clipper: output is strictly inside (-1, 1) whatever the input level.
## [param drive] > 1 pushes the signal harder into saturation; follow with
## [method normalize] when a specific peak is wanted.
static func soft_clip(samples: PackedFloat32Array, drive: float = 1.0) -> PackedFloat32Array:
	for i in samples.size():
		samples[i] = tanh(samples[i] * drive)
	return samples


## Raised-cosine fades on both ends of a (possibly interleaved) buffer to remove
## edge clicks from one-shots.
static func fade_edges(samples: PackedFloat32Array, fade_in_ms: float, fade_out_ms: float, channels: int = 1, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var total_frames := _frame_count_of(samples, channels)
	var in_frames := mini(frames(fade_in_ms * 0.001, sample_rate), total_frames)
	var out_frames := mini(frames(fade_out_ms * 0.001, sample_rate), total_frames)
	for i in in_frames:
		var g := 0.5 - 0.5 * cos(PI * float(i) / in_frames)
		for c in channels:
			samples[i * channels + c] *= g
	for i in out_frames:
		var g := 0.5 - 0.5 * cos(PI * float(i) / out_frames)
		var frame := total_frames - 1 - i
		for c in channels:
			samples[frame * channels + c] *= g
	return samples


## Returns a shorter copy of [param samples] whose tail has been crossfaded into
## its head so that playing it as a loop produces no discontinuity. The result is
## [code]crossfade_ms[/code] shorter than the input. Equal-power fading suits
## noisy textures; linear fading suits correlated (tonal) material.
static func make_seamless(samples: PackedFloat32Array, crossfade_ms: float, channels: int = 1, sample_rate: int = SAMPLE_RATE, equal_power: bool = true) -> PackedFloat32Array:
	var total_frames := _frame_count_of(samples, channels)
	var fade_frames := frames(crossfade_ms * 0.001, sample_rate)
	if fade_frames <= 0 or fade_frames * 2 > total_frames:
		return samples.duplicate()
	var out_frames := total_frames - fade_frames
	var out := samples.slice(0, out_frames * channels)
	var tail_start := out_frames
	for i in fade_frames:
		var t := float(i) / fade_frames
		var fade_in := t
		var fade_out := 1.0 - t
		if equal_power:
			fade_in = sin(t * PI * 0.5)
			fade_out = cos(t * PI * 0.5)
		for c in channels:
			var head: float = samples[i * channels + c]
			var tail: float = samples[(tail_start + i) * channels + c]
			out[i * channels + c] = head * fade_in + tail * fade_out
	return out


# ---------------------------------------------------------------------------
# Noise
# ---------------------------------------------------------------------------


static func white_noise(frame_count: int, seed_value: int) -> PackedFloat32Array:
	var rng := _rng(seed_value)
	var out := silence(frame_count)
	for i in frame_count:
		out[i] = rng.randf() * 2.0 - 1.0
	return out


## 1/f noise using Paul Kellet's refined filter method, normalised to 0.9 peak.
static func pink_noise(frame_count: int, seed_value: int) -> PackedFloat32Array:
	var rng := _rng(seed_value)
	var out := silence(frame_count)
	var b0 := 0.0
	var b1 := 0.0
	var b2 := 0.0
	var b3 := 0.0
	var b4 := 0.0
	var b5 := 0.0
	var b6 := 0.0
	for i in frame_count:
		var white := rng.randf() * 2.0 - 1.0
		b0 = 0.99886 * b0 + white * 0.0555179
		b1 = 0.99332 * b1 + white * 0.0750759
		b2 = 0.96900 * b2 + white * 0.1538520
		b3 = 0.86650 * b3 + white * 0.3104856
		b4 = 0.55000 * b4 + white * 0.5329522
		b5 = -0.7616 * b5 - white * 0.0168980
		out[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362) * 0.11
		b6 = white * 0.115926
	return normalize(out, 0.9)


## Leaky-integrated white noise (-6 dB/oct), normalised to 0.9 peak.
## [param leak] sets how quickly the integrator forgets; 0.002 gives a ~14 Hz corner.
static func brown_noise(frame_count: int, seed_value: int, leak: float = 0.002) -> PackedFloat32Array:
	var rng := _rng(seed_value)
	var out := silence(frame_count)
	var keep := 1.0 - clampf(leak, 0.0, 1.0)
	var acc := 0.0
	for i in frame_count:
		acc = (acc + 0.02 * (rng.randf() * 2.0 - 1.0)) * keep
		out[i] = acc
	return normalize(out, 0.9)


# ---------------------------------------------------------------------------
# Oscillators
# ---------------------------------------------------------------------------


static func sine(frame_count: int, frequency: float, amplitude: float = 1.0, phase: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var increment := TAU * frequency / sample_rate
	var current := phase
	for i in frame_count:
		out[i] = sin(current) * amplitude
		current += increment
		if current >= TAU:
			current -= TAU
	return out


static func saw(frame_count: int, frequency: float, amplitude: float = 1.0, phase: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var increment := frequency / sample_rate
	var current := fposmod(phase / TAU, 1.0)
	for i in frame_count:
		out[i] = (current * 2.0 - 1.0) * amplitude
		current += increment
		if current >= 1.0:
			current -= 1.0
	return out


static func triangle(frame_count: int, frequency: float, amplitude: float = 1.0, phase: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var increment := frequency / sample_rate
	var current := fposmod(phase / TAU, 1.0)
	for i in frame_count:
		out[i] = (4.0 * absf(current - 0.5) - 1.0) * amplitude
		current += increment
		if current >= 1.0:
			current -= 1.0
	return out


## Sine whose instantaneous frequency follows [param frequency_curve] (Hz per sample).
static func sine_from_curve(frequency_curve: PackedFloat32Array, amplitude: float = 1.0, phase: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var count := frequency_curve.size()
	var out := silence(count)
	var scale := TAU / sample_rate
	var current := phase
	for i in count:
		out[i] = sin(current) * amplitude
		current += frequency_curve[i] * scale
		if current >= TAU:
			current -= TAU
	return out


## Sum of sine partials. [param frequencies] and [param amplitudes] are paired by index.
static func additive(frame_count: int, frequencies: PackedFloat32Array, amplitudes: PackedFloat32Array, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var partials := mini(frequencies.size(), amplitudes.size())
	for p in partials:
		mix_into(out, sine(frame_count, frequencies[p], amplitudes[p], 0.0, sample_rate))
	return out


# ---------------------------------------------------------------------------
# Envelopes and control curves
# ---------------------------------------------------------------------------


## Linear attack, curved decay to [param sustain_level], hold, curved release to 0.
## Segments are scaled down proportionally when they do not fit in [param frame_count].
static func adsr(frame_count: int, attack: float, decay: float, sustain_level: float, release: float, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var a := frames(attack, sample_rate)
	var d := frames(decay, sample_rate)
	var r := frames(release, sample_rate)
	var needed := a + d + r
	if needed > frame_count and needed > 0:
		var ratio := float(frame_count) / needed
		a = int(a * ratio)
		d = int(d * ratio)
		r = frame_count - a - d
	var sustain_end := frame_count - r
	for i in frame_count:
		var level := sustain_level
		if i < a:
			level = float(i) / a
		elif i < a + d:
			var t := float(i - a) / d
			level = sustain_level + (1.0 - sustain_level) * (1.0 - t) * (1.0 - t)
		elif i >= sustain_end:
			var t := float(i - sustain_end) / maxi(r, 1)
			level = sustain_level * (1.0 - t) * (1.0 - t)
		out[i] = level
	return out


## Exponential decay reaching -60 dB after [param decay_seconds], with an optional
## linear attack ramp so percussive sounds do not start with a click.
static func exp_decay(frame_count: int, decay_seconds: float, attack_seconds: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := silence(frame_count)
	var attack_frames := frames(attack_seconds, sample_rate)
	var rate := DECAY_60_DB / (maxf(decay_seconds, 1e-4) * sample_rate)
	for i in frame_count:
		var level := exp(-float(maxi(i - attack_frames, 0)) * rate)
		if i < attack_frames:
			level *= float(i) / attack_frames
		out[i] = level
	return out


static func linear_curve(frame_count: int, start_value: float, end_value: float) -> PackedFloat32Array:
	var out := silence(frame_count)
	var span := maxi(frame_count - 1, 1)
	for i in frame_count:
		out[i] = lerpf(start_value, end_value, float(i) / span)
	return out


## Geometric interpolation; natural for pitch sweeps. Both values must be > 0.
static func exp_curve(frame_count: int, start_value: float, end_value: float) -> PackedFloat32Array:
	var out := silence(frame_count)
	var span := maxi(frame_count - 1, 1)
	var safe_start := maxf(start_value, 1e-6)
	var ratio := maxf(end_value, 1e-6) / safe_start
	for i in frame_count:
		out[i] = safe_start * pow(ratio, float(i) / span)
	return out


## Hann (raised-cosine) window: a smooth bump for grains and swells.
static func hann(frame_count: int) -> PackedFloat32Array:
	var out := silence(frame_count)
	var span := maxi(frame_count - 1, 1)
	for i in frame_count:
		out[i] = 0.5 - 0.5 * cos(TAU * float(i) / span)
	return out


## [code]base + depth * sin(...)[/code] at [param rate_hz]; a slow modulation source.
static func lfo(frame_count: int, rate_hz: float, base: float, depth: float, phase: float = 0.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	return offset(sine(frame_count, rate_hz, depth, phase, sample_rate), base)


## Smooth random control curve: new targets are drawn [param rate_hz] times per
## second and joined with smoothstep interpolation.
static func random_walk(frame_count: int, seed_value: int, rate_hz: float, min_value: float, max_value: float, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var rng := _rng(seed_value)
	var out := silence(frame_count)
	var segment := maxi(1, int(sample_rate / maxf(rate_hz, 1e-3)))
	var current := rng.randf_range(min_value, max_value)
	var next := rng.randf_range(min_value, max_value)
	var position_in_segment := 0
	for i in frame_count:
		if position_in_segment >= segment:
			position_in_segment = 0
			current = next
			next = rng.randf_range(min_value, max_value)
		var t := float(position_in_segment) / segment
		t = t * t * (3.0 - 2.0 * t)
		out[i] = current + (next - current) * t
		position_in_segment += 1
	return out


# ---------------------------------------------------------------------------
# Filters (mono, in place)
# ---------------------------------------------------------------------------


static func one_pole_low_pass(samples: PackedFloat32Array, cutoff_hz: float, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var alpha := _one_pole_alpha(cutoff_hz, sample_rate)
	var state := 0.0
	for i in samples.size():
		state += alpha * (samples[i] - state)
		samples[i] = state
	return samples


static func one_pole_high_pass(samples: PackedFloat32Array, cutoff_hz: float, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var alpha := _one_pole_alpha(cutoff_hz, sample_rate)
	var state := 0.0
	for i in samples.size():
		var x: float = samples[i]
		state += alpha * (x - state)
		samples[i] = x - state
	return samples


## Direct form I biquad. [param coefficients] is [b0, b1, b2, a1, a2] with a0 normalised to 1.
static func biquad(samples: PackedFloat32Array, coefficients: PackedFloat32Array) -> PackedFloat32Array:
	var b0: float = coefficients[0]
	var b1: float = coefficients[1]
	var b2: float = coefficients[2]
	var a1: float = coefficients[3]
	var a2: float = coefficients[4]
	var x1 := 0.0
	var x2 := 0.0
	var y1 := 0.0
	var y2 := 0.0
	for i in samples.size():
		var x: float = samples[i]
		var y := b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
		x2 = x1
		x1 = x
		y2 = y1
		y1 = y
		samples[i] = y
	return samples


static func low_pass(samples: PackedFloat32Array, cutoff_hz: float, q: float = 0.7071, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	return biquad(samples, low_pass_coefficients(cutoff_hz, q, sample_rate))


static func high_pass(samples: PackedFloat32Array, cutoff_hz: float, q: float = 0.7071, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	return biquad(samples, high_pass_coefficients(cutoff_hz, q, sample_rate))


static func band_pass(samples: PackedFloat32Array, center_hz: float, q: float = 1.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	return biquad(samples, band_pass_coefficients(center_hz, q, sample_rate))


## Band-pass whose centre follows [param center_curve] (Hz per sample). Coefficients
## are refreshed every [param block_size] samples, which is inaudible for slow
## modulation and several times cheaper than per-sample recomputation.
static func band_pass_sweep(samples: PackedFloat32Array, center_curve: PackedFloat32Array, q: float = 1.0, sample_rate: int = SAMPLE_RATE, block_size: int = 32) -> PackedFloat32Array:
	var count := samples.size()
	if count == 0 or center_curve.is_empty():
		return samples
	var last_curve_index := center_curve.size() - 1
	var x1 := 0.0
	var x2 := 0.0
	var y1 := 0.0
	var y2 := 0.0
	var block_start := 0
	var step := maxi(block_size, 1)
	while block_start < count:
		var block_end := mini(block_start + step, count)
		var c := band_pass_coefficients(center_curve[mini(block_start, last_curve_index)], q, sample_rate)
		var b0: float = c[0]
		var b2: float = c[2]
		var a1: float = c[3]
		var a2: float = c[4]
		for i in range(block_start, block_end):
			var x: float = samples[i]
			var y := b0 * x + b2 * x2 - a1 * y1 - a2 * y2
			x2 = x1
			x1 = x
			y2 = y1
			y1 = y
			samples[i] = y
		block_start = block_end
	return samples


static func low_pass_coefficients(cutoff_hz: float, q: float = 0.7071, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var w0 := _angular(cutoff_hz, sample_rate)
	var cos_w0 := cos(w0)
	var alpha := sin(w0) / (2.0 * maxf(q, 1e-3))
	var a0 := 1.0 + alpha
	return PackedFloat32Array([
		(1.0 - cos_w0) * 0.5 / a0,
		(1.0 - cos_w0) / a0,
		(1.0 - cos_w0) * 0.5 / a0,
		-2.0 * cos_w0 / a0,
		(1.0 - alpha) / a0,
	])


static func high_pass_coefficients(cutoff_hz: float, q: float = 0.7071, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var w0 := _angular(cutoff_hz, sample_rate)
	var cos_w0 := cos(w0)
	var alpha := sin(w0) / (2.0 * maxf(q, 1e-3))
	var a0 := 1.0 + alpha
	return PackedFloat32Array([
		(1.0 + cos_w0) * 0.5 / a0,
		-(1.0 + cos_w0) / a0,
		(1.0 + cos_w0) * 0.5 / a0,
		-2.0 * cos_w0 / a0,
		(1.0 - alpha) / a0,
	])


## Constant 0 dB peak-gain band-pass (RBJ cookbook).
static func band_pass_coefficients(center_hz: float, q: float = 1.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var w0 := _angular(center_hz, sample_rate)
	var cos_w0 := cos(w0)
	var alpha := sin(w0) / (2.0 * maxf(q, 1e-3))
	var a0 := 1.0 + alpha
	return PackedFloat32Array([
		alpha / a0,
		0.0,
		-alpha / a0,
		-2.0 * cos_w0 / a0,
		(1.0 - alpha) / a0,
	])


# ---------------------------------------------------------------------------
# Composite building blocks
# ---------------------------------------------------------------------------


## Band-passed white noise with an exponential decay: the basis of pops, splashes,
## crunches and hisses.
static func noise_burst(frame_count: int, seed_value: int, center_hz: float, q: float, decay_seconds: float, attack_seconds: float = 0.001, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := band_pass(white_noise(frame_count, seed_value), center_hz, q, sample_rate)
	return multiply(out, exp_decay(frame_count, decay_seconds, attack_seconds, sample_rate))


## Broadband transient a few milliseconds long, high-passed so it reads as a "tick".
static func click(seed_value: int, duration_seconds: float, high_pass_hz: float = 2000.0, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var count := maxi(frames(duration_seconds, sample_rate), 4)
	var out := high_pass(white_noise(count, seed_value), high_pass_hz, 0.7071, sample_rate)
	return multiply(out, exp_decay(count, duration_seconds * 0.5, 0.0, sample_rate))


## Sine gliding exponentially from [param frequency_start] to [param frequency_end]
## under an exponential decay: thumps, knocks, blips and drips.
static func decaying_tone(frame_count: int, frequency_start: float, frequency_end: float, decay_seconds: float, amplitude: float = 1.0, attack_seconds: float = 0.001, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var out := sine_from_curve(exp_curve(frame_count, frequency_start, frequency_end), amplitude, 0.0, sample_rate)
	return multiply(out, exp_decay(frame_count, decay_seconds, attack_seconds, sample_rate))


# ---------------------------------------------------------------------------
# Stereo
# ---------------------------------------------------------------------------


static func interleave(left: PackedFloat32Array, right: PackedFloat32Array) -> PackedFloat32Array:
	var count := mini(left.size(), right.size())
	var out := silence(count * 2)
	for i in count:
		out[i * 2] = left[i]
		out[i * 2 + 1] = right[i]
	return out


## Constant-power gains for [param pan_position] in [-1 (left), 1 (right)].
static func pan_gains(pan_position: float) -> Vector2:
	var angle := (clampf(pan_position, -1.0, 1.0) + 1.0) * PI * 0.25
	return Vector2(cos(angle), sin(angle))


static func pan(mono: PackedFloat32Array, pan_position: float) -> PackedFloat32Array:
	var gains := pan_gains(pan_position)
	var out := silence(mono.size() * 2)
	for i in mono.size():
		var s: float = mono[i]
		out[i * 2] = s * gains.x
		out[i * 2 + 1] = s * gains.y
	return out


## Mid/side widening: the side signal is a wrapped delay of the mono input so the
## result stays loop-safe and sums back to mono without colouration.
static func widen(mono: PackedFloat32Array, delay_ms: float = 12.0, width: float = 0.5, sample_rate: int = SAMPLE_RATE) -> PackedFloat32Array:
	var count := mono.size()
	var out := silence(count * 2)
	if count == 0:
		return out
	var delay := posmod(frames(delay_ms * 0.001, sample_rate), count)
	var side_gain := clampf(width, 0.0, 1.0)
	var scale := 1.0 / (1.0 + side_gain)
	for i in count:
		var mid: float = mono[i]
		var side: float = mono[posmod(i - delay, count)] * side_gain
		out[i * 2] = (mid + side) * scale
		out[i * 2 + 1] = (mid - side) * scale
	return out


## Adds a mono grain into an interleaved stereo buffer at a pan position.
static func stamp_stereo(dest_stereo: PackedFloat32Array, mono: PackedFloat32Array, frame_offset: int, pan_position: float, level: float = 1.0, wrap_around: bool = true) -> void:
	var dest_frames := _frame_count_of(dest_stereo, 2)
	if dest_frames == 0:
		return
	var gains := pan_gains(pan_position) * level
	for i in mono.size():
		var frame := frame_offset + i
		if wrap_around:
			frame = posmod(frame, dest_frames)
		elif frame < 0 or frame >= dest_frames:
			continue
		var s: float = mono[i]
		dest_stereo[frame * 2] += s * gains.x
		dest_stereo[frame * 2 + 1] += s * gains.y


# ---------------------------------------------------------------------------
# Encoding
# ---------------------------------------------------------------------------


## Packs samples into a 16-bit PCM [AudioStreamWAV]. Samples are hard-clamped to
## [-1, 1]; callers are expected to have normalised or soft-clipped beforehand.
static func encode(samples: PackedFloat32Array, stereo: bool, sample_rate: int = SAMPLE_RATE) -> AudioStreamWAV:
	var bytes := PackedByteArray()
	bytes.resize(samples.size() * 2)
	for i in samples.size():
		bytes.encode_s16(i * 2, int(clampf(samples[i], -1.0, 1.0) * INT16_MAX))
	var stream := AudioStreamWAV.new()
	stream.format = AudioStreamWAV.FORMAT_16_BITS
	stream.mix_rate = sample_rate
	stream.stereo = stereo
	stream.data = bytes
	return stream


## Encodes a forward-looping stream. When [param crossfade_ms] > 0 the tail is first
## blended into the head with [method make_seamless]; pass 0 for material that is
## already loop-synchronous or was built with wrap-around stamping.
static func loop_stream(samples: PackedFloat32Array, stereo: bool, crossfade_ms: float = 0.0, sample_rate: int = SAMPLE_RATE, equal_power: bool = true) -> AudioStreamWAV:
	var channels := 2 if stereo else 1
	var data := samples
	if crossfade_ms > 0.0:
		data = make_seamless(samples, crossfade_ms, channels, sample_rate, equal_power)
	var stream := encode(data, stereo, sample_rate)
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	stream.loop_begin = 0
	stream.loop_end = _frame_count_of(data, channels)
	return stream


## Reads a 16-bit [AudioStreamWAV] back into float samples (interleaved if stereo).
static func decode(stream: AudioStreamWAV) -> PackedFloat32Array:
	var bytes := stream.data
	var out := silence(bytes.size() >> 1)
	for i in out.size():
		out[i] = bytes.decode_s16(i * 2) / INT16_SCALE
	return out


## Number of frames (samples per channel) held by a 16-bit stream.
static func stream_frame_count(stream: AudioStreamWAV) -> int:
	var bytes_per_frame := 4 if stream.stereo else 2
	@warning_ignore("integer_division")
	return stream.data.size() / bytes_per_frame


# ---------------------------------------------------------------------------
# Internals
# ---------------------------------------------------------------------------


static func _rng(seed_value: int) -> RandomNumberGenerator:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	return rng


static func _frame_count_of(samples: PackedFloat32Array, channels: int) -> int:
	@warning_ignore("integer_division")
	return samples.size() / maxi(channels, 1)


static func _angular(frequency_hz: float, sample_rate: int) -> float:
	var clamped := clampf(frequency_hz, MIN_FILTER_HZ, sample_rate * MAX_FILTER_RATIO)
	return TAU * clamped / sample_rate


static func _one_pole_alpha(cutoff_hz: float, sample_rate: int) -> float:
	var clamped := clampf(cutoff_hz, MIN_FILTER_HZ, sample_rate * MAX_FILTER_RATIO)
	return 1.0 - exp(-TAU * clamped / sample_rate)
