"""Regenerates the screenshots in docs/images (README) from a headless run of the game.

Needs the game data (config.json or WC1_GAME_DIR), a Vulkan driver, Python 3 with Pillow, and a
Release build of wc1tool:

    dotnet build src/WingCommander.Tools -c Release
    python scripts/readme-screenshots.py

Every picture comes from `wc1tool snap --gpu`: the game runs on the virtual clock with scripted
input (deterministic), and each frame is rendered offscreen by the Vulkan renderer exactly like
the window. The "original" halves of the comparisons use the same renderer with the port
additions switched off (--classic-space --classic-text). The flight uses the developer switches
"Origin -k" (invulnerable player), so the scripted pilot survives the first dogfight.
"""

import os
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOL = os.path.join(ROOT, "src", "WingCommander.Tools", "bin", "Release", "net10.0", "wc1tool.dll")
OUT = os.path.join(ROOT, "docs", "images")
LABEL_FONT = os.path.join(ROOT, "assets", "fonts", "tektur", "Tektur-SBold.ttf")

# Title: S (new game) through the forced simulator run, name entry (Enter, Enter) and ranking.
NEW_GAME = "2000 key 0x1f; 4000 key 0x1f; 6000 key 0x1f; 17000 key 0x1c 0x0d; 18500 key 0x1c 0x0d; 20000 key 0x39 0x20"

# Bar: chalkboard (kill board), leave it, talk to Paladin.
BAR = NEW_GAME + "; 24000 click 215 60; 28000 key 0x39 0x20; 30000 click 185 110"

# Barracks door, mission hangar, skip the briefing (Esc), autopilot (A), lock target (L), hide the
# key help (F10), chase view (F5), target view (F7), guns (Space), cockpit (F1), pause menu (Esc),
# cursor out of the way (the first move after the flight's pointer warp is ignored), Settings,
# Flight keys (Up twice from the first row).
FLIGHT = (NEW_GAME + "; 27000 click 300 100; 30000 click 300 60; 37000 key 0x01 0x1b; 51000 key 0x1e 0x41; "
          "61000 key 0x26 0x4c; 62500 key 0x44 0x79; 63000 key 0x3f 0x74; 70000 key 0x41 0x76; "
          + "; ".join(f"{62000 + i * 700} key 0x39 0x20" for i in range(21))
          + "; 78000 key 0x3b 0x70; 79000 key 0x01 0x1b; 79500 move 316 196; 79600 move 316 196; "
          "81000 key 0x50 0x28; 81500 key 0x1c 0x0d; 84000 key 0x48 0x26; 84300 key 0x48 0x26; 84600 key 0x1c 0x0d")

CLASSIC = ["--classic-space", "--classic-text"]
CHEATS = "Origin -k x"  # the last argument is dropped by the original's argument loop


def snap(work, name, size, script, times, extra=(), args=None):
    """Runs wc1tool snap and returns {time: image} of the Vulkan frames."""
    out = os.path.join(work, name)
    command = ["dotnet", TOOL, "snap", "--skip-intro", "--gpu", size, "--input", script,
               "--at", ",".join(str(t) for t in times), "--out", out, *extra]
    if args:
        command += ["--args", args]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"{name}: wc1tool snap failed\n{result.stdout}\n{result.stderr}")
    print(f"captured {name}")
    return {t: Image.open(os.path.join(out, f"snap_{t:06d}_gpu.png")).convert("RGB") for t in times}


def save(image, name):
    path = os.path.join(OUT, name)
    image.save(path, optimize=True)
    print(f"{name:24} {image.width}x{image.height}  {os.path.getsize(path) // 1024} KB")


def label(image, text, size):
    """The image under a header strip that names it."""
    font = ImageFont.truetype(LABEL_FONT, size)
    header = size + 18
    canvas = Image.new("RGB", (image.width, image.height + header), (24, 24, 24))
    ImageDraw.Draw(canvas).text((12, header // 2), text, font=font, fill=(255, 220, 64), anchor="lm")
    canvas.paste(image, (0, header))
    return canvas


def side_by_side(images, horizontal, gap=6):
    width = sum(i.width for i in images) + gap * (len(images) - 1) if horizontal else max(i.width for i in images)
    height = max(i.height for i in images) if horizontal else sum(i.height for i in images) + gap * (len(images) - 1)
    canvas = Image.new("RGB", (width, height), (24, 24, 24))
    position = 0
    for image in images:
        canvas.paste(image, (position, 0) if horizontal else (0, position))
        position += (image.width if horizontal else image.height) + gap
    return canvas


def main():
    if not os.path.exists(TOOL):
        sys.exit("wc1tool not found: dotnet build src/WingCommander.Tools -c Release")
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="wc1-screenshots-") as work:
        wide = snap(work, "flight-wide", "1920x1080", FLIGHT, [58000, 73250], args=CHEATS)
        flight = snap(work, "flight", "1440x1080", FLIGHT, [36000, 73250, 80000, 83000, 86000], args=CHEATS)
        flight_classic = snap(work, "flight-classic", "1440x1080", FLIGHT, [36000, 73250], CLASSIC, args=CHEATS)
        bar = snap(work, "bar", "1440x1080", BAR, [26000, 33000])
        bar_classic = snap(work, "bar-classic", "1440x1080", BAR, [26000], CLASSIC)

        save(wide[73250], "space-combat.png")
        save(wide[58000], "cockpit-key-help.png")
        save(flight[80000], "pause-menu.png")
        save(flight[83000], "settings.png")
        save(flight[86000], "flight-keys.png")
        save(flight[36000], "briefing.png")
        save(bar[26000], "kill-board.png")
        save(bar[33000], "conversation.png")

        ships = (560, 120, 1440, 600)
        save(side_by_side([label(flight_classic[73250].crop(ships), "ORIGINAL  320x200", 30),
                           label(flight[73250].crop(ships), "REFLIGHT", 30)], horizontal=True), "compare-ships.png")

        subtitle = (30, 845, 1420, 995)
        save(side_by_side([label(flight_classic[36000].crop(subtitle), "ORIGINAL", 26),
                           label(flight[36000].crop(subtitle), "REFLIGHT", 26)], horizontal=False), "compare-text.png")

        board = (0, 170, 980, 780)
        save(side_by_side([label(flight_classic[36000].crop(board), "ORIGINAL", 30),
                           label(flight[36000].crop(board), "REFLIGHT", 30)], horizontal=True), "compare-board.png")

        half = (720, 540)
        save(side_by_side([label(bar_classic[26000].resize(half, Image.LANCZOS), "ORIGINAL", 22),
                           label(bar[26000].resize(half, Image.LANCZOS), "REFLIGHT", 22)], horizontal=True),
             "compare-kill-board.png")


if __name__ == "__main__":
    main()
