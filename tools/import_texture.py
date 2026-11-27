#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把「外面画好的大图」规整成游戏能用的 16x16 方块贴图。

用途：不管你用 Material Maker、Aseprite 还是别的工具画出来，导出 PNG 之后
丢给这个脚本，它会做四件事 —— 这四步是 AI 或手绘直接导出时最容易出问题的地方：

    1. 裁成正方形并去掉边缘（水印、边框、留白）
    2. 面积平均降到 16x16（不能直接 NEAREST，会丢细节；也不能 LANCZOS，会振铃）
    3. 量化到一组固定色阶（默认 5 档），保证和别的方块是一套颜色
    4. 边缘对齐：把最外一圈向内侧靠半像素，避免拼起来有缝

用法：
    py -3 tools/import_texture.py 大图.png [输出.png] [--crop 0.85] [--levels 5]
"""

import argparse
import os
from PIL import Image

T = 16


def center_crop(img, ratio=1.0):
    """取中间一块正方形（ratio=0.85 就是取中间 85% 的宽度）。"""
    w, h = img.size
    side = int(min(w, h) * ratio)
    x = (w - side) // 2
    y = (h - side) // 2
    return img.crop((x, y, x + side, y + side))


def downsample(img, tile=T):
    """面积平均。比 NEAREST 保真，比 LANCZOS 少振铃。"""
    return img.convert("RGB").resize((tile, tile), Image.BOX)


def build_palette(img, levels=5):
    """从图里取自己的色阶：先量化，再按亮度排序，保证是一组「同色系」的颜色。"""
    q = img.convert("RGB").quantize(colors=levels, method=Image.MEDIANCUT).convert("RGB")
    colors = q.getcolors(T * T) or []
    cols = [c for _, c in colors]
    cols.sort(key=lambda c: c[0] * 0.299 + c[1] * 0.587 + c[2] * 0.114)
    return cols


def quantize(img, pal):
    """每个像素吸附到调色板里最近的颜色（按 RGB 距离）。"""
    out = Image.new("RGB", img.size)
    sp, dp = img.load(), out.load()
    for y in range(img.height):
        for x in range(img.width):
            c = sp[x, y]
            best = min(pal, key=lambda p: (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 + (p[2] - c[2]) ** 2)
            dp[x, y] = best
    return out


def align_edges(img):
    """最外一圈向内侧靠半档：相邻块拼起来才不会有硬缝。"""
    p = img.load()
    w, h = img.size
    for x in range(w):
        for y in (0, h - 1):
            c = p[x, y]
            p[x, y] = tuple((c[i] * 2 + p[x, 1 if y == 0 else h - 2][i]) // 3 for i in range(3))
    for y in range(h):
        for x in (0, w - 1):
            c = p[x, y]
            p[x, y] = tuple((c[i] * 2 + p[1 if x == 0 else w - 2, y][i]) // 3 for i in range(3))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst", nargs="?", default=None)
    ap.add_argument("--crop", type=float, default=0.86, help="取中间多大的正方形，避开边缘水印/留白")
    ap.add_argument("--levels", type=int, default=5)
    a = ap.parse_args()

    src = Image.open(a.src)
    img = center_crop(src, a.crop)
    small = downsample(img)
    pal = build_palette(small, a.levels)
    out = align_edges(quantize(small, pal))

    dst = a.dst or os.path.splitext(a.src)[0] + "_16.png"
    out.save(dst)

    # 同时出一张对比图：原图裁切 + 16x16 放大
    prev = Image.new("RGB", (T * 12 * 2 + 24, T * 12), (26, 24, 28))
    big = out.resize((T * 12, T * 12), Image.NEAREST)
    prev.paste(big, (8, 0))
    prev.save(os.path.splitext(dst)[0] + "_preview.png")
    print("已生成", dst, "调色板档数", len(pal))


if __name__ == "__main__":
    main()
