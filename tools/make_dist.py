"""把 Build/ 里的 Windows 播放器打成一个可以直接发给朋友、解压即玩的 zip。

为什么不直接右键压缩 Build 目录：
1. Build/ 里还有 boot.config 之类的调试残留、以及上次构建的旧文件，得挑着拷；
2. Windows 自带的「发送到 → 压缩文件夹」用系统代码页存文件名，中文名会乱码；
   这里用 zipfile 显式给非 ASCII 名字打上 UTF-8 标志位（flag 0x800），Win10 解压不乱码；
3. 顺便塞一份使用说明进去，朋友拿到不会一脸问号。

用法：
    python tools/make_dist.py            # 输出 Build/Minecraft-Tetris-v0.1-win64.zip
    python tools/make_dist.py --no-zip   # 只准备文件夹，不压缩（调试用）
"""
import os
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(ROOT, "Build")
NAME = "Minecraft-Tetris-v0.1-win64"
OUT_ZIP = os.path.join(BUILD, NAME + ".zip")

# 播放器本体：exe + 数据目录 + 运行时依赖，少一个都起不来
PLAYER_ITEMS = [
    "MinecraftTetris.exe",
    "MinecraftTetris_Data",
    "MonoBleedingEdge",
    "UnityPlayer.dll",
    "UnityCrashHandler64.exe",
]

README = """\
Minecraft x Tetris 3D —— 我的世界风格 3D 俄罗斯方块（v0.1 试玩版）
================================================================

【怎么开始玩】
1. 把整个压缩包解压出来（别只把 exe 拖出来，它离不开旁边那几个文件和 _Data 文件夹）
2. 双击 MinecraftTetris.exe
3. 如果 Windows 弹「Windows 已保护你的电脑」，点「更多信息」→「仍要运行」。
   这是因为作者没有买数字签名，属于正常提示，不是病毒。

【配置要求】
- Windows 10 / 11，64 位
- 能跑基本 3D 的显卡即可（核显也行）
- 默认窗口 1440×900，可以拖边框改大小

【操作】
  A / D 或 ← / →     左右移动
  W / S 或 ↑ / ↓     前后移动（场地有深度时才用得上）
  空格                硬降
  Shift               软降
  Q / E               面内旋转（逆时针 / 顺时针）
  Z / C               俯视旋转（逆时针 / 顺时针，绕竖直轴）
  V / R               前后翻（往正面 / 往背面）
  鼠标拖拽 / 滚轮     转视角 / 缩放
  Esc                 暂停

  键位都能在「设置 → 按键设置」里改，改完立即生效。

【建议先这么玩】
  第一次进游戏，场地是 10 × 20 × 1（就是普通的平面俄罗斯方块）。
  玩顺手了再去「设置 → 场地尺寸」把 Z 调成 3 或更大，那才是这游戏真正不一样的地方——
  这时候方块要靠 W / S 往深处放，还能用 Z / C 俯视旋转、V / R 前后翻。
  3D 场地里考虑落点的时间更久，建议顺手把「下落速度」调到 0.5 ~ 0.8 倍。

【设置里有什么】
  场地尺寸   X / Y / Z 三轴可调（Z > 1 解锁真正的 3D）
  消除模式   行消 / 层消
  下落速度   0.01 ~ 2.00 倍，1.00 是标准（每 0.8 秒落一格）
  音乐       音量 0 ~ 100%，0 就是关（原版曲库，随机播放）
  基岩进池   默认关。打开后大约每 100 个方块里有 1 个基岩
  结构方块进池 默认关。打开后大约每 100 个方块里有 1 个结构方块
  按键设置   全部键位可改
  创意工坊   自己画模具，启用的模具会和 7 种默认方块一起随机出现

【方块特性（和原版 MC 一样）】
  沙子 / 沙砾 / 混凝土粉末   落定后会自己塌下去
  水                         按体积流动，会积聚、会往低处灌
  岩浆                       同上，碰到水会生成黑曜石或原石
  TNT                        碰到红石块会被点着，闪白 2.4 秒后炸掉周围一圈，还会连锁
  原木                       落定后周围会长出树叶（9 种树种）
  基岩                       消行时钉在原地不动，但自己那一行被消时照样消失
  结构方块                   落定时记住周围一片构造，被消掉时先清空再一格一格搭回来

  TNT 是道具不是砖：它按件数随机出现，而且抽到 TNT 之后下一块必定是红石块。
  把红石块放到 TNT 上下左右前后任意一面挨着，它就点着了。

  基岩默认不出现。想玩的话去「设置 → 基岩进池」打开，之后大约每 100 个方块里会有 1 个基岩。
  基岩掉下来就钉在那儿不动了，它上面那一摞方块也会停在它上面（那一列从此填不满整行）。

【已知问题】
  - v0.1 试玩版：玻璃压碎、立体的方块形状还没做
  - 最高分和设置存在本机注册表里（PlayerPrefs），换电脑不同步
  - 想删干净：删掉这个文件夹即可，注册表里会留一点设置项，不影响系统

【版权说明】
  方块贴图、音效和背景音乐来自《我的世界》（版权归 Mojang / Microsoft），
  界面字体是「方正字迹-龙吟体」（版权归方正字库）。
  本项目是个人学习作品，非商业用途；素材请勿单独提取使用或商用。
  如果权利方认为侵权，请联系作者，会立即删除。

项目主页：https://github.com/GD-Monody/Minecraft-Tetris
有问题或想法，欢迎在 GitHub 提 issue。
"""


def prepare(stage):
    if os.path.isdir(stage):
        shutil.rmtree(stage)
    os.makedirs(stage)

    missing = []
    for item in PLAYER_ITEMS:
        src = os.path.join(BUILD, item)
        if not os.path.exists(src):
            missing.append(item)
            continue
        dst = os.path.join(stage, item)
        if os.path.isdir(src):
            shutil.copytree(src, dst)
        else:
            shutil.copy2(src, dst)

    if missing:
        print("【警告】Build/ 里缺少这些文件，打出来的包可能跑不起来：", ", ".join(missing))

    # 用 UTF-8 + BOM 写，Windows 记事本 / 各种国产编辑器都不会乱码
    with open(os.path.join(stage, "使用说明.txt"), "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write(README)

    total = 0
    for dirpath, _, files in os.walk(stage):
        for name in files:
            total += os.path.getsize(os.path.join(dirpath, name))
    print("已准备目录：%s（%.1f MB）" % (stage, total / 1048576.0))
    return len(missing) == 0


def zip_dir(stage, out):
    if os.path.exists(out):
        os.remove(out)

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        for dirpath, _, files in os.walk(stage):
            for name in files:
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, stage)
                # 压缩包里套一层同名文件夹，解压出来不会散落一地
                arc = os.path.join(NAME, rel).replace("\\", "/")
                info = zipfile.ZipInfo.from_file(full, arc)
                info.compress_type = zipfile.ZIP_DEFLATED
                # 关键：非 ASCII 文件名要打 UTF-8 标志位，否则 Windows 解压出来是乱码
                if any(ord(ch) > 127 for ch in arc):
                    info.flag_bits |= 0x800
                with open(full, "rb") as src, zf.open(info, "w") as dst:
                    shutil.copyfileobj(src, dst, 1 << 20)
                count += 1

    size = os.path.getsize(out)
    print("已打包：%s（%d 个文件，%.1f MB）" % (out, count, size / 1048576.0))
    return out


def main():
    stage = os.path.join(BUILD, NAME)
    ok = prepare(stage)
    if "--no-zip" in sys.argv:
        return 0 if ok else 1
    zip_dir(stage, OUT_ZIP)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
