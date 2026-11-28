#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA 图标三方案（供挑选）。

上一版为什么丑：
  · 一个等距立方体占了 80% 画布，留白空、配色平，看着像「3D 图标素材站送的默认图标」；
  · 没有轮廓线，缩小到 48px/16px 就是一块灰绿糊；
  · 没有「游戏感」—— 好图标要么一眼看出玩法，要么有一个极简但抢眼的形状。

三个方向：
  A 地层剖面 —— 圆角方块里四道横向地层（草/土/石/深岩），最像「Substrata」这个词，
               平涂 + 粗描边，任何尺寸都清楚；
  B 掉落方块 —— 底部两层地基 + 半空一块正在落下的方块，直接说玩法，最有「游戏感」；
  C 粗描边立方体 —— 单块等距立方，强明暗对比 + 粗描边 + 一道层缝，最稳。

产物：Logs/icon_abc.png（256px 与 48px 两种尺寸并排，看小尺寸是否还认得出）
"""

import math
import os
from PIL import Image, ImageDraw

S = 1024
OUTLINE = (26, 28, 36)

# 纸艺色板
GRASS = (122, 178, 80)
GRASS_D = (92, 142, 60)
SOIL = (172, 126, 84)
SOIL_D = (132, 92, 60)
STONE = (150, 150, 162)
STONE_D = (108, 108, 122)
DEEP = (74, 72, 88)
ACCENT = (255, 214, 130)


def rounded(draw, box, r, fill, outline=None, width=0):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def shade(c, f):
    return tuple(max(0, min(255, int(v * f))) for v in c)


# ── A：地层剖面 ──────────────────────────────────────────────────────────
def icon_strata():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = 70                                  # 外边距
    box = (m, m, S - m, S - m)
    r = 190

    # 深色底 + 圆角（描边就是这层）
    rounded(d, box, r, OUTLINE + (255,))

    inner = (m + 34, m + 34, S - m - 34, S - m - 34)
    r2 = r - 26
    # 先在内部铺满（用于裁圆角），再按比例画四条地层带
    rounded(d, inner, r2, GRASS + (255,))

    h = inner[3] - inner[1]
    bands = [
        (0.00, 0.30, GRASS, GRASS_D),
        (0.30, 0.58, SOIL, SOIL_D),
        (0.58, 0.84, STONE, STONE_D),
        (0.84, 1.00, DEEP, shade(DEEP, 0.85)),
    ]
    for (a, b, c, cd) in bands:
        y0 = inner[1] + int(h * a)
        y1 = inner[1] + int(h * b)
        d.rectangle((inner[0], y0, inner[2], y1), fill=c + (255,))
        # 每条带底部压一档暗色，形成「一层一层」的厚度
        d.rectangle((inner[0], y1 - 18, inner[2], y1), fill=cd + (255,))

    # 重新裁一次圆角：把超出的方块角切掉
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle(inner, radius=r2, fill=255)
    img.putalpha(Image.composite(img.getchannel("A"), Image.new("L", (S, S), 0), mask))

    # 顶面高光（纸被折过的那种反光），一条斜向浅色
    gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(gloss)
    gd.polygon([(inner[0], inner[1] + 210), (inner[0] + 210, inner[1]),
                (inner[0] + 330, inner[1]), (inner[0], inner[1] + 330)],
               fill=(255, 255, 255, 26))
    gloss.putalpha(Image.composite(gloss.getchannel("A"), Image.new("L", (S, S), 0), mask))
    img = Image.alpha_composite(img, gloss)
    return img


# ── B：掉落方块 ──────────────────────────────────────────────────────────
def iso_cube(d, cx, top_y, w, h, top_c, left_c, right_c, ow=14):
    """等距立方体：顶面菱形 + 左右两面。带描边。"""
    pts_top = [(cx, top_y), (cx + w, top_y + h), (cx, top_y + 2 * h), (cx - w, top_y + h)]
    pts_left = [(cx - w, top_y + h), (cx, top_y + 2 * h), (cx, top_y + 2 * h + h * 2), (cx - w, top_y + h + h * 2)]
    pts_right = [(cx, top_y + 2 * h), (cx + w, top_y + h), (cx + w, top_y + h + h * 2), (cx, top_y + 2 * h + h * 2)]
    for pts, c in ((pts_left, left_c), (pts_right, right_c), (pts_top, top_c)):
        d.polygon(pts, fill=c + (255,), outline=OUTLINE + (255,), width=ow)


def icon_falling():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = S // 2
    # 一块等距立方体占 4h 的高度（顶面 2h + 侧面 2h）。三块塞不进 1024，改成两块：
    # 上面一块正在落、下面一块地基，两块之间留一指宽的缝。
    w = 215
    h = 107
    iso_cube(d, cx, 120, w, h, (150, 200, 96), GRASS, GRASS_D)
    iso_cube(d, cx, 120 + h * 4 + 90, w, h, SOIL, SOIL_D, shade(SOIL_D, 0.82))

    # 下落速度线
    for dx in (-w - 52, w + 52):
        x = cx + dx
        d.line([(x, 400), (x, 320)], fill=(170, 176, 196, 110), width=13)
    return img


# ── C：粗描边立方体 ──────────────────────────────────────────────────────
def icon_cube():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = S // 2
    w = 330
    h = 165
    top_y = 210
    iso_cube(d, cx, top_y, w, h, (176, 226, 120), GRASS, GRASS_D, ow=22)
    # 两道层缝：沿左右两个侧面走，和面的边平行 —— 暗示「一层压一层」
    side_top = top_y + 2 * h
    for k in (1, 2):
        dy = (h * 2) * k // 3
        yl = side_top + dy
        d.line([(cx - w, yl - h), (cx, yl)], fill=OUTLINE + (255,), width=12)
        d.line([(cx, yl), (cx + w, yl - h)], fill=OUTLINE + (255,), width=12)
    return img


def sheet():
    items = [("A 地层剖面", icon_strata()), ("B 掉落方块", icon_falling()), ("C 粗描边立方", icon_cube())]
    pad = 24
    big, small = 256, 48
    cols = len(items)
    w = pad + cols * (big + pad)
    hgt = pad + big + pad + small + pad + 24
    out = Image.new("RGB", (w, hgt), (32, 30, 38))
    from PIL import ImageDraw as ID, ImageFont
    d = ID.Draw(out)
    try:
        f = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 18)
    except Exception:
        f = ImageFont.load_default()
    for i, (name, im) in enumerate(items):
        x = pad + i * (big + pad)
        d.text((x, 4), name, font=f, fill=(230, 232, 240))
        b = im.resize((big, big), Image.LANCZOS)
        out.paste(b, (x, 26), b)
        s = im.resize((small, small), Image.LANCZOS)
        out.paste(s, (x, 26 + big + pad), s)
        d.text((x + small + 10, 26 + big + pad + 12), "48px", font=f, fill=(150, 156, 170))
    return out


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    sheet().save(os.path.join(root, "Logs", "icon_abc.png"))
    for name, fn in (("a_strata", icon_strata), ("b_falling", icon_falling), ("c_cube", icon_cube)):
        fn().save(os.path.join(root, "Logs", "icon_%s_1024.png" % name))
    print("三个方案已出 Logs/icon_abc.png")
