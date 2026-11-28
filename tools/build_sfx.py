"""程序合成 Substrata 的全部音效（放置音 + 界面音）。

为什么合成而不是找素材：
- 281 个放置音如果去 CC0 库里一个个找，光对口味的筛选就要很久，而且风格参差；
- 方块撞击音本来就是「噪声爆发 + 短促共鸣」，是合成器最擅长的东西；
- 固定种子可复现，等于「音频版的原创证明」——和方块贴图走同一条路子。

合成三层：
    瞬态：1~3ms 的极短脉冲，负责「击打感」
    主体：带通噪声（材质越硬、频带越高） + 低频共鸣（材质越重、频率越低）
    尾巴：指数衰减，石/木 100ms 级，金属/晶体 400ms 级

用法：
    py -3 tools/build_sfx.py            # 生成全部
    py -3 tools/build_sfx.py --ui-only  # 只重做界面音
"""
import math
import os
import subprocess
import sys
import wave
import zlib

import numpy as np

# 放置音最终采用钢琴风格（见 tools/sfx_piano.py 的对比实验）。直接复用它的合成函数，
# 避免两套参数各改一遍走样。
import sys as _sys
_sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sfx_piano import make as piano_make, NOTES as PIANO_NOTES, piano as piano_note

# 放置音最终采用钢琴风格（见 tools/sfx_piano.py 的对比实验）。直接复用它的合成函数，
# 避免两套参数各改一遍走样。
import sys as _sys
_sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sfx_piano import make as piano_make, NOTES as PIANO_NOTES, piano as piano_note

SR = 44100
FF = r"C:\Users\lenovo\AppData\Local\Programs\Python\Python314\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v9.0.2.exe"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Sfx")
TMP = os.path.join(ROOT, "Logs", "sfx_tmp")


# ---------- 基础件 ----------

def noise(n, seed):
    return np.random.default_rng(seed).normal(0.0, 1.0, n)


def lowpass(x, fc, sr=SR):
    """一阶 IIR 低通。够用，且不需要 scipy。"""
    a = 1.0 - math.exp(-2.0 * math.pi * fc / sr)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x, fc, sr=SR):
    a = math.exp(-2.0 * math.pi * fc / sr)
    y = np.empty_like(x)
    prev_x = 0.0
    prev_y = 0.0
    for i in range(len(x)):
        prev_y = a * (prev_y + x[i] - prev_x)
        prev_x = x[i]
        y[i] = prev_y
    return y


def bandpass(x, lo, hi, sr=SR):
    return lowpass(highpass(x, lo, sr), hi, sr)


def env(n, attack=0.002, decay=0.15, sr=SR):
    """攻击 + 指数衰减。attack 用线性，衰减用 exp，符合打击音的直觉。"""
    t = np.arange(n) / sr
    a = np.clip(t / max(attack, 1e-6), 0.0, 1.0)
    d = np.exp(-t / max(decay, 1e-6))
    return a * d


def tone(freq, n, decay, sr=SR, phase=0.0):
    t = np.arange(n) / sr
    return np.sin(2 * math.pi * freq * t + phase) * np.exp(-t / max(decay, 1e-6))


def norm(x, peak=0.62):
    m = float(np.max(np.abs(x))) if len(x) else 0.0
    return x if m < 1e-9 else x * (peak / m)


def secs(s):
    return int(SR * s)


def seed_of(text):
    """确定性的种子。不能用内置 hash()：Python 的字符串哈希每个进程都加盐，
    同一个名字两次运行会得到不同的音色，等于放弃了「可复现」这个前提。"""
    return zlib.crc32(text.encode("utf-8")) % 100000


# ---------- 材质预设 ----------

def hit_body(n, seed, lo, hi, body_freq, decay, body_mix=0.55, attack=0.002):
    """通用打击：带通噪声 + 低频体。"""
    bp = bandpass(noise(n, seed), lo, hi)
    body = tone(body_freq, n, decay * 1.6)
    e = env(n, attack, decay)
    return norm((bp * 0.8 + body * body_mix) * e)


