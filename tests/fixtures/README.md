# Content fixtures

`main.scene.txt` and `content.json` were measured from the last GDScript source
commit, `265d2f4a9cc59bc69306d2cb43b45aba8f0f3937`, on Godot 4.8 dev6
`8898c2b3d`. They contain no local paths or device-specific results.

The scene dump records native stored properties, resource values, child order,
owners, groups and persistent connections of the detached main scene. Script
identity is normalized to the class name, and script-defined fields are excluded.
The C# builder is compared against this fixture before the scene enters the tree.

The 1,065 content fingerprints use SHA-256 over Godot's `var_to_bytes` encoding.
They cover the sampled terrain, planned trees/rocks/shrubs, generated tree and
plant mesh channels, canoe/tent meshes, scanned-dressing transforms, meadow and
grass batches, sampled camera routes, analytic waves and the generated audio bank.
The sampled routes also check that the film has no music cues. Byte lengths guard
against silently accepting empty geometry from a renderer-less mesh readback.

These are CPU-only checks, not images, gameplay recordings or performance
measurements.

## Role and current state

The fixtures proved the GDScript-to-C# conversion exact at that commit. Terrain,
vegetation and gameplay have changed on purpose since then, and whether
`SceneContractProbe` and `FingerprintProbe` still pass on `master` has not been
recorded. Roadmap milestone M0 runs both, traces every mismatch to its commit and
rebaselines the fixtures to the current game. From then on they are the current-game
baseline: a key that changes outside a deliberate content change is a regression.

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
