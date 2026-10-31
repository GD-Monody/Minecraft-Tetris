"""改完 C# 之后的本地预检——在叫 Unity 之前先把低级错误挑出来。

为什么要这个：
Unity 批处理一轮要几十秒（冷启动 + 导入资源 + 编译程序集），而「自检跑很久没结果」
绝大多数时候不是自检慢，而是**编译失败**了—— Unity 编不过就直接退出，那条批处理
命令却还挂着，看上去就像卡住。括号写错、变量重名这类错误，本来在文本层面就能看出来，
犯不着为它白等一轮冷启动。

用法：
    python tools/check.py               # 只做静态预检，不启动 Unity（毫秒级）
    python tools/check.py <日志路径>    # 解析 Unity 跑完的日志，把错误挑出来

退出码：
    0 没发现问题    1 有 error 级问题（别去跑 Unity 了）    2 发现是不干净的工作区
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")
EDITOR = os.path.join(ROOT, "Assets", "Editor")

errors = []
warns = []

# C# 里作声明用的类型名：内置关键字、Unity 常用类型、以及任何首字母大写的标识符
BUILTIN_TYPES = {
    "var", "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
    "float", "double", "decimal", "bool", "char", "string", "object", "dynamic",
}

DECL_RE = re.compile(
    r"\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?==|;|\)|,)"
)


def strip_comments_and_strings(src):
    """把注释和字符串字面量换成等长空白。

    这样后面用下标找括号时不会被 \"{\" 或注释里的半个括号骗到，
    而且行号、列号保持不变——报错要能指到原文那一行。
    """
    out = list(src)
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            while i < n and src[i] != "\n":
                out[i] = " "
                i += 1
        elif c == "/" and i + 1 < n and src[i + 1] == "*":
            while i < n:
                if src[i] == "\n":
                    out[i] = "\n"
                    i += 1
                    continue
                if src[i] == "*" and i + 1 < n and src[i + 1] == "/":
                    out[i] = " "
                    out[i + 1] = " "
                    i += 2
                    break
                out[i] = " "
                i += 1
            else:
                pass
        elif c == '"':
            start = i
            i += 1
            while i < n:
                if src[i] == "\\":
                    out[i] = " "
                    if i + 1 < n:
                        out[i + 1] = " "
                    i += 2
                    continue
                if src[i] == '"':
                    out[i] = " "
                    i += 1
                    break
                if src[i] == "\n":      # 字符串没闭合，让它跨行会污染后面所有判定
                    break
                out[i] = " "
                i += 1
            for k in range(start, min(i, n)):
                out[k] = " "
        elif c == "'":
            start = i
            i += 1
            while i < n:
                if src[i] == "\\":
                    out[i] = " "
                    if i + 1 < n:
                        out[i + 1] = " "
                    i += 2
                    continue
                if src[i] == "'":
                    out[i] = " "
                    i += 1
                    break
                if src[i] == "\n":
                    break
                out[i] = " "
                i += 1
            for k in range(start, min(i, n)):
                out[k] = " "
        else:
            i += 1
    return "".join(out)


def check_balance(path, src, clean):
    """括号配平。返回最深的那一层是否正常收尾。"""
    pairs = {")": "(", "]": "[", "}": "{"}
    stack = []
    for i, c in enumerate(clean):
        if c in "([{":
            stack.append((c, i))
        elif c in ")]}":
            if not stack:
                line = clean.count("\n", 0, i) + 1
                errors.append("%s:%d  多出来的 '%s'（前面没有配对的 '%s'）"
                              % (path, line, c, pairs[c]))
                return
            top, _ = stack.pop()
            if top != pairs[c]:
                line = clean.count("\n", 0, i) + 1
                errors.append("%s:%d  括号套错了：这里收的是 '%s'，打开的是 '%s'"
                              % (path, line, c, top))
                return
    if stack:
        c, i = stack[-1]
        line = clean.count("\n", 0, i) + 1
        errors.append("%s:%d  '%s' 没有闭合（文件结尾还欠 %d 个）"
                      % (path, line, c, len(stack)))


def check_unterminated_string(path, src):
    """逐行找没闭合的双引号。多行字符串在 Unity 里不算合法 C# 字符串场景，直接报。"""
    for lineno, line in enumerate(src.split("\n"), 1):
        # 去掉转义后的引号再数，\" 不算边界
        stripped = line.replace('\\"', "")
        if stripped.count('"') % 2 != 0:
            clean_line = line.split("//")[0]
            if clean_line.replace('\\"', "").count('"') % 2 != 0:
                errors.append("%s:%d  这一行的双引号是单数个，字符串没闭合：%s"
                              % (path, lineno, line.strip()[:70]))


