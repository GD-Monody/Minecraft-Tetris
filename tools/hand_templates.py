#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
手绘模板试验：和「程序化随机」比一比。

结论先行：程序化能解决「不脏」，但解决不了「好看」——
随机游走画出来的裂缝像虫子，随机撒的亮点像划痕，眼睛一看就知道不是人画的。
真正决定质感的是**有设计的图案**：哪块该深、哪几笔该成线，是人定的。

这里把 3 个材质手写成 16x16 的「色阶图」（数字 = 调色板第几档，0 最亮），
再按材质上色。以后要加新方块，只要换调色板，图不用重画。
"""

import os
from PIL import Image, ImageDraw, ImageFont

T = 16

# 色阶图：0=最亮 4=最暗。同一行 16 个字符。
STONE = [
    "2222222222222222",
    "2233222222222222",
    "2233222223322222",
    "2222222223322222",
    "2222222222222222",
    "2222112222222222",
    "2222112222233222",
    "2222222222233222",
    "2222222222222222",
    "2233222222222222",
    "2233222222112222",
    "2222222222112222",
    "2222222222222222",
    "2222223322222222",
    "2222223322222222",
    "2222222222222222",
]

DIRT = [
    "2222222222222222",
    "2212222222212222",
    "2332222222332222",
    "2332222222332222",
    "2222222222222222",
    "2222221222222222",
    "2222233222222222",
    "2222233222222222",
    "2222222222222222",
    "2122222222212222",
    "2332222222332222",
    "2332222222332222",
    "2222222222222222",
    "2222221222222222",
    "2222233222222222",
    "2222222222222222",
]

# 草方块侧面：上面 4 行是草、下面接土，交界每列深浅不一（草垂下来的样子）
GRASS_SIDE = [
    "0011001100110011",
    "1100110011001100",
    "0110011001100110",
    "1010101010101010",
    "3202320232023202",   # 交界：交替草根与土
    "2233223322332233",
    "2222222222222222",
    "2212222222212222",
    "2332222222332222",
    "2332222222332222",
    "2222222222222222",
    "2222221222222222",
    "2222233222222222",
    "2222233222222222",
    "2222222222222222",
    "2122222222212222",
]

PAL = {
    "stone": [(152, 152, 158), (134, 134, 142), (116, 116, 126), (96, 98, 110), (170, 170, 176)],
    "dirt":  [(150, 104, 62), (132, 88, 52), (112, 72, 44), (92, 58, 40), (176, 132, 84)],
    "grass": [(126, 186, 76), (100, 160, 60), (78, 132, 46)],
}


def paint(rows, kind, rows_grass=None):
    """按色阶图上色。前 rows_grass 行用草的调色板，其余用 kind 的调色板。"""
    img = Image.new("RGBA", (T, T), (0, 0, 0, 0))
    p = img.load()
    for y in range(T):
        line = rows[y]
        for x in range(T):
            lv = int(line[x])
            if rows_grass is not None and y < rows_grass:
                pal = PAL["grass"]
                lv = min(len(pal) - 1, lv)
            else:
                pal = PAL[kind]
                lv = min(len(pal) - 1, lv)
            p[x, y] = pal[lv] + (255,)

    # 侧光：右上受光、左下背光（和游戏里那个固定光向一致）
    for x in range(T):
        p[x, 0] = tuple(min(255, int(v * 1.06)) for v in p[x, 0][:3]) + (255,)
        p[x, T - 1] = tuple(int(v * 0.90) for v in p[x, T - 1][:3]) + (255,)
    return img


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    items = [
        ("石（手绘）", paint(STONE, "stone")),
        ("土（手绘）", paint(DIRT, "dirt")),
        ("草侧（手绘）", paint(GRASS_SIDE, "dirt", rows_grass=4)),
    ]
    scale, pad = 10, 12
    cell = T * scale
    w = pad + len(items) * (cell + pad)
    h = pad * 2 + 20 + cell
    sheet = Image.new("RGB", (w, h), (26, 24, 28))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 14)
    except Exception:
        font = ImageFont.load_default()

    for i, (cn, img) in enumerate(items):
        x = pad + i * (cell + pad)
        d.text((x, 4), cn, font=font, fill=(206, 212, 224))
        big = img.resize((cell, cell), Image.NEAREST)
        sheet.paste(big, (x, 22), big)

    out = os.path.join(root, "Logs", "hand_templates.png")
    sheet.save(out)
    print("已生成", out, sheet.size)


if __name__ == "__main__":
    main()
