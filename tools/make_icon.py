#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SUBSTRATA 应用图标生成器。

设计：三层叠起来的等距方块 —— 草（顶）/ 土 / 石。
「一层压一层」正是游戏名和核心机制（3D 深度、层次图），比单个方块更有辨识度。

全部代码绘制：多边形几何 + 纸面颗粒 + 棱边高光，不引用任何第三方素材。
产物：Assets/Art/AppIcon/app_icon_<size>.png（构建脚本按尺寸槽位取用）
"""

import math
import os
import random
from PIL import Image, ImageDraw

S = 1024                      # 原图边长
W = int(S * 0.40)             # 立方体半宽（留出边距）
HR = W // 2                   # 顶面菱形的半高
T = int(W * 0.42)             # 每层厚度

# 调色板（和游戏图集同一套纸艺色）
GRASS_TOP = (122, 178, 80)
GRASS_L = (96, 146, 64)
GRASS_R = (78, 122, 52)
SOIL_L = (168, 122, 78)
SOIL_R = (136, 96, 60)
STONE_L = (146, 146, 156)
STONE_R = (114, 114, 128)
EDGE_LIGHT = (255, 255, 255, 90)


def shade(c, f, a=255):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))),
            max(0, min(255, int(c[2] * f))), a)


def h(x, y, s=0):
    n = (x * 374761393 + y * 668265263 + s * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def build():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx = S // 2
    # 顶面菱形的最上点：按「整个堆叠高度」居中，之前是按半高算的，底部被裁掉了
    stack_h = HR * 2 + T * 3
    top_y = (S - stack_h) // 2

    # 三层：从上往下
    layers = [
        # (左面颜色, 右面颜色, 是否画顶面)
        (GRASS_L, GRASS_R, True),
        (SOIL_L, SOIL_R, False),
        (STONE_L, STONE_R, False),
    ]

    for i, (cl, cr, draw_top) in enumerate(layers):
        y0 = top_y + i * T
        # 顶面：菱形（只有最上层可见）
        if draw_top:
            d.polygon([(cx, y0), (cx + W, y0 + HR), (cx, y0 + HR * 2), (cx - W, y0 + HR)],
                      fill=GRASS_TOP + (255,))
        # 左面：平行四边形
        d.polygon([(cx - W, y0 + HR), (cx, y0 + HR * 2),
                   (cx, y0 + HR * 2 + T), (cx - W, y0 + HR + T)], fill=cl + (255,))
        # 右面
        d.polygon([(cx, y0 + HR * 2), (cx + W, y0 + HR),
                   (cx + W, y0 + HR + T), (cx, y0 + HR * 2 + T)], fill=cr + (255,))

    # 纸面颗粒：整个图标统一加一层极轻的噪声（不破坏色块，但去掉塑料感）
    px = img.load()
    for y in range(S):
        for x in range(S):
            c = px[x, y]
            if c[3] == 0:
                continue
            n = (h(x // 3, y // 3, 7) - 0.5) * 0.06
            px[x, y] = (max(0, min(255, int(c[0] * (1 + n)))),
                        max(0, min(255, int(c[1] * (1 + n)))),
                        max(0, min(255, int(c[2] * (1 + n)))), c[3])

    # 层间暗缝：给三层之间压一条深色线，层叠关系才清楚
    d2 = ImageDraw.Draw(img)
    for i in (1, 2):
        y0 = top_y + i * T
        d2.line([(cx - W, y0 + HR), (cx, y0 + HR * 2)], fill=(0, 0, 0, 60), width=4)
        d2.line([(cx, y0 + HR * 2), (cx + W, y0 + HR)], fill=(0, 0, 0, 60), width=4)

    # 棱边高光：把几何边界描一条细亮线，像素进游戏图标才会「立」起来
    d = ImageDraw.Draw(img)
    for i in range(3):
        y0 = top_y + i * T
        d.line([(cx - W, y0 + HR), (cx, y0 + HR * 2)], fill=(255, 255, 255, 60), width=3)
        d.line([(cx, y0 + HR * 2), (cx, y0 + HR * 2 + T)], fill=(255, 255, 255, 40), width=2)
    # 顶面外棱
    d.line([(cx - W, top_y + HR), (cx, top_y)], fill=(255, 255, 255, 55), width=3)
    d.line([(cx, top_y), (cx + W, top_y + HR)], fill=(255, 255, 255, 40), width=3)

    return img


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon = build()
    icon.save(os.path.join(root, "Logs", "icon_preview.png"))

    out_dir = os.path.join(root, "Assets", "Art", "AppIcon")
    os.makedirs(out_dir, exist_ok=True)
    sizes = [16, 32, 48, 64, 72, 96, 128, 144, 192, 256, 512, 1024]
    for s in sizes:
        # 大尺寸保留原图细节，小尺寸用 LANCZOS 缩，避免锯齿
        im = icon.resize((s, s), Image.LANCZOS)
        im.save(os.path.join(out_dir, "app_icon_%d.png" % s))
    md = os.path.join(root, "docs", "icon")
    os.makedirs(md, exist_ok=True)
    icon.resize((512, 512), Image.LANCZOS).save(os.path.join(md, "app_icon_512.png"))
    icon.resize((1024, 1024), Image.LANCZOS).save(os.path.join(md, "app_icon_1024.png"))
    print("图标已生成 %d 个尺寸" % len(sizes))
