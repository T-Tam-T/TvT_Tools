using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using UnityEditor.IMGUI.Controls;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FolderTagNamespace
{
    public class FolderTag : MonoBehaviour
    {
        public static Dictionary<string, TagInfo> folderTags = new Dictionary<string, TagInfo>();
        private const string EXCEL_FILE_NAME = "名称标记.csv";

        // 修改默认颜色为 #00CBFF
        private static Color DEFAULT_COLOR = new Color(0f, 0.796f, 1f, 1f); // #00CBFF

        [System.Serializable]
        public struct TagInfo
        {
            public string tagText;
            public Color textColor;
            public Vector2 offset;
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            LoadFromExcel();
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemOnGUI;
        }

        static void OnProjectWindowItemOnGUI(string guid, Rect selectionRect)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (Directory.Exists(assetPath))
            {
                if (folderTags.TryGetValue(assetPath, out var tagInfo))
                {
                    Event e = Event.current;
                    if (e.type == EventType.Repaint)
                    {
                        GUIStyle style = new GUIStyle(GUI.skin.label);
                        style.normal.textColor = tagInfo.textColor;
                        style.fontSize = 10;

                        float offsetX = tagInfo.offset.x;
                        float offsetY = tagInfo.offset.y;
                        Vector2 labelSize = style.CalcSize(new GUIContent($"[{tagInfo.tagText}]"));
                        Rect labelRect = new Rect(selectionRect.x + offsetX, selectionRect.y + offsetY, labelSize.x, labelSize.y);

                        GUI.Label(labelRect, $"[{tagInfo.tagText}]", style);
                    }
                }
            }
        }

        // 将方法改为public以便外部访问
        public static void LoadFromExcel()
        {
            // 修改为Assets目录下的路径
            string excelPath = Path.Combine(Application.dataPath, EXCEL_FILE_NAME);

            if (!File.Exists(excelPath))
            {
                Debug.LogWarning($"找不到标记文件: {excelPath}");
                return;
            }

            try
            {
                // 清空现有标记
                folderTags.Clear();

                // 读取Excel文件内容
                string[] lines = ReadExcelWithEncodingDetection(excelPath);
                int importedCount = 0;

                // 跳过标题行
                for (int i = 1; i < lines.Length; i++)
                {
                    // 使用正则表达式处理可能的格式问题
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    MatchCollection matches = Regex.Matches(line, @"\""(.*?)\""|([^,\s]+)");

                    if (matches.Count < 2) continue;

                    string folderName = matches[0].Value.Trim('"').Trim();
                    string tagText = matches[1].Value.Trim('"').Trim();

                    // 检查文件夹名称是否有效
                    if (IsValidFolderName(folderName) && !string.IsNullOrEmpty(tagText))
                    {
                        // 在项目中搜索匹配的文件夹
                        string[] matchingFolders = AssetDatabase.FindAssets(folderName)
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => Directory.Exists(p) && Path.GetFileName(p) == folderName)
                            .ToArray();

                        // 标记所有匹配的文件夹（不只是第一个）
                        foreach (string folderPath in matchingFolders)
                        {
                            // 如果文件夹已经有标记，跳过（避免重复）
                            if (folderTags.ContainsKey(folderPath)) continue;

                            TagInfo tagInfo = new TagInfo
                            {
                                tagText = tagText,
                                textColor = DEFAULT_COLOR,
                                offset = new Vector2(150, 0)
                            };

                            folderTags[folderPath] = tagInfo;
                            importedCount++;
                        }

                        if (matchingFolders.Length == 0)
                        {
                            Debug.LogWarning($"找不到文件夹: {folderName}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"跳过无效行: {line}");
                    }
                }

                Debug.Log($"成功导入 {importedCount} 个文件夹标记");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"导入Excel失败: {ex.Message}");
            }
        }

        // 使用编码检测读取Excel文件
        private static string[] ReadExcelWithEncodingDetection(string filePath)
        {
            // 尝试多种编码
            Encoding[] encodings = new Encoding[]
            {
                Encoding.UTF8,
                Encoding.GetEncoding("GB2312"),
                Encoding.GetEncoding("GBK"),
                Encoding.GetEncoding("Big5"),
                Encoding.Default
            };

            foreach (var encoding in encodings)
            {
                try
                {
                    string[] lines = File.ReadAllLines(filePath, encoding);

                    // 检查第一行是否包含有效数据
                    if (lines.Length > 0 && IsValidHeaderLine(lines[0]))
                    {
                        Debug.Log($"使用编码: {encoding.EncodingName}");
                        return lines;
                    }
                }
                catch
                {
                    // 尝试下一个编码
                }
            }

            // 如果所有编码都失败，使用默认编码
            return File.ReadAllLines(filePath);
        }

        // 检查是否是有效的标题行
        private static bool IsValidHeaderLine(string line)
        {
            return line.Contains("文件夹名称") || line.Contains("文件夹标记") ||
                   line.Contains("名称") || line.Contains("标记");
        }

        // 检查文件夹名称是否有效
        private static bool IsValidFolderName(string name)
        {
            // 检查是否包含控制字符
            foreach (char c in name)
            {
                if (char.IsControl(c))
                {
                    return false;
                }
            }

            // 检查是否包含特殊字符
            return !Regex.IsMatch(name, @"[^\w\s\.-]");
        }

        // 添加刷新菜单项
        [MenuItem("Tools/TvTTools/刷新文件夹标记")]
        public static void RefreshFolderTags()
        {
            LoadFromExcel();
            Debug.Log("文件夹标记已刷新");
        }

        // 修复菜单项乱码
        [MenuItem("Assets/TvTTools/文件夹标记/添加标记", validate = true, priority = 5)]
        static bool AddFolderTagValidation()
        {
            return Selection.activeObject != null && Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject)) && !folderTags.ContainsKey(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Assets/TvTTools/文件夹标记/添加标记", priority = 5)]
        static void AddFolderTag()
        {
            string selectedFolderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            TagEditorWindow window = (TagEditorWindow)EditorWindow.GetWindow(typeof(TagEditorWindow), false, "添加标记");
            window.Init(selectedFolderPath, null);
        }

        [MenuItem("Assets/TvTTools/文件夹标记/编辑标记", validate = true, priority = 5)]
        static bool EditFolderTagValidation()
        {
            return Selection.activeObject != null && Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject)) && folderTags.ContainsKey(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Assets/TvTTools/文件夹标记/编辑标记", priority = 5)]
        static void EditFolderTag()
        {
            string selectedFolderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            TagInfo currentTagInfo = folderTags[selectedFolderPath];
            TagEditorWindow window = (TagEditorWindow)EditorWindow.GetWindow(typeof(TagEditorWindow), false, "编辑标记");
            window.Init(selectedFolderPath, currentTagInfo);
        }


        [MenuItem("Assets/TvTTools/文件夹标记/调整位置", validate = true, priority = 5)]
        static bool AdjustFolderTagOffsetValidation()
        {
            return Selection.activeObject != null && Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject)) && folderTags.ContainsKey(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Assets/TvTTools/文件夹标记/调整位置", priority = 5)]
        static void AdjustFolderTagOffset()
        {
            string selectedFolderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            TagInfo currentTagInfo = folderTags[selectedFolderPath];
            OffsetEditorWindow window = (OffsetEditorWindow)EditorWindow.GetWindow(typeof(OffsetEditorWindow), false, "调整位置");
            window.Init(selectedFolderPath, currentTagInfo);
        }

        [MenuItem("Assets/TvTTools/文件夹标记/删除标记", validate = true, priority = 5)]
        static bool DeleteFolderTagValidation()
        {
            return Selection.activeObject != null && Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject)) && folderTags.ContainsKey(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Assets/TvTTools/文件夹标记/删除标记", priority = 5)]
        static void DeleteFolderTag()
        {
            string selectedFolderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            folderTags.Remove(selectedFolderPath);
        }

        // 在Project窗口中显示树状列表
        [MenuItem("Window/文件夹标记管理器")]
        static void ShowTagManager()
        {
            TagManagerWindow window = (TagManagerWindow)EditorWindow.GetWindow(typeof(TagManagerWindow), false, "文件夹标记");
            window.Show();
        }
    }

    // 文件夹标记管理器窗口
    public class TagManagerWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        void OnGUI()
        {
            GUILayout.Label("文件夹标记管理器", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // 刷新按钮
            if (GUILayout.Button("刷新标记", GUILayout.Width(100)))
            {
                FolderTag.LoadFromExcel();
            }

            // 添加手动刷新Excel选项
            if (GUILayout.Button("重新加载Excel", GUILayout.Width(120)))
            {
                FolderTag.RefreshFolderTags();
            }

            EditorGUILayout.EndHorizontal();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var kvp in FolderTag.folderTags)
            {
                EditorGUILayout.BeginHorizontal();

                // 显示文件夹路径
                EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(300));

                // 显示标记文本
                EditorGUILayout.LabelField($"[{kvp.Value.tagText}]", GetTagStyle(kvp.Value.textColor));

                // 编辑按钮
                if (GUILayout.Button("编辑", GUILayout.Width(60)))
                {
                    TagEditorWindow window = (TagEditorWindow)EditorWindow.GetWindow(typeof(TagEditorWindow), false, "编辑标记");
                    window.Init(kvp.Key, kvp.Value);
                }

                // 调整偏移按钮
                if (GUILayout.Button("调整位置", GUILayout.Width(80)))
                {
                    OffsetEditorWindow window = (OffsetEditorWindow)EditorWindow.GetWindow(typeof(OffsetEditorWindow), false, "调整位置");
                    window.Init(kvp.Key, kvp.Value);
                }

                // 删除按钮
                if (GUILayout.Button("删除", GUILayout.Width(60)))
                {
                    FolderTag.folderTags.Remove(kvp.Key);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private GUIStyle GetTagStyle(Color color)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = color;
            style.fontStyle = FontStyle.Bold;
            return style;
        }
    }

    public class TagEditorWindow : EditorWindow
    {
        private string folderPath;
        private string tagText;
        private Color textColor;

        public void Init(string path, FolderTag.TagInfo? info)
        {
            folderPath = path;
            if (info.HasValue)
            {
                tagText = info.Value.tagText;
                textColor = info.Value.textColor;
            }
            else
            {
                tagText = "";
                // 使用自定义颜色作为默认颜色
                textColor = new Color(0f, 0.796f, 1f, 1f); // #00CBFF
            }
            Show();
        }

        void OnGUI()
        {
            GUILayout.Label("编辑标记", EditorStyles.boldLabel);

            tagText = EditorGUILayout.TextField("标记文本:", tagText);
            textColor = EditorGUILayout.ColorField("文本颜色:", textColor);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("保存"))
            {
                if (!string.IsNullOrEmpty(tagText))
                {
                    FolderTag.TagInfo updatedTagInfo = new FolderTag.TagInfo
                    {
                        tagText = tagText,
                        textColor = textColor,
                        offset = FolderTag.folderTags.ContainsKey(folderPath) ?
                                 FolderTag.folderTags[folderPath].offset : new Vector2(70, 0)
                    };
                    FolderTag.folderTags[folderPath] = updatedTagInfo;
                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog("错误", "标记文本不能为空", "确定");
                }
            }

            // 添加刷新按钮
            if (GUILayout.Button("刷新"))
            {
                FolderTag.LoadFromExcel();
                Close();
            }

            if (GUILayout.Button("取消"))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    public class OffsetEditorWindow : EditorWindow
    {
        private string folderPath;
        private Vector2 offset;

        public void Init(string path, FolderTag.TagInfo info)
        {
            folderPath = path;
            offset = info.offset;
            Show();
        }

        void OnGUI()
        {
            GUILayout.Label("调整位置", EditorStyles.boldLabel);

            offset = EditorGUILayout.Vector2Field("偏移量:", offset);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("保存"))
            {
                FolderTag.TagInfo updatedTagInfo = new FolderTag.TagInfo
                {
                    tagText = FolderTag.folderTags[folderPath].tagText,
                    textColor = FolderTag.folderTags[folderPath].textColor,
                    offset = offset
                };
                FolderTag.folderTags[folderPath] = updatedTagInfo;
                Close();
            }

            // 添加刷新按钮
            if (GUILayout.Button("刷新"))
            {
                FolderTag.LoadFromExcel();
                Close();
            }

            if (GUILayout.Button("取消"))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    [CustomEditor(typeof(FolderTag))]
    public class FolderTagEditor : Editor
    {
        private TreeView treeView;

        void OnEnable()
        {
            var treeViewState = new TreeViewState();
            treeView = new CustomTreeView(treeViewState);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            // 添加刷新按钮
            if (GUILayout.Button("刷新标记"))
            {
                FolderTag.LoadFromExcel();
            }

            treeView.Reload();
            treeView.OnGUI(GUILayoutUtility.GetRect(300, 400));
        }

        private class CustomTreeView : TreeView
        {
            public CustomTreeView(TreeViewState state) : base(state) { }

            protected override TreeViewItem BuildRoot()
            {
                List<TreeViewItem> rows = new List<TreeViewItem>();
                int id = 0;

                foreach (var kvp in FolderTag.folderTags)
                {
                    rows.Add(new TreeViewItem(id++, 0, kvp.Key));
                }

                var root = new TreeViewItem(-1, -1, "Root")
                {
                    children = rows
                };

                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                base.RowGUI(args);

                if (args.item.id >= 0)
                {
                    string assetPath = args.item.displayName;
                    if (FolderTag.folderTags.TryGetValue(assetPath, out var tagInfo))
                    {
                        GUIStyle style = new GUIStyle(GUI.skin.label);
                        style.normal.textColor = tagInfo.textColor;
                        style.fontSize = 10;

                        // 在项目右侧显示标记
                        float offsetX = 5f;
                        Vector2 labelSize = style.CalcSize(new GUIContent($"[{tagInfo.tagText}]"));
                        Rect labelRect = new Rect(args.rowRect.xMax + offsetX, args.rowRect.yMin, labelSize.x, labelSize.y);

                        GUI.Label(labelRect, $"[{tagInfo.tagText}]", style);
                    }
                }
            }
        }
    }

}
