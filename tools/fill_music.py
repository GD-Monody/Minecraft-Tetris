"""补/换背景音乐：只接受时长够的 CC0 曲目。

上一轮按「先到先得」下了 10 首，其中 4 首只有 25~40 秒——当循环 BGM 会很快听腻。
这里改成「下载到临时文件 → 量时长 → 合格才收」，不合格就换下一个候选。

目标槽位：substrata_04 / 05 / 11 / 14（太短）+ menu_02（缺）
"""
import json
import os
import re
import subprocess
import urllib.parse

FF = r"C:\Users\lenovo\AppData\Local\Programs\Python\Python314\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v9.0.2.exe"
OUT = os.path.join("Assets", "Resources", "Music")
TMP = os.path.abspath("Logs/fill_tmp.bin")
MIN_SEC = 75          # 低于这个秒数不要（再短循环起来就听腻了）
MIN_BYTES = 1600000    # 体积预筛：小于 1.6MB 的基本都是 30 秒左右的短曲，不下
MAX_MB = 8            # 太大的也不要（包体）

# 槽位可由命令行指定（一次补一个，避免长时间跑被中断）：
#   py -3 tools/fill_music.py substrata_11.ogg
import sys as _sys
NEED = _sys.argv[1:] or ["substrata_04.ogg", "substrata_05.ogg", "substrata_11.ogg",
                         "substrata_14.ogg", "menu_02.ogg"]

QUERIES = ["ambient", "meditation", "relaxing", "fantasy", "orchestral",
           "atmospheric", "mellow", "soundtrack", "journey", "space ambient"]

LOG = open(os.path.join("Logs", "fill_music.log"), "a", encoding="utf-8")


def log(*a):
    m = " ".join(str(x) for x in a)
    print(m, flush=True)
    LOG.write(m + "\n")
    LOG.flush()


def get(url):
    r = subprocess.run(["curl", "-sL", "-m", "40", "-A", "Mozilla/5.0", url],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.stdout or ""


def duration(path):
    r = subprocess.run([FF, "-i", path], capture_output=True, text=True)
    m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", r.stderr or "")
    if not m:
        return 0.0
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))


def main():
    done = 0
    seen = set()
    for q in QUERIES:
        if done >= len(NEED):
            break
        url = ("https://opengameart.org/art-search-advanced?keys=" + urllib.parse.quote(q)
               + "&field_art_type_tid%5B%5D=12&field_art_licenses_tid%5B%5D=4")
        html = get(url)
        links = [l for l in dict.fromkeys(re.findall(r'href="(/content/[^"?#]+)"', html))
                 if "faq" not in l]
        log("搜索 %-14s 条目 %d（已补 %d）" % (q, len(links), done))

        for path in links:
            if done >= len(NEED):
                break
            if path in seen:
                continue
            seen.add(path)
            page = get("https://opengameart.org" + path)
            names = re.findall(r'license-name[^>]*>\s*([^<]{2,30}?)\s*<', page)
            if "CC0" not in names:
                continue
            files = [m for m in dict.fromkeys(
                        re.findall(r'https://opengameart\.org/sites/default/files/[^"\'\s]+?\.(?:ogg|mp3)', page))
                     if "/styles/" not in m and "/audio_preview/" not in m]
            if not files:
                continue

            name = path.split("/content/")[-1]
            # 先 HEAD 看大小：小文件直接跳过，省掉「下载 - 转换 - 量时长」这一整轮
            head = subprocess.run(["curl", "-sIL", "-m", "20", "-A", "Mozilla/5.0", files[0]],
                                  capture_output=True, text=True)
            sizes = re.findall(r"[Cc]ontent-[Ll]ength://s*(//d+)", head.stdout or "")
            if sizes and int(sizes[-1]) < MIN_BYTES:
                log("  跳过（%.1fMB）%s" % (int(sizes[-1]) / 1048576, name))
                continue

            r = subprocess.run(["curl", "-sL", "--retry", "2", "-m", "300", "-A", "Mozilla/5.0",
                                "-o", TMP, files[0]])
            if r.returncode != 0 or not os.path.exists(TMP) or os.path.getsize(TMP) < 30000:
                log("  下载失败", name)
                continue

            size_mb = os.path.getsize(TMP) / 1048576
            # 先粗判：体积太小基本就是短曲，省一次 ffmpeg
            if size_mb < 0.6:
                log("  跳过（体积 %.1fMB，多半太短）%s" % (size_mb, name))
                os.remove(TMP)
                continue

            dst = os.path.join(OUT, NEED[done])
            subprocess.run([FF, "-y", "-loglevel", "error", "-i", TMP,
                            "-c:a", "libvorbis", "-q:a", "5", "-ar", "44100", dst])
            os.remove(TMP)
            sec = duration(dst)
            if sec < MIN_SEC or os.path.getsize(dst) / 1048576 > MAX_MB:
                log("  不合格（%.0fs, %.1fMB）%s" % (sec, os.path.getsize(dst) / 1048576, name))
                os.remove(dst)
                continue

            log("  采用 %s  %.0fs  %.1fMB  %s" % (NEED[done], sec, os.path.getsize(dst) / 1048576, name))
            done += 1

    log("补齐 %d / %d" % (done, len(NEED)))


if __name__ == "__main__":
    main()
