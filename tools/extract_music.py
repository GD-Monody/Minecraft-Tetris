# -*- coding: utf-8 -*-
"""从本机 Minecraft 的资源里提取背景音乐到 Assets/Resources/Music。

做法和 extract_sound.py 一样：新版 MC 的 ogg 按 sha1 存在
.minecraft/assets/objects/<前两位>/<sha1>，索引在 assets/indexes/*.json。

这里只取一小部分曲子（原版一共 98 首、339 MB，全放进来包会大得没法分享）。
挑的是 C418 那批最经典的常速曲 + 两首菜单曲，总共约 28 MB。

用法：
    python tools/extract_music.py            # 提取并打印清单
    python tools/extract_music.py --list     # 只列原始条目，不复制
"""

import glob
import json
import os
import shutil
import sys

ASSETS = os.path.join(os.environ.get("APPDATA", ""), ".minecraft", "assets")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Music")

# (MC 里的路径, 输出文件名)。输出名一律 ASCII：Resources.Load 的路径带非 ASCII
# 在打包 / 跨平台时容易出编码问题（字体那次已经踩过一次）。
TRACKS = [
    # --- 游戏内的曲库（标题里点开始之后随机放） ---
    ("game/sweden", "game_sweden"),
    ("game/minecraft", "game_minecraft"),
    ("game/calm1", "game_calm1"),
    ("game/calm2", "game_calm2"),
    ("game/calm3", "game_calm3"),
    ("game/hal1", "game_hal1"),
    ("game/hal2", "game_hal2"),
    ("game/hal3", "game_hal3"),
    ("game/haggstrom", "game_haggstrom"),
    ("game/piano1", "game_piano1"),
    ("game/piano2", "game_piano2"),
    ("game/piano3", "game_piano3"),
    ("game/nuance1", "game_nuance1"),
    ("game/nuance2", "game_nuance2"),
    # --- 标题界面 ---
    ("menu/menu1", "menu_menu1"),
    ("menu/moog_city_2", "menu_moog_city_2"),
]


def load_objects():
    """把索引合并成一张「路径 -> 记录」。同一路径以先读到的（较新的索引）为准。"""
    files = glob.glob(os.path.join(ASSETS, "indexes", "*.json"))
    files.sort(key=lambda p: os.path.getmtime(p), reverse=True)
    merged = {}
    for path in files:
        try:
            with open(path, encoding="utf-8") as f:
                objects = json.load(f).get("objects", {})
        except Exception as e:
            print("跳过 %s: %s" % (path, e))
            continue
        for k, v in objects.items():
            merged.setdefault(k, v)
    return merged


def main():
    objects = load_objects()
    if not objects:
        print("没读到索引，检查 %s 是否存在" % ASSETS)
        return 1

    if "--list" in sys.argv:
        names = sorted(k for k in objects if k.startswith("minecraft/sounds/music/"))
        print("原版音乐条目 %d 个，共 %.1f MB" % (
            len(names), sum(objects[k]["size"] for k in names) / 1e6))
        for k in names:
            print("  %-50s %5.2f MB" % (k.replace("minecraft/sounds/music/", ""),
                                        objects[k]["size"] / 1e6))
        return 0

    os.makedirs(OUT_DIR, exist_ok=True)
    total = 0
    missing = []
    for mc_path, name in TRACKS:
        key = "minecraft/sounds/music/%s.ogg" % mc_path
        rec = objects.get(key)
        if not rec:
            missing.append(mc_path)
            print("缺条目: %s" % mc_path)
            continue

        src = os.path.join(ASSETS, "objects", rec["hash"][:2], rec["hash"])
        if not os.path.exists(src):
            missing.append(mc_path)
            print("缺文件（启动器里补下载一次资源）: %s" % mc_path)
            continue

        dst = os.path.join(OUT_DIR, name + ".ogg")
        shutil.copyfile(src, dst)

        with open(dst, "rb") as f:
            magic = f.read(4)
        if magic != b"OggS":
            print("警告：%s 不是 ogg（头部 %r）" % (dst, magic))

        size = os.path.getsize(dst)
        total += size
        print("  %-24s <- %-22s %5.2f MB" % (name, mc_path, size / 1e6))

    print("\n共写出 %d 首，%.2f MB" % (len(TRACKS) - len(missing), total / 1e6))
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
