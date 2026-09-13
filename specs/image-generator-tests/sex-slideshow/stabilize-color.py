"""Colour-stabilise a chain frame against a reference frame.

WHY THIS EXISTS
  In a chained edit run each frame becomes the next step's reference. At CFG 1 the sampler has no
  negative-branch pressure, so a small colour cast in frame N is adopted as conditioning by frame N+1 and
  amplified. Measured on the 19-step sex-slideshow (2026-09-12):

    step01 reference .......... background (17,19,21)  saturation  50
    1 link (step02) ........... background (21,22,26)  saturation  46
    1 link, step-19 prompt ..... background (23,23,28)  saturation  48   <-- same prompt as the chained end
    18 links (chained step19) .. background (99,56,86)  saturation 120   <-- REMIX
    18 links (chained step19) .. background (77,27,46)  saturation 153   <-- v23

  The cast is a function of CHAIN LENGTH, not of the checkpoint or the LoRA: the same prompt and seed
  applied as ONE edit produced no drift at all. This script applies a per-channel mean/std transfer so the
  frame fed forward matches the reference's colour statistics, which breaks the feedback loop at source.

  Apply it to the frame that is FED FORWARD, not to the saved output: if the input to a link is clean, the
  model's own output for that link is clean too, so the saved frames improve without being altered.

Usage:
  stabilize-color.py <reference> <input> <output> [--full]

The default is MEAN OFFSET ONLY, because the measured drift is overwhelmingly a mean shift (backdrop
17,19,21 -> 99,56,86). Cancelling the offset removes the cast without touching contrast. Full mean/std
matching also restores the backdrop but OVER-SATURATES the subject -- it stretches the subject's channels
while dragging the large dark background back, which measured saturation UP (120 -> 140) on the worst
frame. Only pass --full if contrast/range is also drifting.
"""
import sys

from PIL import Image, ImageStat

if len(sys.argv) not in (4, 5):
    raise SystemExit("usage: stabilize-color.py <reference> <input> <output> [--full]")

reference_path, input_path, output_path = sys.argv[1], sys.argv[2], sys.argv[3]

with Image.open(reference_path) as image:
    reference = image.convert("RGB")
with Image.open(input_path) as image:
    frame = image.convert("RGB")

reference_stats = ImageStat.Stat(reference)
frame_stats = ImageStat.Stat(frame)

full = "--full" in sys.argv[4:]

out_channels = []
for index, band in enumerate(frame.split()):
    mean_ref = reference_stats.mean[index]
    mean_in = frame_stats.mean[index]

    if full:
        std_ref = reference_stats.stddev[index]
        std_in = frame_stats.stddev[index]
        gain = (std_ref / std_in) if std_in > 1e-6 else 1.0

        def remap(value, m=mean_in, g=gain, mr=mean_ref):
            return max(0, min(255, int(round((value - m) * g + mr))))
    else:
        offset = mean_ref - mean_in

        def remap(value, o=offset):
            return max(0, min(255, int(round(value + o))))

    out_channels.append(band.point(remap))

Image.merge("RGB", out_channels).save(output_path)

print(
    f"stabilized ({'mean+std' if full else 'mean-offset'}) {input_path} -> {output_path} | "
    f"frame mean=({frame_stats.mean[0]:.0f},{frame_stats.mean[1]:.0f},{frame_stats.mean[2]:.0f}) "
    f"reference mean=({reference_stats.mean[0]:.0f},{reference_stats.mean[1]:.0f},{reference_stats.mean[2]:.0f})"
)
