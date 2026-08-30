using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

/// <summary>
/// Prefab 导出工具主窗口。
/// 依赖：ExportItem / ExcelReader / DependencyHelper / PreviewWindow
/// </summary>
public class PrefabExporterWindow : EditorWindow
{
    // ──────────────────────────────────────────────
    //  常量
    // ──────────────────────────────────────────────
    private const string NameMappingFolder = "Assets/命名文档";

    // ──────────────────────────────────────────────
    //  状态字段
    // ──────────────────────────────────────────────
    private List<ExportItem> exportItems = new List<ExportItem>();
    private string exportPath = "";
    private Vector2 scrollPosition;
    private Vector2 dependenciesScrollPosition;

    // 通用开关
    private bool autoGenerateNames       = true;
    private bool showDependencies        = true;
    private bool includeAllDependencies  = true;
    private bool includeScripts          = false;

    // 资源类型筛选
    private bool includeTextures   = true;
    private bool includeModels     = true;
    private bool includeMaterials  = true;
    private bool includeAnimations = false;
    private bool includeOthers     = false;

    // 布局
    private float   splitRatio   = 0.5f;  // 左右分栏比例
    private float   topRatio     = 0.75f; // 顶部区域占窗口高度比例
    private bool    isDraggingHSplitter;  // 左右分割线拖动中
    private bool    isDraggingVSplitter;  // 上下分割线拖动中

    // 命名文件列表
    private List<string> availableNameMappingFiles = new List<string>();

    private readonly string[] columnOptions = {
        "A","B","C","D","E","F","G","H","I","J","K","L","M",
        "N","O","P","Q","R","S","T","U","V","W","X","Y","Z"
    };

    // 映射参数
    private string prefixColumn          = "D";
    private string mappingColumn         = "F";
    private string keyValueSeparator     = ":";
    private string joinSeparator         = "_";
    private bool   descriptionFirst      = true;
    private string customLineSeparator   = "\n";

    // 工作表选择
    private string              selectedMappingFile  = "";
    private string              selectedSheetName    = "特效";
    private List<string>        availableSheetNames  = new List<string>();
    private Dictionary<string, string> sheetNameToPath = new Dictionary<string, string>();

    // ──────────────────────────────────────────────
    //  菜单入口
    // ──────────────────────────────────────────────
    [MenuItem("Tools/TvTTools/Prefab导出工具")]
    public static void ShowWindow()
    {
        GetWindow<PrefabExporterWindow>("Prefab导出工具");
    }

    // ──────────────────────────────────────────────
    //  生命周期
    // ──────────────────────────────────────────────
    private void OnEnable()
    {
        RefreshNameMappingFiles();
        splitRatio = EditorPrefs.GetFloat("PrefabExporter_SplitRatio", 0.5f);
        topRatio   = EditorPrefs.GetFloat("PrefabExporter_TopRatio", 0.75f);

        prefixColumn       = EditorPrefs.GetString("PrefabExporter_PrefixColumn", "D");
        mappingColumn      = EditorPrefs.GetString("PrefabExporter_MappingColumn", "F");
        keyValueSeparator  = EditorPrefs.GetString("PrefabExporter_KeyValueSeparator", ":");
        joinSeparator      = EditorPrefs.GetString("PrefabExporter_JoinSeparator", "_");
        descriptionFirst   = EditorPrefs.GetBool  ("PrefabExporter_DescriptionFirst", true);
        customLineSeparator = EditorPrefs.GetString("PrefabExporter_LineSeparator", "\n");
        selectedMappingFile = EditorPrefs.GetString("PrefabExporter_SelectedMappingFile", "");
        selectedSheetName   = EditorPrefs.GetString("PrefabExporter_SelectedSheetName", "特效");

        if (!string.IsNullOrEmpty(selectedMappingFile) && availableNameMappingFiles.Contains(selectedMappingFile))
            RefreshSheetListForFile(selectedMappingFile);
        else if (availableNameMappingFiles.Count > 0 && string.IsNullOrEmpty(selectedMappingFile))
            selectedMappingFile = availableNameMappingFiles[0];
    }

