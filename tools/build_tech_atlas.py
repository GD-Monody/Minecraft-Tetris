#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA —— 科技风方块图集（16x16，全程序化绘制）。

16 像素每块只有 256 个像素可以说话，所以不能靠「纹理细节」，得靠「符号」：
铆钉、缝隙、发光条、斜纹警示、单像素高光。这一版先用 18 块把科技风的语汇立起来。

和 32x32 那版的区别不只是尺寸：那版是「自然写实」（噪点堆出泥土感），
这版是「工业符号」（干净的平涂 + 明确的几何笔画），所以基本不用噪声，
改用对称、重复的机械结构。
"""

import os
from PIL import Image

TILE = 16
PAD = 1
COLS = 6

# ── 科技风配色：冷灰钢 + 青色霓虹 + 警示橙/红 ──────────────────────────
STEEL = [(70, 78, 90), (92, 101, 116), (118, 128, 145)]     # 基础钢
STEEL_DARK = [(40, 45, 54), (54, 60, 71), (68, 75, 88)]
CYAN = (86, 226, 244)
CYAN_DIM = (44, 132, 150)
AMBER = (255, 176, 64)
RED = (226, 74, 58)
WHITE_PANEL = [(168, 176, 188), (192, 200, 212), (214, 222, 232)]


def px(img):
    return img.load()


def fill(img, colors, top_bias=0.0):
    """竖直渐变铺底：上面亮一点，像被顶光照着。"""
    p = px(img)
    for y in range(TILE):
        t = 1.0 - (y / (TILE - 1)) * (0.22 - top_bias)
        for x in range(TILE):
            c = colors[min(len(colors) - 1, int((1.0 - y / TILE) * len(colors)))]
            p[x, y] = tuple(max(0, min(255, int(v * t))) for v in c) + (255,)


def seam(img, color=(28, 32, 40), inset=1):
    """四周留一圈暗缝：块与块拼起来才有"每块是独立构件"的感觉。"""
    p = px(img)
    for x in range(inset, TILE - inset):
        p[x, inset - 1] = color + (255,) if inset > 0 else p[x, 0]
        p[x, TILE - inset] = color + (255,)
    for y in range(inset, TILE - inset):
        p[inset - 1, y] = color + (255,)
        p[TILE - inset, y] = color + (255,)


def bolts(img, color=(150, 160, 175)):
    """四角铆钉：16px 上就是一个亮点 + 一个暗影，两像素搞定。"""
    p = px(img)
    for (x, y) in ((2, 2), (TILE - 3, 2), (2, TILE - 3), (TILE - 3, TILE - 3)):
        p[x, y] = color + (255,)
        p[x + 1, y] = (40, 45, 54, 255)


def hline(img, y, color, x0=0, x1=TILE):
    p = px(img)
    for x in range(x0, x1):
        p[x, y] = color + (255,)


def vline(img, x, color, y0=0, y1=TILE):
    p = px(img)
    for y in range(y0, y1):
        p[x, y] = color + (255,)


def glow(img, x0, y0, x1, y1, color):
    """发光矩形：本体亮色，外面一圈暗一档，看起来在发亮。"""
    p = px(img)
    dark = tuple(int(v * 0.45) for v in color)
    for y in range(y0 - 1, y1 + 1):
        for x in range(x0 - 1, x1 + 1):
            if 0 <= x < TILE and 0 <= y < TILE:
                p[x, y] = dark + (255,)
    for y in range(y0, y1):
        for x in range(x0, x1):
            if 0 <= x < TILE and 0 <= y < TILE:
                p[x, y] = color + (255,)


# ── 18 种方块 ────────────────────────────────────────────────────────────


def b_plate(img):
    """基础钢板：平涂 + 中缝 + 四角铆钉。"""
    fill(img, STEEL)
    seam(img)
    hline(img, 7, (56, 62, 74))
    hline(img, 8, (140, 150, 166))
    bolts(img)


def b_grate(img):
    """通风格栅：横向百叶，缝里透暗。"""
    fill(img, STEEL_DARK)
    for y in range(2, TILE - 2, 3):
        hline(img, y, (128, 138, 155), 2, TILE - 2)
        hline(img, y + 1, (22, 26, 32), 2, TILE - 2)
    seam(img)


def b_steel_brick(img):
    """钢砖：错缝砌法的金属板。"""
    fill(img, STEEL)
    for row, y in enumerate(range(3, TILE - 2, 4)):
        hline(img, y, (36, 42, 52), 1, TILE - 1)
        off = 0 if row % 2 == 0 else 4
        for x in range(1 + off, TILE - 1, 8):
            vline(img, x, (36, 42, 52), y - 3, y)
    seam(img)


def b_rivet(img):
    """铆接板：密排铆钉，像船体。"""
    fill(img, STEEL_DARK)
    for y in range(3, TILE - 2, 5):
        for x in range(3, TILE - 2, 5):
            p = px(img)
            p[x, y] = (168, 178, 194, 255)
            p[x + 1, y] = (52, 58, 70, 255)
    seam(img)


def b_hazard(img):
    """警示斜纹：黄黑相间，机械行业通用符号。"""
    p = px(img)
    for y in range(TILE):
        for x in range(TILE):
            band = ((x + y) // 3) % 2
            c = AMBER if band else (38, 38, 42)
            p[x, y] = c + (255,)
    seam(img, (20, 20, 24))


def b_conduit(img):
    """管道：两根纵向管线 + 卡箍。"""
    fill(img, STEEL_DARK)
    for x in (4, 5, 10, 11):
        vline(img, x, (36, 42, 52), 1, TILE - 1)
    for x in (4, 10):
        vline(img, x + 1, (154, 164, 180), 2, TILE - 2)
    for y in (4, 11):
        hline(img, y, (110, 120, 136), 2, TILE - 2)
    seam(img)


def b_circuit(img):
    """电路板：走线 + 焊点。"""
    fill(img, [(26, 44, 44), (32, 54, 52), (38, 64, 60)])
    traces = [(2, 4, 9), (10, 4, 12), (4, 9, 12)]
    for y, x0, x1 in traces:
        hline(img, y, (86, 200, 170), x0, x1)
    vline(img, 12, (86, 200, 170), 4, 10)
    vline(img, 4, (86, 200, 170), 9, 13)
    p = px(img)
    for (x, y) in ((4, 9), (12, 4), (12, 10), (9, 4)):
        p[x, y] = (196, 255, 236, 255)
    seam(img, (16, 26, 26))


def b_data(img):
    """数据块：方格状的量化纹理，像屏幕像素。"""
    fill(img, [(20, 36, 54), (26, 46, 66), (34, 58, 80)])
    p = px(img)
    for y in range(2, TILE - 2, 3):
        for x in range(2, TILE - 2, 3):
            if (x + y) % 2 == 0:
                p[x, y] = (72, 190, 220, 255)
                p[x + 1, y + 1] = (40, 110, 140, 255)
    seam(img)


def b_led(img):
    """灯带：一条发光条 + 光晕。"""
    fill(img, STEEL_DARK)
    glow(img, 2, 6, TILE - 2, 10, CYAN)
    seam(img)


def b_coolant(img):
    """冷却液（替代「水」）：青蓝色，横向流动感 + 顶部高光。"""
    p = px(img)
    for y in range(TILE):
        for x in range(TILE):
            k = 0.72 + 0.28 * abs(((y * 1.7) % 4) / 4 - 0.5) * 2
            base = (34, 132, 160) if y % 4 else (56, 176, 200)
            c = tuple(int(v * k) for v in base)
            p[x, y] = c + (210,)
    hline(img, 2, (140, 240, 255), 2, TILE - 2)


def b_plasma(img):
    """等离子（替代「岩浆」）：橙红发光 + 内部亮核。"""
    fill(img, [(120, 34, 20), (176, 58, 24), (214, 86, 30)])
    glow(img, 4, 4, 12, 12, (255, 150, 60))
    glow(img, 6, 6, 10, 10, (255, 226, 140))
    seam(img, (70, 20, 12))


def b_glass(img):
    """能量玻璃：半透明 + 边框 + 斜向高光。"""
    p = px(img)
    for y in range(TILE):
        for x in range(TILE):
            p[x, y] = (120, 210, 235, 70)
    for x in range(TILE):
        p[x, 0] = (170, 236, 250, 230)
        p[x, TILE - 1] = (170, 236, 250, 230)
    for y in range(TILE):
        p[0, y] = (170, 236, 250, 230)
        p[TILE - 1, y] = (170, 236, 250, 230)
    for i in range(5):
        p[3 + i, 12 - i] = (235, 252, 255, 190)


def b_battery(img):
    """储能块：中间一条电量格。"""
    fill(img, STEEL)
    seam(img)
    p = px(img)
    for y in range(4, 12):
        p[3, y] = (200, 210, 222, 255)
        p[12, y] = (200, 210, 222, 255)
    for i, y in enumerate(range(5, 11, 2)):
        hline(img, y, (86, 240, 160) if i < 2 else (60, 90, 80), 5, 11)
    bolts(img)


def b_core(img):
    """核心：中心一个亮的能量点，四周暗。"""
    p = px(img)
    for y in range(TILE):
        for x in range(TILE):
            d = max(abs(x - 7.5), abs(y - 7.5))
            k = max(0.15, 1.0 - d / 8.0)
            p[x, y] = (int(30 + 60 * k), int(90 + 140 * k), int(120 + 120 * k), 255)
    glow(img, 6, 6, 10, 10, (200, 255, 255))
    seam(img, (18, 40, 52))


def b_vent(img):
    """排气口：同心方框，中间留黑洞。"""
    fill(img, STEEL_DARK)
    for i in (2, 4, 6):
        for x in range(i, TILE - i):
            p = px(img)
            p[x, i] = (120, 130, 146, 255)
            p[x, TILE - 1 - i] = (120, 130, 146, 255)
        for y in range(i, TILE - i):
            p[i, y] = (120, 130, 146, 255)
            p[TILE - 1 - i, y] = (120, 130, 146, 255)
    p = px(img)
    for y in range(7, 9):
        for x in range(7, 9):
            p[x, y] = (14, 16, 20, 255)


def b_armor(img):
    """装甲板：中间一个斜切面，看着更厚。"""
    fill(img, STEEL)
    p = px(img)
    for i in range(TILE):
        p[i, i] = (176, 186, 202, 255)
        if i + 1 < TILE:
            p[i + 1, i] = (48, 54, 66, 255)
    seam(img)
    bolts(img, (196, 206, 220))


def b_white_panel(img):
    """白色涂层板：给「颜色变体」探个路。"""
    fill(img, WHITE_PANEL)
    seam(img, (120, 128, 140))
    vline(img, 7, (150, 158, 170))
    hline(img, 8, (232, 238, 246), 1, TILE - 1)


def b_dust(img):
    """金属粉尘（替代「沙」）：冷灰细颗粒。原来用暖沙色，整张图里就它一个暖色，扎眼。"""
    p = px(img)
    for y in range(TILE):
        for x in range(TILE):
            n = ((x * 37 + y * 91) % 13) / 13.0
            c = int(118 + n * 46)
            p[x, y] = (c - 4, c, c + 8, 255)
    # 几粒亮的金属屑
    for (x, y) in ((4, 5), (11, 3), (7, 12), (13, 10)):
        p[x, y] = (206, 216, 230, 255)
    seam(img, (62, 68, 80))


def b_rubble(img):
    """废料（替代「砾石」）：不规则铁屑，靠暗缝把碎块分开。"""
    p = px(img)
    chunks = [(3, 3, 5, 4, 96), (9, 2, 5, 5, 78), (2, 9, 4, 5, 118),
              (7, 8, 4, 3, 68), (12, 9, 3, 4, 106), (6, 13, 8, 2, 88)]
    for y in range(TILE):
        for x in range(TILE):
            p[x, y] = (40, 44, 52, 255)
    for (x0, y0, w, h, v) in chunks:
        for y in range(y0, min(TILE, y0 + h)):
            for x in range(x0, min(TILE, x0 + w)):
                p[x, y] = (v, v + 8, v + 18, 255)
        for x in range(x0, min(TILE, x0 + w)):
            p[x, y0] = (v + 40, v + 48, v + 58, 255)
    seam(img, (24, 28, 34))


BLOCKS = [
    ("plate", b_plate),
    ("grate", b_grate),
    ("steel_brick", b_steel_brick),
    ("rivet", b_rivet),
    ("hazard", b_hazard),
    ("conduit", b_conduit),
    ("circuit", b_circuit),
    ("data", b_data),
    ("led", b_led),
    ("coolant", b_coolant),
    ("plasma", b_plasma),
    ("glass", b_glass),
    ("battery", b_battery),
    ("core", b_core),
    ("vent", b_vent),
    ("armor", b_armor),
    ("white_panel", b_white_panel),
    ("dust", b_dust),
    ("rubble", b_rubble),
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


def preview(tiles, scale=8, cols=5):
    gap = 8
    rows = (len(tiles) + cols - 1) // cols
    w = cols * (TILE * scale + gap) + gap
    h = rows * (TILE * scale + gap) + gap
    sheet = Image.new("RGB", (w, h), (18, 20, 26))
    for i, t in enumerate(tiles):
        big = t.resize((TILE * scale, TILE * scale), Image.NEAREST)
        x = gap + (i % cols) * (TILE * scale + gap)
        y = gap + (i // cols) * (TILE * scale + gap)
        sheet.paste(big, (x, y), big)
    return sheet


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "Assets", "Art", "AtlasTech")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(os.path.join(root, "Logs"), exist_ok=True)

    atlas, tiles, rows = build()
    atlas.save(os.path.join(out_dir, "atlas.png"))
    with open(os.path.join(out_dir, "blocks.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(n for n, _ in BLOCKS) + "\n")
    preview(tiles).save(os.path.join(root, "Logs", "tech_atlas_preview.png"))
    print("图集 %dx%d，%d 块" % (atlas.width, atlas.height, len(BLOCKS)))
