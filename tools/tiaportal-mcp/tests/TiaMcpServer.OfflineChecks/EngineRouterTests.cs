using System;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// EngineRouter 的重引号逻辑：Windows 下把参数拼成命令行时，空格 / 引号 / **尾随反斜杠**
    /// 三类字符各有各的规则，写错了只在"路径刚好带空格"时炸，静默且难查 —— 属于典型的
    /// 「只有会失败的用例盯得住」的地方。
    /// </summary>
    internal static class EngineRouterTests
    {
        public static void Run()
        {
            // ---- 不需要引号的原样放行 ----
            T.Eq("plain arg unchanged", "abc", EngineRouter.QuoteArgs(new[] { "abc" }));
            T.Eq("plain backslash untouched", "a\\b", EngineRouter.QuoteArgs(new[] { "a\\b" }));
            T.Eq("empty array -> empty string", "", EngineRouter.QuoteArgs(Array.Empty<string>()));

            // ---- 需要引号 ----
            T.Eq("arg with space is quoted", "\"a b\"", EngineRouter.QuoteArgs(new[] { "a b" }));
            T.Eq("arg with tab is quoted", "\"a\tb\"", EngineRouter.QuoteArgs(new[] { "a\tb" }));
            T.Eq("empty arg -> empty quotes", "\"\"", EngineRouter.QuoteArgs(new[] { "" }));

            // ---- 引号与反斜杠的转义规则（反斜杠只在引号前需要成对加倍）----
            T.Eq("embedded quote escaped", "\"a\\\"b\"", EngineRouter.QuoteArgs(new[] { "a\"b" }));
            // 尾随反斜杠只在**参数被引号包起来时**才需要成对加倍（Windows 命令行解析规则）：
            T.Eq("trailing backslash doubled inside a quoted arg", "\"a b\\\\\"", EngineRouter.QuoteArgs(new[] { "a b\\" }));
            T.Eq("unquoted trailing backslash passes through", "a\\", EngineRouter.QuoteArgs(new[] { "a\\" }));

            // ---- 多个参数以空格连接，各自按需引号 ----
            T.Eq("multiple args joined", "a \"b c\"", EngineRouter.QuoteArgs(new[] { "a", "b c" }));

            // ---- 反向哨兵：它确实在做事，不是恒等返回 ----
            T.Check("QuoteArgs is not a no-op",
                    EngineRouter.QuoteArgs(new[] { "a b" }) != "a b");

            // ---- 编译期版本常量必须落在已知集合内 ----
            var v = EngineRouter.CompiledTiaMajorVersion;
            T.Check("CompiledTiaMajorVersion in {18,20,21}", v == 18 || v == 20 || v == 21, $"got {v}");

            // ---- 在测试宿主这种目录布局下不该抛异常（找不到兄弟 exe 就返回 null）----
            T.Check("FindSiblingExe is null-safe",
                    NoThrow(() => EngineRouter.FindSiblingExe(18)));
        }

        private static bool NoThrow(Func<object?> f)
        {
            try { f(); return true; }
            catch { return false; }
        }
    }
}
