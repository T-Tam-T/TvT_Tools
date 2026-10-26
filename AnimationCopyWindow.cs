using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 带动画复制：把 Hierarchy 中带动画（Animator / 旧版 Animation）的对象拖入窗口，
/// 勾选它下面的子对象进行复制。副本放在原层级位置（检测对象本身不复制），
/// 并把被复制对象在现有 .anim 剪辑里的关键帧轨道复制给副本（不新建 .anim / .controller）。
/// 可选同时把材质复制成独立资源。
/// </summary>
public class AnimationCopyWindow : EditorWindow
{
    [MenuItem("Tools/TvTTools/带动画复制", false, 22)]
    [MenuItem("GameObject/TvTTools/带动画复制", false, 52)]
    private static void OpenWindow()
    {
        var window = GetWindow<AnimationCopyWindow>(false, "带动画复制");
        window.minSize = new Vector2(900f, 560f);
        window.AddSelection();
        window.Show();
    }

    private const string PrefPrefix = "TvT.AnimationCopy.";

    // =============================================================
    //  数据
    // =============================================================

    private sealed class Entry
    {
        public GameObject root;
        public bool expanded = true;
        public string analysisMessage = "";
        public List<AnimationCopyExecutor.AnimationSource> sources = new List<AnimationCopyExecutor.AnimationSource>();
        public List<AnimationCopyExecutor.Row> rows = new List<AnimationCopyExecutor.Row>();
        public readonly Dictionary<int, bool> includeStates = new Dictionary<int, bool>();
        public readonly Dictionary<int, string> customNames = new Dictionary<int, string>();
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Vector2 entryScroll;
    private Vector2 targetScroll;
    private string lastMessage = "";
    private MessageType lastMessageType = MessageType.Info;

    // 命名规则（持久化）
    private AnimationCopyExecutor.NameMode nameMode = AnimationCopyExecutor.NameMode.Suffix;
    private string prefix = "";
    private string suffix = "_Copy";
    private string findText = "";
    private string replaceText = "";
    private bool useRegex;
    private string numberPrefix = "Copy_";
    private int startNumber = 1;
    private int step = 1;
    private int digits = 2;

    // 材质命名规则（与对象重命名完全独立）
    private AnimationCopyExecutor.NameMode materialNameMode = AnimationCopyExecutor.NameMode.Suffix;
    private string materialPrefix = "";
    private string materialSuffix = "_Copy";
    private string materialFindText = "";
    private string materialReplaceText = "";
    private bool materialUseRegex;
    private string materialNumberPrefix = "Mat_";
    private int materialStartNumber = 1;
    private int materialStep = 1;
    private int materialDigits = 2;
    private string materialFolder = "";

    private GUIStyle dropStyle;
    private GUIStyle dropLabelStyle;

    // =============================================================
    //  生命周期
    // =============================================================

    private void OnEnable()
    {
        LoadPrefs();
    }

    private void OnDisable()
    {
        SavePrefs();
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.HelpBox(
            "拖入带动画的对象（Animator / 旧版 Animation）→ 在右侧勾选要复制的子对象 → 点“复制本体”或“复制本体及材质”。\n" +
            "副本放在原层级位置（检测对象本身不复制），被复制对象在 .anim 里的关键帧轨道会复制给副本；不新建 .anim / .controller。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawLeftColumn();
        DrawRightColumn();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        DrawBottomButtons();
    }

    // =============================================================
    //  左栏
    // =============================================================

    private void DrawLeftColumn()
    {
        float leftWidth = Mathf.Max(430f, position.width * 0.55f);
        EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth), GUILayout.ExpandHeight(true));