    // ──────────────────────────────────────────────
    //  主 GUI
    // ──────────────────────────────────────────────
    private void OnGUI()
    {
        float totalWidth  = position.width;
        float totalHeight = position.height;

        // ── 标题 ──
        Rect titleRect = new Rect(0, 8, totalWidth, 18);
        EditorGUI.LabelField(titleRect, "Prefab导出工具", EditorStyles.boldLabel);

        float contentTop = 30f;
        float topAreaH   = Mathf.Max((totalHeight - contentTop) * topRatio, 120f);
        float dividerH   = 6f;
        float splitterW  = 6f;

        // █████████████ 顶部区域（无滚动）█████████████
        Rect topRect = new Rect(0, contentTop, totalWidth, topAreaH);
        GUILayout.BeginArea(topRect);

        EditorGUILayout.BeginHorizontal();
        {
            float leftW  = (totalWidth - splitterW) * splitRatio;
            float rightW = totalWidth - splitterW - leftW;

            // ── 左侧：SettingsBox + 拖拽区域 + 按钮 ──
            EditorGUILayout.BeginVertical(GUILayout.Width(leftW));
            {
                float dragW    = leftW * 0.5f;   // 拖拽区域 = SettingsBox 一半
                float settW    = leftW - dragW - 4;
                Rect   dragRect = Rect.zero;

                EditorGUILayout.BeginHorizontal();
                {
                    // SettingsBox
                    EditorGUILayout.BeginVertical(GUILayout.Width(settW));
                    DrawSettingsBox();
                    EditorGUILayout.EndVertical();

                    GUILayout.Space(4);

                    // 拖拽区域（取得 SettingsBox 布局后的实际行高）
                    Rect columnRect = GUILayoutUtility.GetLastRect();
                    float boxHeight = Mathf.Max(columnRect.yMax, EditorGUIUtility.singleLineHeight * 7);
                    Rect rawRect = GUILayoutUtility.GetRect(dragW, boxHeight, GUILayout.ExpandHeight(true));
                    dragRect = GUILayoutUtility.GetLastRect();
                }
                EditorGUILayout.EndHorizontal();

                // 在最终坐标绘制拖拽框
                {
                    Rect final = new Rect(dragRect.x, dragRect.y, dragW, dragRect.height);
                    GUI.Box(final, "拖拽Prefab\n到此处", EditorStyles.helpBox);
                    HandleDropEvents(final);
                }

                GUILayout.Space(4);
                if (GUILayout.Button("+ 添加Prefab", GUILayout.Height(28)))
                    exportItems.Add(new ExportItem());
            }
            EditorGUILayout.EndVertical();

            // ── 分割线 ──
            {
                Rect sr = GUILayoutUtility.GetRect(splitterW, 1, GUILayout.ExpandHeight(true));
                float lineX = sr.x + sr.width / 2f;
                EditorGUI.DrawRect(new Rect(lineX - 0.5f, sr.y, 1, sr.height),
                    new Color(0.45f, 0.45f, 0.45f, 1f));
                HandleHorizontalSplitter(sr, totalWidth, contentTop);
            }

            // ── 右侧：AdvancedMappingPanel ──
            EditorGUILayout.BeginVertical(GUILayout.Width(rightW));
            DrawAdvancedMappingPanel();
            GUILayout.Space(4);
            if (GUILayout.Button("高级匹配（拆分+拼接）", GUILayout.Height(28)))
                MatchExportNamesFromExcel();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.EndArea();

        // █████████████████ 上下分割线 █████████████████
        float topDividerY = contentTop + topAreaH;
        Rect topDividerRect = new Rect(0, topDividerY, totalWidth, dividerH);
        EditorGUI.DrawRect(new Rect(0, topDividerY + dividerH / 2f - 1, totalWidth, 2),
            new Color(0.45f, 0.45f, 0.45f, 1f));
        EditorGUIUtility.AddCursorRect(topDividerRect, MouseCursor.ResizeVertical);
        HandleVerticalSplitter(topDividerRect, totalHeight, contentTop);

        // █████████████████ 底部区域 █████████████████
        float bottomY = topDividerY + dividerH;
        float bottomH = totalHeight - bottomY;
        Rect bottomRect = new Rect(0, bottomY, totalWidth, bottomH);
        GUILayout.BeginArea(bottomRect);
        {
            DrawPrefabList();
            GUILayout.Space(6);
            DrawExportPath();
            GUILayout.Space(6);
            if (GUILayout.Button("导出UnityPackages", GUILayout.Height(40)))
                ExportUnityPackages();
        }
        GUILayout.EndArea();
    }

    // ── 分割线拖动处理器 ──

    private void HandleHorizontalSplitter(Rect contentRect, float totalWidth, float areaY)
    {
        // 转换到屏幕坐标（BeginArea 内的 Rect 需要加上 Area 的 Y 偏移）
        Rect screenRect = new Rect(contentRect.x, areaY + contentRect.y, contentRect.width, contentRect.height);

        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when screenRect.Contains(e.mousePosition):
                isDraggingHSplitter = true;
                e.Use();
                break;
            case EventType.MouseUp:
                isDraggingHSplitter = false;
                break;
            case EventType.MouseDrag when isDraggingHSplitter:
                splitRatio = Mathf.Clamp(e.mousePosition.x / totalWidth, 0.2f, 0.65f);
                EditorPrefs.SetFloat("PrefabExporter_SplitRatio", splitRatio);
                Repaint();
                e.Use();
                break;
            case EventType.Repaint:
                EditorGUIUtility.AddCursorRect(screenRect, MouseCursor.ResizeHorizontal);
                break;
        }
    }

