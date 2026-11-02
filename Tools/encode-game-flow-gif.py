"""Encode continuously captured Editor camera frames; no concept or static-page inputs."""
import hashlib
import json
from pathlib import Path
import statistics
from PIL import Image, ImageDraw, ImageSequence, ImageStat

repo = Path(__file__).resolve().parents[1]
recording = repo.parent / "Artifacts/GameFlow/Recording"
report = json.loads((recording / "recording.json").read_text())
assert report["passed"] and report["continuousCameraFrames"] and report["frames"] > 100
paths = sorted((recording / "Frames").glob("frame-*.png"))
assert len(paths) == report["frames"]
frames = [Image.open(path).convert("RGB").resize((960, 540), Image.Resampling.LANCZOS) for path in paths]
durations = []
times = report["frameTimes"]
for index in range(len(frames)):
    seconds = times[index + 1] - times[index] if index + 1 < len(times) else .12
    durations.append(max(60, min(300, round(seconds * 100) * 10)))

# One palette avoids temporal color noise in the static world while actual UI animates.
sheet = Image.new("RGB", (960, 720))
palette_indices = [round(i * (len(frames) - 1) / 11) for i in range(12)]
for i, index in enumerate(palette_indices):
    sheet.paste(frames[index].resize((320, 180), Image.Resampling.LANCZOS), ((i % 3) * 320, (i // 3) * 180))
palette = sheet.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
indexed = [frame.quantize(palette=palette, dither=Image.Dither.FLOYDSTEINBERG) for frame in frames]
output = repo / "Packages/com.fujisheng.urui/Documentation~/Images/game-flow.gif"
output.parent.mkdir(parents=True, exist_ok=True)
indexed[0].save(output, save_all=True, append_images=indexed[1:], duration=durations,
                loop=0, optimize=True, disposal=1)

decoded = Image.open(output)
assert decoded.is_animated and decoded.n_frames > 30 and decoded.size == (960, 540)
duration = 0
dark = []
for i, frame in enumerate(ImageSequence.Iterator(decoded)):
    frame.load()
    duration += frame.info.get("duration", 0)
    stat = ImageStat.Stat(frame.convert("L"))
    if stat.mean[0] < 3 and stat.stddev[0] < 2:
        dark.append(i)
assert not dark, "Unexpected entirely dark frames: " + str(dark)
assert decoded.info.get("loop") == 0
assert output.stat().st_size < 12 * 1024 * 1024, "GIF is too large for the README"

# Local inspection sheet only, never included in the repository.
inspection = Image.new("RGB", (960, 4 * 298), "#16212b")
draw = ImageDraw.Draw(inspection)
inspection_indices = [0] + [min(report["frames"] - 1, marker["frame"] + 3) for marker in report["markers"][1:]]
for i, index in enumerate(inspection_indices):
    draw.text(((i % 2) * 480 + 10, (i // 2) * 298 + 6), "Live frame " + str(index), fill="white")
    # Identical camera frames are coalesced by GIF encoding. Locate the actual
    # decoded GIF frame by elapsed time, not by original capture frame number.
    elapsed = 0
    for gif_index in range(decoded.n_frames):
        decoded.seek(gif_index)
        elapsed += decoded.info.get("duration", 0)
        if elapsed >= sum(durations[:index + 1]):
            break
    inspection.paste(decoded.convert("RGB").resize((480, 270), Image.Resampling.LANCZOS), ((i % 2) * 480, (i // 2) * 298 + 24))
inspection.save(recording / "gif-inspection.png")
summary = {"passed": True, "source": "continuous real Unity Editor Play Mode camera frames",
           "file": str(output.relative_to(repo)), "width": 960, "height": 540,
           "frames": decoded.n_frames, "capturedFrames": report["frames"], "durationSeconds": duration / 1000,
           "averageFps": decoded.n_frames / (duration / 1000), "loop": 0, "bytes": output.stat().st_size,
           "sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "blackFrames": dark,
           "markers": report["markers"], "noDesktopOrTerminalCapture": True}
(recording / "gif-validation.json").write_text(json.dumps(summary, indent=2) + "\n")
print(json.dumps(summary))
