#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
三值草稿演示：先只画「亮/中/暗」，判断结构，再上色。

这是美术的实际工作顺序 —— 结构没定就上色，最后一定返工。
三个候选都是石头，只差明暗分布，请对比哪个「像石头」：

    A 大块斑：一块大的暗区偏左下，右上几点亮 —— 像被削过一角的岩壁
    B 碎斑  ：很多小团块均匀铺开 —— 最常见、也最保险的石头
    C 裂纹  ：整体平，一条短裂纹 + 两处亮点 —— 像地面石板

产出 Logs/value_study.png：第一行三值草稿，第二行上色，第三行 3x3 平铺（查接缝）。
"""

import os
from PIL import Image, ImageDraw, ImageFont

T = 16

DRAFTS = {
    "A 大块斑": [
        "1111111111111111",
        "1111111111111111",
        "1111111111110111",
        "1111111111111111",
        "1112221111111111",
        "1122222111111011",
        "1222222211111111",
        "1222222211111111",
        "1222222111111111",
        "1122221111111111",
        "1112211111111111",
        "1111111111111111",
        "1111110111111111",
        "1111111111111111",
        "1111111111111111",
        "1111111111111111",
    ],
    "B 碎斑": [
        "1111111111111111",
        "1122111111112211",
        "1222211111122221",
        "1222111111122221",
        "1111111111111111",
        "1111112211111111",
        "1101112221110111",
        "1111112211111111",
        "1111111111111111",
        "1111221111112211",
        "1112222111122221",
        "1112211111122111",
        "1111111111111111",
        "1221111111111111",
        "1222111111111221",
        "1111111111111111",
    ],
    "C 裂纹": [
        "1111111111111111",
        "1111111111111111",
        "1111111211111111",
        "1111111211111111",
        "1111111211111111",
        "1111111121111011",
        "1111111121111111",
        "1111111122111111",
        "1111111111211111",
        "1111111111211111",
        "1110111111121111",
        "1111111111121111",
        "1111111111111111",
        "1111111111111111",
        "1111111111111111",
        "1111111111111111",
    ],
}

# 三档草稿 -> 五档上色。注意每一档蓝通道都在涨（冷阴影），红绿涨得慢
# 三档 -> 五档的映射：0(亮) -> [0,1]；1(中) -> [2,3]；2(暗) -> [4]
STONE = [(172, 172, 180), (152, 152, 162), (132, 132, 144), (112, 114, 128), (92, 96, 114)]
DIRT = [(166, 122, 78), (148, 106, 64), (128, 88, 52), (106, 70, 44), (84, 54, 38)]


def h(x, y, s=0):
    n = (x * 374761393 + y * 668265263 + s * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def draft_img(rows, pal):
    """把 0/1/2 的三值图画出来。pal 给 3 个颜色时是草稿，给 5 个时是上色。"""
    img = Image.new("RGB", (T, T))
    p = img.load()
    for y in range(T):
        for x in range(T):
            lv = int(rows[y][x])
            if len(pal) == 3:
                p[x, y] = pal[lv]
            else:
                # 三档展开成五档：暗档用第 5 档，中档在两档之间轮换，亮档用第 1 档
                # 用哈希而不是 (x*7+y*3)%2：周期性取色会在整块上铺出规则棋盘，
                # 平铺 3x3 时特别明显（每一格都能看出同一个花纹）
                idx = {0: [0, 1], 1: [2, 3], 2: [4, 4]}[lv][0 if h(x // 2, y // 2, 3) > 0.62 else 1]
                p[x, y] = pal[idx]
    return img


def tile3(img):
    out = Image.new("RGB", (T * 3, T * 3))
    for i in range(3):
        for j in range(3):
            out.paste(img, (i * T, j * T))
    return out


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    names = list(DRAFTS)
    scale = 5
    cell = T * scale
    gap = 12
    w = gap + len(names) * (cell * 3 + gap)
    h = gap * 2 + 20 + cell + gap + cell + gap + cell * 3
    sheet = Image.new("RGB", (w, h), (24, 22, 26))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 14)
        small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 12)
    except Exception:
        font = small = ImageFont.load_default()

    for i, name in enumerate(names):
        x = gap + i * (cell * 3 + gap)
        rows = DRAFTS[name]
        d.text((x, 4), name, font=font, fill=(210, 216, 228))

        # 第一行：三值草稿
        g = draft_img(rows, [(210, 210, 210), (140, 140, 140), (70, 70, 70)])
        big = g.resize((cell, cell), Image.NEAREST)
        sheet.paste(big, (x, 24))

        # 第二行：上色
        c = draft_img(rows, STONE)
        big = c.resize((cell, cell), Image.NEAREST)
        sheet.paste(big, (x, 24 + cell + gap))

        # 第三行：3x3 平铺查接缝
        t = tile3(c).resize((cell * 1, cell * 1), Image.NEAREST)
        sheet.paste(t, (x, 24 + cell * 2 + gap * 2))

    d.text((gap, h - 16), "每列：上=灰阶草稿（只判结构）  中=上色  下=3x3 平铺（查接缝）", font=small, fill=(130, 136, 150))
    out = os.path.join(root, "Logs", "value_study.png")
    sheet.save(out)
    print("已生成", out, sheet.size)


if __name__ == "__main__":
    main()
