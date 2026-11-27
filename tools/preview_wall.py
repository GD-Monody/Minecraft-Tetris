#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把图集里的方块拼成一面「立体墙」，看看实际进游戏是什么感觉。
纯粹是预览工具，不参与构建。

画法：每块画三面 —— 正面（原图）、顶面（压扁 + 提亮）、右侧面（压窄 + 压暗）。
从后往前画，前面的块自然挡住后面的。
"""

import os
from PIL import Image, ImageEnhance

from build_original_atlas import BLOCKS, TILE, build

DEPTH_X = 10      # 侧面宽度
DEPTH_Y = 10      # 顶面高度
COLS, ROWS = 9, 6

# 一面墙的配方：用名字取图，没写的高度就随机挑一个「实心」块
WALL = [
    ["snow", "snow", "ice", "ice", "stone", "stone", "cobble", "cobble", "leaves"],
    ["soil", "grass", "grass", "log", "planks", "planks", "stone", "cobble", "leaves"],
    ["soil", "soil", "stone", "stone", "brick", "brick", "metal", "metal", "glass"],
    ["soil", "sand", "sand", "stone", "lava", "brick", "metal", "glass", "glass"],
    ["stone", "sand", "gravel", "lava", "lava", "cooled", "explosive", "explosive", "glass"],
    ["stone", "stone", "cooled", "cooled", "crystal", "gold", "explosive", "planks", "water"],
]


def main():
    atlas, tiles, _ = build()
    by_name = {n: t for (n, _), t in zip(BLOCKS, tiles)}

    w = COLS * TILE + DEPTH_X + 20
    h = ROWS * (TILE + DEPTH_Y) + 20
    # 背景：深色 + 上下渐变，接近游戏里暗色主题的场地
    canvas = Image.new("RGBA", (w, h), (26, 29, 36, 255))
    for y in range(h):
        k = 1.0 - y / h * 0.35
        for x in range(0, w, 4):
            canvas.paste(tuple(int(c * k) for c in (26, 29, 36)) + (255,), (x, y, x + 4, y + 1))

    for row in range(ROWS - 1, -1, -1):          # 从后往前
        for col in range(COLS):
            name = WALL[row][col]
            tile = by_name.get(name) or by_name["stone"]
            x = 10 + col * TILE
            y = 10 + row * (TILE + DEPTH_Y)
            top = tile.resize((TILE, DEPTH_Y), Image.LANCZOS)
            side = tile.resize((DEPTH_X, TILE + DEPTH_Y), Image.LANCZOS)
            top = ImageEnhance.Brightness(top).enhance(1.28)
            side = ImageEnhance.Brightness(side).enhance(0.62)
            canvas.paste(top, (x, y), top)
            canvas.paste(side, (x + TILE, y + DEPTH_Y), side)
            canvas.paste(tile, (x, y + DEPTH_Y), tile)

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    log_dir = os.path.join(root, "Logs")
    os.makedirs(log_dir, exist_ok=True)
    out = os.path.join(log_dir, "original_wall_mock.png")
    canvas.convert("RGB").save(out)
    print("立体预览 ->", out, canvas.size)


if __name__ == "__main__":
    main()
