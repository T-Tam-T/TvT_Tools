using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 批量改名（动画保持）
///
/// 把需要处理的动画剪辑（AnimationClip 资产）和“挂载该剪辑的对象”（通常带 Animator / Animation 组件）
/// 放入本窗口。工具会识别挂载对象及其整个子层级内的所有 GameObject 名称，按规则批量改名，
/// 并同步改写动画剪辑内部的曲线绑定路径（EditorCurveBinding.path），
/// 确保改名后动画仍然指向改名后的对象、不丢失动画目标。
///
/// 用法：
///  1. 把动画剪辑拖到“动画剪辑”区域，或选中后点“添加选中”。
///  2. 把挂载对象（场景里的 GameObject）拖到“挂载对象”区域，或选中后点“添加选中”。
///  3. 选择重命名规则，在“识别到的对象”区预览改名效果。
///  4. 点“应用改名”，工具会批量改名并同步更新所选剪辑的动画绑定路径。
/// </summary>
public class AnimationRenameWindow : EditorWindow
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

    private List<AnimationClip> clips = new List<AnimationClip>();
    private List<GameObject> mounts = new List<GameObject>();
    private List<GameObject> targets = new List<GameObject>();
    private string statusMessage = "";

    private Vector2 contentScrollPos;
    private Vector2 clipsScrollPos;
    private Vector2 mountsScrollPos;
    private Vector2 previewScrollPos;
    private bool isDraggingSplitter;
    private float rulesPanelHeight = 260f;
    private const float SplitterHeight = 6f;
    private const float MinRulesHeight = 100f;
    private const float MinPreviewHeight = 80f;

    [MenuItem("Tools/TvTTools/批量改名(动画保持)", false, 20)]
    private static void ShowWindow()
    {
        OpenWindow();
    }

    [MenuItem("GameObject/TvTTools/批量改名(动画保持)", false, 50)]
    private static void ShowWindowFromHierarchy()
    {
        OpenWindow();
    }

    private static void OpenWindow()
    {
        var window = GetWindow<AnimationRenameWindow>("批量改名(动画保持)");
        window.minSize = new Vector2(540, 470);
        window.Show();

        // 打开窗口时，把当前选中的动画剪辑 / 场景对象直接带进来
        foreach (var o in Selection.objects)
        {
            if (o is AnimationClip ac)
                window.AddClip(ac);
            else if (o is GameObject go && !AssetDatabase.Contains(go))
                window.AddMount(go);
        }
        window.RebuildTargets();
    }

    private void AddClip(AnimationClip clip)
    {
        if (clip != null && !clips.Contains(clip)) clips.Add(clip);
    }

    private void AddMount(GameObject go)
    {
        if (go != null && !mounts.Contains(go)) mounts.Add(go);
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    // =============================================================
    //  GUI
    // =============================================================

    private void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("批量改名（动画保持）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "放入动画剪辑（AnimationClip）与挂载该剪辑的对象（带 Animator / Animation 组件的场景对象）。\n" +
            "工具会识别该对象及子层级内的全部名称，按规则改名后同步更新剪辑的动画绑定路径，改名后动画不丢失。",
            MessageType.Info);

        float totalHeight = Mathf.Max(position.height - 110f, 260f);
        float maxRulesHeight = totalHeight - MinPreviewHeight - SplitterHeight;

        rulesPanelHeight = Mathf.Clamp(rulesPanelHeight, MinRulesHeight, maxRulesHeight);

        // ========== 上半部分：配置 + 规则（可拖动高度） ==========
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(rulesPanelHeight));
        contentScrollPos = EditorGUILayout.BeginScrollView(contentScrollPos);

        DrawClipsList();
        GUILayout.Space(8);
        DrawMountsList();
        GUILayout.Space(8);
        DrawRule();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ========== 可拖动分割条 ==========
        Rect splitterRect = GUILayoutUtility.GetRect(10, SplitterHeight, GUILayout.ExpandWidth(true));
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(splitterRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
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

        // ========== 下半部分：识别结果预览（占满剩余空间） ==========
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"识别到的对象（共 {targets.Count} 个，含挂载对象及子层级）", EditorStyles.boldLabel);
        if (GUILayout.Button("重新识别", GUILayout.Width(80)))
        {
            RebuildTargets();
            Repaint();
        }
        GUILayout.EndHorizontal();
        DrawPreview();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6);
        DrawApplyButton();

        if (!string.IsNullOrEmpty(statusMessage))
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
    }

    private void DrawClipsList()
    {
        EditorGUILayout.LabelField("① 动画剪辑 (AnimationClip)", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("添加选中", GUILayout.Width(80)))
        {
            foreach (var o in Selection.objects)
                if (o is AnimationClip ac) AddClip(ac);
        }
        if (GUILayout.Button("清空", GUILayout.Width(60)))
            clips.Clear();
        GUILayout.Label("（可把动画文件拖到下方区域）", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();

        Rect dropRect = GUILayoutUtility.GetRect(10, 90, GUILayout.ExpandWidth(true));
        HandleClipDragDrop(dropRect);
        GUILayout.BeginArea(dropRect, EditorStyles.helpBox);
        clipsScrollPos = GUILayout.BeginScrollView(clipsScrollPos);
        for (int i = 0; i < clips.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(clips[i] != null ? clips[i].name : "<已丢失>");
            if (GUILayout.Button("移除", GUILayout.Width(50)))
            {
                clips.RemoveAt(i);
                i--;
            }
            GUILayout.EndHorizontal();
        }
        if (clips.Count == 0)
            GUILayout.Label("尚未添加动画剪辑，可拖拽或点“添加选中”", EditorStyles.centeredGreyMiniLabel);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawMountsList()
    {
        EditorGUILayout.LabelField("② 挂载动画剪辑的对象 (挂载对象/带 Animator 或 Animation 组件)", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("添加选中", GUILayout.Width(80)))
        {
            foreach (var o in Selection.objects)
                if (o is GameObject go && !AssetDatabase.Contains(go)) AddMount(go);
            RebuildTargets();
        }
        if (GUILayout.Button("清空", GUILayout.Width(60)))
        {
            mounts.Clear();
            RebuildTargets();
        }
        GUILayout.Label("（可把场景对象拖到下方区域）", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();

        Rect dropRect = GUILayoutUtility.GetRect(10, 90, GUILayout.ExpandWidth(true));
        HandleMountDragDrop(dropRect);
        GUILayout.BeginArea(dropRect, EditorStyles.helpBox);
        mountsScrollPos = GUILayout.BeginScrollView(mountsScrollPos);
        for (int i = 0; i < mounts.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(mounts[i] != null ? mounts[i].name : "<已丢失>");
            if (GUILayout.Button("移除", GUILayout.Width(50)))
            {
                mounts.RemoveAt(i);
                RebuildTargets();
                i--;
            }
            GUILayout.EndHorizontal();
        }
        if (mounts.Count == 0)
            GUILayout.Label("尚未添加挂载对象，可拖拽或点“添加选中”", EditorStyles.centeredGreyMiniLabel);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawRule()
    {
        GUILayout.Space(2);
        EditorGUILayout.LabelField("③ 重命名规则", EditorStyles.boldLabel);
        currentMode = (RenameMode)EditorGUILayout.EnumPopup("重命名模式", currentMode);
        GUILayout.Space(4);

        switch (currentMode)
        {
            case RenameMode.加前缀:
                prefix = EditorGUILayout.TextField("添加前缀:", prefix);
                break;
            case RenameMode.加后缀:
                suffix = EditorGUILayout.TextField("添加后缀:", suffix);
                break;
            case RenameMode.查找替换:
                DrawReplaceRule();
                break;
            case RenameMode.自定义增量:
                DrawSequenceRule();
                break;
        }
    }

    private void DrawReplaceRule()
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

    private void DrawSequenceRule()
    {
        customPrefix = EditorGUILayout.TextField("自定义前缀:", customPrefix);
        startNumber = EditorGUILayout.IntField("起始数字:", startNumber);
        step = EditorGUILayout.IntField("数字增量:", step);
        padding = EditorGUILayout.IntField("数字位数:", padding);
    }

    private void DrawPreview()
    {
        previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.ExpandHeight(true));
        float halfWidth = (EditorGUIUtility.currentViewWidth - 40) * 0.5f;
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(halfWidth));
        EditorGUILayout.LabelField("原名称", EditorStyles.miniBoldLabel);
        for (int i = 0; i < targets.Count; i++)
        {
            string name = targets[i] != null ? targets[i].name : "";
            EditorGUILayout.LabelField(FormatNameForDisplay(name), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUILayout.Width(halfWidth));
        EditorGUILayout.LabelField("改名后", EditorStyles.miniBoldLabel);
        for (int i = 0; i < targets.Count; i++)
        {
            string name = targets[i] != null ? GetNewName(targets[i].name, i) : "";
            EditorGUILayout.LabelField(FormatNameForDisplay(name), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    private void DrawApplyButton()
    {
        string btnText = "";
        switch (currentMode)
        {
            case RenameMode.加前缀: btnText = "应用前缀"; break;
            case RenameMode.加后缀: btnText = "应用后缀"; break;
            case RenameMode.查找替换: btnText = "执行替换"; break;
            case RenameMode.自定义增量: btnText = "应用序列"; break;
        }
        if (GUILayout.Button(btnText, GUILayout.Height(32)))
            Apply();
    }

    // =============================================================
    //  拖拽
    // =============================================================

    private void HandleClipDragDrop(Rect dropRect)
    {
        var evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var o in DragAndDrop.objectReferences)
            {
                if (o is AnimationClip ac) AddClip(ac);
            }
            evt.Use();
        }
    }

    private void HandleMountDragDrop(Rect dropRect)
    {
        var evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var o in DragAndDrop.objectReferences)
            {
                if (o is GameObject go && !AssetDatabase.Contains(go)) AddMount(go);
            }
            RebuildTargets();
            evt.Use();
        }
    }

    // =============================================================
    //  逻辑
    // =============================================================

    /// <summary>根据挂载对象，识别出其本身 + 整个子层级内所有的 GameObject。</summary>
    private void RebuildTargets()
    {
        targets.Clear();
        var seen = new HashSet<GameObject>();
        foreach (var m in mounts)
        {
            if (m == null) continue;
            foreach (var t in m.GetComponentsInChildren<Transform>(true))
            {
                if (seen.Add(t.gameObject)) targets.Add(t.gameObject);
            }
        }
    }

    private string GetNewName(string original, int index)
    {
        switch (currentMode)
        {
            case RenameMode.加前缀:
                return prefix + original;
            case RenameMode.加后缀:
                return original + suffix;
            case RenameMode.查找替换:
                return ApplyReplaceToName(original);
            case RenameMode.自定义增量:
                int num = startNumber + index * step;
                string padded = padding > 0 ? num.ToString().PadLeft(padding, '0') : num.ToString();
                return $"{customPrefix}_{padded}";
            default:
                return original;
        }
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

        StringBuilder patternBuilder = new StringBuilder();
        foreach (char c in findStr)
        {
            if (c == '*') patternBuilder.Append("(.*)");
            else if (c == '?') patternBuilder.Append("(.)");
            else patternBuilder.Append(Regex.Escape(c.ToString()));
        }
        string findPattern = patternBuilder.ToString();

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

    private void Apply()
    {
        statusMessage = "";
        RebuildTargets();

        if (targets.Count == 0)
        {
            statusMessage = "没有可改名的对象：请先在“② 挂载动画剪辑的对象”里添加场景对象。";
            return;
        }

        // 计算要改名的对象及其新名称
        var nameMap = new Dictionary<GameObject, string>();
        int index = 0;
        foreach (var go in targets)
        {
            if (go == null) continue;
            string newName = GetNewName(go.name, index);
            if (newName != go.name)
                nameMap[go] = newName;
            index++;
        }

        if (nameMap.Count == 0)
        {
            statusMessage = "规则未产生任何名称变化，无需执行。";
            return;
        }

        var pathRemap = BuildPathRemap(nameMap);

        // 记录场景对象撤销
        Undo.RecordObjects(nameMap.Keys.ToArray(), "批量改名(动画保持)");

        // 批量改名
        foreach (var kv in nameMap)
            kv.Key.name = kv.Value;

        // 更新动画剪辑绑定路径
        int updatedBindings = 0;
        foreach (var clip in clips)
            updatedBindings += UpdateClipPaths(clip, pathRemap);

        AssetDatabase.Refresh();
        Repaint();

        string suffixMsg = updatedBindings > 0 ? $"，已同步更新 {updatedBindings} 条动画绑定路径" : "";
        statusMessage = $"已改名 {nameMap.Count} 个对象{suffixMsg}。";
    }

    /// <summary>
    /// 根据各挂载对象，建立“旧相对路径 → 新相对路径”的映射。
    /// 相对路径是相对挂载对象（绑定路径根）的、以 / 分隔的对象名链，例如 “A/B”。
    /// </summary>
    private Dictionary<string, string> BuildPathRemap(Dictionary<GameObject, string> nameMap)
    {
        var map = new Dictionary<string, string>();
        foreach (var m in mounts)
        {
            if (m == null) continue;
            var rootT = m.transform;
            foreach (var t in m.GetComponentsInChildren<Transform>(true))
            {
                if (t == rootT) continue; // 根对象的路径为空串，不需改名

                var chain = GetChildChain(rootT, t);
                if (chain == null || chain.Count == 0) continue;

                var oldSegs = new string[chain.Count];
                var newSegs = new string[chain.Count];
                for (int i = 0; i < chain.Count; i++)
                {
                    oldSegs[i] = chain[i].name;
                    newSegs[i] = nameMap.TryGetValue(chain[i].gameObject, out var nn) ? nn : chain[i].name;
                }

                string oldPath = string.Join("/", oldSegs);
                string newPath = string.Join("/", newSegs);
                if (oldPath == newPath) continue;
                map[oldPath] = newPath;
            }
        }
        return map;
    }

    /// <summary>取 root 到 target 之间的子节点链（不含 root，含 target）。若 target 不在 root 下，返回 null。</summary>
    private static List<Transform> GetChildChain(Transform root, Transform target)
    {
        var chain = new List<Transform>();
        Transform cur = target;
        while (cur != null && cur != root)
        {
            chain.Add(cur);
            cur = cur.parent;
        }
        if (cur != root)
            return null;
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// 改写一个动画剪辑内所有命中改名映射的曲线绑定路径，返回改动的绑定数量。
    /// 动画对象（曲线所指向的 GameObject）通过其绑定路径被引用，改名后同步路径即可不丢失动画。
    /// </summary>
    private int UpdateClipPaths(AnimationClip clip, Dictionary<string, string> pathRemap)
    {
        if (clip == null || pathRemap.Count == 0) return 0;
        int changed = 0;

        Undo.RegisterCompleteObjectUndo(clip, "更新动画绑定路径");

        // 普通曲线（如 Transform、自定义属性）
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (string.IsNullOrEmpty(binding.path)) continue;
            if (!pathRemap.TryGetValue(binding.path, out var newPath) || newPath == binding.path) continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;

            EditorCurveBinding nb = binding;
            nb.path = newPath;
            AnimationUtility.SetEditorCurve(clip, binding, null);
            AnimationUtility.SetEditorCurve(clip, nb, curve);
            changed++;
        }

        // 对象引用曲线（如 Sprite 引用、Material 引用）
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (string.IsNullOrEmpty(binding.path)) continue;
            if (!pathRemap.TryGetValue(binding.path, out var newPath) || newPath == binding.path) continue;

            var refs = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            EditorCurveBinding nb = binding;
            nb.path = newPath;
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clip, nb, refs);
            changed++;
        }

        if (changed > 0)
        {
            EditorUtility.SetDirty(clip);
            if (AssetDatabase.Contains(clip))
                AssetDatabase.SaveAssets();
        }
        return changed;
    }

    // 让结尾空格在预览中更明显：用 · 替代结尾空格
    private string FormatNameForDisplay(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "\"\"";

        int trailing = 0;
        for (int i = name.Length - 1; i >= 0 && name[i] == ' '; i--)
            trailing++;

        if (trailing == 0)
            return name;

        string core = name.Substring(0, name.Length - trailing);
        string markers = new string('·', trailing);
        return core + markers;
    }
}
