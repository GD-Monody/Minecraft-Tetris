"""抹掉生成图标右下角的「AI生成 WORKBUDDY」水印（v2：借纹理）。

v1 用纯双线性插值还原颜色，结果是一块「太干净」的补丁——原图有 pixel-grain
纹理，补丁里没有，两种纹理交界处会出现网格痕迹。

v2 改成「借纹理、不借颜色」：
    结果 = 上方源区域的像素 - 源区域的低频 + 目标区域的低频
低频用大半径模糊取（源）和双线性拟合场取（目标，因为目标区被水印污染了）。
这样纹理是原图自己的，颜色由渐变场决定，看不出补丁。

用法：py -3 tools/clean_icon_watermark_v2.py <输入> <输出>
"""
import sys
from PIL import Image, ImageFilter

MARGIN_X = 0.14
MARGIN_Y = 0.09
SHIFT_UP = 260          # 源区域向上平移的像素数（保证落在纯背景、无水印）
BLUR_R = 40             # 求低频用的模糊半径


def low_freq(im, radius=BLUR_R):
    return im.filter(ImageFilter.GaussianBlur(radius))


def clean(src, dst):
    im = Image.open(src).convert("RGB")
    w, h = im.size
    x0 = int(w * (1 - MARGIN_X))
    y0 = int(h * (1 - MARGIN_Y))

    px = im.load()
    # 目标区域的低频场：用地道的双线性拟合（目标区像素被水印污染，不能直接模糊）
    top_row = [px[x, y0 - 2] for x in range(x0, w)]
    left_col = [px[x0 - 2, y] for y in range(y0, h)]
    corner = px[x0 - 2, y0 - 2]
    target_low = {}
    for y in range(y0, h):
        for x in range(x0, w):
            t = top_row[x - x0]
            l = left_col[y - y0]
            target_low[(x, y)] = [t[i] + l[i] - corner[i] for i in range(3)]

    # 源区域的低频：整图模糊后取（纹理会被抹掉，正是我们要的「低频」）
    src_im = im.crop((0, y0 - SHIFT_UP, w, y0 - SHIFT_UP + (h - y0)))
    src_low = low_freq(src_im).load()
    src_px = src_im.load()

    for y in range(y0, h):
        sy = y - y0
        for x in range(x0, w):
            sx = x
            p = src_px[sx, sy]
            lo = src_low[sx, sy]
            tl = target_low[(x, y)]
            px[x, y] = tuple(
                max(0, min(255, int(p[i] - lo[i] + tl[i]))) for i in range(3)
            )

    im.save(dst)
    print("已处理", dst)


if __name__ == "__main__":
    clean(sys.argv[1], sys.argv[2])