        GUILayout.Label("重命名规则", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawNameRulePanel();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("场景对象/预制体拖拽区域", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("添加选中对象", GUILayout.Width(96f))) AddSelection();
        EditorGUILayout.EndHorizontal();
        DrawDropZone();

        GUILayout.Space(6f);
        DrawEntryList();

        GUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("材质设置（与对象重命名独立）", EditorStyles.boldLabel);
        DrawMaterialRulePanel();
        DrawMaterialFolderRow();
        EditorGUILayout.EndVertical();

        GUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("复制选项", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "不新建 .anim / .controller：勾选对象的关键帧轨道会追加到检测对象已有的剪辑里（会直接修改原剪辑资源）。",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndVertical();
    }

    private void DrawNameRulePanel()
    {
        nameMode = (AnimationCopyExecutor.NameMode)EditorGUILayout.Popup(
            "重命名模式", (int)nameMode, new[] { "加后缀", "加前缀", "查找替换", "自定义序号" });

        switch (nameMode)
        {
            case AnimationCopyExecutor.NameMode.Suffix:
                suffix = EditorGUILayout.TextField("添加后缀:", suffix);
                break;
            case AnimationCopyExecutor.NameMode.Prefix:
                prefix = EditorGUILayout.TextField("添加前缀:", prefix);
                break;
            case AnimationCopyExecutor.NameMode.Replace:
                findText = EditorGUILayout.TextField("查找:", findText);
                replaceText = EditorGUILayout.TextField("替换为:", replaceText);
                useRegex = EditorGUILayout.Toggle("使用正则表达式", useRegex);
                break;
            case AnimationCopyExecutor.NameMode.Numbered:
                numberPrefix = EditorGUILayout.TextField("名称前缀:", numberPrefix);
                EditorGUILayout.BeginHorizontal();
                startNumber = EditorGUILayout.IntField("起始", startNumber);
                step = EditorGUILayout.IntField("步长", step);
                digits = EditorGUILayout.IntField("位数", digits);
                EditorGUILayout.EndHorizontal();
                break;
        }
    }

    private void DrawMaterialRulePanel()
    {
        materialNameMode = (AnimationCopyExecutor.NameMode)EditorGUILayout.Popup(
            "材质重命名模式", (int)materialNameMode, new[] { "加后缀", "加前缀", "查找替换", "自定义序号" });

        switch (materialNameMode)
        {
            case AnimationCopyExecutor.NameMode.Suffix:
                materialSuffix = EditorGUILayout.TextField("添加后缀:", materialSuffix);
                break;
            case AnimationCopyExecutor.NameMode.Prefix:
                materialPrefix = EditorGUILayout.TextField("添加前缀:", materialPrefix);
                break;
            case AnimationCopyExecutor.NameMode.Replace:
                materialFindText = EditorGUILayout.TextField("查找:", materialFindText);
                materialReplaceText = EditorGUILayout.TextField("替换为:", materialReplaceText);
                materialUseRegex = EditorGUILayout.Toggle("使用正则表达式", materialUseRegex);
                break;
            case AnimationCopyExecutor.NameMode.Numbered:
                materialNumberPrefix = EditorGUILayout.TextField("名称前缀:", materialNumberPrefix);
                EditorGUILayout.BeginHorizontal();
                materialStartNumber = EditorGUILayout.IntField("起始", materialStartNumber);
                materialStep = EditorGUILayout.IntField("步长", materialStep);
                materialDigits = EditorGUILayout.IntField("位数", materialDigits);
                EditorGUILayout.EndHorizontal();
                break;
        }

        EditorGUILayout.LabelField(
            "材质名示例：" + AnimationCopyExecutor.PreviewMaterialName("Mat", BuildOptions(true), 0) + ".mat",
            EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawMaterialFolderRow()
    {
        EditorGUILayout.BeginHorizontal();
        materialFolder = EditorGUILayout.TextField("材质复制到:", materialFolder);
        if (GUILayout.Button("选择", GUILayout.Width(52f)))
        {
            string startFolder = string.IsNullOrEmpty(materialFolder) ? "Assets" : materialFolder;
            string selected = EditorUtility.OpenFolderPanel("选择材质复制目录", ToAbsolutePath(startFolder), "");
            if (!string.IsNullOrEmpty(selected)) materialFolder = ToProjectRelativePath(selected);
        }
        if (GUILayout.Button("清空", GUILayout.Width(48f))) materialFolder = "";
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(materialFolder)
                ? "留空 = 复制到原材质所在目录（工程 Assets 内的路径）"
                : "材质将复制到：" + materialFolder,
            EditorStyles.wordWrappedMiniLabel);
    }

    private static string ToAbsolutePath(string projectRelative)
    {
        if (string.IsNullOrEmpty(projectRelative)) return Application.dataPath;
        if (projectRelative.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            projectRelative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            return Path.Combine(projectRoot, projectRelative.Replace('/', Path.DirectorySeparatorChar));
        }
        return projectRelative;
    }

    private static string ToProjectRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return "";
        string projectRoot = (Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath).Replace('\\', '/').TrimEnd('/');
        string normalized = absolutePath.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(projectRoot.Length + 1);
        }
        return normalized;
    }

    private void DrawDropZone()
    {
        var rect = GUILayoutUtility.GetRect(10f, 72f, GUILayout.ExpandWidth(true));
        GUI.Box(rect, GUIContent.none, dropStyle);
        GUI.Label(rect, entries.Count == 0 ? "把 Hierarchy 中带动画的对象拖到这里\n（只接收场景对象 / 预制体实例）" : "拖入更多对象", dropLabelStyle);
        HandleDrop(rect, Event.current);
    }

    private void HandleDrop(Rect rect, Event evt)
    {
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
        if (!rect.Contains(evt.mousePosition)) return;

        bool hasSceneObject = false;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            var go = obj as GameObject;
            if (go != null && !AssetDatabase.Contains(go)) { hasSceneObject = true; break; }
        }
        if (!hasSceneObject) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type != EventType.DragPerform) return;

