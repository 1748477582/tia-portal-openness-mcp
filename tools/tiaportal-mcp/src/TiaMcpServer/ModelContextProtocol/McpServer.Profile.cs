using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace TiaMcpServer.ModelContextProtocol
{
    // TIA_MCP_PROFILE=lite: expose only [L0]/[L1] tools (~40 essentials) instead of
    // ~200, so a small / non-expert model is not drowned in choices and hosts with a
    // tool cap (VS Code: 128) can enable everything. Opt-in via env var; full profile
    // (WithToolsFromAssembly) stays the default. All tools are static so no DI target
    // is needed.
    public static partial class McpServer
    {
        public static IList<McpServerTool> GetLiteTools()
        {
            return GetToolsByLayer(desc =>
                desc.StartsWith("[L0]", StringComparison.Ordinal) ||
                desc.StartsWith("[L1]", StringComparison.Ordinal));
        }

        /// <summary>全部工具（过滤条件恒真）。
        /// 之所以要它：大响应寄存的包装层（WrapWithResponseGuard）必须拿到**已构造好的工具对象**
        /// 才有东西可包，而 WithToolsFromAssembly 在 SDK 内部构造，包不进去 —— 所以全量档也改成
        /// 显式列表。
        /// ⚠ 列表来源必须与 WithToolsFromAssembly 覆盖的范围一致（都是 McpServer 这个 partial 上的
        /// [McpServerTool] 方法）；工具数由 manifest 生成时实测核对，掉一个立刻能看出来。</summary>
        public static IList<McpServerTool> GetAllTools()
        {
            return GetToolsByLayer(_ => true);
        }

        private static IList<McpServerTool> GetToolsByLayer(Func<string, bool> accept)
        {
            var tools = new List<McpServerTool>();
            foreach (var method in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() == null) continue;
                var desc = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                if (accept(desc))
                {
                    tools.Add(McpServerTool.Create(method));
                }
            }
            return tools;
        }

        public static bool IsLiteProfile()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("TIA_MCP_PROFILE")?.Trim(),
                "lite", StringComparison.OrdinalIgnoreCase);
        }
    }
}