def looks_like_type(tok):
    if tok in BUILTIN_TYPES:
        return True
    return tok[0].isupper()


# 这些关键字后面带的圆括号是「语句头」，里面的变量只活到这句结束
STMT_KW_RE = re.compile(
    r"\b(for|foreach|while|if|else|do|using|lock|catch|switch|fixed)\b\s*"
)


def find_close_paren(s, open_at):
    """给定 '(' 的下标，返回配对的 ')' 下标；找不到返回 -1。"""
    depth = 0
    for i in range(open_at, len(s)):
        if s[i] == "(":
            depth += 1
        elif s[i] == ")":
            depth -= 1
            if depth == 0:
                return i
    return -1


def check_dup_locals(path, clean, changed):
    """找会带来 CS0136 的重复声明：新写的变量跟外层/内层已有的同名。

    只对你这次改动过的那些行报警——旧代码里一大堆 for (int i) 本来就合法，
    全量扫会淹没人（试过，50 个文件报 645 条，全是噪音）。

    两阶段走，不走单遍。原因很实在：C# 的规矩是「里层和外层不许同名」，跟谁先写
    没关系。单遍扫描只能发现「后面写的撞了前面的」，倒过来写就漏；而且语句头处理
    完如果没跳过整个括号，主循环会把同一个 int i 再扫一遍，跟自己撞，全是假警报。

    阶段一：把每个声明记成 (名字, 所在作用域, 行号)，同时记下作用域的父子关系。
    阶段二：同名声明两两配对，只要作用域是包含关系就报；并列的两个 for 互不包含，不报。

    作用域怎么算：
    - '{' 开一层；
    - 语句关键字（for/if/switch...）也开一层，它后面紧跟的 '{' 算语句体、不再单独开；
    - 没带花括号的单语句，到 ';' 收掉那层；
    - 普通圆括号也单独开一层。这条是专门为了方法签名——static int Dot(Vector3Int a, ...)
      里的 a 活不出那个括号，不这么处理的话，文件里每个方法签名都带着同名的 a，
      彼此看着像冲突，其实八竿子打不着（剩的最后那几十条噪音全是它）。
    """
    lines = clean.split("\n")

    scope_names = [dict()]     # 每个作用域里声明过的名字 -> 行号（去重时用）
    parents = [-1]             # 作用域的父作用域下标，0 号是根
    decls = []                 # (名字, 作用域下标, 行号)
    kinds = ["block"]          # 每层是 'block' / 'stmt' / 'paren'
    stack = [0]

    def push(kind):
        idx = len(scope_names)
        scope_names.append(dict())
        parents.append(stack[-1])
        kinds.append(kind)
        stack.append(idx)
        return idx

    def pop():
        if len(stack) > 1:
            stack.pop()
            kinds.pop()

    def add(name, lineno):
        scope_names[stack[-1]][name] = lineno
        decls.append((name, stack[-1], lineno))

    i = 0
    n = len(clean)
    while i < n:
        c = clean[i]

        mkw = STMT_KW_RE.match(clean, i)
        if mkw:
            j = mkw.end()
            while j < n and clean[j] in " \t\r\n":
                j += 1
            if j < n and clean[j] == "(":
                close = find_close_paren(clean, j)
                if close > 0:
                    push("stmt")
                    for dm in DECL_RE.finditer(clean, j + 1, close):
                        if looks_like_type(dm.group(1)):
                            add(dm.group(2), clean.count("\n", 0, dm.start()) + 1)
                    i = close + 1          # 整个括号都吃掉，别让主循环再扫一遍
                    continue
                i = j + 1
                continue
            if j < n and clean[j] == "{":
                push("stmt")
                i = j
                continue
            i = j
            continue

        if c == "(":
            push("paren")
            i += 1
            continue

        if c == ")":
            if kinds[-1] == "paren":
                pop()
            i += 1
            continue

        if c == "{":
            if kinds[-1] == "stmt":
                kinds[-1] = "block"       # 语句体，归到刚才那层
            else:
                push("block")
            i += 1
            continue

        if c == "}":
            # 先收自己这一层。再往上把连着的语句层也收掉：
            #     for (int x...) for (int z...) { ... }
            # 那个花括号是内层 for 的语句体，它一闭，外层那条 for 整句也跟着结束了；
            # 不一路收上去，外层 for 的 x 会一直挂在方法体里，跟后面再出现的 for x 撞。
            # 带花括号的语句会在 '{' 处把自己变成 'block'，所以这里不会误伤更外层的块。
            if kinds[-1] in ("block", "stmt"):
                pop()
                while len(stack) > 1 and kinds[-1] == "stmt":
                    pop()
            i += 1
            continue

        if c == ";":
            # 不带花括号的语句体走到这里就结束了。注意要连着收好几层：
            #     for (int y...) for (int x...) SetCell(x, y);
            # 末尾这一个分号同时结束了 SetCell 那句和两层 for，只 pop 一次会让栈逐渐
            # 错位——方法体那层到最后也没人收，后面所有方法都被算进上一个方法里，
            # 于是某个方法体里的 i 和另一个方法体里的 i 就成了「冲突」。
            while len(stack) > 1 and kinds[-1] == "stmt":
                pop()
            i += 1
            continue

        m = DECL_RE.match(clean, i)
        if m and looks_like_type(m.group(1)):
            name = m.group(2)
            if name not in ("new", "return", "typeof", "sizeof", "nameof", "default"):
                add(name, clean.count("\n", 0, i) + 1)
                i = m.end()
                continue
        i += 1

    def is_changed(lineno):
        return changed is None or lineno in changed

    def is_ancestor(outer, inner):
        cur = parents[inner]
        while cur >= 0:
            if cur == outer:
                return True
            cur = parents[cur]
        return False

    by_name = {}
    for name, scope, lineno in decls:
        by_name.setdefault(name, []).append((scope, lineno))

    for name, items in sorted(by_name.items()):
        if len(items) < 2:
            continue
        for a in range(len(items)):
            for b in range(a + 1, len(items)):
                s1, l1 = items[a]
                s2, l2 = items[b]
                if s1 == s2:
                    if l1 == l2:
                        continue          # 同一次声明被登记了两遍，不是冲突
                    conflict = True
                else:
                    # 第 0 层是「类」那一级：不同方法的参数都落在那儿，同名完全合法
                    if 0 in (s1, s2):
                        conflict = False
                    else:
                        conflict = is_ancestor(s1, s2) or is_ancestor(s2, s1)
                if conflict and (is_changed(l1) or is_changed(l2)):
                    first, second = (l1, l2) if l1 <= l2 else (l2, l1)
                    warns.append(
                        "%s:%d  第 %d 行和第 %d 行的 '%s' 是里外层同名，C# 会报 CS0136：%s"
                        % (path, second, first, second, name,
                           lines[second - 1].strip()[:56]))


