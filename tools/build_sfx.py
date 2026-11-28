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

def ui_click(n, seed):
    x = tone(1500, n, 0.018) * 0.7 + bandpass(noise(n, seed), 2000, 8000) * env(n, 0.001, 0.006) * 0.5
    return norm(x, 0.45)


def ui_place_stone(n, seed):
    return s_stone(n, seed)


def ui_break(n, seed, lo, hi, decay, tail=0.35):
    """破碎：比放置更长、更散，尾部补一点碎屑。"""
    x = bandpass(noise(n, seed), lo, hi) * env(n, 0.001, decay)
    for k in range(5):
        start = int(np.random.default_rng(seed + k).uniform(0.01, 0.12) * SR)
        ln = secs(0.02)
        if start + ln < n:
            x[start:start + ln] += bandpass(noise(ln, seed + k * 5), lo, hi) * env(ln, 0.001, 0.01) * tail
    return norm(x)


def ui_explode(n, seed):
    """爆炸：低频轰 + 宽带噪声，前面加一个极短的冲击。"""
    low = lowpass(noise(n, seed), 240) * env(n, 0.001, 0.28) * 1.6
    mid = bandpass(noise(n, seed + 3), 200, 4000) * env(n, 0.002, 0.16)
    boom = tone(52, n, 0.30) * 0.9
    x = low + mid + boom
    return norm(x, 0.85)


def ui_fuse(n, seed):
    """引信：白噪声嘶嘶声，用慢速随机调制做出"噼啪"的不均匀感。"""
    base = highpass(noise(n, seed), 2500)
    rng = np.random.default_rng(seed)
    mod = np.ones(n)
    step = int(SR * 0.01)
    for i in range(0, n, step):
        mod[i:i + step] = rng.uniform(0.35, 1.0)
    x = base * mod * env(n, 0.02, 0.9)
    return norm(x, 0.5)


def ui_fizz(n, seed):
    """结晶 / 水浇火：中频嘶声，尾巴快速收掉。"""
    x = bandpass(noise(n, seed), 1200, 6000) * env(n, 0.01, 0.22)
    return norm(x, 0.55)


def ui_levelup(n, seed):
    """升级：三个上行音，每个带一点谐波。"""
    x = np.zeros(n)
    notes = (523.25, 659.25, 783.99)
    for i, f in enumerate(notes):
        start = int(SR * 0.07 * i)
        seg = n - start
        lf = np.arange(seg) / SR
        tone_seg = (np.sin(2 * math.pi * f * lf) * 0.7
                    + np.sin(2 * math.pi * f * 2 * lf) * 0.25) * np.exp(-lf / 0.22)
        x[start:] += tone_seg * (1.0 - i * 0.12)
    return norm(x, 0.6)


def ui_pickup(n, seed):
    """得分：钢琴的快速上行琶音（C5 → G5 → C6）。

    原来是一段 50ms 的电子滑音，和钢琴的放置音完全不在一个世界里。
    现在复用同一套钢琴合成，只是音区更高、两个音错开 55ms ——
    听起来是"叮-叮"两下向上跳，和放置音的 do re mi 是一套乐器。
    """
    # 三个音：C5 G5 C6，后两个依次延迟
    notes = (523.25, 783.99, 1046.50)
    delays = (0.0, 0.055, 0.105)
    x = np.zeros(n)
    for i, (f, dl) in enumerate(zip(notes, delays)):
        seg = int(SR * 0.42)
        d = piano_note(f, seg / SR, 0.26 - i * 0.03, seed + i * 17, brightness=1.15)
        off = int(SR * dl)
        if off >= n:
            continue
        take = min(len(d), n - off)
        x[off:off + take] += d[:take] * (0.9 - i * 0.12)
    return norm(x, 0.5)


# 名字 -> (函数, 时长)：文件名与 SfxPlayer 里的硬编码路径保持一致
UI = {
    "click": (ui_click, 0.06),
    "place_stone": (ui_place_stone, 0.30),
    "break_stone": (lambda n, s: ui_break(n, s, 700, 3500, 0.10), 0.38),
    "break_wood": (lambda n, s: ui_break(n, s, 300, 1600, 0.12), 0.38),
    "break_glass": (lambda n, s: ui_break(n, s, 3000, 9000, 0.09), 0.38),
    "break_gravel": (lambda n, s: ui_break(n, s, 1000, 4500, 0.11), 0.38),
    "break_sand": (lambda n, s: ui_break(n, s, 1200, 5000, 0.13), 0.38),
    "explode": (ui_explode, 0.70),
    "tnt_explode1": (lambda n, s: ui_explode(n, s), 0.70),
    "tnt_explode2": (lambda n, s: ui_explode(n, s + 21), 0.70),
    "tnt_explode3": (lambda n, s: ui_explode(n, s + 42), 0.70),
    "tnt_explode4": (lambda n, s: ui_explode(n, s + 63), 0.70),
    "fuse": (ui_fuse, 0.80),
    "tnt_fuse": (ui_fuse, 0.80),
    "fizz": (ui_fizz, 0.32),
    "levelup": (ui_levelup, 0.55),
    "pickup_xp": (ui_pickup, 0.55),
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