        DragAndDrop.AcceptDrag();
        foreach (var obj in DragAndDrop.objectReferences)
        {
            var go = obj as GameObject;
            if (go != null && !AssetDatabase.Contains(go)) AddObject(go);
        }
        evt.Use();
    }

    private void DrawEntryList()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("对象列表", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("重新检测", GUILayout.Width(72f))) RefreshAllEntries();
        if (GUILayout.Button("清空列表", GUILayout.Width(72f)))
        {
            entries.Clear();
            lastMessage = "";
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        if (entries.Count == 0)
        {
            EditorGUILayout.LabelField("（空）把带动画的对象拖入上方区域，或点击“添加选中对象”。", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        entryScroll = EditorGUILayout.BeginScrollView(entryScroll, false, false, GUILayout.ExpandHeight(true), GUILayout.MinHeight(120f));
        Entry toRemove = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry == null || entry.root == null) { entries.RemoveAt(i); continue; }
            if (DrawEntry(entry)) toRemove = entry;
        }
        if (toRemove != null) entries.Remove(toRemove);
        EditorGUILayout.EndScrollView();
    }

    private bool DrawEntry(Entry entry)
    {
        bool remove = false;
        string title = entry.root.name + "（" + entry.sources.Count + " 个动画源，" + entry.rows.Count + " 个子对象）";

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        entry.expanded = EditorGUILayout.Foldout(entry.expanded, title, true);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22f))) remove = true;
        EditorGUILayout.EndHorizontal();

        if (entry.expanded)
        {
            if (!string.IsNullOrEmpty(entry.analysisMessage))
            {
                EditorGUILayout.HelpBox(entry.analysisMessage, MessageType.Warning);
            }

            foreach (var source in entry.sources)
            {
                EditorGUILayout.LabelField("· " + source.Label, EditorStyles.miniBoldLabel);
                foreach (var clip in source.clips)
                {
                    DrawClipLine(clip);
                }
            }
        }

        EditorGUILayout.EndVertical();
        return remove;
    }

    private static void DrawClipLine(AnimationClip clip)
    {
        if (clip == null) return;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        string detail = "时长: " + clip.length.ToString("0.###") + "s   FPS: " + clip.frameRate.ToString("0") +
                        "   循环: " + (settings.loopTime ? "是" : "否");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(18f);
        EditorGUILayout.LabelField(clip.name, EditorStyles.miniLabel, GUILayout.Width(150f));
        EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // =============================================================
    //  右栏
    // =============================================================

    private void DrawRightColumn()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        int total = 0;
        int selected = 0;
        foreach (var entry in entries)
        {
            foreach (var row in entry.rows)
            {
                total++;
                if (row.include) selected++;
            }
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("识别到的对象（共 " + total + " 个，含子层级；已勾选 " + selected + " 个）", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(44f))) SetAllIncluded(true);
        if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(52f))) SetAllIncluded(false);
        if (GUILayout.Button("重新识别", GUILayout.Width(72f))) RefreshAllEntries();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(18f);
        GUILayout.Label("原名称", EditorStyles.miniBoldLabel, GUILayout.Width(160f));
        GUILayout.Label("改名后（可直接输入修改）", EditorStyles.miniBoldLabel);
        EditorGUILayout.EndHorizontal();

        targetScroll = EditorGUILayout.BeginScrollView(targetScroll, false, false, GUILayout.ExpandHeight(true));
        if (entries.Count == 0)
        {
            EditorGUILayout.LabelField("拖入带动画的对象后，这里会列出它下面可复制的子对象。", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var entry in entries)
            {
                if (entry == null || entry.root == null) continue;
                DrawEntryTargets(entry);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private void DrawEntryTargets(Entry entry)
    {
        GUILayout.Space(4f);
        EditorGUILayout.LabelField("▼ " + entry.root.name + "（检测对象，不复制）", EditorStyles.miniBoldLabel);
        foreach (var row in entry.rows)
        {
            DrawTargetRow(entry, row);
        }
    }

    private void DrawTargetRow(Entry entry, AnimationCopyExecutor.Row row)
    {
        int id = row.target != null ? row.target.GetInstanceID() : 0;
        var options = BuildOptions(false);
        string preview = AnimationCopyExecutor.PreviewName(row.target != null ? row.target.name : "GameObject", options, row.index);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(18f + row.depth * 12f);

        bool include = EditorGUILayout.Toggle(row.include, GUILayout.Width(16f));
        if (include != row.include)
        {
            SetRowIncluded(entry, row, include);
        }

        GUILayout.Label(row.target != null ? row.target.name : "(null)", GUILayout.Width(160f));

        string shown = string.IsNullOrEmpty(row.customName) ? preview : row.customName;
        EditorGUI.BeginDisabledGroup(!row.include);
        string next = EditorGUILayout.TextField(shown);
        EditorGUI.EndDisabledGroup();
        if (next != shown)
        {
            bool reset = string.IsNullOrEmpty(next) || next == preview;
            row.customName = reset ? "" : next;
            if (id != 0)
            {
                if (reset) entry.customNames.Remove(id);
                else entry.customNames[id] = row.customName;
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private static void SetRowIncluded(Entry entry, AnimationCopyExecutor.Row row, bool include)
    {
        row.include = include;
        if (row.target != null) entry.includeStates[row.target.GetInstanceID()] = include;
        foreach (var child in row.children)
        {
            SetRowIncluded(entry, child, include);
        }
    }

    private void SetAllIncluded(bool include)
    {
        foreach (var entry in entries)
        {
            foreach (var row in entry.rows)
            {
                if (row.parent == null) SetRowIncluded(entry, row, include);
            }
        }
    }

    // =============================================================
    //  底部按钮 / 执行
    // =============================================================

    private void DrawBottomButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("复制本体", GUILayout.Height(30f))) ExecuteCopy(false);
        if (GUILayout.Button("复制本体及材质", GUILayout.Height(30f))) ExecuteCopy(true);
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastMessage))
        {
            EditorGUILayout.HelpBox(lastMessage, lastMessageType);
        }
    }

    private void ExecuteCopy(bool withMaterials)
    {
        SavePrefs();
        RefreshAllEntries();

        var requests = BuildRequests();
        if (requests.Count == 0)
        {
            ShowMessage("请先把 Hierarchy 中带动画的对象拖进窗口。", MessageType.Info);
            return;
        }

        int selectedCount = 0;
        foreach (var request in requests)
        {
            foreach (var row in request.rows)
            {
                if (row.include) selectedCount++;
            }
        }
        if (selectedCount == 0)
        {
            ShowMessage("请在右侧至少勾选一个要复制的子对象。", MessageType.Info);
            return;
        }

        var clipPaths = CollectAnimationClipPaths(requests);
        if (clipPaths.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("即将复制 " + selectedCount + " 个对象。");
            sb.AppendLine();
            sb.AppendLine("会把这些对象在以下 .anim 剪辑中的关键帧轨道复制给副本（直接修改原剪辑资源）：");
            int shown = 0;
            foreach (var path in clipPaths)
            {
                if (shown++ >= 6) { sb.AppendLine("  …… 共 " + clipPaths.Count + " 个"); break; }
                sb.AppendLine("  " + path);
            }
            sb.AppendLine();
            sb.AppendLine("注意：Unity 的撤销（Ctrl+Z）无法恢复剪辑资源里的改动。");
            if (!EditorUtility.DisplayDialog("带动画复制", sb.ToString(), "继续复制", "取消")) return;
        }

        var options = BuildOptions(withMaterials);
        var report = AnimationCopyExecutor.Execute(requests, options);

        var message = new StringBuilder();
        message.Append("完成：复制 " + report.createdRoots + " 个对象（共 " + report.createdObjects + " 个 Transform），");
        message.Append("新增关键帧轨道 " + report.duplicatedCurves + " 条，");
        message.Append("修改剪辑 " + report.modifiedClips.Count + " 个，");
        message.Append("复制材质 " + report.copiedMaterials + " 个。");
        if (report.errors.Count > 0)
        {
            message.AppendLine();
            message.Append("错误：" + JoinList(report.errors, 6));
        }
        if (report.warnings.Count > 0)
        {
            message.AppendLine();
            message.Append("警告：" + JoinList(report.warnings, 6));
        }

        if (report.errors.Count > 0) ShowMessage(message.ToString(), MessageType.Error);
        else if (report.warnings.Count > 0) ShowMessage(message.ToString(), MessageType.Warning);
        else ShowMessage(message.ToString(), MessageType.Info);

        if (report.roots.Count > 0) Selection.objects = report.roots.ToArray();
        Repaint();
    }

    private void ShowMessage(string message, MessageType type)
    {
        lastMessage = message;
        lastMessageType = type;
        Debug.Log("[带动画复制] " + message);
    }

    private static string JoinList(List<string> list, int max)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i >= max) { sb.Append(" ……共 " + list.Count + " 条"); break; }
            if (i > 0) sb.Append("；");
            sb.Append(list[i]);
        }
        return sb.ToString();
    }

    private List<string> CollectAnimationClipPaths(List<AnimationCopyExecutor.Request> requests)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>();
        foreach (var request in requests)
        {
            var sources = AnimationCopyExecutor.GatherAnimationSources(request.detectedRoot);
            foreach (var source in sources)
            {
                foreach (var clip in source.clips)
                {
                    if (clip == null) continue;
                    string path = AssetDatabase.GetAssetPath(clip);
                    if (string.IsNullOrEmpty(path)) continue;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".anim") continue;
                    if (seen.Add(path)) paths.Add(path);
                }
            }
        }
        return paths;
    }

    private AnimationCopyExecutor.Options BuildOptions(bool copyMaterials)
    {
        return new AnimationCopyExecutor.Options
        {
            nameMode = nameMode,
            prefix = prefix,
            suffix = suffix,
            findText = findText,
            replaceText = replaceText,
            useRegex = useRegex,
            numberPrefix = numberPrefix,
            startNumber = startNumber,
            step = step,
            digits = digits,
            copyMaterials = copyMaterials,
            materialNameMode = materialNameMode,
            materialPrefix = materialPrefix,
            materialSuffix = materialSuffix,
            materialFindText = materialFindText,
            materialReplaceText = materialReplaceText,
            materialUseRegex = materialUseRegex,
            materialNumberPrefix = materialNumberPrefix,
            materialStartNumber = materialStartNumber,
            materialStep = materialStep,
            materialDigits = materialDigits,
            materialFolder = materialFolder,
        };
    }

    private List<AnimationCopyExecutor.Request> BuildRequests()
    {
        var requests = new List<AnimationCopyExecutor.Request>();
        foreach (var entry in entries)
        {
            if (entry == null || entry.root == null) continue;
            var request = new AnimationCopyExecutor.Request { detectedRoot = entry.root };
            foreach (var row in entry.rows)
            {
                if (row.target == null) continue;
                request.rows.Add(row);
            }
            requests.Add(request);
        }
        return requests;
    }

    // =============================================================
    //  对象管理
    // =============================================================

    private void AddSelection()
    {
        var selected = Selection.gameObjects;
        foreach (var go in selected)
        {
            if (go != null && !AssetDatabase.Contains(go)) AddObject(go);
        }
    }

    private void AddObject(GameObject go)
    {
        if (go == null) return;
        foreach (var entry in entries)
        {
            if (entry.root == go)
            {
                entry.expanded = true;
                return;
            }
        }

        var newEntry = new Entry { root = go };
        RefreshEntry(newEntry, false);
        entries.Add(newEntry);
        Repaint();
    }

    private void RefreshAllEntries()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] == null || entries[i].root == null) entries.RemoveAt(i);
        }
        foreach (var entry in entries) RefreshEntry(entry, true);
    }

    private void RefreshEntry(Entry entry, bool keepStates)
    {
        entry.sources = AnimationCopyExecutor.GatherAnimationSources(entry.root);
        entry.rows = AnimationCopyExecutor.BuildRows(entry.root);

        foreach (var row in entry.rows)
        {
            if (row.target == null) continue;
            int id = row.target.GetInstanceID();
            if (keepStates)
            {
                if (entry.includeStates.TryGetValue(id, out bool include)) row.include = include;
                if (entry.customNames.TryGetValue(id, out string name)) row.customName = name;
            }
            else
            {
                row.include = true;
                row.customName = "";
            }
        }

        entry.analysisMessage = entry.sources.Count == 0
            ? "没有检测到 Animator / 旧版 Animation 组件：只会复制对象本身（以及可选的材质），不会新增关键帧。"
            : "";
    }

    // =============================================================
    //  持久化
    // =============================================================

    private void LoadPrefs()
    {
        nameMode = (AnimationCopyExecutor.NameMode)EditorPrefs.GetInt(PrefPrefix + "mode", (int)AnimationCopyExecutor.NameMode.Suffix);
        prefix = EditorPrefs.GetString(PrefPrefix + "prefix", "");
        suffix = EditorPrefs.GetString(PrefPrefix + "suffix", "_Copy");
        findText = EditorPrefs.GetString(PrefPrefix + "find", "");
        replaceText = EditorPrefs.GetString(PrefPrefix + "replace", "");
        useRegex = EditorPrefs.GetBool(PrefPrefix + "regex", false);
        numberPrefix = EditorPrefs.GetString(PrefPrefix + "numberPrefix", "Copy_");
        startNumber = EditorPrefs.GetInt(PrefPrefix + "start", 1);
        step = EditorPrefs.GetInt(PrefPrefix + "step", 1);
        digits = EditorPrefs.GetInt(PrefPrefix + "digits", 2);
        materialNameMode = (AnimationCopyExecutor.NameMode)EditorPrefs.GetInt(PrefPrefix + "matMode", (int)AnimationCopyExecutor.NameMode.Suffix);
        materialPrefix = EditorPrefs.GetString(PrefPrefix + "matPrefix", "");
        materialSuffix = EditorPrefs.GetString(PrefPrefix + "matSuffix", "_Copy");
        materialFindText = EditorPrefs.GetString(PrefPrefix + "matFind", "");
        materialReplaceText = EditorPrefs.GetString(PrefPrefix + "matReplace", "");
        materialUseRegex = EditorPrefs.GetBool(PrefPrefix + "matRegex", false);
        materialNumberPrefix = EditorPrefs.GetString(PrefPrefix + "matNumberPrefix", "Mat_");
        materialStartNumber = EditorPrefs.GetInt(PrefPrefix + "matStart", 1);
        materialStep = EditorPrefs.GetInt(PrefPrefix + "matStep", 1);
        materialDigits = EditorPrefs.GetInt(PrefPrefix + "matDigits", 2);
        materialFolder = EditorPrefs.GetString(PrefPrefix + "matFolder", "");
    }

    private void SavePrefs()
    {
        EditorPrefs.SetInt(PrefPrefix + "mode", (int)nameMode);
        EditorPrefs.SetString(PrefPrefix + "prefix", prefix);
        EditorPrefs.SetString(PrefPrefix + "suffix", suffix);
        EditorPrefs.SetString(PrefPrefix + "find", findText);
        EditorPrefs.SetString(PrefPrefix + "replace", replaceText);
        EditorPrefs.SetBool(PrefPrefix + "regex", useRegex);
        EditorPrefs.SetString(PrefPrefix + "numberPrefix", numberPrefix);
        EditorPrefs.SetInt(PrefPrefix + "start", startNumber);
        EditorPrefs.SetInt(PrefPrefix + "step", step);
        EditorPrefs.SetInt(PrefPrefix + "digits", digits);
        EditorPrefs.SetInt(PrefPrefix + "matMode", (int)materialNameMode);
        EditorPrefs.SetString(PrefPrefix + "matPrefix", materialPrefix);
        EditorPrefs.SetString(PrefPrefix + "matSuffix", materialSuffix);
        EditorPrefs.SetString(PrefPrefix + "matFind", materialFindText);
        EditorPrefs.SetString(PrefPrefix + "matReplace", materialReplaceText);
        EditorPrefs.SetBool(PrefPrefix + "matRegex", materialUseRegex);
        EditorPrefs.SetString(PrefPrefix + "matNumberPrefix", materialNumberPrefix);
        EditorPrefs.SetInt(PrefPrefix + "matStart", materialStartNumber);
        EditorPrefs.SetInt(PrefPrefix + "matStep", materialStep);
        EditorPrefs.SetInt(PrefPrefix + "matDigits", materialDigits);
        EditorPrefs.SetString(PrefPrefix + "matFolder", materialFolder);
    }

    // =============================================================
    //  样式
    // =============================================================

    private void EnsureStyles()
    {
        if (dropStyle != null) return;

        const int size = 32;
        const float radius = 8f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var fill = new Color32(56, 56, 56, 255);
        var border = new Color32(32, 32, 32, 255);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float qx = Mathf.Abs(px - size * 0.5f) - (size * 0.5f - radius);
                float qy = Mathf.Abs(py - size * 0.5f) - (size * 0.5f - radius);
                float ox = Mathf.Max(qx, 0f);
                float oy = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                pixels[y * size + x] = dist > -1.2f ? border : fill;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        dropStyle = new GUIStyle { border = new RectOffset(8, 8, 8, 8) };
        dropStyle.normal.background = texture;

        dropLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
    }
}