def scan_tree():
    files = []
    for base in (SCRIPTS, EDITOR):
        if not os.path.isdir(base):
            continue
        for dirpath, _dirs, names in os.walk(base):
            for nm in names:
                if nm.endswith(".cs"):
                    files.append(os.path.join(dirpath, nm))
    files.sort()
    return files


def check_meta(path):
    """新增脚本必须带 .meta，漏了 Unity 会给它换一个随机 GUID，引用全断。"""
    meta = path + ".meta"
    if not os.path.exists(meta):
        git_tracked = os.path.exists(os.path.join(ROOT, ".git"))
        if git_tracked:
            errors.append("缺少 .meta：%s\n           （新脚本必须把 Unity 生成的 .meta 一起提交，"
                          "否则换台机器 GUID 变了，场景里的挂载全丢）" % path)


def check_duplicate_class(files):
    seen = {}
    for path in files:
        with open(path, "r", encoding="utf-8-sig") as f:
            src = f.read()
        clean = strip_comments_and_strings(src)
        for m in re.finditer(r"\b(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)", clean):
            name = m.group(1)
            if name in seen and seen[name] != path:
                errors.append("类名重复：'%s' 在 %s 和 %s 都定义了"
                              % (name, seen[name], path))
            seen.setdefault(name, path)
    return seen


