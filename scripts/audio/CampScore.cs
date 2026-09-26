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

/// Original, deterministic score: soft struck notes with optional sustained fifths.
/// Rendered once into a stereo PCM stream; no per-frame synthesis or assets.
public partial class CampScore
{
    public const long RATE = 32000;
    public static readonly Godot.Collections.Array CHORDS = new Godot.Collections.Array { new Godot.Collections.Array { 50, 57, 61, 64, 69 }, new Godot.Collections.Array { 47, 54, 57, 62, 66 }, new Godot.Collections.Array { 43, 50, 54, 57, 62 }, new Godot.Collections.Array { 45, 52, 57, 59, 64 }, new Godot.Collections.Array { 50, 57, 61, 66, 69 }, new Godot.Collections.Array { 43, 50, 54, 62, 66 }, new Godot.Collections.Array { 50, 57, 61, 64, 69 } };

    public static AudioStreamWav build(double seconds = 62.0, Variant cues = default(Variant))
    {
        long frames = (long)(seconds * RATE);
        List<float> stereo = Synth.silence(frames * 2);
        // Cue positions come from the edit, so extending a nature shot does not
        // accidentally fill it with another seven minutes of repeating melody.
        // An omitted arrangement requests the standalone demonstration cue. An
        // explicitly empty film arrangement means that this edit contains no music.
        Godot.Collections.Array<Godot.Collections.Dictionary> arrangement = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (cues.VariantType == Variant.Type.Nil)
        {
            arrangement.Add(new Godot.Collections.Dictionary { { (StringName)"start", 1.5 }, { (StringName)"length", seconds - 3.0 }, { (StringName)"gain", 1.0 }, { (StringName)"melody", true }, { (StringName)"pads", true } });
        }
        else
        {
            G.assign(arrangement, cues.AsGodotArray<Godot.Collections.Dictionary>());
        }
        Godot.Collections.Array<Godot.Collections.Dictionary> events = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (Godot.Collections.Dictionary cue in arrangement)
        {
            long bars = maxi(1, ceili((G.to_float(cue["length"]) - 8.0) / 8.0));
            for (long bar = 0; bar < bars; bar++)
            {
                events.Add(new Godot.Collections.Dictionary { { (StringName)"start", G.to_float(cue["start"]) + bar * 8.0 }, { (StringName)"chord", bar % (long)CHORDS.Count }, { (StringName)"gain", G.to_float(cue["gain"]) }, { (StringName)"melody", G.truthy(cue["melody"]) }, { (StringName)"pads", G.truthy(G.get(cue, "pads", false)) }, { (StringName)"resolve", bar == bars - 1 } });
            }
        }
        foreach (Godot.Collections.Dictionary @event in events)
        {
            double start = @event["start"].AsDouble();
            Godot.Collections.Array chord = CHORDS[(int)(G.truthy(@event["resolve"]) ? 0 : G.to_int(@event["chord"]))].AsGodotArray();
            double gain = @event["gain"].AsDouble();
            if (G.truthy(@event["pads"]))
            {
                for (long voice = 0; voice < 5; voice++)
                {
                    long note = chord[(int)voice].AsInt64();
                    double hz = 440.0 * pow(2.0, (double)(note - 69) / 12.0);
                    List<float> pad = Synth.sine((long)(RATE * 10.0), hz, 0.55, 0.0, RATE);
                    Synth.mix_into(pad, Synth.sine((long)pad.Count, hz * 1.0018, 0.26, 0.8, RATE));
                    Synth.mix_into(pad, Synth.sine((long)pad.Count, hz * 2.0, 0.09, 0.0, RATE));
                    Synth.multiply(pad, Synth.adsr((long)pad.Count, 2.4, 1.0, 0.65, 3.4, RATE));
                    Synth.stamp_stereo(stereo, pad, (long)(start * RATE), ((double)voice - 2.0) * 0.3, 0.035 * gain, false);
                }
            }
            // A restrained answering phrase; slight harmonic inharmonicity gives
            // the strike a wooden body instead of a pure electronic beep.
            for (long beat = 0; beat < 4; beat++)
            {
                if (!(G.truthy(@event["melody"])))
                {
                    continue;
                }
                long note2 = G.op("+", chord[new Godot.Collections.Array { 2, 4, 3, 1 }[(int)beat].AsInt32()], 12).AsInt64();
                double hz2 = 440.0 * pow(2.0, (double)(note2 - 69) / 12.0);
                long n = (long)(RATE * 4.5);
                List<float> key = Synth.sine(n, hz2, 0.65, 0.0, RATE);
                Synth.mix_into(key, Synth.sine(n, hz2 * 2.003, 0.18, 0.0, RATE));
                Synth.mix_into(key, Synth.sine(n, hz2 * 3.011, 0.045, 0.0, RATE));
                Synth.multiply(key, Synth.exp_decay(n, 1.1, 0.018, RATE));
                Synth.fade_edges(key, 18.0, 500.0, 1, RATE);
                long at = (long)((start + 0.8 + (double)beat * 1.55) * RATE);
                double pan = beat % 2 == 0 ? -0.35 : 0.35;
                Synth.stamp_stereo(stereo, key, at, pan, 0.13 * gain, false);
                Synth.stamp_stereo(stereo, key, at + (long)(0.43 * RATE), -pan, 0.024 * gain, false);
            }
        }
        Synth.fade_edges(stereo, 1600.0, 4500.0, 2, RATE);
        return Synth.encode(stereo, true, RATE);
    }
}
