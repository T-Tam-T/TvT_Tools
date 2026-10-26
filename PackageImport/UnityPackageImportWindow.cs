using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace TvT.EditorTools
{
    /// <summary>
    /// 仿 Unity 内置「Import Unity Package」窗口的重画版本，**不做 GUID 去重**。
    ///
    /// 为什么只能重画：内置窗口是 UnityEditor.dll 里的 internal 类 UnityEditor.PackageImport，
    /// 没有公开扩展点，它的「已存在同 GUID 就隐藏/过滤」逻辑改不了。但 .unitypackage 的格式
    /// 是公开的（gzip+tar，每项 {GUID}/asset、asset.meta、pathname），所以本窗口用自带的
    /// UnityPackageTar 直接读包、自己画同款界面：工程里已存在的资源也照样列出、勾选、导入（覆盖）。
    ///
    /// 导入方式：把 asset + asset.meta 按包内原路径写回工程，GUID 保持不变 —— 等于绕过
    /// "Import Unity Package" 窗口的 GUID 去重。覆盖前可选备份到
    /// Library/UnityPackageImportBackup/&lt;yyyyMMdd_HHmmss&gt;/。
    /// </summary>
    internal sealed class UnityPackageImportWindow : EditorWindow
    {
        const string Title = "Import Unity Package";
        const string BackupDirName = "UnityPackageImportBackup";

        // 与内置窗口一致的风味：Dark 皮肤下也一样
        enum ItemState
        {
            New,                // 工程里没有这个 GUID
            Overwrite,          // 同 GUID 且同路径 —— 重新导入即覆盖
            PathConflict,       // 目标路径被「另一个 GUID」的资源占着
            DuplicateElsewhere, // 同 GUID 已经在工程里的其它路径（写进去会造成重复 GUID，跳过）
            FolderNew,
            FolderExists
        }

        sealed class Node
        {
            public string Path;
            public string Name;
            public bool IsFolder;
            public UnityPackageTar.Item Item;
            public Node Parent;
            public readonly List<Node> Children = new List<Node>();
            public int Depth;
            public bool Selected = true;
            public bool Expanded = true;
            public bool Visible = true;
            public ItemState State;
        }

        static readonly Dictionary<string, Texture> IconCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;

        string _packagePath;
        string _search = string.Empty;
        string _message;
        MessageType _messageType;
        List<UnityPackageTar.Item> _items = new List<UnityPackageTar.Item>();
        readonly List<Node> _roots = new List<Node>();
        readonly List<Node> _allNodes = new List<Node>();
        Vector2 _scroll;
        bool _backupBeforeOverwrite = true;
        bool _deletePathConflicts = true;
        bool _closeAfterImport = true;
        string _visibilitySearchKey;

        // ---------------------------------------------------------------- 打开入口

        [MenuItem("Tools/Unity Package Inspector/Import Unity Package（不过滤 GUID）")]
        static void OpenFromMenu()
        {
            Open(null);
        }

        [MenuItem("Assets/Import Unity Package（不过滤 GUID）", false, 1101)]
        static void OpenFromContextMenu()
        {
            Open(FirstSelectedPackage());
        }

        [MenuItem("Assets/Import Unity Package（不过滤 GUID）", true)]
        static bool ValidateOpenFromContextMenu()
        {
            return FirstSelectedPackage() != null;
        }

        /// <summary>尝试截断双击 .unitypackage 的默认行为：优先打开本窗口。返回 true 表示已消费。</summary>
        [OnOpenAsset(0)]
        static bool OnOpenUnityPackage(int instanceId, int line)
        {
            string path = AssetDatabase.GetAssetPath(instanceId);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                return false;
            Open(path);
            return true;
        }

        static string FirstSelectedPackage()
        {
            UnityEngine.Object[] objects = Selection.objects;
            if (objects == null)
                return null;
            for (int i = 0; i < objects.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(objects[i]);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }

        internal static void Open(string packagePath)
        {
            UnityPackageImportWindow win = GetWindow<UnityPackageImportWindow>(true, Title, true);
            win.minSize = new Vector2(580f, 400f);
            if (!string.IsNullOrEmpty(packagePath))
                win.SetPackage(packagePath);
            win.Show();
            win.Focus();
        }

        void OnEnable()
        {
            titleContent = new GUIContent(Title);
        }

        // ---------------------------------------------------------------- 读包 / 建树

        void SetPackage(string packagePath)
        {
            string full = packagePath;
            if (!Path.IsPathRooted(full))
                full = Path.GetFullPath(Path.Combine(ProjectRoot, full));
            _packagePath = full;
            Reload();
        }

        void Reload()
        {
            _items = new List<UnityPackageTar.Item>();
            _roots.Clear();
            _allNodes.Clear();
            _message = null;
            _visibilitySearchKey = null;

            if (string.IsNullOrEmpty(_packagePath))
                return;
            if (!File.Exists(_packagePath))
            {
                ShowMessage("包文件不存在：" + _packagePath, MessageType.Error);
                return;
            }

            try
            {
                _items = UnityPackageTar.ReadItems(_packagePath);
                BuildTree();
                ComputeStates();
                RebuildVisibility();
            }
            catch (Exception ex)
            {
                ShowMessage("读取包失败：" + ex.Message, MessageType.Error);
                Debug.LogException(ex);
            }
        }

        void BuildTree()
        {
            var folders = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _items.Count; i++)
            {
                UnityPackageTar.Item item = _items[i];
                string path = (item.Path ?? string.Empty).TrimEnd('/');
                if (path.Length == 0)
                    continue;

                string[] parts = path.Split('/');
                Node parent = null;
                for (int p = 0; p < parts.Length; p++)
                {
                    string current = string.Join("/", parts, 0, p + 1);
                    Node node;
                    if (!folders.TryGetValue(current, out node))
                    {
                        node = new Node
                        {
                            Path = current,
                            Name = parts[p],
                            IsFolder = true,
                            Parent = parent,
                            Depth = p,
                            Expanded = true
                        };
                        if (parent == null)
                            _roots.Add(node);
                        else
                            parent.Children.Add(node);
                        folders[current] = node;
                        _allNodes.Add(node);
                    }
                    if (p == parts.Length - 1)
                    {
                        node.IsFolder = item.IsFolder;
                        node.Item = item;
                    }
                    parent = node;
                }
            }

            SortRecursive(_roots);
        }

        static void SortRecursive(List<Node> nodes)
        {
            nodes.Sort(delegate(Node a, Node b)
            {
                if (a.IsFolder != b.IsFolder)
                    return a.IsFolder ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < nodes.Count; i++)
                SortRecursive(nodes[i].Children);
        }

        void ComputeStates()
        {
            for (int i = 0; i < _allNodes.Count; i++)
            {
                Node node = _allNodes[i];
                if (node.Item == null)
                {
                    node.State = Directory.Exists(FullPath(node.Path)) ? ItemState.FolderExists : ItemState.FolderNew;
                    continue;
                }

                string existing = AssetDatabase.GUIDToAssetPath(node.Item.Guid);

                if (node.Item.IsFolder)
                {
                    node.State = !string.IsNullOrEmpty(existing) || Directory.Exists(FullPath(node.Path))
                        ? ItemState.FolderExists
                        : ItemState.FolderNew;
                    continue;
                }

                if (!string.IsNullOrEmpty(existing))
                {
                    node.State = string.Equals(existing, node.Path, StringComparison.OrdinalIgnoreCase)
                        ? ItemState.Overwrite
                        : ItemState.DuplicateElsewhere;
                    continue;
                }

                bool occupied = File.Exists(FullPath(node.Path)) || Directory.Exists(FullPath(node.Path));
                node.State = occupied ? ItemState.PathConflict : ItemState.New;
            }
        }

        void RebuildVisibility()
        {
            _visibilitySearchKey = _search;
            for (int i = 0; i < _allNodes.Count; i++)
                _allNodes[i].Visible = MatchesSearch(_allNodes[i]);
        }

        bool MatchesSearch(Node node)
        {
            if (string.IsNullOrEmpty(_search))
                return true;
            if (node.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            for (int i = 0; i < node.Children.Count; i++)
                if (MatchesSearch(node.Children[i]))
                    return true;
            return false;
        }

        // ---------------------------------------------------------------- 界面

        void OnGUI()
        {
            HandleDragAndDrop();

            if (string.IsNullOrEmpty(_packagePath))
            {
                DrawEmptyState();
                return;
            }

            DrawHeader();
            if (!string.IsNullOrEmpty(_visibilitySearchKey) && _visibilitySearchKey != _search || _visibilitySearchKey == null && _search.Length > 0)
                RebuildVisibility();
            DrawTree();
            DrawFooter();
        }

        void DrawEmptyState()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "把 .unitypackage 拖进本窗口，或点下面的「选择包…」。\n\n" +
                "这是内置 Import Unity Package 窗口的重画版：不按 GUID 去重，工程里已存在的资源也会列出来，" +
                "可以重新导入（覆盖）——所以不会再出现 \"Nothing to import! All assets from this package are already in your project.\" 的空列表。",
                MessageType.Info);

            if (GUILayout.Button("选择包…", GUILayout.Height(26f)))
                PickPackage();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string newSearch = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (newSearch != _search)
            {
                _search = newSearch;
                RebuildVisibility();
            }

            if (GUILayout.Button("全部", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                SelectOnly(null);
            if (GUILayout.Button("全不选", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                SelectOnly(delegate(Node n) { return false; });
            if (GUILayout.Button("仅新增", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                SelectOnly(delegate(Node n) { return n.State == ItemState.New || n.State == ItemState.PathConflict; });
            if (GUILayout.Button("仅覆盖", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                SelectOnly(delegate(Node n) { return n.State == ItemState.Overwrite; });

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重新扫描", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                AssetDatabase.Refresh();
                Reload();
            }
            if (GUILayout.Button("选择包…", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                PickPackage();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(_packagePath, EditorStyles.miniLabel);

            int countNew = 0, countOverwrite = 0, countConflict = 0, countDuplicate = 0;
            for (int i = 0; i < _allNodes.Count; i++)
            {
                Node node = _allNodes[i];
                if (node.IsFolder || node.Item == null)
                    continue;
                switch (node.State)
                {
                    case ItemState.New: countNew++; break;
                    case ItemState.Overwrite: countOverwrite++; break;
                    case ItemState.PathConflict: countConflict++; break;
                    case ItemState.DuplicateElsewhere: countDuplicate++; break;
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format("共 {0} 项：新增 {1}   覆盖 {2}   路径冲突 {3}   已在其它路径(跳过) {4}",
                countNew + countOverwrite + countConflict + countDuplicate, countNew, countOverwrite, countConflict, countDuplicate),
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, _messageType);
        }

        void DrawTree()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, "box", GUILayout.ExpandHeight(true));
            for (int i = 0; i < _roots.Count; i++)
                DrawNode(_roots[i]);
            EditorGUILayout.EndScrollView();
        }

        void DrawNode(Node node)
        {
            if (!node.Visible)
                return;

            EditorGUILayout.BeginHorizontal();

            if (node.IsFolder)
            {
                GUILayout.Space(node.Depth * 14f);
                if (node.Children.Count > 0)
                {
                    Rect foldRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldout, GUILayout.Width(12f));
                    node.Expanded = EditorGUI.Foldout(foldRect, node.Expanded, GUIContent.none);
                }
                else
                {
                    GUILayout.Space(12f);
                }

                bool all = AllSelected(node);
                bool none = NoneSelected(node);
                EditorGUI.showMixedValue = !all && !none;
                bool toggled = EditorGUILayout.Toggle(all, GUILayout.Width(16f));
                EditorGUI.showMixedValue = false;
                if (toggled != all)
                    SetSelectedRecursive(node, toggled);

                DrawIconAndLabel(node);
                DrawStatus(node);
                EditorGUILayout.EndHorizontal();

                if (node.Expanded || !string.IsNullOrEmpty(_search))
                {
                    for (int i = 0; i < node.Children.Count; i++)
                        DrawNode(node.Children[i]);
                }
            }
            else
            {
                GUILayout.Space(node.Depth * 14f + 12f);
                node.Selected = EditorGUILayout.Toggle(node.Selected, GUILayout.Width(16f));
                DrawIconAndLabel(node);
                DrawStatus(node);
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawIconAndLabel(Node node)
        {
            Texture icon = GetIcon(node);
            GUIContent content = new GUIContent(" " + node.Name, icon, node.Item != null ? node.Path : node.Path + "（目录）");
            if (node.IsFolder)
                EditorGUILayout.LabelField(new GUIContent(" " + node.Name + "/", icon, node.Path), GUILayout.MinWidth(120f));
            else
                EditorGUILayout.LabelField(content, GUILayout.MinWidth(120f));
        }

        void DrawStatus(Node node)
        {
            GUILayout.FlexibleSpace();
            string text;
            Color color;
            switch (node.State)
            {
                case ItemState.New:
                    text = "新增"; color = new Color(0.55f, 0.9f, 0.55f); break;
                case ItemState.Overwrite:
                    text = "覆盖"; color = new Color(0.95f, 0.85f, 0.45f); break;
                case ItemState.PathConflict:
                    text = "路径冲突"; color = new Color(0.95f, 0.65f, 0.35f); break;
                case ItemState.DuplicateElsewhere:
                    text = "已在别处"; color = new Color(0.95f, 0.5f, 0.5f); break;
                case ItemState.FolderNew:
                    text = "新目录"; color = new Color(0.6f, 0.75f, 0.9f); break;
                default:
                    return;
            }

            Color oldContent = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(76f));
            GUI.contentColor = oldContent;
        }

        void DrawFooter()
        {
            int selectedCount = 0;
            long totalSize = 0;
            for (int i = 0; i < _allNodes.Count; i++)
            {
                Node node = _allNodes[i];
                if (node.IsFolder || node.Item == null || !node.Selected)
                    continue;
                selectedCount++;
                totalSize += node.Item.Size;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format("已选 {0} 个资源 · {1}", selectedCount, HumanSize(totalSize)), GUILayout.Width(200f));

            _backupBeforeOverwrite = GUILayout.Toggle(_backupBeforeOverwrite, "覆盖前备份", GUILayout.Width(88f));
            _deletePathConflicts = GUILayout.Toggle(_deletePathConflicts, "备份后解冲突", GUILayout.Width(100f));
            _closeAfterImport = GUILayout.Toggle(_closeAfterImport, "导入后关闭", GUILayout.Width(88f));

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledGroupScope(selectedCount == 0))
            {
                if (GUILayout.Button("导入", GUILayout.Width(80f), GUILayout.Height(24f)))
                    RunImport();
            }
            if (GUILayout.Button("取消", GUILayout.Width(64f), GUILayout.Height(24f)))
                Close();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            UnityPackageImportWatchdog.Enabled = GUILayout.Toggle(
                UnityPackageImportWatchdog.Enabled,
                "接管内置 Import Unity Package 窗口（一出现就关掉，换成这个窗口）",
                GUILayout.Width(400f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- 选择逻辑

        void SelectOnly(Func<Node, bool> predicate)
        {
            for (int i = 0; i < _allNodes.Count; i++)
            {
                Node node = _allNodes[i];
                if (node.IsFolder || node.Item == null)
                    continue;
                node.Selected = predicate == null || predicate(node);
            }
        }

        static bool AllSelected(Node node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                Node child = node.Children[i];
                if (child.IsFolder)
                {
                    if (!AllSelected(child))
                        return false;
                }
                else if (!child.Selected)
                {
                    return false;
                }
            }
            return true;
        }

        static bool NoneSelected(Node node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                Node child = node.Children[i];
                if (child.IsFolder)
                {
                    if (!NoneSelected(child))
                        return false;
                }
                else if (child.Selected)
                {
                    return false;
                }
            }
            return true;
        }

        static void SetSelectedRecursive(Node node, bool value)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                Node child = node.Children[i];
                if (child.IsFolder)
                    SetSelectedRecursive(child, value);
                else
                    child.Selected = value;
            }
        }

        // ---------------------------------------------------------------- 导入

        void RunImport()
        {
            var toWrite = new List<Node>();
            var skipDuplicate = new List<Node>();
            var blocked = new List<Node>();
            var removeConflicts = new List<Node>();

            for (int i = 0; i < _allNodes.Count; i++)
            {
                Node node = _allNodes[i];
                if (node.IsFolder || node.Item == null || !node.Selected)
                    continue;

                if (node.State == ItemState.DuplicateElsewhere)
                {
                    skipDuplicate.Add(node);
                    continue;
                }
                if (node.State == ItemState.PathConflict)
                {
                    if (Directory.Exists(FullPath(node.Path)))
                    {
                        blocked.Add(node);
                        continue;
                    }
                    if (!_deletePathConflicts)
                    {
                        blocked.Add(node);
                        continue;
                    }
                    removeConflicts.Add(node);
                }
                toWrite.Add(node);
            }

            if (toWrite.Count == 0)
            {
                ShowMessage(string.Format("没有可导入的资源：已选 {0} 个跳过，{1} 个被阻塞。", skipDuplicate.Count, blocked.Count), MessageType.Warning);
                return;
            }

            string summary = string.Format(
                "准备导入 {0} 个资源（GUID 保持不变，会覆盖工程里同 GUID 的同名资源）。\n" +
                "· 跳过（GUID 已在工程其它路径）{1} 个\n" +
                "· 阻塞 {2} 个\n\n" +
                "继续？", toWrite.Count, skipDuplicate.Count, blocked.Count);
            if (!EditorUtility.DisplayDialog("Import Unity Package（不过滤 GUID）", summary, "导入", "取消"))
                return;

            string backupDir = null;
            if (_backupBeforeOverwrite && (removeConflicts.Count > 0 || HasOverwrite(toWrite)))
                backupDir = Path.Combine(ProjectRoot, "Library", BackupDirName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            EditorUtility.DisplayProgressBar("Import Unity Package", "准备备份 / 清理冲突…", 0.1f);
            try
            {
                for (int i = 0; i < removeConflicts.Count; i++)
                {
                    Node node = removeConflicts[i];
                    BackupFile(backupDir, node.Path);
                    BackupFile(backupDir, node.Path + ".meta");
                    SafeDelete(FullPath(node.Path));
                    SafeDelete(FullPath(node.Path) + ".meta");
                }

                for (int i = 0; i < toWrite.Count; i++)
                {
                    Node node = toWrite[i];
                    if (File.Exists(FullPath(node.Path)))
                    {
                        BackupFile(backupDir, node.Path);
                        BackupFile(backupDir, node.Path + ".meta");
                    }
                }

                EditorUtility.DisplayProgressBar("Import Unity Package", "正在写入资源…", 0.4f);

                var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < toWrite.Count; i++)
                    guids.Add(toWrite[i].Item.Guid);
                for (int i = 0; i < _items.Count; i++)
                {
                    UnityPackageTar.Item item = _items[i];
                    if (item.IsFolder && !Directory.Exists(FullPath(item.Path)))
                        guids.Add(item.Guid);
                }

                int written = UnityPackageTar.Extract(_packagePath, ProjectRoot, _items, guids);
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Import Unity Package", "校验导入结果…", 0.85f);

                int ready = 0;
                for (int i = 0; i < toWrite.Count; i++)
                {
                    string mapped = AssetDatabase.GUIDToAssetPath(toWrite[i].Item.Guid);
                    if (!string.IsNullOrEmpty(mapped) && File.Exists(FullPath(mapped)))
                        ready++;
                }

                Reload();

                if (ready == toWrite.Count && skipDuplicate.Count == 0 && blocked.Count == 0)
                    ShowMessage(string.Format("导入完成：{0}/{1} 个资源已按原 GUID 就位。", ready, toWrite.Count), MessageType.Info);
                else
                    ShowMessage(string.Format("导入完成：{0}/{1} 个资源就位；跳过 {2}，阻塞 {3}。{4}",
                        ready, toWrite.Count, skipDuplicate.Count, blocked.Count,
                        backupDir != null ? "备份：" + backupDir : string.Empty), MessageType.Warning);

                Debug.Log("[UnityPackageImportWindow] " + _message);
                if (backupDir != null)
                    Debug.Log("[UnityPackageImportWindow] 备份目录：" + backupDir);

                if (_closeAfterImport)
                {
                    EditorUtility.DisplayDialog("Import Unity Package", _message, "好");
                    Close();
                }
            }
            catch (Exception ex)
            {
                ShowMessage("导入失败：" + ex.Message, MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static bool HasOverwrite(List<Node> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (File.Exists(FullPath(nodes[i].Path)))
                    return true;
            }
            return false;
        }

        static void BackupFile(string backupDir, string projectRelativePath)
        {
            if (string.IsNullOrEmpty(backupDir))
                return;
            string source = FullPath(projectRelativePath);
            if (!File.Exists(source))
                return;
            string target = Path.Combine(backupDir, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(source, target, true);
        }

        static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityPackageImportWindow] 删除失败：" + path + "，" + ex.Message);
            }
        }

        static string FullPath(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
                return ProjectRoot;
            return Path.GetFullPath(Path.Combine(ProjectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string HumanSize(long bytes)
        {
            if (bytes >= 1024L * 1024L)
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            if (bytes >= 1024L)
                return (bytes / 1024f).ToString("0.0") + " KB";
            return bytes + " B";
        }

        // ---------------------------------------------------------------- 图标 / 杂项

        static Texture GetIcon(Node node)
        {
            if (node.IsFolder)
                return LoadIcon("Folder Icon");

            string extension = Path.GetExtension(node.Path);
            if (string.IsNullOrEmpty(extension))
                return LoadIcon("DefaultAsset Icon");
            switch (extension.ToLowerInvariant())
            {
                case ".cs": return LoadIcon("cs Script Icon");
                case ".shader": return LoadIcon("Shader Icon");
                case ".mat": return LoadIcon("Material Icon");
                case ".prefab": return LoadIcon("Prefab Icon");
                case ".asset": return LoadIcon("ScriptableObject Icon");
                case ".unity": return LoadIcon("SceneAsset Icon");
                case ".anim": return LoadIcon("AnimationClip Icon");
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd": return LoadIcon("Texture Icon");
                case ".fbx":
                case ".obj":
                case ".mesh": return LoadIcon("Mesh Icon");
                case ".txt":
                case ".json":
                case ".xml":
                case ".csv":
                case ".yaml": return LoadIcon("TextAsset Icon");
                default: return LoadIcon("DefaultAsset Icon");
            }
        }

        static Texture LoadIcon(string name)
        {
            Texture texture;
            if (IconCache.TryGetValue(name, out texture))
                return texture;
            GUIContent content = EditorGUIUtility.IconContent(name);
            texture = content == null ? null : content.image;
            IconCache[name] = texture;
            return texture;
        }

        void PickPackage()
        {
            string path = EditorUtility.OpenFilePanel("选择 .unitypackage", ProjectRoot, "unitypackage");
            if (!string.IsNullOrEmpty(path))
                SetPackage(path);
        }

        void ShowMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }

        void HandleDragAndDrop()
        {
            Event current = Event.current;
            if (current == null)
                return;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;

            string package = FirstPackageFromDrag();
            if (package == null)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetPackage(package);
            }
            current.Use();
        }

        static string FirstPackageFromDrag()
        {
            string[] paths = DragAndDrop.paths;
            if (paths == null)
                return null;
            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]) && paths[i].EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                    return paths[i];
            }
            return null;
        }
    }
}
