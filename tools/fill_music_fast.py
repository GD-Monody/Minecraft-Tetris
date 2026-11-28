"""并发版补曲：先把候选池一次建好，再挑大的下。

慢在哪：串行抓每个条目详情页（1~3 秒/页），24 条就是一分多钟，
而且每次重跑都从头再来一遍。这里改成 8 线程并发抓，整池几十秒建完。

挑选顺序：按文件体积降序 —— 体积和时长强相关，先下大的，合格的先满足。
"""
import concurrent.futures as cf
import os
import re
import subprocess
import sys
import urllib.parse

FF = r"C:\Users\lenovo\AppData\Local\Programs\Python\Python314\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v9.0.2.exe"
OUT = os.path.join("Assets", "Resources", "Music")
TMP = os.path.abspath("Logs/fast_tmp.bin")
MIN_SEC = 75
MIN_BYTES = 1500000

QUERIES = ["ambient", "meditation", "relaxing", "fantasy", "atmospheric",
           "calm", "space ambient", "mellow"]

NEED = sys.argv[1:] or ["substrata_11.ogg", "substrata_14.ogg", "menu_02.ogg"]


def curl(args, timeout=45):
    return subprocess.run(["curl"] + args, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=timeout + 10)


def get(url):
    r = curl(["-sL", "-m", "45", "-A", "Mozilla/5.0", url])
    return r.stdout or ""


def head_size(url):
    r = curl(["-sIL", "-m", "20", "-A", "Mozilla/5.0", url])
    sizes = re.findall(r"[Cc]ontent-[Ll]ength:\s*(\d+)", r.stdout or "")
    return int(sizes[-1]) if sizes else 0


def duration(path):
    r = subprocess.run([FF, "-i", path], capture_output=True, text=True)
    m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", r.stderr or "")
    return (int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))) if m else 0.0


def main():
    # 1) 并发抓搜索页
    urls = ["https://opengameart.org/art-search-advanced?keys=" + urllib.parse.quote(q)
            + "&field_art_type_tid%5B%5D=12&field_art_licenses_tid%5B%5D=4" for q in QUERIES]
    links = []
    with cf.ThreadPoolExecutor(8) as ex:
        for html in ex.map(get, urls):
            for l in re.findall(r'href="(/content/[^"?#]+)"', html):
                if "faq" not in l and l not in links:
                    links.append(l)
    print("候选条目 %d" % len(links), flush=True)

    # 2) 并发抓详情页，收 CC0 + 直链
    def parse(path):
        page = get("https://opengameart.org" + path)
        names = re.findall(r'license-name[^>]*>\s*([^<]{2,30}?)\s*<', page)
        if "CC0" not in names:
            return None
        files = [m for m in dict.fromkeys(
                    re.findall(r'https://opengameart\.org/sites/default/files/[^"\'\s]+?\.(?:ogg|mp3)', page))
                 if "/styles/" not in m and "/audio_preview/" not in m]
        return (path.split("/content/")[-1], files[0]) if files else None

    with cf.ThreadPoolExecutor(8) as ex:
        cands = [c for c in ex.map(parse, links) if c]
    print("CC0 且有文件 %d" % len(cands), flush=True)

    # 3) 并发看大小，大的优先
    with cf.ThreadPoolExecutor(8) as ex:
        sizes = list(ex.map(lambda c: head_size(c[1]), cands))
    ranked = sorted(zip(cands, sizes), key=lambda x: -x[1])

    # 4) 逐个下载，合格的填槽
    done = 0
    for (name, url), size in ranked:
        if done >= len(NEED):
            break
        if size < MIN_BYTES:
            continue
        r = curl(["-sL", "--retry", "2", "-m", "300", "-A", "Mozilla/5.0", "-o", TMP, url], 320)
        if not os.path.exists(TMP) or os.path.getsize(TMP) < 30000:
            continue
        dst = os.path.join(OUT, NEED[done])
        subprocess.run([FF, "-y", "-loglevel", "error", "-i", TMP,
                        "-c:a", "libvorbis", "-q:a", "5", "-ar", "44100", dst])
        os.remove(TMP)
        sec = duration(dst)
        if sec < MIN_SEC:
            print("  不合格 %.0fs %s" % (sec, name), flush=True)
            os.remove(dst)
            continue
        print("  采用 %s  %.0fs  %.1fMB  %s" % (NEED[done], sec, os.path.getsize(dst) / 1048576, name), flush=True)
        done += 1

    print("补齐 %d / %d" % (done, len(NEED)), flush=True)


if __name__ == "__main__":
    main()
