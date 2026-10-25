# 把 Assets/Art/Blocks 下的单张方块贴图拼成一张图集
# 8 列，每格 16×16，行数按贴图数量自动取整
# 用法：python tools/build_atlas.py
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BLOCKS = ROOT / "Assets" / "Art" / "Blocks"
OUT = ROOT / "Assets" / "Resources" / "atlas.png"
TILE = 16
COLS = 8


def main():
    files = sorted(p for p in BLOCKS.glob("*.png") if p.name != "atlas.png")
    if not files:
        raise SystemExit("没有找到方块贴图")

    rows = (len(files) + COLS - 1) // COLS
    atlas = Image.new("RGBA", (COLS * TILE, rows * TILE), (0, 0, 0, 0))

    mapping = {}
    for i, f in enumerate(files):
        img = Image.open(f).convert("RGBA")
        if img.size != (TILE, TILE):
            img = img.resize((TILE, TILE), Image.NEAREST)
        col, row = i % COLS, i // COLS
        atlas.paste(img, (col * TILE, row * TILE))
        mapping[f.name] = {"col": col, "row": row}

    atlas.save(OUT)
    print("atlas: %s  %dx%d  tiles=%d  grid=%dx%d" % (OUT, atlas.width, atlas.height, len(files), COLS, rows))
    for name, pos in mapping.items():
        print("  %-32s col=%d row=%d" % (name, pos["col"], pos["row"]))


if __name__ == "__main__":
    main()
