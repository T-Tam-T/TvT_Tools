using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TvT.EditorTools
{
    /// <summary>
    /// .unitypackage 结构查看 / 导入工具。
    ///
    /// 为什么需要它：Unity 自带的 Import Unity Package 窗口会按 GUID 去重——包里每个资源只要工程里
    /// 已存在相同 GUID，就整个从列表里剔除；全部被剔除时就只剩 "Nothing to import!"，既看不到文件树，
    /// 也没法覆盖重导。本窗口直接解析 .unitypackage（tar.gz）本体，绕过 Unity 的过滤，永远能显示
    /// 文件结构，并标注每个资源当前的状态：
    ///   [新增]      工程里没有这个 GUID，也没有同路径文件
    ///   [已存在]    工程里已有同 GUID 资源（显示它现在在哪）——这就是自带窗口说 nothing 的原因
    ///   [路径冲突]  目标路径被一个不同 GUID 的资源占着
    ///
    /// 菜单：Tools/Unity Package Inspector
    /// </summary>
    public class UnityPackageInspectorWindow : EditorWindow
    {
        const string MenuPath = "Tools/Unity Package Inspector";
        const string WindowTitle = "Unity Package Inspector";
        const string BackupDirName = "UnityPackageImportBackup";

        // ---------------------------------------------------------------- 数据

        class Entry
        {
            public string Guid;
            public string Path;
            public bool IsFolder;
            public long Size;
            public string MetaGuid;
            public string ExistingPath;     // 工程里同 GUID 资源路径（为空表示没有）
            public bool PathOccupied;       // 目标路径被别的 GUID 占着
            public bool Selected;

            public string Name
            {
                get
                {
                    if (string.IsNullOrEmpty(Path))
                        return string.Empty;
                    int idx = Path.LastIndexOf('/');
                    return idx >= 0 ? Path.Substring(idx + 1) : Path;
                }
            }

            public bool IsNew
            {
                get { return string.IsNullOrEmpty(ExistingPath) && !PathOccupied; }
            }
        }

        class Node
        {
            public string Name;
            public string Path;
            public bool IsFolder;
            public Entry Entry;
            public List<Node> Children = new List<Node>();
            public bool Expanded;
        }

        // ---------------------------------------------------------------- 状态

        string _packagePath = string.Empty;
        DefaultAsset _packageAsset;
        List<UnityPackageTar.Item> _items = new List<UnityPackageTar.Item>();
        List<Entry> _entries = new List<Entry>();
        Node _root;
        Vector2 _scroll;
        string _filter = string.Empty;
        bool _showOnlyMissing;
        string _status = string.Empty;
        MessageType _statusType = MessageType.Info;
        string _lastBackupDir = string.Empty;

        // ---------------------------------------------------------------- 入口

        [MenuItem(MenuPath)]
        static void OpenWindow()
        {
            UnityPackageInspectorWindow win = GetWindow<UnityPackageInspectorWindow>(false, WindowTitle, true);
            win.minSize = new Vector2(640, 420);
            win.Show();
        }

        [MenuItem("Assets/Unity Package Inspector", true)]
        static bool ValidateInspectSelected()
        {
            string path = GetSelectedPackagePath();
            return !string.IsNullOrEmpty(path);
        }

        [MenuItem("Assets/Unity Package Inspector")]
        static void InspectSelected()
        {
            string path = GetSelectedPackagePath();
            if (string.IsNullOrEmpty(path))
                return;
            UnityPackageInspectorWindow win = GetWindow<UnityPackageInspectorWindow>(false, WindowTitle, true);
            win.minSize = new Vector2(640, 420);
            win.SetPackage(path);
            win.Show();
        }

        static string GetSelectedPackagePath()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
                return string.Empty;
            string path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                return path;
            return string.Empty;
        }

        // ---------------------------------------------------------------- 解析

        void SetPackage(string path)
        {
            _packagePath = path ?? string.Empty;
            _items.Clear();
            _entries.Clear();
            _root = null;
            _status = string.Empty;

            if (string.IsNullOrEmpty(_packagePath))
            {
                _packageAsset = null;
                return;
            }

            // 工程内用相对路径，工程外 .unitypackage 用绝对路径
            if (!File.Exists(_packagePath))
            {
                string abs = ToAbsolute(_packagePath);
                if (!string.IsNullOrEmpty(abs) && File.Exists(abs))
                    _packagePath = abs;
            }

            // 让上方对象框跟随当前包
            if (_packageAsset == null)
            {
                string assetPath = _packagePath.Replace('\\', '/');
                int assetsIdx = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    _packageAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
                }
                else if (assetsIdx >= 0)
                {
                    _packageAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath.Substring(assetsIdx + 1));
                }
            }

            try
            {
                _items = UnityPackageTar.ReadItems(_packagePath);
                _entries = BuildEntries(_items);
                RefreshStatus();
                BuildTree();
                _status = string.Format("已解析：{0} 个资源 / {1} 个文件夹", CountAssets(), CountFolders());
                _statusType = MessageType.Info;
            }
            catch (Exception ex)
            {
                _items.Clear();
                _entries.Clear();
                _root = null;
                _status = "解析失败：" + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }

        static List<Entry> BuildEntries(List<UnityPackageTar.Item> items)
        {
            var list = new List<Entry>(items.Count);
            foreach (UnityPackageTar.Item item in items)
            {
                Entry e = new Entry();
                e.Guid = item.Guid;
                e.Path = item.Path;
                e.IsFolder = item.IsFolder;
                e.Size = item.Size;
                e.MetaGuid = item.MetaGuid;
                list.Add(e);
            }
            return list;
        }

        int CountAssets()
        {
            int n = 0;
            foreach (Entry e in _entries)
                if (!e.IsFolder)
                    n++;
            return n;
        }

        int CountFolders()
        {
            int n = 0;
            foreach (Entry e in _entries)
                if (e.IsFolder)
                    n++;
            return n;
        }

        void RefreshStatus()
        {
            foreach (Entry e in _entries)
            {
                e.ExistingPath = AssetDatabase.GUIDToAssetPath(e.Guid);
                if (e.ExistingPath == null)
                    e.ExistingPath = string.Empty;

                string abs = ToAbsolute(e.Path);
                e.PathOccupied = string.IsNullOrEmpty(e.ExistingPath)
                                 && !string.IsNullOrEmpty(abs)
                                 && (File.Exists(abs) || Directory.Exists(abs));

                if (!e.IsFolder)
                    e.Selected = e.IsNew;
            }
        }

        void BuildTree()
        {
            Node root = new Node();
            root.Name = "包内资源";
            root.IsFolder = true;
            root.Expanded = true;

            foreach (Entry e in _entries.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                string[] parts = e.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                Node cur = root;
                for (int i = 0; i < parts.Length; i++)
                {
                    bool last = i == parts.Length - 1;
                    string nodePath = string.Join("/", parts, 0, i + 1);

                    Node child = null;
                    for (int c = 0; c < cur.Children.Count; c++)
                    {
                        if (cur.Children[c].Name == parts[i])
                        {
                            child = cur.Children[c];
                            break;
                        }
                    }
                    if (child == null)
                    {
                        child = new Node();
                        child.Name = parts[i];
                        child.Path = nodePath;
                        child.IsFolder = !last || e.IsFolder;
                        cur.Children.Add(child);
                    }
                    if (last)
                    {
                        child.Entry = e;
                        child.IsFolder = e.IsFolder;
                    }
                    cur = child;
                }
            }

            SortChildren(root);
            _root = root;
        }

        static void SortChildren(Node node)
        {
            node.Children.Sort(delegate(Node a, Node b)
            {
                if (a.IsFolder != b.IsFolder)
                    return a.IsFolder ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (Node child in node.Children)
                SortChildren(child);
        }

        // ---------------------------------------------------------------- GUI

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DefaultAsset newAsset = (DefaultAsset)EditorGUILayout.ObjectField("包文件", _packageAsset, typeof(DefaultAsset), false);
            if (newAsset != _packageAsset)
            {
                _packageAsset = newAsset;
                string path = newAsset != null ? AssetDatabase.GetAssetPath(newAsset) : string.Empty;
                if (!string.IsNullOrEmpty(path))
                    SetPackage(path);
            }
            if (GUILayout.Button("选择…", GUILayout.Width(64)))
            {
                string path = EditorUtility.OpenFilePanel("选择 .unitypackage", "", "unitypackage");
                if (!string.IsNullOrEmpty(path))
                    SetPackage(path);
            }
            if (GUILayout.Button("重新扫描", GUILayout.Width(72)))
                SetPackage(_packagePath);
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(_packagePath))
            {
                EditorGUILayout.HelpBox("选择一个 .unitypackage 文件（可拖到上方对象框，或用「选择…」）。\n\n" +
                                        "本窗口直接读取包内文件，不经过 Unity 的 GUID 去重过滤，所以永远能显示完整文件结构。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(_packagePath, EditorStyles.miniLabel);

            // 统计
            int countNew = 0, countExist = 0, countConflict = 0;
            foreach (Entry e in _entries)
            {
                if (e.IsNew)
                    countNew++;
                else if (!string.IsNullOrEmpty(e.ExistingPath))
                    countExist++;
                else if (e.PathOccupied)
                    countConflict++;
            }

            int selectedCount = 0;
            foreach (Entry e in _entries)
                if (e.Selected)
                    selectedCount++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format("新增 {0}   ·   已存在(同 GUID) {1}   ·   路径冲突 {2}", countNew, countExist, countConflict));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format("已勾选 {0} 项", selectedCount), GUILayout.Width(90));
            using (new EditorGUI.DisabledGroupScope(selectedCount == 0))
            {
                if (GUILayout.Button("解压所选到文件夹…", GUILayout.Height(22)))
                    ExtractSelected();
                if (GUILayout.Button("把所选写入工程", GUILayout.Height(22)))
                    ImportSelected();
            }
            if (GUILayout.Button("静默导入(不弹窗)", GUILayout.Height(22)))
                SilentImport();
            if (GUILayout.Button("同款导入窗口（不过滤 GUID）…", GUILayout.Height(22)))
                UnityPackageImportWindow.Open(_packagePath);
            if (GUILayout.Button("备份同 GUID 资源并打开原生导入窗口", GUILayout.Height(22)))
                BackupConflictsAndOpenImportDialog();
            if (!string.IsNullOrEmpty(_lastBackupDir) && GUILayout.Button("打开备份目录", GUILayout.Width(110), GUILayout.Height(22)))
                EditorUtility.RevealInFinder(_lastBackupDir);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            string newFilter = EditorGUILayout.TextField("过滤", _filter);
            if (newFilter != _filter)
            {
                _filter = newFilter;
                _scroll = Vector2.zero;
            }
            _showOnlyMissing = EditorGUILayout.ToggleLeft("只看新增/冲突", _showOnlyMissing, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            // 文件树
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_root != null)
            {
                if (string.IsNullOrEmpty(_filter) && !_showOnlyMissing)
                {
                    DrawNode(_root, 0);
                }
                else
                {
                    foreach (Entry e in _entries)
                    {
                        if (!MatchFilter(e))
                            continue;
                        DrawEntryRow(e, 0, false);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        bool MatchFilter(Entry e)
        {
            if (_showOnlyMissing && !e.IsNew)
                return false;
            if (string.IsNullOrEmpty(_filter))
                return true;
            return e.Path != null && e.Path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void DrawNode(Node node, int depth)
        {
            if (node.Entry == null)
            {
                if (node.IsFolder)
                {
                    node.Expanded = EditorGUILayout.Foldout(node.Expanded, node.Name + "/", true);
                    if (node.Expanded)
                        foreach (Node child in node.Children)
                            DrawNode(child, depth + 1);
                }
                return;
            }

            if (node.IsFolder)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(6 + depth * 16);
                bool sel = EditorGUILayout.Toggle(node.Entry.Selected, GUILayout.Width(16));
                if (sel != node.Entry.Selected)
                    SetFolderSelected(node, sel);
                node.Expanded = EditorGUILayout.Foldout(node.Expanded, node.Name + "/", true);
                DrawEntryStatus(node.Entry);
                EditorGUILayout.EndHorizontal();
                if (node.Expanded)
                    foreach (Node child in node.Children)
                        DrawNode(child, depth + 1);
            }
            else
            {
                DrawEntryRow(node.Entry, depth, true);
            }
        }

        void DrawEntryRow(Entry e, int depth, bool withTreeIndent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6 + (withTreeIndent ? depth * 16 : 0));
            bool sel = EditorGUILayout.Toggle(e.Selected, GUILayout.Width(16));
            if (sel != e.Selected)
                e.Selected = sel;
            EditorGUILayout.LabelField(e.Name, GUILayout.MinWidth(160));
            if (e.Size > 0)
                EditorGUILayout.LabelField(FormatSize(e.Size), EditorStyles.miniLabel, GUILayout.Width(70));
            DrawEntryStatus(e);
            EditorGUILayout.EndHorizontal();
        }

        void DrawEntryStatus(Entry e)
        {
            Color old = GUI.color;
            if (!string.IsNullOrEmpty(e.ExistingPath))
            {
                GUI.color = new Color(1f, 0.85f, 0.35f);
                EditorGUILayout.LabelField("已存在: " + Shorten(e.ExistingPath, 90), EditorStyles.miniLabel);
            }
            else if (e.PathOccupied)
            {
                GUI.color = new Color(1f, 0.5f, 0.4f);
                EditorGUILayout.LabelField("路径冲突: " + Shorten(e.Path, 90), EditorStyles.miniLabel);
            }
            else
            {
                GUI.color = new Color(0.55f, 0.9f, 0.55f);
                EditorGUILayout.LabelField("新增", EditorStyles.miniLabel, GUILayout.Width(40));
            }
            GUI.color = old;
        }

        void SetFolderSelected(Node folder, bool selected)
        {
            if (folder.Entry != null)
                folder.Entry.Selected = selected;
            foreach (Node child in folder.Children)
                SetFolderSelected(child, selected);
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024f).ToString("0.0") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        }

        static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;
            return "…" + s.Substring(s.Length - max + 1);
        }

        // ---------------------------------------------------------------- 路径工具

        static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        /// <summary>"Assets/..." → 绝对路径；其他（Packages/…）返回空字符串。</summary>
        static string ToAbsolute(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
                return string.Empty;
            string normalized = projectRelativePath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return Path.Combine(ProjectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        }

        string PackageDisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(_packagePath))
                    return "package";
                return Path.GetFileNameWithoutExtension(_packagePath);
            }
        }

        List<Entry> SelectedEntries(bool assetsOnly)
        {
            var list = new List<Entry>();
            foreach (Entry e in _entries)
                if (e.Selected && (!assetsOnly || !e.IsFolder))
                    list.Add(e);
            return list;
        }

        /// <summary>把包内所有"被选中资源的祖先文件夹条目"也加进来，保证文件夹 .meta 一并写出。</summary>
        HashSet<string> ExpandSelectedGuids()
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in _entries)
                if (e.Selected)
                    selected.Add(e.Guid);

            foreach (Entry folder in _entries)
            {
                if (!folder.IsFolder || string.IsNullOrEmpty(folder.Path))
                    continue;
                string prefix = folder.Path + "/";
                foreach (Entry e in _entries)
                {
                    if (!e.Selected || string.IsNullOrEmpty(e.Path))
                        continue;
                    if (e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        selected.Add(folder.Guid);
                        break;
                    }
                }
            }
            return selected;
        }

        // ---------------------------------------------------------------- 操作：解压

        void ExtractSelected()
        {
            List<Entry> selected = SelectedEntries(false);
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("没有选中项", "请先勾选要解压的资源。", "好");
                return;
            }

            string dir = EditorUtility.OpenFolderPanel("选择解压到哪个文件夹", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(dir))
                return;

            string destRoot = Path.Combine(dir, PackageDisplayName);
            HashSet<string> guids = ExpandSelectedGuids();

            try
            {
                EditorUtility.DisplayProgressBar("解压 unitypackage", "正在写出文件…", 0.5f);
                int written = UnityPackageTar.Extract(_packagePath, destRoot, _items, guids);
                _status = string.Format("已解压 {0} 个资源到：{1}（含 .meta，GUID 保持不变）", written, destRoot);
                _statusType = MessageType.Info;
                Debug.Log("[UnityPackageInspector] " + _status);
                EditorUtility.RevealInFinder(destRoot);
            }
            catch (Exception ex)
            {
                _status = "解压失败：" + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ---------------------------------------------------------------- 操作：写入工程

        void ImportSelected()
        {
            List<Entry> selected = SelectedEntries(false);
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("没有选中项", "请先勾选要写入工程的资源。", "好");
                return;
            }

            var targets = new List<Entry>();
            var skippedExisting = new List<Entry>();
            var skippedNonAssets = new List<Entry>();

            foreach (Entry e in selected)
            {
                if (string.IsNullOrEmpty(ToAbsolute(e.Path)))
                {
                    skippedNonAssets.Add(e);   // 不在 Assets/ 下的（比如包内 Packages 路径）
                    continue;
                }
                if (!string.IsNullOrEmpty(e.ExistingPath))
                {
                    skippedExisting.Add(e);    // 同 GUID 已存在：默认跳过，避免把工程里的资源覆盖掉
                    continue;
                }
                targets.Add(e);
            }

            bool overwrite = false;
            if (skippedExisting.Count > 0 || targets.Any(x => x.PathOccupied))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "存在已存在/冲突的资源",
                    string.Format("所选资源里有 {0} 个在工程中已存在同 GUID 资源，{1} 个目标路径被其他资源占用。\n\n默认只写入「新增」的资源。",
                        skippedExisting.Count, targets.Count(x => x.PathOccupied)),
                    "只写新增",
                    "取消",
                    "覆盖已有文件");
                if (choice == 1)
                    return;
                overwrite = choice == 2;
            }

            var writeList = new List<Entry>();
            foreach (Entry e in targets)
            {
                if (e.PathOccupied && !overwrite)
                    continue;
                writeList.Add(e);
            }
            if (overwrite)
            {
                foreach (Entry e in skippedExisting)
                    if (!string.IsNullOrEmpty(ToAbsolute(e.Path)))
                        writeList.Add(e);
            }

            if (writeList.Count == 0)
            {
                _status = "没有可写入的资源。";
                _statusType = MessageType.Warning;
                return;
            }

            // 要写出的 GUID：所选资源本身 + 包内的祖先文件夹条目。
            // 祖先文件夹只在这个文件夹工程里还不存在时才补写 .meta，避免改掉已有文件夹的 GUID。
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in writeList)
                guids.Add(e.Guid);

            foreach (Entry folder in _entries)
            {
                if (!folder.IsFolder || string.IsNullOrEmpty(folder.Path))
                    continue;
                string prefix = folder.Path + "/";
                bool isAncestor = false;
                foreach (Entry e in writeList)
                {
                    if (e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        isAncestor = true;
                        break;
                    }
                }
                if (!isAncestor)
                    continue;

                string folderAbs = ToAbsolute(folder.Path);
                if (string.IsNullOrEmpty(folderAbs))
                    continue;
                if (Directory.Exists(folderAbs) || File.Exists(folderAbs + ".meta"))
                    continue;
                guids.Add(folder.Guid);
            }

            try
            {
                EditorUtility.DisplayProgressBar("写入工程", "正在复制资源…", 0.5f);
                int written = UnityPackageTar.Extract(_packagePath, ProjectRoot, _items, guids);
                AssetDatabase.Refresh();
                RefreshStatus();
                BuildTree();

                _status = string.Format("已写入 {0} 个资源到工程，其他项按策略跳过。", written);
                _statusType = MessageType.Info;
                Debug.Log("[UnityPackageInspector] " + _status);
            }
            catch (Exception ex)
            {
                _status = "写入失败：" + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ---------------------------------------------------------------- 操作：静默导入（不走导入窗口）

        /// <summary>
        /// 走 Unity 官方的非交互导入：不显示 "Import Unity Package" 窗口（因此不会出现
        /// "Nothing to import!" 空列表），底层仍按 GUID 合并——工程里没有的资源全部导入，
        /// 已存在同 GUID 的会被 Unity 跳过（不覆盖）。
        /// </summary>
        void SilentImport()
        {
            if (string.IsNullOrEmpty(_packagePath) || !File.Exists(_packagePath))
            {
                EditorUtility.DisplayDialog("包不存在", "当前包路径不存在：" + _packagePath, "好");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "静默导入（不弹导入窗口）",
                    "将调用 Unity 的非交互导入 AssetDatabase.ImportPackage(path, false)：\n" +
                    "· 不显示导入窗口，所以不会出现被 GUID 过滤后的空列表；\n" +
                    "· Unity 仍按 GUID 合并：工程里没有的资源全部导入，已存在同 GUID 的资源会被跳过（不覆盖）。\n\n" +
                    "想强制覆盖时：先用「把所选写入工程」并选「覆盖已有文件」。\n\n继续？",
                    "导入", "取消"))
                return;

            try
            {
                AssetDatabase.ImportPackage(_packagePath, false);
                AssetDatabase.Refresh();
                RefreshStatus();
                BuildTree();

                int total = 0, ready = 0;
                foreach (Entry e in _entries)
                {
                    if (e.IsFolder)
                        continue;
                    total++;
                    if (!string.IsNullOrEmpty(e.ExistingPath))
                        ready++;
                }

                _status = string.Format("静默导入完成：{0}/{1} 个资源已按原 GUID 在工程中就位（其余若与已有 GUID 冲突，Unity 会跳过不覆盖）。", ready, total);
                _statusType = ready == total ? MessageType.Info : MessageType.Warning;
                Debug.Log("[UnityPackageInspector] " + _status);
            }
            catch (Exception ex)
            {
                _status = "静默导入失败：" + ex.Message;
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }

        // ---------------------------------------------------------------- 操作：备份冲突并打开原生窗口

        void BackupConflictsAndOpenImportDialog()
        {
            if (string.IsNullOrEmpty(_packagePath) || !File.Exists(_packagePath))
            {
                EditorUtility.DisplayDialog("包不存在", "当前包路径不存在：" + _packagePath, "好");
                return;
            }

            AssetDatabase.Refresh();
            RefreshStatus();

            var conflicts = new List<Entry>();
            foreach (Entry e in _entries)
                if (!string.IsNullOrEmpty(e.ExistingPath))
                    conflicts.Add(e);

            if (conflicts.Count == 0)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "工程里没有同 GUID 资源",
                    "当前工程里没有和包内资源 GUID 相同的资源，原生导入窗口本来就应该能显示文件树。\n\n现在直接打开原生导入窗口？",
                    "打开", "取消");
                if (ok)
                    AssetDatabase.ImportPackage(_packagePath, true);
                return;
            }

            // 只处理位于 Assets/ 下的资源（Packages/ 下只读，动不了）
            var movable = conflicts.Where(x => x.ExistingPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)).ToList();
            var readOnlyConflicts = conflicts.Where(x => !x.ExistingPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)).ToList();

            string message = string.Format(
                "将把 {0} 个已存在资源（连同 .meta）移动到下面这个工程外目录：\n{1}\n\n这样 Unity 的 GUID 去重过滤就会放行，原生导入窗口会重新显示完整文件树。\n\n注意：这是把工程里现有的资源挪走（不是删除），之后需要时可以从备份目录移回。是否继续？",
                movable.Count, "Library/" + BackupDirName + "/<时间戳>/");

            if (readOnlyConflicts.Count > 0)
                message += string.Format("\n\n另有 {0} 个同 GUID 资源位于 Packages/ 等只读位置，无法移动，可能仍会被原生窗口过滤。", readOnlyConflicts.Count);

            if (!EditorUtility.DisplayDialog("备份同 GUID 资源", message, "继续", "取消"))
                return;

            string backupRoot = Path.Combine(ProjectRoot, "Library", BackupDirName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupRoot);

            var movedRoots = new List<string>();
            int moved = 0;
            try
            {
                EditorUtility.DisplayProgressBar("备份资源", "正在移动已有资源…", 0.3f);
                foreach (Entry e in movable.OrderBy(x => x.ExistingPath.Length))
                {
                    string src = ToAbsolute(e.ExistingPath);
                    if (string.IsNullOrEmpty(src))
                        continue;

                    bool ancestorMoved = false;
                    foreach (string root in movedRoots)
                    {
                        if (src.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                            src.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            ancestorMoved = true;
                            break;
                        }
                    }
                    if (ancestorMoved)
                        continue;

                    string dst = Path.Combine(backupRoot, e.ExistingPath.Replace('/', Path.DirectorySeparatorChar));
                    string dstDir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dstDir))
                        Directory.CreateDirectory(dstDir);

                    if (Directory.Exists(src))
                    {
                        if (Directory.Exists(dst))
                            Directory.Delete(dst, true);
                        Directory.Move(src, dst);
                    }
                    else if (File.Exists(src))
                    {
                        if (File.Exists(dst))
                            File.Delete(dst);
                        File.Move(src, dst);
                    }
                    else
                    {
                        continue;
                    }

                    string meta = src + ".meta";
                    if (File.Exists(meta))
                    {
                        string metaDst = dst + ".meta";
                        if (File.Exists(metaDst))
                            File.Delete(metaDst);
                        File.Move(meta, metaDst);
                    }

                    movedRoots.Add(src);
                    moved++;
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("备份过程中出错", ex.Message + "\n\n已移动的文件在：" + backupRoot, "好");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _lastBackupDir = backupRoot;
            _status = string.Format("已移动 {0} 项到 {1}；现在打开原生导入窗口。", moved, backupRoot);
            _statusType = MessageType.Warning;
            Debug.Log("[UnityPackageInspector] " + _status);

            RefreshStatus();
            AssetDatabase.ImportPackage(_packagePath, true);
        }
    }
}
