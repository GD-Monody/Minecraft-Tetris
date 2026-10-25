# 把 Assets/Art/Blocks 下的单张方块贴图拼成一张图集。
# 每个格子外扩 1 像素（复制边缘像素做 padding），这样 UV 采样在格子边界附近越界时，
# 取到的还是这块方块自己的边缘颜色，不会串到相邻格子的贴图（之前方块边缘出现的红线就是这么来的）。
# 用法：python tools/build_atlas.py
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BLOCKS = ROOT / "Assets" / "Art" / "Blocks"
OUT = ROOT / "Assets" / "Resources" / "atlas.png"
TILE = 16
PAD = 1
CELL = TILE + PAD * 2
COLS = 8

# MC 原版里草方块顶面是一张灰度 tint 遮罩，进游戏后才按生物群系乘颜色。
# 我们不用光照也不分群系，直接把平原草色乘进去，否则顶面看起来就是灰色的石头。
TINT = {
    "grass_block_top": (145, 189, 89),      # #91BD59 平原草色
    "grass_block_side_overlay": (145, 189, 89),
}


def apply_tint(img, tint):
    """把灰度贴图乘上 tint 色。tint 为 None 时原样返回。"""
    if tint is None:
        return img
    arr = np.array(img).astype(np.float32)
    scale = np.array(tint, dtype=np.float32) / 255.0
    arr[:, :, :3] = np.clip(arr[:, :, :3] * scale, 0, 255)
    return Image.fromarray(arr.astype(np.uint8))


def main():
    files = sorted(p for p in BLOCKS.glob("*.png") if p.name != "atlas.png")
    if not files:
        raise SystemExit("没有找到方块贴图")

    rows = (len(files) + COLS - 1) // COLS
    atlas = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))

    mapping = {}
    for i, f in enumerate(files):
        img = Image.open(f).convert("RGBA")
        if img.size != (TILE, TILE):
            img = img.resize((TILE, TILE), Image.NEAREST)
        img = apply_tint(img, TINT.get(f.stem))
        arr = np.array(img)
        arr = np.pad(arr, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="edge")
        padded = Image.fromarray(arr)
        col, row = i % COLS, i // COLS
        atlas.paste(padded, (col * CELL, row * CELL))
        mapping[f.name] = {"col": col, "row": row}

    atlas.save(OUT)
    print("atlas: %s  %dx%d  tiles=%d  grid=%dx%d  cell=%d(pad %d)"
          % (OUT, atlas.width, atlas.height, len(files), COLS, rows, CELL, PAD))
    for name, pos in mapping.items():
        print("  %-32s col=%d row=%d" % (name, pos["col"], pos["row"]))


if __name__ == "__main__":
    main()
