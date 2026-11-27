#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA —— 纸艺折纸风格图集（16x16）。

这一版的规则（也是它和「写实颗粒」的根本区别）：
    1. 不用暗部噪点表现材质，改用「纸的明度 + 折痕方向」；
    2. 每种材质给一段固定明度区间（石=中灰纸、土=牛皮纸、沙=米白、液=薄纸、
       危险=暖色 + 深色折边、玻璃=半透明硫酸纸），保证相邻块一眼能分开；
    3. 每块都有一道「折痕」：上/左亮一条、下/右暗一条，像一张纸被折过。

用法：
    py -3 tools/build_paper_atlas.py
产物：
    Assets/Art/AtlasPaper/atlas.png
    Assets/Art/AtlasPaper/blocks.txt
    Logs/paper_atlas_preview.png
"""

import math
import os
from PIL import Image

TILE = 16
PAD = 1
COLS = 8


def hnoise(x, y, salt=0):
    n = (x * 374761393 + y * 668265263 + salt * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def sh(c, f, a=None):
    r = max(0, min(255, int(c[0] * f)))
    g = max(0, min(255, int(c[1] * f)))
    b = max(0, min(255, int(c[2] * f)))
    return (r, g, b, c[3] if a is None else a)


def base_paper(img, tint, fiber=0.05, vgrad=0.06):
    """铺纸：极轻的纤维噪声 + 很浅的竖向渐变（像纸被顶光照着）。"""
    p = img.load()
    for y in range(TILE):
        k = 1.0 + vgrad * (0.5 - y / (TILE - 1))
        for x in range(TILE):
            n = (hnoise(x, y, 5) - 0.5) * 2 * fiber + (hnoise(x // 3, y, 9) - 0.5) * fiber
            p[x, y] = sh(tint, k + n)


def fold(img, up=1.13, down=0.86, crease=True):
    """折痕：上/左亮，下/右暗；再往里一像素压一条浅折线，纸才有厚度。"""
    p = img.load()
    for i in range(TILE):
        p[i, 0] = sh(p[i, 0], up)
        p[0, i] = sh(p[0, i], up * 0.98)
        p[i, TILE - 1] = sh(p[i, TILE - 1], down)
        p[TILE - 1, i] = sh(p[TILE - 1, i], down * 0.97)
    if crease:
        for i in range(1, TILE - 1):
            p[i, 1] = sh(p[i, 1], 1.06)
            p[1, i] = sh(p[1, i], 1.04)
            p[i, TILE - 2] = sh(p[i, TILE - 2], 0.94)
            p[TILE - 2, i] = sh(p[TILE - 2, i], 0.95)


def pen(p, x, y, c, f=1.0, a=None):
    if 0 <= x < TILE and 0 <= y < TILE:
        p[x, y] = sh(c, f, a)


def motif_stone(img, c):
    p = img.load()
    for i in range(3, TILE - 3):                     # 一道对角折
        pen(p, i, i, c, 0.86)
        pen(p, i + 1, i, c, 1.10)
    for x in range(3, 9):                            # 一条横折
        pen(p, x, 11, c, 0.88)


def motif_soil(img, c):
    p = img.load()
    for (x, y) in ((4, 4), (9, 6), (6, 10), (11, 11), (3, 8)):
        pen(p, x, y, c, 0.84)                        # 土块阴影
        pen(p, x + 1, y - 1, c, 1.12)                # 受光面


def motif_sand(img, c):
    p = img.load()
    for i in range(4):
        for x in range(2 + i * 3, min(TILE - 2, 3 + i * 3)):
            pen(p, x, 5 + i * 2, c, 0.92)
            pen(p, x, 6 + i * 2, c, 1.08)


def motif_liquid(img, c, top=4):
    p = img.load()
    for y in range(top, TILE - 1):                   # 液面以下压暗一档，像有厚度
        for x in range(1, TILE - 1):
            if y > top + 1:
                p[x, y] = sh(p[x, y], 0.94)
    for x in range(2, TILE - 2):                     # 起伏的纸边
        y = top + int(round(math.sin(x * 0.8) * 1.2))
        pen(p, x, y, c, 1.22)
        pen(p, x, y + 1, c, 0.82)
    for x in range(3, TILE - 3, 3):                  # 反光短划
        pen(p, x, top + 5, c, 1.26)
    for x in range(2, TILE - 2):                     # 底部沉色，别是一片平
        pen(p, x, TILE - 2, c, 0.86)


def motif_hazard(img, c):
    p = img.load()
    for y in range(TILE):                            # 折纸锯齿
        for x in range(TILE):
            if ((x + y) // 3) % 2 == 0:
                p[x, y] = sh(c, 0.32)


def motif_glass(img, c):
    p = img.load()
    for i in range(5):                               # 一道亮折
        pen(p, 3 + i, 12 - i, (255, 255, 255), 1.0, 200)


def motif_fiber(img, c):
    p = img.load()
    for y in range(2, TILE - 2, 3):                  # 瓦楞纸的横楞
        for x in range(2, TILE - 2):
            pen(p, x, y, c, 0.90)
            pen(p, x, y + 1, c, 1.08)


def motif_brick(img, c):
    p = img.load()
    for row, y in enumerate((4, 9, 13)):
        for x in range(1, TILE - 1):
            pen(p, x, y, c, 0.84)
        off = 0 if row % 2 == 0 else 3
        for x in range(1 + off, TILE - 1, 6):
            for yy in range(y - 4 if row else 1, y):
                pen(p, x, yy, c, 0.86)


def motif_crystal(img, c):
    p = img.load()
    for i in range(4):                               # 三个切面，靠明暗分组
        for x in range(5 + i, 11 - i):
            pen(p, x, 4 + i, c, 1.15)
            pen(p, x, 11 - i, c, 0.86)


def motif_gear(img, c):
    p = img.load()
    for i in range(4, 12):                           # 外圈
        pen(p, i, 4, c, 0.84); pen(p, i, 11, c, 0.84)
        pen(p, 4, i, c, 0.84); pen(p, 11, i, c, 0.84)
    for (x, y) in ((7, 7), (8, 7), (7, 8), (8, 8)):  # 轴心
        pen(p, x, y, c, 1.18)
    for (x, y) in ((7, 3), (7, 12), (3, 7), (12, 7)):  # 四个齿
        pen(p, x, y, c, 1.10)


def motif_switch(img, c):
    p = img.load()
    for i in range(4, 12):                           # 底板
        pen(p, i, 8, c, 0.86)
    for x in range(5, 8):                            # 拨杆
        for y in range(5, 9):
            pen(p, x, y, c, 1.16 if x < 7 else 1.02)
    pen(p, 10, 8, c, 1.22)                           # 指示灯


def motif_lamp(img, c):
    p = img.load()
    for (x, y) in ((7, 6), (8, 6), (7, 7), (8, 7), (7, 8), (8, 8), (6, 7), (9, 7)):
        pen(p, x, y, (255, 255, 255), 1.0, 235)
    for (x, y) in ((5, 5), (10, 5), (5, 10), (10, 10)):   # 光晕
        pen(p, x, y, c, 1.22)


def motif_flake(img, c):
    p = img.load()
    for (cx, cy) in ((5, 5), (10, 9), (8, 12)):
        for d in range(-1, 2):                       # 三横一竖，极简雪花
            pen(p, cx + d, cy, c, 1.12)
        pen(p, cx, cy - 1, c, 1.12)
        pen(p, cx, cy + 1, c, 1.12)


def motif_spark(img, c):
    p = img.load()
    for (x, y) in ((6, 6), (7, 7), (6, 7), (7, 6)):
        pen(p, x, y, c, 1.35)
    for (x, y) in ((4, 4), (9, 9), (4, 9), (9, 4)):
        pen(p, x, y, c, 1.15)


# 材质表：名字 -> (主色, 明度系数, 图案, 不透明度)
STONE = (150, 150, 156)
KINDS = [
    # 石系
    ("stone",        STONE,            1.00, motif_stone, 255),
    ("stone_smooth", (162, 162, 168),  1.04, None,        255),
    ("cobble",       (136, 138, 142),  0.95, motif_soil,  255),
    ("slate",        (104, 110, 118),  0.92, motif_stone, 255),
    ("marble",       (214, 212, 206),  1.06, motif_stone, 255),
    ("granite",      (156, 140, 132),  0.98, motif_soil,  255),
    ("brick",        (176, 118, 100),  1.00, motif_brick, 255),
    ("tile",         (168, 176, 178),  1.02, motif_brick, 255),
    # 土系
    ("soil",         (150, 116, 84),   1.00, motif_soil,  255),
    ("grass",        (126, 164, 104),  1.00, motif_fiber, 255),
    ("clay",         (176, 138, 112),  0.98, motif_soil,  255),
    ("mud",          (118, 96, 74),    0.92, motif_soil,  255),
    ("gravel",       (146, 140, 130),  0.96, motif_soil,  255),
    ("ash",          (120, 118, 116),  0.94, None,        255),
    # 沙 / 粉
    ("sand",         (232, 214, 172),  1.02, motif_sand,  255),
    ("dust",         (222, 214, 200),  1.00, motif_sand,  255),
    ("salt",         (244, 242, 236),  1.04, motif_sand,  255),
    ("snow",         (246, 248, 250),  1.05, motif_flake, 255),
    # 液体
    ("water",        (150, 194, 224),  1.00, motif_liquid, 190),
    ("lava",         (238, 132, 72),   1.00, motif_liquid, 235),
    ("acid",         (170, 214, 118),  1.00, motif_liquid, 210),
    ("oil",          (110, 102, 120),  0.98, motif_liquid, 235),
    ("glow",         (250, 224, 150),  1.04, motif_spark,  240),
    # 纸 / 木
    ("paper_kraft",  (196, 172, 134),  1.00, motif_fiber, 255),
    ("cardboard",    (204, 176, 132),  1.00, motif_fiber, 255),
    ("corrugated",   (188, 164, 126),  0.98, motif_fiber, 255),
    ("plank",        (198, 164, 118),  1.00, motif_brick, 255),
    ("log",          (168, 134, 96),   0.98, motif_fiber, 255),
    ("leaf",         (128, 168, 108),  1.00, motif_soil,  255),
    # 功能 / 危险
    ("hazard",       (236, 186, 96),   1.00, motif_hazard, 255),
    ("explosive",    (224, 118, 96),   1.00, motif_hazard, 255),
    ("fuse",         (196, 120, 96),   1.00, motif_fiber,  255),
    ("switch",       (188, 192, 196),  1.00, motif_switch, 255),
    ("lamp",         (246, 236, 186),  1.04, motif_lamp,   255),
    ("gear",         (170, 168, 160),  1.00, motif_gear,   255),
    ("magnet",       (198, 128, 130),  1.00, motif_brick,  255),
    ("cannon",       (128, 130, 136),  0.96, motif_brick,  255),
    # 透明 / 晶体
    ("glass",        (216, 238, 244),  1.02, motif_glass,  120),
    ("frosted",      (226, 236, 240),  1.02, None,         165),
    ("stained_red",  (226, 148, 148),  1.02, motif_glass,  150),
    ("stained_blue", (150, 178, 226),  1.02, motif_glass,  150),
    ("crystal",      (160, 220, 218),  1.02, motif_crystal, 200),
    # 染色纸（颜色变体，同一套纹理换色）
    ("dye_red",      (214, 122, 118),  1.00, None, 255),
    ("dye_orange",   (224, 160, 96),   1.00, None, 255),
    ("dye_yellow",   (226, 204, 112),  1.00, None, 255),
    ("dye_green",    (142, 182, 118),  1.00, None, 255),
    ("dye_teal",     (118, 186, 182),  1.00, None, 255),
    ("dye_blue",     (124, 152, 206),  1.00, None, 255),
    ("dye_purple",   (158, 136, 190),  1.00, None, 255),
    ("dye_pink",     (216, 152, 176),  1.00, None, 255),
    ("dye_brown",    (166, 132, 106),  1.00, None, 255),
    ("dye_gray",     (158, 158, 162),  1.00, None, 255),
    ("dye_black",    (98, 98, 104),    1.00, None, 255),
    ("dye_white",    (234, 232, 228),  1.04, None, 255),
]


# 预览工具（tools/preview_wall.py）按 (名字, 任意) 的格式取块
BLOCKS = [(k[0], None) for k in KINDS]

# 一面墙的配方：上冷下暖、右半边放功能块和液体，看整体气氛
WALL_LAYOUT = [
    ["snow", "frosted", "glass", "glass", "crystal", "stone_smooth", "lamp", "lamp", "crystal"],
    ["marble", "tile", "glass", "glass", "dye_blue", "stone_smooth", "switch", "gear", "lamp"],
    ["stone", "stone", "brick", "brick", "dye_blue", "plank", "paper_kraft", "glass", "glass"],
    ["slate", "stone", "hazard", "hazard", "plank", "glow", "paper_kraft", "dye_white", "glass"],
    ["soil", "sand", "slate", "lava", "lava", "plank", "cardboard", "dye_white", "frosted"],
    ["soil", "grass", "sand", "gravel", "water", "water", "cardboard", "leaf", "water"],
]


def make(kind, tile=TILE):
    name, tint, bright, motif, alpha = kind
    img = Image.new("RGBA", (tile, tile), (0, 0, 0, 0))
    c = (tint[0], tint[1], tint[2], alpha)
    base_paper(img, c)
    p = img.load()
    for y in range(tile):
        for x in range(tile):
            p[x, y] = sh(p[x, y], bright)
    if motif:
        motif(img, c)
    fold(img)
    return img


def build(tile=TILE):
    rows = (len(KINDS) + COLS - 1) // COLS
    cell = tile + PAD * 2
    atlas = Image.new("RGBA", (COLS * cell, rows * cell), (0, 0, 0, 0))
    tiles = []
    for i, k in enumerate(KINDS):
        im = make(k, tile)
        atlas.paste(im, ((i % COLS) * cell + PAD, (i // COLS) * cell + PAD))
        tiles.append(im)
    return atlas, tiles, rows


def preview(tiles, scale=8, cols=8):
    gap = 6
    rows = (len(tiles) + cols - 1) // cols
    w = cols * (TILE * scale + gap) + gap
    h = rows * (TILE * scale + gap) + gap
    sheet = Image.new("RGB", (w, h), (24, 22, 26))
    for i, t in enumerate(tiles):
        big = t.resize((TILE * scale, TILE * scale), Image.NEAREST)
        x = gap + (i % cols) * (TILE * scale + gap)
        y = gap + (i // cols) * (TILE * scale + gap)
        sheet.paste(big, (x, y), big)
    return sheet


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "Assets", "Art", "AtlasPaper")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(os.path.join(root, "Logs"), exist_ok=True)

    atlas, tiles, rows = build()
    atlas.save(os.path.join(out_dir, "atlas.png"))
    with open(os.path.join(out_dir, "blocks.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(k[0] for k in KINDS) + "\n")
    preview(tiles).save(os.path.join(root, "Logs", "paper_atlas_preview.png"))
    print("图集 %dx%d，%d 块，%d 列" % (atlas.width, atlas.height, len(KINDS), COLS))
