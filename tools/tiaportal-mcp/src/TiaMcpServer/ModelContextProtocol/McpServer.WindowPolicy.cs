using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TiaMcpServer.ModelContextProtocol
{
    // ───────────────────────────────────────────────────────────────────────────
    // "改程序必须有窗口" —— 用户 2026-10-28 的硬要求，原话：
    //   "我要的是改程序所有内容都显示窗口"
    //
    // 为什么做成注册处的包装层而不是在每个工具里各加一句：
    //   写工具 60+ 个，逐个加必然漏；漏掉的那个会在无窗口实例里悄悄改用户程序。
    //   包在 WrapTools 这一处是结构性的 —— 进了工具表就必然过这一层。
    //
    // 判定口径（只拦"改程序内容"，不拦本机文件与在线操作）：
    //   - 命中 WriteTools 名单 且
    //   - 当前实例是引擎自建的无窗口实例（ConnectMode=isolated/new-instance 且 withUserInterface=false）
    //   ⇒ 拒绝，并告诉调用方怎么切到有窗口模式。
    //   挂接用户自己的 TIA（attached）时用户本来就看得见，直接放行。
    // ───────────────────────────────────────────────────────────────────────────
    public static partial class McpServer
    {
        /// <summary>会改动工程内容的工具（块/类型/变量表/技术对象/硬件组态/HMI/报警）。
        /// 刻意**不含**：导出与落盘类（SaveExport/Export*）、本机文件（WritePlcSclSourceFile）、
        /// 项目存档与另存（ArchiveProject/SaveAsProject）、在线操作与下载（GoOnline/GoOffline/DownloadToPlc）、
        /// 在线改值（SetWatchTableModifyValue）、Windows 组与版本控制工作区、以及 CreateProject（新工程无既有内容可毁）。</summary>
        public static readonly HashSet<string> WriteTools = new HashSet<string>(StringComparer.Ordinal)
        {
            // 块 / 类型 / 变量表 / 技术对象 / 外部源
            "ImportBlock", "ImportBlocksFromDirectory", "ImportBlockInterface", "ImportBlocksFromDocuments",
            "ImportFromDocuments", "ImportPlcProgramFromDirectory", "ImportPlcExternalSource",
            "ImportType",
            "ImportPlcTagTable", "ImportPlcTagTablesFromDirectory",
            "ImportTechnologyObject", "ImportTechnologyObjectsFromDirectory",
            "ImportMasterCopyFromGlobalLibrary",
            "DeleteBlock", "DeletePlcBlock", "DeletePlcType", "DeletePlcTagTable", "DeletePlcExternalSource",
            "SetBlockNumber", "MoveBlockToGroup", "MoveBlocksToGroup", "CreatePlcBlockGroup",
            "RegenerateBlockFromSource", "RepairAndReimportBlock", "GenerateBlocksFromExternalSource",
            "PlcBuildAndImport",
            "SeedProjectFromReference",

            // 硬件组态 / 网络 / 驱动
            "AddDevice", "AddDeviceWithFallback", "AddDriveComponent", "AddGsdDeviceWithProbe",
            "AddHardwareCatalogDeviceWithProbe", "PlugDeviceItem",
            "SetDeviceItemAttribute", "SetDeviceItemIoAddress", "SetCpuCommonSettings",
            "SetDriveParameter", "SetDriveTelegram", "SetPutGetAccess", "SetOpcUaInterfaceEnabled",
            "ImportOpcUaInterface", "AttachDeviceNodeToSubnet", "ConnectDeviceNodesToProfinetSubnet",
            "EnsureSubnet",

            // HMI / 报警
            "EnsureClassicHmiConnection", "EnsureStartStopUnifiedHmi",
            "EnsureUnifiedHmiConnection", "EnsureUnifiedHmiScreen", "EnsureUnifiedHmiScreenItem",
            "EnsureUnifiedHmiTag", "EnsureUnifiedHmiTagTable",
            "EnsureUnifiedHmiButtonAction", "EnsureUnifiedHmiButtonEventHandler", "EnsureUnifiedHmiDynamization",
            "ApplyUnifiedHmiLayout", "ApplyUnifiedHmiScreenDesignJson", "ApplyUnifiedHmiTheme",
            "SetUnifiedHmiButtonEventScriptCode",
            "ImportHmiConnection", "ImportHmiScreen", "ImportHmiScreensFromDirectory",
            "ImportHmiTagTable", "ImportHmiTagTablesFromDirectory",
            "ImportAlarmClasses", "ImportAlarmTextLists"
        };

        /// <summary>当前实例是否"引擎自建且无窗口"。</summary>
        public static bool IsHeadlessInstance()
        {
            try { return Portal.IsHeadlessInstance; }
            catch { return false; }   // 拿不到状态就不要拦，交给工具自己报错
        }

        /// <summary>在 WrapTools 的唯一入口上再套一层：写操作 + 无窗口实例 = 直接拒绝。</summary>
        public static IList<McpServerTool> WrapWithWindowPolicy(IList<McpServerTool> tools)
        {
            if (tools == null) return new List<McpServerTool>();
            var outList = new List<McpServerTool>(tools.Count);
            foreach (var t in tools)
            {
                if (t == null) continue;
                outList.Add(new WindowPolicyTool(t));
            }
            return outList;
        }
    }

    internal sealed class WindowPolicyTool : McpServerTool
    {
        private readonly McpServerTool _inner;

        public WindowPolicyTool(McpServerTool inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public override Tool ProtocolTool => _inner.ProtocolTool;

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            var name = ProtocolTool?.Name ?? string.Empty;

            if (McpServer.WriteTools.Contains(name) && McpServer.IsHeadlessInstance())
            {
                // 不要 throw：异常经 SDK 转成结果时会丢正文，客户端只看到"出错了"却不知道怎么办。
                return new ValueTask<CallToolResult>(TextError(
                    $"'{name}' 会改动程序内容，但当前 TIA 实例是【无窗口模式】——本工具不会在你看不见的窗口里改程序。"
                    + "\n做法：先 Disconnect，再用 ConnectIsolated(withUserInterface=true) 重新连接（会打开可见的 TIA 窗口），然后重试本操作。"
                    + "\n只读操作（读结构、读块、导出、体检）不受此限制，可继续用无窗口实例。"));
            }

            return _inner.InvokeAsync(request, cancellationToken);
        }

        /// <summary>带正文的错误结果：客户端必须能读到"为什么被拒、该怎么改"。</summary>
        private static CallToolResult TextError(string message) =>
            new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = message } }
            };
    }
}
