using System.Collections.Generic;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// HmiScreenWalk 递归遍历 HMI 画面树（含嵌套画面组）。真正的回归点是：
    /// 以前只读根级 Screens，**组里的画面会被判"查不到"** —— 而这正是 PR #41 修的那类静默错误。
    /// 纯反射，所以能用假对象图喂它，不需要装 TIA。
    /// </summary>
    internal static class HmiScreenWalkTests
    {
        private sealed class Screen { public string? Name { get; set; } }

        private sealed class Group
        {
            public List<Screen> Screens { get; } = new List<Screen>();
            public List<Group> ScreenGroups { get; } = new List<Group>();
            public List<Group> Folders { get; } = new List<Group>();
        }

        // Classic HmiTarget: root.ScreenFolder 是"单个"文件夹对象，不是集合。
        private sealed class ClassicRoot
        {
            public Group ScreenFolder { get; set; } = new Group();
        }

        public static void Run()
        {
            // ---- Unified 形状：root.Screens + 嵌套 ScreenGroups ----
            var nested = new Group();
            nested.Screens.Add(new Screen { Name = "Nested1" });
            var root = new Group();
            root.Screens.Add(new Screen { Name = "Main" });
            root.ScreenGroups.Add(nested);

            var names = string.Join(",", HmiScreenWalk.ListNames(root));
            T.Contains("root screen listed", names, "Main");
            T.Contains("nested (ScreenGroups) screen listed", names, "Nested1");

            // 找到嵌套里的画面（大小写不敏感）
            T.Check("finds nested screen case-insensitively",
                    HmiScreenWalk.FindByName(root, "nested1") != null);

            // ---- Classic 形状：root.ScreenFolder（单个）+ 它的 Folders ----
            var sub = new Group();
            sub.Screens.Add(new Screen { Name = "Deep" });
            var classic = new ClassicRoot();
            classic.ScreenFolder.Screens.Add(new Screen { Name = "ClassicRoot" });
            classic.ScreenFolder.Folders.Add(sub);

            var cnames = string.Join(",", HmiScreenWalk.ListNames(classic));
            T.Contains("classic root screen listed", cnames, "ClassicRoot");
            T.Contains("classic nested (Folders) screen listed", cnames, "Deep");

            // ---- 边界：null / 不存在的名字，都不能抛、不能瞎给一个 ----
            T.Eq("null root -> 0 names", 0, HmiScreenWalk.ListNames(null).Count);
            T.Check("null root -> FindByName null", HmiScreenWalk.FindByName(null, "x") == null);
            T.Check("absent name -> null", HmiScreenWalk.FindByName(root, "does-not-exist") == null);
        }
    }
}