def s_stone(n, seed):
    rng = np.random.default_rng(seed)
    f = 150 * rng.uniform(0.9, 1.1)
    x = hit_body(n, seed, 700, 3200, f, 0.075, 0.6)
    # 一点点高频碎屑，石头才"脆"
    x += bandpass(noise(n, seed + 7), 3000, 7000) * env(n, 0.001, 0.02) * 0.35
    return norm(x)


def s_wood(n, seed):
    rng = np.random.default_rng(seed)
    f = 105 * rng.uniform(0.88, 1.12)
    x = hit_body(n, seed, 250, 1100, f, 0.11, 0.85, attack=0.003)
    # 木头的"咔"：更短的第二次瞬态
    x += bandpass(noise(n, seed + 11), 900, 2500) * env(n, 0.001, 0.03) * 0.25
    return norm(x)


def s_gravel(n, seed):
    """砾石：一堆小颗粒，所以是若干个错开的短噪声脉冲。"""
    rng = np.random.default_rng(seed)
    x = np.zeros(n)
    for k in range(7):
        start = int(rng.uniform(0, n * 0.35))
        ln = secs(0.012)
        if start + ln >= n:
            continue
        g = bandpass(noise(ln, seed + k * 13), 1200, 5000) * env(ln, 0.001, 0.008)
        x[start:start + ln] += g * rng.uniform(0.5, 1.0)
    x += hit_body(n, seed, 400, 1800, 120 * rng.uniform(0.9, 1.1), 0.05, 0.4)
    return norm(x)


def s_grass(n, seed):
    """草/叶：几乎只有高频噪声，轻、短、没有共鸣。"""
    x = bandpass(noise(n, seed), 2600, 8000) * env(n, 0.001, 0.05)
    x += bandpass(noise(n, seed + 5), 1200, 2600) * env(n, 0.002, 0.03) * 0.4
    return norm(x, 0.5)


def s_sand(n, seed):
    """沙：宽而软的噪声，没有明显音高。"""
    x = bandpass(noise(n, seed), 1500, 6000) * env(n, 0.004, 0.09)
    x += bandpass(noise(n, seed + 3), 600, 1500) * env(n, 0.004, 0.06) * 0.5
    return norm(x, 0.55)


def s_snow(n, seed):
    """雪：更闷，低通压掉大部分高频。"""
    x = lowpass(bandpass(noise(n, seed), 400, 3000), 2200) * env(n, 0.005, 0.08)
    return norm(x, 0.5)


def s_glass(n, seed):
    """玻璃：高频正弦簇，衰减比石头长一点，有"叮"的味道。"""
    rng = np.random.default_rng(seed)
    base = 2400 * rng.uniform(0.92, 1.08)
    x = np.zeros(n)
    for k, (mul, amp) in enumerate(((1.0, 1.0), (1.5, 0.5), (2.35, 0.3), (3.1, 0.18))):
        x += tone(base * mul, n, 0.065 / (k + 1), phase=rng.uniform(0, 6.28)) * amp
    x += bandpass(noise(n, seed + 9), 4000, 9000) * env(n, 0.001, 0.02) * 0.5
    return norm(x)


def s_metal(n, seed):
    """金属：非谐波泛音（不成整数比），衰减长，才"响"得起来。"""
    rng = np.random.default_rng(seed)
    base = 380 * rng.uniform(0.95, 1.05)
    x = np.zeros(n)
    for mul, amp, dec in ((1.0, 1.0, 0.16), (2.76, 0.7, 0.12), (5.4, 0.45, 0.09), (8.9, 0.25, 0.06)):
        x += tone(base * mul, n, dec, phase=rng.uniform(0, 6.28)) * amp
    x += bandpass(noise(n, seed + 4), 3000, 8000) * env(n, 0.001, 0.015) * 0.4
    return norm(x)


