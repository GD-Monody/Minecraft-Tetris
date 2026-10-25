# 从 Minecraft 客户端的 unifont 点阵字体里取字形，拼出标题图（白色像素 + 透明底）。
# 用法：python tools/build_title.py
import json
import os
import zipfile

from PIL import Image

TITLE = "MINECRAFT X TETRIS"
MC_INDEX = "26.json"

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "title.png")
MC_ASSETS = os.path.join(os.environ.get("APPDATA", ""), ".minecraft", "assets")


def find_hex():
    index_path = os.path.join(MC_ASSETS, "indexes", MC_INDEX)
    with open(index_path, encoding="utf-8") as f:
        index = json.load(f)
    digest = index["objects"]["minecraft/font/unifont.zip"]["hash"]
    zip_path = os.path.join(MC_ASSETS, "objects", digest[:2], digest)
    with zipfile.ZipFile(zip_path) as z:
        name = next(n for n in z.namelist() if n.endswith(".hex"))
        return z.read(name).decode("utf-8")


def parse_glyphs(hex_text, codes):
    """unifont hex：半角字符每行 2 个 hex（8 宽），全角每行 4 个（16 宽），共 16 行。"""
    glyphs = {}
    for line in hex_text.splitlines():
        if ":" not in line:
            continue
        code_str, bits = line.split(":", 1)
        code = int(code_str, 16)
        if code not in codes:
            continue
        bits = bits.strip()
        if len(bits) == 64:
            width, step = 16, 4
        elif len(bits) == 32:
            width, step = 8, 2
        else:
            continue
        rows = []
        for i in range(16):
            row = int(bits[i * step:(i + 1) * step], 16)
            rows.append([(row >> (width - 1 - j)) & 1 for j in range(width)])
        glyphs[code] = (width, rows)
    return glyphs


def glyph_width(width, rows):
    last = -1
    for row in rows:
        for x in range(width - 1, -1, -1):
            if row[x]:
                last = max(last, x)
                break
    return max(width // 2, last + 1)


def build():
    codes = {ord(c) for c in TITLE if c != " "}
    glyphs = parse_glyphs(find_hex(), codes)

    gap = 1
    space_w = 4
    widths = []
    total = 0
    for c in TITLE:
        if c == " ":
            widths.append(space_w)
            total += space_w + gap
        else:
            w, rows = glyphs[ord(c)]
            gw = glyph_width(w, rows)
            widths.append(gw)
            total += gw + gap
    total -= gap

    img = Image.new("RGBA", (total, 16), (0, 0, 0, 0))
    pixels = img.load()
    meta = []
    x = 0
    for i, c in enumerate(TITLE):
        meta.append({"x": x, "w": widths[i], "space": c == " "})
        if c != " ":
            width, rows = glyphs[ord(c)]
            for y in range(16):
                for gx in range(min(width, widths[i])):
                    if rows[y][gx]:
                        pixels[x + gx, y] = (255, 255, 255, 255)
        x += widths[i] + gap

    img.save(OUT)
    meta_path = os.path.join(ROOT, "Assets", "Resources", "title_meta.json")
    with open(meta_path, "w", encoding="utf-8") as f:
        json.dump({"chars": meta}, f)
    print("meta: %s  %d chars" % (meta_path, len(meta)))
    print("title: %s  %dx%d" % (OUT, img.width, img.height))


if __name__ == "__main__":
    build()
