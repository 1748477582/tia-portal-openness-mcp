using System;
using System.IO;
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

            // ---- 回归：重定向在「环境变量大小写仅差」时必须优雅降级，不得抛异常 ----
            // 故障本体：ProcessStartInfo.EnvironmentVariables 在 **.NET Framework 4.x** 上是
            // 大小写不敏感的 StringDictionary。Windows 环境变量大小写不敏感，Git Bash / MSYS /
            // 部分 CI runner 会同时导出 HTTP_PROXY 与 http_proxy；复制父环境后，索引器赋值
            // psi.EnvironmentVariables[RedirectGuardVar] = "1" 会抛 ArgumentException
            // （"已添加项"），把一次本可正常工作的重定向变成 FATAL（真机实测崩溃点）。
            //
            // 注意：本离线套件跑在 **net8.0**，而 .NET Core 的 EnvironmentVariables 是
            // 大小写**敏感**的 IDictionary —— 该故障在 net8.0 上无法复现（实测 no throw）。
            // 所以这里不能用运行时重现，只能断言源码层面的保证：
            //   (1) TryRedirect 把重定向包在 try/catch 里，异常降级为 log + return false；
            //   (2) 赋值前先 Remove 同键，避免大小写冲突。
            // 并且仍然断言 TryRedirect 在运行时永不抛（不受 TFM 影响的契约）。
            VerifyRedirectGuardIsPresentInSource();
            T.Check("TryRedirect never throws (TFM-independent contract)",
                    NoThrow(() => EngineRouter.TryRedirect(18, new[] { "--version" }, _ => { }, out _)));
        }

        /// <summary>
        /// 断言 EngineRouter.cs 仍带「重定向异常降级」守卫。用源码断言而非运行时重现，原因见调用处：
        /// 该故障只在 .NET Framework 上发生，而离线套件是 net8.0，无法在运行时复现。
        /// </summary>
        private static void VerifyRedirectGuardIsPresentInSource()
        {
            string? path = FindSourceFile("Siemens/EngineRouter.cs");
            if (path == null)
            {
                T.Check("EngineRouter.cs found for source assertion", false,
                        "could not locate source under the repo tree");
                return;
            }
            string src = File.ReadAllText(path);

            T.Check("redirect is wrapped in try/catch",
                    src.Contains("catch (Exception ex)", StringComparison.Ordinal)
                    && src.Contains("staying in this exe.", StringComparison.Ordinal));
            T.Check("guard key removed before assignment (case-collision safe)",
                    src.Contains("EnvironmentVariables.Remove(RedirectGuardVar)", StringComparison.Ordinal));
        }

        /// <summary>从测试程序集位置向上找仓库里的源码文件（离线套件无 repo-root 注入）。</summary>
        private static string? FindSourceFile(string relativeUnderSrc)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName,
                    "tools", "tiaportal-mcp", "src", "TiaMcpServer", relativeUnderSrc);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static bool NoThrow(Func<object?> f)
        {
            try { f(); return true; }
            catch { return false; }
        }
    }
}