def s_crystal(n, seed):
    """晶体：明亮钟声，整数谐波 + 长衰减。"""
    rng = np.random.default_rng(seed)
    base = 880 * rng.uniform(0.95, 1.05)
    x = np.zeros(n)
    for k, amp in enumerate((1.0, 0.6, 0.35, 0.2, 0.1)):
        x += tone(base * (k + 1), n, 0.14 / (k + 1), phase=rng.uniform(0, 6.28)) * amp
    return norm(x)


def s_water(n, seed):
    """水：低频"哗"一声 + 几个气泡。原来借用玻璃的高频"叮"，完全不像倒水。"""
    rng = np.random.default_rng(seed)
    x = bandpass(noise(n, seed), 300, 2600) * env(n, 0.015, 0.15)
    for k in range(4):
        start = int(rng.uniform(0.02, 0.13) * SR)
        ln = secs(0.03)
        if start + ln < n:
            bub = bandpass(noise(ln, seed + k * 7), 900, 3200) * env(ln, 0.003, 0.018)
            x[start:start + ln] += bub * 0.6
    return norm(x, 0.6)


def s_lava(n, seed):
    """岩浆：粘稠的低频咕嘟，比水更闷更长，带一点 70Hz 的体。"""
    x = lowpass(bandpass(noise(n, seed), 120, 1200), 900) * env(n, 0.02, 0.26)
    x += tone(70, n, 0.22) * 0.7
    return norm(x, 0.75)


def s_water(n, seed):
    """水：低频"哗"一声 + 几个气泡。原来借用玻璃的高频"叮"，完全不像倒水。"""
    rng = np.random.default_rng(seed)
    x = bandpass(noise(n, seed), 300, 2600) * env(n, 0.015, 0.15)
    for k in range(4):
        start = int(rng.uniform(0.02, 0.13) * SR)
        ln = secs(0.03)
        if start + ln < n:
            bub = bandpass(noise(ln, seed + k * 7), 900, 3200) * env(ln, 0.003, 0.018)
            x[start:start + ln] += bub * 0.6
    return norm(x, 0.6)


def s_lava(n, seed):
    """岩浆：粘稠的低频咕嘟，比水更闷更长，带一点 70Hz 的体。"""
    x = lowpass(bandpass(noise(n, seed), 120, 1200), 900) * env(n, 0.02, 0.26)
    x += tone(70, n, 0.22) * 0.7
    return norm(x, 0.75)


# 组名 -> (生成函数, 时长秒)
PLACE = {
    "stone":   (s_stone, 0.30),
    "wood":    (s_wood, 0.32),
    "gravel":  (s_gravel, 0.34),
    "grass":   (s_grass, 0.26),
    "sand":    (s_sand, 0.30),
    "snow":    (s_snow, 0.28),
    "glass":   (s_glass, 0.28),
    "metal":   (s_metal, 0.32),
    "crystal": (s_crystal, 0.30),
    "water":   (s_water, 0.30),
    "lava":    (s_lava, 0.45),
}


# ---------- 界面音 ----------

# ---------- 界面音：全部用钢琴黑键（五声） ----------

# 黑键频率表。选黑键而不是白键，是因为黑键本身就是五声音阶
# （C# D# F# G# A#），任意两个撞在一起都不刺耳；
# 而放置音走的是白键 do re mi，两套叠起来也不会打起来。
BK = {
    "C#1": 34.65, "D#1": 38.89, "F#1": 46.25, "G#1": 51.91, "A#1": 58.27,
    "C#2": 69.30, "D#2": 77.78, "F#2": 92.50, "G#2": 103.83, "A#2": 116.54,
    "C#3": 138.59, "D#3": 155.56, "F#3": 185.00, "G#3": 207.65, "A#3": 233.08,
    "C#4": 277.18, "D#4": 311.13, "F#4": 369.99, "G#4": 415.30, "A#4": 466.16,
    "C#5": 554.37, "D#5": 622.25, "F#5": 739.99, "G#5": 830.61, "A#5": 932.33,
    "C#6": 1108.73, "D#6": 1244.51, "F#6": 1479.98, "G#6": 1661.22, "A#6": 1864.66,
    "C#7": 2217.46,
}


