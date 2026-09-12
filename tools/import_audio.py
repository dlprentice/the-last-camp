#!/usr/bin/env python3
"""Prepare the credited nature/water/weather recordings; originals stay in local-data.

Run with --source-dir to reuse the reviewed downloads, or without it to fetch
the same public files. Requires ffmpeg and numpy. Never normalizes the mix at
runtime: each edited take has a fixed onset, tail and measured peak headroom.
"""
import argparse
import pathlib
import subprocess
import urllib.request
import wave
import zipfile

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[1]
RATE = 48000
SOURCES = {
    "Mark_DiAngelo_Water_Churning.wav": "https://soundbible.com/grab.php?id=1790&type=wav",
    "Mike_Koenig_Water_Splash.wav": "https://soundbible.com/grab.php?id=1460&type=wav",
    "Mark_DiAngelo_Thunder_HD.wav": "https://soundbible.com/grab.php?id=1913&type=wav",
    "Ylmir_Rain_OGG.zip": "https://opengameart.org/sites/default/files/Rain%20OGG.zip",
}
NATURE_SOURCES = {
    "PagDev_Fire.wav": "https://opengameart.org/sites/default/files/fire.wav",
    "Naturenotesuk_Trees_Wind.mp3": "https://cdn.freesound.org/previews/457/457428_7455632-hq.mp3",
}


def decode(path, start=0, duration=100, filters="anull", channels=2):
    data = subprocess.check_output([
        "ffmpeg", "-v", "error", "-ss", str(start), "-i", str(path),
        "-t", str(duration), "-af", filters, "-ar", str(RATE), "-ac", str(channels), "-f", "f32le", "-",
    ])
    return np.frombuffer(data, dtype="<f4").reshape(-1, channels).copy()


def peak_scale(samples, peak):
    return samples * (peak / max(float(np.max(np.abs(samples))), 1e-8))


def save_wave(path, samples, peak, fade_out):
    samples = samples - samples.mean(axis=0)
    attack = min(int(RATE * .005), len(samples))
    release = min(int(RATE * fade_out), len(samples))
    samples[:attack] *= np.linspace(0, 1, attack)[:, None]
    samples[-release:] *= np.linspace(1, 0, release)[:, None]
    samples = peak_scale(samples, peak)
    with wave.open(str(path), "wb") as out:
        out.setparams((samples.shape[1], 2, RATE, len(samples), "NONE", "not compressed"))
        out.writeframes((samples * 32767).astype("<i2").tobytes())
    print(f"{path.name}: {len(samples)/RATE:.3f} s, peak {20*np.log10(peak):.1f} dBFS")


def save_loop(path, samples, peak, fade_seconds):
    """Fold the tail into the head, retaining a continuous seam and stereo image."""
    samples -= samples.mean(axis=0)
    fade = int(RATE * fade_seconds)
    theta = np.linspace(0, np.pi / 2, fade)[:, None]
    loop = samples[fade:].copy()
    loop[-fade:] = samples[-fade:] * np.cos(theta) + samples[:fade] * np.sin(theta)
    loop = peak_scale(loop, peak)
    with wave.open(str(path), "wb") as out:
        out.setparams((loop.shape[1], 2, RATE, len(loop), "NONE", "not compressed"))
        out.writeframes((loop * 32767).astype("<i2").tobytes())
    print(f"{path.name}: {len(loop)/RATE:.3f} s, peak {20*np.log10(peak):.1f} dBFS, {fade_seconds:g} s loop crossfade")


def prepare_nature(src, output):
    # The take carries its own broadband crackles; no synthesized fire is mixed in.
    # Reduce the recording's heavy low-frequency body before peak normalization;
    # retain the uncompressed broadband crackles and their natural timing.
    fire_filter = "highpass=f=65,bass=f=200:g=-7:t=q:w=0.707:p=2,lowpass=f=6500"
    fire = decode(src / "PagDev_Fire.wav", filters=fire_filter, channels=1)
    save_loop(output / "fire_loop.wav", fire, .74, 1.0)
    feed = decode(src / "PagDev_Fire.wav", 11.0, 2.2, fire_filter, channels=1)
    save_wave(output / "fire_feed.wav", feed, .56, .65)
    # This source contains great tit calls. Retain only the low wind/branch band,
    # including at night: three 2-pole low-passes strongly reject the bird band.
    # 80..140 s has relatively little 2..5.5 kHz energy in a source spectrum check;
    # this is not a claim that the recording was auditioned or birds hand-edited.
    # Two cascaded 1.4 kHz poles keep the branch and leaf band and still sit
    # 40 dB down at the great tit's 4 kHz; the old triple 750 Hz cascade left a
    # dull roar that read as a fake wind.
    wind = decode(src / "Naturenotesuk_Trees_Wind.mp3", 80, 60,
                  "highpass=f=70,lowpass=f=1400,lowpass=f=1400")
    save_loop(output / "wind_trees.wav", wind, .55, 2.0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=pathlib.Path, default=ROOT / "local-data/audio-sources")
    parser.add_argument("--nature-only", action="store_true", help="Prepare only recorded fire and filtered wind")
    args = parser.parse_args()
    src = args.source_dir
    src.mkdir(parents=True, exist_ok=True)
    sources = NATURE_SOURCES if args.nature_only else SOURCES | NATURE_SOURCES
    for name, url in sources.items():
        dest = src / name
        if not dest.exists():
            with urllib.request.urlopen(url, timeout=30) as response:
                dest.write_bytes(response.read())
    output = ROOT / "audio"
    output.mkdir(exist_ok=True)
    prepare_nature(src, output)
    if args.nature_only:
        return

    splash = peak_scale(decode(src / "Mike_Koenig_Water_Splash.wav", filters="highpass=f=65"), .55)
    bubbles = peak_scale(decode(src / "Mark_DiAngelo_Water_Churning.wav", 5.70, 2.15,
                               "highpass=f=55,lowpass=f=2200"), .33)
    entry = np.zeros((int(2.4 * RATE), 2), np.float32)
    entry[:len(splash)] += splash
    offset = int(.12 * RATE)
    entry[offset:offset+len(bubbles)] += bubbles * np.exp(-np.arange(len(bubbles)) / RATE * 1.0)[:, None]
    save_wave(output / "water_entry.wav", entry, .74, .4)

    exit_sound = decode(src / "Mark_DiAngelo_Water_Churning.wav", 1.90, 3.0, "highpass=f=90")
    exit_sound *= np.exp(-np.arange(len(exit_sound)) / RATE * .65)[:, None]
    save_wave(output / "water_exit.wav", exit_sound, .62, .45)
    thunder = decode(src / "Mark_DiAngelo_Thunder_HD.wav", .43, 8.46, "highpass=f=32")
    save_wave(output / "thunder.wav", thunder, .82, .6)
    with zipfile.ZipFile(src / "Ylmir_Rain_OGG.zip") as archive:
        member = next(name for name in archive.namelist() if name.endswith("/3.ogg") or name == "3.ogg")
        (output / "rain.ogg").write_bytes(archive.read(member))
    print("rain.ogg: original 45-second CC0 loop, unchanged")


if __name__ == "__main__":
    main()
