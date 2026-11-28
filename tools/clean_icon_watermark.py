"""抹掉生成图标右下角的「AI生成 WORKBUDDY」水印。

思路：水印只落在右下角的纯渐变背景上，所以不需要 inpaint 那种猜内容——
把该矩形区域当成一张双线性曲面还原：

    c(x, y) = top(x) + left(y) - corner

top(x) 取矩形上边外侧一行，left(y) 取左边外侧一列，corner 是两者交点。
平滑渐变上这个拟合几乎无误差；再按邻近区域的噪声强度撒回同等噪声，
避免出现一块「过于干净」的补丁。

用法：py -3 tools/clean_icon_watermark.py <输入> <输出>
"""
import sys
import random
from PIL import Image

# 水印区：右下角固定留一块余量（实测约 180x100，取大一点更保险）
MARGIN_X = 0.14        # 从右边量
MARGIN_Y = 0.09        # 从底边量


def clean(src, dst):
    im = Image.open(src).convert("RGB")
    w, h = im.size
    px = im.load()

    x0 = int(w * (1 - MARGIN_X))
    y0 = int(h * (1 - MARGIN_Y))

    # 采样：上边一行、左边一列（都在矩形外，避免取到水印本身）
    top_row = [px[x, y0 - 2] for x in range(x0, w)]
    left_col = [px[x0 - 2, y] for y in range(y0, h)]
    corner = px[x0 - 2, y0 - 2]

    # 邻近区域的噪声强度（用来把「太干净」的补丁打回原样）
    diffs = []
    for y in range(y0 - 40, y0 - 20):
        for x in range(x0 - 40, x0 - 20):
            a, b = px[x, y], px[x + 1, y]
            diffs.append(abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2]))
    noise = (sum(diffs) / len(diffs) / 3.0) if diffs else 1.0
    rng = random.Random(7)

    for y in range(y0, h):
        for x in range(x0, w):
            t = top_row[x - x0]
            l = left_col[y - y0]
            base = [t[i] + l[i] - corner[i] for i in range(3)]
            base = [max(0, min(255, v)) for v in base]
            n = rng.uniform(-noise, noise)
            px[x, y] = tuple(max(0, min(255, int(v + n))) for v in base)

    im.save(dst)
    print("已处理", dst, "（水印区 x>=%d y>=%d）" % (x0, y0))


if __name__ == "__main__":
    clean(sys.argv[1], sys.argv[2])
