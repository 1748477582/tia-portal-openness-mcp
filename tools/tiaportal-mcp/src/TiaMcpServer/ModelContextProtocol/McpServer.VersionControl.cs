#if !TIA_V18
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Partial: 版本控制接口（VCI）工具族 —— 把 TIA 工程变成 Git 能 diff/提交的文本树。
    ///
    /// 🔴 **V20+ 专属**：V18 的 Openness 缺 VCI 的完整 API（`MappedObject`/`GetSupportedFileFormats`/
    /// `ExportObject` 等），故整个文件 `#if !TIA_V18` 守卫，与仓内 23 个 Unified 工具同一约定。
    ///
    /// 本仓适配：上游把 Openness 访问直接写在工具方法里；本仓所有 Openness 访问必须在 PortalSta 线程内
    /// 完成且不得外泄 RCW，所以这里只做参数整理与响应组装，实际动作全在 `Portal.*` 的 `_sta.Run` 里。
    /// </summary>
    public static partial class McpServer
    {
        [McpServerTool(Name = "GetVersionControlWorkspaces"), Description(
            "[L1][VersionControl] List this project's version control (VCI) workspaces: name, folder on disk, "
            + "language, and how many objects are mapped. A workspace is the plain-text mirror of the project "
            + "that Git can actually diff and commit. Read-only. Requires an open project (V20+).")]
        public static ResponseStringList GetVersionControlWorkspaces()
        {
            try
            {
                var r = Portal.ListVciWorkspaces();
                return new ResponseStringList
                {
                    Message = r.Message,
                    Items = r.Lines,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true },
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, "GetVersionControlWorkspaces failed: " + pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, "GetVersionControlWorkspaces failed: " + ex.Message + McpHints.Recovery(ex));
            }
        }

        [McpServerTool(Name = "CreateVersionControlWorkspace"), Description(
            "[L2][VersionControl] Create a VCI workspace pointing at a folder on disk — normally the working "
            + "tree of a Git repository, so every synchronized export lands where Git can commit it. "
            + "Creating the workspace does NOT map any objects into it — call ConnectProjectToWorkspace "
            + "afterwards to map the whole project (or one device) automatically. "
            + "Requires an open project (V20+).")]
        public static ResponseMessage CreateVersionControlWorkspace(
            [Description("workspaceName: name shown in the TIA project tree, e.g. 'git'.")] string workspaceName,
            [Description("folderPath: existing folder the text files are written to, e.g. 'D:\\repos\\my-project'. Use your Git working tree.")] string folderPath)
        {
            try
            {
                var msg = Portal.CreateVciWorkspace(workspaceName, folderPath);
                return new ResponseMessage
                {
                    Message = msg,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true },
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, "CreateVersionControlWorkspace failed: " + pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, "CreateVersionControlWorkspace failed: " + ex.Message + McpHints.Recovery(ex));
            }
        }

        [McpServerTool(Name = "GetVersionControlStatus"), Description(
            "[L1][VersionControl] Per-object status of a VCI workspace: which mapped objects differ between the "
            + "TIA project and the text files on disk. This is the input for a change log — it names exactly what "
            + "changed before you commit. Read-only, changes nothing. "
            + "Status values: Equal (in sync), Unequal (project and file differ), WorkspaceFileMissing (never exported), Unknown.")]
        public static ResponseStringList GetVersionControlStatus(
            [Description("workspaceName: which workspace. Empty = the first one in the project.")] string workspaceName = "",
            [Description("changedOnly: default true — list only objects that are NOT in sync. false lists every mapped object.")] bool changedOnly = true)
        {
            try
            {
                var r = Portal.GetVciStatus(workspaceName, changedOnly);
                return new ResponseStringList
                {
                    Message = r.Message,
                    Items = r.Lines,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true },
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, "GetVersionControlStatus failed: " + pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, "GetVersionControlStatus failed: " + ex.Message + McpHints.Recovery(ex));
            }
        }

        [McpServerTool(Name = "SyncVersionControlWorkspace"), Description(
            "[L1][VersionControl] Synchronize a VCI workspace. direction='ProjectToWorkspace' writes the TIA "
            + "project's objects out as text files (do this before `git commit`); 'WorkspaceToProject' reads the "
            + "text files back INTO the project (do this after `git pull` / to restore a reviewed version). "
            + "DEFAULTS TO dryRun=true: the default call only reports what WOULD be synchronized. "
            + "WorkspaceToProject OVERWRITES blocks in the open project — compile and save afterwards.")]
        public static ResponseStringList SyncVersionControlWorkspace(
            [Description("direction: 'ProjectToWorkspace' (export, for committing) or 'WorkspaceToProject' (import, for restoring).")] string direction = "ProjectToWorkspace",
            [Description("workspaceName: which workspace. Empty = the first one in the project.")] string workspaceName = "",
            [Description("dryRun: DEFAULT true — only reports what would change. Pass false to actually synchronize.")] bool dryRun = true,
            [Description("changedOnly: default true — synchronize only objects whose status is not Equal. false also attempts objects whose status could not be determined.")] bool changedOnly = true)
        {
            try
            {
                var r = Portal.SyncVciWorkspace(direction, workspaceName, dryRun, changedOnly);
                return new ResponseStringList
                {
                    Message = r.Message,
                    Items = r.Lines,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = r.Ok },
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, "SyncVersionControlWorkspace failed: " + pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, "SyncVersionControlWorkspace failed: " + ex.Message + McpHints.Recovery(ex));
            }
        }

        [McpServerTool(Name = "ConnectProjectToWorkspace"), Description(
            "[L2][VersionControl] Put a WHOLE project under version control automatically - no TIA UI clicks. "
            + "Walks the project tree, asks every object whether VCI can map it (Workspace.GetSupportedFileFormats) "
            + "and maps each supported object with Workspace.ConnectObject. COARSE-FIRST: when a device or PLC "
            + "software object is mappable as one unit it is mapped whole and its children are not visited, so you "
            + "get the fewest mappings that still cover everything. Objects VCI does not support (typically hardware "
            + "configuration) are reported, never silently dropped. "
            + "DEFAULTS TO dryRun=true: the default call only reports what it WOULD map. "
            + "After a real run call SyncVersionControlWorkspace(ProjectToWorkspace, dryRun=false), then git commit.")]
        public static ResponseStringList ConnectProjectToWorkspace(
            [Description("workspaceName: which workspace to map into. Empty = the first one in the project.")] string workspaceName = "",
            [Description("dryRun: DEFAULT true - reports what would be mapped and changes nothing. Pass false to actually map.")] bool dryRun = true,
            [Description("deviceFilter: map only this device (exact name, e.g. 'PLC_1'). Empty = the whole project.")] string deviceFilter = "",
            [Description("maxObjects: safety cap on how many tree nodes are visited. Default 3000.")] int maxObjects = 3000,
            [Description("walkTrace: log one line per visited node. Diagnostic only.")] bool walkTrace = false)
        {
            try
            {
                var r = Portal.ConnectProjectToVciWorkspace(workspaceName, dryRun, deviceFilter, maxObjects, walkTrace);
                return new ResponseStringList
                {
                    Message = r.Message,
                    Items = r.Lines,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = r.Ok },
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, "ConnectProjectToWorkspace failed: " + pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, "ConnectProjectToWorkspace failed: " + ex.Message + McpHints.Recovery(ex));
            }
        }
    }
}
#endif
