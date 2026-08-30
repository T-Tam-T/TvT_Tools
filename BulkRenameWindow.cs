using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class BulkRenameWindow : EditorWindow
{
    private enum RenameMode { 加前缀, 加后缀, 查找替换, 自定义增量 }
    private RenameMode currentMode = RenameMode.加前缀;

    private string prefix = "";
    private string suffix = "";
    private string findStr = "";
    private string replaceStr = "";
    private bool useRegex = false;
    private string customPrefix = "";
    private int startNumber = 1;
    private int step = 1;
    private int padding = 0;

    private List<Object> selectedObjects = new List<Object>();
    private Vector2 scrollPos;
    private Vector2 listScrollPos;
    private float rulesPanelHeight = 200f;
    private bool isDraggingSplitter;
    private const float SplitterHeight = 6f;
    private const float MinRulesHeight = 100f;
    private const float MinPreviewHeight = 80f;

    [MenuItem("Tools/TvTTools/批量改名", false, 19)]
    private static void ShowWindow()
    {
        OpenBulkRenameWindow();
    }

    [MenuItem("GameObject/TvTTools/批量改名", false, 49)]
    private static void ShowWindowFromHierarchy()
    {
        OpenBulkRenameWindow();
    }

    // [MenuItem("GameObject/TvTTools/批量改名", true)]
    // private static bool ValidateBulkRename()
    // {
    //     return Selection.objects.Length > 0;
    // }

    private static void OpenBulkRenameWindow()
    {
        var window = GetWindow<BulkRenameWindow>("批量重命名");
        window.minSize = new Vector2(450, 400);
        window.selectedObjects = Selection.objects.ToList();
        window.Show();
    }

    void OnSelectionChange()
    {
        selectedObjects = Selection.objects.ToList();
        Repaint();
    }

    void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("批量重命名工具", EditorStyles.boldLabel);

        float totalHeight = Mathf.Max(position.height - 100f, 250f);
        float maxRulesHeight = totalHeight - MinPreviewHeight - SplitterHeight;

        // ========== 上半部分：规则区域（可拖动高度） ==========
        rulesPanelHeight = Mathf.Clamp(rulesPanelHeight, MinRulesHeight, maxRulesHeight);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(rulesPanelHeight));
        EditorGUILayout.LabelField("规则设置", EditorStyles.boldLabel);
        currentMode = (RenameMode)EditorGUILayout.EnumPopup("重命名模式", currentMode);
        GUILayout.Space(5);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        switch (currentMode)
        {
            case RenameMode.加前缀:
                DrawPrefixGUI();
                break;
            case RenameMode.加后缀:
                DrawSuffixGUI();
                break;
            case RenameMode.查找替换:
                DrawReplaceGUI();
                break;
            case RenameMode.自定义增量:
                DrawSequenceGUI();
                break;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ========== 可拖动分割条 ==========
        Rect splitterRect = GUILayoutUtility.GetRect(10, SplitterHeight, GUILayout.ExpandWidth(true));
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(splitterRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
        if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
        {
            isDraggingSplitter = true;
            Event.current.Use();
        }
        if (isDraggingSplitter && Event.current.type == EventType.MouseDrag)
        {
            rulesPanelHeight += Event.current.delta.y;
            rulesPanelHeight = Mathf.Clamp(rulesPanelHeight, MinRulesHeight, maxRulesHeight);
            Event.current.Use();
            Repaint();
        }
        if (Event.current.type == EventType.MouseUp)
        {
            isDraggingSplitter = false;
        }

        // ========== 下半部分：名称预览（占满剩余空间） ==========
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField($"名称预览（共 {selectedObjects.Count} 个对象）", EditorStyles.boldLabel);
        listScrollPos = EditorGUILayout.BeginScrollView(listScrollPos, GUILayout.ExpandHeight(true));

        float halfWidth = (EditorGUIUtility.currentViewWidth - 40) * 0.5f;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(GUILayout.Width(halfWidth));
        EditorGUILayout.LabelField("原名称", EditorStyles.miniBoldLabel);
        foreach (var obj in selectedObjects)
        {
            string name = obj is GameObject go ? go.name : obj.name;
            EditorGUILayout.LabelField(FormatNameForDisplay(name), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical(GUILayout.Width(halfWidth));
        EditorGUILayout.LabelField("按规则修改后", EditorStyles.miniBoldLabel);
        var previewNames = GetPreviewNames();
        for (int i = 0; i < previewNames.Count; i++)
        {
            EditorGUILayout.LabelField(FormatNameForDisplay(previewNames[i]), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ========== 底部：执行按钮 ==========
        GUILayout.Space(6);
        DrawApplyButton();
    }

    private void DrawApplyButton()
    {
        string btnText = "";
        System.Action action = null;
        switch (currentMode)
        {
            case RenameMode.加前缀: btnText = "应用前缀"; action = ApplyPrefix; break;
            case RenameMode.加后缀: btnText = "应用后缀"; action = ApplySuffix; break;
            case RenameMode.查找替换: btnText = "执行替换"; action = ApplyReplace; break;
            case RenameMode.自定义增量: btnText = "应用序列"; action = ApplySequence; break;
        }
        if (action != null && GUILayout.Button(btnText, GUILayout.Height(32)))
        {
            action();
        }
    }

    private List<string> GetPreviewNames()
    {
        var list = new List<string>();
        int index = 0;
        foreach (var obj in selectedObjects)
        {
            string name = obj is GameObject go ? go.name : obj.name;
            list.Add(GetPreviewName(name, index));
            index++;
        }
        return list;
    }

    // 让结尾空格在预览中更明显：用 · 替代结尾空格
    private string FormatNameForDisplay(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "\"\"";

        int trailing = 0;
        for (int i = name.Length - 1; i >= 0 && name[i] == ' '; i--)
        {
            trailing++;
        }

        if (trailing == 0)
            return name;

        string core = name.Substring(0, name.Length - trailing);
        string markers = new string('·', trailing);
        return core + markers;
    }

    private string GetPreviewName(string originalName, int index)
    {
        switch (currentMode)
        {
            case RenameMode.加前缀:
                return prefix + originalName;
            case RenameMode.加后缀:
                return originalName + suffix;
            case RenameMode.查找替换:
                return ApplyReplaceToName(originalName);
            case RenameMode.自定义增量:
                int num = startNumber + index * step;
                string paddedNumber = padding > 0 ? num.ToString().PadLeft(padding, '0') : num.ToString();
                return $"{customPrefix}_{paddedNumber}";
            default:
                return originalName;
        }
    }

    private void DrawPrefixGUI()
    {
        prefix = EditorGUILayout.TextField("添加前缀:", prefix);
    }

    private void DrawSuffixGUI()
    {
        suffix = EditorGUILayout.TextField("添加后缀:", suffix);
    }

    private void DrawReplaceGUI()
    {
        findStr = EditorGUILayout.TextField("查找内容:", findStr);
        replaceStr = EditorGUILayout.TextField("替换为:", replaceStr);
        useRegex = EditorGUILayout.Toggle("使用正则表达式", useRegex);
        if (!useRegex)
        {
            EditorGUILayout.HelpBox(
                "通配符（非正则）：\n" +
                "• *  → 匹配任意长度字符（包含 0 个）\n" +
                "• ?  → 匹配单个字符\n" +
                "查找中的 * / ? 会被当成通配符，替换中的 * / ? 会替换为对应位置上匹配到的内容。\n" +
                "例如：查找 \"obj_*\" 替换 \"enemy_*\" → obj_1 / obj_13 都会变成 enemy_1 / enemy_13。",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "正则范例：\n" +
                "• 数字 \\d+  → 一个或多个数字\n" +
                "• 字母 [a-zA-Z]+  → 一个或多个字母\n" +
                "• 任意 .*  → 任意字符\n" +
                "• 分组 (\\d+) 替换用 $1  → 保留匹配内容\n" +
                "• ^ 开头 $ 结尾  → 整名匹配\n" +
                "例：查找 \"^Enemy_(\\d+)$\" 替换 \"Enemy_$1\" 可规范化名称",
                MessageType.Info);
        }
    }

    private void DrawSequenceGUI()
    {
        customPrefix = EditorGUILayout.TextField("自定义前缀:", customPrefix);
        startNumber = EditorGUILayout.IntField("起始数字:", startNumber);
        step = EditorGUILayout.IntField("数字增量:", step);
        padding = EditorGUILayout.IntField("数字位数:", padding);
    }

    private void ApplyPrefix()
    {
        if (string.IsNullOrEmpty(prefix)) return;

        // 处理资源对象
        var assetObjects = selectedObjects.Where(obj =>
            AssetDatabase.Contains(obj) && !(obj is GameObject)).ToList();

        AssetDatabase.StartAssetEditing();
        foreach (var obj in assetObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            AssetDatabase.RenameAsset(path, prefix + obj.name);
        }
        AssetDatabase.StopAssetEditing();

        // 处理场景对象（Hierarchy中的GameObject）
        var sceneObjects = selectedObjects.OfType<GameObject>().ToList();
        Undo.RecordObjects(sceneObjects.ToArray(), "Add Prefix");
        foreach (var go in sceneObjects)
        {
            go.name = prefix + go.name;
        }

        AssetDatabase.Refresh();
    }

    private void ApplySuffix()
    {
        if (string.IsNullOrEmpty(suffix)) return;

        // 处理资源对象
        var assetObjects = selectedObjects.Where(obj =>
            AssetDatabase.Contains(obj) && !(obj is GameObject)).ToList();

        AssetDatabase.StartAssetEditing();
        foreach (var obj in assetObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            AssetDatabase.RenameAsset(path, obj.name + suffix);
        }
        AssetDatabase.StopAssetEditing();

        // 处理场景对象
        var sceneObjects = selectedObjects.OfType<GameObject>().ToList();
        Undo.RecordObjects(sceneObjects.ToArray(), "Add Suffix");
        foreach (var go in sceneObjects)
        {
            go.name = go.name + suffix;
        }

        AssetDatabase.Refresh();
    }

    private string ApplyReplaceToName(string name)
    {
        if (string.IsNullOrEmpty(findStr)) return name;

        if (useRegex)
        {
            try
            {
                return Regex.Replace(name, findStr, replaceStr);
            }
            catch
            {
                return name;
            }
        }

        // 通配符模式：* / ? 转为正则，支持完整通配符
        int wildcardCount = findStr.Count(c => c == '*' || c == '?');
        if (wildcardCount == 0)
            return name.Replace(findStr, replaceStr);

        // 构造匹配用的正则：* -> (.*)  ? -> (.)
        StringBuilder patternBuilder = new StringBuilder();
        foreach (char c in findStr)
        {
            if (c == '*')
            {
                patternBuilder.Append("(.*)");
            }
            else if (c == '?')
            {
                patternBuilder.Append("(.)");
            }
            else
            {
                patternBuilder.Append(Regex.Escape(c.ToString()));
            }
        }
        string findPattern = patternBuilder.ToString();

        // 构造替换字符串：替换中的 * / ? 映射到对应捕获组 $1,$2...
        StringBuilder replaceBuilder = new StringBuilder();
        int groupIndex = 1;
        foreach (char c in replaceStr)
        {
            if ((c == '*' || c == '?') && groupIndex <= wildcardCount)
            {
                replaceBuilder.Append("$").Append(groupIndex);
                groupIndex++;
            }
            else
            {
                replaceBuilder.Append(c);
            }
        }
        string replacePattern = replaceBuilder.ToString();

        try
        {
            return Regex.Replace(name, findPattern, replacePattern);
        }
        catch
        {
            return name;
        }
    }

    private void ApplyReplace()
    {
        if (string.IsNullOrEmpty(findStr)) return;

        var assetObjects = selectedObjects.Where(obj =>
            AssetDatabase.Contains(obj) && !(obj is GameObject)).ToList();

        AssetDatabase.StartAssetEditing();
        foreach (var obj in assetObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            string newName = ApplyReplaceToName(obj.name);
            AssetDatabase.RenameAsset(path, newName);
        }
        AssetDatabase.StopAssetEditing();

        var sceneObjects = selectedObjects.OfType<GameObject>().ToList();
        Undo.RecordObjects(sceneObjects.ToArray(), "Find and Replace");
        foreach (var go in sceneObjects)
        {
            go.name = ApplyReplaceToName(go.name);
        }

        AssetDatabase.Refresh();
        Repaint();
    }

    private void ApplySequence()
    {
        // 处理资源对象
        var assetObjects = selectedObjects.Where(obj =>
            AssetDatabase.Contains(obj) && !(obj is GameObject)).ToList();

        AssetDatabase.StartAssetEditing();
        int assetNumber = startNumber;
        foreach (var obj in assetObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            string paddedNumber = assetNumber.ToString().PadLeft(padding, '0');
            string newName = $"{customPrefix}_{paddedNumber}";
            AssetDatabase.RenameAsset(path, newName);
            assetNumber += step;
        }
        AssetDatabase.StopAssetEditing();

        // 处理场景对象
        var sceneObjects = selectedObjects.OfType<GameObject>().ToList();
        Undo.RecordObjects(sceneObjects.ToArray(), "Apply Sequence");
        int sceneNumber = startNumber + (assetObjects.Count * step);
        foreach (var go in sceneObjects)
        {
            string paddedNumber = sceneNumber.ToString().PadLeft(padding, '0');
            go.name = $"{customPrefix}_{paddedNumber}";
            sceneNumber += step;
        }

        AssetDatabase.Refresh();
    }
}