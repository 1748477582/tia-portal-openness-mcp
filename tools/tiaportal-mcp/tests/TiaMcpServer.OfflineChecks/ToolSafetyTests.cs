using System;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// 工具风险分级（ToolSafety）用例。它唯一的产出是 FindTools 里给人看的那行标注，所以错了不会
    /// 崩、只会让人被误导 —— 例如把 Export* 标成"会改工程数据"，读者就再也不信这行标注了。
    /// 正反两向都钉：真正危险的必须标出来，看起来无害的不能被误标。
    /// </summary>
    internal static class ToolSafetyTests
    {
        public static void Run()
        {
            // ---- 真危险的：必须直呼其名（动运行中 CPU / 删工程数据 / 关掉用户开着的工程）----
            T.Check("RISK: DownloadToPlc", ToolSafety.Classify("DownloadToPlc").DirectOnly);
            T.Check("RISK: GoOnline", ToolSafety.Classify("GoOnline").DirectOnly);
            T.Check("RISK: CloseProject (closes the user's project)",
                ToolSafety.Classify("CloseProject").DirectOnly);
            T.Check("RISK: Disconnect", ToolSafety.Classify("Disconnect").DirectOnly);
            T.Check("RISK: DeleteBlock", ToolSafety.Classify("DeleteBlock").DirectOnly);
            T.Check("RISK: DeletePlcBlock", ToolSafety.Classify("DeletePlcBlock").DirectOnly);
            T.Check("RISK: DeletePlcType", ToolSafety.Classify("DeletePlcType").DirectOnly);
            T.Check("RISK: DeletePlcTagTable", ToolSafety.Classify("DeletePlcTagTable").DirectOnly);
            T.Check("RISK: RegenerateBlockFromSource (overwrites a block)",
                ToolSafety.Classify("RegenerateBlockFromSource").DirectOnly);

            // ---- 读类工具：只读、幂等、不碰运行中 CPU，且绝不能被标成 RISK ----
            foreach (var readOnly in new[] { "GetState", "GetProjectTree", "DescribeBlockLogic",
                                             "GetCrossReferences", "AnalyzeBlockImpact", "CheckDownloadReadiness" })
            {
                var i = ToolSafety.Classify(readOnly);
                T.Check("read-only: " + readOnly, i.ReadOnly && !i.Destructive && !i.DirectOnly);
            }

            // ---- 一个"读工程但写文件"的工具：既不是只读，也不该被说成改工程数据 ----
            // Export* 的自带描述写着 "Read-only against the TIA project"，标成 mutating 会自相矛盾。
            foreach (var export in new[] { "ExportPlcWatchTable", "ExportBlockSourceUtf8", "ExportDeviceAml" })
            {
                var i = ToolSafety.Classify(export);
                var label = ToolSafety.RiskLabel(i);
                T.Check("export writes a file but not the project: " + export,
                    !i.ReadOnly && !i.Destructive && label.Contains("writes a file"));
            }

            // ---- 写工程数据但不必直呼其名：标成"写/覆盖"即可 ----
            foreach (var writer in new[] { "SetDriveTelegram", "SetBlockNumber", "MoveBlocksToGroup",
                                           "SaveProject", "AutoClassifyBlocks" })
            {
                T.Check("writes engineering data: " + writer, ToolSafety.Classify(writer).Destructive);
            }

            // ---- 通用反射桥：能调任意公开方法 ⇒ 继承最坏情况；但读用是常态，所以不算 RISK ----
            foreach (var bridge in new[] { "InvokeObject", "InvokeService" })
            {
                var i = ToolSafety.Classify(bridge);
                T.Check("bridge inherits the worst case: " + bridge, i.Destructive && !i.DirectOnly);
            }

            // ---- 不碰 Portal 句柄的工具（检索/导出货架）----
            T.Check("FindTools is portal-free", !ToolSafety.Classify("FindTools").TouchesPortal);
            T.Check("GetExport is portal-free", !ToolSafety.Classify("GetExport").TouchesPortal);
            T.Check("GetState needs the portal", ToolSafety.Classify("GetState").TouchesPortal);

            // ---- ConnectIsolated 每次都新开一个实例 ⇒ 不幂等，必须说出来 ----
            T.Check("ConnectIsolated is not idempotent", !ToolSafety.Classify("ConnectIsolated").Idempotent);
            T.Check("ConnectIsolated is not read-only", !ToolSafety.Classify("ConnectIsolated").ReadOnly);

            // ---- 动词只在词边界生效："Settle…" 不是 "Set…" ----
            T.Check("word boundary: SettleAxis is not Set*", !ToolSafety.Classify("SettleAxis").Destructive);
            T.Check("word boundary: SetBlockNumber IS Set*", ToolSafety.Classify("SetBlockNumber").Destructive);

            // ---- 标注文案：非空，且只读工具必须写明 read-only ----
            T.Check("label: read-only tool", ToolSafety.RiskLabel(ToolSafety.Classify("GetState")).Contains("read-only"));
            T.Check("label: RISK tool mentions call-by-name",
                ToolSafety.RiskLabel(ToolSafety.Classify("DownloadToPlc")).Contains("call by name"));
            T.Check("label: never empty", ToolSafety.RiskLabel(ToolSafety.Classify("Whatever")).Length > 0);
            T.Check("label: unknown tool is not read-only", !ToolSafety.Classify("Whatever").ReadOnly);

            // ---- 联网工具单独标出来 ----
            T.Check("OpenWorld: ReadPlcLiveValuesOpcUa", ToolSafety.Classify("ReadPlcLiveValuesOpcUa").OpenWorld);
            T.Check("not OpenWorld: GetState", !ToolSafety.Classify("GetState").OpenWorld);
        }
    }
}