def pn(freq, dur, decay, seed, vol, brightness=1.0):
    """弹一个钢琴音。sfx_piano.piano 用模块级 VOL_OF_CURRENT 归一化，先设好再调。"""
    import sfx_piano as sp
    sp.VOL_OF_CURRENT = vol
    return sp.piano(freq, dur, decay, seed, brightness)


def ui_click(n, seed):
    """点击：一颗高音，越短越好。"""
    return pn(BK["C#6"], n / SR, 0.055, seed, 0.30)


def ui_place_stone(n, seed):
    return pn(BK["C#4"], n / SR, 0.26, seed, 0.45)


def ui_break(n, seed, f1, f2, decay, vol=0.40):
    """破碎：两个黑键一起砸下去。不协和但都在五声里，听起来是「裂」不是「噪」。"""
    d = n / SR
    a = pn(f1, d, decay, seed, vol)
    b = pn(f2, d, decay * 0.75, seed + 31, vol * 0.7, brightness=1.1)
    return norm(a + b, vol)


def ui_explode(n, seed, root):
    """爆炸：低音区重击 —— 基频 + 两个八度泛音，衰减拉长。"""
    d = n / SR
    x = pn(root, d, 0.5, seed, 0.8)
    x += pn(root * 2, d, 0.32, seed + 7, 0.45)
    x += pn(root * 3, d, 0.18, seed + 11, 0.22)
    return norm(x, 0.8)


def ui_fuse(n, seed):
    """引信：高音以 45ms 的间隔反复敲，靠节奏做出「嘶——」而不是靠噪声。"""
    x = np.zeros(n)
    step = int(SR * 0.045)
    rng = np.random.default_rng(seed)
    for i in range(0, max(0, n - step), step):
        seg = min(int(SR * 0.16), n - i)
        d = pn(BK["C#6"], seg / SR, 0.04, seed + i, 0.20 + 0.12 * rng.random(), brightness=1.2)
        x[i:i + seg] += d[:seg]
    return norm(x, 0.40)


def ui_fizz(n, seed):
    """结晶 / 灭火：一串高音快速下行，像水汽散开。"""
    notes = ["A#6", "F#6", "C#6", "G#5", "D#5"]
    x = np.zeros(n)
    step = int(SR * 0.032)
    for i, name in enumerate(notes):
        off = i * step
        seg = min(int(SR * 0.22), n - off)
        if seg <= 0:
            break
        d = pn(BK[name], seg / SR, 0.10, seed + i * 13, 0.34)
        x[off:off + seg] += d[:seg] * (1.0 - i * 0.12)
    return norm(x, 0.45)


def ui_levelup(n, seed):
    """消多行：四个黑键上行，比得分更长更重。"""
    notes = ["C#5", "F#5", "A#5", "C#6"]
    x = np.zeros(n)
    step = int(SR * 0.072)
    for i, name in enumerate(notes):
        off = i * step
        seg = min(int(SR * 0.42), n - off)
        if seg <= 0:
            break
        d = pn(BK[name], seg / SR, 0.26, seed + i * 19, 0.48, brightness=1.08)
        x[off:off + seg] += d[:seg] * (0.95 - i * 0.07)
    return norm(x, 0.55)


def ui_pickup(n, seed):
    """得分：三个黑键快速上行。"""
    notes = ["C#6", "F#6", "A#6"]
    delays = (0.0, 0.052, 0.100)
    x = np.zeros(n)
    for i, (name, dl) in enumerate(zip(notes, delays)):
        off = int(SR * dl)
        seg = min(int(SR * 0.38), n - off)
        if seg <= 0:
            continue
        d = pn(BK[name], seg / SR, 0.22 - i * 0.03, seed + i * 17, 0.46, brightness=1.15)
        x[off:off + seg] += d[:seg] * (0.9 - i * 0.1)
    return norm(x, 0.5)


