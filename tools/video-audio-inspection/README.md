# video-audio-inspection — measured audio verdicts for generated video

Measures the **audio track** of a generated video (MiniMax H3 and similar) from the decoded
signal, so a verdict about sound is reproducible instead of a guess from watching frames.

## Why it exists

H3 generates video and audio **jointly** — a clip's soundtrack is a graded deliverable, not a
side effect. Looking at frames cannot tell you whether:

- the audio is present at all,
- it is silent (an audio stream exists but carries nothing),
- it is **too quiet to notice** (H3's native −24 to −30 LUFS is ~12 dB below delivery level),
- it is voice-only with no physical/body sound,
- or it has no low-end weight.

Those states all looked identical until this was measured. The tool was promoted after the B-150
H3 work showed that every clip carried audio, that the content was **vocalisation-dominant with the
body/impact band almost absent**, and that the level was **far too low** — none of which any amount
of frame inspection would have surfaced.

It exists partly because of a mistake worth not repeating: the audio was once reported as "working"
on the strength of RMS and spectral measurement alone, when it was in fact ~12 dB too quiet to be
heard at normal volume. That is why loudness (LUFS) is now a first-class column here rather than an
afterthought.

## Run

Use the repo venv (it has the pinned deps):

```powershell
# by file
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/video-audio-inspection/inspect_video_audio.py clip.mp4

# by B-150 proof run tag (resolves artifacts/tmp/h3-nsfw-proof/<tag>/*.mp4)
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/video-audio-inspection/inspect_video_audio.py --tag dual-subj-long

# several at once, export audio for listening, or emit JSON
... inspect_video_audio.py --tag a --tag b --export artifacts/tmp/audio/out.wav
... inspect_video_audio.py clip.mp4 --json
```

Outputs go to whatever path you pass to `--export` (point it at git-ignored `artifacts/tmp/**`).

## What it reports

| Field | Meaning |
|---|---|
| stream | codec, sample rate, channels, duration — proves whether an audio stream exists |
| **loudness** | **integrated LUFS, true peak (dBTP), loudness range (LU)** via `loudnorm` |
| level | overall RMS in dBFS, true peak, dynamic range, stereo correlation |
| per-second dBFS | the level envelope, so a static bed is distinguishable from a build-up |
| centroid | spectral centre of mass in Hz |
| flatness | noise-like (higher) vs tonal/harmonic (near 0) |
| harmonicity | mean normalised autocorrelation peak of active frames in the 60–400 Hz pitch-lag range |
| band % | energy share per band (sub, body, voice, presence, air) |
| verdict | names the likely problem, or says the lane looks healthy |

Bands: `sub<100`, `body100-400`, `voice400-2k`, `presence2k-6k`, `air>6k`.

## How to read it

- **Check loudness first.** Integrated LUFS is what answers "will a person hear this at normal
  volume". Normal media sits around −19 to −14 LUFS. **RMS is not a substitute** — a track can look
  reasonable in RMS while sitting ~12 dB below delivery level, which is exactly what H3 does
  (measured −24 to −30 LUFS). A verdict of `FAR TOO QUIET` means it needs `loudnorm`, not a model
  change.
- **Harmonicity > 0.5 with a large `voice400-2k` share** means voiced human sound (moans, speech,
  singing) — human vocalisation is strongly harmonic.
- **Low flatness** means tonal/harmonic content; noise-like texture (wet/friction sounds) raises it.
- **A large voice share with almost no `sub<100` / `body100-400`** means the model produced
  vocalisation but no physical body sound — the fix is prompt wording in `overall_soundscape`
  (H3 puts non-verbal human sound and physical action sounds there), not a model change.
- **A dynamic range under ~3 dB** means a static bed with no build-up.

## Limits

- This measures the signal, not the meaning: it can tell you the audio is voiced and dynamic, but not
  *whether the words are intelligible*. Intelligibility needs listening or an external ASR pass — use
  `--export` to write a WAV for that.
- **It cannot tell you whether a player will play the file.** VS Code's Electron build lacks an AAC
  decoder, so a perfectly good mp4 plays silently there. If you hear nothing, confirm the player
  (VLC) before concluding the audio is broken.

## Dependencies

`requirements.txt` pins `imageio-ffmpeg` (bundles a static ffmpeg binary — this machine has no
system ffmpeg), plus `numpy` and `scipy`. Install with:

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe -m pip install -r tools/video-audio-inspection/requirements.txt
```
