#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
演示：多色调色板 + 每面一张图，能不能用代码画出来。

和现在图集的两个差别：
    1. 每块不是「一个基色 × 明暗」，而是一组 4~7 个**不同色相**的颜色，
       噪声决定落在哪个颜色上 —— 这就是原版看着「有东西」的原因；
    2. 顶 / 侧 / 底各画一张，草方块侧面还有一圈垂下来的草边。

产物：Logs/faces_demo.png —— 上排是 3 个面的贴图，下排是拼出来的立体块。
"""

import math
import os
import random
from PIL import Image, ImageDraw, ImageFont

T = 16


def h(x, y, s=0):
    n = (x * 374761393 + y * 668265263 + s * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def pick(pal, t):
    """按 0..1 落在调色板的哪一档。调色板是一组「不同色相」的颜色，不是明暗。"""
    i = min(len(pal) - 1, max(0, int(t * len(pal))))
    return pal[i]


# ── 调色板：每块 5~7 个颜色，色相真的不一样 ────────────────────────────
# 泥土：棕 + 橙褐 + 暗紫褐 + 米点（不是同一个棕的四种亮度）
DIRT = [(120, 86, 58), (98, 66, 44), (86, 58, 44), (74, 50, 46), (146, 112, 78), (168, 142, 112)]
GRASS_TOP = [(96, 152, 68), (78, 130, 56), (118, 174, 82), (64, 108, 48), (146, 190, 96)]
GRASS_SIDE = DIRT
STONE = [(128, 128, 132), (108, 108, 116), (146, 146, 150), (92, 94, 104),
         (166, 166, 168), (118, 122, 132)]
SAND = [(226, 204, 152), (238, 220, 172), (206, 182, 132), (246, 232, 192), (188, 164, 118)]
WATER = [(52, 104, 186), (40, 84, 164), (74, 132, 208), (96, 158, 226), (28, 64, 132)]
ROCK = [(126, 120, 114), (106, 98, 92), (146, 138, 130), (90, 84, 80), (162, 154, 146)]


def face(kind, which, pal, salt=0):
    """画一个面。which: top / side / bottom —— 三面的噪点尺度、明暗、条纹都不同。"""
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = img.load()

    for y in range(T):
        for x in range(T):
            if which == "top":
                # 顶面：颗粒细一点、受光最亮
                n = h(x, y, salt) * 0.7 + h(x // 2, y // 2, salt + 3) * 0.3
                n = min(1.0, n * 1.12)
            elif which == "bottom":
                # 底面：最暗、噪声粗
                n = h(x // 2, y // 2, salt + 7) * 0.85
            else:
                # 侧面：竖向拉长的条纹（像被挤压过），明暗居中
                n = h(x, y // 3, salt + 11) * 0.6 + h(x // 3, y, salt + 13) * 0.4
            p[x, y] = pick(pal, n) + (255,)

    # 材质专属：只在特定面上加
    if kind == "grass" and which == "side":
        rnd = random.Random(5)
        for x in range(T):                       # 顶部垂下一圈草边，长短不齐
            depth = 3 + int(rnd.random() * 3)
            for y in range(depth):
                p[x, y] = pick(GRASS_TOP, 0.4 + rnd.random() * 0.5) + (255,)
    if kind == "grass" and which == "top":
        rnd = random.Random(9)
        for _ in range(18):                      # 草叶：一亮一暗
            x, y = rnd.randrange(T), rnd.randrange(T)
            p[x, y] = pick(GRASS_TOP, 0.85) + (255,)
    if kind == "stone" and which in ("top", "side"):
        for i in range(T):                       # 一道裂缝，顶面和侧面走向不同
            if which == "top":
                p[min(T - 1, 4 + i // 2), i] = pick(STONE, 0.1) + (255,)
            else:
                p[i, min(T - 1, 6 + int(math.sin(i * 0.5) * 2))] = pick(STONE, 0.1) + (255,)
    if kind == "water":
        for base in (4, 9, 13):                  # 浪脊
            for x in range(1, T - 1):
                y = base + int(round(math.sin(x * 0.7 + base) * 0.8))
                p[x, y] = (150, 210, 246, 255)
                p[x, y + 1] = pick(WATER, 0.05) + (255,)

    # 边缘：上面的面亮、下面的面暗
    for x in range(T):
        p[x, 0] = tuple(min(255, int(v * 1.18)) for v in p[x, 0][:3]) + (255,)
        p[x, T - 1] = tuple(int(v * 0.82) for v in p[x, T - 1][:3]) + (255,)
    return img


BLOCKS = [
    ("草方块", "grass", GRASS_TOP, GRASS_SIDE, DIRT),
    ("石", "stone", STONE, STONE, STONE),
    ("沙", "sand", SAND, SAND, SAND),
    ("水", "water", WATER, WATER, WATER),
    ("碎石", "rock", ROCK, ROCK, ROCK),
]

SCALE = 6
DX, DY = 26, 26          # 侧面投影宽度 / 顶面投影高度


def iso_mock(tops, sides, bottoms):
    """拼一个「伪立体」块：顶面压扁一条、右侧面压窄一条、正面照原样。

    注意三个面用的是三张不同的图：顶面走 tops、正面和右侧面走 sides。
    之前这里把正面拉伸成 (w-DX, w-DY)，结果面被拉成竖条 —— 定尺寸不能拉伸，
    只能「压扁一条」「压窄一条」，正面保持 1:1。
    """
    cell = T * SCALE
    w = cell + DX
    h = cell + DY
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))

    top = tops.resize((cell, DY), Image.LANCZOS)          # 顶面：压扁
    side = sides.resize((DX, cell + DY), Image.LANCZOS)   # 右侧面：压窄
    front = sides.resize((cell, cell), Image.NEAREST)     # 正面：原样

    img.paste(top, (0, 0), top)
    img.paste(side, (cell, DY), side)
    img.paste(front, (0, DY), front)
    return img


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    cell = T * SCALE
    pad = 12
    w = pad + (cell + pad) * len(BLOCKS)
    h = pad * 2 + 22 + cell + 30 + (cell + DY)
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 14)
    except Exception:
        font = ImageFont.load_default()

    for i, (cn, kind, top_pal, side_pal, bot_pal) in enumerate(BLOCKS):
        x = pad + i * (cell + pad)
        top = face(kind, "top", top_pal, 1)
        side = face(kind, "side", side_pal, 2)
        bot = face(kind, "bottom", bot_pal, 3)
        d.text((x, 6), cn, font=font, fill=(210, 216, 226))

        # 上排：三个面的原图（顶 / 侧 / 底）
        for j, f in enumerate((top, side, bot)):
            big = f.resize((cell // 3, cell // 3), Image.NEAREST)
            sheet.paste(big, (x + j * (cell // 3 + 2), 26), big)

        # 下排：拼成立体
        mock = iso_mock(top, side, bot)
        big = mock.resize((mock.width * 2, mock.height * 2), Image.NEAREST)
        sheet.paste(big, (x, 26 + cell // 3 + 16), big)

    out = os.path.join(root, "Logs", "faces_demo.png")
    sheet.save(out)
    print("已生成", out, sheet.size)


if __name__ == "__main__":
    main()
