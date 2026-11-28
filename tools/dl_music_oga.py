"""从 OpenGameArt 抓 CC0 音乐，补进 Assets/Resources/Music。

为什么走 OGA：FreePD 原站已关，它的 GitHub 镜像走 raw.githubusercontent 与
jsDelivr（最终也跳回 raw），这两个域名在本机都不通；OpenGameArt 直连可用。

许可判定（踩过的坑）：
- 搜索页的 `field_art_licenses_tid[]=4` 过滤**不完全可靠**，必须逐条复查；
- 条目页里许可是 `<span class="license-name">CC0</span>` + `license_images/cc0.png`，
  用 `licenses/zero` 或裸 "CC0" 文本判断会误判；
- 音频链接要排除 `/styles/` 和 `/audio_preview/`（那是试听片段，不是完整曲目）。

输出：写成游戏里的目标文件名（substrata_04.ogg 这种），已存在则跳过。
"""
import json
import os
import re
import subprocess
import sys
import urllib.parse

FF = r"C:\Users\lenovo\AppData\Local\Programs\Python\Python314\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v9.0.2.exe"
OUT = os.path.join("Assets", "Resources", "Music")
TMP = os.path.abspath("Logs/oga_tmp.bin")

NEED = ["substrata_04.ogg", "substrata_05.ogg", "substrata_07.ogg", "substrata_09.ogg",
        "substrata_11.ogg", "substrata_12.ogg", "substrata_13.ogg", "substrata_14.ogg",
        "menu_01.ogg", "menu_02.ogg"]

QUERIES = ["piano", "calm", "ambient", "relaxing", "peaceful", "meditation",
           "soft piano", "gentle", "dreamy", "quiet"]

LOG = open(os.path.join("Logs", "dl_music.log"), "w", encoding="utf-8")


def log(*a):
    msg = " ".join(str(x) for x in a)
    print(msg, flush=True)
    LOG.write(msg + "\n")
    LOG.flush()


def get(url):
    r = subprocess.run(["curl", "-sL", "-m", "40", "-A", "Mozilla/5.0", url],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.stdout or ""


def main():
    cache = os.path.join("Logs", "oga_picked.json")
    if os.path.exists(cache):
        picked = json.load(open(cache, encoding="utf-8"))
        log("读缓存：%d 首" % len(picked))
    else:
        picked = search()
        json.dump(picked, open(cache, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    download(picked)


def search():
    seen, picked = set(), []
    for q in QUERIES:
        if len(picked) >= len(NEED):
            break
        url = ("https://opengameart.org/art-search-advanced?keys=" + urllib.parse.quote(q)
               + "&field_art_type_tid%5B%5D=12&field_art_licenses_tid%5B%5D=4")
        html = get(url)
        links = [l for l in dict.fromkeys(re.findall(r'href="(/content/[^"?#]+)"', html))
                 if "faq" not in l]
        log("搜索 %-12s 条目 %d，已选 %d" % (q, len(links), len(picked)))
        for path in links:
            if len(picked) >= len(NEED):
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
            picked.append((path.split("/content/")[-1], files[0]))
            log("  选中", path.split("/content/")[-1])
    log("共选中 %d 首" % len(picked))
    return picked


def download(picked):
    for i, (name, url) in enumerate(picked):
        dst = os.path.join(OUT, NEED[i])
        if os.path.exists(dst) and os.path.getsize(dst) > 50000:
            log("已有", NEED[i])
            continue
        # -C - 断点续传：上一轮被中断时留下的半截文件能接着下
        r = subprocess.run(["curl", "-sL", "--retry", "3", "--retry-delay", "2",
                            "-C", "-", "-m", "300", "-A", "Mozilla/5.0",
                            "-o", TMP, url])
        if r.returncode != 0 or not os.path.exists(TMP) or os.path.getsize(TMP) < 30000:
            log("下载失败", name, "size=", os.path.getsize(TMP) if os.path.exists(TMP) else 0)
            continue
        r = subprocess.run([FF, "-y", "-loglevel", "error", "-i", TMP,
                            "-c:a", "libvorbis", "-q:a", "5", "-ar", "44100", dst])
        size = os.path.getsize(dst) if os.path.exists(dst) else 0
        if os.path.exists(TMP):
            os.remove(TMP)
        log(("完成 " if r.returncode == 0 else "转换失败 ") + NEED[i],
            "%.1fMB" % (size / 1048576), "|", name)
    log("DONE")


if __name__ == "__main__":
    main()
