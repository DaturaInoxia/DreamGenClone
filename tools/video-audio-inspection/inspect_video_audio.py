"""Inspect the audio track of a generated video (H3 and similar).

WHY THIS EXISTS
---------------
H3 generates video and audio jointly, so a clip's audio is a graded deliverable,
not a side effect. Frames alone cannot tell you whether the audio is present, is
silent, is voice-only, or is missing the body/impact band — all of which looked
identical until this was measured. This tool answers those questions from the
decoded signal rather than from listening, so a verdict is reproducible.

WHAT IT MEASURES
  1. Stream presence: is there an audio stream, what codec, sample rate, channels.
  2. Level: overall RMS in dBFS, true peak, and a per-second RMS envelope (so a
     static bed is distinguishable from a build-up).
  3. Character: spectral centroid, spectral flatness (noise-like vs tonal),
     harmonicity (voiced vs unvoiced), and energy distribution across bands.
  4. A verdict that names the likely problem.

READING THE NUMBERS
  - harmonicity is the mean normalised autocorrelation peak of active frames in
    the 60-400 Hz pitch-lag range. Voiced human sound (moans, speech, singing) is
    strongly harmonic, typically > 0.5. Noise-like sound (wet/friction) is low.
  - spectral flatness near 0 means tonal/harmonic content; higher means noise.
  - the voice band is 400-2000 Hz (vocal formants). Body/impact energy lives
    below 100 Hz. Heavy voice-band share with almost no sub-100 Hz means the
    model produced vocalisation but no physical body sound.

USAGE
  python tools/video-audio-inspection/inspect_video_audio.py <video...>
  python tools/video-audio-inspection/inspect_video_audio.py --tag dual-subj-long
  python tools/video-audio-inspection/inspect_video_audio.py out.mp4 --export artifacts/tmp/audio/out.wav

Run with the repo venv:
  d:/src/DreamGenClone/.venv/Scripts/python.exe ...
"""
import argparse
import glob
import json
import os
import subprocess
import sys

import numpy as np

H3_PROOF_ROOT = os.path.join('artifacts', 'tmp', 'h3-nsfw-proof')

BANDS = {
    'sub<100': (20, 100),
    'body100-400': (100, 400),
    'voice400-2k': (400, 2000),
    'presence2k-6k': (2000, 6000),
    'air>6k': (6000, 16000),
}


def ffmpeg_exe():
    """Locate an ffmpeg binary, preferring the one bundled with imageio-ffmpeg."""
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        from shutil import which
        found = which('ffmpeg')
        if found:
            return found
        raise SystemExit(
            'No ffmpeg found. Install imageio-ffmpeg into the repo venv:\n'
            '  .venv/Scripts/python.exe -m pip install -r tools/video-audio-inspection/requirements.txt')


def probe(ff, path):
    """Return stream facts, or None when the file carries no audio stream."""
    out = subprocess.run([ff, '-hide_banner', '-i', path], capture_output=True, text=True).stderr
    codec = rate = channels = None
    duration = None
    for line in out.splitlines():
        if 'Duration:' in line and duration is None:
            try:
                stamp = line.split('Duration:')[1].split(',')[0].strip()
                h, m, s = stamp.split(':')
                duration = int(h) * 3600 + int(m) * 60 + float(s)
            except Exception:
                pass
        if 'Audio:' in line:
            head = line.split('Audio:')[1]
            codec = head.split(',')[0].strip()
            for part in head.split(','):
                part = part.strip()
                if part.endswith('Hz') and part[:-2].strip().isdigit():
                    rate = int(part[:-2].strip())
                elif part.endswith('stereo') or part.endswith('mono'):
                    channels = 2 if part.endswith('stereo') else 1
    if codec is None:
        return None
    return {'sample_rate': rate, 'channels': channels, 'codec': codec, 'duration': duration}


def decode(ff, path, sample_rate, channels):
    raw = subprocess.run(
        [ff, '-v', 'error', '-i', path, '-f', 'f32le', '-ac', str(channels),
         '-ar', str(sample_rate), '-'],
        capture_output=True).stdout
    audio = np.frombuffer(raw, dtype=np.float32)
    if audio.size == 0:
        return np.zeros((0, channels), np.float32)
    return audio.reshape(-1, channels)


