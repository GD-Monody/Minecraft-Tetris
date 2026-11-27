#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
材质实验台：换掉「每像素随机取色」，改用「成块噪声 + 手放特征」。

为什么之前的不好看：
    逐像素从调色板里随机取色 → 相邻像素频繁跳色 → 观感是电视雪花。
    原版的纹理是「成块的」：一片 2~4 像素的同色区，边缘再散几粒，然后叠加
    明确的特征（裂缝、草叶、沙纹）。眼睛读的是「块」，不是「点」。

这里的三层结构：
    1. 块状底：value noise（双线性插值 + 平滑）两层叠加，量化到 4~5 档；
       ——插值保证同色成片，量化保证像像素画而不是模糊照片
    2. 特征笔触：每个材质手放（石头裂缝、泥土颗粒、草叶、沙纹）
    3. 面差异：顶面亮、颗粒细；侧面暗、竖向拉伸；底面最暗

产物：Logs/tex_lab.png —— 上排是现在图集里的，下排是新画法。
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


def lerp(a, b, t):
    return a + (b - a) * t


def vnoise(x, y, scale, seed):
    """值噪声：格点哈希 + 双线性插值 + smoothstep。同色会成片。"""
    fx, fy = x / scale, y / scale
    x0, y0 = math.floor(fx), math.floor(fy)
    tx, ty = fx - x0, fy - y0
    tx = tx * tx * (3 - 2 * tx)
    ty = ty * ty * (3 - 2 * ty)
    n00 = h(x0, y0, seed)
    n10 = h(x0 + 1, y0, seed)
    n01 = h(x0, y0 + 1, seed)
    n11 = h(x0 + 1, y0 + 1, seed)
    return lerp(lerp(n00, n10, tx), lerp(n01, n11, tx), ty)


def field(seed, sx=3.6, sy=3.6, oct2=0.42):
    """两层叠加的噪声场，返回 0..1 的二维数组。"""
    out = []
    for y in range(T):
        row = []
        for x in range(T):
            a = vnoise(x, y, sx, seed)
            b = vnoise(x, y, 1.9, seed + 5)
            row.append(min(1.0, a * (1 - oct2) + b * oct2))
        out.append(row)
    return out


def quant(f, levels=5):
    """量化到指定档数：这一步是「像素画」和「照片」的分界。"""
    return min(levels - 1, max(0, int(f * levels)))


# ── 调色板：同色系相邻两档只差一点，另有一档深阴影、一档亮高光 ───────────
PAL = {
    # 更饱和、更接近原版的一组：每档都带一点色相偏移（暗部偏紫、亮部偏黄），
    # 单纯压暗会发灰，这是之前看着"脏"的主因
    "dirt":  [(150, 104, 62), (132, 88, 52), (112, 72, 44), (92, 58, 40), (176, 132, 84)],
    "grass": [(104, 164, 62), (86, 142, 52), (68, 118, 42), (50, 96, 36), (138, 196, 84)],
    "stone": [(142, 142, 148), (124, 124, 132), (106, 106, 116), (86, 88, 100), (170, 170, 176)],
    "sand":  [(232, 208, 142), (218, 192, 126), (200, 174, 110), (178, 152, 92), (248, 230, 176)],
    "gravel": [(150, 144, 136), (132, 126, 120), (112, 108, 104), (92, 88, 86), (176, 170, 162)],
}


