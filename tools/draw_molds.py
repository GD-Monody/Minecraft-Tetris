# 把 molds_v1 里三个默认模具的形状画成一张对比图（只用于验证/展示）。
import json
import winreg
from PIL import Image, ImageDraw

KEY = r"Software\appdatalow\software\MinecraftTetris\MinecraftTetris"
SIZE = 5
CELL = 48
GAP = 4
PAD = 12
LABEL_H = 26

k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, KEY)
raw = None
i = 0
while True:
    try:
        name, val, _ = winreg.EnumValue(k, i)
        if name.startswith("molds"):
            raw = bytes(val).decode("utf-8", "replace")
        i += 1
    except OSError:
        break

data = json.loads(raw.rstrip("\x00"))
items = data["items"]

W = len(items) * (SIZE * (CELL + GAP) + PAD * 2)
H = LABEL_H + SIZE * (CELL + GAP) + PAD * 2
img = Image.new("RGB", (W, H), (17, 18, 23))
d = ImageDraw.Draw(img)

for idx, item in enumerate(items):
    ox = idx * (SIZE * (CELL + GAP) + PAD * 2) + PAD
    oy = PAD + LABEL_H
    d.text((ox, PAD), item["name"], fill=(230, 232, 238))
    cells = set()
    for part in item["cells"].split(";"):
        if not part:
            continue
        x, y = part.split(",")
        cells.add((int(x), int(y)))
    for row in range(SIZE):
        for col in range(SIZE):
            gx = col
            gy = SIZE - 1 - row  # row0 = 顶部
            x0 = ox + col * (CELL + GAP)
            y0 = oy + row * (CELL + GAP)
            on = (gx, gy) in cells
            color = (66, 190, 235) if on else (30, 33, 40)
            d.rectangle([x0, y0, x0 + CELL, y0 + CELL], fill=color)

img.save("Logs/ws_molds_shapes.png")
print("saved", img.size)
