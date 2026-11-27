#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
风格采样器：同一批方块、不同画风，横向对比。

为什么要采样器而不是每种风格画一整套：
整套 19 块 × 8 种风格 = 152 张图，做完了也看不出差别在哪。
挑 6 个「结构差异最大」的代表块——石（大面）、土（松散）、沙（细颗粒）、
液（半透明）、危险（高对比图案）、玻璃（透明+描边）——每种画风一眼就能分出好坏。

用法：
    py -3 tools/style_sampler.py        # 出 16 和 32 两张对比图
产物：
    Logs/style_samples_16.png
    Logs/style_samples_32.png

风格由三件事定义：配色（base）、质感（tex）、边缘处理（edge）。
想加风格只要往 STYLES 里加一条，不用改绘制逻辑。
"""

import os
from PIL import Image, ImageDraw, ImageFont

KINDS = ["stone", "soil", "sand", "liquid", "hazard", "glass"]
KIND_CN = {"stone": "石", "soil": "土", "sand": "沙",
           "liquid": "液", "hazard": "危险", "glass": "玻璃"}

# ── 风格表 ──────────────────────────────────────────────────────────────
# base: 每类方块的主色；tex: 质感；edge: 边缘；accent: 点缀色

STYLES = [
    dict(
        key="flat", name="平涂卡通", cn="平涂卡通",
        base=dict(stone=(122, 132, 148), soil=(148, 108, 78), sand=(226, 206, 150),
                  liquid=(72, 150, 226), hazard=(240, 176, 60), glass=(190, 226, 240)),
        tex="flat", edge="outline", accent=(40, 44, 56),
    ),
    dict(
        key="pastel", name="马卡龙", cn="马卡龙",
        base=dict(stone=(198, 204, 216), soil=(206, 176, 154), sand=(238, 226, 196),
                  liquid=(152, 200, 232), hazard=(244, 198, 168), glass=(214, 236, 240)),
        tex="soft", edge="bevel_soft", accent=(150, 156, 172),
    ),
    dict(
        key="retro", name="复古四色", cn="复古四色",
        base=dict(stone=(88, 96, 88), soil=(120, 88, 56), sand=(200, 184, 120),
                  liquid=(56, 96, 152), hazard=(208, 152, 56), glass=(168, 200, 200)),
        tex="dither", edge="outline", accent=(24, 28, 24),
    ),
    dict(
        key="neon", name="霓虹赛博", cn="霓虹赛博",
        base=dict(stone=(38, 44, 62), soil=(52, 44, 60), sand=(72, 66, 84),
                  liquid=(28, 92, 148), hazard=(84, 44, 72), glass=(30, 52, 70)),
        tex="dark", edge="neon", accent=(96, 232, 255),
    ),
    dict(
        key="blueprint", name="工程蓝图", cn="工程蓝图",
        base=dict(stone=(24, 46, 84), soil=(24, 46, 84), sand=(24, 46, 84),
                  liquid=(24, 46, 84), hazard=(24, 46, 84), glass=(24, 46, 84)),
        tex="blueprint", edge="line", accent=(196, 220, 255),
    ),
    dict(
        key="paper", name="纸艺折纸", cn="纸艺折纸",
        base=dict(stone=(232, 228, 218), soil=(222, 204, 180), sand=(244, 236, 216),
                  liquid=(198, 220, 232), hazard=(236, 208, 168), glass=(238, 244, 248)),
        tex="paper", edge="fold", accent=(150, 142, 130),
    ),
    dict(
        key="etch", name="石版刻蚀", cn="石版刻蚀",
        base=dict(stone=(176, 176, 172), soil=(160, 152, 140), sand=(200, 196, 186),
                  liquid=(140, 150, 160), hazard=(188, 172, 150), glass=(196, 200, 204)),
        tex="hatch", edge="line", accent=(64, 62, 58),
    ),
    dict(
        key="gritty", name="写实颗粒", cn="写实颗粒",
        base=dict(stone=(132, 132, 138), soil=(112, 84, 60), sand=(216, 198, 146),
                  liquid=(58, 118, 200), hazard=(210, 96, 60), glass=(196, 226, 244)),
        tex="grain", edge="bevel", accent=(70, 70, 76),
    ),
]


def hnoise(x, y, salt=0):
    n = (x * 374761393 + y * 668265263 + salt * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def sh(c, f, a=255):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))),
            max(0, min(255, int(c[2] * f))), a)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


# ── 质感 ────────────────────────────────────────────────────────────────


def tex_flat(p, x, y, c, T, st):
    p[x, y] = sh(c, 1.0 + (0.06 if y < T * 0.5 else -0.06))


def tex_soft(p, x, y, c, T, st):
    n = hnoise(x // 2, y // 2, 3) * 0.10 - 0.05
    p[x, y] = sh(c, 1.0 + n)


def tex_grain(p, x, y, c, T, st):
    n = hnoise(x // max(1, T // 16), y // max(1, T // 16), 7)
    p[x, y] = sh(c, 0.82 + n * 0.42)


def tex_dither(p, x, y, c, T, st):
    """四色风格：只有两档明暗，中间调用稀疏棋盘格过渡。
    一开始每像素都摇，16px 下看着像雪花屏 —— 改成只在 1/4 的格子上摇。"""
    n = hnoise(x, y, 11)
    dark = sh(c, 0.66)
    light = sh(c, 1.18)
    if n > 0.72:
        p[x, y] = light
    elif n < 0.28:
        p[x, y] = dark
    else:
        p[x, y] = light if (x + y) % 4 < 2 else dark


def tex_dark(p, x, y, c, T, st):
    n = hnoise(x, y, 13)
    p[x, y] = sh(c, 0.88 + n * 0.20)


def tex_blueprint(p, x, y, c, T, st):
    """蓝图：深底 + 细网格线，内容靠后面的描边表现。"""
    grid = T // 8
    on = (x % grid == 0) or (y % grid == 0)
    p[x, y] = (38, 66, 118, 255) if on else (24, 46, 84, 255)


def tex_paper(p, x, y, c, T, st):
    n = hnoise(x, y, 17)
    p[x, y] = sh(c, 0.96 + n * 0.08)


def tex_hatch(p, x, y, c, T, st):
    """刻蚀：斜线随密度变化，像铜版画。"""
    step = max(2, T // 8)
    on = ((x + y) % step == 0)
    p[x, y] = sh(c, 0.78) if on else sh(c, 1.02)


TEX = dict(flat=tex_flat, soft=tex_soft, grain=tex_grain, dither=tex_dither,
           dark=tex_dark, blueprint=tex_blueprint, paper=tex_paper, hatch=tex_hatch)


# ── 边缘与点缀 ──────────────────────────────────────────────────────────


def edge_outline(img, st, T):
    p = img.load()
    c = st["accent"] + (255,)
    for i in range(T):
        for (x, y) in ((i, 0), (i, T - 1), (0, i), (T - 1, i)):
            p[x, y] = c


def edge_bevel(img, st, T, soft=False):
    p = img.load()
    k = max(1, T // 16)
    hi = 1.22 if not soft else 1.10
    lo = 0.70 if not soft else 0.90
    for i in range(k):
        for x in range(i, T - i):
            p[x, i] = sh(p[x, i], hi)
            p[x, T - 1 - i] = sh(p[x, T - 1 - i], lo)
        for y in range(i, T - i):
            p[i, y] = sh(p[i, y], hi)
            p[T - 1 - i, y] = sh(p[T - 1 - i, y], lo)


def edge_neon(img, st, T):
    p = img.load()
    c = st["accent"]

    # 只在最外圈一条，之前是 1px 框 + 1px 暗边，16px 下框就占掉四分之一
    for i in range(T):
        p[i, 0] = c + (255,)
        p[i, T - 1] = c + (255,)
        p[0, i] = c + (255,)
        p[T - 1, i] = c + (255,)

    # 四个角各画一个 L 形角标：科技面板的通用符号，也给块一个「内容」
    k = max(2, T // 8)
    for i in range(1, 1 + k):
        p[i, 1] = c + (200,)
        p[1, i] = c + (200,)
        p[T - 2 - (i - 1), 1] = c + (200,)
        p[T - 2, i] = c + (200,)
        p[i, T - 2] = c + (200,)
        p[1, T - 1 - i] = c + (200,)
        p[T - 2 - (i - 1), T - 2] = c + (200,)
        p[T - 2, T - 1 - i] = c + (200,)


def edge_line(img, st, T):
    p = img.load()
    c = st["accent"] + (255,)
    for i in range(T):
        p[i, 0] = c
        p[i, T - 1] = c
        p[0, i] = c
        p[T - 1, i] = c


def edge_fold(img, st, T):
    """纸艺：只在上/左折一条亮边，右下折一条暗边，像一张纸对折过。"""
    p = img.load()
    for i in range(T):
        p[i, 0] = sh(p[i, 0], 1.16)
        p[0, i] = sh(p[0, i], 1.10)
        p[i, T - 1] = sh(p[i, T - 1], 0.86)
        p[T - 1, i] = sh(p[T - 1, i], 0.90)


EDGE = dict(outline=edge_outline, bevel=edge_bevel, bevel_soft=None,
            neon=edge_neon, line=edge_line, fold=edge_fold)


# ── 组装一块 ────────────────────────────────────────────────────────────


def paint_symbol(img, kind, st, T):
    """线稿符号：蓝图和刻蚀这两种「靠线条说话」的风格缺了它，六种方块会长一样。"""
    p = img.load()
    c = st["accent"]
    m = max(2, T // 8)          # 边距

    def dot(x, y, a=255):
        if 0 <= x < T and 0 <= y < T:
            p[x, y] = c + (a,)

    if kind == "stone":
        rows = 3
        for r in range(rows):                       # 错缝砖线
            y = m + r * (T - 2 * m) // rows
            for x in range(m, T - m):
                dot(x, y)
            off = 0 if r % 2 == 0 else (T - 2 * m) // 4
            for x in range(m + off, T - m, (T - 2 * m) // 2):
                for yy in range(y, min(y + (T - 2 * m) // rows, T - m)):
                    dot(x, yy)
    elif kind == "soil":
        for i in range(T // 2):                     # 散点
            x = m + (i * 7) % max(1, T - 2 * m)
            y = m + (i * 11) % max(1, T - 2 * m)
            dot(x, y, 220)
    elif kind == "sand":
        import math
        for k in range(3):                          # 波浪
            base = m + k * (T - 2 * m) // 3
            for x in range(m, T - m):
                y = int(base + math.sin(x * 0.7) * (T / 16))
                dot(x, y)
    elif kind == "liquid":
        import math
        for k in range(2):                          # 波纹 + 底线
            base = m + k * (T - 2 * m) // 2
            for x in range(m, T - m):
                y = int(base + math.sin(x * 0.5 + k) * (T / 12))
                dot(x, y)
        for x in range(m, T - m, 2):
            dot(x, T - m - 1, 150)
    elif kind == "hazard":
        for y in range(m, T - m):                   # 斜纹
            for x in range(m, T - m):
                if (x + y) % max(2, T // 8) == 0:
                    dot(x, y)
    elif kind == "glass":
        for x in range(m, T - m):                   # 内框 + 斜线
            dot(x, m)
            dot(x, T - m - 1)
        for y in range(m, T - m):
            dot(m, y)
            dot(T - m - 1, y)
        for i in range((T - 2 * m) // 2):
            dot(m + i, T - m - 1 - i)


def make_tile(kind, st, T):
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = img.load()
    base = st["base"][kind]
    fn = TEX[st["tex"]]

    for y in range(T):
        for x in range(T):
            fn(p, x, y, base, T, st)

    # 材质专属笔画：这些是「认得出是什么」的关键，风格再怎么变都要留
    if kind == "stone":
        for i in range(2, T - 2):
            p[i, i] = sh(p[i, i], 0.82)          # 一条斜纹
    elif kind == "soil":
        for i in range(T // 4, T - 2, 3):
            p[i, i + 1] = sh(p[i, i + 1], 1.14)  # 碎屑亮点
    elif kind == "sand":
        for i in range(1, T - 1, max(2, T // 8)):
            p[i, i] = sh(p[i, i], 1.08)
    elif kind == "liquid":
        for y in range(T):
            for x in range(T):
                if hnoise(x // max(1, T // 8), y // max(1, T // 8), 23) > 0.72:
                    r, g, b, a = p[x, y]
                    p[x, y] = (r, g, b, 205)     # 半透明
        band = T // 4
        for x in range(2, T - 2):
            p[x, band] = sh(p[x, band], 1.30)
    elif kind == "hazard":
        for y in range(T):
            for x in range(T):
                if ((x + y) // max(1, T // 8)) % 2 == 0:
                    p[x, y] = sh(st["base"]["hazard"], 0.35)
    elif kind == "glass":
        for y in range(T):
            for x in range(T):
                r, g, b, a = p[x, y]
                p[x, y] = (r, g, b, 90)
        for i in range(T // 2):
            p[i + 2, T // 2 - i] = (255, 255, 255, 200)

    if st["tex"] in ("blueprint", "hatch"):
        edge_line(img, st, T)
        paint_symbol(img, kind, st, T)           # 按类型画线稿，六块才分得开
    else:
        edge = st["edge"]
        if edge == "bevel_soft":
            edge_bevel(img, st, T, soft=True)
        else:
            EDGE[edge](img, st, T)

    return img


def sheet(T, scale):
    pad, gap = 8, 6
    label_w = 118
    cell = T * scale
    w = label_w + len(KINDS) * (cell + gap) + gap
    h = pad + len(STYLES) * (cell + gap) + gap
    out = Image.new("RGB", (w, h), (18, 20, 26))
    d = ImageDraw.Draw(out)

    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
        small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 11)
    except Exception:
        font = ImageFont.load_default()
        small = font

    for i, kind in enumerate(KINDS):
        x = label_w + i * (cell + gap)
        d.text((x + cell // 2 - 8, 10), KIND_CN[kind], font=small, fill=(150, 158, 172))

    for r, st in enumerate(STYLES):
        y = pad + 22 + r * (cell + gap)
        d.text((pad, y + cell // 2 - 10), st["cn"], font=font, fill=(224, 230, 240))
        d.text((pad, y + cell // 2 + 8), "%dpx" % T, font=small, fill=(120, 128, 142))
        for c, kind in enumerate(KINDS):
            t = make_tile(kind, st, T)
            big = t.resize((cell, cell), Image.NEAREST)
            x = label_w + c * (cell + gap)
            out.paste(big, (x, y), big)
    return out


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    log = os.path.join(root, "Logs")
    os.makedirs(log, exist_ok=True)
    sheet(16, 6).save(os.path.join(log, "style_samples_16.png"))
    sheet(32, 4).save(os.path.join(log, "style_samples_32.png"))
    print("已生成 Logs/style_samples_16.png 与 style_samples_32.png（%d 种风格）" % len(STYLES))
