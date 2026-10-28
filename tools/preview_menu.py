"""按 TitleScreen / MenuLayout 的同一套数字，把主菜单渲染成 PNG，用来肉眼核对放大后有没有压字、越界。

不是游戏里的真实截屏（GUI 只能在有窗口的环境里跑），但字号、坐标、配色都是从 C# 里抄过来的，
排版是否有问题一看便知。
"""
import math
from PIL import Image, ImageDraw, ImageFont

W, H = 1440, 900
TEXT_SCALE = 1.25
MENU_SCALE = 2.0

FONT = "Assets/Resources/Fonts/LongYinTi.ttf"


def P(v):
    return v * TEXT_SCALE


def Size(v):
    return int(round(v * TEXT_SCALE))


def title_pixel_scale():
    return max(2, round(H * 0.105 / 16))


def title_height():
    return 16 * title_pixel_scale()


def title_top_y():
    return round(H * 0.15)


def compute_menu(n, base_w, base_h, base_gap, band_top, band_bottom, max_width, target):
    unit = n * base_h + (n - 1) * base_gap
    avail = max(1.0, band_bottom - band_top)
    by_h = avail / unit
    by_w = max_width / base_w
    s = max(0.5, min(target, by_h, by_w))
    h, w, gap = base_h * s, base_w * s, base_gap * s
    block = n * h + (n - 1) * gap
    start = max(band_top, band_top + (avail - block) * 0.5)
    return s, w, h, gap, start, block


def load(size):
    return ImageFont.truetype(FONT, size)


def text_center(draw, cx, cy, text, font, fill):
    box = draw.textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    draw.text((cx - tw / 2 - box[0], cy - th / 2 - box[1]), text, font=font, fill=fill)


def glow_blob(size, alpha):
    n = 64
    tile = Image.new("L", (n, n), 0)
    px = tile.load()
    half = n * 0.5
    for y in range(n):
        for x in range(n):
            dx = (x + 0.5 - half) / half
            dy = (y + 0.5 - half) / half
            r = math.hypot(dx, dy)
            a = min(1.0, max(0.0, 1.0 - r))
            a = a * a * (3 - 2 * a)
            px[x, y] = int(round(a * 255))
    tile = tile.resize((max(1, int(size)), max(1, int(size))), Image.BILINEAR)
    out = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    out.putalpha(tile.point(lambda v: int(v * alpha)))
    return out


def draw_trail(img, points):
    """points: [(x, y, k)] k=1 最新 → 0 最老；照 CursorTrail 的公式来（含按弧长补点）。"""
    img = img.convert("RGBA")
    trail = Image.new("RGBA", img.size, (0, 0, 0, 0))
    trail_c = (26, 82, 255)
    hot_c = (148, 204, 255)
    unit = max(16.0, H * 0.030)

    def node(x, y, k):
        if k <= 0:
            return
        blob = glow_blob(unit * 2.8 * (1 - k) + unit * 0.85 * k, pow(k, 1.7) * 0.55)
        c = (
            int(trail_c[0] + (hot_c[0] - trail_c[0]) * k * k),
            int(trail_c[1] + (hot_c[1] - trail_c[1]) * k * k),
            int(trail_c[2] + (hot_c[2] - trail_c[2]) * k * k),
            255,
        )
        colored = Image.new("RGBA", blob.size, c)
        colored.putalpha(blob.getchannel("A"))
        trail.alpha_composite(colored, (int(x - blob.width / 2), int(y - blob.height / 2)))

    if points:
        node(*points[0])
        for i in range(1, len(points)):
            x0, y0, k0 = points[i - 1]
            x1, y1, k1 = points[i]
            dist = math.hypot(x1 - x0, y1 - y0)
            steps = max(1, min(8, int(math.ceil(dist / (unit * 0.45)))))
            for s in range(1, steps + 1):
                t = s / steps
                node(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, k0 + (k1 - k0) * t)

    mx, my = points[-1][0], points[-1][1]
    for size, a in ((unit * 3.4, 0.18), (unit * 1.7, 0.34)):
        blob = glow_blob(size, a)
        colored = Image.new("RGBA", blob.size, trail_c + (255,))
        colored.putalpha(blob.getchannel("A"))
        trail.alpha_composite(colored, (int(mx - blob.width / 2), int(my - blob.height / 2)))

    blob = glow_blob(unit * 0.75, 0.55)
    colored = Image.new("RGBA", blob.size, hot_c + (255,))
    colored.putalpha(blob.getchannel("A"))
    trail.alpha_composite(colored, (int(mx - blob.width / 2), int(my - blob.height / 2)))

    return Image.alpha_composite(img, trail).convert("RGB")


def render(n_items, out_path, trail=True):
    img = Image.new("RGB", (W, H), (0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 标题 logo：title.png 按 6 倍像素放大
    ps = title_pixel_scale()
    title = Image.open("Assets/Resources/title.png").convert("RGBA")
    title = title.resize((title.width * ps, title.height * ps), Image.NEAREST)
    th = title_height()
    img.paste(title, (int((W - title.width) / 2), title_top_y()), title)
    draw = ImageDraw.Draw(img)

    # 副标题
    text_center(draw, W / 2, title_top_y() + th + int(P(2)) + P(44) / 2,
                "Made by Monody", load(Size(30)), (255, 255, 255))

    band_top = title_top_y() + th + P(2) + P(44) + P(20)
    band_bottom = H - P(62) - P(10)
    s, bw, bh, gap, start, block = compute_menu(
        n_items, P(280), P(44), P(10), band_top, band_bottom, W * 0.86, MENU_SCALE)

    labels = ["继续游戏", "开始游戏", "创意工坊", "设置", "退出"][:n_items]
    if n_items == 5:
        labels = ["开始游戏", "创意工坊", "设置", "退出"]

    btn_font = load(max(12, int(round(Size(18) * s))))
    for i, label in enumerate(labels):
        x = (W - bw) / 2
        y = start + i * (bh + gap)
        draw.rectangle([x, y, x + bw, y + bh], fill=(33, 38, 48))
        text_center(draw, W / 2, y + bh / 2, label, btn_font, (255, 255, 255))

    # 底部署名
    text_center(draw, W / 2, H - P(62) + P(28) / 2, "AGENTS辅助完成", load(Size(17)), (217, 217, 217))

    if trail:
        pts = []
        steps = 26
        for i in range(steps):
            t = i / (steps - 1)
            x = 300 + t * 760 + 40 * math.sin(t * 5.2)
            y = 640 - t * 330
            pts.append((x, y, t))
        img = draw_trail(img, pts)

    img.save(out_path)
    print("%s  按钮 %d 个：scale=%.3f  %.0f×%.0f  gap=%.1f  startY=%.1f  bottom=%.1f  字号=%d"
          % (out_path, n_items, s, bw, bh, gap, start, start + block, btn_font.size))
    return s, bw, bh


if __name__ == "__main__":
    render(5, "Logs/menu_preview_5.png")
    render(6, "Logs/menu_preview_6.png")
