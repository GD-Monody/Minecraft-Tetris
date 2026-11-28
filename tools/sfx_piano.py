"""钢琴风格的放置音：加法合成 + 五声音阶。

为什么钢琴适合这个游戏：
- 放置音是高频重复事件，噪声型音色听久了会累；钢琴音有明确的衰减与音高，
  连放时是"演奏"而不是"噪音堆叠"。
- 音高全部落在 C 大调五声音阶（C D E G A）上，任意两块连着放都不会撞出小二度，
  这是最容易"听起来是对的"的做法。

合成要点（真实的钢琴不是简单的谐波叠加）：
- 谐波幅度按 1/k^1.2 衰减，高次谐波衰减更快（所以音头亮、尾巴暗）
- 弦有轻微不谐和：第 k 个泛音约在 k*f*sqrt(1+B*k²)，B 取 0.0004
- 琴槌击弦：最初 8ms 的极短宽带噪声，给"敲"的触感
- 每个变体随机化相位与极小的音分偏移，听起来是同一种乐器的不同次触键

输出到 Logs/sfx_piano，带试听页；确认后再写入游戏。
"""
import math
import os
import wave
import zlib

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Logs", "sfx_piano")

# C 大调五声音阶，按材质"硬度"分配音区：越硬越高、越重越低
# C2 65.4 / G2 98 / C3 130.8 / D3 146.8 / E3 164.8 / G3 196
# C4 261.6 / E4 329.6 / G4 392 / A4 440 / C5 523.3 / E5 659.3 / A5 880 / C6 1046.5
NOTES = {
    # 七声音阶 do re mi fa so la xi，跨两个八度。
    # 低音给重的、高音给轻的：连放时自然形成旋律走向，而不是一堆随机音高。
    "stone":   261.63,   # do  C4
    "wood":    293.66,   # re  D4
    "gravel":  329.63,   # mi  E4
    "sand":    349.23,   # fa  F4
    "grass":   392.00,   # so  G4
    "snow":    440.00,   # la  A4
    "glass":   493.88,   # xi  B4
    "metal":   523.25,   # do  C5
    "crystal": 659.25,   # mi  E5
    "water":   220.00,   # la  A3（低八度）
    "lava":    130.81,   # do  C3（低八度）
}

CN = {"stone": "石头", "wood": "木头", "gravel": "砾石", "grass": "草/叶", "sand": "沙",
      "snow": "雪", "glass": "玻璃", "metal": "金属", "crystal": "晶体",
      "water": "水", "lava": "岩浆"}

# 音量：雪和草最轻
VOL = {"stone": 0.55, "wood": 0.58, "gravel": 0.50, "grass": 0.34, "sand": 0.45,
       "snow": 0.20, "glass": 0.46, "metal": 0.50, "crystal": 0.44,
       "water": 0.48, "lava": 0.55}

# 衰减时长：低音留得久，高音收得快
# 音长整体缩短：原来是 0.64~0.72s，连放两块就叠在一起，听着糊。
# 现在 0.34~0.55s，既保留钢琴的衰减尾巴，又不至于互相盖住。
DECAY = {"stone": 0.30, "wood": 0.27, "gravel": 0.22, "grass": 0.17, "sand": 0.21,
         "snow": 0.13, "glass": 0.25, "metal": 0.29, "crystal": 0.28,
         "water": 0.25, "lava": 0.38}


def seed_of(t):
    return zlib.crc32(t.encode("utf-8")) % 100000


def piano(freq, dur, decay, seed, brightness=1.0):
    n = int(SR * dur)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed)

    x = np.zeros(n)
    B = 0.0004                       # 弦的不谐和系数
    for k in range(1, 17):
        fk = freq * k * math.sqrt(1.0 + B * k * k)
        if fk > SR / 2 - 200:        # 超过奈奎斯特会混叠，直接跳过
            break
        amp = (1.0 / (k ** 1.2)) * brightness
        dec = decay / (1.0 + 0.55 * (k - 1))     # 高次谐波衰减快 → 音头亮尾巴暗
        phase = rng.uniform(0, 2 * math.pi)
        x += amp * np.sin(2 * math.pi * fk * t + phase) * np.exp(-t / dec)

    # 琴槌击弦：最初 8ms 的宽带噪声，音量很小，但决定"是敲出来的"
    hammer_n = int(SR * 0.008)
    if hammer_n < n:
        nz = rng.normal(0, 1, hammer_n) * np.exp(-np.arange(hammer_n) / SR / 0.002)
        x[:hammer_n] += nz * 0.08

    # 早期反射：4 条延迟线模拟室内反射。量很小，只为了"有空间"而不是"加混响"
    wet = np.zeros(n)
    for delay_ms, gain in ((17, 0.30), (29, 0.22), (43, 0.15), (61, 0.10)):
        d = int(SR * delay_ms / 1000.0)
        if d < n:
            wet[d:] += x[:-d] * gain
    x = x + wet * 0.35

    # 起音包络：2ms 到满，避免爆音
    a = np.clip(t / 0.002, 0, 1)
    x *= a

    peak = float(np.max(np.abs(x)))
    return x * (VOL_OF_CURRENT / peak) if peak > 1e-9 else x


