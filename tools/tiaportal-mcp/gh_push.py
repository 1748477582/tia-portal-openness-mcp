#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Incremental push of the locally-tested TIA MCP server to GitHub via the
Git Database API (github.com:443 is blocked on this machine, api.github.com works).

Strategy (safe, non-destructive):
  1. re-read remote HEAD and ABORT if it moved since the diff plan was made
  2. create a blob for every added/modified file (raw bytes, base64)
  3. create a tree with base_tree = remote HEAD tree  -> only the 31 changed
     paths are overwritten, everything else on the remote is preserved
  4. create a commit whose parent is the current remote HEAD
  5. PATCH refs/heads/<branch> to the new commit (never force)

Usage:
  python gh_push.py --dry-run     # create blobs + tree + commit, DO NOT move the ref
  python gh_push.py               # full push
"""
import base64
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gh_diff_preview import api, read_token, OWNER, REPO, BRANCH, REMOTE_PREFIX, LOCAL_ROOT  # noqa: E402

COMMIT_MSG = """fix: STA-safe compiler messages, multi-version single MCP entry, save-before-close

Accumulated local changes (2026-08-22 .. 2026-08-25) that were never pushed:

Single multi-version MCP entry
- add tia_mcp_launcher.bat + detect_tia_version.ps1: one mcp.json entry that
  auto-selects bin-v18 / bin-v20 based on the running TIA Portal version and
  injects the matching PATH + --tia-major-version / --tia-portal-location.
- Portal.ConnectPortal now filters candidate processes by major version
  (TiaPortalProcessMatchesVersion) so a V20 server cannot attach to a V18 TIA.

Compile crash fix (0xE0434352 corrupted-state exception)
- CompilerResult.Messages is an Openness COM object and MUST be read on the
  STA thread. Add Portal.CollectCompilerMessagesOnSta and route all 5 call
  sites through it (Blocks.cs x2, Optimizations.cs, PlcSoftware.cs x2).
  Previously the tool layer read them off-STA and killed the process.

STA / RCW hardening
- StaExecutor now runs a real Windows message pump (Dispatcher.Run) instead of
  a bare SynchronizationContext; csproj references WindowsBase.
- Split RCW-returning internals (GetBlockRcw / GetBlockRcwList / GetTypeRcw)
  from plain-data DTO accessors so no Siemens.Engineering RCW crosses the STA
  boundary (Portal.Blocks / Portal.Devices / Portal.Helpers / Portal.Software /
  Portal.BlockImpact / Portal.CausalTrace + response DTOs).

No more silent data loss
- Disconnect / CloseProject / CloseSession take saveBeforeClose (default true)
  and save the open project or session before closing.
- best-effort auto-save on process exit.

Misc
- HTTP MCP response timeout 30s -> 120s (long compiles).
- add verification scripts (verify_compile_existing.py, discover_open.py,
  verify_compile_v18.py, smoke_test_v18.py, v20_*.py) and the GitHub
  diff/push tooling.

Verified locally: V18 + V20 builds 0 errors; CompileAndDiagnosePlc,
CompileSoftware and GetCompileDiagnostics all return without crashing.
"""


def main():
    dry = "--dry-run" in sys.argv
    # optional: --message "subject\n\nbody"  (overrides the default COMMIT_MSG)
    msg = COMMIT_MSG
    if "--message" in sys.argv:
        i = sys.argv.index("--message")
        if i + 1 < len(sys.argv):
            msg = sys.argv[i + 1]
    token = read_token()
    if not token:
        print("ERROR: no GitHub token")
        return 2

    plan_path = os.path.join(LOCAL_ROOT, "gh_push_plan.json")
    if not os.path.exists(plan_path):
        print("ERROR: run gh_diff_preview.py first")
        return 2
    plan = json.load(open(plan_path, encoding="utf-8"))

    # 1. confirm remote HEAD has not moved
    ref = api("/repos/%s/%s/git/ref/heads/%s" % (OWNER, REPO, BRANCH), token)
    cur_head = ref["object"]["sha"]
    if cur_head != plan["head_sha"]:
        print("ABORT: remote HEAD moved %s -> %s. Re-run gh_diff_preview.py."
              % (plan["head_sha"][:10], cur_head[:10]))
        return 3
    print("Remote HEAD confirmed: %s" % cur_head)

    commit = api("/repos/%s/%s/git/commits/%s" % (OWNER, REPO, cur_head), token)
    base_tree = commit["tree"]["sha"]
    print("Base tree: %s" % base_tree)

    targets = [("added", r) for r in plan["added"]] + [("modified", r) for r in plan["modified"]]
    print("Files to push: %d (added=%d, modified=%d)"
          % (len(targets), len(plan["added"]), len(plan["modified"])))

    # 2. create blobs
    tree_entries = []
    for kind, rel in targets:
        local_path = os.path.join(LOCAL_ROOT, rel.replace("/", os.sep))
        if not os.path.exists(local_path):
            print("  !! missing locally, skipping: %s" % rel)
            continue
        with open(local_path, "rb") as f:
            data = f.read()
        try:
            blob = api("/repos/%s/%s/git/blobs" % (OWNER, REPO), token, method="POST",
                       payload={"content": base64.b64encode(data).decode("ascii"),
                                "encoding": "base64"})
        except Exception as e:
            print("  !! blob failed for %s: %s" % (rel, e))
            return 4
        rpath = "%s/%s" % (REMOTE_PREFIX, rel)
        tree_entries.append({"path": rpath, "mode": "100644", "type": "blob", "sha": blob["sha"]})
        print("  [%s] %s -> blob %s (%d bytes)" % (kind[:3], rel, blob["sha"][:8], len(data)))

    if not tree_entries:
        print("Nothing to push.")
        return 0

    # 3. create tree on top of the remote tree (preserves everything else)
    new_tree = api("/repos/%s/%s/git/trees" % (OWNER, REPO), token, method="POST",
                   payload={"base_tree": base_tree, "tree": tree_entries})
    print("New tree: %s" % new_tree["sha"])

    # 4. create commit
    new_commit = api("/repos/%s/%s/git/commits" % (OWNER, REPO), token, method="POST",
                     payload={"message": msg, "tree": new_tree["sha"],
                              "parents": [cur_head]})
    print("New commit: %s" % new_commit["sha"])

    if dry:
        print("\n--dry-run: ref NOT moved. Nothing was published.")
        return 0

    # 5. move the ref forward (no force)
    api("/repos/%s/%s/git/refs/heads/%s" % (OWNER, REPO, BRANCH), token, method="PATCH",
        payload={"sha": new_commit["sha"]})
    print("Ref %s updated to %s" % (BRANCH, new_commit["sha"]))

    # 6. verify
    ref2 = api("/repos/%s/%s/git/ref/heads/%s" % (OWNER, REPO, BRANCH), token)
    ok = ref2["object"]["sha"] == new_commit["sha"]
    print("\nVERIFY: remote HEAD == new commit : %s" % ok)
    print("https://github.com/%s/%s/commit/%s" % (OWNER, REPO, new_commit["sha"]))
    return 0 if ok else 5


if __name__ == "__main__":
    sys.exit(main())
