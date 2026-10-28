# 把扫描出来的完整方块贴图拼成图集，并生成 C# 数据表。
# 用法：先跑 tools/scan_full_blocks.py，再跑 python tools/build_full_atlas.py
import io
import json
import os
import zipfile

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAR = os.path.expandvars(r'%APPDATA%\.minecraft\versions\26.2\26.2.jar')
BLOCKS_JSON = os.path.join(ROOT, "tools", "full_blocks.json")
ATLAS_OUT = os.path.join(ROOT, "Assets", "Resources", "atlas.png")
DATA_OUT = os.path.join(ROOT, "Assets", "Scripts", "Core", "BlockCatalogData.cs")

COLS = 20
PAD = 1
CELL = 18
FACES = ("up", "down", "north", "south", "east", "west")


LOCAL_TILES = {
    # 自己做的贴图：南瓜顶面去掉梗，用来当南瓜的底面（原版 down 也写的是 pumpkin_top，会露出梗）
    "pumpkin_top_nostem": os.path.join(ROOT, "tools", "textures", "pumpkin_top_nostem.png"),
}


def main():
    with open(BLOCKS_JSON, encoding="utf-8") as f:
        blocks = json.load(f)

    z = zipfile.ZipFile(JAR)
    names = set(z.namelist())

    # 去重后的贴图，按方块出现顺序排；顺序固定，生成的数据表才能稳定
    tiles = []
    tile_index = {}
    for b in blocks:
        for face in FACES:
            png = b["faces"].get(face)
            if not png:
                continue
            if png not in tile_index:
                tile_index[png] = len(tiles)
                tiles.append(png)

    # 需要染色的贴图：草方块顶面是灰度图，游戏里靠 biome 上色（平原约 #91BD59）。
    # 我们不做 biome，直接乘一个固定的草色，否则草方块顶面会是灰的。
    GRASS_TINT = (0.57, 0.74, 0.35)
    # 水同理：water_still 是灰度图，游戏里靠 biome 水色（约 #3F76E4）染色。
    # 不染的话水流出来是灰白的，看着不像水。岩浆是彩色图，不能染。
    WATER_TINT = (0.25, 0.46, 0.89)
    tint_pngs = set()
    for b in blocks:
        for face in b.get("tint") or []:
            png = b["faces"].get(face)
            if png:
                tint_pngs.add(png)
    if tint_pngs:
        print("tint: %s" % ", ".join(sorted(tint_pngs)))

    # 树叶：原版贴图是灰度图，靠 biome 的 foliage 色上色（和草方块顶面一个道理）。
    # 不染的话树叶会是灰白的。樱花和苍白橡树的贴图本身带色，不在这个表里。
    # 颜色取原版平原 / 各树种的近似值。
    FOLIAGE = (0.47, 0.67, 0.18)      # 橡树 / 丛林 / 金合欢 / 深色橡树 / 红树
    LEAF_TINT = {
        "oak_leaves": FOLIAGE,
        "jungle_leaves": FOLIAGE,
        "acacia_leaves": FOLIAGE,
        "dark_oak_leaves": FOLIAGE,
        "mangrove_leaves": FOLIAGE,
        "spruce_leaves": (0.38, 0.60, 0.38),
        "birch_leaves": (0.50, 0.65, 0.33),
    }

    rows = (len(tiles) + COLS - 1) // COLS
    atlas = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))

    for i, png in enumerate(tiles):
        path = "assets/minecraft/textures/block/%s.png" % png
        if png in LOCAL_TILES:
            im = Image.open(LOCAL_TILES[png]).convert("RGBA")
        elif path in names:
            im = Image.open(io.BytesIO(z.read(path))).convert("RGBA")
        else:
            continue
        if im.size != (16, 16):
            # 动画贴图是 16 x (16*帧数) 的竖排帧（命令方块、海晶石、绯红菌柄这些）。
            # 直接把整张压成 16x16 会把所有帧叠在一起，看起来就是「图案重复好几份」。
            # 所以只取第一帧。
            if im.width == 16 and im.height % 16 == 0:
                im = im.crop((0, 0, 16, 16))
            else:
                im = im.resize((16, 16), Image.NEAREST)
        arr = np.array(im)
        if png in tint_pngs:
            f = arr.astype(np.float32)
            f[:, :, 0] *= GRASS_TINT[0]
            f[:, :, 1] *= GRASS_TINT[1]
            f[:, :, 2] *= GRASS_TINT[2]
            arr = np.clip(f, 0, 255).astype(np.uint8)
        elif png.startswith("water_"):
            f = arr.astype(np.float32)
            f[:, :, 0] *= WATER_TINT[0]
            f[:, :, 1] *= WATER_TINT[1]
            f[:, :, 2] *= WATER_TINT[2]
            arr = np.clip(f, 0, 255).astype(np.uint8)
        if png in LEAF_TINT:
            t = LEAF_TINT[png]
            f = arr.astype(np.float32)
            f[:, :, 0] *= t[0]
            f[:, :, 1] *= t[1]
            f[:, :, 2] *= t[2]
            arr = np.clip(f, 0, 255).astype(np.uint8)
        arr = np.pad(arr, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="edge")
        atlas.paste(Image.fromarray(arr), ((i % COLS) * CELL, (i // COLS) * CELL))

    atlas.save(ATLAS_OUT)
    print("atlas: %s  %dx%d  tiles=%d  blocks=%d" % (ATLAS_OUT, atlas.width, atlas.height, len(tiles), len(blocks)))

    lines = []
    lines.append("// 由 tools/build_full_atlas.py 自动生成，不要手改。")
    lines.append("namespace MinecraftTetris")
    lines.append("{")
    lines.append("    public static class BlockCatalogData")
    lines.append("    {")
    lines.append("        public const int AtlasColumns = %d;" % COLS)
    lines.append("        public const int AtlasRows = %d;" % rows)
    lines.append("        public const int TilePadding = %d;" % PAD)
    lines.append("        public const int TilePixels = 16;")
    lines.append("        public const int CellPixels = %d;" % CELL)
    lines.append("")

    lines.append("        public static readonly string[] Names =")
    lines.append("        {")
    for b in blocks:
        lines.append('            "%s",' % b["name"])
    lines.append("        };")
    lines.append("")

    lines.append("        public static readonly string[] NamesZh =")
    lines.append("        {")
    for b in blocks:
        lines.append('            "%s",' % b.get("zh", b["name"]))
    lines.append("        };")
    lines.append("")

    lines.append("        // 每个方块六个面：up, down, north, south, east, west 对应的图集格子编号")
    lines.append("        public static readonly int[] FaceTiles =")
    lines.append("        {")
    for b in blocks:
        faces = []
        fallback = None
        for face in FACES:
            png = b["faces"].get(face)
            if png is not None:
                idx = tile_index.get(png)
                if fallback is None:
                    fallback = idx
                faces.append(idx)
            else:
                faces.append(None)
        if fallback is None:
            fallback = 0
        faces = [fallback if v is None else v for v in faces]
        lines.append("            %s," % ", ".join(str(v) for v in faces))
    lines.append("        };")
    lines.append("    }")
    lines.append("}")

    with open(DATA_OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("data: %s" % DATA_OUT)


if __name__ == "__main__":
    main()
