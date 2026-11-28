"""生成音效试听页（Logs/sfx_preview/index.html）。

给用户快速过一遍所有音效，不用进游戏一个个试。
音频是拷贝出来的副本，源文件仍在 Assets/Resources/Sfx。
"""
import os
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_PLACE = os.path.join(ROOT, "Assets", "Resources", "Sfx", "Place")
SRC_UI = os.path.join(ROOT, "Assets", "Resources", "Sfx")
DST = os.path.join(ROOT, "Logs", "sfx_preview")

GROUPS = [("tap", "放置（高频嗒）")]
UIS = [("click", "点击"), ("place_stone", "放置(默认)"), ("break_stone", "石头碎"),
       ("break_wood", "木头碎"), ("break_glass", "玻璃碎"), ("break_gravel", "砾石碎"),
       ("break_sand", "沙碎"), ("tnt_explode1", "爆炸1"), ("tnt_explode2", "爆炸2"),
       ("tnt_explode3", "爆炸3"), ("tnt_explode4", "爆炸4"), ("fuse", "引信"),
       ("tnt_fuse", "TNT引信"), ("fizz", "结晶/灭火"), ("levelup", "升级"),
       ("pickup_xp", "得分")]


def main():
    # 不整目录删（会触发批量删除保护），同名覆盖即可
    os.makedirs(DST, exist_ok=True)

    for g, _ in GROUPS:
        for v in range(1, 5):
            f = os.path.join(SRC_PLACE, "%s%d.ogg" % (g, v))
            if os.path.exists(f):
                shutil.copy(f, DST)
    for n, _ in UIS:
        f = os.path.join(SRC_UI, "%s.ogg" % n)
        if os.path.exists(f):
            shutil.copy(f, DST)

    def card(title, files):
        cells = "".join(
            '<button class="snd" onclick="p(\'%s\',this)">%s</button>' % (f, lbl)
            for f, lbl in files)
        return '<div class="grp"><div class="t">%s</div><div class="row">%s</div></div>' % (title, cells)

    rows = [card("%s（%s）" % (cn, g), [("%s%d.ogg" % (g, v), "变体 %d" % v) for v in range(1, 5)])
            for g, cn in GROUPS]
    rows.append(card("界面音", [("%s.ogg" % n, lbl) for n, lbl in UIS]))

    page = """<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8">
<title>Substrata 音效试听</title><style>
body{background:#141821;color:#dde3ee;font:14px/1.6 system-ui,"Noto Sans SC","Microsoft YaHei",sans-serif;margin:0;padding:28px}
h1{font-size:20px;margin:0 0 4px}p.sub{color:#8592a8;margin:0 0 22px;font-size:13px}
.grp{margin-bottom:18px;border-bottom:1px solid #232b3a;padding-bottom:14px}
.t{color:#8fd6a8;font-size:13px;margin-bottom:8px;letter-spacing:.5px}
.row{display:flex;gap:8px;flex-wrap:wrap}
.snd{background:#1e2532;color:#cfd8e8;border:1px solid #2e3a4e;border-radius:8px;
padding:9px 16px;font-size:13px;cursor:pointer;transition:.15s;font-family:inherit}
.snd:hover{background:#27324a;border-color:#3f5170}
.snd.on{background:#2f4a3a;border-color:#4f8a68;color:#b8f0cf}
</style></head><body>
<h1>Substrata 音效试听</h1>
<p class="sub">全部由 tools/build_sfx.py 程序合成（固定种子可复现）。点一下播放。</p>
""" + "".join(rows) + """
<script>
function p(f,btn){
  document.querySelectorAll('.snd').forEach(b=>b.classList.remove('on'));
  btn.classList.add('on');
  var a=new Audio(f); a.volume=1; a.play();
  a.onended=()=>btn.classList.remove('on');
}
</script></body></html>"""

    open(os.path.join(DST, "index.html"), "w", encoding="utf-8").write(page)
    print("试听页：", DST, "| 音频", len(os.listdir(DST)) - 1, "个")


if __name__ == "__main__":
    main()
