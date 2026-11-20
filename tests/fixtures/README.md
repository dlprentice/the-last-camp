# Content fixtures

`main.scene.txt` was measured from the last GDScript source commit,
`265d2f4a9cc59bc69306d2cb43b45aba8f0f3937`, on Godot 4.8 dev6 `8898c2b3d`, and
still matches the C# scene builder unchanged. `content.json` was rebaselined on
2026-11-20 on Godot 4.8 beta4 .NET (`4.8.beta4.mono.official.b070e63b1`) after
the micro-cut places, look-round-4 tent/grass/path work and Tom's Rock lookout
corridor were reviewed in full-size 1080p RTX 4060 stills (`973` keys: expected
changes only in `terrain`, `tree_layout`, `rock_layout`, `shrub_layout`,
`scanned/*`, `meadow_plan`, `grass`, `grass_chunk/*`, `tent_canvas` and
`tent_hem`; all tree meshes, habitat/seed meshes, canoe, tent poles, camera
routes, score cues, waves and `main.scene.txt` unchanged). Neither fixture
contains local paths or device-specific results.

The scene dump records native stored properties, resource values, child order,
owners, groups and persistent connections of the detached main scene. Script
identity is normalized to the class name, and script-defined fields are excluded.
The C# builder is compared against this fixture before the scene enters the tree.

The 1,015 content fingerprints use SHA-256 over Godot's `var_to_bytes` encoding.
They cover the sampled terrain, planned trees/rocks/shrubs, generated tree and
plant mesh channels, canoe/tent meshes, scanned-dressing transforms, meadow and
grass batches, sampled camera routes and analytic waves. The game's audio is no
longer generated in code, so it has no fingerprints.
The sampled routes also check that the film has no music cues. Byte lengths guard
against silently accepting empty geometry from a renderer-less mesh readback.

These are CPU-only checks, not images, gameplay recordings or performance
measurements.

## Role and current state

The fixtures proved the GDScript-to-C# conversion exact at that commit. Terrain,
vegetation and gameplay changes made since then left every recorded key unchanged:
on October 20 both probes passed on `master` at `1b33dfd` (`SCENE_CONTRACT PASS`
and `CONTENT_PARITY count=1065 result=PASS`), so no rebaseline was needed. The
fixtures are the current-game baseline. The one rebaseline since removed the 50
keys of the retired synthesised audio bank (48 `sound_bank/*`, `noise` and
`filtered_noise`) when the game's audio moved to ElevenLabs; every other key was
unchanged, so the fixture is the earlier one minus those keys. A key that changes outside a deliberate
content change is a regression, and a deliberate change follows the procedure
below.

## Rebaseline procedure

1. Run both probes before the change and keep their output in `local-data/`.
2. Make the content change, run the probes again and list the changed keys by group
   (terrain, vegetation plans, meshes, dressing, grass and meadow, camera routes,
   waves, audio bank, scene dump).
3. Check the groups against the milestone's expected list in the
   [roadmap's fixture plan](../../docs/roadmap.md#fixture-plan). Any other changed group
   is investigated, not accepted.
4. Review the change itself: full-size stills or 1:1 footage for visual groups,
   listening and the audio check for the audio bank, the scene diff for the dump.
5. Regenerate the fixtures and commit them in the same change, with a commit message
   that names the changed groups, the reason and the review evidence. Update the
   source commit named at the top of this page.
