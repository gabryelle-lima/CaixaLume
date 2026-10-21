"""Gera o ícone do aplicativo a partir da geometria em Assets/mercado.svg."""

from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
assets = root / "Assets"
assets.mkdir(exist_ok=True)

size = 1024
image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((56, 56, 968, 968), radius=224, fill="#101b24", outline="#167c61", width=40)
draw.line([(282, 708), (282, 330), (512, 566), (742, 330), (742, 708)], fill="#38d5a2", width=100, joint="curve")
for x, y in [(282, 708), (282, 330), (512, 566), (742, 330), (742, 708)]:
    draw.ellipse((x - 50, y - 50, x + 50, y + 50), fill="#38d5a2")

image.save(assets / "mercado.png", optimize=True)
image.save(assets / "mercado.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
