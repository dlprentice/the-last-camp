# Conversion fixtures

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

These are CPU-only contracts, not images, gameplay recordings or performance
measurements. Update fixtures only for an intentional, separately reviewed content
change; do not regenerate them from the converted implementation to hide a mismatch.