def measure_loudness(ff, path):
    """Integrated loudness (LUFS), true peak (dBTP) and loudness range (LU) via ffmpeg loudnorm.

    RMS alone is misleading: it does not track perceived loudness, and a track can look
    reasonable in RMS while sitting ~12 dB below any normal delivery level. This is the
    measurement that actually answers "will a person hear this at normal volume".
    """
    out = subprocess.run(
        [ff, '-hide_banner', '-i', path, '-af', 'loudnorm=print_format=summary', '-f', 'null', '-'],
        capture_output=True, text=True).stderr

    values = {}
    for line in out.splitlines():
        for key, name in (('Input Integrated', 'integrated_lufs'),
                          ('Input True Peak', 'true_peak_dbtp'),
                          ('Input LRA', 'loudness_range_lu')):
            if line.strip().startswith(key):
                text = line.split(':', 1)[1].strip().split()[0]
                try:
                    values[name] = float(text)
                except ValueError:
                    pass
    return values or None


def analyse(samples, sample_rate):
    """Level + spectral + harmonicity statistics over the active part of the signal."""
    from scipy import signal

    mono = samples.mean(axis=1).astype(np.float64)
    if mono.size < sample_rate // 2:
        return None

    rms = float(np.sqrt((mono ** 2).mean()))
    peak = float(np.abs(mono).max())
    seconds = int(len(mono) // sample_rate)
    per_second = [
        round(float(20 * np.log10(max(np.sqrt((mono[i * sample_rate:(i + 1) * sample_rate] ** 2).mean()), 1e-9))), 1)
        for i in range(seconds)
    ]

    freqs, _, spec = signal.stft(mono, fs=sample_rate, nperseg=1024)
    magnitude = np.abs(spec)
    if magnitude.size == 0:
        return None

    energy = magnitude.sum(axis=0)
    active = energy > np.percentile(energy, 55)
    power = magnitude[:, active] ** 2
    if power.sum() <= 0:
        return None

    centroid = float((freqs[:, None] * power).sum() / power.sum())
    flatness = float(np.exp(np.log(power + 1e-12).mean()) / (power.mean() + 1e-12))
    distribution = {}
    for name, (low, high) in BANDS.items():
        mask = (freqs >= low) & (freqs < high)
        distribution[name] = round(100 * float(power[mask].sum()) / float(power.sum()), 1)

    harmonicity = []
    for index in np.where(active)[0][:40]:
        window = spec[:, index]
        auto = np.fft.irfft(np.abs(window) ** 2)[:int(sample_rate / 60)]
        if auto[0] > 0:
            harmonicity.append(float(auto[1:].max() / auto[0]))
    harmonicity_value = float(np.mean(harmonicity)) if harmonicity else float('nan')

    correlation = float('nan')
    if samples.shape[1] == 2 and len(samples) > 1:
        correlation = float(np.corrcoef(samples[:, 0], samples[:, 1])[0, 1])

    return {
        'rms_dbfs': round(20 * np.log10(max(rms, 1e-9)), 1),
        'peak': round(peak, 3),
        'per_second_dbfs': per_second,
        'dynamic_range_db': round(max(per_second) - min(per_second), 1) if per_second else 0.0,
        'centroid_hz': round(centroid),
        'flatness': round(flatness, 5),
        'harmonicity': round(harmonicity_value, 3),
        'band_pct': distribution,
        'stereo_correlation': round(correlation, 3),
    }


def verdict(stats, loudness=None):
    """Name the likely problem, or say the lane looks healthy."""
    if stats is None:
        return 'NO DECODABLE AUDIO'

    notes = []
    integrated = (loudness or {}).get('integrated_lufs')
    if integrated is not None:
        if integrated <= -50:
            return 'SILENT - audio stream present but effectively empty'
        if integrated < -24:
            notes.append(f'FAR TOO QUIET ({integrated:.1f} LUFS; normal media is about -19 to -14)')
        elif integrated < -20:
            notes.append(f'quiet ({integrated:.1f} LUFS)')

    if stats['harmonicity'] > 0.5 and stats['band_pct']['voice400-2k'] > 35:
        notes.append('voiced/vocal content dominant')
    if stats['band_pct']['sub<100'] < 1.0 and stats['band_pct']['body100-400'] < 5.0:
        notes.append('body/impact band almost absent (no low-end weight)')
    if stats['flatness'] < 0.01:
        notes.append('strongly tonal/harmonic; noise-like texture (wet/friction) unlikely')
    if stats['dynamic_range_db'] < 3:
        notes.append('static bed, no build-up')
    return '; '.join(notes) if notes else 'healthy: present, dynamic, with low-end weight'


def resolve_inputs(videos, tags):
    paths = list(videos)
    for tag in tags or []:
        found = sorted(glob.glob(os.path.join(H3_PROOF_ROOT, tag, '*.mp4')))
        if not found:
            print(f'[{tag}] no mp4 found under {H3_PROOF_ROOT}', file=sys.stderr)
        paths.extend(found)
    return paths


def main():
    ap = argparse.ArgumentParser(description='Inspect the audio track of a generated video.')
    ap.add_argument('videos', nargs='*', help='video file(s)')
    ap.add_argument('--tag', action='append', default=[], help='H3 proof run tag (repeatable); resolves its mp4')
    ap.add_argument('--export', default=None, help='also write decoded audio to this .wav (for listening)')
    ap.add_argument('--json', action='store_true', help='emit a JSON report instead of text')
    args = ap.parse_args()

    paths = resolve_inputs(args.videos, args.tag)
    if not paths:
        raise SystemExit('No video given. Pass files or --tag <h3 proof tag>.')

    ff = ffmpeg_exe()
    report = []
    for path in paths:
        if not os.path.exists(path):
            print(f'MISSING {path}', file=sys.stderr)
            continue
        name = os.path.basename(path)
        info = probe(ff, path)
        if info is None:
            report.append({'file': path, 'audio': None, 'verdict': 'NO AUDIO STREAM'})
            if not args.json:
                print(f'{name}: NO AUDIO STREAM')
            continue

        rate = info['sample_rate'] or 32000
        channels = info['channels'] or 2
        samples = decode(ff, path, rate, channels)
        stats = analyse(samples, rate)
        loudness = measure_loudness(ff, path)
        entry = {'file': path, 'stream': info, 'stats': stats, 'loudness': loudness,
                 'verdict': verdict(stats, loudness)}
        report.append(entry)

        if args.export:
            os.makedirs(os.path.dirname(args.export) or '.', exist_ok=True)
            target = args.export
            if len(paths) > 1:
                stem, ext = os.path.splitext(target)
                target = f'{stem}-{os.path.splitext(name)[0]}{ext}'
            subprocess.run([ff, '-y', '-v', 'error', '-i', path, '-ac', '1', '-ar', '32000', target], check=False)
            entry['exported'] = target

        if not args.json:
            print(f'== {name}')
            print(f'   stream   : {info["codec"]}  {rate} Hz  {channels}ch  {info["duration"]}s')
            if stats is None:
                print('   stats    : too short to analyse')
            else:
                if loudness:
                    print(f'   loudness : {loudness.get("integrated_lufs", "?")} LUFS   '
                          f'true peak {loudness.get("true_peak_dbtp", "?")} dBTP   '
                          f'range {loudness.get("loudness_range_lu", "?")} LU')
                print(f'   level    : RMS {stats["rms_dbfs"]} dBFS   peak {stats["peak"]}   '
                      f'range {stats["dynamic_range_db"]} dB   stereoCorr {stats["stereo_correlation"]}')
                print(f'   per-second dBFS: {stats["per_second_dbfs"]}')
                print(f'   character: centroid {stats["centroid_hz"]} Hz   flatness {stats["flatness"]}   '
                      f'harmonicity {stats["harmonicity"]}')
                print('   band %   : ' + '  '.join(f'{k}={v}' for k, v in stats['band_pct'].items()))
            print(f'   verdict  : {entry["verdict"]}')
            if entry.get('exported'):
                print(f'   exported : {entry["exported"]}')
            print()

    if args.json:
        print(json.dumps(report, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