/// <summary>带动画复制的执行逻辑（不依赖窗口，方便自动化测试）。</summary>
public static class AnimationCopyExecutor
{
    public enum NameMode
    {
        Suffix,
        Prefix,
        Replace,
        Numbered,
    }

    /// <summary>右侧列表里的一行（检测对象层级中的一个子对象）。</summary>
    public sealed class Row
    {
        public Transform target;
        public bool include = true;
        public string customName = "";
        public int depth;
        public int index;
        public Row parent;
        public readonly List<Row> children = new List<Row>();
    }

    /// <summary>一次复制请求：一个检测对象 + 它的全部子对象行。</summary>
    public sealed class Request
    {
        public GameObject detectedRoot;
        public List<Row> rows = new List<Row>();
    }

    /// <summary>复制选项。</summary>
    public sealed class Options
    {
        public NameMode nameMode = NameMode.Suffix;
        public string prefix = "";
        public string suffix = "_Copy";
        public string findText = "";
        public string replaceText = "";
        public bool useRegex;
        public string numberPrefix = "Copy_";
        public int startNumber = 1;
        public int step = 1;
        public int digits = 2;
        public bool copyMaterials;
        public NameMode materialNameMode = NameMode.Suffix;
        public string materialPrefix = "";
        public string materialSuffix = "_Copy";
        public string materialFindText = "";
        public string materialReplaceText = "";
        public bool materialUseRegex;
        public string materialNumberPrefix = "Mat_";
        public int materialStartNumber = 1;
        public int materialStep = 1;
        public int materialDigits = 2;
        public string materialFolder = "";
    }