# 名字 -> (函数, 时长)：文件名与 SfxPlayer 里的硬编码路径保持一致
UI = {
    "click": (ui_click, 0.10),
    "place_stone": (ui_place_stone, 0.40),
    "break_stone": (lambda n, s: ui_break(n, s, BK["C#3"], BK["G#3"], 0.14), 0.45),
    "break_wood": (lambda n, s: ui_break(n, s, BK["D#3"], BK["A#3"], 0.12), 0.42),
    "break_glass": (lambda n, s: ui_break(n, s, BK["C#6"], BK["F#6"], 0.09), 0.36),
    "break_gravel": (lambda n, s: ui_break(n, s, BK["F#3"], BK["C#4"], 0.13), 0.42),
    "break_sand": (lambda n, s: ui_break(n, s, BK["A#3"], BK["D#4"], 0.15), 0.45),
    "explode": (lambda n, s: ui_explode(n, s, BK["C#2"]), 0.90),
    "tnt_explode1": (lambda n, s: ui_explode(n, s, BK["C#2"]), 0.90),
    "tnt_explode2": (lambda n, s: ui_explode(n, s + 21, BK["A#1"]), 0.90),
    "tnt_explode3": (lambda n, s: ui_explode(n, s + 42, BK["D#2"]), 0.90),
    "tnt_explode4": (lambda n, s: ui_explode(n, s + 63, BK["F#2"]), 0.90),
    "fuse": (ui_fuse, 0.80),
    "tnt_fuse": (ui_fuse, 0.80),
    "fizz": (ui_fizz, 0.36),
    "levelup": (ui_levelup, 0.60),
    "pickup_xp": (ui_pickup, 0.50),
}


# ---------- 输出 ----------

def _stereo(x, width=0.0007):
    """左声道略提前、右声道略滞后：几十分之一毫秒的时差就够撑开宽度。"""
    d = max(1, int(SR * width))
    left = np.concatenate([x[d:], np.zeros(d)])
    right = np.concatenate([np.zeros(d), x[:-d]])
    return np.stack([left * 0.99, right * 0.99], axis=1)


def write_wav(path, x, stereo=False):
    if stereo:
        data = _stereo(x)
        ch = 2
    else:
        data = x
        ch = 1
    pcm = (np.clip(data, -1.0, 1.0) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def to_ogg(wav_path, ogg_path):
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", wav_path,
                    "-c:a", "libvorbis", "-q:a", "6", "-ar", "44100", ogg_path],
                   check=True)
    os.remove(wav_path)


def build_place():
    dirp = os.path.join(OUT, "Place")
    os.makedirs(dirp, exist_ok=True)
    os.makedirs(TMP, exist_ok=True)
    n_made = 0
    for group in PIANO_NOTES:
        for v in range(1, 5):
            x = piano_make(group, v)
            wav = os.path.join(TMP, "%s%d.wav" % (group, v))
            write_wav(wav, x, stereo=True)
            to_ogg(wav, os.path.join(dirp, "%s%d.ogg" % (group, v)))
            n_made += 1
    print("放置音 %d 个 / %d 组" % (n_made, len(PLACE)))


def build_ui():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(TMP, exist_ok=True)
    n_made = 0
    for name, (fn, dur) in UI.items():
        n = secs(dur)
        x = fn(n, seed_of(name))
        wav = os.path.join(TMP, "%s.wav" % name)
        write_wav(wav, x)
        to_ogg(wav, os.path.join(OUT, "%s.ogg" % name))
        n_made += 1
    print("界面音 %d 个" % n_made)


if __name__ == "__main__":
    ui_only = "--ui-only" in sys.argv
    if not ui_only:
        build_place()
    build_ui()
    if os.path.isdir(TMP):
        for f in os.listdir(TMP):
            os.remove(os.path.join(TMP, f))
        os.rmdir(TMP)
    print("DONE")
