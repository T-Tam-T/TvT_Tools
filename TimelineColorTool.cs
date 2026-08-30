using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;

/// <summary>
/// Timeline 轨道颜色编辑器 & 自定义背景图工具
/// - 递归扫描 Timeline 中所有 Track 及 GroupTrack 下的 Sub-Group，按层级缩进展示
/// - 每行：色块 + 名称 + 类型 + 颜色选择器 + 重置按钮
/// - 颜色通过 SerializedObject.FindProperty("m_Color") 读写（TrackAsset.color 为 internal）
/// - 重置功能设 m_Color = InvalidColor(-1,-1,-1,-1)，回退到引擎默认预设色
/// - 支持为 Timeline 窗口注入自定义背景图，可调节透明度
/// - EditorPrefs 持久化：自动模式、不透明度、背景图路径、窗口位置
/// </summary>
public class TimelineColorTool : EditorWindow
{
    #region --- 窗口 & 菜单 ---

    [MenuItem("Tools/TvTTools/Timeline 轨道颜色 & 背景", false, 21)]
    private static void ShowWindow()
    {
        var window = GetWindow<TimelineColorTool>("Timeline 颜色 & 背景");
        window.minSize = new Vector2(560, 340);
        window.Show();
    }

    #endregion

    #region --- 轨道颜色相关字段 ---

    private Vector2 trackScrollPos;

    // 轨道树条目
    private class TrackEntry
    {
        public TrackAsset track;
        public int indent; // 层级缩进
        public string displayName;
        public string typeName;

        // 颜色属性缓存
        public SerializedObject serializedObject;
        public SerializedProperty colorProperty;
        public bool triedReflection;
        public PropertyInfo reflectionProperty;
    }

    private List<TrackEntry> trackEntries = new List<TrackEntry>();
    private bool[] trackFoldouts = new bool[0];

    // 自动跟随模式：打开窗口时自动读取当前选中 Timeline
    private bool autoFollowTimeline = true;

    // 快捷预设色
    private static readonly Color[] QuickColors = new Color[]
    {
        new Color(0.90f, 0.35f, 0.35f), // 红
        new Color(0.90f, 0.60f, 0.25f), // 橙
        new Color(0.90f, 0.85f, 0.30f), // 黄
        new Color(0.40f, 0.85f, 0.40f), // 绿
        new Color(0.35f, 0.65f, 0.90f), // 蓝
        new Color(0.50f, 0.40f, 0.90f), // 紫
        new Color(0.90f, 0.45f, 0.75f), // 粉
        new Color(0.55f, 0.55f, 0.55f), // 灰
    };

    #endregion

    #region --- 背景图相关字段 ---

    private string backgroundImagePath = "";
    private float backgroundOpacity = 0.30f;
    private Texture2D backgroundTexture;
    private bool backgroundEnabled = true;

    // Timeline 窗口引用（按 EditorWindow 实例缓存）
    private static Dictionary<EditorWindow, VisualElement> injectedLayers = new Dictionary<EditorWindow, VisualElement>();

    #endregion

    #region --- EditorPrefs Keys ---

    private const string PrefAutoFollow = "TimelineColorTool_AutoFollow";
    private const string PrefOpacity = "TimelineColorTool_Opacity";
    private const string PrefBgPath = "TimelineColorTool_BgPath";
    private const string PrefBgEnabled = "TimelineColorTool_BgEnabled";

    #endregion

    #region --- Unity 生命周期 ---

