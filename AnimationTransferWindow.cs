using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 动画转移：把 A（源物体）下勾选的子物体复制到 B（目标物体）下，
/// 并把 A 中这些对象在动画剪辑里的轨道合并到 B 的剪辑里。
/// 支持：勾选复制内容、按名称搜索、按组件过滤、Animator / 旧版 Animation 自动识别、
/// 剪辑按名称匹配（名称不同但唯一时自动配对，也可在“剪辑映射”里手动指定）。
/// </summary>
public class AnimationTransferWindow : EditorWindow
{
    [MenuItem("Tools/TvTTools/动画转移", false, 23)]
    [MenuItem("GameObject/TvTTools/动画转移", false, 53)]
    private static void OpenWindow()
    {
        var window = GetWindow<AnimationTransferWindow>(false, "动画转移");
        window.minSize = new Vector2(980f, 620f);
        window.AddSelection();
        window.Show();
    }

    private const string PrefPrefix = "TvT.AnimationTransfer.";

    // =============================================================
    //  数据
    // =============================================================

    private sealed class ComponentFilterEntry
    {
        public Type type;
        public int count;
    }

    private GameObject source;
    private GameObject target;

    private List<AnimationTransferExecutor.Row> rows = new List<AnimationTransferExecutor.Row>();
    private readonly Dictionary<int, bool> includeStates = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> expandStates = new Dictionary<int, bool>();

    private string searchText = "";
    private Type componentFilterType;
    private readonly List<ComponentFilterEntry> componentFilters = new List<ComponentFilterEntry>();
    private string[] componentPopupLabels = new string[] { "全部组件" };

    private AnimationTransferExecutor.Analysis analysis;
    private bool showAnalysis = true;
    private bool createMissingClips;

    // 剪辑映射：源剪辑 → 0=自动，1=不转移，>=2 为目标剪辑在 B 剪辑列表中的下标 + 2
    private const int ClipChoiceAuto = 0;
    private const int ClipChoiceSkip = 1;
    private const int ClipChoiceTargetOffset = 2;
    private readonly Dictionary<AnimationClip, int> clipChoices = new Dictionary<AnimationClip, int>();
    private AnimationTransferExecutor.Analysis effectiveAnalysis;
    private bool effectiveAnalysisDirty = true;

    private Vector2 treeScroll;
    private Vector2 analysisScroll;

    private string lastMessage = "";
    private MessageType lastMessageType = MessageType.Info;

