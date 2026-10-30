"""从原版 26.2 的 jar 里取火贴图，拼成一张 32 帧动画条。

原版的两张火：
  assets/minecraft/textures/block/fire_0.png   16 x 512，竖着 32 帧，每帧 16x16
  assets/minecraft/textures/block/fire_1.png   同上（另一套火苗形状，两层叠着放才够密）

黑底靠 PNG 的 tRNS 当透明用（tRNS 就是 (0,0,0)），所以这里把纯黑转成 alpha=0，
其余 alpha=255 —— 渲染时走 alpha test，火苗边缘就是原版那种硬像素边。

拼法：两张并排塞进一张 32x512 的图。
  左半列（u 0~0.5）= fire_0，右半列（u 0.5~1）= fire_1，
  竖直方向 32 等分 = 32 帧，材质每帧改一下 UV 的 v 偏移就动起来了。

玩法：python tools/build_fire.py
"""

import os
import shutil
import sys
import zipfile

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAR = os.path.join(
    os.environ.get("APPDATA", ""), ".minecraft", "versions", "26.2", "26.2.jar"
)
SRC = [
    "assets/minecraft/textures/block/fire_0.png",
    "assets/minecraft/textures/block/fire_1.png",
]
OUT = os.path.join(ROOT, "Assets", "Resources", "Fire.png")
FRAMES = 32

# 固定 guid：FireMat.mat 里要引用这张图，写死比让 Unity 现生成一个更好维护
GUID = "7a1c4f8e2b9d4a3f8c6e5b1d0a9f2c34"
META_SRC = os.path.join(ROOT, "Assets", "Resources", "atlas.png.meta")


def main():
    if not os.path.exists(JAR):
        sys.exit("找不到原版 jar：" + JAR)

    with zipfile.ZipFile(JAR) as z:
        sheets = []
        for name in SRC:
            raw = z.read(name)
            tmp = os.path.join(ROOT, "Logs", "_" + name.split("/")[-1])
            with open(tmp, "wb") as f:
                f.write(raw)
            img = Image.open(tmp).convert("RGBA")
            os.remove(tmp)
            if img.size != (16, 16 * FRAMES):
                sys.exit("%s 尺寸不对：%s" % (name, img.size))
            sheets.append(img)

    # 黑底 → 透明
    for img in sheets:
        px = img.load()
        for y in range(img.height):
            for x in range(img.width):
                r, g, b, _ = px[x, y]
                px[x, y] = (r, g, b, 0 if (r + g + b) == 0 else 255)

    out = Image.new("RGBA", (32, 16 * FRAMES))
    for f in range(FRAMES):
        left = sheets[0].crop((0, f * 16, 16, f * 16 + 16))
        right = sheets[1].crop((0, f * 16, 16, f * 16 + 16))
        out.paste(left, (0, f * 16))
        out.paste(right, (16, f * 16))
    out.save(OUT)

    write_meta()
    print("已生成 %s（%dx%d，%d 帧）" % (OUT, out.width, out.height, FRAMES))


def write_meta():
    """照抄图集的导入设置（点采样、无 mipmap、Clamp），只换 guid 和尺寸上限。"""
    if not os.path.exists(META_SRC):
        return
    with open(META_SRC, encoding="utf-8") as f:
        text = f.read()
    lines = text.split("\n")
    out = []
    for line in lines:
        if line.startswith("guid:"):
            out.append("guid: " + GUID)
        elif line.strip().startswith("maxTextureSize:"):
            out.append(line.replace("2048", "512"))
        else:
            out.append(line)
    with open(OUT + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))


if __name__ == "__main__":
    main()
