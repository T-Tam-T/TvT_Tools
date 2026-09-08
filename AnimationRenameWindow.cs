using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 批量改名（动画保持）
///
/// 布局（参考示意图）：
///   左栏：
///     - 重命名规则
///     - 场景对象/预制体拖拽区域
///     - 对象列表（下拉式：每个对象一个折叠头，其下排列它状态机里检测到的动画剪辑）
///   右栏：
///     - 识别到的对象（含子层级）预览：原名称 → 改名后
///     - 应用按钮
///
/// 动画剪辑通过对象上的 Animator（AnimatorController 状态机，含 BlenderTree / 覆盖动画）
/// 或旧版 Animation 组件自动检测，多个剪辑会依次排列在对象下面。
/// 每个剪辑行：左侧为剪辑名称（左对齐），右侧为时长 / FPS / 是否循环 / 资源框（右对齐）。
/// 当窗口较窄时，右侧不显示内联信息，改为可展开的详情（时长 / FPS / 循环 / 帧数）。
///
/// 改名时会同步改写对应动画剪辑内部的曲线绑定路径（EditorCurveBinding.path），
/// 确保改名后动画仍指向改名后的对象、不丢失动画目标。
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

    /// <summary>一条记录：对象 + 检测到的动画剪辑列表。</summary>
    private class Entry
    {
        public GameObject obj;
        public bool expanded = true;                // 对象折叠头是否展开
        public List<ClipInfo> clips = new List<ClipInfo>();
    }

    /// <summary>一个动画剪辑的信息（名称 / 时长 / FPS / 循环等展示用）。</summary>
    private class ClipInfo
    {
        public AnimationClip clip;
        public bool expanded;                       // 剪辑折叠是否展开（显示详情）
        public string usagePath = "Animator Controller";
    }

    /// <summary>一条记录在改名前的路径映射快照：剪辑 + 旧路径→新路径映射。</summary>
    private class EntryRemap
    {
        public List<ClipInfo> clips = new List<ClipInfo>();
        public Dictionary<string, string> pathRemap = new Dictionary<string, string>();
    }

    private List<Entry> entries = new List<Entry>();
    private List<GameObject> targets = new List<GameObject>();
    private string statusMessage = "";

    private Vector2 entryScrollPos;
    private Vector2 previewScrollPos;
    private float leftPaneWidth = 420f;
    private readonly Dictionary<GameObject, string> customNames = new Dictionary<GameObject, string>();

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
        window.minSize = new Vector2(760, 500);
        window.Show();

        // 打开窗口时，把当前选中的场景对象 / 动画剪辑直接带进来
        AnimationClip selectedClip = null;
        foreach (var o in Selection.objects)
        {
            if (o is AnimationClip c) { selectedClip = c; break; }
        }
        foreach (var o in Selection.objects)
        {
            if (o is GameObject go && !AssetDatabase.Contains(go))
                window.AddEntry(go, selectedClip);
        }
        window.RebuildTargets();
    }

    private void AddEntry(GameObject go, AnimationClip clip)
    {
        if (go == null) return;
        var existing = entries.FirstOrDefault(en => en.obj == go);
        if (existing != null)
        {
            // 若手动给了剪辑且该剪辑尚未被检测到，则补充进去
            if (clip != null && !existing.clips.Exists(ci => ci.clip == clip))
                existing.clips.Add(new ClipInfo { clip = clip, usagePath = "手动设置" });
            return;
        }
        var entry = new Entry { obj = go };
        RefreshClips(entry);
        if (clip != null && !entry.clips.Exists(ci => ci.clip == clip))
            entry.clips.Insert(0, new ClipInfo { clip = clip, usagePath = "手动设置" });
        entries.Add(entry);
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
        GUILayout.Space(4);
        EditorGUILayout.LabelField(
            "拖入场景对象/预制体，工具会通过状态机自动检测其动画剪辑；按规则改名后会自动更新动画绑定路径（动画对象不丢失）。",
            EditorStyles.miniLabel);
        GUILayout.Space(2);

        float leftWidth = Mathf.Max(340f, position.width * 0.55f);
        leftPaneWidth = leftWidth;

        EditorGUILayout.BeginHorizontal();

        // ================= 左栏 =================
        EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
        DrawRulePanel();
        GUILayout.Space(6);
        DrawDropZone();
        GUILayout.Space(6);
        DrawEntryList();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6);

        // ================= 右栏 =================
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawPreview();
        GUILayout.Space(6);
        DrawApplyButton();
        if (!string.IsNullOrEmpty(statusMessage))
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRulePanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("重命名规则", EditorStyles.boldLabel);
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
        EditorGUILayout.EndVertical();
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

    private static GUIStyle _roundedDropStyle;

    /// <summary>
    /// 缓存一个可 9 宫格拉伸的圆角底样式：填充 rgb(64,64,64)、边框 rgb(35,35,35)、带圆角。
    /// 用 9 宫格（style.border）拉伸，任意宽高都能保持圆角不变形。
    /// </summary>
    private static GUIStyle RoundedDropStyle()
    {
        if (_roundedDropStyle != null) return _roundedDropStyle;

        const int s = 32;
        const float r = 6f;      // 圆角半径（纹理像素）
        const float bw = 1.6f;   // 边框宽度（纹理像素）
        Color fill = new Color(64f / 255f, 64f / 255f, 64f / 255f, 1f);
        Color border = new Color(35f / 255f, 35f / 255f, 35f / 255f, 1f);

        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s;
                float v = (y + 0.5f) / s;
                float pxc = u * s - s * 0.5f;
                float pyc = v * s - s * 0.5f;
                float qx = Mathf.Abs(pxc) - (s * 0.5f - r);
                float qy = Mathf.Abs(pyc) - (s * 0.5f - r);
                float qx0 = Mathf.Max(qx, 0f);
                float qy0 = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(qx0 * qx0 + qy0 * qy0) - r;  // 圆角矩形有符号距离
                float alpha = Mathf.Clamp01(0.5f - dist);
                if (alpha <= 0f) { px[y * s + x] = new Color(0f, 0f, 0f, 0f); continue; }
                Color c = (dist > -bw) ? border : fill;
                c.a = alpha;
                px[y * s + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        var style = new GUIStyle();
        style.normal.background = tex;
        int corner = Mathf.RoundToInt(r);
        style.border.left = corner; style.border.right = corner;
        style.border.top = corner; style.border.bottom = corner;
        style.padding.left = 0; style.padding.right = 0;
        style.padding.top = 0; style.padding.bottom = 0;
        style.margin.left = 0; style.margin.right = 0;
        style.margin.top = 0; style.margin.bottom = 0;
        _roundedDropStyle = style;
        return _roundedDropStyle;
    }

    private static GUIStyle _wrappedBoldLabel;

    /// <summary>可换行的加粗标签样式（用于避免标题被截断）。</summary>
    private static GUIStyle WrappedBoldLabel()
    {
        if (_wrappedBoldLabel == null)
            _wrappedBoldLabel = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
        return _wrappedBoldLabel;
    }

    private void DrawDropZone()
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("场景对象/预制体拖拽区域", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("添加选中对象", GUILayout.Width(96)))
            AddSelectedObjects();
        GUILayout.EndHorizontal();

        Rect dropRect = GUILayoutUtility.GetRect(10, 76, GUILayout.ExpandWidth(true));
        HandleObjectDrop(dropRect);

        // 圆角浅色底 + 边框示意拖拽区
        GUI.Box(dropRect, GUIContent.none, RoundedDropStyle());

        GUILayout.BeginArea(dropRect);
        GUILayout.Label("把场景里的对象 / 预制体拖到这里", EditorStyles.centeredGreyMiniLabel);
        GUILayout.Label("（工具会识别其子层级名称，并经状态机检测动画）", EditorStyles.centeredGreyMiniLabel);
        GUILayout.EndArea();
    }

    private void DrawEntryList()
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("对象列表", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("重新检测", GUILayout.Width(80)))
        {
            foreach (var e in entries) RefreshClips(e);
            Repaint();
        }
        if (GUILayout.Button("清空列表", GUILayout.Width(80)))
        {
            entries.Clear();
            RebuildTargets();
        }
        GUILayout.EndHorizontal();

        // 窗口较窄时，隐藏右侧内联信息，改为可展开的详情（时长/FPS/循环/帧数）
        bool showInlineInfo = leftPaneWidth >= 480f;

        entryScrollPos = EditorGUILayout.BeginScrollView(entryScrollPos, GUILayout.ExpandHeight(true));
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.obj == null)
            {
                GUILayout.Label("<已丢失对象>", EditorStyles.centeredGreyMiniLabel);
                continue;
            }

            // ---- 对象折叠头 ----
            GUILayout.BeginHorizontal();
            e.expanded = GUILayout.Toggle(e.expanded, new GUIContent($"{e.obj.name}（{e.clips.Count} 个动画）"), EditorStyles.foldout, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                entries.RemoveAt(i);
                RebuildTargets();
                GUILayout.EndHorizontal();
                i--;
                continue;
            }
            GUILayout.EndHorizontal();

            // ---- 对象下的剪辑列表 ----
            if (!e.expanded)
                continue;

            for (int j = 0; j < e.clips.Count; j++)
            {
                var ci = e.clips[j];
                GUILayout.BeginHorizontal();
                GUILayout.Space(14); // 层级缩进（剪辑行）
                ci.expanded = GUILayout.Toggle(ci.expanded, new GUIContent(ci.clip != null ? ci.clip.name : "<空>"), EditorStyles.foldout, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                if (showInlineInfo && ci.clip != null)
                {
                    GUILayout.Label($"时长: {ci.clip.length:0.##}s", EditorStyles.miniLabel);
                    GUILayout.Label($"FPS: {ci.clip.frameRate:0}", EditorStyles.miniLabel);
                    GUILayout.Label($"循环: {LoopText(ci.clip)}", EditorStyles.miniLabel);
                }
                ci.clip = (AnimationClip)EditorGUILayout.ObjectField(ci.clip, typeof(AnimationClip), false, GUILayout.Width(160));
                GUILayout.EndHorizontal();

                // ---- 剪辑详情（可展开） ----
                if (ci.expanded && ci.clip != null)
                {
                    EditorGUI.indentLevel += 2;
                    EditorGUILayout.LabelField("时长", $"{ci.clip.length:0.##}s");
                    EditorGUILayout.LabelField("FPS", $"{ci.clip.frameRate:0}");
                    EditorGUILayout.LabelField("循环", LoopText(ci.clip));
                    EditorGUILayout.LabelField("帧数", $"{Mathf.RoundToInt(ci.clip.length * ci.clip.frameRate)}");
                    EditorGUI.indentLevel -= 2;
                }
            }

            GUILayout.Space(4);
        }
        if (entries.Count == 0)
            GUILayout.Label("列表为空：在蓝色区域拖入对象/预制体，或点“添加选中对象”", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndScrollView();
    }

    private string LoopText(AnimationClip clip)
    {
        return clip != null && clip.isLooping ? "是" : "否";
    }

    private void DrawPreview()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
        GUILayout.BeginHorizontal();
        GUILayout.Label($"识别到的对象（共 {targets.Count} 个，含挂载对象及子层级）", WrappedBoldLabel(), GUILayout.ExpandWidth(true));
        if (GUILayout.Button("重新识别", GUILayout.Width(80)))
        {
            RebuildTargets();
            Repaint();
        }
        GUILayout.EndHorizontal();

        previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.ExpandHeight(true));

        // 右栏（预览）的可用宽度 = 窗口宽度 - 左栏宽度 - 间距
        float paneWidth = Mathf.Max(position.width - leftPaneWidth - 16f, 300f);
        float halfWidth = (paneWidth - 40f) * 0.5f;

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
        EditorGUILayout.LabelField("改名后（可直接输入修改）", EditorStyles.miniBoldLabel);
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go == null) { EditorGUILayout.TextField(""); continue; }
            string effective = GetEffectiveNewName(go, i);
            string edited = EditorGUILayout.TextField(effective, GUILayout.Width(halfWidth));
            if (edited != effective)
            {
                if (string.IsNullOrEmpty(edited))
                    customNames.Remove(go);
                else
                    customNames[go] = edited;
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
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

    private void HandleObjectDrop(Rect dropRect)
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
                if (o is GameObject go && !AssetDatabase.Contains(go)) AddEntry(go, null);
            }
            RebuildTargets();
            evt.Use();
        }
    }

    private void AddSelectedObjects()
    {
        AnimationClip selectedClip = null;
        foreach (var o in Selection.objects)
        {
            if (o is AnimationClip c) { selectedClip = c; break; }
        }
        foreach (var o in Selection.objects)
        {
            if (o is GameObject go && !AssetDatabase.Contains(go)) AddEntry(go, selectedClip);
        }
        RebuildTargets();
        Repaint();
    }

    // =============================================================
    //  状态机检测动画剪辑
    // =============================================================

    /// <summary>
    /// 找到对象自身或其上级父节点上挂载动画（Animator / Animation）的对象，作为动画来源与绑定路径根。
    /// 若都没找到则退回对象本身，避免漏检。
    /// </summary>
    private static GameObject FindAnimationRoot(GameObject go)
    {
        var t = go != null ? go.transform : null;
        while (t != null)
        {
            if (t.GetComponent<Animator>() != null || t.GetComponent<Animation>() != null)
                return t.gameObject;
            t = t.parent;
        }
        return go;
    }

    /// <summary>根据对象上的 Animator（状态机）/ 旧版 Animation 检测其使用的动画剪辑。</summary>
    private void RefreshClips(Entry entry)
    {
        if (entry == null || entry.obj == null) return;

        // 保留已有的“展开”状态，按剪辑引用匹配
        var expandedSet = new HashSet<AnimationClip>();
        foreach (var ci in entry.clips)
            if (ci.expanded && ci.clip != null) expandedSet.Add(ci.clip);

        entry.clips.Clear();

        var animRoot = FindAnimationRoot(entry.obj);
        if (animRoot == null) return;

        var animator = animRoot.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var list = new List<AnimationClip>();
            var controller = animator.runtimeAnimatorController;

            // 状态机（含 BlendTree / 子状态机）里的剪辑全部收集，再用 controller.animationClips 兜底，确保不遗漏
            if (controller is UnityEditor.Animations.AnimatorController ac)
            {
                foreach (var layer in ac.layers)
                    CollectStateMachineClips(layer.stateMachine, list);
            }
            foreach (var c in controller.animationClips)
                if (c != null && !list.Contains(c)) list.Add(c);

            AddClips(entry, list, "Animator Controller", expandedSet);
        }
        else
        {
            var legacy = animRoot.GetComponent<Animation>();
            if (legacy != null)
            {
                var list = new List<AnimationClip>();
                foreach (AnimationState st in legacy)
                    if (st.clip != null && !list.Contains(st.clip)) list.Add(st.clip);
                AddClips(entry, list, "Animation", expandedSet);
            }
        }
    }

    private void AddClips(Entry entry, List<AnimationClip> clips, string usagePath, HashSet<AnimationClip> expandedSet)
    {
        foreach (var c in clips)
            entry.clips.Add(new ClipInfo { clip = c, expanded = expandedSet.Contains(c), usagePath = usagePath });
    }

    private static void CollectStateMachineClips(UnityEditor.Animations.AnimatorStateMachine stateMachine, List<AnimationClip> result)
    {
        if (stateMachine == null) return;
        foreach (var childState in stateMachine.states)
        {
            if (childState.state == null) continue;
            CollectMotionClips(childState.state.motion, result);
        }
        foreach (var childSM in stateMachine.stateMachines)
        {
            if (childSM.stateMachine != null)
                CollectStateMachineClips(childSM.stateMachine, result);
        }
    }

    private static void CollectMotionClips(UnityEngine.Motion motion, List<AnimationClip> result)
    {
        if (motion is AnimationClip clip)
        {
            if (clip != null && !result.Contains(clip)) result.Add(clip);
        }
        else if (motion is UnityEditor.Animations.BlendTree bt)
        {
            foreach (var child in bt.children)
                CollectMotionClips(child.motion, result);
        }
    }

    // =============================================================
    //  逻辑
    // =============================================================

    /// <summary>根据各条记录的对象，识别出其本身 + 整个子层级内所有的 GameObject。</summary>
    private void RebuildTargets()
    {
        targets.Clear();
        var seen = new HashSet<GameObject>();
        foreach (var e in entries)
        {
            if (e.obj == null) continue;
            foreach (var t in e.obj.GetComponentsInChildren<Transform>(true))
            {
                if (seen.Add(t.gameObject)) targets.Add(t.gameObject);
            }
        }

        // 清理已不在识别范围内对象的自定义名称
        var stale = customNames.Keys.Where(go => go == null || !seen.Contains(go)).ToList();
        foreach (var go in stale) customNames.Remove(go);
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

    /// <summary>取一个对象的最终新名称：优先使用用户在预览里手动输入的文本，否则按规则计算。</summary>
    private string GetEffectiveNewName(GameObject go, int index)
    {
        if (go == null) return "";
        if (customNames.TryGetValue(go, out var cn) && !string.IsNullOrEmpty(cn))
            return cn;
        return GetNewName(go.name, index);
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
            statusMessage = "没有可改名的对象：请先在左侧拖入场景对象/预制体。";
            return;
        }

        // 计算要改名的对象及其新名称（优先使用预览里手动输入的文本）
        var nameMap = new Dictionary<GameObject, string>();
        int index = 0;
        foreach (var go in targets)
        {
            if (go == null) { index++; continue; }
            string newName = GetEffectiveNewName(go, index);
            if (newName != go.name)
                nameMap[go] = newName;
            index++;
        }

        if (nameMap.Count == 0)
        {
            statusMessage = "规则未产生任何名称变化，且未输入自定义名称，无需执行。";
            return;
        }

        // 关键：改名之前先为每条记录计算“旧路径→新路径”映射（此时对象仍是旧名，才能正确比对绑定路径）。
        // 之后再改名，否则绑定路径会被算成“老路径==新路径”，导致动画绑定不会被更新。
        var entryRemaps = new List<EntryRemap>();
        foreach (var e in entries)
        {
            if (e.obj == null) continue;
            var animRoot = FindAnimationRoot(e.obj);
            var pathRemap = BuildPathRemapForObject(animRoot, nameMap);
            entryRemaps.Add(new EntryRemap { clips = e.clips, pathRemap = pathRemap });
        }

        // 记录场景对象撤销（改名）
        Undo.RecordObjects(nameMap.Keys.ToArray(), "批量改名(动画保持)");

        // 批量改名
        foreach (var kv in nameMap)
            kv.Key.name = kv.Value;

        // 改名之后，用之前保存的映射改写动画剪辑的绑定路径
        int updatedBindings = 0;
        var processed = new HashSet<AnimationClip>();
        foreach (var er in entryRemaps)
        {
            foreach (var ci in er.clips)
            {
                if (ci.clip == null) continue;
                if (!processed.Add(ci.clip)) continue; // 同一剪辑只处理一次
                updatedBindings += UpdateClipPaths(ci.clip, er.pathRemap);
            }
        }

        AssetDatabase.Refresh();
        Repaint();

        string suffixMsg = updatedBindings > 0 ? $"，已同步更新 {updatedBindings} 条动画绑定路径" : "";
        statusMessage = $"已改名 {nameMap.Count} 个对象{suffixMsg}。";
    }

    /// <summary>
    /// 以某条记录的对象为路径根，建立其子层级内“旧相对路径 → 新相对路径”的映射。
    /// 相对路径是相对该对象（绑定路径根）的、以 / 分隔的对象名链，例如 “A/B”。
    /// </summary>
    private Dictionary<string, string> BuildPathRemapForObject(GameObject root, Dictionary<GameObject, string> nameMap)
    {
        var map = new Dictionary<string, string>();
        if (root == null) return map;
        var rootT = root.transform;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
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
