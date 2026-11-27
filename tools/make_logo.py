#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 SUBSTRATA 的像素标题图（替代 title.png）。

规格跟原来那张一致：140x16、白像素 + 透明底。游戏里 TitleScreen 会按屏幕宽度
自己缩放，而且亮色主题下会把整张图压成深灰，所以这里只需要「纯白 + 透明」，
不要做成彩色的。

用法： py -3 tools/make_logo.py [输出路径]
默认输出 Logs/title_substrata.png（先看效果，没问题再覆盖 Assets/Resources/title.png）。
"""

import sys
from PIL import Image

# 5x7 点阵字体，只收录 SUBSTRATA 用到的字母。1 = 亮像素
FONT = {
    'S': ["01110", "10001", "10000", "01110", "00001", "10001", "01110"],
    'U': ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    'B': ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    'T': ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    'R': ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    'A': ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
}

SCALE = 2          # 每个点放大成 2x2，正好 14px 高
GAP = 4            # 字母间距
W, H = 140, 16


def render(text):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = img.load()

    letters = [FONT[c] for c in text]
    cell_w = 5 * SCALE
    total = len(letters) * cell_w + (len(letters) - 1) * GAP
    x0 = (W - total) // 2
    y0 = (H - 7 * SCALE) // 2

    for idx, glyph in enumerate(letters):
        gx = x0 + idx * (cell_w + GAP)
        for row, line in enumerate(glyph):
            for col, ch in enumerate(line):
                if ch != '1':
                    continue
                for dy in range(SCALE):
                    for dx in range(SCALE):
                        px[gx + col * SCALE + dx, y0 + row * SCALE + dy] = (255, 255, 255, 255)
    return img


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "Logs/title_substrata.png"
    img = render("SUBSTRATA")
    img.save(out)
    print("已生成", out, img.size)
