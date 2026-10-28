"""不依赖第三方库，读 TTF 的 name / cmap 表，检查字体名与汉字覆盖。

用法：python tools/inspect_font.py <字体路径>
"""
import struct
import sys


def read_tables(data):
    num = struct.unpack(">H", data[4:6])[0]
    tables = {}
    for i in range(num):
        p = 12 + i * 16
        tag = data[p:p + 4].decode("latin-1")
        offset, length = struct.unpack(">II", data[p + 8:p + 16])
        tables[tag] = (offset, length)
    return tables


def read_names(data, tables):
    if "name" not in tables:
        return {}
    base = tables["name"][0]
    count, strOff = struct.unpack(">HH", data[base + 2:base + 6])
    out = {}
    for i in range(count):
        p = base + 6 + i * 12
        pid, eid, lid, nid, length, off = struct.unpack(">HHHHHH", data[p:p + 12])
        raw = data[base + strOff + off: base + strOff + off + length]
        try:
            text = raw.decode("utf-16-be") if (pid == 3 or pid == 0) else raw.decode("latin-1")
        except UnicodeDecodeError:
            continue
        out.setdefault(nid, set()).add(text)
    return out


def cmap_codepoints(data, tables):
    base = tables["cmap"][0]
    num = struct.unpack(">H", data[base + 2:base + 4])[0]
    best = None
    for i in range(num):
        p = base + 4 + i * 8
        pid, eid, off = struct.unpack(">HHI", data[p:p + 8])
        sub = base + off
        fmt = struct.unpack(">H", data[sub:sub + 2])[0]
        score = {(3, 10): 3, (3, 1): 2, (0, 4): 3, (0, 3): 2}.get((pid, eid), 1)
        if fmt in (4, 12) and (best is None or score > best[0]):
            best = (score, sub, fmt)
    if best is None:
        return set()
    _, sub, fmt = best
    cps = set()
    if fmt == 12:
        ngroups = struct.unpack(">I", data[sub + 12:sub + 16])[0]
        for g in range(ngroups):
            p = sub + 16 + g * 12
            start, end, _gid = struct.unpack(">III", data[p:p + 12])
            cps.update(range(start, min(end, start + 0x20000) + 1))
    else:
        segx2 = struct.unpack(">H", data[sub + 6:sub + 8])[0]
        seg = segx2 // 2
        ends = struct.unpack(">%dH" % seg, data[sub + 14:sub + 14 + segx2])
        p = sub + 16 + segx2
        starts = struct.unpack(">%dH" % seg, data[p:p + segx2])
        for s, e in zip(starts, ends):
            if s == 0xFFFF:
                continue
            cps.update(range(s, e + 1))
    return cps


def main():
    path = sys.argv[1]
    data = open(path, "rb").read()
    tables = read_tables(data)
    print("tables:", " ".join(sorted(tables)))
    names = read_names(data, tables)
    for nid, label in ((1, "family"), (2, "style"), (4, "full"), (6, "postscript")):
        if nid in names:
            print("%-10s %s" % (label, " | ".join(sorted(names[nid]))))
    cps = cmap_codepoints(data, tables)
    cjk = [c for c in cps if 0x4E00 <= c <= 0x9FFF]
    print("码位数 %d，汉字 %d（区间 U+%04X ~ U+%04X）" % (len(cps), len(cjk), min(cjk), max(cjk)))
    # 抽查游戏里会用到的字
    probe = "分数消除最高下一个游戏结束暂停继续重新开始退出主界面设置键位方块素材刷新下落速度恢复标准返回"
    missing = [ch for ch in probe if ord(ch) not in cps]
    print("抽查 %d 字，缺 %d 个：%s" % (len(probe), len(missing), "".join(missing) or "无"))
    for ch in "0123456789×|.：，":
        if ord(ch) not in cps:
            print("缺符号", ch)


if __name__ == "__main__":
    main()