    private void OnEnable()
    {
        LoadPrefs();
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        SavePrefs();
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        // 自动跟随模式下，检测当前 Timeline 变更
        if (autoFollowTimeline)
        {
            var currentTimeline = GetCurrentTimelineAsset();
            if (currentTimeline != null && trackEntries.Count > 0)
            {
                bool sameTimeline = trackEntries.Count > 0 &&
                    trackEntries[0].track != null &&
                    trackEntries[0].track.timelineAsset == currentTimeline;

                if (!sameTimeline)
                {
                    Repaint(); // 触发 OnGUI 重建
                }
            }
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        GUILayout.Space(4);
        DrawTrackList();
        GUILayout.Space(6);
        DrawBackgroundSection();
    }

    #endregion

    #region --- 工具栏 ---

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 标题
        GUILayout.Label("Timeline 轨道颜色 & 背景图", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        // 自动跟随
        EditorGUI.BeginChangeCheck();
        autoFollowTimeline = GUILayout.Toggle(autoFollowTimeline, new GUIContent("自动跟随"), EditorStyles.toolbarButton,
            GUILayout.Width(70));
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefAutoFollow, autoFollowTimeline);
            if (autoFollowTimeline)
                RefreshTrackList();
        }

        // 刷新
        if (GUILayout.Button("刷新轨道", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshTrackList();
        }

        // 展开全部
        if (GUILayout.Button("展开全部", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            ExpandAll();
        }

        // 折叠全部
        if (GUILayout.Button("折叠全部", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            CollapseAll();
        }

        GUILayout.Space(6);
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region --- 轨道列表 ---

    private void DrawTrackList()
    {
        var timelineAsset = GetCurrentTimelineAsset();

        if (timelineAsset == null)
        {
            EditorGUILayout.HelpBox(
                "未找到 Timeline。请打开 Timeline 窗口并选中一个 Timeline 资源。",
                MessageType.Info);
            return;
        }

        // 自动刷新（首次或无轨道时）
        if (trackEntries.Count == 0)
        {
            RefreshTrackList();
            if (trackEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("当前 Timeline 没有轨道。", MessageType.Info);
                return;
            }
        }

        // 确保 foldout 数组大小
        if (trackFoldouts.Length != trackEntries.Count)
        {
            var newFoldouts = new bool[trackEntries.Count];
            for (int i = 0; i < Mathf.Min(trackFoldouts.Length, newFoldouts.Length); i++)
                newFoldouts[i] = trackFoldouts[i];
            trackFoldouts = newFoldouts;
        }

        // 表头
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("", GUILayout.Width(24)); // 色块
        GUILayout.Label("轨道名称", GUILayout.MinWidth(140));
        GUILayout.Label("类型", GUILayout.Width(80));
        GUILayout.Label("颜色", GUILayout.Width(160));
        GUILayout.Label("快捷色", GUILayout.Width(QuickColors.Length * 22));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // 滚动区
        trackScrollPos = EditorGUILayout.BeginScrollView(trackScrollPos);

        for (int i = 0; i < trackEntries.Count; i++)
        {
            DrawTrackRow(i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTrackRow(int index)
    {
        var entry = trackEntries[index];
        var track = entry.track;
        if (track == null) return;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        // 缩进
        float indentWidth = entry.indent * 20f;
        GUILayout.Space(indentWidth);

        // 折叠/展开箭头（有子轨道的 GroupTrack）
        bool hasChildren = track is GroupTrack && track.GetChildTracks().Any();
        if (hasChildren)
        {
            trackFoldouts[index] = EditorGUILayout.Foldout(trackFoldouts[index], "", true);
        }
        else
        {
            GUILayout.Space(18);
        }

        // 色块
        Color currentColor = GetTrackColor(track);
        Rect swatchRect = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
        EditorGUI.DrawRect(swatchRect, currentColor);
        EditorGUI.DrawRect(new Rect(swatchRect.x, swatchRect.y, swatchRect.width, 1), Color.black);
        EditorGUI.DrawRect(new Rect(swatchRect.x, swatchRect.y + swatchRect.height - 1, swatchRect.width, 1), Color.black);
        EditorGUI.DrawRect(new Rect(swatchRect.x, swatchRect.y, 1, swatchRect.height), Color.black);
        EditorGUI.DrawRect(new Rect(swatchRect.x + swatchRect.width - 1, swatchRect.y, 1, swatchRect.height), Color.black);

        // 名称
        GUILayout.Label(entry.displayName, GUILayout.MinWidth(120));

        // 类型
        GUILayout.Label(entry.typeName, EditorStyles.miniLabel, GUILayout.Width(80));

        // 颜色选择器
        EditorGUI.BeginChangeCheck();
        Color newColor = EditorGUILayout.ColorField(currentColor, GUILayout.Width(60));
        if (EditorGUI.EndChangeCheck())
        {
            SetTrackColor(track, newColor);
        }

        // 快捷预设色
        foreach (var quickC in QuickColors)
        {
            GUILayout.Space(1);
            Color bg = EditorGUIUtility.isProSkin ? Color.gray * 0.6f : Color.white * 0.85f;
            Rect qRect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18), GUILayout.Height(18));
            EditorGUI.DrawRect(qRect, bg);
            Rect inner = new Rect(qRect.x + 2, qRect.y + 2, qRect.width - 4, qRect.height - 4);
            EditorGUI.DrawRect(inner, quickC);
            if (Event.current.type == EventType.MouseDown && qRect.Contains(Event.current.mousePosition))
            {
                SetTrackColor(track, quickC);
                Event.current.Use();
                Repaint();
            }
        }

        GUILayout.Space(4);

        // 重置按钮
        if (GUILayout.Button("↺", GUILayout.ExpandWidth(false), GUILayout.Width(28)))
        {
            ResetTrackColor(track);
        }

        GUILayout.Space(2);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 递归扫描所有轨道，含 Sub-Group
    /// </summary>
    private void RefreshTrackList()
    {
        trackEntries.Clear();

        var timelineAsset = GetCurrentTimelineAsset();
        if (timelineAsset == null) return;

        ScanTracks(timelineAsset.GetRootTracks(), 0);
    }

    private void ScanTracks(IEnumerable<TrackAsset> tracks, int indent)
    {
        foreach (var track in tracks)
        {
            if (track == null) continue;

            var entry = new TrackEntry
            {
                track = track,
                indent = indent,
                displayName = track.name,
                typeName = GetShortTypeName(track.GetType()),
            };

            trackEntries.Add(entry);

            // 如果是 GroupTrack，递归扫描子轨道
            if (track is GroupTrack groupTrack)
            {
                var children = groupTrack.GetChildTracks();
                // 根据折叠状态决定是否展开
                int idx = trackEntries.Count - 1;
                if (idx < trackFoldouts.Length && !trackFoldouts[idx])
                    continue;
                ScanTracks(children, indent + 1);
            }
        }
    }

    private string GetShortTypeName(Type type)
    {
        string name = type.Name;
        if (name.EndsWith("Track"))
            name = name.Substring(0, name.Length - 5);
        if (name.EndsWith("Asset"))
            name = name.Substring(0, name.Length - 5);
        return name;
    }

    private void ExpandAll()
    {
        for (int i = 0; i < trackFoldouts.Length; i++)
            trackFoldouts[i] = true;
        RefreshTrackList();
    }

    private void CollapseAll()
    {
        for (int i = 0; i < trackFoldouts.Length; i++)
            trackFoldouts[i] = false;
        RefreshTrackList();
    }

    #endregion

    #region --- 颜色读写 (SerializedObject + 反射兜底 + 调试) ---

    // 一次性调试标记：首次成功时输出使用的方法
    private static bool s_DebugGetDone;
    private static bool s_DebugSetDone;

    private Color GetTrackColor(TrackAsset track)
    {
        if (track == null) return Color.white;

        // 方法1：反射访问 color 属性（最可靠——内部会处理 m_ColorIndex 回退）
        var prop = typeof(TrackAsset).GetProperty("color",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null)
        {
            try
            {
                Color c = (Color)prop.GetValue(track);
                if (!s_DebugGetDone) { Debug.Log($"[TimelineColorTool] Get: 反射 color 属性 → {c}"); s_DebugGetDone = true; }
                // 属性自身已处理 InvalidColor→ColorIndex 回退
                return c;
            }
            catch (Exception ex)
            {
                if (!s_DebugGetDone) Debug.LogWarning($"[TimelineColorTool] Get: 反射 color 属性异常: {ex.Message}");
            }
        }

        // 方法2：通过 SerializedObject.FindProperty("m_Color")
        var so = new SerializedObject(track);
        var colorProp = so.FindProperty("m_Color");
        if (colorProp != null)
        {
            Color c = colorProp.colorValue;
            if (!s_DebugGetDone) { Debug.Log($"[TimelineColorTool] Get: SerializedObject m_Color → {c}"); s_DebugGetDone = true; }
            if (c.r >= 0 && c.g >= 0 && c.b >= 0 && c.a >= 0)
                return c;
        }

        // 方法3：反射访问 m_Color 字段
        var field = typeof(TrackAsset).GetField("m_Color",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
        {
            Color c = (Color)field.GetValue(track);
            if (!s_DebugGetDone) { Debug.Log($"[TimelineColorTool] Get: 反射 m_Color 字段 → {c}"); s_DebugGetDone = true; }
            if (c.r >= 0 && c.g >= 0 && c.b >= 0 && c.a >= 0)
                return c;
        }

        // 方法4：遍历 SerializedObject 所有属性找颜色相关字段
        var allProps = new SerializedObject(track);
        var iter = allProps.GetIterator();
        if (iter.Next(true))
        {
            do
            {
                if (iter.name.ToLower().Contains("color") && iter.propertyType == SerializedPropertyType.Color)
                {
                    Color c = iter.colorValue;
                    if (!s_DebugGetDone) { Debug.Log($"[TimelineColorTool] Get: 遍历发现 '{iter.name}' → {c}"); s_DebugGetDone = true; }
                    if (c.r >= 0 && c.g >= 0 && c.b >= 0 && c.a >= 0)
                        return c;
                }
            } while (iter.Next(false));
        }

        if (!s_DebugGetDone) { Debug.LogWarning("[TimelineColorTool] Get: 所有方法均失败，返回白色"); s_DebugGetDone = true; }
        return Color.white;
    }

    private void SetTrackColor(TrackAsset track, Color color)
    {
        if (track == null) return;

        Undo.RecordObject(track, "Set Track Color");
        bool set = false;

        // 方法1：反射访问 color 属性 setter
        var prop = typeof(TrackAsset).GetProperty("color",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanWrite)
        {
            try
            {
                prop.SetValue(track, color);
                if (!s_DebugSetDone) { Debug.Log($"[TimelineColorTool] Set: 反射 color 属性 → {color}"); s_DebugSetDone = true; }
                set = true;
            }
            catch (Exception ex)
            {
                if (!s_DebugSetDone) Debug.LogWarning($"[TimelineColorTool] Set: 反射 color 属性异常: {ex.Message}");
            }
        }

        // 方法2：反射调用 SetColor(Color) 方法（某些版本有此方法）
        if (!set)
        {
            var setColorMethod = typeof(TrackAsset).GetMethod("SetColor",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new Type[] { typeof(Color) }, null);
            if (setColorMethod != null)
            {
                try
                {
                    setColorMethod.Invoke(track, new object[] { color });
                    if (!s_DebugSetDone) { Debug.Log($"[TimelineColorTool] Set: 反射 SetColor() → {color}"); s_DebugSetDone = true; }
                    set = true;
                }
                catch (Exception ex)
                {
                    if (!s_DebugSetDone) Debug.LogWarning($"[TimelineColorTool] Set: 反射 SetColor 异常: {ex.Message}");
                }
            }
        }

        // 方法3：通过 SerializedObject.FindProperty("m_Color")
        if (!set)
        {
            var so = new SerializedObject(track);
            var colorProp = so.FindProperty("m_Color");
            if (colorProp != null)
            {
                colorProp.colorValue = color;
                so.ApplyModifiedProperties();
                if (!s_DebugSetDone) { Debug.Log($"[TimelineColorTool] Set: SerializedObject m_Color → {color}"); s_DebugSetDone = true; }
                set = true;
            }
        }

        // 方法4：反射直接设 m_Color 字段
        if (!set)
        {
            var field = typeof(TrackAsset).GetField("m_Color",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(track, color);
                if (!s_DebugSetDone) { Debug.Log($"[TimelineColorTool] Set: 反射 m_Color 字段 → {color}"); s_DebugSetDone = true; }
                set = true;
            }
        }

        if (!set && !s_DebugSetDone)
        {
            Debug.LogWarning("[TimelineColorTool] Set: 所有方法均失败！");
            s_DebugSetDone = true;
        }

        // 标记轨道和父 Timeline 为脏，并强制保存
        EditorUtility.SetDirty(track);
        if (track.timelineAsset != null)
            EditorUtility.SetDirty(track.timelineAsset);

        RepaintTimelineWindow();
    }

    private void ResetTrackColor(TrackAsset track)
    {
        if (track == null) return;

        Undo.RecordObject(track, "Reset Track Color");
        Color invalid = new Color(-1f, -1f, -1f, -1f);

        // 优先通过反射设置
        var prop = typeof(TrackAsset).GetProperty("color",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanWrite)
        {
            try
            {
                prop.SetValue(track, invalid);
                EditorUtility.SetDirty(track);
                if (track.timelineAsset != null)
                    EditorUtility.SetDirty(track.timelineAsset);
                RepaintTimelineWindow();
                return;
            }
            catch { }
        }

        // 回退：SerializedObject
        var so = new SerializedObject(track);
        var colorProp = so.FindProperty("m_Color");
        if (colorProp != null)
        {
            colorProp.colorValue = invalid;
            so.ApplyModifiedProperties();
        }
        else
        {
            var field = typeof(TrackAsset).GetField("m_Color",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                field.SetValue(track, invalid);
        }

        EditorUtility.SetDirty(track);
        if (track.timelineAsset != null)
            EditorUtility.SetDirty(track.timelineAsset);
        RepaintTimelineWindow();
    }

    #endregion

    #region --- Timeline 窗口操作 ---

    /// <summary>
    /// 获取当前打开的 Timeline 资源
    /// </summary>
    private TimelineAsset GetCurrentTimelineAsset()
    {
        // 通过 UnityEditor.Timeline.TimelineEditor（公开 API）
        var type = Type.GetType("UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor");
        if (type != null)
        {
            var prop = type.GetProperty("inspectedAsset",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                return prop.GetValue(null) as TimelineAsset;
            }
        }

        // 回退：从 Selection 获取
        if (Selection.activeObject is TimelineAsset ta)
            return ta;

        return null;
    }

    /// <summary>
    /// 获取 Timeline 编辑器窗口实例
    /// </summary>
    private EditorWindow GetTimelineWindow()
    {
        var type = Type.GetType("UnityEditor.Timeline.TimelineWindow, Unity.Timeline.Editor");
        if (type == null) return null;

        var windows = Resources.FindObjectsOfTypeAll(type);
        if (windows.Length > 0)
            return windows[0] as EditorWindow;

        return null;
    }

    /// <summary>
    /// 重绘 Timeline 窗口
    /// </summary>
    private void RepaintTimelineWindow()
    {
        var tw = GetTimelineWindow();
        if (tw != null)
            tw.Repaint();

        Repaint();
        RefreshTrackList();
    }

    #endregion

    #region --- 自定义背景图 ---

    private void DrawBackgroundSection()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Timeline 背景图 ──", EditorStyles.centeredGreyMiniLabel);

        // 启用/禁用
        EditorGUI.BeginChangeCheck();
        backgroundEnabled = EditorGUILayout.Toggle("启用背景图", backgroundEnabled);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefBgEnabled, backgroundEnabled);
            ApplyBackgroundToTimeline();
        }

        EditorGUI.BeginDisabledGroup(!backgroundEnabled);

        // 背景图路径
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("背景图:", GUILayout.Width(50));
        EditorGUILayout.TextField(backgroundImagePath);
        if (GUILayout.Button("浏览...", GUILayout.Width(50)))
        {
            string newPath = EditorUtility.OpenFilePanel("选择背景图（PNG/JPG）",
                string.IsNullOrEmpty(backgroundImagePath) ? Application.dataPath : Path.GetDirectoryName(backgroundImagePath),
                "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(newPath))
            {
                backgroundImagePath = newPath;
                LoadBackgroundTexture();
                EditorPrefs.SetString(PrefBgPath, backgroundImagePath);
                ApplyBackgroundToTimeline();
            }
        }
        if (GUILayout.Button("清除", GUILayout.Width(36)))
        {
            backgroundImagePath = "";
            backgroundTexture = null;
            EditorPrefs.SetString(PrefBgPath, "");
            RemoveBackgroundFromTimeline();
        }
        EditorGUILayout.EndHorizontal();

        // 不透明度
        EditorGUI.BeginChangeCheck();
        backgroundOpacity = EditorGUILayout.Slider("不透明度", backgroundOpacity, 0.05f, 1.0f);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetFloat(PrefOpacity, backgroundOpacity);
            ApplyBackgroundToTimeline();
        }

        EditorGUI.EndDisabledGroup();

        // 预览
        if (backgroundEnabled && backgroundTexture != null)
        {
            GUILayout.Space(4);
            Rect previewRect = GUILayoutUtility.GetRect(200, 80, GUILayout.ExpandWidth(true));
            previewRect.width = Mathf.Min(previewRect.width, 400);
            float aspect = (float)backgroundTexture.width / backgroundTexture.height;
            float previewH = previewRect.width / aspect;
            if (previewH > 80) { previewH = 80; previewRect.width = previewH * aspect; }
            previewRect.height = previewH;
            previewRect.x += (EditorGUIUtility.currentViewWidth - previewRect.width) * 0.5f;

            EditorGUI.DrawRect(previewRect, EditorGUIUtility.isProSkin ? Color.black : Color.gray);
            GUI.DrawTexture(previewRect, backgroundTexture, ScaleMode.ScaleToFit);

            GUILayout.Space(previewH + 4);
        }

        // 操作说明
        EditorGUILayout.HelpBox(
            "点「浏览」选择 PNG/JPG 图片作为 Timeline 窗口背景。\n" +
            "不透明度滑块实时调节背景可见度。\n" +
            "关闭此窗口不会移除已注入的背景，需点「清除」或关闭 Timeline 窗口。",
            MessageType.None);
    }

    private void LoadBackgroundTexture()
    {
        backgroundTexture = null;
        if (string.IsNullOrEmpty(backgroundImagePath) || !File.Exists(backgroundImagePath))
            return;

        byte[] data = File.ReadAllBytes(backgroundImagePath);
        var tex = new Texture2D(2, 2);
        if (tex.LoadImage(data))
        {
            backgroundTexture = tex;
        }
        else
        {
            Debug.LogWarning("[TimelineColorTool] 无法加载背景图: " + backgroundImagePath);
        }
    }

    /// <summary>
    /// 将背景图注入 Timeline 窗口
    /// 使用 UI Toolkit：在 rootVisualElement 最底层插入一个独立背景层 VisualElement
    /// 通过对内容区域（timelineArea/trackList/sequenceContent/contentView）移除外层背景色来实现透明
    /// </summary>
    private void ApplyBackgroundToTimeline()
    {
        var tw = GetTimelineWindow();
        if (tw == null) return;

        if (!backgroundEnabled || backgroundTexture == null || string.IsNullOrEmpty(backgroundImagePath))
        {
            RemoveBackgroundFromTimeline();
            return;
        }

        // 确保纹理已加载
        if (backgroundTexture == null)
            LoadBackgroundTexture();
        if (backgroundTexture == null) return;

        var root = tw.rootVisualElement;
        if (root == null) return;

        // 移除旧注入层
        RemoveBackgroundFromTimeline();

        // 创建背景层
        var bgLayer = new VisualElement();
        bgLayer.name = "__TimelineColorTool_BgLayer";
        bgLayer.style.position = Position.Absolute;
        bgLayer.style.top = 0;
        bgLayer.style.left = 0;
        bgLayer.style.right = 0;
        bgLayer.style.bottom = 0;
        bgLayer.style.opacity = backgroundOpacity;
        bgLayer.pickingMode = PickingMode.Ignore; // 不拦截鼠标事件

        // 设置背景图（使用兼容旧版 Unity 的 API）
        bgLayer.style.backgroundImage = new StyleBackground(backgroundTexture);
        bgLayer.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; // = Cover 效果

        // 插入最底层
        root.Insert(0, bgLayer);
        injectedLayers[tw] = bgLayer;

        // 让内容区域透明化 —— 查找关键 USS 类名并设置透明背景
        MakeContentTransparent(root);

        tw.Repaint();
        Debug.Log($"[TimelineColorTool] 背景图已注入 Timeline 窗口 (opacity={backgroundOpacity:F2})");
    }

    private void RemoveBackgroundFromTimeline()
    {
        var tw = GetTimelineWindow();
        if (tw == null) return;

        var root = tw.rootVisualElement;
        if (root == null) return;

        // 移除注入的背景层
        if (injectedLayers.TryGetValue(tw, out var layer))
        {
            if (layer.parent == root)
                root.Remove(layer);
            injectedLayers.Remove(tw);
        }

        // 也可以按名称清理
        var existing = root.Q("__TimelineColorTool_BgLayer");
        if (existing != null)
            root.Remove(existing);

        // 恢复内容区域背景色
        RestoreContentBackground(root);

        tw.Repaint();
    }

    /// <summary>
    /// 将 Timeline 内容区域的背景设为透明，以显示底层背景图
    /// </summary>
    private void MakeContentTransparent(VisualElement root)
    {
        if (root == null) return;

        // 关键 USS 类名
        string[] targetClasses = { "timelineArea", "trackList", "sequenceContent", "contentView",
                                    "timeline-header", "timeline-bottom-bar" };

        foreach (var cls in targetClasses)
        {
            var elements = root.Query(className: cls).ToList();
            foreach (var el in elements)
            {
                // 保存原始颜色（用于之后恢复）
                if (el.style.backgroundColor.value.r >= 0 && el.style.backgroundColor.value.a > 0)
                {
                    el.userData = el.style.backgroundColor.value;
                }

                // 设为透明
                el.style.backgroundColor = new StyleColor(Color.clear);
            }
        }
    }

    private void RestoreContentBackground(VisualElement root)
    {
        if (root == null) return;

        string[] targetClasses = { "timelineArea", "trackList", "sequenceContent", "contentView",
                                    "timeline-header", "timeline-bottom-bar" };

        foreach (var cls in targetClasses)
        {
            var elements = root.Query(className: cls).ToList();
            foreach (var el in elements)
            {
                if (el.userData is Color originalColor)
                {
                    el.style.backgroundColor = new StyleColor(originalColor);
                }
            }
        }
    }

    #endregion

    #region --- EditorPrefs 持久化 ---

    private void LoadPrefs()
    {
        autoFollowTimeline = EditorPrefs.GetBool(PrefAutoFollow, true);
        backgroundOpacity = EditorPrefs.GetFloat(PrefOpacity, 0.30f);
        backgroundImagePath = EditorPrefs.GetString(PrefBgPath, "");
        backgroundEnabled = EditorPrefs.GetBool(PrefBgEnabled, true);

        if (!string.IsNullOrEmpty(backgroundImagePath))
            LoadBackgroundTexture();
    }

    private void SavePrefs()
    {
        EditorPrefs.SetBool(PrefAutoFollow, autoFollowTimeline);
        EditorPrefs.SetFloat(PrefOpacity, backgroundOpacity);
        EditorPrefs.SetString(PrefBgPath, backgroundImagePath);
        EditorPrefs.SetBool(PrefBgEnabled, backgroundEnabled);
    }

    #endregion
}
