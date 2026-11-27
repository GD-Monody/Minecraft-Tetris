#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA 方块图集生成器（32x32，全部程序化绘制，不引用任何第三方贴图）。

为什么不直接拿图改：
第三方材质包就算写着「免费」，许可一般也只允许在某个游戏里用，禁止提取和再分发；
真正干净的做法是从零画出像素。这里每一块都是「配色梯度 + 哈希噪声 + 该材质的特征笔触」，
可复现（固定种子），也就是从代码就能证明它是自己画的。

用法：
    py -3 tools/build_original_atlas.py            # 出图集 + 预览
产物：
    Assets/Art/AtlasOriginal/atlas.png             32x32 一块，1px padding
    Assets/Art/AtlasOriginal/blocks.txt            方块名顺序（= 图集内 id 顺序）
    Logs/original_atlas_preview.png                放大预览，肉眼看效果
"""

import os
import random
from PIL import Image

TILE = 32          # 每块像素
PAD = 1            # 块与块之间的透明边，防止采样串色（和现有图集一致）
COLS = 6

# ── 工具 ────────────────────────────────────────────────────────────────


def hnoise(x, y, salt=0):
    """确定性哈希噪声：同样的坐标永远得到同样的值，构建可复现。"""
    n = (x * 374761393 + y * 668265263 + salt * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def shade(c, f):
    """明暗：f>1 提亮，f<1 压暗，夹在 0..255。"""
    return tuple(max(0, min(255, int(v * f))) for v in c[:3]) + ((c[3] if len(c) > 3 else 255),)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (a[3] if len(a) > 3 else 255,)


def ramp(colors, t):
    """在若干颜色之间取插值，t 在 0..1。"""
    t = max(0.0, min(0.999, t))
    i = int(t * (len(colors) - 1))
    f = t * (len(colors) - 1) - i
    return mix(colors[i], colors[i + 1], f)


def grain(img, colors, salt, strength=1.0, blocky=1):
    """铺底色 + 细颗粒。blocky>1 时颗粒成块（石头味），=1 是细沙味。"""
    px = img.load()
    rnd = random.Random(salt)
    for y in range(TILE):
        for x in range(TILE):
            n = hnoise(x // blocky, y // blocky, salt)
            n = n * 0.65 + hnoise(x, y, salt + 7) * 0.35 * strength
            px[x, y] = ramp(colors, n)


# ── 各种材质 ────────────────────────────────────────────────────────────


def t_soil(img):
    grain(img, [(58, 42, 32), (92, 68, 48), (120, 92, 64)], 11, blocky=2)
    px = img.load()
    rnd = random.Random(3)
    for _ in range(26):                      # 小石子
        x, y = rnd.randrange(TILE), rnd.randrange(TILE)
        px[x, y] = (150, 132, 110)
        if rnd.random() < 0.5:
            px[min(TILE - 1, x + 1), y] = (132, 114, 94)


def t_grass(img):
    grain(img, [(52, 92, 40), (86, 138, 56), (120, 172, 74)], 21, blocky=2)
    px = img.load()
    rnd = random.Random(9)
    for _ in range(40):                      # 草叶：一竖两像素
        x, y = rnd.randrange(TILE), rnd.randrange(TILE - 3)
        c = (146, 196, 96) if rnd.random() < 0.5 else (74, 122, 52)
        px[x, y] = c
        px[x, y + 1] = c


def t_stone(img):
    grain(img, [(96, 96, 102), (128, 128, 134), (156, 156, 162)], 31, blocky=3)
    px = img.load()
    for y in range(1, TILE - 1):             # 裂缝
        for x in range(1, TILE - 1):
            if hnoise(x // 4, y // 4, 44) > 0.86 and abs(x - y) < 6:
                px[x, y] = (86, 86, 92)


def t_cobble(img):
    """圆石：先撒一圈抖动的种子点，每像素归属最近的那颗，
    到最近与次近种子距离接近的地方画暗缝 —— 出来是一颗颗圆石头，不是马赛克。"""
    import math
    seeds = []
    rnd = random.Random(41)
    for gy in range(4):
        for gx in range(4):
            seeds.append((gx * 8 + 4 + rnd.randint(-2, 2), gy * 8 + 4 + rnd.randint(-2, 2),
                          rnd.random()))
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            ds = sorted(((x - sx) ** 2 + (y - sy) ** 2, s3) for sx, sy, s3 in seeds)
            d1, tone = ds[0]
            d2 = ds[1][0]
            if math.sqrt(d2) - math.sqrt(d1) < 1.15:          # 石缝
                px[x, y] = (66, 66, 72)
            else:
                t = 0.35 + tone * 0.45 - min(1.0, math.sqrt(d1) / 9.0) * 0.22
                px[x, y] = ramp([(104, 104, 110), (146, 146, 152), (176, 176, 182)], t)


def t_sand(img):
    import math
    grain(img, [(196, 176, 122), (222, 204, 148), (240, 226, 176)], 51)
    px = img.load()
    for y in range(TILE):                       # 轻微起伏，不然是一块死平
        k = 1.0 + 0.05 * math.sin(y * 0.55 + 1.2)
        for x in range(TILE):
            px[x, y] = shade(px[x, y], k)
    px = img.load()
    rnd = random.Random(5)
    for _ in range(30):
        x, y = rnd.randrange(TILE), rnd.randrange(TILE)
        px[x, y] = (206, 186, 132)


def t_gravel(img):
    grain(img, [(112, 104, 96), (140, 132, 122), (166, 158, 148)], 61, blocky=2)
    px = img.load()
    rnd = random.Random(7)
    for _ in range(34):                      # 卵石
        x, y = rnd.randrange(TILE - 1), rnd.randrange(TILE - 1)
        c = rnd.choice([(180, 172, 162), (96, 90, 84), (150, 142, 132)])
        px[x, y] = c
        px[x + 1, y] = c
        px[x, y + 1] = shade(c, 0.86)


def t_water(img):
    """半透明水：横向波纹。alpha 让下层透出来。"""
    import math
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            base = hnoise(x // 4, y // 4, 71)
            phase = y * 0.85 + math.sin(x * 0.28) * 1.6     # 横向、轻微起伏
            w = math.sin(phase) * 0.5 + 0.5
            c = ramp([(34, 78, 160), (54, 112, 200), (92, 158, 228)], base * 0.5 + w * 0.5)
            px[x, y] = c[:3] + (198,)


def t_lava(img):
    """岩浆：暗色硬壳打底，亮色熔岩只在「裂缝」里走。全屏噪点看不出是岩浆。"""
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            crust = hnoise(x // 3, y // 3, 81)
            vein = hnoise(x // 2, y // 2, 83)
            if crust > 0.52:                                  # 冷却的壳
                c = ramp([(96, 34, 22), (132, 52, 28)], crust)
            else:
                c = ramp([(226, 88, 20), (250, 150, 40), (255, 214, 120)], vein)
            px[x, y] = c if len(c) == 4 else c[:3] + (255,)


def t_cooled(img):
    """岩浆遇水凝固的产物（原版叫黑曜石，这里换个说法：凝固岩）。"""
    grain(img, [(44, 38, 52), (64, 56, 74), (88, 78, 100)], 91, blocky=3)
    px = img.load()
    for _ in range(14):                      # 玻璃质反光
        x, y = random.Random(_ + 300).randrange(TILE), random.Random(_ + 900).randrange(TILE)
        if x < TILE - 2 and y < TILE - 2:
            px[x, y] = (128, 118, 148)
            px[x + 1, y + 1] = (104, 96, 122)


def t_log(img):
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            t = hnoise(x // 3, y, 101) * 0.6 + (0.3 if (x % 7) < 2 else 0.0)
            px[x, y] = ramp([(74, 52, 30), (108, 78, 44), (140, 104, 60)], t)


def t_planks(img):
    grain(img, [(150, 110, 64), (178, 136, 82), (200, 160, 102)], 111, blocky=2)
    px = img.load()
    for y in range(TILE):
        if y % 8 == 0:                       # 板缝
            for x in range(TILE):
                px[x, y] = (112, 80, 44)


def t_leaves(img):
    grain(img, [(36, 74, 32), (58, 106, 44), (84, 134, 58)], 121)
    px = img.load()
    rnd = random.Random(13)
    for _ in range(70):                      # 叶隙
        x, y = rnd.randrange(TILE), rnd.randrange(TILE)
        if rnd.random() < 0.45:
            px[x, y] = (28, 52, 26)


def t_glass(img):
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            px[x, y] = (214, 238, 252, 74)   # 大面积近乎透明（提亮一点，深色背景上才看得出是玻璃）
    for x in range(TILE):                    # 边框
        for y in (0, TILE - 1):
            px[x, y] = (214, 238, 252, 225)
        for y in range(TILE):
            for x2 in (0, TILE - 1):
                px[x2, y] = (214, 238, 252, 225)
    for i in range(9):                       # 斜向反光
        x, y = 5 + i, 22 - i
        if 0 <= x < TILE and 0 <= y < TILE:
            px[x, y] = (255, 255, 255, 150)


def t_explosive(img):
    """炸药：红底 + 深色缠带 + 引线。不叫 TNT，也不画原版那种字母。"""
    grain(img, [(150, 44, 36), (188, 62, 48), (214, 88, 66)], 131, blocky=2)
    px = img.load()
    for y in range(12, 20):                  # 缠带
        for x in range(TILE):
            px[x, y] = (60, 52, 48) if (y < 14 or y > 17) else (86, 74, 66)
    for x in range(14, 19):                  # 引信
        for y in range(0, 12):
            px[x, y] = (92, 82, 70)
    px[16, 0] = (255, 196, 90)


def t_metal(img):
    grain(img, [(152, 152, 160), (186, 186, 194), (214, 214, 220)], 141, blocky=2)
    px = img.load()
    for i in range(TILE):                    # 铆钉
        for c in ((5, 5), (TILE - 6, 5), (5, TILE - 6), (TILE - 6, TILE - 6)):
            px[c[0], c[1]] = (120, 120, 128)
            px[c[0] + 1, c[1] + 1] = (232, 232, 238)


def t_gold(img):
    grain(img, [(168, 128, 40), (214, 172, 62), (244, 214, 112)], 151, blocky=2)


def t_crystal(img):
    """晶石（对应原版钻石那类，换名字换质感）。"""
    grain(img, [(40, 116, 118), (64, 168, 168), (120, 214, 210)], 161, blocky=3)
    px = img.load()
    for i in range(TILE):                    # 斜切面高光
        for k in range(3):
            x, y = (i + k * 6) % TILE, i
            px[x, y] = shade(px[x, y], 1.35)


def t_brick(img):
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            row = y // 8
            off = 0 if row % 2 == 0 else 8
            if y % 8 == 0 or (x + off) % 16 == 0:
                px[x, y] = (150, 142, 132)   # 灰浆
            else:
                t = hnoise(x // 2 + row * 5, y // 2, 171)
                px[x, y] = ramp([(148, 70, 52), (178, 90, 68), (200, 112, 86)], t)


def t_ice(img):
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            t = hnoise(x // 3, y // 3, 181)
            px[x, y] = ramp([(150, 200, 226), (186, 224, 240), (222, 242, 250)], t)[:3] + (218,)
    for i in range(0, TILE, 7):              # 裂纹
        for k in range(9):
            x, y = i + k, 20 - k
            if 0 <= x < TILE and 0 <= y < TILE:
                px[x, y] = (236, 250, 255, 240)


def t_snow(img):
    grain(img, [(226, 238, 248), (242, 248, 252), (252, 254, 255)], 191, blocky=2)


BLOCKS = [
    ("soil", t_soil),
    ("grass", t_grass),
    ("stone", t_stone),
    ("cobble", t_cobble),
    ("sand", t_sand),
    ("gravel", t_gravel),
    ("water", t_water),
    ("lava", t_lava),
    ("cooled", t_cooled),
    ("log", t_log),
    ("planks", t_planks),
    ("leaves", t_leaves),
    ("glass", t_glass),
    ("explosive", t_explosive),
    ("metal", t_metal),
    ("gold", t_gold),
    ("crystal", t_crystal),
    ("brick", t_brick),
    ("ice", t_ice),
    ("snow", t_snow),
]


def build():
    rows = (len(BLOCKS) + COLS - 1) // COLS
    cell = TILE + PAD * 2
    atlas = Image.new("RGBA", (COLS * cell, rows * cell), (0, 0, 0, 0))
    tiles = []
    for i, (name, fn) in enumerate(BLOCKS):
        im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
        fn(im)
        atlas.paste(im, ((i % COLS) * cell + PAD, (i // COLS) * cell + PAD))
        tiles.append(im)
    return atlas, tiles, rows


def preview(tiles, scale=6, cols=5):
    """接触表：每块放大若干倍，垫深色底，方便肉眼比对。"""
    gap = 8
    rows = (len(tiles) + cols - 1) // cols
    w = cols * (TILE * scale + gap) + gap
    h = rows * (TILE * scale + gap) + gap
    sheet = Image.new("RGB", (w, h), (22, 24, 30))
    for i, t in enumerate(tiles):
        big = t.resize((TILE * scale, TILE * scale), Image.NEAREST)
        x = gap + (i % cols) * (TILE * scale + gap)
        y = gap + (i // cols) * (TILE * scale + gap)
        sheet.paste(big, (x, y), big)
    return sheet


if __name__ == "__main__":
    out_dir = os.path.join("Assets", "Art", "AtlasOriginal")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs("Logs", exist_ok=True)

    atlas, tiles, rows = build()
    atlas.save(os.path.join(out_dir, "atlas.png"))
    with open(os.path.join(out_dir, "blocks.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(n for n, _ in BLOCKS) + "\n")

    preview(tiles).save(os.path.join("Logs", "original_atlas_preview.png"))
    print("图集 %dx%d（%d 块，%d 列 %d 行）" % (atlas.width, atlas.height, len(BLOCKS), COLS, rows))
    print("预览 Logs/original_atlas_preview.png")