    /// <summary>复制结果。</summary>
    public sealed class Report
    {
        public int createdRoots;
        public int createdObjects;
        public int copiedMaterials;
        public int duplicatedCurves;
        public readonly List<string> modifiedClips = new List<string>();
        public readonly List<string> copiedMaterialPaths = new List<string>();
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> errors = new List<string>();

        public void Warn(string message)
        {
            if (!string.IsNullOrEmpty(message)) warnings.Add(message);
        }

        public void Error(string message)
        {
            if (!string.IsNullOrEmpty(message)) errors.Add(message);
        }
    }

    /// <summary>检测到的一处动画来源。</summary>
    public sealed class AnimationSource
    {
        public Animator animator;
        public Animation animation;
        public RuntimeAnimatorController controller;
        public AnimationClip[] clips = new AnimationClip[0];

        public string Label
        {
            get
            {
                if (animator != null)
                {
                    string controllerName = controller != null ? controller.name : "无控制器";
                    return "Animator · " + controllerName + "（" + animator.gameObject.name + "）";
                }
                if (animation != null)
                {
                    return "Animation · " + clips.Length + " 个剪辑（" + animation.gameObject.name + "）";
                }
                return "动画";
            }
        }
    }

    // =============================================================
    //  公共入口
    // =============================================================

    /// <summary>收集检测对象自身及子级中的动画来源。</summary>
    public static List<AnimationSource> GatherAnimationSources(GameObject detectedRoot)
    {
        var result = new List<AnimationSource>();
        if (detectedRoot == null) return result;

        foreach (var animator in detectedRoot.GetComponentsInChildren<Animator>(true))
        {
            result.Add(new AnimationSource
            {
                animator = animator,
                controller = animator.runtimeAnimatorController,
                clips = GetEffectiveClips(animator.runtimeAnimatorController),
            });
        }

        foreach (var animation in detectedRoot.GetComponentsInChildren<Animation>(true))
        {
            result.Add(new AnimationSource
            {
                animation = animation,
                clips = GetLegacyClips(animation),
            });
        }

        return result;
    }

