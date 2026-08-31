#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Fetch remote blob content for each MODIFIED file and show a compact diff
summary, so we can confirm WHAT changed before pushing anything.

Usage: python gh_diff_detail.py
Reads gh_push_plan.json (produced by gh_diff_preview.py).
"""
import base64
import difflib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gh_diff_preview import api, read_token, OWNER, REPO, REMOTE_PREFIX, LOCAL_ROOT  # noqa: E402


def norm_lines(text: str):
    # normalise line endings so CRLF/LF noise does not show up as a change
    return text.replace("\r\n", "\n").replace("\r", "\n").split("\n")


def main():
    token = read_token()
    plan_path = os.path.join(LOCAL_ROOT, "gh_push_plan.json")
    if not os.path.exists(plan_path):
        print("ERROR: run gh_diff_preview.py first")
        return 2
    plan = json.load(open(plan_path, encoding="utf-8"))

    targets = list(plan.get("modified", []))
    print("Inspecting %d modified files\n" % len(targets))

    results = []
    for rel in targets:
        rpath = "%s/%s" % (REMOTE_PREFIX, rel)
        try:
            blob = api("/repos/%s/%s/contents/%s?ref=%s" % (OWNER, REPO, rpath, plan["head_sha"]), token)
        except Exception as e:
            print("  !! %s : fetch failed %s" % (rel, e))
            results.append((rel, "FETCH_FAIL", 0, 0))
            continue

        if blob.get("encoding") == "base64":
            remote_bytes = base64.b64decode(blob["content"])
        else:
            remote_bytes = (blob.get("content") or "").encode("utf-8")

        local_path = os.path.join(LOCAL_ROOT, rel.replace("/", os.sep))
        with open(local_path, "rb") as f:
            local_bytes = f.read()

        # raw byte comparison
        if remote_bytes == local_bytes:
            print("  == %s : IDENTICAL bytes (sha mismatch was metadata/encoding only)" % rel)
            results.append((rel, "IDENTICAL", 0, 0))
            continue

        r_txt = remote_bytes.decode("utf-8", errors="replace")
        l_txt = local_bytes.decode("utf-8", errors="replace")
        rl, ll = norm_lines(r_txt), norm_lines(l_txt)

        sm = difflib.SequenceMatcher(None, rl, ll, autojunk=False)
        added = removed = 0
        samples = []
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag == "equal":
                continue
            ra, rb = rl[i1:i2], ll[j1:j2]
            if tag == "replace":
                removed += len(ra)
                added += len(rb)
            elif tag == "delete":
                removed += len(ra)
            elif tag == "insert":
                added += len(rb)
            if len(samples) < 4:
                samples.append((tag, [x.strip() for x in ra if x.strip()][:2],
                                [x.strip() for x in rb if x.strip()][:2]))

        raw_crlf = (b"\r\n" in remote_bytes, b"\r\n" in local_bytes)
        print("  ~~ %s" % rel)
        print("       lines: remote=%d local=%d  +%d / -%d   (remote CRLF=%s, local CRLF=%s)"
              % (len(rl), len(ll), added, removed, raw_crlf[0], raw_crlf[1]))
        for tag, ra, rb in samples:
            print("       [%s]" % tag)
            for x in ra[:2]:
                print("         - %s" % x[:110])
            for x in rb[:2]:
                print("         + %s" % x[:110])
        results.append((rel, "CHANGED", added, removed))
        print()

    print("=" * 72)
    print("DETAIL SUMMARY")
    print("=" * 72)
    for rel, kind, a, r in results:
        print("  %-58s %-12s +%d/-%d" % (rel, kind, a, r))
    return 0


if __name__ == "__main__":
    sys.exit(main())
