using System;
using System.IO;
using TiaMcpServer;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// %TEMP% 清理器的用例。这段代码替用户删文件，所以判据必须钉死三件事：
    /// 只删「自家 + 带唯一后缀 + 超过时限」的；**新鲜目录绝不删**（本次会话正在用）；
    /// **固定名目录绝不删**（里面是按块名存的导出 SCL，可能是用户要留的）。
    /// 宁可多留，不可误删 —— 误删的代价远大于残留。
    /// </summary>
    internal static class TempArtifactSweeperTests
    {
        public static void Run()
        {
            var root = Path.GetTempPath();
            var stamp = DateTime.Now.ToString("HHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var staleDir = Path.Combine(root, "TiaMcpServer_Export_stale" + stamp);
            var staleImpact = Path.Combine(root, "tia_impact_stale" + stamp);
            var staleFile = Path.Combine(root, "tia_mcp_import_stale" + stamp + ".xml");
            var freshDir = Path.Combine(root, "TiaMcpServer_Export_fresh" + stamp);
            // 固定名目录：模拟 tia_mcp_scl 那类按块名存放的目录，必须活下来
            var fixedNameDir = Path.Combine(root, "tia_mcp_scl");
            // 别人的东西：前缀相近但不是我们的清单，绝不能删
            var foreignDir = Path.Combine(root, "tia_somethingelse_" + stamp);

            Directory.CreateDirectory(staleDir);
            Directory.CreateDirectory(staleImpact);
            Directory.CreateDirectory(freshDir);
            Directory.CreateDirectory(fixedNameDir);
            Directory.CreateDirectory(foreignDir);
            File.WriteAllText(staleFile, "<x/>");
            File.WriteAllText(Path.Combine(fixedNameDir, "FB_KeepMe.scl"), "keep");
            File.WriteAllText(Path.Combine(foreignDir, "data.bin"), "keep");

            var old = DateTime.UtcNow.AddHours(-48);
            Directory.SetLastWriteTimeUtc(staleDir, old);
            Directory.SetLastWriteTimeUtc(staleImpact, old);
            Directory.SetLastWriteTimeUtc(fixedNameDir, old);      // 旧但固定名 → 仍要保留
            Directory.SetLastWriteTimeUtc(foreignDir, old);
            File.SetLastWriteTimeUtc(staleFile, old);

            int removed = TempArtifactSweeper.Sweep(24, null);

            // 不断言"恰好删了 3 个"：%TEMP% 里可能还躺着此前真实运行留下的陈旧目录，那本来就该被清掉。
            // 真正要钉的是下面那几条逐项断言——新建的、别人家的、固定名的都必须活着。
            T.Check("sweep removed at least our 3 stale own artifacts", removed >= 3, "removed=" + removed);
            T.Check("stale export dir deleted", !Directory.Exists(staleDir));
            T.Check("stale impact dir deleted", !Directory.Exists(staleImpact));
            T.Check("stale import file deleted", !File.Exists(staleFile));

            T.Check("FRESH own dir kept (this session may be using it)", Directory.Exists(freshDir));
            T.Check("fixed-name dir kept even when old", Directory.Exists(fixedNameDir));
            T.Check("fixed-name content intact", File.Exists(Path.Combine(fixedNameDir, "FB_KeepMe.scl")));
            T.Check("foreign dir with similar prefix kept", Directory.Exists(foreignDir));
            T.Check("foreign content intact", File.Exists(Path.Combine(foreignDir, "data.bin")));

            // 清理测试自己造的东西
            try { Directory.Delete(freshDir, true); } catch { }
            try { Directory.Delete(foreignDir, true); } catch { }
            // 固定名目录只在为空时才删，避免动用户文件
            try { if (Directory.Exists(fixedNameDir) && Directory.GetFileSystemEntries(fixedNameDir).Length == 0) Directory.Delete(fixedNameDir); } catch { }
        }
    }
}