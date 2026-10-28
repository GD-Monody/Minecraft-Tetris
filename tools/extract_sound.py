# -*- coding: utf-8 -*-
"""从本机 Minecraft 的声音资源里提取一个 ogg 到 Assets/Resources/Sfx。

新版 MC 的 ogg 不在版本 jar 里，而是按 sha1 存在 .minecraft/assets/objects/<前两位>/<sha1>，
由 assets/indexes/*.json 记录「资源路径 -> sha1」。所以这里先查索引拿 sha1，再去 objects 取。

用法：
    python tools/extract_sound.py random/fizz fizz
    python tools/extract_sound.py block/stone/break stone_break

第一个参数是 MC 资源库里的路径（assets/minecraft/sounds/ 之后那一段），
第二个参数是输出文件名（不带 .ogg）。
"""

import glob
import json
import os
import shutil
import sys

ASSETS = os.path.join(os.environ.get("APPDATA", ""), ".minecraft", "assets")
OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "Assets", "Resources", "Sfx")


def load_indexes():
    """把 asset 索引读出来，新的在前（同一路径以先读到的为准）。"""
    files = glob.glob(os.path.join(ASSETS, "indexes", "*.json"))
    files.sort(key=lambda p: os.path.getmtime(p), reverse=True)
    for path in files:
        try:
            with open(path, encoding="utf-8") as f:
                yield path, json.load(f).get("objects", {})
        except Exception as e:
            print("跳过 %s: %s" % (path, e))


def find_hash(sound_path):
    key = "minecraft/sounds/%s.ogg" % sound_path
    for path, objects in load_indexes():
        if key in objects:
            return path, objects[key]["hash"]
    return None, None


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    sound_path = sys.argv[1].replace("\\", "/").strip("/")
    name = sys.argv[2]

    index, digest = find_hash(sound_path)
    if not digest:
        print("索引里没有 minecraft/sounds/%s.ogg" % sound_path)
        return 1

    src = os.path.join(ASSETS, "objects", digest[:2], digest)
    if not os.path.exists(src):
        print("objects 里缺这个文件，可能需要在启动器里完整下载一次资源: %s" % src)
        return 1

    os.makedirs(OUT_DIR, exist_ok=True)
    dst = os.path.join(OUT_DIR, name + ".ogg")
    shutil.copyfile(src, dst)

    with open(dst, "rb") as f:
        magic = f.read(4)
    if magic != b"OggS":
        print("警告：%s 不是 ogg（头部 %r），可能取错了" % (dst, magic))

    print("来源索引: %s" % os.path.basename(index))
    print("sha1: %s" % digest)
    print("写出: %s (%d 字节)" % (dst, os.path.getsize(dst)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
