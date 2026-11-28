"""音效风格实验：同一套 11 组材质，做两种完全不同的音色方向。

A 干净电子：三角波为主 + 极短包络，有明确音高，没有噪声。
             像现代解谜游戏（Tetris Effect 那一路），干净、不吵。
B 柔和木琴：正弦基频 + 四倍谐波（木琴的特征配比），衰减柔和，
             带一点点击打噪声。温暖、有"敲木头"的实感。

输出到 Logs/sfx_style_a / b，各带一个试听页，不覆盖游戏里的素材。
"""
import math
import os
import wave
import zlib

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GROUPS = ["stone", "wood", "gravel", "grass", "sand", "snow",
          "glass", "metal", "crystal", "water", "lava"]
CN = {"stone": "石头", "wood": "木头", "gravel": "砾石", "grass": "草/叶", "sand": "沙",
      "snow": "雪", "glass": "玻璃", "metal": "金属", "crystal": "晶体",
      "water": "水", "lava": "岩浆"}

# 每种材质的基频（Hz）：越硬越高、越重越低
BASE = {"stone": 233, "wood": 165, "gravel": 294, "grass": 440, "sand": 370,
        "snow": 587, "glass": 880, "metal": 660, "crystal": 1046,
        "water": 175, "lava": 110}

# 每种材质的音量：雪最轻，砂砾其次
VOL = {"stone": 0.62, "wood": 0.66, "gravel": 0.55, "grass": 0.42, "sand": 0.48,
       "snow": 0.26, "glass": 0.55, "metal": 0.58, "crystal": 0.50,
       "water": 0.55, "lava": 0.62}


def seed_of(t):
    return zlib.crc32(t.encode("utf-8")) % 100000


def env(n, attack, decay):
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-6), 0.0, 1.0)
    return a * np.exp(-t / max(decay, 1e-6))


