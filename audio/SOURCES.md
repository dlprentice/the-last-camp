# Sound sources

## Generated with ElevenLabs

All of the game's audio, the ambience beds, footsteps, wildlife, weather, actions,
interface sounds and June's voice, is generated with ElevenLabs. No sound is made
in code and no third-party recording is used.

The generated audio is licensed for this game. It is not MIT and is not
redistributed in this repository: the files live under `audio/generated/`, which Git
ignores, and they ship only inside builds. The repository keeps the lists of what
exists:

- [`generated/manifest.json`](generated/manifest.json): every sound by id, with its
  kind (bed or one-shot), its files, the measured loudness of each file and the
  prompt it was generated from. [The sound list](../docs/sound-list.md) says what each
  id is for.
- [`generated/voice.json`](generated/voice.json): June's lines paragraph by paragraph,
  with their text and file names.

A copy of the repository without the files builds, runs and passes its tests; it
plays silently, with June's lines as subtitles.

There is no music in play.
