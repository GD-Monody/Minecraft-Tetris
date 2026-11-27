#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
图集体检：找出「越界像素」——颜色明显跑到这块方块色系之外的那些点。

为什么要量化：16 像素下，一两个跨色系的亮点就是一块「贴片」，
但人眼一张一张看 54 块很容易漏。这里按 HSV 算：
    色相差 > 28°  且  明度比底色亮 0.30 以上  → 越界
命中数 ≥ 2 就报警（单点可能是刻意的反光）。

用法：
    py -3 tools/audit_tiles.py            # 体检 paper 图集
"""

import colorsys
import os
import sys

import build_paper_atlas as P


def hsv(c):
    r, g, b = c[0] / 255.0, c[1] / 255.0, c[2] / 255.0
    return colorsys.rgb_to_hsv(r, g, b)


def audit(limit_hue=28.0, limit_val=0.30, min_hits=2):
    rows = []
    for kind in P.KINDS:
        name, tint, bright, motif, alpha = kind
        img = P.make(kind)
        px = img.load()
        base = tuple(min(255, int(v * bright)) for v in tint[:3])
        bh, bs, bv = hsv(base)
        hits = []
        for y in range(P.TILE):
            for x in range(P.TILE):
                c = px[x, y]
                if c[3] < 60:
                    continue
                h, s, v = hsv(c)
                dh = abs(h - bh)
                dh = min(dh, 1.0 - dh) * 360.0
                # 两种越界：① 色相跑了；② 色相没跑但饱和度塌了（白色/灰白补丁）。
                # 第二种是之前漏掉的：白点在浅色底上，色相差很小，人眼却一眼看出是补丁。
                hue_off = dh > limit_hue and v - bv > limit_val * 0.5
                sat_gone = bs > 0.14 and s < bs * 0.45 and v > bv + 0.04
                if hue_off or sat_gone:
                    hits.append((x, y, dh, v))
        if len(hits) >= min_hits:
            rows.append((name, len(hits), max(h[0] for h in hits), hits[:4]))
    return rows


if __name__ == "__main__":
    rows = sorted(audit(), key=lambda r: -r[1])
    if not rows:
        print("体检通过：没有越界像素")
        sys.exit(0)
    print("有 %d 块存在越界像素（色相偏 >28° 且明显更亮）：\n" % len(rows))
    for name, n, dh, sample in rows:
        print("  %-14s %2d 个点  最大色相差 %5.1f°  示例 %s" % (name, n, dh, sample))