    private void HandleVerticalSplitter(Rect r, float totalHeight, float minTop)
    {
        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when r.Contains(e.mousePosition):
                isDraggingVSplitter = true;
                e.Use();
                break;
            case EventType.MouseUp:
                isDraggingVSplitter = false;
                break;
            case EventType.MouseDrag when isDraggingVSplitter:
                float newTopRatio = (e.mousePosition.y - minTop) / (totalHeight - minTop);
                topRatio = Mathf.Clamp(newTopRatio, 0.25f, 0.90f);
                EditorPrefs.SetFloat("PrefabExporter_TopRatio", topRatio);
                Repaint();
                e.Use();
                break;
            case EventType.Repaint:
                EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  GUI 子区域
    // ──────────────────────────────────────────────

    private void DrawSettingsBox()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);

        DrawToggleRow("自动生成导出名称:", ref autoGenerateNames, 120);
        DrawToggleRow("显示依赖项:",       ref showDependencies,  120);
        DrawToggleRow("默认包含所有依赖:", ref includeAllDependencies, 120);
        DrawToggleRow("包含脚本文件:",     ref includeScripts,    120);

        EditorGUILayout.LabelField("导出内容类型", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        includeTextures   = EditorGUILayout.ToggleLeft("贴图",   includeTextures,   GUILayout.Width(60));
        includeModels     = EditorGUILayout.ToggleLeft("模型",   includeModels,     GUILayout.Width(60));
        includeMaterials  = EditorGUILayout.ToggleLeft("材质球", includeMaterials,  GUILayout.Width(70));
        includeAnimations = EditorGUILayout.ToggleLeft("动画文件", includeAnimations, GUILayout.Width(80));
        includeOthers     = EditorGUILayout.ToggleLeft("其他",   includeOthers,     GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawToggleRow(string label, ref bool value, int labelWidth)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        value = EditorGUILayout.Toggle(value);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAdvancedMappingPanel()
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.BeginVertical(GUI.skin.box);

        // Excel 文件选择
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Excel 文件:", GUILayout.Width(80));
        int fileIndex = availableNameMappingFiles.IndexOf(selectedMappingFile);
        if (fileIndex < 0 && availableNameMappingFiles.Count > 0) fileIndex = 0;
        int newFileIndex = EditorGUILayout.Popup(fileIndex, availableNameMappingFiles.ToArray(), GUILayout.Width(200));
        if (newFileIndex != fileIndex && newFileIndex >= 0 && newFileIndex < availableNameMappingFiles.Count)
        {
            selectedMappingFile = availableNameMappingFiles[newFileIndex];
            EditorPrefs.SetString("PrefabExporter_SelectedMappingFile", selectedMappingFile);
            RefreshSheetListForFile(selectedMappingFile);
        }
        EditorGUILayout.EndHorizontal();

        // 工作表选择
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("工作表:", GUILayout.Width(80));
        if (availableSheetNames.Count > 0)
        {
            int sheetIndex = availableSheetNames.IndexOf(selectedSheetName);
            if (sheetIndex < 0) sheetIndex = 0;
            int newSheetIndex = EditorGUILayout.Popup(sheetIndex, availableSheetNames.ToArray(), GUILayout.Width(150));
            if (newSheetIndex != sheetIndex && newSheetIndex >= 0 && newSheetIndex < availableSheetNames.Count)
            {
                selectedSheetName = availableSheetNames[newSheetIndex];
                EditorPrefs.SetString("PrefabExporter_SelectedSheetName", selectedSheetName);
            }
        }
        else
        {
            EditorGUILayout.LabelField("(请先选择一个Excel文件)");
        }
        EditorGUILayout.EndHorizontal();

        // 前缀列
        DrawColumnPopup("前缀列:", ref prefixColumn, "PrefabExporter_PrefixColumn", 80);

        // 映射列
        DrawColumnPopup("映射列:", ref mappingColumn, "PrefabExporter_MappingColumn", 80);

        // 键值对分隔符
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("键值对分隔符:", GUILayout.Width(100));
        string newSep = EditorGUILayout.TextField(keyValueSeparator, GUILayout.Width(60));
        if (newSep != keyValueSeparator)
        {
            keyValueSeparator = string.IsNullOrEmpty(newSep) ? ":" : newSep;
            EditorPrefs.SetString("PrefabExporter_KeyValueSeparator", keyValueSeparator);
        }
        EditorGUILayout.EndHorizontal();

        // 拼接分隔符
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("拼接分隔符:", GUILayout.Width(100));
        string newJoin = EditorGUILayout.TextField(joinSeparator, GUILayout.Width(60));
        if (newJoin != joinSeparator)
        {
            joinSeparator = string.IsNullOrEmpty(newJoin) ? "_" : newJoin;
            EditorPrefs.SetString("PrefabExporter_JoinSeparator", joinSeparator);
        }
        EditorGUILayout.EndHorizontal();

        // 格式顺序
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("映射列格式:", GUILayout.Width(100));
        bool newDescFirst = EditorGUILayout.ToggleLeft("描述:资源名", descriptionFirst,  GUILayout.Width(100));
        bool newResFirst  = EditorGUILayout.ToggleLeft("资源名:描述", !descriptionFirst, GUILayout.Width(100));
        if (newDescFirst != descriptionFirst)
        {
            descriptionFirst = true;
            EditorPrefs.SetBool("PrefabExporter_DescriptionFirst", descriptionFirst);
        }
        else if (newResFirst != !descriptionFirst)
        {
            descriptionFirst = false;
            EditorPrefs.SetBool("PrefabExporter_DescriptionFirst", descriptionFirst);
        }
        EditorGUILayout.EndHorizontal();

        // 行分隔符
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("行分隔符（转义符）:", GUILayout.Width(150));
        string newLineSep = EditorGUILayout.TextField(customLineSeparator, GUILayout.Width(60));
        if (newLineSep != customLineSeparator)
        {
            customLineSeparator = string.IsNullOrEmpty(newLineSep) ? "\n" : newLineSep;
            EditorPrefs.SetString("PrefabExporter_LineSeparator", customLineSeparator);
        }
        GUILayout.Label("（\\n 表示换行）", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel--;
    }

    private void DrawColumnPopup(string label, ref string column, string prefKey, int labelWidth)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        int idx    = Mathf.Max(0, Array.IndexOf(columnOptions, column));
        int newIdx = EditorGUILayout.Popup(idx, columnOptions, GUILayout.Width(60));
        if (columnOptions[newIdx] != column)
        {
            column = columnOptions[newIdx];
            EditorPrefs.SetString(prefKey, column);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPrefabList()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Prefab资源",  EditorStyles.boldLabel, GUILayout.Width(position.width * 0.4f));
        GUILayout.Label("导出名称",    EditorStyles.boldLabel, GUILayout.Width(position.width * 0.4f));
        GUILayout.Label("操作",        EditorStyles.boldLabel, GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        for (int i = 0; i < exportItems.Count; i++)
        {
            var item = exportItems[i];

            EditorGUILayout.BeginHorizontal();
            item.prefab     = (GameObject)EditorGUILayout.ObjectField(item.prefab, typeof(GameObject), false, GUILayout.Width(position.width * 0.4f));
            item.exportName = EditorGUILayout.TextField(item.exportName, GUILayout.Width(position.width * 0.4f));

            if (GUILayout.Button("-", GUILayout.Width(30)))
            {
                exportItems.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            if (GUILayout.Button("预览", GUILayout.Width(40)))
                PreviewWindow.ShowWindow(item);
            EditorGUILayout.EndHorizontal();

            if (autoGenerateNames && item.prefab != null && string.IsNullOrEmpty(item.exportName))
                item.exportName = item.prefab.name;

            if (item.prefab != null && showDependencies)
            {
                if (item.dependencies.Count == 0)
                    RefreshItemDependencies(item);

                item.foldout = EditorGUILayout.Foldout(item.foldout, $"依赖资源 ({item.dependencies.Count})");
                if (item.foldout)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("全选", GUILayout.Width(60))) DependencyHelper.SelectAllDependencies(item, true);
                    if (GUILayout.Button("反选", GUILayout.Width(60))) DependencyHelper.ToggleAllDependencies(item);
                    if (GUILayout.Button("刷新", GUILayout.Width(60))) RefreshItemDependencies(item);
                    EditorGUILayout.EndHorizontal();

                    dependenciesScrollPosition = EditorGUILayout.BeginScrollView(dependenciesScrollPosition, GUILayout.Height(150));
                    foreach (var dep in item.dependencies.ToList())
                    {
                        EditorGUILayout.BeginHorizontal();
                        bool sel = EditorGUILayout.Toggle(dep.Value.selected, GUILayout.Width(20));
                        if (sel != dep.Value.selected)
                            item.dependencies[dep.Key] = (sel, dep.Value.assetType);
                        GUILayout.Label(Path.GetFileName(dep.Key));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"[{dep.Value.assetType}] {dep.Key}", EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                    EditorGUI.indentLevel--;
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawExportPath()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("导出路径:", GUILayout.Width(70));
        EditorGUILayout.LabelField(exportPath, EditorStyles.textField);
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string newPath = EditorUtility.SaveFolderPanel("选择导出路径", exportPath, "");
            if (!string.IsNullOrEmpty(newPath)) exportPath = newPath;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void RefreshNameMappingFiles()
    {
        availableNameMappingFiles.Clear();
        string folder = Path.Combine(Directory.GetCurrentDirectory(), NameMappingFolder);
        if (!Directory.Exists(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.xlsx", SearchOption.AllDirectories);
        foreach (string abs in files)
        {
            string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), abs).Replace("\\", "/");
            availableNameMappingFiles.Add(rel);
        }
        availableNameMappingFiles = availableNameMappingFiles.OrderBy(x => x).ToList();

        if (availableNameMappingFiles.Count > 0 && string.IsNullOrEmpty(selectedMappingFile))
            selectedMappingFile = availableNameMappingFiles[0];
    }

    // ──────────────────────────────────────────────
    //  工作表列表刷新
    // ──────────────────────────────────────────────

    private void RefreshSheetListForFile(string excelRelativePath)
    {
        availableSheetNames.Clear();
        sheetNameToPath.Clear();
        if (string.IsNullOrEmpty(excelRelativePath)) return;

        string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), excelRelativePath);
        if (!File.Exists(absolutePath))
        {
            Debug.LogWarning($"文件不存在: {excelRelativePath}");
            return;
        }

        try
        {
            using (FileStream fs = File.OpenRead(absolutePath))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var sheetMap = ExcelReader.LoadSheetNamesAndPaths(archive);
                foreach (var kv in sheetMap)
                {
                    availableSheetNames.Add(kv.Key);
                    sheetNameToPath[kv.Key] = kv.Value;
                }
                if (availableSheetNames.Count > 0 && !availableSheetNames.Contains(selectedSheetName))
                    selectedSheetName = availableSheetNames[0];
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"读取工作表失败: {e.Message}");
        }
    }

    // ──────────────────────────────────────────────
    //  名称匹配
    // ──────────────────────────────────────────────

    private void MatchExportNamesFromExcel()
    {
        if (string.IsNullOrEmpty(selectedMappingFile))
        {
            EditorUtility.DisplayDialog("提示", "请在高级映射设置中选择一个 Excel 文件。", "确定");
            return;
        }
        string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), selectedMappingFile);
        if (!File.Exists(absolutePath))
        {
            EditorUtility.DisplayDialog("错误", $"文件不存在: {selectedMappingFile}", "确定");
            return;
        }

        var mappings = ExcelReader.ReadAdvancedMappingsFromExcel(
            absolutePath, selectedMappingFile, selectedSheetName, sheetNameToPath,
            prefixColumn, mappingColumn, keyValueSeparator, joinSeparator,
            customLineSeparator, descriptionFirst);

        if (mappings.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "高级映射未生成任何有效映射规则。\n请检查列设置、分隔符以及工作表内容。", "确定");
            return;
        }

        int matchedCount = 0;
        foreach (var item in exportItems)
        {
            if (item.prefab == null) continue;
            if (mappings.TryGetValue(item.prefab.name.Trim(), out string exportName) && !string.IsNullOrEmpty(exportName))
            {
                item.exportName = exportName;
                matchedCount++;
            }
        }
        EditorUtility.DisplayDialog("匹配完成", $"匹配并更新导出名称: {matchedCount} 个", "确定");
        Repaint();
    }

    // ──────────────────────────────────────────────
    //  拖拽区域
    // ──────────────────────────────────────────────

    private void DrawDragDropArea()
    {
        Rect area = GUILayoutUtility.GetRect(0f, 60f, GUILayout.ExpandWidth(true));
        GUI.Box(area, "将Prefab资源拖拽至此区域以添加到列表\n（支持多选）", EditorStyles.helpBox);

        HandleDropEvents(area);
    }

    private void HandleDropEvents(Rect area)
    {
        Event e = Event.current;
        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && area.Contains(e.mousePosition))
        {
            bool valid = DragAndDrop.objectReferences.Any(obj => DependencyHelper.IsValidPrefab(obj));
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                    if (DependencyHelper.IsValidPrefab(obj))
                        AddPrefabToExportList(obj as GameObject);
                e.Use();
            }
        }
    }

    private void AddPrefabToExportList(GameObject prefab)
    {
        if (prefab == null) return;
        var item = new ExportItem { prefab = prefab };
        if (autoGenerateNames) item.exportName = prefab.name;
        RefreshItemDependencies(item);
        exportItems.Add(item);
        Repaint();
    }

    // ──────────────────────────────────────────────
    //  依赖刷新（桥接 DependencyHelper）
    // ──────────────────────────────────────────────

    private void RefreshItemDependencies(ExportItem item)
    {
        DependencyHelper.RefreshDependencies(item,
            includeScripts, includeAllDependencies,
            includeTextures, includeModels, includeMaterials,
            includeAnimations, includeOthers);
    }

    // ──────────────────────────────────────────────
    //  导出
    // ──────────────────────────────────────────────

    private void ExportUnityPackages()
    {
        if (exportItems.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "请至少添加一个Prefab", "确定");
            return;
        }
        if (string.IsNullOrEmpty(exportPath))
        {
            EditorUtility.DisplayDialog("错误", "请选择导出路径", "确定");
            return;
        }
        if (!Directory.Exists(exportPath))
            Directory.CreateDirectory(exportPath);

        int success = 0, fail = 0;
        foreach (var item in exportItems)
        {
            if (item.prefab == null || string.IsNullOrEmpty(item.exportName)) { fail++; continue; }

            var assets = item.dependencies.Where(d => d.Value.selected).Select(d => d.Key).ToList();
            if (assets.Count == 0) { fail++; continue; }

            string filePath = Path.Combine(exportPath, item.exportName + ".unitypackage");
            try
            {
                AssetDatabase.ExportPackage(assets.ToArray(), filePath, ExportPackageOptions.Interactive);
                success++;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                fail++;
            }
        }

        EditorUtility.DisplayDialog("导出完成", $"成功: {success} 个\n失败: {fail} 个", "确定");
        if (success > 0)
            System.Diagnostics.Process.Start(exportPath);
    }
}
