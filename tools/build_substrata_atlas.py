#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA 正式材质生成器 —— 颗粒法 v3 推广到全表。

和实验版（build_grain_atlas.py）的差别：
    · 覆盖全部 54 个方块（和 blocks.json 的方块表一一对应）；
    · 每块顶/侧/底三面，侧面竖拉、底面压暗；草方块三面用三套调色板；
    · 机制块保留招牌笔触（浪脊/熔线/气泡/油膜/斜纹/编织/叶脉/树皮/年轮）；
    · 12 个染色纸由程序从色相环生成调色板（暗部统一偏冷，不会发灰）；
    · 同步导出 blocks.json（id/中文名/机制标签），图集和方块表永远同源。

用法：
    py -3 tools/build_substrata_atlas.py
产物：
    Assets/Art/AtlasSubstrata/atlas.png   18 列 x 9 行，每格 16px + 1px padding
    Assets/Art/AtlasSubstrata/blocks.json / blocks.txt
    Logs/substrata_preview.png            每块侧面的总览
    Logs/substrata_wall.png               拼成立体墙
"""

import colorsys
import json
import math
import os
import random
from PIL import Image

T = 16
PAD = 1
COLS = 18

# ── 调色板（5 档：0 最亮 → 4 最暗；暗部偏冷、亮部偏暖）──────────────────
STONE = [(152, 152, 160), (136, 136, 146), (120, 120, 132), (102, 104, 118), (86, 88, 104)]
DIRT = [(172, 126, 82), (152, 106, 66), (130, 88, 54), (108, 70, 44), (86, 54, 38)]
GRASS = [(122, 178, 80), (104, 158, 68), (86, 136, 56), (68, 114, 46), (52, 94, 38)]
SAND = [(240, 224, 178), (226, 208, 158), (210, 190, 138), (192, 170, 118), (174, 150, 100)]
GRAVEL = [(148, 144, 138), (130, 126, 122), (112, 108, 106), (94, 92, 92), (78, 76, 78)]
LOG = [(132, 98, 60), (116, 84, 50), (100, 70, 42), (84, 58, 36), (70, 48, 30)]
WATER = [(66, 118, 196), (54, 100, 178), (44, 84, 158), (36, 70, 136), (28, 56, 112)]
LAVA = [(240, 140, 52), (226, 108, 44), (206, 80, 36), (178, 58, 30), (146, 42, 26)]
ACID = [(178, 208, 84), (158, 188, 72), (136, 166, 60), (112, 142, 50), (90, 116, 40)]
FUEL = [(214, 164, 76), (196, 148, 62), (176, 130, 50), (152, 110, 40), (126, 90, 32)]
PAPER = [(238, 234, 226), (224, 218, 206), (208, 200, 186), (190, 180, 164), (170, 158, 142)]
GLASSY = [(206, 234, 244), (188, 222, 236), (168, 206, 226), (146, 188, 214), (124, 168, 200)]
LEAF = [(118, 172, 96), (100, 152, 82), (84, 130, 68), (68, 108, 56), (52, 86, 46)]
METAL = [(196, 200, 208), (176, 180, 190), (156, 160, 172), (134, 138, 152), (112, 116, 132)]
SNOW = [(248, 250, 254), (240, 244, 250), (230, 236, 244), (218, 226, 238), (204, 214, 230)]


BEDROCK = [(96, 96, 104), (78, 78, 88), (60, 60, 72), (44, 44, 56), (118, 116, 124)]
OBSIDIAN = [(56, 44, 78), (46, 36, 66), (36, 28, 54), (28, 22, 44), (70, 58, 96)]
ICE = [(188, 220, 244), (172, 208, 238), (154, 194, 230), (136, 180, 222), (210, 236, 250)]
STRUCT = [(148, 150, 172), (124, 126, 152), (100, 102, 134), (78, 80, 114), (186, 190, 216)]


def m_structure(g):
    """结构方块：亮边框 + 内场 + 四角暗标，一眼看出「这不是普通方块」。"""
    for i in range(T):
        g[0][i] = g[T - 1][i] = 0
        g[i][0] = g[i][T - 1] = 0
    for y in range(2, T - 2):
        for x in range(2, T - 2):
            g[y][x] = 2
    for (x, y) in ((2, 2), (T - 3, 2), (2, T - 3), (T - 3, T - 3)):
        for dy in range(2):
            for dx in range(2):
                g[y + dy][x + dx] = 4
    for y in range(7, 9):
        for x in range(7, 9):
            g[y][x] = 1


def m_ice(g, rng):
    """冰：两道短裂纹。"""
    for (x0, y0) in ((3, 3), (10, 9)):
        for k in range(5):
            x, y = x0 + k, y0 + (k % 3)
            if 0 <= x < T and 0 <= y < T:
                g[y][x] = 0


def dye_palette(hue):
    """染色纸调色板：同一色相 5 档，暗部往冷偏（降 R 升 B），亮部往黄偏。"""
    out = []
    for i, (v, sat) in enumerate(((0.86, 0.52), (0.78, 0.50), (0.68, 0.48),
                                  (0.58, 0.46), (0.46, 0.42))):
        r, g, b = colorsys.hsv_to_rgb(hue, sat, v)
        # 暗部往冷偏：蓝多加一点、红少加一点
        b_shift = (4 - i) * 6
        out.append((min(255, int(r * 255)), min(255, int(g * 255)),
                    min(255, int(b * 255 + b_shift))))
    return out


DYE_HUES = {
    "dye_red": 0.995, "dye_orange": 0.08, "dye_yellow": 0.13, "dye_green": 0.30,
    "dye_teal": 0.46, "dye_blue": 0.58, "dye_purple": 0.74, "dye_pink": 0.90,
    "dye_brown": 0.07, "dye_gray": 0.62, "dye_black": 0.62, "dye_white": 0.11,
}
DYE_VAL = {"dye_gray": (0.62, 0.52), "dye_black": (0.30, 0.26), "dye_white": (0.96, 0.86)}


def h(x, y, s=0):
    n = (x * 374761393 + y * 668265263 + s * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def px(img):
    return img.load()


# ── 通用颗粒层 ──────────────────────────────────────────────────────────


def grain_field(g, rng, base, which, count=None, vertical=0.0):
    """底色 + 颗粒。top 亮而少、side 多且竖拉、bottom 暗而大。"""
    b = {"top": 1, "side": 2, "bottom": 3}[which]
    for y in range(T):
        for x in range(T):
            g[y][x] = b

    n = count if count else {"top": 9, "side": 13, "bottom": 14}[which]
    tone_choice = {"top": (0, 1, 3), "side": (1, 3, 3, 0), "bottom": (3, 4, 4)}[which]
    steps = {"top": (3, 6), "side": (4, 8), "bottom": (5, 9)}[which]
    v = vertical if which == "side" else 0.0
    for _ in range(n):
        grain(g, rng.randrange(T), rng.randrange(T), rng.randrange(*steps),
              rng.choice(tone_choice), rng, v)


def grain(g, x0, y0, steps, tone, rng, vertical=0.0):
    x, y = x0 % T, y0 % T
    dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
    for _ in range(steps):
        g[y][x] = tone
        if rng.random() < 0.45 + vertical * 0.35:
            dx, dy = (0, rng.choice((1, -1))) if vertical > 0 else rng.choice(((1, 0), (-1, 0)))
        else:
            dx, dy = rng.choice(((1, 0), (-1, 0), (0, 1), (0, -1)))
        x = (x + dx) % T
        y = (y + dy) % T


def specks(g, n, tone, rng):
    for _ in range(n):
        g[rng.randrange(T)][rng.randrange(T)] = tone


def edges(g, which):
    """顶面最上一行提亮、底面最下一行压暗（同色相，不引白色）。"""
    if which == "top":
        for x in range(T):
            g[0][x] = max(0, g[0][x] - 1)
    elif which == "side":
        for x in range(T):
            g[0][x] = max(0, g[0][x] - 1)
            g[T - 1][x] = min(4, g[T - 1][x] + 1)


# ── 机制块与材质块的专属笔触（都只动序号，不碰颜色）────────────────────


def m_grass_side(g, rng):
    depth = [4 + rng.randrange(3) for _ in range(T)]
    for x in range(T):
        for y in range(depth[x]):
            g[y][x] = 1 + (x + y) % 2
        g[depth[x]][x] = 0
        if rng.random() < 0.5:
            g[(depth[x] + 1) % T][x] = 2


def m_grass_top(g, rng):
    for _ in range(10):
        x, y = rng.randrange(T), rng.randrange(T)
        g[y][x] = 0
        g[(y + 1) % T][x] = 4


def m_log_side(g):
    for x in range(T):
        if x % 3 == 0:
            for y in range(T):
                g[y][x] = 3 if (y + x) % 5 < 3 else 2


def m_log_top(g, rng):
    cx = cy = T // 2
    for ring in range(3, T // 2, 2):            # 年轮
        for a in range(0, 360, 15):
            x = cx + int(round(math.cos(math.radians(a)) * ring))
            y = cy + int(round(math.sin(math.radians(a)) * ring))
            if 0 <= x < T and 0 <= y < T:
                g[y][x] = 3 if ring % 4 == 3 else 2


def m_plank(g):
    """木板：横缝 + 每行错开的竖缝（之前竖缝没错开，看着像华夫饼）。"""
    joints = [3, 9, 5, 12]
    for row, y0 in enumerate((1, 6, 11)):
        for x in range(T):
            g[y0 - 1][x] = 4
        for x in range(joints[row], T, 7):
            for y in range(y0, min(y0 + 4, T)):
                g[y][x] = 4


def m_brick(g):
    for y0 in (0, 5, 10):
        for x in range(T):
            g[y0 + 4 if y0 + 4 < T else T - 1][x] = 4
    for row, y0 in enumerate((0, 5, 10)):
        off = 0 if row % 2 == 0 else 4
        for x in range(off, T, 8):
            for y in range(y0, min(y0 + 4, T)):
                g[y][x] = 4


def m_cobble(g, rng):
    """圆石：抖动种子 + 最近邻，缝在中线上（实验里验证过的那套）。"""
    seeds = []
    for gy in range(4):
        for gx in range(4):
            seeds.append((gx * 4 + 2 + rng.randint(-1, 1),
                          gy * 4 + 2 + rng.randint(-1, 1), rng.random()))
    for y in range(T):
        for x in range(T):
            ds = sorted(((x - sx) ** 2 + (y - sy) ** 2, t) for sx, sy, t in seeds)
            d1, t = ds[0]
            d2 = ds[1][0]
            if math.sqrt(max(0.0, d2 * d2 - d1 * d1)) < 0.9:
                g[y][x] = 4
            else:
                g[y][x] = min(4, max(0, int(t * 3) + (0 if d1 < 6 else 1)))


def m_water(g):
    for base in (3, 8, 13):
        for x in range(T):
            y = (base + int(round(math.sin(x * 0.6 + base) * 1.2))) % T
            g[y][x] = 0
            g[(y + 1) % T][x] = 4


def m_lava(g, rng):
    """熔线：亮线走两三条，但每条中间断开几像素，避免铁丝感。"""
    for i in range(3):
        y0 = rng.randrange(2, T - 2)
        y = y0
        gap_at = rng.randrange(4, 10)
        for x in range(T):
            if (x + gap_at) % 6 < 2:
                y = max(1, min(T - 2, y + rng.choice((-1, 0, 1))))
                continue
            g[y][x] = 0
            g[(y + 1) % T][x] = 4


def m_acid(g, rng):
    for (cx, cy, r) in ((5, 6, 2), (10, 10, 2), (8, 3, 1)):
        for a in range(0, 360, 12):
            x = cx + int(round(math.cos(math.radians(a)) * r))
            y = cy + int(round(math.sin(math.radians(a)) * r))
            if 0 <= x < T and 0 <= y < T:
                g[y][x] = 0
        g[cy][cx] = 4


def m_fuel(g, rng):
    """燃料：油膜三段反光 + 液面折痕 + 底部沉色。"""
    for i, (y, tone) in enumerate(((5, 0), (8, 1), (11, 3))):
        for x in range(2 + i, min(T - 2, 10 + i)):
            g[y][x] = tone
    for x in range(2, T - 2):
        y = 3 + int(round(math.sin(x * 0.8) * 0.9))
        g[y][x] = 0
        g[(y + 1) % T][x] = 4
    for x in range(2, T - 2):
        g[T - 2][x] = 4


def m_hazard(g):
    for y in range(T):
        for x in range(T):
            if ((x + y) // 3) % 2 == 0:
                g[y][x] = 4


def m_explosive(g):
    m_hazard(g)


def m_fuse(g, rng):
    for y in range(T):
        for x in range(T):
            if ((x + y) // 2) % 2 == 0:
                g[y][x] = 0
            else:
                g[y][x] = 4


def m_leaf(g, rng):
    for y in range(3, T - 2):
        g[y][T // 2] = 3 if y % 3 else 4        # 中脉
    for y in (5, 8, 11):                        # 侧脉
        for k in range(3):
            if T // 2 - 1 - k >= 0:
                g[y + (k % 2)][T // 2 - 1 - k] = 4
            g[y + (k % 2)][T // 2 + 1 + k] = 4
    for x in range(3, T - 3):
        g[2][x] = 0


def m_glass_border(g):
    for x in range(T):
        g[0][x] = 0
        g[T - 1][x] = 1
    for y in range(T):
        g[0 if False else y][0] = 0
        g[y][T - 1] = 1


def m_crystal(g):
    for i in range(3):
        for x in range(4 + i, 12 - i):
            g[4 + i][x] = 0 if i == 0 else 1
            g[11 - i][x] = 4


def m_glow(g):
    for y in range(6, 10):
        for x in range(6, 10):
            g[y][x] = 0
    for (x, y) in ((5, 7), (10, 7), (7, 5), (7, 10)):
        g[y][x] = 1


def m_foil(g):
    for i in range(2, T - 2, 4):
        for x in range(2, T - 2):
            g[(i + x) % T][x] = 1 if (x + i) % 3 else 3


def m_snow(g, rng):
    for (cx, cy) in ((5, 5), (10, 9), (8, 12)):
        for d in (-1, 0, 1):
            g[cy][cx + d] = 0
        g[cy - 1][cx] = 0
        g[cy + 1][cx] = 0


def m_ash(g, rng):
    specks(g, 10, 3, rng)
    specks(g, 6, 1, rng)


MOTIFS = {
    "grass_side": m_grass_side, "grass_top": m_grass_top,
    "log_side": m_log_side, "log_top": m_log_top,
    "plank": m_plank, "brick": m_brick, "cobble": m_cobble,
    "water": m_water, "lava": m_lava, "acid": m_acid, "fuel": m_fuel,
    "hazard": m_hazard, "explosive": m_explosive, "fuse": m_fuse,
    "leaf": m_leaf, "glass_border": m_glass_border, "crystal": m_crystal,
    "glow": m_glow, "foil": m_foil, "snow": m_snow, "ash": m_ash,
}

# ── 方块表（和 blocks.json 一一对应）────────────────────────────────────
# (name, cn, tags, palette, alpha, marks{top/side/bottom})


def B(name, cn, tags="", pal=STONE, alpha=255, marks=None, sfx=None):
    return dict(name=name, cn=cn, tags=[t for t in tags.split(",") if t],
                pal=pal, alpha=alpha, sfx=sfx,
                marks=marks or {"top": "plain", "side": "plain", "bottom": "plain"})


BLOCKS = [
    B("stone", "石", "", STONE, marks={"side": "plain"}),
    B("stone_smooth", "磨光石", "", STONE),
    B("cobble", "圆石", "", STONE, marks={"side": "cobble", "top": "cobble"}),
    B("slate", "板岩", "", [(110, 112, 122), (98, 100, 112), (86, 88, 102), (74, 76, 90), (62, 64, 78)], sfx="stone"),
    B("marble", "大理石", "", [(216, 214, 210), (200, 198, 194), (184, 182, 178), (168, 166, 164), (152, 150, 148)], sfx="stone"),
    B("granite", "花岗岩", "", [(164, 148, 140), (148, 132, 124), (132, 116, 108), (116, 100, 92), (100, 84, 78)],
      marks={"side": "cobble", "top": "cobble"}),
    B("brick", "砖", "", [(178, 118, 100), (160, 104, 88), (142, 90, 76), (124, 76, 64), (106, 62, 52)],
      marks={"side": "brick", "top": "brick"}),
    B("tile", "瓷砖", "", [(172, 178, 180), (156, 162, 166), (140, 146, 152), (124, 130, 138), (108, 114, 124)],
      marks={"side": "brick", "top": "brick"}),
    B("soil", "土", "", DIRT),
    B("grass", "草", "", DIRT, marks={"top": "grass_top", "side": "grass_side"}),
    B("clay", "黏土", "", [(186, 148, 122), (170, 134, 108), (154, 120, 96), (138, 106, 84), (122, 92, 72)], sfx="gravel"),
    B("mud", "泥", "", [(118, 96, 74), (104, 84, 64), (90, 72, 56), (76, 60, 48), (62, 48, 40)], sfx="gravel"),
    B("gravel", "砾石", "", GRAVEL),
    B("ash", "灰", "", [(122, 120, 118), (110, 108, 106), (98, 96, 96), (86, 84, 86), (74, 72, 76)],
      marks={"side": "ash", "top": "ash"}),
    B("sand", "沙", "gravity", SAND),
    B("dust", "粉尘", "gravity", [(216, 210, 198), (202, 196, 184), (188, 182, 170), (174, 168, 158), (160, 154, 146)], sfx="sand"),
    B("salt", "盐", "gravity", SNOW),
    B("snow", "雪", "", SNOW, marks={"side": "snow", "top": "snow"}),
    B("water", "水", "liquid", WATER, 208, marks={"side": "water", "top": "water", "bottom": "water"}),
    B("lava", "岩浆", "liquid,melt:slate", LAVA, 246, marks={"side": "lava", "top": "lava", "bottom": "lava"}),
    B("acid", "酸", "liquid", ACID, 218, marks={"side": "acid", "top": "acid", "bottom": "acid"}),
    B("fuel", "燃料", "flammable,liquid", FUEL, 238, marks={"side": "fuel", "top": "fuel", "bottom": "fuel"}),
    B("glow", "辉光液", "glow", [(252, 236, 176), (246, 224, 152), (238, 208, 128), (226, 188, 104), (212, 168, 84)],
      240, marks={"side": "glow", "top": "glow", "bottom": "glow"}),
    B("paper_kraft", "纸张", "flammable", PAPER),
    B("cardboard", "板纸", "flammable", [(206, 178, 136), (192, 164, 122), (178, 150, 110), (162, 134, 96), (146, 118, 84)], sfx="wood"),
    B("corrugated", "瓦楞纸", "flammable", [(190, 164, 126), (176, 150, 114), (162, 136, 102), (148, 122, 90), (134, 108, 78)],
      marks={"side": "log_side"}),
    B("plank", "木板", "flammable", LOG, marks={"side": "plank", "top": "plank", "bottom": "plank"}),
    B("log", "原木", "flammable", LOG, marks={"side": "log_side", "top": "log_top", "bottom": "log_top"}),
    B("leaf", "叶", "flammable", LEAF, 240, marks={"side": "leaf", "top": "leaf"}),
    B("hazard", "警示板", "", [(238, 190, 96), (222, 174, 84), (204, 156, 72), (186, 138, 62), (168, 120, 52)],
      marks={"side": "hazard", "top": "hazard", "bottom": "hazard"}),
    B("explosive", "炸药", "explosive", [(208, 68, 58), (190, 58, 50), (172, 50, 44), (152, 42, 38), (132, 34, 32)],
      marks={"side": "explosive", "top": "explosive", "bottom": "explosive"}),
    B("fuse", "引线", "fuse", [(196, 164, 122), (178, 146, 106), (158, 128, 92), (138, 110, 78), (118, 92, 64)],
      marks={"side": "fuse", "top": "fuse", "bottom": "fuse"}),
    B("lamp", "灯", "glow", [(248, 240, 200), (240, 230, 186), (230, 218, 170), (218, 204, 154), (204, 188, 138)],
      marks={"side": "glow", "top": "glow", "bottom": "glow"}),
    B("foil", "铝箔", "", METAL, marks={"side": "foil", "top": "foil", "bottom": "foil"}),
    B("paper_roll", "纸卷", "flammable", PAPER, marks={"side": "log_side", "top": "plain", "bottom": "plain"}),
    B("kraft_tape", "胶带", "", [(222, 196, 150), (208, 182, 136), (194, 168, 122), (178, 152, 108), (162, 136, 94)],
      210),
    B("wax_paper", "蜡纸", "", [(236, 228, 206), (224, 216, 194), (212, 202, 182), (198, 188, 168), (184, 174, 156)],
      190),
    B("glass", "玻璃", "", GLASSY, 150, marks={"side": "glass_border", "top": "glass_border", "bottom": "glass_border"}),
    B("frosted", "磨砂玻璃", "", GLASSY, 165),
    B("stained_red", "红彩玻", "", [(226, 148, 148), (210, 132, 132), (192, 116, 118), (172, 100, 104), (152, 84, 90)],
      150, marks={"side": "glass_border", "top": "glass_border", "bottom": "glass_border"}),
    B("stained_blue", "蓝彩玻", "", [(150, 178, 226), (136, 162, 210), (122, 146, 192), (108, 130, 174), (94, 114, 156)],
      150, marks={"side": "glass_border", "top": "glass_border", "bottom": "glass_border"}),
    B("crystal", "水晶", "glow", [(160, 220, 218), (144, 202, 202), (128, 182, 186), (112, 162, 168), (96, 142, 150)],
      220, marks={"side": "crystal", "top": "crystal", "bottom": "crystal"}),
    B("bedrock", "基岩", "", BEDROCK, marks={"side": "ash", "top": "ash", "bottom": "ash"}),
    B("obsidian", "黑曜石", "", OBSIDIAN, marks={"side": "ash", "top": "ash", "bottom": "ash"}),
    B("ice", "冰", "", ICE, 190, marks={"side": "ice", "top": "ice", "bottom": "ice"}),
    B("structure_block", "结构方块", "", STRUCT,
      marks={"side": "structure", "top": "structure", "bottom": "structure"}),
]
for dye, hue in DYE_HUES.items():
    cn = {"red": "红", "orange": "橙", "yellow": "黄", "green": "绿", "teal": "青",
          "blue": "蓝", "purple": "紫", "pink": "粉", "brown": "棕",
          "gray": "灰", "black": "黑", "white": "白"}[dye[4:]]
    BLOCKS.append(B(dye, cn + "纸", "flammable", dye_palette(hue)))


# 音效组：没在 B() 里单独指定的走这张表（组名必须出现在 BlockSfxData.Groups 里）
SFX = {
    "stone": "stone", "stone_smooth": "stone", "cobble": "stone", "slate": "stone",
    "marble": "stone", "granite": "stone", "brick": "stone", "tile": "stone",
    "soil": "gravel", "grass": "grass", "clay": "gravel", "mud": "gravel",
    "gravel": "gravel", "ash": "gravel", "sand": "sand", "dust": "sand", "salt": "snow",
    "snow": "snow", "water": "glass", "lava": "stone", "acid": "glass",
    "fuel": "wood", "glow": "glass", "paper_kraft": "wood", "cardboard": "wood",
    "corrugated": "wood", "plank": "wood", "log": "wood", "leaf": "grass",
    "hazard": "stone", "explosive": "grass", "fuse": "wood", "lamp": "glass",
    "foil": "metal", "paper_roll": "wood", "kraft_tape": "wood", "wax_paper": "wood",
    "glass": "glass", "frosted": "glass", "stained_red": "glass",
    "stained_blue": "glass", "crystal": "crystal",
    "bedrock": "stone", "structure_block": "stone", "obsidian": "stone", "ice": "glass",
}

# ── 组装 ────────────────────────────────────────────────────────────────


def make_face(spec, which, salt):
    pal = spec["pal"]
    alpha = spec["alpha"]
    rng = random.Random(salt)
    g = [[0] * T for _ in range(T)]
    mark = spec["marks"].get(which, "plain")

    if mark == "cobble":
        m_cobble(g, rng)
    else:
        grain_field(g, rng, pal, which)
        fn = MOTIFS.get(mark)
        if fn:
            fn(g, rng) if mark in ("grass_side", "grass_top", "log_top",
                                   "m_lava", "lava", "acid", "fuel", "ash",
                                   "leaf", "fuse") and False else None
            # 带参数的图案单独处理
        # 统一调用：需要 rng 的传 rng
        fn2 = MOTIFS.get(mark)
        if fn2:
            import inspect
            try:
                needs_rng = "rng" in inspect.signature(fn2).parameters
            except Exception:
                needs_rng = False
            if needs_rng:
                fn2(g, rng)
            else:
                fn2(g)
        edges(g, which)

    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = px(img)
    for y in range(T):
        for x in range(T):
            r, gg, b = pal[g[y][x]]
            p[x, y] = (r, gg, b, alpha)
    return img


def build():
    cell = T + PAD * 2
    total = len(BLOCKS) * 3
    rows = (total + COLS - 1) // COLS
    atlas = Image.new("RGBA", (COLS * cell, rows * cell), (0, 0, 0, 0))
    tiles = {}
    idx = 0
    for bi, spec in enumerate(BLOCKS):
        faces = []
        for f, which in enumerate(("top", "side", "bottom")):
            im = make_face(spec, which, 900 + bi * 10 + f)
            atlas.paste(im, ((idx % COLS) * cell + PAD, (idx // COLS) * cell + PAD))
            faces.append(im)
            idx += 1
        tiles[spec["name"]] = faces
    return atlas, tiles, idx


def preview(tiles, scale=5):
    gap = 8
    cols = 9
    names = [b["name"] for b in BLOCKS]
    rows = (len(names) + cols - 1) // cols
    fw = T * scale
    w = gap + cols * (fw + gap)
    h = gap + rows * (fw + gap)
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    for i, name in enumerate(names):
        side = tiles[name][1]
        big = side.resize((fw, fw), Image.NEAREST)
        x = gap + (i % cols) * (fw + gap)
        y = gap + (i // cols) * (fw + gap)
        sheet.paste(big, (x, y), big)
    return sheet


def wall(tiles, layout):
    DX, DY = 5, 5
    cols = len(layout[0])
    w = cols * T + DX + 12
    h = len(layout) * (T + DY) + 12
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    for row in range(len(layout) - 1, -1, -1):
        for col in range(cols):
            name = layout[row][col]
            faces = tiles.get(name) or tiles["stone"]
            top, side, bottom = faces
            x = 6 + col * T
            y = 6 + row * (T + DY)
            t2 = top.resize((T, DY), Image.LANCZOS)
            s2 = side.resize((DX, T + DY), Image.LANCZOS)
            sheet.paste(t2, (x, y), t2)
            sheet.paste(s2, (x + T, y + DY), s2)
            sheet.paste(side, (x, y + DY), side)
    return sheet


def emit_csharp(out_dir, faces_by_block):
    """从图集像素反推透明/镂空标记，并生成 C# 数据文件 BlockCatalogData.cs。

    面序号顺序（和 BlockCatalog.BuildBlocks 的约定一致）：
        0=Up(顶) 1=Down(底) 2=North 3=South 4=East 5=West（四面墙共用侧面）
    """
    cols, rows = COLS, (len(BLOCKS) * 3 + COLS - 1) // COLS
    cell = T + PAD * 2
    lines = []
    lines.append("// 由 tools/build_substrata_atlas.py 自动生成，不要手改。")
    lines.append("namespace MinecraftTetris")
    lines.append("{")
    lines.append("    public static class BlockCatalogData")
    lines.append("    {")
    lines.append("        public const int AtlasColumns = %d;" % cols)
    lines.append("        public const int AtlasRows = %d;" % rows)
    lines.append("        public const int TilePadding = %d;" % PAD)
    lines.append("        public const int TilePixels = %d;" % T)
    lines.append("        public const int CellPixels = %d;" % (T + PAD * 2))
    lines.append("")
    lines.append("        public static readonly string[] Names =")
    lines.append("        {")
    for b in BLOCKS:
        lines.append('            "%s",' % b["name"])
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly string[] NamesZh =")
    lines.append("        {")
    for b in BLOCKS:
        lines.append('            "%s",' % b["cn"])
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly string[] Tags =")
    lines.append("        {")
    for b in BLOCKS:
        lines.append('            "%s",' % ",".join(b["tags"]))
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly string[] SfxGroups =")
    lines.append("        {")
    for b in BLOCKS:
        sf = b.get("sfx") or SFX.get(b["name"], "stone")
        lines.append('            "%s",' % sf)
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly int[] FaceTiles =")
    lines.append("        {")
    face_idx = 0
    for b in BLOCKS:
        idxs = []
        for f in range(3):                     # top / side / bottom
            i = face_idx + f
            idxs.append(i)
        face_idx += 3
        top, bottom, side = idxs[0], idxs[2], idxs[1]
        lines.append("            %d, %d, %d, %d, %d, %d,   // %s"
                     % (top, bottom, side, side, side, side, b["name"]))
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly byte[] TransparentFlags =")
    lines.append("        {")
    trans = []
    cut = []
    for b in BLOCKS:
        imgs = faces_by_block[b["name"]]
        is_t = 0
        is_c = 0
        for im in imgs:
            alphas = [p[3] for p in im.getdata()]
            if min(alphas) < 200:
                is_t = 1
            if min(alphas) < 120 and max(alphas) > 200:
                is_c = 1
        trans.append(is_t)
        cut.append(is_c)
    lines.append("            " + ", ".join(str(v) for v in trans) + ",")
    lines.append("        };")
    lines.append("")
    lines.append("        public static readonly byte[] CutoutFlags =")
    lines.append("        {")
    lines.append("            " + ", ".join(str(v) for v in cut) + ",")
    lines.append("        };")
    lines.append("    }")
    lines.append("}")

    path = os.path.join(root, "Assets", "Scripts", "Core", "BlockCatalogData.cs")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("C# 数据已生成:", path)


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "Assets", "Art", "AtlasSubstrata")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(os.path.join(root, "Logs"), exist_ok=True)

    atlas, tiles, total = build()
    atlas.save(os.path.join(out_dir, "atlas.png"))
    emit_csharp(out_dir, tiles)

    table = []
    for i, b in enumerate(BLOCKS):
        table.append({"id": i + 1, "name": b["name"], "cn": b["cn"], "tags": b["tags"]})
    with open(os.path.join(out_dir, "blocks.json"), "w", encoding="utf-8") as f:
        json.dump(table, f, ensure_ascii=False, indent=2)
    with open(os.path.join(out_dir, "blocks.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(b["name"] for b in BLOCKS) + "\n")

    preview(tiles).save(os.path.join(root, "Logs", "substrata_preview.png"))

    WALL = [
        ["grass", "grass", "grass", "grass", "grass", "grass", "grass", "grass", "grass"],
        ["soil", "soil", "soil", "clay", "mud", "soil", "soil", "soil", "soil"],
        ["stone", "stone", "cobble", "stone", "slate", "marble", "granite", "stone", "tile"],
        ["gravel", "gravel", "ash", "sand", "sand", "dust", "salt", "snow", "gravel"],
        ["plank", "plank", "log", "cardboard", "corrugated", "paper_kraft", "foil", "paper_roll", "kraft_tape"],
        ["hazard", "hazard", "explosive", "fuse", "lamp", "crystal", "glass", "frosted", "stained_blue"],
        ["water", "water", "fuel", "fuel", "acid", "acid", "lava", "lava", "glow"],
    ]
    wall(tiles, WALL).save(os.path.join(root, "Logs", "substrata_wall.png"))
    print("图集 %dx%d，%d 块 x 3 面 = %d 张" % (atlas.width, atlas.height, len(BLOCKS), total))
