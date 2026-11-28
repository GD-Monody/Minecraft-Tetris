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
    # 七个音，放置时按顺序循环（do re mi fa so la xi），不再跟材质绑定。
    "do": 261.63,   # C4
    "re": 293.66,   # D4
    "mi": 329.63,   # E4
    "fa": 349.23,   # F4
    "so": 392.00,   # G4
    "la": 440.00,   # A4
    "xi": 493.88,   # B4
}

CN = {"do": "do", "re": "re", "mi": "mi", "fa": "fa",
      "so": "so", "la": "la", "xi": "xi"}

# 音量：雪和草最轻
VOL = {"do": 0.56, "re": 0.56, "mi": 0.58, "fa": 0.55,
       "so": 0.56, "la": 0.55, "xi": 0.54}

# 衰减时长：低音留得久，高音收得快
# 音长整体缩短：原来是 0.64~0.72s，连放两块就叠在一起，听着糊。
# 现在 0.34~0.55s，既保留钢琴的衰减尾巴，又不至于互相盖住。
# 尾音 0.42s：太短就没有琴的余韵，太长连放会糊。
DECAY = {"do": 0.44, "re": 0.42, "mi": 0.40, "fa": 0.38,
         "so": 0.36, "la": 0.34, "xi": 0.32}


def seed_of(t):
    return zlib.crc32(t.encode("utf-8")) % 100000


def piano(freq, dur, decay, seed, brightness=1.0):
    """真钢琴的四个特征，缺一个就会"像电子琴"：

    1. 同音多弦：一根弦是一个纯正弦，太干净。真琴每个音有 2~3 根弦，
       彼此差零点几音分，叠加后产生缓慢的"拍"（chorus），这是"活"的来源。
    2. 双段衰减：音头几十分之一秒掉得很快，之后拖一条长尾。
       单一指数衰减听起来像"叮"的一声铃，不像琴。
    3. 高次谐波衰减更快：所以音头亮、尾巴暗，这是钢琴最容易被忽略的一点。
    4. 音板共鸣：80~150Hz 的箱体低频，给声音一个"体积"。
    """
    n = int(SR * dur)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed)

    x = np.zeros(n)
    B = 0.0004                     # 弦的不谐和
    detunes = (-0.7, 0.0, 0.8)     # 三根弦，单位：音分

    for k in range(1, 17):
        fk = freq * k * math.sqrt(1.0 + B * k * k)
        if fk > SR / 2 - 300:
            break

        # 幅度按 1/k^1.25 衰减，再加一个随谐波次数上升的高频衰减
        amp = (1.0 / (k ** 1.25)) * brightness
        if k > 4:
            amp *= 0.72 ** (k - 4)

        # 双段包络：快衰减 + 长尾
        fast = np.exp(-t / max(decay * 0.18, 0.008))
        slow = np.exp(-t / (decay * 1.9)) * 0.42
        env_k = fast + slow

        for d in detunes:
            fd = fk * (2.0 ** (d / 1200.0))
            x += (amp / len(detunes)) * np.sin(2 * math.pi * fd * t + rng.uniform(0, 6.28)) * env_k

    # 音板共鸣：低频箱体
    x += np.sin(2 * math.pi * 98.0 * t) * np.exp(-t / (decay * 0.8)) * 0.05
    x += np.sin(2 * math.pi * 147.0 * t) * np.exp(-t / (decay * 0.5)) * 0.03

    # 槌击：带通噪声（1.5~5kHz），只有 4ms，决定"是敲出来的"
    hn = min(int(SR * 0.004), n)
    if hn > 4:
        nz = rng.normal(0, 1, hn)
        nz = np.diff(np.concatenate([[0.0], nz]))          # 粗略高通
        a = 0.35
        acc, sm = 0.0, np.zeros(hn)
        for i in range(hn):                                 # 一阶低通，做出带通
            acc += a * (nz[i] - acc)
            sm[i] = acc
        x[:hn] += sm * np.exp(-np.arange(hn) / SR / 0.0012) * 0.10

    # 起音 1.5ms，避免爆音
    x *= np.clip(t / 0.0015, 0.0, 1.0)

    peak = float(np.max(np.abs(x)))
    return x * (VOL_OF_CURRENT / peak) if peak > 1e-9 else x


VOL_OF_CURRENT = 0.5   # piano() 里用，build 时按组覆盖


def make(group, variant):
    freq = NOTES[group]
    # 每个变体做极小的音分偏移与亮度差异，模拟同一架琴的不同次触键
    rng = np.random.default_rng(seed_of("%s-%d" % (group, variant)))
    freq *= 2 ** (rng.uniform(-4, 4) / 1200.0)      # ±4 音分
    brightness = rng.uniform(0.88, 1.12)

    dur = DECAY[group] * 2.4
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
