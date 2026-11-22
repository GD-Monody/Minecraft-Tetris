#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Unity 批处理外壳：跑一次构建，失败就立刻带着错误退出。

为什么要有这个脚本：Unity 编译失败时批处理照样会「正常返回」，日志里只有一行
`error CS...`，如果外面还跟着截图/同步的一串命令，就会一直等下去，看起来像卡死。
这里把「跑 Unity → 查日志 → 成败判定」收在一处：失败直接打印第一条错误并 exit 1，
后面的步骤（截图、同步、发布）一条都不会跑。

用法：
    py -3 tools/build.py win        # Windows 版
    py -3 tools/build.py webgl      # WebGL 版
    py -3 tools/build.py selftest   # 自检
"""
import io
import os
import re
import subprocess
import sys
import time

PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe"

TASKS = {
    "win": ("MinecraftTetris.EditorTools.BuildScript.BuildWindows", "build_win.log"),
    "webgl": ("MinecraftTetris.EditorTools.BuildScript.BuildWebGL", "build_webgl.log"),
    "selftest": ("MinecraftTetris.EditorTools.SelfTest.Run", "selftest.log"),
}


def clean_env():
    """代理会挂住 Unity 的批处理（之前推 git 也栽在这），跑之前清掉。"""
    env = dict(os.environ)
    for k in list(env):
        if k.lower() in ("http_proxy", "https_proxy", "all_proxy",
                         "node_options", "node_path", "node_extra_ca_certs"):
            env.pop(k, None)
    return env


def run(task):
    if task not in TASKS:
        print("用法: py -3 tools/build.py <%s>" % "|".join(TASKS))
        return 2

    method, logname = TASKS[task]
    log = os.path.join(PROJ, "Logs", logname)
    os.makedirs(os.path.dirname(log), exist_ok=True)
    if os.path.exists(log):
        os.remove(log)

    args = [UNITY, "-batchmode", "-quit", "-projectPath", PROJ,
            "-executeMethod", method, "-logFile", log]
    t0 = time.time()
    print("[build] %s 开始…" % task)
    proc = subprocess.run(args, env=clean_env())
    cost = time.time() - t0

    text = ""
    if os.path.exists(log):
        text = io.open(log, encoding="utf-8", errors="replace").read()

    # ── 编译错误：一条都不许放过，立刻退出 ────────────────────────────
    errs = re.findall(r"^(Assets\\[^\n]*error CS\d+:[^\n]*)$", text, re.M)
    if errs:
        print("\n[build] 编译失败（%.1fs，Unity 退出码 %d）：" % (cost, proc.returncode))
        for line in dict.fromkeys(errs):
            print("  " + line.strip())
        print("[build] 已中止：后续步骤不执行。")
        return 1

    if proc.returncode != 0:
        print("\n[build] Unity 退出码 %d（%.1fs）" % (proc.returncode, cost))
        tail = [l for l in text.splitlines() if l.strip()][-12:]
        for line in tail:
            print("  " + line)
        print("[build] 已中止：后续步骤不执行。")
        return 1

    # ── 任务自己的成败标记 ────────────────────────────────────────────
    if task == "selftest":
        m = re.search(r"通过 (\d+) 项，失败 (\d+) 项", text)
        if not m:
            print("\n[build] 自检没跑起来（日志里没有汇总行），已中止。")
            return 1
        ok, bad = int(m.group(1)), int(m.group(2))
        print("\n[build] 自检 %.1fs：通过 %d 项，失败 %d 项" % (cost, ok, bad))
        if bad:
            for line in re.findall(r"^\[FAIL\][^\n]*$", text, re.M)[:10]:
                print("  " + line)
            print("[build] 有失败项，已中止。")
            return 1
        return 0

    m = re.search(r"构建结果[:：]\s*(\w+).*?错误[:：]\s*(\d+)", text)
    if not m:
        print("\n[build] 日志里没有构建结果行，按失败处理。")
        return 1
    status, errs_n = m.group(1), int(m.group(2))
    size = re.search(r"大小[:：]\s*(\d+)", text)
    print("\n[build] %s：%s，%.1fs，%s 字节，错误 %d"
          % (task, status, cost, size.group(1) if size else "?", errs_n))
    if status.lower() != "succeeded" or errs_n:
        print("[build] 构建失败，已中止。")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(run(sys.argv[1] if len(sys.argv) > 1 else "win"))
