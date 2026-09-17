#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
gh_push_retry.py - 抗弱网版本的 GitHub 推送（gh_push.py 的重试包装）。

背景（2026-09-17 实测）：
  本机到 api.github.com 必须走 WorkBuddy 本地代理（HTTPS_PROXY=http://127.0.0.1:<port>），
  该代理质量很差：单次请求成功率约 20~30%，失败表现为
      ssl.SSLEOFError: [SSL: UNEXPECTED_EOF_WHILE_READING]
      URLError: Tunnel connection failed: 502 Bad Gateway
  而一次推送需要 6+ 次 API 调用（ref/commit/blobs/tree/commit/ref），
  若只在最外层整段重试，全部成功的概率趋近于 0 —— 必须"每次调用各自重试"。

  另外：代理端口会变（见过 53748 -> 55151），所以不要硬编码端口，
  urllib 会自动读取当前的 HTTPS_PROXY 环境变量。

用法：
  python gh_push_retry.py --message "commit subject"
  python gh_push_retry.py --message "..." --dry-run
  python gh_push_retry.py --message "..." --preview-only   # 只重跑 preview 不推送

说明：本脚本 monkey-patch gh_diff_preview.api，为每次调用加上指数退避重试，
      然后复用 gh_push.py 的既有逻辑（blob -> tree -> commit -> PATCH ref -> verify），
      因此行为与 gh_push.py 完全一致，只是不再被瞬时 SSL 故障打断。
"""
import importlib
import os
import sys
import time

ROOT = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ROOT)
os.chdir(ROOT)

MAX_ATTEMPTS = 60       # 单次调用最多尝试次数
BASE_DELAY = 0.8        # 起始退避秒数
MAX_DELAY = 4.0         # 退避上限

STATS = {"ok": 0, "failed": 0, "retries": 0}


def install_retry(module):
    """把 module.api 换成带重试的版本；返回原始 api。"""
    orig = module.api

    def api_retry(path, token, method="GET", payload=None, max_attempts=MAX_ATTEMPTS):
        last = None
        for attempt in range(1, max_attempts + 1):
            try:
                result = orig(path, token, method, payload)
                STATS["ok"] += 1
                if attempt > 1:
                    STATS["retries"] += 1
                    print("    (retry ok after %d tries: %s %s)" % (attempt, method, path[:60]))
                return result
            except Exception as exc:                      # noqa: BLE001 - 弱网下各种异常都要重试
                last = exc
                STATS["failed"] += 1
                time.sleep(min(BASE_DELAY * (1.25 ** min(attempt, 12)), MAX_DELAY))
        raise RuntimeError("api failed %dx on %s %s: %s" % (max_attempts, method, path, last))

    module.api = api_retry
    return orig


def main():
    args = sys.argv[1:]
    msg = "chore: sync local changes"
    if "--message" in args:
        i = args.index("--message")
        if i + 1 < len(args):
            msg = args[i + 1]

    gdp = importlib.import_module("gh_diff_preview")
    install_retry(gdp)

    print("=== gh_diff_preview ===")
    sys.argv = ["gh_diff_preview.py"]
    try:
        gdp.main()
    except SystemExit:
        pass

    if "--preview-only" in args:
        print("\n--preview-only：未推送。")
        return 0

    sys.modules.pop("gh_push", None)
    gh_push = importlib.import_module("gh_push")
    push_args = ["gh_push.py", "--message", msg]
    if "--dry-run" in args:
        push_args.append("--dry-run")
    sys.argv = push_args

    print("\n=== gh_push ===")
    rc = gh_push.main()

    total = STATS["ok"] + STATS["failed"]
    rate = (100.0 * STATS["ok"] / total) if total else 0.0
    print("\n调用统计: 成功 %d / 失败 %d，重试后成功 %d，单次成功率 %.0f%%"
          % (STATS["ok"], STATS["failed"], STATS["retries"], rate))
    return rc


if __name__ == "__main__":
    sys.exit(main())
