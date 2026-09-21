using System;
using System.Text;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// 大响应寄存的判定与切片。这块坏了不会崩：只会让某一页报废、或让调用方照着
    /// nextOffset 在同一位臵**无限打转**、或把「记错了 id」误报成「过期了」——
    /// 全是"坏了也悄无声息"的那一类，必须由会失败的用例盯住。
    /// </summary>
    internal static class ExportStoreTests
    {
        private static string Repeat(string unit, int times)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < times; i++) sb.Append(unit);
            return sb.ToString();
        }

        /// <summary>把一份内容按分页协议整份翻回来，拼起来必须与原文逐字相同。</summary>
        private static string PageThrough(string id, int pageSize)
        {
            var sb = new StringBuilder();
            int offset = 0;
            for (int guard = 0; guard < 10000; guard++)
            {
                var s = ExportStore.Slice(id, offset, pageSize);
                if (s.Error != null) return "<ERROR:" + s.Error + ">";
                sb.Append(s.Text);
                if (s.Eof) return sb.ToString();
                if (!s.NextOffset.HasValue) return "<NO-NEXT-OFFSET>";
                if (s.NextOffset.Value <= offset) return "<NO-PROGRESS>";   // 死循环哨兵
                offset = s.NextOffset.Value;
            }
            return "<TOO-MANY-PAGES>";
        }

        public static void Run()
        {
            var realNow = ExportStore.NowUtc;

            // ── 基本：寄存 + 头部切片 ───────────────────────────────────────
            ExportStore.ResetForTests();
            string content = Repeat("0123456789", 10);          // 100 字符
            var (id, head) = ExportStore.PutAndSlice("GetBlocks", "PLC_1", content, 30);

            T.Check("PutAndSlice returns a handle", !string.IsNullOrEmpty(id));
            T.Check("head slice has no error (atomic put+slice)", head.Error == null);
            T.Eq("head length honoured", 30, head.Returned);
            T.Eq("head total length", 100, head.TotalLength);
            T.Eq("head is the prefix", content.Substring(0, 30), head.Text);
            T.Check("head is not eof", !head.Eof);
            T.Eq("head nextOffset", 30, head.NextOffset ?? -1);

            // ── 分页拼回原文（逐字）────────────────────────────────────────
            T.Eq("paging reconstructs the original (page=7)", content, PageThrough(id, 7));
            T.Eq("paging reconstructs the original (page=1000)", content, PageThrough(id, 1000));
            T.Eq("paging reconstructs the original (page=1)", content, PageThrough(id, 1));

            // ── 越界一律夹紧，不抛异常 ──────────────────────────────────────
            var neg = ExportStore.Slice(id, -5, 10);
            T.Eq("negative offset clamps to 0", 0, neg.Offset);
            var past = ExportStore.Slice(id, 9999, 10);
            T.Eq("offset past end clamps to total", 100, past.Offset);
            T.Eq("past-end returns nothing", 0, past.Returned);
            T.Check("past-end is eof", past.Eof);
            var huge = ExportStore.Slice(id, 0, 10_000_000);
            T.Check("length is capped at MaxSliceChars",
                    huge.Returned <= ExportStore.MaxSliceChars, $"got {huge.Returned}");
            var zeroLen = ExportStore.Slice(id, 0, 0);
            T.Check("length 0 falls back to a real page", zeroLen.Returned > 0);

            // ── 代理对：不能切出半个字符，且必须前进 ────────────────────────
            ExportStore.ResetForTests();
            string emoji = "\U0001F600";                        // 2 个 char 的代理对
            string tricky = "ab" + emoji + "cd";                // a b [hi lo] c d
            string? tid = ExportStore.Put("LadTextRenderer", "", tricky);
            var cut = ExportStore.Slice(tid, 3, 1);             // 切点正好落在代理对中间
            T.Check("no lone surrogate in the slice",
                    cut.Text.Length != 1 || !char.IsSurrogate(cut.Text[0]),
                    $"got len={cut.Text.Length}");
            T.Check("slice always advances (no infinite paging)",
                    cut.NextOffset.HasValue && cut.NextOffset.Value > cut.Offset,
                    $"offset={cut.Offset} next={cut.NextOffset}");
            T.Eq("tricky content still pages back verbatim", tricky, PageThrough(tid!, 1));

            // ── 三态错误：unknown / evicted / expired 必须分得开 ────────────
            var unknown = ExportStore.Slice("ex_20200101000000_9999", 0, 10);
            T.Eq("never-issued id -> 'expired' (format+age)", "expired", unknown.Error);
            var bogus = ExportStore.Slice("not-an-id", 0, 10);
            T.Eq("malformed id -> 'unknown'", "unknown", bogus.Error);

            ExportStore.ResetForTests();
            string? did = ExportStore.Put("GetBlocks", "", "hello world");
            ExportStore.Delete(did);
            var evicted = ExportStore.Slice(did, 0, 5);
            T.Eq("deleted id -> 'evicted' (not unknown)", "evicted", evicted.Error);

            ExportStore.ResetForTests();
            string? cid = ExportStore.Put("GetBlocks", "", "hello world");
            int cleared = ExportStore.Clear(0);
            T.Eq("Clear(0) removes the handle", 1, cleared);
            T.Eq("cleared id -> 'evicted'", "evicted", ExportStore.Slice(cid, 0, 5).Error);

            ExportStore.ResetForTests();
            string? xid = ExportStore.Put("GetBlocks", "", "hello world");
            ExportStore.NowUtc = () => DateTime.UtcNow.AddHours(ExportStore.DefaultTtlHours + 1);
            T.Eq("past TTL -> 'expired'", "expired", ExportStore.Slice(xid, 0, 5).Error);
            ExportStore.NowUtc = realNow;                       // 复原，别污染后面的用例

            // ── 容量上限：无限塞不能把进程撑爆 ──────────────────────────────
            ExportStore.ResetForTests();
            string last = "";
            for (int i = 0; i < 40; i++) last = ExportStore.Put("GetBlocks", "t" + i, Repeat("x", 10));
            var (count, chars) = ExportStore.Stats();
            T.Eq("store is capped", 32, count);
            T.Check("newest handle survives eviction", !string.IsNullOrEmpty(last) && chars > 0);
            T.Check("just-inserted handle is still readable",
                    ExportStore.Slice(last, 0, 3).Error == null);

            // ── List 过滤 ──────────────────────────────────────────────────
            ExportStore.ResetForTests();
            ExportStore.Put("GetBlocks", "PLC_1", "a");
            ExportStore.Put("ExportBlocksAsDocuments", "PLC_2", "b");
            var onlyBlocks = ExportStore.List("getblocks", 10);
            T.Eq("List filters by tool (case-insensitive)", 1, onlyBlocks.Count);
            T.Eq("List returns newest first", "ExportBlocksAsDocuments",
                 ExportStore.List(null, 1)[0].Tool);

            ExportStore.ResetForTests();
        }
    }
}
