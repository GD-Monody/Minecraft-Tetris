#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA 材质生成器 v3 —— 「颗粒法」。

前三版失败的原因都想清楚了，这一版的原则从它们反推：

    ✗ 逐像素随机取色      → 雪花屏（相邻像素总在跳色）
    ✗ value noise + 量化  → 大片糊色（插值把边界抹掉了）
    ✓ 颗粒法：底色铺一层，
      再撒 12~18 颗「实色小颗粒」——每颗是一小片 3~7 像素的不规则实色区，
      颗粒之间明度只差一档，另掺几粒 1px 的深色/亮色点缀。

「颗粒」用随机游走生成：从一点出发走 4~9 步、每步随机转向，
路径上的像素都涂成同一档色 —— 出来的形状天然不规则、边缘有锯齿，
这正是像素画里「团块」的画法。

可平铺：所有坐标都 % 16，跨边的颗粒会从另一侧续上，3x3 平铺无缝。

每个面单独生成（顶/侧/底），侧面把游走方向偏向竖直，底面整体压暗一档。
"""

import math
import os
import random
from PIL import Image

T = 16
PAD = 1
COLS = 8


# ── 调色板：每块 5 档，暗部偏冷/偏紫、亮部偏暖，明度相邻差 10% 左右 ──────
STONE = [(158, 160, 172), (140, 142, 154), (122, 124, 138), (104, 106, 120), (88, 90, 104)]
DIRT = [(168, 124, 80), (148, 106, 66), (128, 88, 54), (106, 70, 44), (86, 56, 38)]
SAND = [(240, 224, 178), (226, 208, 158), (210, 190, 138), (192, 170, 118), (174, 150, 100)]
GRAVEL = [(148, 144, 138), (130, 126, 122), (112, 108, 106), (94, 92, 92), (78, 76, 78)]
GRASS = [(122, 178, 80), (104, 158, 68), (86, 136, 56), (68, 114, 46), (52, 94, 38)]
LOG = [(132, 98, 60), (116, 84, 50), (100, 70, 42), (84, 58, 36), (70, 48, 30)]
WATER = [(66, 118, 196), (54, 100, 178), (44, 84, 158), (36, 70, 136), (28, 56, 112)]
LAVA = [(240, 140, 52), (226, 108, 44), (206, 80, 36), (178, 58, 30), (146, 42, 26)]


def px(img):
    return img.load()


def grain(p, x0, y0, steps, pal_i, rng, vertical=0.0):
    """随机游走撒一颗颗粒：路径上的像素全涂 pal[pal_i]，坐标取模保证可平铺。"""
    x, y = x0 % T, y0 % T
    dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
    for _ in range(steps):
        p[x, y] = pal_i
        # 转向：vertical>0 时偏竖直（侧面纹理是纵向拉的）
        if rng.random() < 0.45 + vertical * 0.35:
            dx, dy = (0, rng.choice((1, -1))) if vertical > 0 else rng.choice(((1, 0), (-1, 0)))
        else:
            dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
        x = (x + dx) % T
        y = (y + dy) % T


def specks(p, n, pal_i, rng):
    for _ in range(n):
        x, y = rng.randrange(T), rng.randrange(T)
        p[x, y] = pal_i


def face(pal, salt, which="side", kind="plain"):
    """画一个面：先在序号网格上撒颗粒，最后统一映射成颜色。"""
    rng = random.Random(salt)
    grid = [[0] * T for _ in range(T)]

    base = {"top": 1, "side": 2, "bottom": 3}[which]
    for y in range(T):
        for x in range(T):
            grid[y][x] = base

    def grain(x0, y0, steps, tone, vertical=0.0):
        x, y = x0 % T, y0 % T
        dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
        for _ in range(steps):
            grid[y][x] = tone
            if rng.random() < 0.45 + vertical * 0.35:
                dx, dy = (0, rng.choice((1, -1))) if vertical > 0 else rng.choice(((1, 0), (-1, 0)))
            else:
                dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
            x = (x + dx) % T
            y = (y + dy) % T

    def specks(n, tone):
        for _ in range(n):
            grid[rng.randrange(T)][rng.randrange(T)] = tone

    if which == "top":
        for i in range(9):
            grain(rng.randrange(T), rng.randrange(T), rng.randrange(3, 6),
                  rng.choice((0, 1, 3)), 0.0)
        specks(3, 4); specks(4, 0)
    elif which == "bottom":
        for i in range(14):
            grain(rng.randrange(T), rng.randrange(T), rng.randrange(5, 9),
                  rng.choice((3, 4, 4)), 0.0)
        specks(4, 1)
    else:
        for i in range(13):
            grain(rng.randrange(T), rng.randrange(T), rng.randrange(4, 8),
                  rng.choice((1, 3, 3, 0)), 0.55)
        specks(5, 4); specks(3, 0)

    # 材质专属笔触
    if kind == "grass_side":
        depth = [4 + rng.randrange(3) for _ in range(T)]
        for x in range(T):
            for y in range(depth[x]):
                grid[y][x] = 1 + (x + y) % 2      # 草：亮绿/中绿交替
        for x in range(0, T, 3):                   # 垂下来的草根
            grid[depth[x]][x] = 0
            if rng.random() < 0.5:
                grid[(depth[x] + 1) % T][x] = 3
    elif kind == "grass_top":
        for i in range(10):
            x, y = rng.randrange(T), rng.randrange(T)
            grid[y][x] = 0
            grid[(y + 1) % T][x] = 4
    elif kind == "log_side":
        for x in range(T):
            if x % 3 == 0:
                for y in range(T):
                    grid[y][x] = 3 if (y + x) % 5 < 3 else 2
    elif kind == "plank":
        for y in (0, 5, 10, 15):
            for x in range(T):
                grid[y][x] = 4
        for x in (5, 12):
            for y in list(range(1, 5)) + list(range(6, 10)) + list(range(11, 15)):
                grid[y][x] = 4
    elif kind == "water":
        for base in (3, 8, 13):
            for x in range(T):
                y = (base + int(round(math.sin(x * 0.6 + base) * 1.2))) % T
                grid[y][x] = 0
                grid[(y + 1) % T][x] = 4
    elif kind == "lava":
        for i in range(3):
            y0 = rng.randrange(2, T - 2)
            y = y0
            for x in range(T):
                grid[y][x] = 0
                grid[(y + 1) % T][x] = 4
                if rng.random() < 0.4:
                    y = max(1, min(T - 2, y + rng.choice((-1, 1))))

    # 上色
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = px(img)
    for y in range(T):
        for x in range(T):
            p[x, y] = pal[grid[y][x]] + (255,)
    return img


# 面定义：名字 -> (调色板, 专属笔触标记)
BLOCKS = [
    ("stone", STONE, "plain"),
    ("dirt", DIRT, "plain"),
    ("grass_top", GRASS, "grass_top"),
    ("grass_side", DIRT, "grass_side"),
    ("sand", SAND, "plain"),
    ("gravel", GRAVEL, "plain"),
    ("log", LOG, "log_side"),
    ("planks", LOG, "plank"),
    ("water", WATER, "water"),
    ("lava", LAVA, "lava"),
]


def build():
    cell = T + PAD * 2
    faces_total = len(BLOCKS) * 3
    rows = (faces_total + COLS - 1) // COLS
    atlas = Image.new("RGBA", (COLS * cell, rows * cell), (0, 0, 0, 0))
    tiles = []
    idx = 0
    for i, (name, pal, mark) in enumerate(BLOCKS):
        faces = []
        for f, which in enumerate(("top", "side", "bottom")):
            im = face(pal, 100 + idx, which, mark if which in ("top", "side") else "plain")
            atlas.paste(im, ((idx % COLS) * cell + PAD, (idx // COLS) * cell + PAD))
            faces.append(im)
            idx += 1
        tiles.append((name, faces))
    return atlas, tiles


def preview(tiles, scale=8):
    """每块一行：顶 / 侧 / 底 三个面并排，放大看。"""
    gap = 10
    fw = T * scale // 2
    w = gap + len(tiles) * (fw * 3 + gap)
    h = gap + len(tiles) * (T * scale + gap)
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    for i, (name, faces) in enumerate(tiles):
        y = gap + i * (T * scale + gap)
        for j, f in enumerate(faces):
            big = f.resize((fw, fw), Image.NEAREST)
            sheet.paste(big, (gap + j * (fw + 4), y), big)
    return sheet


def wall(tiles, layout, scale=6):
    """立体墙预览：顶面压扁一条 + 正面 + 右侧压窄一条。"""
    by_name = dict(tiles)
    DX, DY = max(4, T // 3), max(4, T // 3)
    cols = len(layout[0])
    w = cols * T + DX + 12
    h = len(layout) * (T + DY) + 12
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    for row in range(len(layout) - 1, -1, -1):
        for col in range(cols):
            name = layout[row][col]
            faces = by_name.get(name) or by_name["stone"]
            top, side, bottom = faces
            x = 6 + col * T
            y = 6 + row * (T + DY)
            t2 = top.resize((T, DY), Image.LANCZOS)
            s2 = side.resize((DX, T + DY), Image.LANCZOS)
            sheet.paste(t2, (x, y), t2)
            sheet.paste(s2, (x + T, y + DY), s2)
            sheet.paste(side, (x, y + DY), side)
    return sheet


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "Assets", "Art", "AtlasGrain")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(os.path.join(root, "Logs"), exist_ok=True)

    atlas, tiles = build()
    atlas.save(os.path.join(out_dir, "atlas.png"))

    WALL = [
        ["grass_top", "grass_top", "grass_top", "grass_top", "grass_top", "grass_top", "grass_top", "grass_top"],
        ["dirt", "dirt", "dirt", "dirt", "dirt", "dirt", "dirt", "dirt"],
        ["stone", "stone", "stone", "stone", "stone", "stone", "stone", "stone"],
        ["gravel", "gravel", "gravel", "gravel", "sand", "sand", "sand", "sand"],
        ["log", "planks", "planks", "log", "planks", "planks", "log", "log"],
        ["water", "water", "water", "water", "lava", "lava", "lava", "lava"],
    ]
    preview(tiles).save(os.path.join(root, "Logs", "grain_preview.png"))
    wall(tiles, WALL).save(os.path.join(root, "Logs", "grain_wall.png"))
    print("图集 %dx%d，%d 块 x 3 面" % (atlas.width, atlas.height, len(BLOCKS)))