    /// <summary>构建检测对象下所有子对象的行（含层级关系）。</summary>
    public static List<Row> BuildRows(GameObject detectedRoot)
    {
        var rows = new List<Row>();
        if (detectedRoot == null || detectedRoot.transform == null) return rows;

        var byTransform = new Dictionary<Transform, Row>();
        int index = 0;
        foreach (var transform in detectedRoot.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform == detectedRoot.transform) continue;
            var row = new Row
            {
                target = transform,
                include = true,
                customName = "",
                depth = GetDepth(transform, detectedRoot.transform),
                index = index++,
            };
            rows.Add(row);
            byTransform[transform] = row;
        }

        foreach (var row in rows)
        {
            if (row.target == null) continue;
            Transform parent = row.target.parent;
            if (parent != null && byTransform.TryGetValue(parent, out Row parentRow))
            {
                row.parent = parentRow;
                parentRow.children.Add(row);
            }
        }

        return rows;
    }

    /// <summary>取得控制器实际用到的剪辑。</summary>
    public static AnimationClip[] GetEffectiveClips(RuntimeAnimatorController controller)
    {
        var list = new List<AnimationClip>();
        if (controller == null) return list.ToArray();

        if (controller is AnimatorOverrideController overrideController)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(pairs);
            foreach (var pair in pairs)
            {
                AddDistinct(list, pair.Value != null ? pair.Value : pair.Key);
            }
        }
        else
        {
            var clips = controller.animationClips;
            if (clips != null)
            {
                foreach (var clip in clips) AddDistinct(list, clip);
            }
        }

        return list.ToArray();
    }

    /// <summary>取得旧版 Animation 组件用到的剪辑。</summary>
    public static AnimationClip[] GetLegacyClips(Animation animation)
    {
        var list = new List<AnimationClip>();
        if (animation == null) return list.ToArray();

        var clips = AnimationUtility.GetAnimationClips(animation.gameObject);
        if (clips != null)
        {
            foreach (var clip in clips) AddDistinct(list, clip);
        }
        AddDistinct(list, animation.clip);
        return list.ToArray();
    }

    /// <summary>按规则预览名字。</summary>
    public static string PreviewName(string original, Options options, int index)
    {
        if (options == null) options = new Options();
        return ApplyNameRule(
            string.IsNullOrEmpty(original) ? "GameObject" : original,
            options.nameMode, options.prefix, options.suffix, options.findText, options.replaceText,
            options.useRegex, options.numberPrefix, options.startNumber, options.step, options.digits, index);
    }

    /// <summary>按材质命名规则预览材质名（与对象命名规则独立）。</summary>
    public static string PreviewMaterialName(string original, Options options, int index)
    {
        if (options == null) options = new Options();
        return ApplyNameRule(
            string.IsNullOrEmpty(original) ? "Material" : original,
            options.materialNameMode, options.materialPrefix, options.materialSuffix,
            options.materialFindText, options.materialReplaceText, options.materialUseRegex,
            options.materialNumberPrefix, options.materialStartNumber, options.materialStep, options.materialDigits, index);
    }

    private static string ApplyNameRule(
        string name, NameMode mode, string prefix, string suffix, string findText, string replaceText,
        bool useRegex, string numberPrefix, int startNumber, int step, int digits, int index)
    {
        switch (mode)
        {
            case NameMode.Suffix:
                return name + (suffix ?? "");
            case NameMode.Prefix:
                return (prefix ?? "") + name;
            case NameMode.Replace:
                if (string.IsNullOrEmpty(findText)) return name;
                if (useRegex)
                {
                    try { return Regex.Replace(name, findText, replaceText ?? ""); }
                    catch { return name; }
                }
                return name.Replace(findText, replaceText ?? "");
            case NameMode.Numbered:
                return (numberPrefix ?? "") + FormatNumber(startNumber + index * step, digits);
            default:
                return name;
        }
    }

    /// <summary>执行复制。</summary>
    public static Report Execute(IList<Request> requests, Options options)
    {
        var report = new Report();
        if (options == null) options = new Options();
        if (requests == null || requests.Count == 0)
        {
            report.Error("没有要复制的对象");
            return report;
        }

        var ctx = new Context(options, report);
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("带动画复制");
        try
        {
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request == null || request.detectedRoot == null)
                {
                    report.Error("第 " + (i + 1) + " 个检测对象为空，已跳过");
                    continue;
                }

                try
                {
                    EditorUtility.DisplayProgressBar(
                        "带动画复制",
                        "正在复制：" + request.detectedRoot.name + "（" + (i + 1) + "/" + requests.Count + "）",
                        (float)i / requests.Count);
                    DuplicateRequest(request, ctx);
                }
                catch (Exception ex)
                {
                    report.Error("复制「" + request.detectedRoot.name + "」失败：" + ex.Message);
                    Debug.LogException(ex);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        return report;
    }

    // =============================================================
    //  内部实现
    // =============================================================

    private struct CopyPair
    {
        public Transform original;
        public Transform copy;
    }

    private sealed class Context
    {
        public readonly Options options;
        public readonly Report report;
        public readonly Dictionary<Material, Material> materialCopies = new Dictionary<Material, Material>();
        private readonly HashSet<string> warnedKeys = new HashSet<string>();
        private int materialIndex;

        public Context(Options options, Report report)
        {
            this.options = options;
            this.report = report;
        }

        public void WarnOnce(string key, string message)
        {
            if (warnedKeys.Add(key)) report.Warn(message);
        }

        public void RegisterModifiedClip(string path)
        {
            if (!report.modifiedClips.Contains(path)) report.modifiedClips.Add(path);
        }

        public Material GetMaterialCopy(Material source)
        {
            if (source == null) return null;
            if (materialCopies.TryGetValue(source, out Material existing)) return existing;

            string assetPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                WarnOnce("mat:" + source.GetInstanceID(), "材质「" + source.name + "」是内置/包内资源，无法复制，副本继续共用原材质");
                materialCopies[source] = null;
                return null;
            }

            string folder = (options.materialFolder ?? "").Trim().Replace('\\', '/');
            if (folder.Length > 0)
            {
                bool insideAssets = folder.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                                    folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
                if (!insideAssets)
                {
                    WarnOnce("matFolder:" + folder,
                        "材质目标目录「" + folder + "」不在工程 Assets 下，已改为复制到原材质所在目录");
                    folder = "";
                }
                else
                {
                    EnsureAssetFolder(folder);
                }
            }
            if (folder.Length == 0)
            {
                folder = Path.GetDirectoryName(assetPath);
                if (string.IsNullOrEmpty(folder)) folder = "Assets";
                folder = folder.Replace('\\', '/');
            }

            string baseName = SanitizeAssetName(PreviewMaterialName(source.name, options, materialIndex));
            if (string.IsNullOrEmpty(baseName)) baseName = "NewMaterial";
            materialIndex++;

            string targetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + ".mat");
            Material copy = null;
            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            try
            {
                if (extension == ".mat")
                {
                    if (AssetDatabase.CopyAsset(assetPath, targetPath))
                    {
                        copy = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                    }
                }
            }
            catch (Exception ex)
            {
                WarnOnce("matCopy:" + source.GetInstanceID(), "复制材质「" + source.name + "」失败：" + ex.Message);
            }

            if (copy == null)
            {
                // 子资源材质（例如模型内嵌材质）用实例化兜底
                try
                {
                    copy = UnityEngine.Object.Instantiate(source);
                    copy.name = Path.GetFileNameWithoutExtension(targetPath);
                    AssetDatabase.CreateAsset(copy, targetPath);
                }
                catch (Exception ex)
                {
                    WarnOnce("matInst:" + source.GetInstanceID(), "复制材质「" + source.name + "」失败：" + ex.Message);
                    copy = null;
                }
            }

            materialCopies[source] = copy;
            if (copy != null)
            {
                report.copiedMaterials++;
                report.copiedMaterialPaths.Add(targetPath);
            }
            return copy;
        }
    }

    private static void DuplicateRequest(Request request, Context ctx)
    {
        var root = request.detectedRoot;
        if (root == null || root.transform == null || !root.scene.IsValid() || AssetDatabase.Contains(root))
        {
            ctx.report.Error("检测对象「" + (root != null ? root.name : "(null)") + "」不是场景对象，已跳过");
            return;
        }

        var included = new HashSet<Transform>();
        var rowByTarget = new Dictionary<Transform, Row>();
        foreach (var row in request.rows)
        {
            if (row == null || row.target == null) continue;
            if (!IsDescendantOf(row.target, root.transform))
            {
                ctx.report.Warn("对象「" + row.target.name + "」不在检测对象「" + root.name + "」的子级中，已跳过");
                continue;
            }
            rowByTarget[row.target] = row;
            if (row.include) included.Add(row.target);
        }

        if (included.Count == 0)
        {
            ctx.report.Error("「" + root.name + "」没有勾选任何要复制的子对象");
            return;
        }

        var sources = GatherAnimationSources(root);

        // 只复制“最上层被勾选”的对象，子级已包含在父级副本里
        var copyRoots = new List<Transform>();
        foreach (var transform in included)
        {
            if (!HasIncludedAncestor(transform, root.transform, included)) copyRoots.Add(transform);
        }
        copyRoots.Sort((a, b) => GetDepth(a, root.transform).CompareTo(GetDepth(b, root.transform)));

        foreach (var copyRoot in copyRoots)
        {
            if (copyRoot == null) continue;
            try
            {
                DuplicateBranch(root, copyRoot, included, rowByTarget, sources, ctx);
            }
            catch (Exception ex)
            {
                ctx.report.Error("复制「" + copyRoot.name + "」失败：" + ex.Message);
                Debug.LogException(ex);
            }
        }
    }

    private static void DuplicateBranch(
        GameObject detectedRoot,
        Transform copyRoot,
        HashSet<Transform> included,
        Dictionary<Transform, Row> rowByTarget,
        List<AnimationSource> sources,
        Context ctx)
    {
        var copy = UnityEngine.Object.Instantiate(copyRoot.gameObject, copyRoot.parent);
        if (copy == null)
        {
            ctx.report.Error("实例化「" + copyRoot.name + "」失败");
            return;
        }

        var pairs = new List<CopyPair>();
        PairTransforms(copyRoot, copy.transform, pairs);

        // 1) 剔除未勾选的子对象
        foreach (var pair in pairs)
        {
            if (pair.copy == null) continue;
            if (IsIncluded(pair.original, included, rowByTarget)) continue;
            UnityEngine.Object.DestroyImmediate(pair.copy.gameObject);
        }

        var alivePairs = new List<CopyPair>();
        foreach (var pair in pairs)
        {
            if (pair.copy == null) continue;
            if (!IsIncluded(pair.original, included, rowByTarget)) continue;
            alivePairs.Add(pair);
        }

        // 2) 按规则改名
        foreach (var pair in alivePairs)
        {
            string desired = GetDesiredName(pair.original, rowByTarget, ctx.options);
            string unique = GameObjectUtility.GetUniqueNameForSibling(pair.copy.parent, desired);
            pair.copy.name = unique;
        }

        // 3) 复制材质（可选）
        if (ctx.options.copyMaterials)
        {
            CopyMaterials(copy, ctx);
        }

        // 4) 把关键帧轨道复制给副本
        foreach (var source in sources)
        {
            Transform sourceRoot = source.animator != null
                ? source.animator.transform
                : (source.animation != null ? source.animation.transform : null);
            if (sourceRoot == null) continue;

            // 动画源在副本内部时，绑定路径相对它不变，不需要复制轨道
            if (sourceRoot == copyRoot || sourceRoot.IsChildOf(copyRoot)) continue;
            if (!copyRoot.IsChildOf(sourceRoot)) continue;

            var pathMap = new Dictionary<string, string>();
            foreach (var pair in alivePairs)
            {
                string oldPath = AnimationUtility.CalculateTransformPath(pair.original, sourceRoot);
                string newPath = AnimationUtility.CalculateTransformPath(pair.copy, sourceRoot);
                if (string.IsNullOrEmpty(oldPath)) continue;
                if (oldPath == newPath) continue;
                pathMap[oldPath] = newPath;
            }
            if (pathMap.Count == 0) continue;

            foreach (var clip in source.clips)
            {
                DuplicateCurves(clip, pathMap, ctx);
            }
        }

        Undo.RegisterCreatedObjectUndo(copy, "带动画复制");
        EditorSceneManager.MarkSceneDirty(copy.scene);

        ctx.report.createdRoots++;
        ctx.report.createdObjects += alivePairs.Count;
        ctx.report.roots.Add(copy);
    }

    private static void PairTransforms(Transform original, Transform copy, List<CopyPair> pairs)
    {
        pairs.Add(new CopyPair { original = original, copy = copy });
        int count = Mathf.Min(original.childCount, copy.childCount);
        for (int i = 0; i < count; i++)
        {
            PairTransforms(original.GetChild(i), copy.GetChild(i), pairs);
        }
    }

    private static bool IsIncluded(Transform transform, HashSet<Transform> included, Dictionary<Transform, Row> rowByTarget)
    {
        return included.Contains(transform) || !rowByTarget.ContainsKey(transform);
    }

    private static string GetDesiredName(Transform original, Dictionary<Transform, Row> rowByTarget, Options options)
    {
        Row row;
        if (rowByTarget.TryGetValue(original, out row) && row != null && !string.IsNullOrEmpty(row.customName))
        {
            return row.customName;
        }
        int index = row != null ? row.index : 0;
        return PreviewName(original != null ? original.name : "GameObject", options, index);
    }

    private static void CopyMaterials(GameObject copyRoot, Context ctx)
    {
        foreach (var renderer in copyRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) continue;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null) continue;
                var copied = ctx.GetMaterialCopy(source);
                if (copied != null && copied != source)
                {
                    materials[i] = copied;
                    changed = true;
                }
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
    }

    private static void DuplicateCurves(AnimationClip clip, Dictionary<string, string> pathMap, Context ctx)
    {
        if (clip == null || pathMap.Count == 0) return;

        string assetPath = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(assetPath))
        {
            ctx.WarnOnce("clipPath:" + clip.GetInstanceID(), "剪辑「" + clip.name + "」不是资源文件，无法写入关键帧");
            return;
        }
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.WarnOnce("clipOutside:" + assetPath, "剪辑「" + clip.name + "」位于工程外（" + assetPath + "），无法写入关键帧");
            return;
        }
        string extension = Path.GetExtension(assetPath).ToLowerInvariant();
        if (extension != ".anim")
        {
            ctx.WarnOnce("clipExt:" + assetPath,
                "剪辑「" + clip.name + "」来自 " + extension + " 模型文件（" + assetPath + "），无法写入关键帧，已跳过");
            return;
        }

        int added = 0;
        var curveBindings = AnimationUtility.GetCurveBindings(clip);
        var newBindings = new List<EditorCurveBinding>();
        var newCurves = new List<AnimationCurve>();
        foreach (var binding in curveBindings)
        {
            string newPath;
            if (!pathMap.TryGetValue(binding.path, out newPath)) continue;
            if (string.IsNullOrEmpty(newPath) || newPath == binding.path) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;
            var newBinding = binding;
            newBinding.path = newPath;
            newBindings.Add(newBinding);
            newCurves.Add(curve);
        }
        for (int i = 0; i < newBindings.Count; i++)
        {
            try
            {
                AnimationUtility.SetEditorCurve(clip, newBindings[i], newCurves[i]);
                added++;
            }
            catch (Exception ex)
            {
                ctx.WarnOnce("curve:" + assetPath + ":" + newBindings[i].path,
                    "写入剪辑「" + clip.name + "」的曲线失败：" + ex.Message);
            }
        }

        var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        var objectNewBindings = new List<EditorCurveBinding>();
        var objectNewKeys = new List<ObjectReferenceKeyframe[]>();
        foreach (var binding in objectBindings)
        {
            string newPath;
            if (!pathMap.TryGetValue(binding.path, out newPath)) continue;
            if (string.IsNullOrEmpty(newPath) || newPath == binding.path) continue;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys == null) continue;
            var newKeys = new ObjectReferenceKeyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                newKeys[i] = new ObjectReferenceKeyframe
                {
                    time = keys[i].time,
                    value = RemapMaterialReference(keys[i].value, ctx),
                };
            }
            var newBinding = binding;
            newBinding.path = newPath;
            objectNewBindings.Add(newBinding);
            objectNewKeys.Add(newKeys);
        }
        for (int i = 0; i < objectNewBindings.Count; i++)
        {
            try
            {
                AnimationUtility.SetObjectReferenceCurve(clip, objectNewBindings[i], objectNewKeys[i]);
                added++;
            }
            catch (Exception ex)
            {
                ctx.WarnOnce("objCurve:" + assetPath + ":" + objectNewBindings[i].path,
                    "写入剪辑「" + clip.name + "」的对象引用曲线失败：" + ex.Message);
            }
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(clip);
            ctx.report.duplicatedCurves += added;
            ctx.RegisterModifiedClip(assetPath);
        }
    }

    private static UnityEngine.Object RemapMaterialReference(UnityEngine.Object value, Context ctx)
    {
        if (value == null) return null;
        Material material = value as Material;
        if (material != null && ctx.materialCopies.TryGetValue(material, out Material copied) && copied != null)
        {
            return copied;
        }
        return value;
    }

    // =============================================================
    //  工具方法
    // =============================================================

    private static void AddDistinct(List<AnimationClip> list, AnimationClip clip)
    {
        if (clip == null || list.Contains(clip)) return;
        list.Add(clip);
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string current = parts.Length > 0 ? parts[0] : "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string FormatNumber(int value, int digits)
    {
        digits = Mathf.Clamp(digits, 1, 6);
        return value.ToString("D" + digits);
    }

    private static string SanitizeAssetName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    private static bool IsDescendantOf(Transform transform, Transform root)
    {
        if (transform == null || root == null) return false;
        return transform != root && transform.IsChildOf(root);
    }

    private static bool HasIncludedAncestor(Transform transform, Transform root, HashSet<Transform> included)
    {
        Transform current = transform.parent;
        while (current != null && current != root)
        {
            if (included.Contains(current)) return true;
            current = current.parent;
        }
        return false;
    }

    private static int GetDepth(Transform transform, Transform root)
    {
        int depth = 0;
        Transform current = transform;
        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }
        return depth - 1 < 0 ? 0 : depth - 1;
    }
}
