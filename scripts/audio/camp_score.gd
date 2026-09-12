class_name CampScore
extends RefCounted

## Original, deterministic score: soft struck notes with optional sustained fifths.
## Rendered once into a stereo PCM stream; no per-frame synthesis or assets.
const RATE := 32000
const CHORDS := [[50, 57, 61, 64, 69], [47, 54, 57, 62, 66], [43, 50, 54, 57, 62], [45, 52, 57, 59, 64], [50, 57, 61, 66, 69], [43, 50, 54, 62, 66], [50, 57, 61, 64, 69]]

static func build(seconds := 62.0, cues: Variant = null) -> AudioStreamWAV:
	var frames := int(seconds * RATE)
	var stereo := Synth.silence(frames * 2)
	# Cue positions come from the edit, so extending a nature shot does not
	# accidentally fill it with another seven minutes of repeating melody.
	# An omitted arrangement requests the standalone demonstration cue. An
	# explicitly empty film arrangement means that this edit contains no music.
	var arrangement: Array[Dictionary] = []
	if cues == null:
		arrangement.append({start = 1.5, length = seconds - 3.0, gain = 1.0, melody = true, pads = true})
	else:
		arrangement.assign(cues)
	var events: Array[Dictionary] = []
	for cue in arrangement:
		var bars := maxi(1, ceili((float(cue.length) - 8.0) / 8.0))
		for bar in bars:
			events.append({start = float(cue.start) + bar * 8.0, chord = bar % CHORDS.size(),
				gain = float(cue.gain), melody = bool(cue.melody),
				pads = bool(cue.get("pads", false)), resolve = bar == bars - 1})
	for event in events:
		var start: float = event.start
		var chord: Array = CHORDS[0 if event.resolve else int(event.chord)]
		var gain: float = event.gain
		if event.pads:
			for voice in 5:
				var note: int = chord[voice]
				var hz := 440.0 * pow(2.0, float(note - 69) / 12.0)
				var pad := Synth.sine(int(RATE * 10.0), hz, 0.55, 0.0, RATE)
				Synth.mix_into(pad, Synth.sine(pad.size(), hz * 1.0018, 0.26, 0.8, RATE))
				Synth.mix_into(pad, Synth.sine(pad.size(), hz * 2.0, 0.09, 0.0, RATE))
				Synth.multiply(pad, Synth.adsr(pad.size(), 2.4, 1.0, 0.65, 3.4, RATE))
				Synth.stamp_stereo(stereo, pad, int(start * RATE), (float(voice) - 2.0) * 0.3, 0.035 * gain, false)
		# A restrained answering phrase; slight harmonic inharmonicity gives
		# the strike a wooden body instead of a pure electronic beep.
		for beat in 4:
			if not event.melody:
				continue
			var note: int = chord[[2, 4, 3, 1][beat]] + 12
			var hz := 440.0 * pow(2.0, float(note - 69) / 12.0)
			var n := int(RATE * 4.5)
			var key := Synth.sine(n, hz, 0.65, 0.0, RATE)
			Synth.mix_into(key, Synth.sine(n, hz * 2.003, 0.18, 0.0, RATE))
			Synth.mix_into(key, Synth.sine(n, hz * 3.011, 0.045, 0.0, RATE))
			Synth.multiply(key, Synth.exp_decay(n, 1.1, 0.018, RATE))
			Synth.fade_edges(key, 18.0, 500.0, 1, RATE)
			var at := int((start + 0.8 + float(beat) * 1.55) * RATE)
			var pan := -0.35 if beat % 2 == 0 else 0.35
			Synth.stamp_stereo(stereo, key, at, pan, 0.13 * gain, false)
			Synth.stamp_stereo(stereo, key, at + int(0.43 * RATE), -pan, 0.024 * gain, false)
	Synth.fade_edges(stereo, 1600.0, 4500.0, 2, RATE)
	return Synth.encode(stereo, true, RATE)
