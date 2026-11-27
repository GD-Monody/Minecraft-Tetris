#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
「油」的三种可能，放在水和岩浆旁边对比。

同一块方块，三种油在游戏里的读法完全不同：
    琥珀燃料 → 一眼看出「可燃液体」，和金黄的沙、橙红的岩浆都不撞；
    深棕原油 → 写实，但在暗场景里和板岩/泥/灰混在一起；
    橄榄机油 → 介于两者之间，偏「工业」，但容易和叶、酸混。
"""

import math
import os
from PIL import Image

import build_paper_atlas as P

T, SCALE = P.TILE, 8


def oil_tile(base, sheen, dark, name):
    """通用的油：液体底 + 油膜反光 + 液面折痕。"""
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = img.load()
    for y in range(T):
        for x in range(T):
            k = 0.84 + 0.20 * P.hnoise(x // 2, y // 2, 41) + (0.08 if y < T / 3 else 0)
            p[x, y] = P.sh(base, k, 240)

    # 油膜：紫、绿、金三段反光，是「油」区别于「果汁」的关键
    for i, (y, col) in enumerate(((5, sheen[0]), (8, sheen[1]), (11, sheen[2]))):
        for x in range(2 + i, min(T - 2, 10 + i)):
            P.pen(p, x, y, col, 1.0, 225)

    # 暗部沉淀，别是一块平色
    for x in range(2, T - 2):
        P.pen(p, x, T - 2, dark, 1.0, 240)

    # 液面折痕
    for x in range(2, T - 2):
        y = 3 + int(round(math.sin(x * 0.8) * 0.9))
        P.pen(p, x, y, base, 1.28, 240)
        P.pen(p, x, y + 1, dark, 0.9, 240)

    return img


VARIANTS = [
    ("fuel_amber",  "琥珀燃料（推荐）", (196, 148, 62), ((120, 84, 160), (86, 150, 120), (255, 214, 130)), (110, 78, 34)),
    ("crude",       "深棕原油",       (74, 56, 44),   ((92, 72, 130), (60, 104, 92), (150, 118, 86)),  (38, 28, 24)),
    ("machine_oil", "橄榄机油",       (128, 126, 74), ((96, 84, 150), (74, 140, 110), (208, 200, 120)), (74, 72, 40)),
]

CONTEXT = [("水", P.KINDS[18]), ("岩浆", P.KINDS[19]), ("酸", P.KINDS[20])]


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rows = len(VARIANTS)
    cols = len(CONTEXT) + 1
    pad, gap = 10, 8
    cell = T * SCALE
    label_w = 132                      # 左边一列留给风格名，之前没留，整行都错位了
    w = pad + label_w + cols * (cell + gap)
    h = pad * 2 + 26 + rows * (cell + gap)
    sheet = Image.new("RGB", (w, h), (24, 22, 26))

    from PIL import ImageDraw, ImageFont
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 15)
    except Exception:
        font = ImageFont.load_default()

    x0 = pad + label_w
    for i, (cn, _k) in enumerate(CONTEXT):
        d.text((x0 + i * (cell + gap) + 4, 6), cn, font=font, fill=(150, 158, 172))
    d.text((x0 + len(CONTEXT) * (cell + gap) + 4, 6), "油（三选一）", font=font, fill=(214, 222, 236))

    for r, (key, cn, base, sheen, dark) in enumerate(VARIANTS):
        y = 26 + r * (cell + gap)
        d.text((pad, y + cell // 2), cn, font=font, fill=(214, 222, 236))
        for c, (_n, kind) in enumerate(CONTEXT):
            img = P.make(kind).resize((cell, cell), Image.NEAREST)
            sheet.paste(img, (x0 + c * (cell + gap), y), img)
        oil = oil_tile(base, sheen, dark, key).resize((cell, cell), Image.NEAREST)
        sheet.paste(oil, (x0 + len(CONTEXT) * (cell + gap), y), oil)

    out = os.path.join(root, "Logs", "oil_variants.png")
    sheet.save(out)
    print("已生成", out, sheet.size)


if __name__ == "__main__":
    main()
