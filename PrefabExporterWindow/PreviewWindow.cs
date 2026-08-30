using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// 资源预览子窗口：以卡片或列表形式展示 Prefab 的所有依赖资源，支持类型过滤、勾选和定位。
/// </summary>
public class PreviewWindow : EditorWindow
{
    // ──────────────────────────────────────────────
    //  视图模式
    // ──────────────────────────────────────────────
    private enum ViewMode
    {
        Grid,   // 图块模式（卡片）
        List    // 列表模式
    }

    private ViewMode viewMode = ViewMode.Grid;

    // ──────────────────────────────────────────────
    //  数据
    // ──────────────────────────────────────────────
    private ExportItem currentItem;
    private Vector2 scroll;

    // 类型过滤开关
    private bool tex = true, mdl = true, mat = true, ani = false, oth = false;

    private class ResItem
    {
        public string    path;
        public string    type;
        public Texture2D preview;
    }

    private List<ResItem> items = new List<ResItem>();

    // ──────────────────────────────────────────────
    //  打开窗口
    // ──────────────────────────────────────────────
    public static void ShowWindow(ExportItem item)
    {
        var win = GetWindow<PreviewWindow>("资源预览");
        win.currentItem = item;
        win.LoadPreferences();   // 读取视图模式偏好
        win.Refresh();
        win.Show();
    }

    // ──────────────────────────────────────────────
    //  偏好存储
    // ──────────────────────────────────────────────
    private void LoadPreferences()
    {
        viewMode = (ViewMode)EditorPrefs.GetInt("PreviewWindow_ViewMode", 0);
    }

    private void SavePreferences()
    {
        EditorPrefs.SetInt("PreviewWindow_ViewMode", (int)viewMode);
    }

    // ──────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────
    private void OnGUI()
    {
        if (currentItem?.prefab == null)
        {
            EditorGUILayout.HelpBox("请选择有效Prefab", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField($"预览: {currentItem.prefab.name}", EditorStyles.boldLabel);
        GUILayout.Space(5);

        // ---- 工具栏（类型过滤 + 视图切换） ----
        EditorGUILayout.BeginHorizontal();

        // 类型过滤
        bool t  = EditorGUILayout.ToggleLeft("贴图", tex, GUILayout.Width(60));
        bool m  = EditorGUILayout.ToggleLeft("模型", mdl, GUILayout.Width(60));
        bool mt = EditorGUILayout.ToggleLeft("材质", mat, GUILayout.Width(70));
        bool a  = EditorGUILayout.ToggleLeft("动画", ani, GUILayout.Width(80));
        bool o  = EditorGUILayout.ToggleLeft("其他", oth, GUILayout.Width(60));
        if (t != tex || m != mdl || mt != mat || a != ani || o != oth)
        {
            tex = t; mdl = m; mat = mt; ani = a; oth = o;
            Refresh();
        }

        GUILayout.FlexibleSpace();

        // 视图切换按钮
        ViewMode newMode = viewMode;
        if (GUILayout.Button(new GUIContent("图块", "卡片/网格视图"), 
                viewMode == ViewMode.Grid ? EditorStyles.miniButtonLeft : EditorStyles.miniButtonMid, GUILayout.Width(50)))
            newMode = ViewMode.Grid;
        if (GUILayout.Button(new GUIContent("列表", "列表视图"), 
                viewMode == ViewMode.List ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid, GUILayout.Width(50)))
            newMode = ViewMode.List;

        if (newMode != viewMode)
        {
            viewMode = newMode;
            SavePreferences();
            // 切换模式后重绘即可，无需重建数据
        }

        if (GUILayout.Button("刷新", GUILayout.Width(60)))
            Refresh();

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);

        if (items.Count == 0)
        {
            EditorGUILayout.HelpBox("无资源", MessageType.Info);
            return;
        }

        // 根据当前模式绘制不同布局
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (viewMode == ViewMode.Grid)
            DrawGrid();
        else
            DrawList();
        EditorGUILayout.EndScrollView();
    }

    // ──────────────────────────────────────────────
    //  图块模式（卡片网格）
    // ──────────────────────────────────────────────
    private void DrawGrid()
    {
        const float w   = 100f;
        const float h   = 110f;
        const float pad = 10f;
        int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (w + pad)));

