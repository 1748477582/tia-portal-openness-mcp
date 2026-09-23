#if !TIA_V18
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.VersionControl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Partial: TIA Portal 的版本控制接口（VCI），从 Openness 走而不是从界面点。
    ///
    /// 为什么需要它：TIA 工程是一个二进制大块，Git 无法 diff。VCI 把它拆开：一个 *workspace*
    /// 就是磁盘上一个普通文件夹，每个被映射的对象一个文本文件（.s7dcl / .xml），可 diff、可提交。
    /// Openness 能建 workspace 并在两个方向同步，于是「导出 → 提交 → 评审 → 还原」整条链路可以无人值守。
    ///
    /// 🔴 **V20+ 专属**：本仓 V18 构建的 `Siemens.Engineering.dll` **没有** VCI 的完整 API
    /// （实测：V18 缺 `MappedObject` / `MappedObjects` / `GetSupportedFileFormats` / `ExportObject` /
    /// `ConnectObject` / `FileNameWithoutExtension`；V20 全部具备）。所以整个文件用 `#if !TIA_V18` 守卫，
    /// 与仓内 23 个 Unified 工具同一约定。
    ///
    /// ✅ **V20 运行期已实测（2026-09-23）**：隔离实例 + 新建空工程上，`GetVersionControlWorkspaces`
    /// 直接**成功返回**（"0 个 workspace"，而不是"无 VersionControlInterface"）；随后
    /// 建 workspace → 列出（`mappedObjects=0 | language=zh-CN`）→ 状态 → dryRun 同步/映射 全链通过。
    /// 故上游那句"VCI requires TIA Portal V21 or later" **不成立**（它只是 target V21，没在 V20 验过），
    /// 勿据此把本族收窄回 V21。
    ///
    /// ⚠️ 本仓适配：上游那份**不包** `_sta.Run`（其线程模型不同），本仓所有 Openness 访问必须在
    /// PortalSta 线程内完成、且**不得把 RCW 传出**。所以这里每个对外方法整体包 `_sta.Run`，只返回普通数据。
    /// </summary>
    public partial class Portal
    {
        // VCI 服务与经它得到的代理对象必须整会话保活：Openness 在服务实例被回收后会顺带作废
        // 经它取得的对象，于是「上一次调用拿到的 workspace 这次用就抛 Access to a disposed object」。
        // 只在 _sta.Run 内访问（恒为同一个 PortalSta 线程），故不违反「RCW 不外泄」。
        private static object? _vciOwnerProject;
        private static VersionControlInterface? _vciCached;
        private static readonly List<object> _vciKeepAlive = new List<object>();

        private static T KeepVci<T>(T o) where T : class
        {
            if (o != null) _vciKeepAlive.Add(o);
            return o!;
        }

        /// <summary>取（并缓存）本工程的 VCI 服务。**必须在 _sta.Run 内调用。**</summary>
        private VersionControlInterface RequireVciOnSta()
        {
            if (IsProjectNull())
            {
                _vciOwnerProject = null;
                _vciCached = null;
                _vciKeepAlive.Clear();
                throw new PortalException(PortalErrorCode.InvalidState,
                    "No project is open. Connect, then AttachToOpenProject / OpenProject first.");
            }

            var project = _project;
            if (_vciCached != null && ReferenceEquals(_vciOwnerProject, project))
                return _vciCached;

            var vci = (project as IEngineeringServiceProvider)?.GetService<VersionControlInterface>();
            if (vci == null)
                throw new PortalException(PortalErrorCode.InvalidState,
                    "This project exposes no VersionControlInterface (VCI). The running TIA Portal does not "
                    + "provide VCI for this project — VCI is a licensed option and needs a build that ships it.");

            _vciKeepAlive.Clear();
            _vciOwnerProject = project;
            _vciCached = vci;
            KeepVci(vci);
            return vci;
        }

        /// <summary>所有 workspace，含嵌套用户组。刻意不用 yield —— 迭代器会让中间的组在 MoveNext 之间被回收。</summary>
        private static List<Workspace> AllVciWorkspaces(VersionControlInterface vci)
        {
            var found = new List<Workspace>();
            var pending = new Stack<WorkspaceGroup>();
            pending.Push(KeepVci(vci.WorkspaceGroup));
            while (pending.Count > 0)
            {
                var g = pending.Pop();
                foreach (var w in KeepVci(g.Workspaces)) found.Add(KeepVci(w));
                foreach (var sub in KeepVci(g.Groups)) pending.Push(KeepVci(sub));
            }
            return found;
        }

        private static Workspace FindVciWorkspace(VersionControlInterface vci, string name)
        {
            var all = AllVciWorkspaces(vci);
            if (all.Count == 0)
                throw new PortalException(PortalErrorCode.NotFound,
                    "This project has no version control workspace yet. Create one with "
                    + "CreateVersionControlWorkspace, then map objects into it with ConnectProjectToWorkspace.");
            if (string.IsNullOrWhiteSpace(name)) return all[0];
            var hit = all.FirstOrDefault(w => string.Equals(w.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit == null)
                throw new PortalException(PortalErrorCode.NotFound,
                    "No workspace named '" + name + "'. Available: " + string.Join(", ", all.Select(w => w.Name)));
            return hit;
        }

        // ── 对外方法（全部 _sta.Run，返回普通数据）──────────────────────────

        public (List<string> Lines, string Message) ListVciWorkspaces()
        {
            return _sta.Run(() =>
            {
                var vci = RequireVciOnSta();
                var lines = new List<string>();
                foreach (var w in AllVciWorkspaces(vci))
                {
                    int mapped = 0;
                    try { mapped = w.MappedObjects.Count; } catch { }
                    string root = "";
                    try { root = w.RootPath?.FullName ?? ""; } catch { }
                    string lang = "-";
                    try { lang = w.WorkspaceLanguage?.ToString() ?? "-"; } catch { }
                    lines.Add(string.Format("{0} | folder={1} | mappedObjects={2} | language={3}",
                        w.Name, root, mapped, lang));
                }
                return (lines, lines.Count == 0
                    ? "No version control workspace exists in this project yet. Create one with "
                      + "CreateVersionControlWorkspace, then map objects into it with ConnectProjectToWorkspace."
                    : lines.Count + " version control workspace(s).");
            });
        }

        public string CreateVciWorkspace(string workspaceName, string folderPath)
        {
            return _sta.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(workspaceName))
                    throw new PortalException(PortalErrorCode.InvalidParams, "CreateVersionControlWorkspace: workspaceName is required.");
                if (string.IsNullOrWhiteSpace(folderPath))
                    throw new PortalException(PortalErrorCode.InvalidParams, "CreateVersionControlWorkspace: folderPath is required.");

                var dir = new DirectoryInfo(folderPath.Trim());
                if (!dir.Exists)
                    throw new PortalException(PortalErrorCode.NotFound,
                        "CreateVersionControlWorkspace: folderPath does not exist: " + dir.FullName + ". Create the folder (or clone the repo) first.");

                var vci = RequireVciOnSta();
                var existing = AllVciWorkspaces(vci)
                    .FirstOrDefault(w => string.Equals(w.Name, workspaceName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    throw new PortalException(PortalErrorCode.InvalidState,
                        "A workspace named '" + workspaceName + "' already exists. Use GetVersionControlWorkspaces to inspect it.");

                var group = KeepVci(vci.WorkspaceGroup);
                var ws = KeepVci(KeepVci(group.Workspaces).Create(workspaceName.Trim(), dir));
                return "Created workspace '" + ws.Name + "' at " + dir.FullName
                     + ". Next: ConnectProjectToWorkspace to map the project's objects into it, then "
                     + "SyncVersionControlWorkspace to write them out.";
            });
        }

        public (List<string> Lines, string Message) GetVciStatus(string workspaceName, bool changedOnly)
        {
            return _sta.Run(() =>
            {
                var vci = RequireVciOnSta();
                var ws = FindVciWorkspace(vci, workspaceName);

                var lines = new List<string>();
                int total = 0, differing = 0;
                foreach (var mo in KeepVci(ws.MappedObjects))
                {
                    total++;
                    string status;
                    // GetStatus() 返回 IndividualObjectCompareResult；对它 ToString() 只会得到类型名，
                    // 真正的判定在 CompareState（Equal / Unequal / WorkspaceFileMissing）。
                    try { status = mo.GetStatus().CompareState.ToString(); }
                    catch (Exception ex) { status = "Unknown(" + ex.Message + ")"; }
                    bool inSync = string.Equals(status, "Equal", StringComparison.OrdinalIgnoreCase);
                    if (!inSync) differing++;
                    if (changedOnly && inSync) continue;
                    string file = "?";
                    try
                    {
                        string d = "";
                        try { d = mo.DirectoryPath?.FullName ?? ""; } catch { }
                        string f = mo.FileNameWithoutExtension ?? "";
                        file = string.IsNullOrEmpty(d) ? f : d.TrimEnd('\\', '/') + "\\" + f;
                    }
                    catch { }
                    string fmt = "";
                    try { fmt = " | format=" + mo.FileFormat; } catch { }
                    string nm = "?";
                    try { nm = mo.FileNameWithoutExtension ?? "?"; } catch { }
                    lines.Add(string.Format("{0} | {1} | file={2}{3}", nm, status, file, fmt));
                }

                return (lines, string.Format(
                    "Workspace '{0}': {1} mapped object(s), {2} differ from the workspace files.{3}",
                    ws.Name, total, differing,
                    differing == 0
                        ? " Project and workspace are in sync — nothing to commit."
                        : " Call SyncVersionControlWorkspace(direction='ProjectToWorkspace') to write the changes out, then commit."));
            });
        }

        public (List<string> Lines, string Message, bool Ok) SyncVciWorkspace(
            string direction, string workspaceName, bool dryRun, bool changedOnly)
        {
            return _sta.Run(() =>
            {
                SynchronizationMode mode;
                string d = (direction ?? "").Trim();
                if (d.Equals("ProjectToWorkspace", StringComparison.OrdinalIgnoreCase)) mode = SynchronizationMode.ProjectToWorkspace;
                else if (d.Equals("WorkspaceToProject", StringComparison.OrdinalIgnoreCase)) mode = SynchronizationMode.WorkspaceToProject;
                else
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        "direction must be 'ProjectToWorkspace' (export for commit) or "
                        + "'WorkspaceToProject' (import to restore); got '" + direction + "'.");

                var vci = RequireVciOnSta();
                var ws = FindVciWorkspace(vci, workspaceName);

                // Openness 拒绝同步 compare status = Equal 的映射（"Synchronize cannot be called on a
                // workspace mapping that has a compare status of equal"），所以「强制全部」不存在；
                // Equal 一律跳过，changedOnly 只决定状态未知的对象要不要attempt。
                var targets = new List<MappedObject>();
                int skippedEqual = 0;
                foreach (var mo in KeepVci(ws.MappedObjects))
                {
                    string st;
                    try { st = mo.GetStatus().CompareState.ToString(); } catch { st = "Unknown"; }
                    if (string.Equals(st, "Equal", StringComparison.OrdinalIgnoreCase)) { skippedEqual++; continue; }
                    if (changedOnly && string.Equals(st, "Unknown", StringComparison.OrdinalIgnoreCase)) continue;
                    targets.Add(mo);
                }

                string wsRoot = "?";
                try { wsRoot = ws.RootPath?.FullName ?? "?"; } catch { }

                if (targets.Count == 0)
                    return (new List<string>(),
                        "Workspace '" + ws.Name + "': nothing to synchronize — every mapped object is already in sync.", true);

                string SafeName(MappedObject mo)
                {
                    try { return mo.FileNameWithoutExtension ?? "?"; } catch { return "?"; }
                }

                var lines = new List<string>();
                if (dryRun)
                {
                    foreach (var mo in targets) lines.Add(SafeName(mo) + " | would sync " + mode);
                    return (lines, string.Format(
                        "DRY RUN — nothing was written. {0} object(s) would be synchronized {1} in workspace '{2}' (folder {3}). "
                        + "Call again with dryRun=false to do it.",
                        targets.Count, mode, ws.Name, wsRoot), true);
                }

                int ok = 0, failed = 0;
                foreach (var mo in targets)
                {
                    try { mo.Synchronize(mode); ok++; lines.Add(SafeName(mo) + " | synchronized"); }
                    catch (Exception ex) { failed++; lines.Add(SafeName(mo) + " | FAILED: " + ex.Message); }
                }

                return (lines, string.Format(
                    "Workspace '{0}' ({1}): {2} synchronized, {3} failed, {6} already equal (skipped). Folder: {4}.{5}",
                    ws.Name, mode, ok, failed, wsRoot,
                    mode == SynchronizationMode.ProjectToWorkspace
                        ? " The text files are updated — `git add -A && git commit` from that folder."
                        : " The project now holds the workspace's version — compile and save to persist it.",
                    skippedEqual), failed == 0);
            });
        }

        // ── 整工程自动映射 ────────────────────────────────────────────────

        private sealed class VcNode
        {
            public IEngineeringObject Obj = null!;
            public string Label = "";
            public string RelDir = "";
            public bool Descendable;
        }

        private static string FlattenVci(string s)
            => (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static string VciObjName(IEngineeringObject o)
        {
            try
            {
                var v = o.GetAttribute("Name");
                var text = v?.ToString();
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }
            catch { }
            return o.GetType().Name;
        }

        private static string SanitizeVciPathPart(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "_";
            var bad = Path.GetInvalidFileNameChars();
            return new string(s.Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
        }

        /// <summary>在 VCI 给的格式里挑最利于 Git 的（优先 s7dcl → simatic → xml）。</summary>
        private static string? PreferredVciFormat(IList<string> formats)
        {
            return formats.FirstOrDefault(f => f.IndexOf("s7dcl", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? formats.FirstOrDefault(f => f.IndexOf("simatic", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? formats.FirstOrDefault(f => f.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? formats.FirstOrDefault();
        }

        /// <summary>
        /// 节点的强类型子节点。刻意不用通用反射桥（GetComposition）：它返回的是会被 Openness 立刻
        /// 回收的临时代理，经它拿到的对象第一次用就抛 "Access to a disposed object"。强类型组合是稳的。
        /// </summary>
        private static List<VcNode> VciTypedChildren(VcNode node)
        {
            var kids = new List<VcNode>();
            string dir = node.RelDir ?? "";

            void Add(IEngineeringObject o, string subDir, bool descendable)
            {
                kids.Add(new VcNode { Obj = o, Label = node.Label + "/" + VciObjName(o), RelDir = subDir, Descendable = descendable });
            }

            string Under(string name)
            {
                string part = SanitizeVciPathPart(name);
                if (string.IsNullOrEmpty(dir)) return part;
                if (string.Equals(Path.GetFileName(dir), part, StringComparison.OrdinalIgnoreCase)) return dir;
                return Path.Combine(dir, part);
            }

            switch (node.Obj)
            {
                case Project proj:
                    foreach (var dv in proj.Devices) Add(dv, dir, true);
                    foreach (var g in proj.DeviceGroups) Add(g, dir, true);
                    break;
                case DeviceUserGroup dg:
                    foreach (var dv in dg.Devices) Add(dv, Under(VciObjName(dg)), true);
                    foreach (var g in dg.Groups) Add(g, Under(VciObjName(dg)), true);
                    break;
                case Device dev:
                    foreach (var di in dev.DeviceItems) Add(di, Under(VciObjName(dev)), true);
                    break;
                case DeviceItem di2:
                    foreach (var sub in di2.DeviceItems) Add(sub, dir, true);
                    var sc = di2.GetService<SoftwareContainer>();
                    if (sc?.Software is IEngineeringObject sw) Add(sw, dir, true);
                    break;
                case PlcSoftware plc:
                    Add(plc.BlockGroup, Under(VciObjName(plc)), true);
                    Add(plc.TagTableGroup, Under(VciObjName(plc)), true);
                    Add(plc.TypeGroup, Under(VciObjName(plc)), true);
                    break;
                case PlcBlockGroup bg:
                    foreach (var b in bg.Blocks) Add(b, dir, false);
                    foreach (var g in bg.Groups) Add(g, Under(VciObjName(bg)), true);
                    break;
                case PlcTagTableGroup tg:
                    foreach (var t in tg.TagTables) Add(t, dir, false);
                    foreach (var g in tg.Groups) Add(g, Under(VciObjName(tg)), true);
                    break;
                case PlcTypeGroup ty:
                    foreach (var t in ty.Types) Add(t, dir, false);
                    foreach (var g in ty.Groups) Add(g, Under(VciObjName(ty)), true);
                    break;
            }
            return kids;
        }

        public (List<string> Lines, string Message, bool Ok) ConnectProjectToVciWorkspace(
            string workspaceName, bool dryRun, string deviceFilter, int maxObjects, bool walkTrace)
        {
            return _sta.Run(() =>
            {
                if (IsProjectNull())
                    throw new PortalException(PortalErrorCode.InvalidState, "ConnectProjectToWorkspace: no project is open.");

                var ws = FindVciWorkspace(RequireVciOnSta(), workspaceName);
                string wsName = ws.Name;
                string wsRootPath;
                try { wsRootPath = ws.RootPath?.FullName ?? "?"; } catch { wsRootPath = "?"; }

                // 一次抛异常的 Openness 调用会**作废**涉及的对象：一次 "The Object is not supported" 之后
                // workspace 句柄本身就死了。而「问一个不支持的对象」正是遍历的常态，所以每次失败后重取句柄。
                Workspace ReAcquire()
                {
                    _vciCached = null;
                    _vciOwnerProject = null;
                    _vciKeepAlive.Clear();
                    return FindVciWorkspace(RequireVciOnSta(), wsName);
                }

                var lines = new List<string>();
                int mapped = 0, already = 0, failed = 0, unsupported = 0, visited = 0;
                bool truncated = false;

                var stack = new Stack<VcNode>();
                stack.Push(new VcNode
                {
                    Obj = (IEngineeringObject)_project!,
                    Label = VciObjName((IEngineeringObject)_project!),
                    RelDir = "",
                    Descendable = true,
                });

                while (stack.Count > 0)
                {
                    if (visited >= maxObjects) { truncated = true; break; }
                    var node = stack.Pop();
                    visited++;

                    if (walkTrace) _logger?.LogInformation("[VCI-walk] #{0} {1} :: {2}", visited, node.Obj.GetType().Name, node.Label);

                    if (!string.IsNullOrWhiteSpace(deviceFilter)
                        && node.Obj is Device
                        && !string.Equals(VciObjName(node.Obj), deviceFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    IList<string> formats;
                    try
                    {
                        var f = ws.GetSupportedFileFormats(node.Obj);
                        formats = f == null ? new List<string>() : f.ToList();
                    }
                    catch (Exception ex)
                    {
                        formats = new List<string>();
                        if (walkTrace) _logger?.LogInformation("[VCI-walk]     query threw: {0}", FlattenVci(ex.Message));
                        ws = ReAcquire();
                    }

                    if (formats.Count > 0)
                    {
                        string fmt = PreferredVciFormat(formats) ?? formats[0];
                        string name = SanitizeVciPathPart(VciObjName(node.Obj));

                        MappedObject? existing = null;
                        try { existing = ws.MappedObjects.Find(node.Obj); }
                        catch { ws = ReAcquire(); }

                        if (existing != null)
                        {
                            already++;
                            lines.Add(node.Label + " | already mapped");
                            continue;
                        }

                        if (dryRun)
                        {
                            mapped++;
                            lines.Add(node.Label + " | would map | format=" + fmt
                                      + " | dir=" + (string.IsNullOrEmpty(node.RelDir) ? "<root>" : node.RelDir));
                        }
                        else
                        {
                            try
                            {
                                // ExportObject —— 不是 ConnectObject —— 才是「映射」这个动作：它写出文本文件
                                // 并建立映射。ConnectObject 只是把对象绑到**已存在**的文件（否则 "Missing
                                // Mandatory files"），且拒绝相对路径。子目录在本版本会被拒
                                // （"Relative Directory Path is Invalid"），故回退到 workspace 根下的扁平布局，
                                // 把工程路径折进文件名以免撞名。
                                string rel = node.RelDir ?? "";
                                bool flat = false;
                                if (!string.IsNullOrEmpty(rel))
                                {
                                    string abs = Path.Combine(wsRootPath, rel);
                                    try
                                    {
                                        Directory.CreateDirectory(abs);
                                        ws.ExportObject(node.Obj, new DirectoryInfo(abs), name, fmt);
                                    }
                                    catch (Exception subEx)
                                    {
                                        if (walkTrace) _logger?.LogInformation("[VCI-walk]     subdir refused ({0}) -> flat", FlattenVci(subEx.Message));
                                        ws = ReAcquire();
                                        flat = true;
                                    }
                                }
                                else flat = true;

                                if (flat)
                                {
                                    string flatName = SanitizeVciPathPart(
                                        (string.IsNullOrEmpty(rel) ? "" : rel.Replace(Path.DirectorySeparatorChar, '_') + "_")
                                        + VciObjName(node.Obj));
                                    ws.ExportObject(node.Obj, new DirectoryInfo(wsRootPath), flatName, fmt);
                                    name = flatName;
                                }
                                mapped++;
                                lines.Add(node.Label + " | mapped | format=" + fmt
                                          + " | dir=" + (string.IsNullOrEmpty(node.RelDir) ? "<root>" : node.RelDir));
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                lines.Add(node.Label + " | FAILED: " + FlattenVci(ex.Message)
                                          + (ex.InnerException != null ? " || inner: " + FlattenVci(ex.InnerException.Message) : ""));
                                ws = ReAcquire();
                            }
                        }
                        continue;   // coarse-first：已映射的对象连带覆盖其子树
                    }

                    if (!node.Descendable)
                    {
                        unsupported++;
                        lines.Add(node.Label + " | not supported by VCI (" + node.Obj.GetType().Name + ")");
                        continue;
                    }

                    List<VcNode> kids;
                    try { kids = VciTypedChildren(node); }
                    catch (Exception ex)
                    {
                        kids = new List<VcNode>();
                        lines.Add(node.Label + " | could not enumerate children: " + FlattenVci(ex.Message));
                    }
                    foreach (var kid in kids) stack.Push(kid);
                }

                string head = dryRun
                    ? string.Format("DRY RUN - nothing was mapped. {0} object(s) would be mapped into workspace '{1}' ({2}); "
                                    + "{3} already mapped, {4} not supported by VCI, {5} tree nodes visited.{6} "
                                    + "Call again with dryRun=false to map them.",
                                    mapped, wsName, wsRootPath, already, unsupported, visited,
                                    truncated ? " ** stopped at maxObjects - raise maxObjects for full coverage **" : "")
                    : string.Format("Workspace '{0}' ({1}): {2} newly mapped, {3} already mapped, {4} failed, "
                                    + "{5} not supported by VCI, {6} tree nodes visited.{7} "
                                    + "Next: SyncVersionControlWorkspace(direction='ProjectToWorkspace', dryRun=false), then git commit. "
                                    + "Save the project to persist the mappings.",
                                    wsName, wsRootPath, mapped, already, failed, unsupported, visited,
                                    truncated ? " ** stopped at maxObjects - raise maxObjects for full coverage **" : "");

                return (lines, head, failed == 0);
            });
        }
    }
}
#endif
