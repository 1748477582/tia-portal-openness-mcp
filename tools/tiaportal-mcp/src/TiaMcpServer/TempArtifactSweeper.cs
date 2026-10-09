using System;
using System.IO;
using System.Linq;

namespace TiaMcpServer
{
    /// <summary>
    /// 清理本服务器自己留在 %TEMP% 里的陈旧产物。
    ///
    /// 为什么需要：导出块、写 SCL、回读接口、导入前的块备份都把工程内容写进临时目录，其中包含
    /// 块源码与接口（受 know-how 保护的块也在内）。这些目录带唯一后缀、跨会话不会被复用，却也不会
    /// 自己消失 —— 于是一个装过几十个工程的机器，%TEMP% 里会长期躺着若干份工程数据副本。
    ///
    /// 只清理由本进程创建、带唯一后缀的目录/文件（OwnPrefixes）。固定名的目录（例如 tia_mcp_scl，
    /// 按块名存放导出的 SCL）一律不动，别人的同名文件更不能碰 —— 宁可多留，也不误删。
    /// </summary>
    internal static class TempArtifactSweeper
    {
        private static readonly string[] OwnDirPrefixes =
        {
            "tia_impact_",              // AnalyzeBlockImpact 的临时导出
            "tia_iface_",               // 接口回读
            "TiaMcpServer_Export_",     // ExportBlock / ExportBlocks 的临时目录
            "tia_mcp_backup_",          // 覆盖前的块备份（若存在）
        };

        private static readonly string[] OwnFilePrefixes =
        {
            "tia_mcp_import_",           // 导入用的临时 XML
        };

        /// <summary>删掉超过 olderThanHours 小时的自家临时产物，返回删除项数。不抛异常：
        /// 清理是锦上添花，绝不能因为它把服务器启动搞挂。</summary>
        public static int Sweep(int olderThanHours, Action<string>? log)
        {
            int removed = 0;
            try
            {
                var root = Path.GetTempPath();
                var cutoff = DateTime.Now.AddHours(-Math.Max(1, olderThanHours));

                foreach (var dir in Directory.GetDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (!OwnDirPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    if (Directory.GetLastWriteTimeUtc(dir) > cutoff) continue;
                    try { Directory.Delete(dir, true); removed++; }
                    catch (Exception ex) { log?.Invoke("temp sweep: could not delete " + name + ": " + ex.Message); }
                }

                foreach (var file in Directory.GetFiles(root))
                {
                    var name = Path.GetFileName(file);
                    if (!OwnFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    try { File.Delete(file); removed++; }
                    catch (Exception ex) { log?.Invoke("temp sweep: could not delete " + name + ": " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("temp sweep skipped: " + ex.Message);
            }
            return removed;
        }
    }
}