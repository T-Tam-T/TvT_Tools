using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class BatchFolderCreator : EditorWindow
{
    private string folderStructure = "";
    private string targetPath = "Assets";
    private Vector2 scrollInput, scrollPreview;
    private string previewTree = "";
    private bool showPreview = true;

    // ---------- 右键菜单项 ----------
    [MenuItem("Assets/TvTTools/文件夹/批量创建文件夹", priority = 5)]
    private static void OpenBatchCreator()
    {
        var window = GetWindow<BatchFolderCreator>("批量创建文件夹");
        window.targetPath = GetSelectedFolderPath();
        window.UpdatePreview();
        window.Show();
    }

    [MenuItem("Assets/TvTTools/文件夹/快速创建特效资源文件夹", priority = 5)]
    private static void QuickCreateVFXFolders()
    {
        string root = GetSelectedFolderPath();
        string[] folders = { "Materials", "Models", "Prefabs", "Textures" };
        int count = 0;
        foreach (var folder in folders)
        {
            string fullPath = Path.Combine(root, folder);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                count++;
            }
        }
        AssetDatabase.Refresh();
        Debug.Log($"已在 {root} 下创建 {count} 个特效资源文件夹");
        if (count > 0)
            EditorUtility.DisplayDialog("完成", $"已在 {root} 下创建 {count} 个文件夹。", "确定");
    }

    [MenuItem("Assets/TvTTools/文件夹/复制文件夹路径", priority = 5)]
    private static void CopyFolderPath()
    {
        string path = GetSelectedFolderPath();
        if (!string.IsNullOrEmpty(path))
        {
            GUIUtility.systemCopyBuffer = path;
            Debug.Log($"已复制路径: {path}");
        }
    }

    // 验证：仅当选中单个文件夹时菜单可用
    [MenuItem("Assets/TvTTools/文件夹/批量创建文件夹", validate = true, priority = 5)]
    [MenuItem("Assets/TvTTools/文件夹/快速创建特效资源文件夹", validate = true, priority = 5)]
    [MenuItem("Assets/TvTTools/文件夹/复制文件夹路径", validate = true, priority = 5)]
    private static bool ValidateSelection()
    {
        return Selection.activeObject != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
    }

    private static string GetSelectedFolderPath()
    {
        if (Selection.activeObject == null)
            return "Assets";
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!AssetDatabase.IsValidFolder(path))
            return "Assets";
        return path;
    }

    // ---------- 窗口 GUI ----------
    private void OnGUI()
    {
        // 显示当前目标文件夹（自动跟随选中，无需手动设置）
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("当前文件夹");
        targetPath = GetSelectedFolderPath();
        EditorGUILayout.TextField(targetPath);
        if (GUILayout.Button("刷新", GUILayout.Width(50)))
        {
            targetPath = GetSelectedFolderPath();
            UpdatePreview();
        }
        EditorGUILayout.EndHorizontal();

        showPreview = EditorGUILayout.Toggle("显示树线预览", showPreview);

        EditorGUILayout.BeginHorizontal();
        if (showPreview)
        {
            // 预览面板
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.45f));
            EditorGUILayout.LabelField("结构预览", EditorStyles.boldLabel);
            scrollPreview = EditorGUILayout.BeginScrollView(scrollPreview, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(previewTree, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // 输入面板
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("输入文件夹结构 (Tab/空格缩进表示层级)", EditorStyles.boldLabel);
        scrollInput = EditorGUILayout.BeginScrollView(scrollInput, GUILayout.ExpandHeight(true));
        var newText = EditorGUILayout.TextArea(folderStructure, GUILayout.ExpandHeight(true));
        if (newText != folderStructure)
        {
            folderStructure = newText;
            UpdatePreview();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        // 创建按钮
        if (GUILayout.Button("创建文件夹", GUILayout.Height(30)))
        {
            CreateFoldersFromText();
        }

        // 提示信息
        EditorGUILayout.HelpBox("使用方式：\n" +
                                "· 每行一个文件夹名称\n" +
                                "· 用 Tab 或两个空格增加缩进表示子文件夹\n" +
                                "· 文件夹将创建在「当前文件夹」下", MessageType.Info);
    }

    private void UpdatePreview()
    {
        previewTree = GenerateTreeLines(folderStructure);
    }

    // 根据缩进文本生成树线字符串
    private string GenerateTreeLines(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var lines = input.Split('\n');
        var depths = new List<int>(); // -1 表示空行
        var names = new List<string>();
        foreach (var line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                depths.Add(-1);
                names.Add("");
            }
            else
            {
                depths.Add(CountIndentDepth(trimmed));
                names.Add(trimmed.Trim());
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (depths[i] == -1)
            {
                sb.AppendLine();
                continue;
            }
            int depth = depths[i];
            string name = names[i];
            var lineBuilder = new StringBuilder();

            // 绘制祖先竖线
            for (int d = 0; d < depth; d++)
            {
                bool hasLaterSibling = false;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (depths[j] == d) { hasLaterSibling = true; break; }
                    if (depths[j] < d) break;
                }
                lineBuilder.Append(hasLaterSibling ? "│  " : "   ");
            }

            // 当前分支符号
            bool isLast = true;
            for (int j = i + 1; j < lines.Length; j++)
            {
                if (depths[j] == depth) { isLast = false; break; }
                if (depths[j] < depth) break;
            }
            lineBuilder.Append(isLast ? "└─ " : "├─ ");
            lineBuilder.Append(name);
            sb.AppendLine(lineBuilder.ToString());
        }
        return sb.ToString();
    }

    private int CountIndentDepth(string line)
    {
        int spaces = 0;
        foreach (char c in line)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += 4;
            else break;
        }
        // 将总空格数转换为深度：每2个空格为一级，兼容习惯
        return spaces / 2;
    }

    // 创建文件夹
    private void CreateFoldersFromText()
    {
        if (string.IsNullOrWhiteSpace(folderStructure))
        {
            EditorUtility.DisplayDialog("提示", "请输入文件夹结构。", "确定");
            return;
        }
        if (!AssetDatabase.IsValidFolder(targetPath))
        {
            EditorUtility.DisplayDialog("错误", $"当前路径 '{targetPath}' 不是有效文件夹。", "确定");
            return;
        }

        var lines = folderStructure.Split('\n');
        var depthStack = new Stack<(int depth, string path)>();
        // 找出第一个有效行的深度作为根深度
        int rootDepth = -1;
        foreach (var line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                rootDepth = CountIndentDepth(trimmed);
                break;
            }
        }
        if (rootDepth == -1) return;

        int created = 0;
        foreach (var line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            string folderName = trimmed.Trim();
            int depth = CountIndentDepth(trimmed) - rootDepth;
            if (depth < 0) depth = 0;

            // 回溯栈到找到父级
            while (depthStack.Count > 0 && depthStack.Peek().depth >= depth)
                depthStack.Pop();

            string parentPath = targetPath;
            if (depthStack.Count > 0)
                parentPath = depthStack.Peek().path;

            string fullPath = Path.Combine(parentPath, folderName);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                created++;
            }
            depthStack.Push((depth, fullPath));
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完成", $"已在 {targetPath} 下创建 {created} 个文件夹。", "确定");
    }
}