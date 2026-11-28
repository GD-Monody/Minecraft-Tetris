"""生成 GitHub 社交预览图（1280x640）与 README 头图。

元素全部来自项目自己的资源：app 图标 + tools/make_logo.py 生成的像素字，
不引用任何外部图片。
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
W, H = 1280, 640
BG_TOP = (16, 20, 28)
BG_BOTTOM = (28, 38, 52)


def gradient_bg():
    img = Image.new("RGB", (W, H), BG_TOP)
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        d.line([(0, y), (W, y)], fill=c)
    return img


def rounded_mask(size, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=radius, fill=255)
    return m


def font(size, bold=False):
    for name in (("msyhbd.ttc" if bold else "msyh.ttc"), "simhei.ttf"):
        p = os.path.join("C:\\Windows\\Fonts", name)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default()


def main():
    img = gradient_bg()

    # 图标：360px，圆角 72，带一点外发光感（叠一层同位置模糊色块）
    icon = Image.open(os.path.join(ROOT, "docs", "icon", "app_icon_512.png")).convert("RGB")
    isz = 360
    icon = icon.resize((isz, isz), Image.LANCZOS)
    mask = rounded_mask((isz, isz), 72)

    glow = Image.new("RGB", (W, H), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.rounded_rectangle([118, 138, 118 + isz + 4, 138 + isz + 4], radius=76, fill=(40, 70, 90))
    from PIL import ImageFilter
    glow = glow.filter(ImageFilter.GaussianBlur(26))
    img = Image.composite(glow, img, Image.new("L", (W, H), 26))

    img.paste(icon, (120, 140), mask)

    # 标题像素字：用项目自己的 title.png（140x16）放大，NEAREST 保持硬边
    title = Image.open(os.path.join(ROOT, "Assets", "Resources", "title.png")).convert("RGBA")
    tw = 560
    th = int(title.height * tw / title.width)
    title = title.resize((tw, th), Image.NEAREST)
    # 反相成浅色（原图是纯白 alpha，浅底主题里会被代码压暗；这里要亮）
    px = title.load()
    for y in range(title.height):
        for x in range(title.width):
            r, g, b, a = px[x, y]
            px[x, y] = (236, 242, 250, a)

    img.paste(title, (560, 200), title)

    d = ImageDraw.Draw(img)
    d.text((562, 320), "分层下落的 3D 方块解谜", font=font(40), fill=(214, 224, 238))
    d.text((562, 386), "沙会塌 · 水会流 · 火会烧 · 整片场地一层层往下收",
           font=font(26), fill=(140, 154, 172))
    d.text((562, 470), "原创素材 · 免费 · 无广告", font=font(24), fill=(110, 190, 150))

    out = os.path.join(ROOT, "docs", "images", "social-preview.png")
    img.save(out)
    print("已生成", out, img.size)


if __name__ == "__main__":
    main()
