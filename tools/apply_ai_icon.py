"""把选定的 1024 图标铺成全套尺寸 + ico。

Unity 的 PlayerSettings 和 Windows/Android 都用这些文件，文件名和路径保持不变，
Unity 会按原有 .meta（Point 过滤、无压缩）重新导入，不需要动导入设置。

用法：py -3 tools/apply_ai_icon.py <选中的1024图>
"""
import os
import sys
from PIL import Image

SIZES = [1024, 512, 256, 192, 144, 128, 96, 72, 64, 48, 32, 16]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main(src):
    im = Image.open(src).convert("RGB")
    if im.size != (1024, 1024):
        im = im.resize((1024, 1024), Image.LANCZOS)

    targets = [os.path.join(ROOT, "docs", "icon"),
               os.path.join(ROOT, "Assets", "Art", "AppIcon")]

    for d in targets:
        os.makedirs(d, exist_ok=True)
        for s in SIZES:
            out = os.path.join(d, "app_icon_%d.png" % s)
            im.resize((s, s), Image.LANCZOS).save(out)
        # ico 用多尺寸打包，Windows 资源管理器和任务栏会各取所需
        im.save(os.path.join(d, "app_icon.ico"),
                sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
        print("已写入", d)

    print("全套尺寸:", ", ".join(str(s) for s in SIZES), "+ ico")


if __name__ == "__main__":
    main(sys.argv[1])