def tri(freq, n, decay, detune=0.0):
    """三角波：谐波按 1/k² 衰减，比正弦多一点"芯"，又不像方波那样刺。"""
    t = np.arange(n) / SR
    x = np.zeros(n)
    f = freq * (1.0 + detune)
    for k in range(1, 7, 2):            # 只取奇次谐波
        x += ((-1) ** ((k - 1) // 2)) / (k * k) * np.sin(2 * math.pi * f * k * t)
    return x * np.exp(-t / max(decay, 1e-6))


def sine_stack(freq, n, decay, ratios):
    t = np.arange(n) / SR
    x = np.zeros(n)
    for i, (mul, amp) in enumerate(ratios):
        x += np.sin(2 * math.pi * freq * mul * t) * amp * np.exp(-t / (decay / (i + 1)))
    return x


def noise(n, seed):
    return np.random.default_rng(seed).normal(0.0, 1.0, n)


def lowpass(x, fc):
    a = 1.0 - math.exp(-2.0 * math.pi * fc / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc)
        y[i] = acc
    return y


def norm(x, peak):
    m = float(np.max(np.abs(x))) if len(x) else 0.0
    return x * (peak / m) if m > 1e-9 else x


def secs(s):
    return int(SR * s)


# ---------- 风格 A：干净电子 ----------

def a_make(group, n, seed):
    rng = np.random.default_rng(seed)
    f = BASE[group] * rng.uniform(0.97, 1.03)
    dur = {"water": 0.26, "lava": 0.30}.get(group, 0.16)

    if group == "water":
        # 水：下滑音 + 一点气泡，仍然是"音"不是"噪"
        t = np.arange(n) / SR
        freq = f * (1.0 - 0.45 * (t / (n / SR)))
        phase = 2 * math.pi * np.cumsum(freq) / SR
        x = np.sin(phase) * np.exp(-t / 0.09)
        x += tri(f * 3, n, 0.04) * 0.25
        return norm(x, VOL[group])

    if group == "lava":
        t = np.arange(n) / SR
        x = tri(f, n, 0.14) + tri(f * 2, n, 0.08) * 0.35
        return norm(x, VOL[group])

    # 常规：三角波 + 二次谐波，极短
    x = tri(f, n, dur)
    x += np.sin(2 * math.pi * f * 2 * np.arange(n) / SR) * np.exp(-np.arange(n) / SR / (dur * 0.6)) * 0.22
    if group in ("gravel", "grass", "sand", "snow"):
        # 松散材质加一点极轻的噪声，否则听起来是"电子音"不是"颗粒"
        amt = {"gravel": 0.30, "sand": 0.22, "grass": 0.18, "snow": 0.12}[group]
        nz = lowpass(noise(n, seed), {"gravel": 6000, "sand": 5000, "grass": 7000, "snow": 3500}[group])
        x += nz * env(n, 0.001, 0.045) * amt * 3.0
    return norm(x, VOL[group])


# ---------- 风格 B：柔和木琴 ----------

def b_make(group, n, seed):
    rng = np.random.default_rng(seed)
    f = BASE[group] * 0.5 * rng.uniform(0.97, 1.03)     # 降八度，木琴音区
    dur = {"water": 0.30, "lava": 0.34}.get(group, 0.22)

    if group == "water":
        t = np.arange(n) / SR
        freq = f * (1.0 - 0.3 * (t / (n / SR)))
        phase = 2 * math.pi * np.cumsum(freq) / SR
        x = np.sin(phase) * np.exp(-t / 0.11)
        for k in range(3):
            start = int(rng.uniform(0.03, 0.14) * SR)
            ln = secs(0.04)
            if start + ln < n:
                x[start:start + ln] += np.sin(2 * math.pi * f * 3 * np.arange(ln) / SR) * env(ln, 0.002, 0.015) * 0.3
        return norm(x, VOL[group])

    # 木琴：基频 + 4 倍（特征泛音）+ 10 倍（很弱），衰减按谐波递减
    x = sine_stack(f, n, dur, [(1.0, 1.0), (4.0, 0.32), (10.0, 0.08)])
    if group == "lava":
        x += sine_stack(f * 0.5, n, dur * 1.4, [(1.0, 0.7)])
    # 击打噪声：只留最初 12ms，给"槌子敲上去"的接触感
    nz = lowpass(noise(n, seed), {"metal": 9000, "glass": 9000}.get(group, 5000))
    x += nz * env(n, 0.0008, 0.012) * {"gravel": 0.5, "sand": 0.35, "grass": 0.3, "snow": 0.15}.get(group, 0.22)
    return norm(x, VOL[group])


# ---------- 输出 ----------

def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def build(tag, maker, preview_title):
    out = os.path.join(ROOT, "Logs", "sfx_style_%s" % tag)
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))

    for g in GROUPS:
        dur = {"water": 0.30, "lava": 0.34}.get(g, 0.22) + 0.14
        for v in range(1, 5):
            n = secs(dur)
            x = maker(g, n, seed_of("%s-%s-%d" % (tag, g, v)))
            write_wav(os.path.join(out, "%s%d.wav" % (g, v)), x)
            os.replace(os.path.join(out, "%s%d.wav" % (g, v)),
                       os.path.join(out, "%s%d.wav" % (g, v)))

    # 转成 wav 直接给浏览器播（省一道 ffmpeg，试听页只在本地看）
    rows = []
    for g in GROUPS:
        cells = "".join('<button class="snd" onclick="p(\'%s%d.wav\',this)">变体 %d</button>' % (g, v, v)
                        for v in range(1, 5))
        rows.append('<div class="grp"><div class="t">%s（%s）</div><div class="row">%s</div></div>'
                    % (CN[g], g, cells))

    page = """<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8">
<title>%s</title><style>
body{background:#141821;color:#dde3ee;font:14px/1.6 system-ui,"Noto Sans SC","Microsoft YaHei",sans-serif;margin:0;padding:28px}
h1{font-size:20px;margin:0 0 4px}p.sub{color:#8592a8;margin:0 0 22px;font-size:13px}
.grp{margin-bottom:16px;border-bottom:1px solid #232b3a;padding-bottom:12px}
.t{color:#8fd6a8;font-size:13px;margin-bottom:8px}
.row{display:flex;gap:8px;flex-wrap:wrap}
.snd{background:#1e2532;color:#cfd8e8;border:1px solid #2e3a4e;border-radius:8px;
padding:9px 16px;font-size:13px;cursor:pointer;font-family:inherit}
.snd:hover{background:#27324a}.snd.on{background:#2f4a3a;border-color:#4f8a68;color:#b8f0cf}
</style></head><body><h1>%s</h1>
<p class="sub">同一套 11 组材质，只换音色做法。点一下播放。</p>
%s
<script>function p(f,b){document.querySelectorAll('.snd').forEach(x=>x.classList.remove('on'));
b.classList.add('on');var a=new Audio(f);a.volume=1;a.play();a.onended=()=>b.classList.remove('on');}</script>
</body></html>""" % (preview_title, preview_title, "".join(rows))

    open(os.path.join(out, "index.html"), "w", encoding="utf-8").write(page)
    print("已生成", out, "|", len(os.listdir(out)) - 1, "个音频")


if __name__ == "__main__":
    build("a", a_make, "风格 A · 干净电子")
    build("b", b_make, "风格 B · 柔和木琴")