    private GUIStyle dropStyle;
    private GUIStyle dropLabelStyle;
    private GUIStyle mutedLabelStyle;

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
            "把 A 中勾选的子物体复制到 B 下，并把 A 里这些对象的关键帧轨道合并到 B 的剪辑。\n" +
            "A、B 的 Animator / 旧版 Animation 会自动识别；剪辑优先按名称（忽略大小写）匹配，" +
            "名称不同但两边都只剩一个时自动配对，也可以在“剪辑映射”里手动指定。\n" +
            "左侧可勾选复制内容，支持按名称/路径搜索和按组件过滤。播放模式下不能执行。",
            MessageType.Info);

        DrawObjectSlots();
        GUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        DrawTreeColumn();
        DrawRightColumn();
        EditorGUILayout.EndHorizontal();
    }

    private void AddSelection()
    {
        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return;

        if (selected.Length >= 2)
        {
            if (source == null) SetSource(selected[0]);
            if (target == null && selected[1] != source) SetTarget(selected[1]);
        }
        else if (source == null)
        {
            SetSource(selected[0]);
        }
        else if (target == null && selected[0] != source)
        {
            SetTarget(selected[0]);
        }
    }

    // =============================================================
    //  A / B 选择区
    // =============================================================

    private void DrawObjectSlots()
    {
        EditorGUILayout.BeginHorizontal();

        bool changedSource;
        bool changedTarget;
        var newSource = DrawObjectSlot(
            "源物体 A", "把 A（要复制内容的物体）拖到这里", source, out changedSource);
        GUILayout.Space(6f);
        var newTarget = DrawObjectSlot(
            "目标物体 B", "把 B（接收内容的物体）拖到这里", target, out changedTarget);

        EditorGUILayout.EndHorizontal();

        if (changedSource) SetSource(newSource);
        if (changedTarget) SetTarget(newTarget);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("交换 A / B", EditorStyles.miniButton, GUILayout.Width(90f)))
        {
            var temp = source;
            SetSource(target);
            SetTarget(temp);
        }
        if (GUILayout.Button("用选中对象填充", EditorStyles.miniButton, GUILayout.Width(120f))) AddSelection();
        EditorGUILayout.EndHorizontal();
    }

    private GameObject DrawObjectSlot(string title, string hint, GameObject current, out bool changed)
    {
        changed = false;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(title, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label(current != null ? "已设置" : "未设置", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        var rect = GUILayoutUtility.GetRect(10f, 52f, GUILayout.ExpandWidth(true));
        GUI.Box(rect, GUIContent.none, dropStyle);
        GUI.Label(rect, current != null ? current.name : hint, dropLabelStyle);

        GameObject dropped = null;
        HandleDrop(rect, delegate(GameObject go) { dropped = go; });
        if (dropped != null && dropped != current)
        {
            current = dropped;
            changed = true;
        }

        var next = (GameObject)EditorGUILayout.ObjectField("物体", current, typeof(GameObject), true);
        if (next != current)
        {
            current = next;
            changed = true;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("用当前选中", EditorStyles.miniButton, GUILayout.Width(88f)))
        {
            var active = Selection.activeGameObject;
            if (active != null && active != current)
            {
                current = active;
                changed = true;
            }
        }
        if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(52f)))
        {
            if (current != null)
            {
                current = null;
                changed = true;
            }
        }
        if (current != null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("子对象 " + current.transform.childCount + " 个", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        return current;
    }

    private void HandleDrop(Rect rect, Action<GameObject> assign)
    {
        var evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
        if (!rect.Contains(evt.mousePosition)) return;

        GameObject found = null;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            var go = obj as GameObject;
            if (go != null && !AssetDatabase.Contains(go))
            {
                found = go;
                break;
            }
        }
        if (found == null) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type != EventType.DragPerform) return;

        DragAndDrop.AcceptDrag();
        assign(found);
        evt.Use();
    }

    private void SetSource(GameObject go)
    {
        if (go != null && !IsSceneObject(go))
        {
            ShowMessage("源物体 A 必须是 Hierarchy 中的场景对象（预制体资源请先拖到场景）。", MessageType.Error);
            return;
        }
        if (go == source) return;
        source = go;
        includeStates.Clear();
        expandStates.Clear();
        RefreshAll();
    }

    private void SetTarget(GameObject go)
    {
        if (go != null && !IsSceneObject(go))
        {
            ShowMessage("目标物体 B 必须是 Hierarchy 中的场景对象（预制体资源请先拖到场景）。", MessageType.Error);
            return;
        }
        if (go == target) return;
        target = go;
        RefreshAll();
    }

    private static bool IsSceneObject(GameObject go)
    {
        return go != null && go.scene.IsValid() && !AssetDatabase.Contains(go);
    }

    // =============================================================
    //  左侧：复制内容树
    // =============================================================

    private void DrawTreeColumn()
    {
        float width = Mathf.Max(460f, position.width * 0.54f);
        EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));

        int total = rows.Count;
        int selected = CountSelected();
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("复制内容（共 " + total + " 个，已勾选 " + selected + " 个）", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(44f))) SetAllIncluded(true);
        if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(52f))) SetAllIncluded(false);
        if (GUILayout.Button("全部展开", EditorStyles.miniButton, GUILayout.Width(64f))) SetAllExpanded(true);
        if (GUILayout.Button("全部折叠", EditorStyles.miniButton, GUILayout.Width(64f))) SetAllExpanded(false);
        if (GUILayout.Button("重新识别", EditorStyles.miniButton, GUILayout.Width(64f))) RefreshAll();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
        if (GUILayout.Button("清除搜索", EditorStyles.miniButton, GUILayout.Width(64f))) searchText = "";
        GUILayout.Space(6f);
        GUILayout.Label("组件过滤", EditorStyles.miniLabel, GUILayout.Width(56f));
        int currentFilter = 0;
        for (int i = 0; i < componentFilters.Count; i++)
        {
            if (componentFilters[i].type == componentFilterType)
            {
                currentFilter = i + 1;
                break;
            }
        }
        int nextFilter = EditorGUILayout.Popup(currentFilter, componentPopupLabels, GUILayout.Width(190f));
        if (nextFilter != currentFilter)
        {
            componentFilterType = nextFilter <= 0 || nextFilter > componentFilters.Count
                ? null
                : componentFilters[nextFilter - 1].type;
        }
        EditorGUILayout.EndHorizontal();

        treeScroll = EditorGUILayout.BeginScrollView(treeScroll, false, false, GUILayout.ExpandHeight(true));
        if (source == null)
        {
            EditorGUILayout.LabelField("先设置源物体 A，这里会列出 A 的所有子对象。", EditorStyles.miniLabel);
        }
        else if (rows.Count == 0)
        {
            EditorGUILayout.LabelField("A 下没有子对象。", EditorStyles.miniLabel);
        }
        else
        {
            var visible = ComputeVisibleRows();
            if (visible.Count == 0)
            {
                EditorGUILayout.LabelField("没有匹配搜索/组件过滤的对象。", EditorStyles.miniLabel);
            }
            foreach (var row in rows)
            {
                if (!visible.Contains(row)) continue;
                DrawTreeRow(row);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private void DrawTreeRow(AnimationTransferExecutor.Row row)
    {
        int id = row.target != null ? row.target.GetInstanceID() : 0;
        EditorGUILayout.BeginHorizontal();

        GUILayout.Space(4f + row.depth * 14f);
        if (row.children.Count > 0 && !IsFilterActive())
        {
            bool expanded = IsExpanded(row);
            if (GUILayout.Button(expanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(16f), GUILayout.Height(16f)))
            {
                if (id != 0) expandStates[id] = !expanded;
            }
        }
        else
        {
            GUILayout.Space(18f);
        }

        bool include = EditorGUILayout.Toggle(row.include, GUILayout.Width(16f));
        if (include != row.include)
        {
            SetRowIncluded(row, include);
        }

        var nameStyle = row.include ? EditorStyles.label : mutedLabelStyle;
        GUILayout.Label(row.target != null ? row.target.name : "(null)", nameStyle);
        if (!string.IsNullOrEmpty(row.componentTags))
        {
            GUILayout.Label(row.componentTags, EditorStyles.miniLabel);
        }
        if (row.target != null && row.target.gameObject.activeSelf == false)
        {
            GUILayout.Label("(未激活)", EditorStyles.miniLabel);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private bool IsFilterActive()
    {
        return !string.IsNullOrWhiteSpace(searchText) || componentFilterType != null;
    }

    private HashSet<AnimationTransferExecutor.Row> ComputeVisibleRows()
    {
        var visible = new HashSet<AnimationTransferExecutor.Row>();
        if (!IsFilterActive())
        {
            foreach (var row in rows)
            {
                bool hidden = false;
                var parent = row.parent;
                while (parent != null)
                {
                    if (parent.children.Count > 0 && !IsExpanded(parent))
                    {
                        hidden = true;
                        break;
                    }
                    parent = parent.parent;
                }
                if (!hidden) visible.Add(row);
            }
            return visible;
        }

        foreach (var row in rows)
        {
            if (!MatchesFilter(row)) continue;
            visible.Add(row);
            var parent = row.parent;
            while (parent != null)
            {
                visible.Add(parent);
                parent = parent.parent;
            }
        }
        return visible;
    }

    private bool MatchesFilter(AnimationTransferExecutor.Row row)
    {
        if (row == null || row.target == null) return false;

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string query = searchText.Trim();
            bool hit = row.target.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hit && !string.IsNullOrEmpty(row.path))
            {
                hit = row.path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (!hit) return false;
        }

        if (componentFilterType != null)
        {
            var component = row.target.GetComponent(componentFilterType);
            if (component == null) return false;
        }
        return true;
    }

    private bool IsExpanded(AnimationTransferExecutor.Row row)
    {
        if (row == null || row.target == null) return true;
        int id = row.target.GetInstanceID();
        bool expanded;
        if (expandStates.TryGetValue(id, out expanded)) return expanded;
        return true;
    }

    private void SetRowIncluded(AnimationTransferExecutor.Row row, bool include)
    {
        if (row == null) return;
        row.include = include;
        if (row.target != null) includeStates[row.target.GetInstanceID()] = include;
        foreach (var child in row.children)
        {
            SetRowIncluded(child, include);
        }
    }

    private void SetAllIncluded(bool include)
    {
        foreach (var row in rows)
        {
            if (row.parent == null) SetRowIncluded(row, include);
        }
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var row in rows)
        {
            if (row.target != null && row.children.Count > 0)
            {
                expandStates[row.target.GetInstanceID()] = expanded;
            }
        }
    }

    private int CountSelected()
    {
        int count = 0;
        foreach (var row in rows)
        {
            if (row.include) count++;
        }
        return count;
    }

    // =============================================================
    //  右侧：分析 / 选项 / 执行
    // =============================================================

    private void DrawRightColumn()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginHorizontal();
        showAnalysis = EditorGUILayout.Foldout(showAnalysis, "动画分析", true);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("重新分析", EditorStyles.miniButton, GUILayout.Width(72f)))
        {
            RefreshAnalysis();
        }
        if (GUILayout.Button("重置映射", EditorStyles.miniButton, GUILayout.Width(68f)))
        {
            ResetClipChoices();
        }
        EditorGUILayout.EndHorizontal();

        if (showAnalysis)
        {
            analysisScroll = EditorGUILayout.BeginScrollView(
                analysisScroll, false, false, GUILayout.MaxHeight(250f), GUILayout.ExpandHeight(false));
            DrawAnalysis();
            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(6f);
        createMissingClips = EditorGUILayout.ToggleLeft(
            new GUIContent(
                "目标缺少同名剪辑时，新建 .anim 并加入 B 的 AnimatorController",
                "仅在 B 使用 AnimatorController 时有效。新建剪辑会作为一个新状态加入控制器的默认层，需要手动接到状态机里。"),
            createMissingClips);

        EditorGUILayout.HelpBox(
            "轨道会直接写入 B 的 .anim 剪辑资源（同名剪辑中已有的同名轨道会被覆盖）。\n" +
            "Unity 的撤销（Ctrl+Z）不保证能恢复剪辑资源里的改动，执行前请确认或备份。",
            MessageType.None);

        GUILayout.FlexibleSpace();
        DrawExecuteArea();
        EditorGUILayout.EndVertical();
    }

    private void DrawAnalysis()
    {
        if (source == null || target == null)
        {
            EditorGUILayout.LabelField("设置 A、B 后自动分析动画源和剪辑匹配。", EditorStyles.miniLabel);
            return;
        }
        if (analysis == null)
        {
            EditorGUILayout.LabelField("尚未分析。", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.LabelField(
            "A 动画源：" + analysis.sourceSources.Count + " 个，剪辑 " + analysis.sourceClipCount + " 个",
            EditorStyles.miniBoldLabel);
        foreach (var animationSource in analysis.sourceSources)
        {
            EditorGUILayout.LabelField("· " + animationSource.Label, EditorStyles.miniLabel);
            foreach (var clip in animationSource.clips)
            {
                if (clip == null) continue;
                EditorGUILayout.LabelField("    " + clip.name + "（" + clip.length.ToString("0.###") + "s）",
                    EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.LabelField(
            "B 动画源：" + analysis.targetSources.Count + " 个，剪辑 " + analysis.targetClipCount + " 个",
            EditorStyles.miniBoldLabel);
        foreach (var animationSource in analysis.targetSources)
        {
            EditorGUILayout.LabelField("· " + animationSource.Label, EditorStyles.miniLabel);
            foreach (var clip in animationSource.clips)
            {
                if (clip == null) continue;
                EditorGUILayout.LabelField("    " + clip.name, EditorStyles.miniLabel);
            }
        }

        if (analysis.matches.Count > 0)
        {
            EditorGUILayout.Space(4f);
            var targetClips = GetTargetClips();
            EditorGUILayout.LabelField(
                "剪辑映射（源剪辑 → B 的剪辑）：可转移 " + analysis.matchedClipCount + " 个，目标缺少 " +
                analysis.missingClipCount + " 个",
                EditorStyles.miniBoldLabel);
            int shown = 0;
            foreach (var match in analysis.matches)
            {
                if (shown++ >= 80)
                {
                    EditorGUILayout.LabelField("    …… 共 " + analysis.matches.Count + " 个", EditorStyles.miniLabel);
                    break;
                }
                var sourceClip = match != null ? match.sourceClip : null;
                if (sourceClip == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("    " + sourceClip.name, EditorStyles.miniLabel, GUILayout.Width(170f));

                string[] options = new string[targetClips.Count + ClipChoiceTargetOffset];
                options[ClipChoiceAuto] = "自动匹配" + AutoMatchHint(match);
                options[ClipChoiceSkip] = "不转移";
                for (int i = 0; i < targetClips.Count; i++)
                {
                    string label = "→ " + targetClips[i].name;
                    if (CountClipsNamed(targetClips, targetClips[i].name) > 1) label += "（" + i + "）";
                    options[i + ClipChoiceTargetOffset] = label;
                }

                int choice = GetClipChoice(sourceClip);
                if (choice < 0 || choice >= options.Length) choice = ClipChoiceAuto;
                int newChoice = EditorGUILayout.Popup(choice, options, GUILayout.Width(250f));
                if (newChoice != choice)
                {
                    SetClipChoice(sourceClip, newChoice);
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        foreach (var warning in analysis.warnings)
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }

    private void DrawExecuteArea()
    {
        int selected = CountSelected();
        string targetName = target != null ? target.name : "B";
        EditorGUILayout.LabelField(
            "将把勾选的 " + selected + " 个子对象复制到「" + targetName + "」下。",
            EditorStyles.miniBoldLabel);

        var effective = GetEffectiveAnalysis();
        if (effective != null)
        {
            string line = "可合并的剪辑：" + effective.matchedClipCount + " / " + effective.matches.Count;
            if (effective.missingClipCount > 0) line += "（目标缺少 " + effective.missingClipCount + " 个）";
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox(
                "当前处于播放模式（Play Mode），不能执行转移。\n" +
                "播放模式下 Unity 不允许记录撤销，复制到一半会失败，动画轨道也不会写入。请先退出播放模式。",
                MessageType.Error);
        }

        if (GUILayout.Button("开始转移", GUILayout.Height(34f)))
        {
            ExecuteTransfer();
        }

        if (!string.IsNullOrEmpty(lastMessage))
        {
            EditorGUILayout.HelpBox(lastMessage, lastMessageType);
        }
    }

    // =============================================================
    //  刷新 / 组件过滤
    // =============================================================

    private void RefreshAll()
    {
        rows = source != null
            ? AnimationTransferExecutor.BuildRows(source)
            : new List<AnimationTransferExecutor.Row>();

        foreach (var row in rows)
        {
            if (row.target == null) continue;
            bool include;
            if (includeStates.TryGetValue(row.target.GetInstanceID(), out include))
            {
                row.include = include;
            }
        }

        clipChoices.Clear();
        RefreshComponentFilters();
        RefreshAnalysis();
    }

    private void RefreshAnalysis()
    {
        analysis = source != null && target != null
            ? AnimationTransferExecutor.Analyze(source, target)
            : null;
        effectiveAnalysisDirty = true;
        Repaint();
    }

    private void ResetClipChoices()
    {
        clipChoices.Clear();
        RefreshAnalysis();
    }

    private int GetClipChoice(AnimationClip clip)
    {
        int choice;
        if (clip != null && clipChoices.TryGetValue(clip, out choice)) return choice;
        return ClipChoiceAuto;
    }

    private void SetClipChoice(AnimationClip clip, int choice)
    {
        if (clip == null) return;
        if (choice == ClipChoiceAuto) clipChoices.Remove(clip);
        else clipChoices[clip] = choice;
        effectiveAnalysisDirty = true;
    }

    private List<AnimationClip> GetTargetClips()
    {
        var clips = new List<AnimationClip>();
        if (analysis == null) return clips;
        foreach (var targetSource in analysis.targetSources)
        {
            if (targetSource.clips == null) continue;
            foreach (var clip in targetSource.clips)
            {
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            }
        }
        return clips;
    }

    private static int CountClipsNamed(List<AnimationClip> clips, string name)
    {
        int count = 0;
        foreach (var clip in clips)
        {
            if (clip != null && string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase)) count++;
        }
        return count;
    }

    private static string AutoMatchHint(AnimationTransferExecutor.ClipMatch match)
    {
        if (match == null || match.targetClip == null) return "（目标缺少同名剪辑）";
        if (match.targetClip == match.sourceClip) return "（A、B 共用同一剪辑）";
        return "（" + match.targetClip.name + "）";
    }

    private void BuildClipOverrides(out Dictionary<AnimationClip, AnimationClip> overrides,
        out HashSet<AnimationClip> skipped)
    {
        overrides = new Dictionary<AnimationClip, AnimationClip>();
        skipped = new HashSet<AnimationClip>();
        if (analysis == null) return;

        var targetClips = GetTargetClips();
        foreach (var match in analysis.matches)
        {
            var sourceClip = match != null ? match.sourceClip : null;
            if (sourceClip == null) continue;
            int choice = GetClipChoice(sourceClip);
            if (choice == ClipChoiceSkip)
            {
                skipped.Add(sourceClip);
            }
            else if (choice >= ClipChoiceTargetOffset)
            {
                int index = choice - ClipChoiceTargetOffset;
                if (index >= 0 && index < targetClips.Count) overrides[sourceClip] = targetClips[index];
            }
        }
    }

    private AnimationTransferExecutor.Analysis GetEffectiveAnalysis()
    {
        if (analysis == null) return null;
        if (!effectiveAnalysisDirty && effectiveAnalysis != null) return effectiveAnalysis;

        Dictionary<AnimationClip, AnimationClip> overrides;
        HashSet<AnimationClip> skipped;
        BuildClipOverrides(out overrides, out skipped);
        effectiveAnalysis = AnimationTransferExecutor.Analyze(source, target, overrides, skipped);
        effectiveAnalysisDirty = false;
        return effectiveAnalysis;
    }

    private void RefreshComponentFilters()
    {
        var counts = new Dictionary<Type, int>();
        var order = new List<Type>();
        foreach (var row in rows)
        {
            if (row.target == null) continue;
            var components = row.target.GetComponents<Component>();
            if (components == null) continue;
            var seen = new HashSet<Type>();
            foreach (var component in components)
            {
                if (component == null) continue;
                Type type = component.GetType();
                if (typeof(Transform).IsAssignableFrom(type)) continue;
                if (!seen.Add(type)) continue;
                int count;
                if (!counts.TryGetValue(type, out count))
                {
                    counts[type] = 0;
                    order.Add(type);
                }
                counts[type] = count + 1;
            }
        }

        componentFilters.Clear();
        foreach (var type in order)
        {
            componentFilters.Add(new ComponentFilterEntry { type = type, count = counts[type] });
        }
        componentFilters.Sort(delegate(ComponentFilterEntry a, ComponentFilterEntry b)
        {
            int byCount = b.count.CompareTo(a.count);
            if (byCount != 0) return byCount;
            return string.Compare(a.type.Name, b.type.Name, StringComparison.OrdinalIgnoreCase);
        });

        if (componentFilterType != null)
        {
            bool found = false;
            foreach (var entry in componentFilters)
            {
                if (entry.type == componentFilterType)
                {
                    found = true;
                    break;
                }
            }
            if (!found) componentFilterType = null;
        }

        componentPopupLabels = new string[componentFilters.Count + 1];
        componentPopupLabels[0] = "全部组件";
        for (int i = 0; i < componentFilters.Count; i++)
        {
            componentPopupLabels[i + 1] = componentFilters[i].type.Name + " (" + componentFilters[i].count + ")";
        }
    }

    // =============================================================
    //  执行
    // =============================================================

    private void ExecuteTransfer()
    {
        SavePrefs();

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            ShowMessage("当前处于播放模式（Play Mode），不能执行动画转移。请先退出播放模式（Exit Play Mode）再试。",
                MessageType.Error);
            return;
        }

        if (!ValidateInputs(out string error))
        {
            ShowMessage(error, MessageType.Error);
            return;
        }
        if (CountSelected() == 0)
        {
            ShowMessage("请至少勾选一个要复制的子对象。", MessageType.Info);
            return;
        }

        var currentAnalysis = GetEffectiveAnalysis() ?? AnimationTransferExecutor.Analyze(source, target);

        var dialog = new StringBuilder();
        dialog.AppendLine("将把 A「" + source.name + "」中勾选的 " + CountSelected() + " 个子对象复制到 B「" + target.name + "」下。");
        if (currentAnalysis.matches.Count > 0)
        {
            dialog.AppendLine("会把 A 的 " + currentAnalysis.matches.Count + " 个剪辑的轨道合并到 B 的同名剪辑（可匹配 "
                              + currentAnalysis.matchedClipCount + " 个，目标缺少 " + currentAnalysis.missingClipCount + " 个）。");
            if (createMissingClips && currentAnalysis.missingClipCount > 0)
            {
                dialog.AppendLine("目标缺少的剪辑会新建 .anim 并加入 B 的 AnimatorController。");
            }
            dialog.AppendLine();
            dialog.AppendLine("注意：会直接修改 B 的 .anim 剪辑资源，Unity 撤销不保证能恢复。");
        }
        else if (currentAnalysis.sourceSources.Count == 0)
        {
            dialog.AppendLine("A 未检测到动画源，本次只会复制对象，不会转移轨道。");
        }

        if (!EditorUtility.DisplayDialog("动画转移", dialog.ToString(), "开始转移", "取消")) return;

        Dictionary<AnimationClip, AnimationClip> clipOverrides;
        HashSet<AnimationClip> skippedClips;
        BuildClipOverrides(out clipOverrides, out skippedClips);

        var request = new AnimationTransferExecutor.Request
        {
            source = source,
            target = target,
            rows = new List<AnimationTransferExecutor.Row>(rows),
            clipOverrides = clipOverrides,
            skippedClips = skippedClips,
        };
        var options = new AnimationTransferExecutor.Options
        {
            createMissingClips = createMissingClips,
        };

        var report = AnimationTransferExecutor.Execute(request, options);

        var message = new StringBuilder();
        message.Append("完成：复制 " + report.createdRoots + " 个根（共 " + report.createdObjects + " 个对象");
        if (report.helperObjects > 0) message.Append("，其中 " + report.helperObjects + " 个是为了保留层级而带上的父级");
        message.Append("），转移轨道 " + report.transferredCurves + " 条");
        if (report.overwrittenCurves > 0) message.Append("（覆盖 " + report.overwrittenCurves + " 条）");
        message.Append("，修改剪辑 " + report.modifiedClips.Count + " 个");
        if (report.createdClips.Count > 0) message.Append("，新建剪辑 " + report.createdClips.Count + " 个");
        if (report.missingClips.Count > 0) message.Append("，目标缺少剪辑 " + report.missingClips.Count + " 个");
        message.Append("。");
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
        if (report.missingClips.Count > 0)
        {
            message.AppendLine();
            message.Append("缺少的剪辑：" + JoinList(report.missingClips, 6));
        }

        if (report.errors.Count > 0) ShowMessage(message.ToString(), MessageType.Error);
        else if (report.warnings.Count > 0 || report.missingClips.Count > 0) ShowMessage(message.ToString(), MessageType.Warning);
        else ShowMessage(message.ToString(), MessageType.Info);

        if (report.roots.Count > 0) Selection.objects = report.roots.ToArray();
        RefreshAll();
        Repaint();
    }

    private bool ValidateInputs(out string error)
    {
        error = "";
        if (source == null)
        {
            error = "请先设置源物体 A。";
            return false;
        }
        if (target == null)
        {
            error = "请先设置目标物体 B。";
            return false;
        }
        if (source == target)
        {
            error = "A 和 B 是同一个物体，无法转移。";
            return false;
        }
        if (!IsSceneObject(source))
        {
            error = "源物体 A 不是场景对象。";
            return false;
        }
        if (!IsSceneObject(target))
        {
            error = "目标物体 B 不是场景对象。";
            return false;
        }
        if (target.transform.IsChildOf(source.transform))
        {
            error = "目标物体 B 位于源物体 A 内部，请先调整层级。";
            return false;
        }
        return true;
    }

    private void ShowMessage(string message, MessageType type)
    {
        lastMessage = message;
        lastMessageType = type;
        if (!string.IsNullOrEmpty(message)) Debug.Log("[动画转移] " + message);
    }

    private static string JoinList(List<string> list, int max)
    {
        var sb = new StringBuilder();
        int shown = 0;
        foreach (var item in list)
        {
            if (shown >= max)
            {
                sb.Append("…… 共 " + list.Count + " 项");
                break;
            }
            if (shown > 0) sb.Append("；");
            sb.Append(item);
            shown++;
        }
        return sb.ToString();
    }

    // =============================================================
    //  偏好设置 / 样式
    // =============================================================

    private void LoadPrefs()
    {
        createMissingClips = EditorPrefs.GetBool(PrefPrefix + "createMissingClips", false);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetBool(PrefPrefix + "createMissingClips", createMissingClips);
    }

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

        mutedLabelStyle = new GUIStyle(EditorStyles.label);
        mutedLabelStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    }
}

/// <summary>动画转移的执行逻辑（不依赖窗口，方便自动化测试）。</summary>
public static class AnimationTransferExecutor
{
    // =============================================================
    //  数据结构
    // =============================================================

    /// <summary>左侧列表里的一行（A 层级中的一个子对象）。</summary>
    public sealed class Row
    {
        public Transform target;
        public bool include = true;
        public int depth;
        public int index;
        public string path = "";
        public string componentTags = "";
        public Row parent;
        public readonly List<Row> children = new List<Row>();
    }

    /// <summary>一次转移请求：源物体 + 目标物体 + A 的全部子对象行。</summary>
    public sealed class Request
    {
        public GameObject source;
        public GameObject target;
        public List<Row> rows = new List<Row>();

        /// <summary>手动指定的剪辑映射：源剪辑 → B 的剪辑（优先于按名称匹配）。</summary>
        public Dictionary<AnimationClip, AnimationClip> clipOverrides = new Dictionary<AnimationClip, AnimationClip>();

        /// <summary>这些源剪辑不转移轨道（但对象仍会复制）。</summary>
        public HashSet<AnimationClip> skippedClips = new HashSet<AnimationClip>();
    }

    /// <summary>转移选项。</summary>
    public sealed class Options
    {
        /// <summary>目标缺少同名剪辑时，是否新建 .anim 并加入目标 AnimatorController。</summary>
        public bool createMissingClips;
    }

    /// <summary>检测到的一处动画来源。</summary>
    public sealed class AnimationSource
    {
        public Animator animator;
        public Animation animation;
        public RuntimeAnimatorController controller;
        public AnimationClip[] clips = new AnimationClip[0];

        public Transform Root
        {
            get
            {
                if (animator != null) return animator.transform;
                if (animation != null) return animation.transform;
                return null;
            }
        }

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

    /// <summary>A 的一个剪辑和 B 中按名称匹配到的剪辑。</summary>
    public sealed class ClipMatch
    {
        public AnimationClip sourceClip;
        public AnimationSource sourceSource;
        public AnimationSource targetSource;
        public AnimationClip targetClip;
    }

    /// <summary>转移前的分析结果。</summary>
    public sealed class Analysis
    {
        public readonly List<AnimationSource> sourceSources = new List<AnimationSource>();
        public readonly List<AnimationSource> targetSources = new List<AnimationSource>();
        public readonly List<ClipMatch> matches = new List<ClipMatch>();
        public readonly List<string> warnings = new List<string>();
        public int sourceClipCount;
        public int targetClipCount;
        public int matchedClipCount;
        public int missingClipCount;
    }

    /// <summary>转移结果。</summary>
    public sealed class Report
    {
        public int createdRoots;
        public int createdObjects;
        public int helperObjects;
        public int transferredCurves;
        public int overwrittenCurves;
        public readonly List<string> modifiedClips = new List<string>();
        public readonly List<string> createdClips = new List<string>();
        public readonly List<string> missingClips = new List<string>();
        public readonly List<string> clipDetails = new List<string>();
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

    // =============================================================
    //  公共入口
    // =============================================================

    /// <summary>收集对象自身及子级中的动画来源；子级没有时向父级查找。</summary>
    public static List<AnimationSource> GatherAnimationSources(GameObject root)
    {
        var result = new List<AnimationSource>();
        if (root == null) return result;

        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        {
            result.Add(new AnimationSource
            {
                animator = animator,
                controller = animator.runtimeAnimatorController,
                clips = GetEffectiveClips(animator.runtimeAnimatorController),
            });
        }

        foreach (var animation in root.GetComponentsInChildren<Animation>(true))
        {
            result.Add(new AnimationSource
            {
                animation = animation,
                clips = GetLegacyClips(animation),
            });
        }

        if (result.Count > 0) return result;

        // 动画面板挂在父级时也能识别（例如 A 只是角色下面的一个分支）
        Transform current = root.transform.parent;
        while (current != null)
        {
            var animator = current.GetComponent<Animator>();
            if (animator != null)
            {
                result.Add(new AnimationSource
                {
                    animator = animator,
                    controller = animator.runtimeAnimatorController,
                    clips = GetEffectiveClips(animator.runtimeAnimatorController),
                });
                break;
            }
            var animation = current.GetComponent<Animation>();
            if (animation != null)
            {
                result.Add(new AnimationSource
                {
                    animation = animation,
                    clips = GetLegacyClips(animation),
                });
                break;
            }
            current = current.parent;
        }

        return result;
    }

    /// <summary>构建 A 下所有子对象的行（父级行先于子级行）。</summary>
    public static List<Row> BuildRows(GameObject source)
    {
        var rows = new List<Row>();
        if (source == null) return rows;
        int index = 0;
        AddRows(source.transform, null, 0, "", rows, ref index);
        return rows;
    }

    private static void AddRows(Transform parent, Row parentRow, int depth, string parentPath,
        List<Row> rows, ref int index)
    {
        int count = parent.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;
            string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;
            var row = new Row
            {
                target = child,
                include = true,
                depth = depth,
                index = index++,
                path = path,
                componentTags = BuildComponentTags(child),
                parent = parentRow,
            };
            rows.Add(row);
            if (parentRow != null) parentRow.children.Add(row);
            AddRows(child, row, depth + 1, path, rows, ref index);
        }
    }

    /// <summary>分析 A / B 的动画源和剪辑匹配情况（不修改任何资源）。</summary>
    public static Analysis Analyze(GameObject source, GameObject target)
    {
        return Analyze(source, target, null, null);
    }

    /// <summary>
    /// 分析 A / B 的动画源和剪辑匹配情况（不修改任何资源）。
    /// 优先级：手动映射（clipOverrides）&gt; 按名称（忽略大小写）&gt; 两边都只剩一个未匹配剪辑时自动配对。
    /// skippedClips 中的源剪辑不会出现在分析结果里，因此也不会转移。
    /// </summary>
    public static Analysis Analyze(GameObject source, GameObject target,
        Dictionary<AnimationClip, AnimationClip> clipOverrides, HashSet<AnimationClip> skippedClips)
    {
        var analysis = new Analysis();
        if (source == null || target == null) return analysis;

        analysis.sourceSources.AddRange(GatherAnimationSources(source));

        var allTargetSources = GatherAnimationSources(target);
        foreach (var animationSource in allTargetSources)
        {
            var root = animationSource.Root;
            if (root == null) continue;
            if (root == target.transform || target.transform.IsChildOf(root))
            {
                analysis.targetSources.Add(animationSource);
            }
            else
            {
                analysis.warnings.Add("B 的动画源「" + animationSource.Label +
                                      "」不在 B 的上层，无法驱动复制到 B 的对象，已忽略。");
            }
        }

        if (analysis.sourceSources.Count == 0)
        {
            analysis.warnings.Add("A 未检测到 Animator / 旧版 Animation，本次不会转移动画轨道。");
        }
        if (analysis.targetSources.Count == 0)
        {
            analysis.warnings.Add("B 未检测到可用的 Animator / 旧版 Animation，无法转移动画轨道。");
        }

        // B 侧剪辑（去重、保持顺序）
        var targetClips = new List<AnimationClip>();
        foreach (var targetSource in analysis.targetSources)
        {
            if (targetSource.clips == null) continue;
            foreach (var targetClip in targetSource.clips)
            {
                if (targetClip != null && !targetClips.Contains(targetClip)) targetClips.Add(targetClip);
            }
        }

        // A 侧剪辑（去重、保持顺序）
        var sourceClips = new List<AnimationClip>();
        var seenClips = new HashSet<AnimationClip>();
        foreach (var animationSource in analysis.sourceSources)
        {
            if (animationSource.clips == null) continue;
            foreach (var clip in animationSource.clips)
            {
                if (clip != null && seenClips.Add(clip)) sourceClips.Add(clip);
            }
        }

        analysis.sourceClipCount = sourceClips.Count;
        analysis.targetClipCount = targetClips.Count;

        var usedTargets = new HashSet<AnimationClip>();
        var pending = new List<ClipMatch>();
        foreach (var clip in sourceClips)
        {
            if (skippedClips != null && skippedClips.Contains(clip)) continue;

            var match = new ClipMatch { sourceClip = clip };
            foreach (var animationSource in analysis.sourceSources)
            {
                if (animationSource.clips == null) continue;
                bool contains = false;
                foreach (var candidate in animationSource.clips)
                {
                    if (candidate == clip)
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains)
                {
                    match.sourceSource = animationSource;
                    break;
                }
            }
            if (match.sourceSource == null) continue;

            AnimationClip overridden;
            if (clipOverrides != null && clipOverrides.TryGetValue(clip, out overridden) && overridden != null)
            {
                // 1) 手动映射优先
                match.targetClip = overridden;
                match.targetSource = FindSourceOfClip(analysis.targetSources, overridden);
                if (match.targetSource == null)
                {
                    analysis.warnings.Add("手动指定的目标剪辑「" + overridden.name +
                                          "」不在 B 的动画源中，已按“目标缺少”处理。");
                    match.targetClip = null;
                }
            }
            else
            {
                // 2) 名称匹配（忽略大小写）；多个同名时取第一个
                AnimationSource matchedSource = null;
                AnimationClip matchedClip = null;
                int candidateCount = 0;
                foreach (var targetClip in targetClips)
                {
                    if (!string.Equals(targetClip.name, clip.name, StringComparison.OrdinalIgnoreCase)) continue;
                    candidateCount++;
                    if (matchedClip == null)
                    {
                        matchedClip = targetClip;
                        matchedSource = FindSourceOfClip(analysis.targetSources, targetClip);
                    }
                }
                if (candidateCount > 1)
                {
                    analysis.warnings.Add("B 中有多个名为「" + clip.name + "」的剪辑，已使用第一个匹配项（可在“剪辑映射”里改）。");
                }
                match.targetClip = matchedClip;
                match.targetSource = matchedSource;
            }

            if (match.targetClip != null) usedTargets.Add(match.targetClip);
            pending.Add(match);
        }

        // 3) 唯一候选自动配对：名称都没匹配上，且双方都只剩一个未匹配剪辑。
        //    手动映射过剪辑时不再自动配对，避免把用户特意留下的剪辑也写进去。
        var unmatched = new List<ClipMatch>();
        foreach (var match in pending)
        {
            if (match.targetClip == null) unmatched.Add(match);
        }
        var freeTargets = new List<AnimationClip>();
        foreach (var targetClip in targetClips)
        {
            if (!usedTargets.Contains(targetClip)) freeTargets.Add(targetClip);
        }
        bool hasManualMapping = clipOverrides != null && clipOverrides.Count > 0;
        if (!hasManualMapping && unmatched.Count == 1 && freeTargets.Count == 1)
        {
            var match = unmatched[0];
            match.targetClip = freeTargets[0];
            match.targetSource = FindSourceOfClip(analysis.targetSources, freeTargets[0]);
            analysis.warnings.Add("剪辑「" + match.sourceClip.name + "」与 B 的「" + freeTargets[0].name +
                                  "」名称不同，已按唯一候选自动配对；可在“剪辑映射”里改。");
        }

        foreach (var match in pending)
        {
            analysis.matches.Add(match);
            if (match.targetClip == null) analysis.missingClipCount++;
            else analysis.matchedClipCount++;
        }
        return analysis;
    }

    private static AnimationSource FindSourceOfClip(List<AnimationSource> sources, AnimationClip clip)
    {
        if (clip == null) return null;
        foreach (var source in sources)
        {
            if (source.clips == null) continue;
            foreach (var candidate in source.clips)
            {
                if (candidate == clip) return source;
            }
        }
        return null;
    }

    /// <summary>执行复制 + 动画轨道转移。</summary>
    public static Report Execute(Request request, Options options)
    {
        var report = new Report();
        if (options == null) options = new Options();
        if (request == null)
        {
            report.Error("没有请求");
            return report;
        }

        var source = request.source;
        var target = request.target;
        if (Application.isPlaying)
        {
            report.Error("播放模式下不能执行动画转移：请先退出播放模式（Exit Play Mode）再试");
            return report;
        }
        if (source == null || target == null)
        {
            report.Error("源物体 A 或目标物体 B 为空");
            return report;
        }
        if (source == target)
        {
            report.Error("A 和 B 是同一个物体");
            return report;
        }
        if (!IsSceneObject(source) || !IsSceneObject(target))
        {
            report.Error("A、B 必须是场景对象（不能是工程里的预制体资源）");
            return report;
        }
        if (target.transform.IsChildOf(source.transform))
        {
            report.Error("目标物体 B 位于源物体 A 内部，请先调整层级");
            return report;
        }

        var included = new HashSet<Transform>();
        var rowByTarget = new Dictionary<Transform, Row>();
        if (request.rows != null)
        {
            foreach (var row in request.rows)
            {
                if (row == null || row.target == null) continue;
                if (!row.target.IsChildOf(source.transform))
                {
                    report.Warn("对象「" + row.target.name + "」不在 A 的子级中，已跳过");
                    continue;
                }
                rowByTarget[row.target] = row;
                if (row.include) included.Add(row.target);
            }
        }

        if (included.Count == 0)
        {
            report.Error("没有勾选任何要复制的子对象");
            return report;
        }

        var copyRoots = new List<Transform>();
        foreach (var transform in included)
        {
            if (!HasIncludedAncestor(transform, source.transform, included)) copyRoots.Add(transform);
        }
        copyRoots.Sort(delegate(Transform a, Transform b)
        {
            int depthA = GetDepth(a, source.transform);
            int depthB = GetDepth(b, source.transform);
            if (depthA != depthB) return depthA.CompareTo(depthB);
            Row rowA, rowB;
            rowByTarget.TryGetValue(a, out rowA);
            rowByTarget.TryGetValue(b, out rowB);
            int indexA = rowA != null ? rowA.index : 0;
            int indexB = rowB != null ? rowB.index : 0;
            return indexA.CompareTo(indexB);
        });

        var analysis = Analyze(source, target, request.clipOverrides, request.skippedClips);
        var ctx = new Context(options, report);
        var allPairs = new List<CopyPair>();

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("动画转移");
        try
        {
            // 1) 复制对象
            for (int i = 0; i < copyRoots.Count; i++)
            {
                var copyRoot = copyRoots[i];
                if (copyRoot == null) continue;
                try
                {
                    EditorUtility.DisplayProgressBar(
                        "动画转移",
                        "正在复制：" + copyRoot.name + "（" + (i + 1) + "/" + copyRoots.Count + "）",
                        0.45f * i / Mathf.Max(1, copyRoots.Count));
                    var pairs = CopyBranch(copyRoot, target.transform, included, rowByTarget, ctx);
                    allPairs.AddRange(pairs);
                }
                catch (Exception ex)
                {
                    report.Error("复制「" + copyRoot.name + "」失败：" + ex.Message);
                    Debug.LogException(ex);
                }
            }

            // 2) 转移轨道
            TransferAll(analysis, allPairs, copyRoots, ctx);

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
    //  复制实现
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
        private readonly HashSet<string> warnedKeys = new HashSet<string>();

        public Context(Options options, Report report)
        {
            this.options = options;
            this.report = report;
        }

        public void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (warnedKeys.Add(key)) report.Warn(message);
        }

        public void RegisterModifiedClip(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!report.modifiedClips.Contains(assetPath)) report.modifiedClips.Add(assetPath);
        }
    }

    private static List<CopyPair> CopyBranch(
        Transform copyRoot,
        Transform targetParent,
        HashSet<Transform> included,
        Dictionary<Transform, Row> rowByTarget,
        Context ctx)
    {
        var copy = UnityEngine.Object.Instantiate(copyRoot.gameObject, targetParent, false);
        if (copy == null)
        {
            ctx.report.Error("实例化「" + copyRoot.name + "」失败");
            return new List<CopyPair>();
        }

        // 中途失败（例如在播放模式下 Undo 不可用）时删掉已经建出来的副本，避免留下半成品
        try
        {
            var pairs = new List<CopyPair>();
            PairTransforms(copyRoot, copy.transform, pairs);

            var keepCache = new Dictionary<Transform, bool>();
            foreach (var pair in pairs)
            {
                if (pair.copy == null) continue;
                if (ShouldKeep(pair.original, included, keepCache)) continue;
                UnityEngine.Object.DestroyImmediate(pair.copy.gameObject);
            }

            var alive = new List<CopyPair>();
            foreach (var pair in pairs)
            {
                if (pair.copy == null) continue;
                if (!ShouldKeep(pair.original, included, keepCache)) continue;
                alive.Add(pair);
            }

            // 顶层先改名，保证动画路径使用最终名字
            foreach (var pair in alive)
            {
                string desired = !string.IsNullOrEmpty(pair.original.name) ? pair.original.name : "GameObject";
                pair.copy.name = MakeUniqueName(pair.copy, desired);
            }

            foreach (var pair in alive)
            {
                if (!included.Contains(pair.original)) ctx.report.helperObjects++;
            }

            Undo.RegisterCreatedObjectUndo(copy, "动画转移");
            EditorSceneManager.MarkSceneDirty(copy.scene);

            ctx.report.createdRoots++;
            ctx.report.createdObjects += alive.Count;
            ctx.report.roots.Add(copy);
            return alive;
        }
        catch
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy.gameObject);
            throw;
        }
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

    /// <summary>
    /// 保留规则：勾选的对象一定保留；未勾选但包含被勾选子级的父级也保留（作为层级容器）。
    /// </summary>
    private static bool ShouldKeep(Transform transform, HashSet<Transform> included,
        Dictionary<Transform, bool> cache)
    {
        if (cache.TryGetValue(transform, out bool cached)) return cached;

        bool keep = included.Contains(transform);
        if (!keep)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                if (ShouldKeep(transform.GetChild(i), included, cache))
                {
                    keep = true;
                    break;
                }
            }
        }
        cache[transform] = keep;
        return keep;
    }

    private static string MakeUniqueName(Transform self, string desired)
    {
        var parent = self.parent;
        if (parent == null) return desired;

        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != null && child != self) used.Add(child.name);
        }
        if (!used.Contains(desired)) return desired;

        for (int i = 1; i < 10000; i++)
        {
            string candidate = desired + "_" + i;
            if (!used.Contains(candidate)) return candidate;
        }
        return desired + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    // =============================================================
    //  轨道转移实现
    // =============================================================

    private static void TransferAll(Analysis analysis, List<CopyPair> pairs,
        List<Transform> copyRoots, Context ctx)
    {
        if (analysis.matches.Count == 0) return;

        int total = analysis.matches.Count;
        var reportedMissing = new HashSet<string>();
        for (int i = 0; i < total; i++)
        {
            var match = analysis.matches[i];
            if (match == null || match.sourceClip == null || match.sourceSource == null) continue;

            var sourceRoot = match.sourceSource.Root;
            if (sourceRoot == null) continue;

            // 动画源本身就在被复制的分支里：副本共用同一个剪辑，路径不变，不需要转移
            if (IsInsideCopiedBranch(sourceRoot, copyRoots)) continue;

            var destinationSource = match.targetSource;
            AnimationClip destinationClip = match.targetClip;

            if (destinationSource == null)
            {
                if (!ctx.options.createMissingClips)
                {
                    AddMissingClip(ctx, reportedMissing, match, "目标缺少同名剪辑");
                    continue;
                }
                destinationSource = FindCreationTarget(analysis);
                if (destinationSource == null)
                {
                    AddMissingClip(ctx, reportedMissing, match, "B 没有 AnimatorController，无法新建剪辑");
                    continue;
                }
            }
            else if (destinationClip != null && destinationClip == match.sourceClip)
            {
                ctx.WarnOnce("shared:" + match.sourceClip.GetInstanceID(),
                    "剪辑「" + match.sourceClip.name + "」被 A 和 B 共用，无法转移（会改到 A 自己的剪辑），已跳过");
                continue;
            }

            var destinationRoot = destinationSource.Root;
            if (destinationRoot == null) continue;

            var pathMap = BuildPathMap(sourceRoot, destinationRoot, pairs);
            if (pathMap.Count == 0)
            {
                ctx.WarnOnce("noMap:" + match.sourceSource.GetHashCode() + ":" + destinationSource.GetHashCode(),
                    "A 的动画源「" + match.sourceSource.Label + "」与勾选内容没有层级关系，已跳过它的剪辑");
                continue;
            }

            try
            {
                EditorUtility.DisplayProgressBar(
                    "动画转移",
                    "正在转移轨道：" + match.sourceClip.name + "（" + (i + 1) + "/" + total + "）",
                    0.45f + 0.55f * i / Mathf.Max(1, total));

                if (destinationClip == null)
                {
                    CreateMissingClip(match.sourceClip, destinationSource, pathMap, ctx);
                }
                else
                {
                    TransferCurves(match.sourceClip, destinationClip, pathMap, ctx);
                }
            }
            catch (Exception ex)
            {
                ctx.report.Error("转移剪辑「" + match.sourceClip.name + "」失败：" + ex.Message);
                Debug.LogException(ex);
            }
        }
    }

    private static void AddMissingClip(Context ctx, HashSet<string> reported, ClipMatch match, string reason)
    {
        string key = match.sourceClip.GetInstanceID() + "|" + reason;
        if (!reported.Add(key)) return;
        string entry = match.sourceClip.name + "（" + match.sourceSource.Label + "）";
        if (!ctx.report.missingClips.Contains(entry)) ctx.report.missingClips.Add(entry);
        ctx.WarnOnce(key, "剪辑「" + match.sourceClip.name + "」" + reason + "，未转移");
    }

    private static AnimationSource FindCreationTarget(Analysis analysis)
    {
        foreach (var animationSource in analysis.targetSources)
        {
            if (animationSource.controller is AnimatorController) return animationSource;
        }
        return null;
    }

    private static bool IsInsideCopiedBranch(Transform sourceRoot, List<Transform> copyRoots)
    {
        foreach (var copyRoot in copyRoots)
        {
            if (copyRoot == null) continue;
            if (sourceRoot == copyRoot || sourceRoot.IsChildOf(copyRoot)) return true;
        }
        return false;
    }

    private static Dictionary<string, string> BuildPathMap(Transform sourceRoot, Transform targetRoot,
        List<CopyPair> pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sourceRoot == null || targetRoot == null) return map;

        foreach (var pair in pairs)
        {
            if (pair.original == null || pair.copy == null) continue;
            if (!pair.original.IsChildOf(sourceRoot)) continue;
            if (!pair.copy.IsChildOf(targetRoot)) continue;

            string oldPath = AnimationUtility.CalculateTransformPath(pair.original, sourceRoot);
            string newPath = AnimationUtility.CalculateTransformPath(pair.copy, targetRoot);
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) continue;
            map[oldPath] = newPath;
        }
        return map;
    }

    private static void CreateMissingClip(AnimationClip sourceClip, AnimationSource destinationSource,
        Dictionary<string, string> pathMap, Context ctx)
    {
        var controller = destinationSource.controller as AnimatorController;
        if (controller == null)
        {
            ctx.WarnOnce("noController:" + sourceClip.GetInstanceID(),
                "剪辑「" + sourceClip.name + "」目标缺少，但 B 不是 AnimatorController，无法新建并挂接，已跳过");
            return;
        }

        string controllerPath = AssetDatabase.GetAssetPath(controller);
        string folder = string.IsNullOrEmpty(controllerPath)
            ? "Assets"
            : (Path.GetDirectoryName(controllerPath) ?? "Assets").Replace('\\', '/');
        if (string.IsNullOrEmpty(folder) || (!folder.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                                              !folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
        {
            folder = "Assets";
        }

        string baseName = SanitizeAssetName(sourceClip.name);
        if (string.IsNullOrEmpty(baseName)) baseName = "NewClip";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + ".anim");

        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(assetPath) };
        AssetDatabase.CreateAsset(clip, assetPath);
        try
        {
            var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        catch (Exception ex)
        {
            ctx.WarnOnce("clipSettings:" + sourceClip.GetInstanceID(),
                "复制剪辑「" + sourceClip.name + "」的设置失败：" + ex.Message);
        }

        int count = TransferCurves(sourceClip, clip, pathMap, ctx);
        if (count == 0)
        {
            AssetDatabase.DeleteAsset(assetPath);
            ctx.WarnOnce("newClipEmpty:" + sourceClip.GetInstanceID(),
                "剪辑「" + sourceClip.name + "」没有可转移的轨道，已跳过新建");
            return;
        }

        EditorUtility.SetDirty(clip);
        try
        {
            controller.AddMotion(clip);
            EditorUtility.SetDirty(controller);
        }
        catch (Exception ex)
        {
            ctx.report.Warn("新建剪辑「" + clip.name + "」已保存，但加入控制器失败：" + ex.Message +
                            "。请手动添加到状态机。");
        }

        ctx.report.createdClips.Add(assetPath);
    }

    /// <summary>把源剪辑里能通过 pathMap 映射的轨道写入目标剪辑。返回写入的轨道数。</summary>
    private static int TransferCurves(AnimationClip sourceClip, AnimationClip destinationClip,
        Dictionary<string, string> pathMap, Context ctx)
    {
        if (sourceClip == null || destinationClip == null || pathMap.Count == 0) return 0;

        string assetPath = AssetDatabase.GetAssetPath(destinationClip);
        if (string.IsNullOrEmpty(assetPath))
        {
            ctx.WarnOnce("destPath:" + destinationClip.GetInstanceID(),
                "目标剪辑「" + destinationClip.name + "」不是资源文件，无法写入关键帧");
            return 0;
        }
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.WarnOnce("destOutside:" + assetPath,
                "目标剪辑「" + destinationClip.name + "」位于工程外（" + assetPath + "），无法写入关键帧");
            return 0;
        }
        if (!string.Equals(Path.GetExtension(assetPath), ".anim", StringComparison.OrdinalIgnoreCase))
        {
            ctx.WarnOnce("destExt:" + assetPath,
                "目标剪辑「" + destinationClip.name + "」来自模型文件（" + assetPath + "），无法写入关键帧，已跳过");
            return 0;
        }

        var newCurveBindings = new List<EditorCurveBinding>();
        var newCurves = new List<AnimationCurve>();
        var existingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in AnimationUtility.GetCurveBindings(destinationClip))
        {
            existingKeys.Add(BindingKey(binding));
        }

        var objectNewBindings = new List<EditorCurveBinding>();
        var objectNewKeys = new List<ObjectReferenceKeyframe[]>();
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(destinationClip))
        {
            existingKeys.Add(BindingKey(binding));
        }

        int overwritten = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
        {
            string newPath;
            if (!pathMap.TryGetValue(binding.path, out newPath)) continue;
            if (string.IsNullOrEmpty(newPath)) continue;

            var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (curve == null) continue;

            var newBinding = binding;
            newBinding.path = newPath;
            newCurveBindings.Add(newBinding);
            newCurves.Add(curve);
            if (existingKeys.Contains(BindingKey(newBinding))) overwritten++;
        }

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
        {
            string newPath;
            if (!pathMap.TryGetValue(binding.path, out newPath)) continue;
            if (string.IsNullOrEmpty(newPath)) continue;

            var keys = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            if (keys == null) continue;

            var newKeys = new ObjectReferenceKeyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                newKeys[i] = new ObjectReferenceKeyframe
                {
                    time = keys[i].time,
                    value = keys[i].value,
                };
            }
            var newBinding = binding;
            newBinding.path = newPath;
            objectNewBindings.Add(newBinding);
            objectNewKeys.Add(newKeys);
            if (existingKeys.Contains(BindingKey(newBinding))) overwritten++;
        }

        if (newCurveBindings.Count == 0 && objectNewBindings.Count == 0) return 0;

        Undo.RegisterCompleteObjectUndo(destinationClip, "动画转移");
        int added = 0;
        for (int i = 0; i < newCurveBindings.Count; i++)
        {
            try
            {
                AnimationUtility.SetEditorCurve(destinationClip, newCurveBindings[i], newCurves[i]);
                added++;
            }
            catch (Exception ex)
            {
                ctx.WarnOnce("curve:" + assetPath + ":" + newCurveBindings[i].path,
                    "写入剪辑「" + destinationClip.name + "」的曲线失败：" + ex.Message);
            }
        }
        for (int i = 0; i < objectNewBindings.Count; i++)
        {
            try
            {
                AnimationUtility.SetObjectReferenceCurve(destinationClip, objectNewBindings[i], objectNewKeys[i]);
                added++;
            }
            catch (Exception ex)
            {
                ctx.WarnOnce("objCurve:" + assetPath + ":" + objectNewBindings[i].path,
                    "写入剪辑「" + destinationClip.name + "」的对象引用曲线失败：" + ex.Message);
            }
        }

        if (added == 0) return 0;

        EditorUtility.SetDirty(destinationClip);
        ctx.report.transferredCurves += added;
        ctx.report.overwrittenCurves += overwritten;
        ctx.RegisterModifiedClip(assetPath);

        string detail = sourceClip.name + " → " + destinationClip.name + "：" + added + " 条轨道";
        if (overwritten > 0) detail += "（覆盖 " + overwritten + " 条）";
        ctx.report.clipDetails.Add(detail);
        return added;
    }

    private static string BindingKey(EditorCurveBinding binding)
    {
        Type type = binding.type;
        return binding.path + "\n" + (type != null ? type.FullName : "") + "\n" + binding.propertyName;
    }

    // =============================================================
    //  工具方法
    // =============================================================

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

    private static void AddDistinct(List<AnimationClip> list, AnimationClip clip)
    {
        if (clip == null || list.Contains(clip)) return;
        list.Add(clip);
    }

    private static bool IsSceneObject(GameObject go)
    {
        return go != null && go.scene.IsValid() && !AssetDatabase.Contains(go);
    }

    private static string BuildComponentTags(Transform transform)
    {
        var parts = new List<string>();
        var components = transform.GetComponents<Component>();
        if (components != null)
        {
            foreach (var component in components)
            {
                if (component == null) continue;
                Type type = component.GetType();
                if (typeof(Transform).IsAssignableFrom(type)) continue;
                string name = type.Name;
                if (!parts.Contains(name)) parts.Add(name);
                if (parts.Count >= 3) break;
            }
        }
        if (parts.Count == 0) return "";
        return "[" + string.Join(" · ", parts.ToArray()) + "]";
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

    private static string SanitizeAssetName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }
}