        for (int i = 0; i < items.Count; i += cols)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = 0; j < cols && i + j < items.Count; j++)
                DrawCard(items[i + j]);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
        }
    }

    private void DrawCard(ResItem r)
    {
        bool sel = currentItem.dependencies.TryGetValue(r.path, out var dep) && dep.selected;

        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(100), GUILayout.Height(110));

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(20));
        if (newSel != sel && currentItem.dependencies.ContainsKey(r.path))
            currentItem.dependencies[r.path] = (newSel, currentItem.dependencies[r.path].assetType);
        GUILayout.EndHorizontal();

        if (r.preview != null)
        {
            GUILayout.Label(r.preview, GUILayout.Width(80), GUILayout.Height(80));
        }
        else
        {
            var obj  = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.path);
            var icon = EditorGUIUtility.ObjectContent(obj, obj?.GetType()).image;
            GUILayout.Label(icon, GUILayout.Width(80), GUILayout.Height(80));
        }

        GUILayout.Label(Path.GetFileName(r.path), EditorStyles.label,     GUILayout.Width(90), GUILayout.Height(20));
        GUILayout.Label(GetTypeName(r.type),       EditorStyles.miniLabel);

        GUILayout.EndVertical();

        // 点击卡片区域 → Ping + 选中
        if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)
            && Event.current.type == EventType.MouseDown)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.path);
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
            Event.current.Use();
        }
    }

    // ──────────────────────────────────────────────
    //  列表模式
    // ──────────────────────────────────────────────
    private void DrawList()
    {
        // 表头
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label(" ", GUILayout.Width(20));            // 勾选占位
        GUILayout.Label("预览", GUILayout.Width(40));
        GUILayout.Label("资源名称", GUILayout.Width(150));
        GUILayout.Label("类型", GUILayout.Width(60));
        GUILayout.Label("路径", GUILayout.ExpandWidth(true));
        GUILayout.Label("定位", GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        foreach (var r in items)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(32));

            // 勾选框
            bool sel = currentItem.dependencies.TryGetValue(r.path, out var dep) && dep.selected;
            bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(20));
            if (newSel != sel && currentItem.dependencies.ContainsKey(r.path))
                currentItem.dependencies[r.path] = (newSel, currentItem.dependencies[r.path].assetType);

            // 缩略图 (32x32)
            Texture2D thumb = r.preview;
            if (thumb == null)
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.path);
                thumb = AssetPreview.GetMiniThumbnail(obj);
            }
            GUILayout.Label(thumb, GUILayout.Width(32), GUILayout.Height(32));

            // 文件名（不带路径）
            string fileName = Path.GetFileName(r.path);
            GUILayout.Label(fileName, GUILayout.Width(150));

            // 类型（中文）
            GUILayout.Label(GetTypeName(r.type), GUILayout.Width(60));

            // 资源路径（相对项目根目录）
            GUILayout.Label(r.path, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));

            // 定位按钮
            if (GUILayout.Button("📍", GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.path);
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    // ──────────────────────────────────────────────
    //  数据刷新
    // ──────────────────────────────────────────────
    private void Refresh()
    {
        items.Clear();
        if (currentItem?.prefab == null) return;

        string path = AssetDatabase.GetAssetPath(currentItem.prefab);
        AddItem(path, GetTypeStr(path));

        foreach (string d in AssetDatabase.GetDependencies(path, true))
        {
            if (d == path || d.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            string t = GetTypeStr(d);
            if (IncludeType(t)) AddItem(d, t);
        }
    }

    private void AddItem(string p, string t)
    {
        if (items.Any(i => i.path == p)) return;
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
        items.Add(new ResItem
        {
            path    = p,
            type    = t,
            preview = AssetPreview.GetAssetPreview(obj) ?? AssetPreview.GetMiniThumbnail(obj)
        });
    }

    // ──────────────────────────────────────────────
    //  类型工具
    // ──────────────────────────────────────────────
    private static string GetTypeStr(string p)
    {
        Type t = AssetDatabase.GetMainAssetTypeAtPath(p);
        if (t == null) return "Other";
        if (t == typeof(Texture2D) || t == typeof(Texture) || t == typeof(Sprite))        return "Texture";
        if (t == typeof(Mesh)      || t == typeof(GameObject))                             return "Model";
        if (t == typeof(Material))                                                         return "Material";
        if (t == typeof(AnimationClip)
            || t == typeof(AnimatorController)
            || t == typeof(AnimatorOverrideController))                                    return "Animation";
        return "Other";
    }

    private static string GetTypeName(string t)
    {
        switch (t)
        {
            case "Texture":   return "贴图";
            case "Model":     return "模型";
            case "Material":  return "材质";
            case "Animation": return "动画";
            default:          return "其他";
        }
    }

    private bool IncludeType(string t)
    {
        switch (t)
        {
            case "Texture":   return tex;
            case "Model":     return mdl;
            case "Material":  return mat;
            case "Animation": return ani;
            default:          return oth;
        }
    }
}