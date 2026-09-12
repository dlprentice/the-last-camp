# Validation and known limits

The release targets **Godot 4.7.2 standard**, Forward+ Vulkan and native Jolt.
Linux x86-64 is the tested platform. Converted assets are included in source;
the first import creates a new local cache.

## Desktop evidence

Measured on September 12, 2026 with an NVIDIA RTX 4060 Laptop GPU (8 GB VRAM),
NVIDIA driver 610.57.04, 31 GiB system RAM, and Godot
`4.7.2.stable.official.ed1daf0bf`:

| Check | Observed result |
| --- | --- |
| Godot regressions | 92 passed, zero failed |
| Complete High scene construction | Passed; 1,278 near/main trees, 641 habitat batches and 18 fish reported (ridge trees are additional) |
| Native captures | Nine full-scene High views at 1920×1080, actual NVIDIA Vulkan device |
| Exported game traversal | All 15 waypoints and photo-mode, lantern, fire-feeding and quality-change checks passed |
| High benchmark | 13.5 FPS mean, 74.23 ms mean frame time, 93.28 ms p99, 10.7 FPS 1% low; 297 measured frames |

The benchmark used 1920×1080 output and High's 0.77 internal scale, without
Movie Maker or fixed FPS. It is a short repeatable route, not a benchmark of
every possible view or machine. Peak memory and separate CPU/GPU timings were
not newly measured in this pass.

## Clean-source and distribution checks

A separate source copy imported successfully from an empty Godot cache, without
private asset-download caches. All **92 Godot tests** and **44 Python tests**
passed; the Python suite includes asset-attribution and safe-rebaking checks.
The capture preflight and shell syntax checks passed.

A fresh Linux release export was checked through its own embedded resources:
all **nine material, 35 model and six recording-source credit entries** were
present, together with the project and shader license notices. The package
also contains the exported engine's own license and component notices.
Its complete High scene construction check passed from the standalone binary
with no project directory or external asset cache.

## Delivered movies

| Output | Duration | Frames | Audio |
| --- | --- | --- | --- |
| Main film | 337 seconds | 20,220 at 1080p60 | −20.9 LUFS integrated, 20.3 LU range, −2.0 dBTP |
| Showcase reel | 111 seconds | 6,660 at 1080p60 | −20.7 LUFS integrated, 13.1 LU range, −2.0 dBTP |

Both full H.264/AAC movies decoded without errors. Visual checks covered
complete contact sheets, full-size scene views and sampled playback, with
both water crossings examined in motion. This does not mean every frame was
individually inspected. The audio checks are source and meter checks;
subjective listening approval is not claimed.

## Known limits

- Full forest coverage is expensive. High is not a realtime 60 FPS target on
  the measured laptop; use a lower preset for interactive exploration.
- Pine branch tiers and some close plant cards remain visibly procedural.
  Daytime flames are softer than night flames, and wet surfaces can look pale.
- Surfacing includes a conspicuous brief lens/waterline band. Water optics,
  wave propagation, weather and animal behaviour use the approximations
  described in [architecture](architecture.md).
- A previous run that switched through every quality preset lost the Vulkan
  device on this driver. Subsequent bounded runs and final films completed;
  the intermittent driver's root cause is unresolved.
- Other operating systems and GPUs, web/mobile exports and long-duration
  gameplay have not received equivalent validation.

See [development](development.md) for reproducible checks. Runtime behaviour
and visual quality need their own checks; unit-test counts and offline output
FPS do not establish either.