def draw_face(kind, which, seed):
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = img.load()
    pal = PAL[kind]
    f = field(seed, *( (2.6, 2.6) if which == "side" else (3.4, 3.4) ))

    # 面差异：顶面整体提亮、底面压暗（用调色板下标整体偏移实现，不另设颜色）
    shift = {"top": -0, "side": 1, "bottom": 2}.get(which, 1)
    if which == "side":
        # 侧面把噪声竖向拉长：同一个 x 列用同一水平位置的噪声
        f = field(seed, 2.2, 5.0)

    for y in range(T):
        for x in range(T):
            lv = min(len(pal) - 1, quant(f[y][x], len(pal)) + (1 if which == "bottom" else 0))
            p[x, y] = pal[lv] + (255,)

    rnd = random.Random(seed * 7 + 1)

    if kind == "stone":
        # 裂缝：随机走一条 8~10 像素的线，用最暗的那档
        cx, cy = rnd.randrange(3, T - 3), rnd.randrange(3, T - 3)
        for i in range(9):
            p[cx, cy] = pal[-1] + (255,)
            p[cx + 1, cy] = tuple(int(v * 0.92) for v in pal[-1]) + (255,)
            cx += rnd.choice((-1, 0, 1))
            cy += 1
            cx = max(1, min(T - 2, cx))
            cy = max(1, min(T - 2, cy))
        for _ in range(3):                       # 两三个亮点当反光
            x, y = rnd.randrange(T), rnd.randrange(T)
            p[x, y] = pal[0] + (255,) if lv_light(pal) else pal[0] + (255,)

    elif kind == "dirt":
        # 颗粒：2x2 暗块 + 单像素亮点，数量不多
        for _ in range(7):
            x, y = rnd.randrange(T - 1), rnd.randrange(T - 1)
            p[x, y] = pal[3] + (255,)
            p[x + 1, y] = pal[3] + (255,)
            p[x, y + 1] = pal[2] + (255,)
        for _ in range(6):
            x, y = rnd.randrange(T), rnd.randrange(T)
            p[x, y] = pal[4] + (255,)

    elif kind == "grass" and which in ("top", "side"):
        # 草叶：竖着一两像素，长短不一
        for _ in range(22):
            x = rnd.randrange(T)
            depth = rnd.choice((1, 1, 2))
            for k in range(depth):
                y = rnd.randrange(0, T if which == "top" else 5)
                p[x, y] = pal[0] if rnd.random() < 0.6 else pal[4]
            p[x, min(T - 1, y)] = pal[4] + (255,)
    elif kind == "grass" and which == "bottom":
        pass

    elif kind == "sand":
        # 沙纹：横向 1px 波线，两条
        for base in (4, 11):
            for x in range(T):
                y = base + int(round(math.sin(x * 0.55 + seed) * 0.7))
                if 0 <= y < T:
                    p[x, y] = pal[3] + (255,)
        for _ in range(8):
            x, y = rnd.randrange(T), rnd.randrange(T)
            p[x, y] = pal[4] + (255,)

    elif kind == "gravel":
        # 卵石：显式摆圆石（这一套之前验证过好用）
        p2 = img.load()
        for y in range(T):
            for x in range(T):
                p2[x, y] = pal[3] + (255,)
        for gy in range(4):
            for gx in range(4):
                cx = gx * 4 + 2 + rnd.uniform(-0.8, 0.8)
                cy = gy * 4 + 2 + rnd.uniform(-0.8, 0.8)
                r = rnd.uniform(1.6, 2.4)
                tone = rnd.random()
                for y in range(max(0, int(cy - r - 1)), min(T, int(cy + r + 2))):
                    for x in range(max(0, int(cx - r - 1)), min(T, int(cx + r + 2))):
                        dx, dy = x - cx, y - cy
                        if math.hypot(dx, dy) > r:
                            continue
                        lv = quant(tone, 3)
                        if dx + dy < -r * 0.3:
                            lv = max(0, lv - 1)
                        elif dx + dy > r * 0.4:
                            lv = min(len(pal) - 2, lv + 1)
                        p2[x, y] = pal[lv] + (255,)

    # 面边缘：顶面亮、底面暗，一条就够
    for x in range(T):
        if which != "bottom":
            p[x, 0] = tuple(min(255, int(v * 1.10)) for v in p[x, 0][:3]) + (255,)
        p[x, T - 1] = tuple(int(v * 0.88) for v in p[x, T - 1][:3]) + (255,)
    return img


def lv_light(pal):
    return True


def old_tile(name):
    """从现有图集里取一块（图集 8 列，1px padding）。"""
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    atlas = Image.open(os.path.join(root, "Assets", "Art", "AtlasPaper", "atlas.png"))
    names = open(os.path.join(root, "Assets", "Art", "AtlasPaper", "blocks.txt"),
                 encoding="utf-8").read().split()
    i = names.index(name)
    cell = T + 2
    x, y = (i % 8) * cell + 1, (i // 8) * cell + 1
    return atlas.crop((x, y, x + T, y + T))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rows = [("dirt", "soil"), ("grass", "grass"), ("stone", "stone"),
            ("sand", "sand"), ("gravel", "gravel")]
    scale, pad = 6, 10
    cell = T * scale
    w = pad + len(rows) * (cell + pad)
    h = pad * 2 + 18 + (cell + pad) + 18 + cell
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)
    except Exception:
        font = ImageFont.load_default()

    d.text((pad, 4), "上：现在图集里的（逐像素随机取色）", font=font, fill=(150, 156, 170))
    d.text((pad, 18 + cell + pad), "下：新画法（成块噪声 + 手放特征）", font=font, fill=(150, 156, 170))

    for i, (new_kind, old_name) in enumerate(rows):
        x = pad + i * (cell + pad)
        old = old_tile(old_name).resize((cell, cell), Image.NEAREST)
        sheet.paste(old, (x, 18), old)
        new = draw_face(new_kind, "side", seed=3 + i).resize((cell, cell), Image.NEAREST)
        sheet.paste(new, (x, 18 + cell + pad), new)

    out = os.path.join(root, "Logs", "tex_lab.png")
    sheet.save(out)
    print("已生成", out, sheet.size)


if __name__ == "__main__":
    main()