VOL_OF_CURRENT = 0.5   # piano() 里用，build 时按组覆盖


def make(group, variant):
    freq = NOTES[group]
    # 每个变体做极小的音分偏移与亮度差异，模拟同一架琴的不同次触键
    rng = np.random.default_rng(seed_of("%s-%d" % (group, variant)))
    freq *= 2 ** (rng.uniform(-4, 4) / 1200.0)      # ±4 音分
    brightness = rng.uniform(0.88, 1.12)

    dur = DECAY[group] + 0.22
    global VOL_OF_CURRENT
    VOL_OF_CURRENT = VOL[group]
    return piano(freq, dur, DECAY[group], seed_of("%s-%d-p" % (group, variant)), brightness)


def to_stereo(x, width=0.0007):
    """单声道转立体声：左声道略提前、右声道略滞后。

    不是简单复制两遍 —— 那样听起来还是"一个点"。几十分之一毫秒的时差
    就足以让声音有宽度，这是最省事也最不容易出错的做宽方式。
    """
    d = max(1, int(SR * width))
    left = np.concatenate([x[d:], np.zeros(d)])
    right = np.concatenate([np.zeros(d), x[:-d]])
    return np.stack([left * 0.99, right * 0.99], axis=1)


def write_wav(path, x):
    st = to_stereo(x)
    pcm = (np.clip(st, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        os.remove(os.path.join(OUT, f))

    for g in NOTES:
        for v in range(1, 5):
            write_wav(os.path.join(OUT, "%s%d.wav" % (g, v)), make(g, v))

    rows = []
    for g in NOTES:
        cells = "".join('<button class="snd" onclick="p(\'%s%d.wav\',this)">变体 %d</button>' % (g, v, v)
                        for v in range(1, 5))
        rows.append('<div class="grp"><div class="t">%s（%s）· %s</div><div class="row">%s</div></div>'
                    % (CN[g], g, "%.0f Hz" % NOTES[g], cells))

    page = """<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8">
<title>钢琴风格音效</title><style>
body{background:#141821;color:#dde3ee;font:14px/1.6 system-ui,"Noto Sans SC","Microsoft YaHei",sans-serif;margin:0;padding:28px}
h1{font-size:20px;margin:0 0 4px}p.sub{color:#8592a8;margin:0 0 22px;font-size:13px}
.grp{margin-bottom:16px;border-bottom:1px solid #232b3a;padding-bottom:12px}
.t{color:#8fd6a8;font-size:13px;margin-bottom:8px}
.row{display:flex;gap:8px;flex-wrap:wrap}
.snd{background:#1e2532;color:#cfd8e8;border:1px solid #2e3a4e;border-radius:8px;
padding:9px 16px;font-size:13px;cursor:pointer;font-family:inherit}
.snd:hover{background:#27324a}.snd.on{background:#2f4a3a;border-color:#4f8a68;color:#b8f0cf}
</style></head><body><h1>钢琴风格的放置音</h1>
<p class="sub">加法合成 + 五声音阶。可以连点几个不同的听和声效果。</p>
%s
<script>function p(f,b){document.querySelectorAll('.snd').forEach(x=>x.classList.remove('on'));
b.classList.add('on');var a=new Audio(f);a.volume=1;a.play();a.onended=()=>b.classList.remove('on');}</script>
</body></html>""" % ("".join(rows))
    open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write(page)
    print("已生成", OUT, "|", len(os.listdir(OUT)) - 1, "个音频")


if __name__ == "__main__":
    main()