def git_out(args):
    try:
        import subprocess
        r = subprocess.run(["git"] + args, cwd=ROOT, capture_output=True,
                           text=True, encoding="utf-8", errors="replace")
        return r.stdout if r.returncode == 0 else ""
    except Exception:
        return ""


def changed_files():
    """本次改动过哪些 .cs、各是哪几行。

    返回 {绝对路径: 行号集合}，值 None 表示「整个文件都是新的」。
    拿不到 git 信息就返回空——那时候不查重名（避免几百条噪音），
    只想全量查的话手动传 --all。
    """
    res = {}

    for rel in git_out(["ls-files", "--others", "--exclude-standard"]).split():
        rel = rel.strip()
        if rel.endswith(".cs"):
            res[os.path.join(ROOT, rel.replace("/", os.sep))] = None

    cur = None
    for line in git_out(["diff", "-U0", "--", "Assets"]).split("\n"):
        if line.startswith("+++ b/"):
            cur = os.path.join(ROOT, line[6:].strip().replace("/", os.sep))
            if cur.endswith(".cs") and cur not in res:
                res[cur] = set()
        elif line.startswith("@@") and cur:
            rng = re.search(r"\+(\d+)(?:,(\d+))?", line)
            if rng and res.get(cur) is not None:
                start = int(rng.group(1))
                cnt = int(rng.group(2)) if rng.group(2) else 1
                if cnt:
                    res[cur].update(range(start, start + cnt))
    return res


def static_check(only_all=False):
    files = scan_tree()
    if not files:
        errors.append("没找到任何 .cs 文件，路径不对？根=%s" % ROOT)
        return
    changed = {} if only_all else changed_files()
    for path in files:
        check_meta(path)
        with open(path, "r", encoding="utf-8-sig") as f:
            src = f.read()
        check_unterminated_string(path, src)
        clean = strip_comments_and_strings(src)
        check_balance(path, src, clean)
        check_dup_locals(path, clean, changed.get(path, set() if not only_all else None))
    check_duplicate_class(files)
    scope = "全量" if only_all else "只查改动过的行"
    print("静态预检：扫了 %d 个 .cs（%s）" % (len(files), scope))


def parse_unity_log(log_path):
    """从 Unity 日志里把编译错误和自检失败挑出来。"""
    if not os.path.exists(log_path):
        errors.append("日志文件不存在：%s" % log_path)
        return
    with open(log_path, "r", encoding="utf-8", errors="replace") as f:
        lines = f.read().split("\n")

    compile_errors = []
    fails = []
    passes = 0
    for line in lines:
        if "error CS" in line:
            compile_errors.append(line.strip())
        if "[FAIL]" in line:
            fails.append(line.strip())
        elif "[PASS]" in line:
            passes += 1

    print("── Unity 日志分析：%s" % os.path.basename(log_path))

    if compile_errors:
        # Unity 打这类盘的时候前面还有一堆路径噪声，截到 error CS 开头
        print("\n!! 编译失败（%d 条），Unity 根本没跑到自检：" % len(compile_errors))
        for line in compile_errors[:25]:
            k = line.find("error CS")
            print("   " + line[k:] if k >= 0 else "   " + line)
        if len(compile_errors) > 25:
            print("   ...还有 %d 条" % (len(compile_errors) - 25))
        print("\n   修完再跑。编译都过不去的话，等再久也不会有自检结果。")
        errors.append("编译失败 %d 条" % len(compile_errors))

    if fails:
        print("\n!! 自检失败 %d 项：" % len(fails))
        for line in fails[:30]:
            print("   " + line)
        errors.append("自检失败 %d 项" % len(fails))

    if not compile_errors and not fails:
        print("   通过 %d 项，没有编译错误，也没有失败项。" % passes)
    else:
        print("\n   通过 %d 项。" % passes)


def main():
    only_all = "--all" in sys.argv
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    static_check(only_all)
    for p in args:
        parse_unity_log(p)

    print("")
    if errors:
        print("=== 有 %d 个必须处理的问题 ===" % len(errors))
        for e in errors:
            print("  ✗ " + e)
    if warns:
        print("=== 有 %d 处可疑（不一定错，扫一眼）===" % len(warns))
        for w in warns[:40]:
            print("  ? " + w)
        if len(warns) > 40:
            print("  ...还有 %d 处" % (len(warns) - 40))
    if not errors and not warns:
        print("=== 干净，可以去跑 Unity 了 ===")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
