using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

public class GlobalResourceCheckerWindow : EditorWindow
{
    // 资源检测设置
    [System.Serializable]
    public class CheckSettings
    {
        public bool checkNameSpace = true;
        public Color nameSpaceColor = Color.red;

        public bool checkNameBrackets = true;
        public Color nameBracketsColor = new Color(1f, 0.5f, 0f); // 橙色

        public bool checkNameUppercase = true;
        public Color nameUppercaseColor = Color.red;

        public bool checkNameLength = true;
        public int nameLengthThreshold = 20;
        public Color nameLengthColor = Color.yellow;

        public bool checkFileNameSpace = true;
        public Color fileNameSpaceColor = Color.red;

        public bool checkFileBrackets = true;
        public Color fileBracketsColor = new Color(1f, 0.5f, 0f);

        // 新增规则：中文检测
        public bool checkNameChinese = true;
        public Color nameChineseColor = Color.cyan;

        // 新增规则：连字符检测
        public bool checkNameHyphen = true;
        public Color nameHyphenColor = Color.magenta;

        // 保存和加载设置
        public void SaveSettings()
        {
            string json = JsonUtility.ToJson(this);
            EditorPrefs.SetString("GlobalResourceChecker_Settings", json);
        }

        public void LoadSettings()
        {
            if (EditorPrefs.HasKey("GlobalResourceChecker_Settings"))
            {
                string json = EditorPrefs.GetString("GlobalResourceChecker_Settings");
                JsonUtility.FromJsonOverwrite(json, this);
            }
        }
    }

    // UI状态
    private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
    private Dictionary<string, bool> typeFoldouts = new Dictionary<string, bool>();
    private Dictionary<string, bool> folderFoldouts = new Dictionary<string, bool>();
    private Dictionary<Object, bool> selectedResources = new Dictionary<Object, bool>();
    private Dictionary<string, bool> categoryAllSelected = new Dictionary<string, bool>();
    private Dictionary<Object, string> resourceRenameMap = new Dictionary<Object, string>();

    // 问题统计
    private Dictionary<string, int> typeProblemCounts = new Dictionary<string, int>();
    private Dictionary<string, int> folderProblemCounts = new Dictionary<string, int>();

    // 检测设置
    private CheckSettings settings = new CheckSettings();
    private bool showSettings = false;
    private Vector2 settingsScroll;

    // 资源数据
    private List<Object> allResources = new List<Object>();
    private Dictionary<System.Type, List<Object>> resourcesByType = new Dictionary<System.Type, List<Object>>();
    private bool hasScanned = false;

    // 批量重命名
    private enum RenameMode { 加前缀, 加后缀, 查找替换, 自定义增量 }
    private RenameMode currentRenameMode = RenameMode.加前缀;
    private string prefix = "";
    private string suffix = "";
    private string findStr = "";
    private string replaceStr = "";
    private string customPrefix = "";
    private int startNumber = 1;
    private int step = 1;
    private int padding = 0;

    // 移动资源相关
    private string targetFolderPath = "Assets/整理资源";
    private Vector2 mainScrollPosition;

    [MenuItem("Tools/TvTTools/全局资源检查器")]
    public static void ShowWindow()
    {
        GetWindow<GlobalResourceCheckerWindow>("全局资源检查器");
    }

    private void OnEnable()
    {
        settings.LoadSettings();
    }

    private void OnGUI()
    {
        DrawMainWindow();
    }

    private void DrawMainWindow()
    {
        EditorGUILayout.BeginVertical();

        GUILayout.Label("全局资源检查器", EditorStyles.boldLabel);

        // 设置面板
        DrawSettingsPanel();

        // 扫描按钮
        DrawScanButton();

        if (!hasScanned)
        {
            EditorGUILayout.HelpBox("请先扫描项目资源", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        if (allResources.Count == 0)
        {
            EditorGUILayout.HelpBox("未找到资源", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // 批量重命名面板
        DrawBulkRenamePanel();

        // 全局操作按钮
        DrawGlobalSelectionButtons();

        // 资源列表
        mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);
        DrawResourceList();
        EditorGUILayout.EndScrollView();

        // 操作按钮
        DrawActionButtons();

        EditorGUILayout.EndVertical();
    }

    private void DrawSettingsPanel()
    {
        showSettings = EditorGUILayout.Foldout(showSettings, "检测规则设置", true);
        if (showSettings)
        {
            EditorGUILayout.BeginVertical("Box");
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll, GUILayout.Height(200));

            // === 名称检测设置 ===
            EditorGUILayout.LabelField("资源名称检测", EditorStyles.boldLabel);

            // 检查名称空格
            EditorGUILayout.BeginHorizontal();
            settings.checkNameSpace = EditorGUILayout.ToggleLeft("检查名称空格", settings.checkNameSpace, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameSpaceColor = EditorGUILayout.ColorField("", settings.nameSpaceColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查名称括号
            EditorGUILayout.BeginHorizontal();
            settings.checkNameBrackets = EditorGUILayout.ToggleLeft("检查名称括号", settings.checkNameBrackets, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameBracketsColor = EditorGUILayout.ColorField("", settings.nameBracketsColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查名称大写字母
            EditorGUILayout.BeginHorizontal();
            settings.checkNameUppercase = EditorGUILayout.ToggleLeft("检查名称大写字母", settings.checkNameUppercase, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameUppercaseColor = EditorGUILayout.ColorField("", settings.nameUppercaseColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查名称长度
            EditorGUILayout.BeginHorizontal();
            settings.checkNameLength = EditorGUILayout.ToggleLeft("检查名称长度", settings.checkNameLength, GUILayout.Width(120));
            GUILayout.Label("阈值:", GUILayout.Width(40));
            settings.nameLengthThreshold = EditorGUILayout.IntField(settings.nameLengthThreshold, GUILayout.Width(60));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameLengthColor = EditorGUILayout.ColorField("", settings.nameLengthColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查名称中文
            EditorGUILayout.BeginHorizontal();
            settings.checkNameChinese = EditorGUILayout.ToggleLeft("检查名称中文", settings.checkNameChinese, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameChineseColor = EditorGUILayout.ColorField("", settings.nameChineseColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查名称连字符
            EditorGUILayout.BeginHorizontal();
            settings.checkNameHyphen = EditorGUILayout.ToggleLeft("检查名称连字符(-)", settings.checkNameHyphen, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.nameHyphenColor = EditorGUILayout.ColorField("", settings.nameHyphenColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // === 文件名检测设置 ===
            EditorGUILayout.LabelField("文件名称检测", EditorStyles.boldLabel);

            // 检查文件名空格
            EditorGUILayout.BeginHorizontal();
            settings.checkFileNameSpace = EditorGUILayout.ToggleLeft("检查文件名空格", settings.checkFileNameSpace, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.fileNameSpaceColor = EditorGUILayout.ColorField("", settings.fileNameSpaceColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 检查文件名括号
            EditorGUILayout.BeginHorizontal();
            settings.checkFileBrackets = EditorGUILayout.ToggleLeft("检查文件名括号", settings.checkFileBrackets, GUILayout.Width(120));
            GUILayout.Label("颜色:", GUILayout.Width(40));
            settings.fileBracketsColor = EditorGUILayout.ColorField("", settings.fileBracketsColor, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // 保存按钮
            if (GUILayout.Button("保存设置", GUILayout.Height(25)))
            {
                settings.SaveSettings();
                Debug.Log("全局资源检查设置已保存");
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
    }

    private void DrawScanButton()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("扫描项目资源", GUILayout.Height(30)))
        {
            ScanProjectResources();
        }

        if (GUILayout.Button("清除扫描结果", GUILayout.Height(30)))
        {
            ClearScanResults();
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
    }

    private void DrawBulkRenamePanel()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("批量重命名", EditorStyles.boldLabel);

        currentRenameMode = (RenameMode)EditorGUILayout.EnumPopup("重命名模式", currentRenameMode);

        switch (currentRenameMode)
        {
            case RenameMode.加前缀:
                prefix = EditorGUILayout.TextField("前缀:", prefix);
                break;
            case RenameMode.加后缀:
                suffix = EditorGUILayout.TextField("后缀:", suffix);
                break;
            case RenameMode.查找替换:
                findStr = EditorGUILayout.TextField("查找:", findStr);
                replaceStr = EditorGUILayout.TextField("替换:", replaceStr);
                break;
            case RenameMode.自定义增量:
                customPrefix = EditorGUILayout.TextField("前缀:", customPrefix);
                startNumber = EditorGUILayout.IntField("起始数字:", startNumber);
                step = EditorGUILayout.IntField("数字增量:", step);
                padding = EditorGUILayout.IntField("数字位数:", padding);
                break;
        }

        if (GUILayout.Button("应用重命名到选中资源"))
        {
            ApplyBulkRename();
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(10);
    }

    private void DrawGlobalSelectionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("全局全选", GUILayout.Height(25)))
        {
            SelectAllResources();
        }

        if (GUILayout.Button("清除选择", GUILayout.Height(25)))
        {
            ClearSelection();
        }

        // 仅选择有问题的资源
        if (GUILayout.Button("选择问题资源", GUILayout.Height(25)))
        {
            SelectProblemResources();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);
    }

    private void DrawResourceList()
    {
        // 按指定顺序的资源类型分组
        var orderedTypes = GetOrderedResourceTypes();

        foreach (var type in orderedTypes)
        {
            string typeName = GetResourceTypeDisplayName(type);
            string categoryKey = typeName;

            if (!typeFoldouts.ContainsKey(categoryKey))
            {
                typeFoldouts[categoryKey] = false;
            }

            // 计算该类型的问题数量
            int typeProblemCount = GetTypeProblemCount(type);
            string typeDisplayName = $"{typeName} ({resourcesByType[type].Count}个)";

            // 如果有问题资源，在名称后显示问题数量
            if (typeProblemCount > 0)
            {
                typeDisplayName += $" <color=#{ColorUtility.ToHtmlStringRGB(GetProblemColorForType(type))}>[{typeProblemCount}]</color>";
            }

            EditorGUILayout.BeginVertical("box");

            // 类型标题和全选按钮
            EditorGUILayout.BeginHorizontal();

            // 使用富文本显示问题数量
            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout);
            foldoutStyle.richText = true;
            typeFoldouts[categoryKey] = EditorGUILayout.Foldout(typeFoldouts[categoryKey], typeDisplayName, foldoutStyle);

            GUILayout.FlexibleSpace();

            // 类型全选按钮
            if (!categoryAllSelected.ContainsKey(categoryKey))
            {
                categoryAllSelected[categoryKey] = false;
            }

            bool newValue = EditorGUILayout.Toggle(categoryAllSelected[categoryKey], GUILayout.Width(20));
            if (newValue != categoryAllSelected[categoryKey])
            {
                categoryAllSelected[categoryKey] = newValue;
                SetCategorySelection(categoryKey, resourcesByType[type], categoryAllSelected[categoryKey]);
            }

            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();

            if (typeFoldouts[categoryKey])
            {
                // 按文件夹路径分组
                var resourcesByFolder = resourcesByType[type]
                    .GroupBy(r => Path.GetDirectoryName(AssetDatabase.GetAssetPath(r)))
                    .OrderBy(g => g.Key);

                foreach (var folderGroup in resourcesByFolder)
                {
                    string folderPath = folderGroup.Key ?? "未知路径";
                    string folderKey = $"{categoryKey}_{folderPath}";

                    if (!folderFoldouts.ContainsKey(folderKey))
                    {
                        folderFoldouts[folderKey] = false;
                    }

                    // 计算该文件夹的问题数量
                    int folderProblemCount = GetFolderProblemCount(folderGroup.ToList());
                    string folderDisplayName = $"{folderPath} ({folderGroup.Count()}个)";

                    // 如果有问题资源，在名称后显示问题数量
                    if (folderProblemCount > 0)
                    {
                        folderDisplayName += $" <color=#{ColorUtility.ToHtmlStringRGB(GetProblemColorForFolder(folderGroup.ToList()))}>[{folderProblemCount}]</color>";
                    }

                    EditorGUILayout.BeginVertical("helpbox");
                    EditorGUI.indentLevel++;

                    // 文件夹标题和全选按钮
                    EditorGUILayout.BeginHorizontal();

                    // 使用富文本显示问题数量
                    folderFoldouts[folderKey] = EditorGUILayout.Foldout(folderFoldouts[folderKey], folderDisplayName, foldoutStyle);

                    GUILayout.FlexibleSpace();

                    // 文件夹全选按钮
                    bool allInFolderSelected = folderGroup.All(r => selectedResources.ContainsKey(r) && selectedResources[r]);
                    bool newFolderValue = EditorGUILayout.Toggle(allInFolderSelected, GUILayout.Width(20));
                    if (newFolderValue != allInFolderSelected)
                    {
                        foreach (var resource in folderGroup)
                        {
                            selectedResources[resource] = newFolderValue;
                        }
                    }

                    GUILayout.Space(5);
                    EditorGUILayout.EndHorizontal();

                    if (folderFoldouts[folderKey])
                    {
                        // 绘制文件夹内的资源
                        foreach (var resource in folderGroup)
                        {
                            DrawResourceItem(resource);
                        }
                    }

                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
    }

    private void DrawResourceItem(Object resource)
    {
        EditorGUILayout.BeginHorizontal();

        // 选择状态
        if (!selectedResources.ContainsKey(resource))
        {
            selectedResources[resource] = false;
        }

        // 选择/取消选择按钮
        string buttonText = selectedResources[resource] ? "取消选择" : "选择";
        if (GUILayout.Button(buttonText, GUILayout.Width(60)))
        {
            selectedResources[resource] = !selectedResources[resource];
            GUI.changed = true;
        }

        // 检测结果颜色指示
        Color? problemColor = CheckResourceProblems(resource);
        Color originalColor = GUI.color;
        if (problemColor.HasValue)
        {
            GUI.color = problemColor.Value;
        }

        // 资源名称（可编辑）
        if (!resourceRenameMap.ContainsKey(resource))
        {
            resourceRenameMap[resource] = resource.name;
        }

        // 名称文本框
        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField(
            resourceRenameMap[resource],
            GUILayout.Width(150)
        );

        if (EditorGUI.EndChangeCheck())
        {
            resourceRenameMap[resource] = newName;
        }

        GUI.color = originalColor;

        // 资源类型标签
        EditorGUILayout.LabelField(
            GetResourceTypeDisplayName(resource.GetType()),
            GUILayout.Width(80)
        );

        // 问题描述 - 增加宽度
        string problemDescription = GetProblemDescription(resource);
        if (!string.IsNullOrEmpty(problemDescription))
        {
            EditorGUILayout.LabelField(problemDescription, EditorStyles.miniLabel, GUILayout.Width(200));
        }

        // 弹性空间，将资源框推到最右边
        GUILayout.FlexibleSpace();

        // 对象字段（资源框）- 放在最右边
        EditorGUILayout.ObjectField("", resource, resource.GetType(), false, GUILayout.Width(200));

        EditorGUILayout.EndHorizontal();

        // 分隔线
        if (Event.current.type == EventType.Repaint)
        {
            Rect rect = GUILayoutUtility.GetLastRect();
            rect.y += rect.height + 1;
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }
    }

    private void DrawActionButtons()
    {
        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        // 目标文件夹设置
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("移动目标设置", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("目标路径:", GUILayout.Width(60));
        targetFolderPath = EditorGUILayout.TextField(targetFolderPath);
        if (GUILayout.Button("浏览", GUILayout.Width(50)))
        {
            string path = EditorUtility.SaveFolderPanel("选择目标文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
            {
                targetFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("移动选中资源", GUILayout.Height(30)))
        {
            MoveSelectedResources();
        }

        if (GUILayout.Button("应用重命名", GUILayout.Height(30)))
        {
            ApplyRenaming();
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 扫描项目资源（排除脚本和Packages文件夹）
    /// </summary>
    private void ScanProjectResources()
    {
        allResources.Clear();
        resourcesByType.Clear();
        selectedResources.Clear();
        resourceRenameMap.Clear();
        typeProblemCounts.Clear();
        folderProblemCounts.Clear();

        // 获取所有资源GUID
        string[] allAssetGuids = AssetDatabase.FindAssets("");

        EditorUtility.DisplayProgressBar("扫描资源", "正在扫描项目资源...", 0);

        try
        {
            for (int i = 0; i < allAssetGuids.Length; i++)
            {
                string guid = allAssetGuids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // 跳过文件夹、脚本文件和Packages文件夹
                if (AssetDatabase.IsValidFolder(assetPath) ||
                    assetPath.EndsWith(".cs") ||
                    assetPath.EndsWith(".js") ||
                    assetPath.EndsWith(".dll") ||
                    assetPath.StartsWith("Packages/"))
                {
                    continue;
                }

                // 加载资源
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null && !(asset is MonoScript))
                {
                    allResources.Add(asset);

                    System.Type assetType = asset.GetType();
                    if (!resourcesByType.ContainsKey(assetType))
                    {
                        resourcesByType[assetType] = new List<Object>();
                    }
                    resourcesByType[assetType].Add(asset);

                    // 初始化重命名映射
                    resourceRenameMap[asset] = asset.name;
                }

                // 更新进度条
                if (i % 100 == 0)
                {
                    EditorUtility.DisplayProgressBar("扫描资源", $"已扫描 {i}/{allAssetGuids.Length} 个资源", (float)i / allAssetGuids.Length);
                }
            }

            // 计算问题统计
            CalculateProblemStatistics();

            hasScanned = true;
            Debug.Log($"扫描完成，找到 {allResources.Count} 个资源");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// 计算问题统计
    /// </summary>
    private void CalculateProblemStatistics()
    {
        typeProblemCounts.Clear();
        folderProblemCounts.Clear();

        // 计算类型问题统计
        foreach (var kvp in resourcesByType)
        {
            string typeName = GetResourceTypeDisplayName(kvp.Key);
            int problemCount = kvp.Value.Count(r => CheckResourceProblems(r).HasValue);
            typeProblemCounts[typeName] = problemCount;
        }

        // 计算文件夹问题统计
        foreach (var resource in allResources)
        {
            string folderPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(resource)) ?? "未知路径";
            if (!folderProblemCounts.ContainsKey(folderPath))
            {
                folderProblemCounts[folderPath] = 0;
            }

            if (CheckResourceProblems(resource).HasValue)
            {
                folderProblemCounts[folderPath]++;
            }
        }
    }

    /// <summary>
    /// 获取指定顺序的资源类型
    /// </summary>
    private List<System.Type> GetOrderedResourceTypes()
    {
        // 定义优先级顺序
        var priorityOrder = new Dictionary<string, int>
        {
            { "贴图", 1 },
            { "模型", 2 },
            { "材质", 3 },
            { "预制体", 4 },
            { "动画", 5 },
            { "着色器", 6 }
        };

        return resourcesByType.Keys
            .OrderBy(t =>
            {
                string typeName = GetResourceTypeDisplayName(t);
                if (priorityOrder.ContainsKey(typeName))
                    return priorityOrder[typeName];

                // 中文类型排在后面
                if (ContainsChinese(typeName))
                    return 100;

                // 其他类型排在最后
                return 1000;
            })
            .ThenBy(t => GetResourceTypeDisplayName(t))
            .ToList();
    }

    /// <summary>
    /// 获取类型的问题数量
    /// </summary>
    private int GetTypeProblemCount(System.Type type)
    {
        string typeName = GetResourceTypeDisplayName(type);
        return typeProblemCounts.ContainsKey(typeName) ? typeProblemCounts[typeName] : 0;
    }

    /// <summary>
    /// 获取文件夹的问题数量
    /// </summary>
    private int GetFolderProblemCount(List<Object> resources)
    {
        int count = 0;
        foreach (var resource in resources)
        {
            if (CheckResourceProblems(resource).HasValue)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 获取类型的代表性问题颜色
    /// </summary>
    private Color GetProblemColorForType(System.Type type)
    {
        var resources = resourcesByType[type];
        foreach (var resource in resources)
        {
            var color = CheckResourceProblems(resource);
            if (color.HasValue)
                return color.Value;
        }
        return Color.red; // 默认颜色
    }

    /// <summary>
    /// 获取文件夹的代表性问题颜色
    /// </summary>
    private Color GetProblemColorForFolder(List<Object> resources)
    {
        foreach (var resource in resources)
        {
            var color = CheckResourceProblems(resource);
            if (color.HasValue)
                return color.Value;
        }
        return Color.red; // 默认颜色
    }

    /// <summary>
    /// 检查资源问题并返回对应的颜色
    /// </summary>
    private Color? CheckResourceProblems(Object resource)
    {
        if (resource == null) return null;

        string resourceName = resource.name;
        string assetPath = AssetDatabase.GetAssetPath(resource);
        if (string.IsNullOrEmpty(assetPath)) return null;

        string fileName = Path.GetFileNameWithoutExtension(assetPath);

        // 检查资源名称问题
        if (settings.checkNameSpace && resourceName.Contains(" "))
        {
            return settings.nameSpaceColor;
        }

        if (settings.checkNameBrackets && (resourceName.Contains("(") || resourceName.Contains(")") || resourceName.Contains("（") || resourceName.Contains("）")))
        {
            return settings.nameBracketsColor;
        }

        if (settings.checkNameUppercase && ContainsUppercase(resourceName))
        {
            return settings.nameUppercaseColor;
        }

        if (settings.checkNameLength && resourceName.Length > settings.nameLengthThreshold)
        {
            return settings.nameLengthColor;
        }

        if (settings.checkNameChinese && ContainsChinese(resourceName))
        {
            return settings.nameChineseColor;
        }

        if (settings.checkNameHyphen && resourceName.Contains("-"))
        {
            return settings.nameHyphenColor;
        }

        // 检查文件名问题（可能与资源名不同）
        if (settings.checkFileNameSpace && fileName.Contains(" "))
        {
            return settings.fileNameSpaceColor;
        }

        if (settings.checkFileBrackets && (fileName.Contains("(") || fileName.Contains(")") || fileName.Contains("（") || fileName.Contains("）")))
        {
            return settings.fileBracketsColor;
        }

        return null;
    }

    /// <summary>
    /// 获取问题描述
    /// </summary>
    private string GetProblemDescription(Object resource)
    {
        if (resource == null) return "";

        string resourceName = resource.name;
        string assetPath = AssetDatabase.GetAssetPath(resource);
        if (string.IsNullOrEmpty(assetPath)) return "";

        string fileName = Path.GetFileNameWithoutExtension(assetPath);

        List<string> problems = new List<string>();

        if (settings.checkNameSpace && resourceName.Contains(" "))
            problems.Add("名称空格");

        if (settings.checkNameBrackets && (resourceName.Contains("(") || resourceName.Contains(")") || resourceName.Contains("（") || resourceName.Contains("）")))
            problems.Add("名称括号");

        if (settings.checkNameUppercase && ContainsUppercase(resourceName))
            problems.Add("名称大写");

        if (settings.checkNameLength && resourceName.Length > settings.nameLengthThreshold)
            problems.Add("名称过长");

        if (settings.checkNameChinese && ContainsChinese(resourceName))
            problems.Add("名称中文");

        if (settings.checkNameHyphen && resourceName.Contains("-"))
            problems.Add("名称连字符");

        if (settings.checkFileNameSpace && fileName.Contains(" "))
            problems.Add("文件名空格");

        if (settings.checkFileBrackets && (fileName.Contains("(") || fileName.Contains(")") || fileName.Contains("（") || fileName.Contains("）")))
            problems.Add("文件名括号");

        return problems.Count > 0 ? string.Join(", ", problems) : "";
    }

    /// <summary>
    /// 检查字符串是否包含大写字母
    /// </summary>
    private bool ContainsUppercase(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;

        foreach (char c in str)
        {
            if (char.IsUpper(c)) return true;
        }
        return false;
    }

    /// <summary>
    /// 检查字符串是否包含中文
    /// </summary>
    private bool ContainsChinese(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;

        // 使用正则表达式检查中文字符
        return Regex.IsMatch(str, @"[\u4e00-\u9fa5]");
    }

    /// <summary>
    /// 应用批量重命名
    /// </summary>
    private void ApplyBulkRename()
    {
        var selected = selectedResources.Where(kvp => kvp.Value && kvp.Key != null).Select(kvp => kvp.Key).ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选择要重命名的资源", "确定");
            return;
        }

        int currentNumber = startNumber;

        foreach (var resource in selected)
        {
            if (resource == null) continue;

            string newName = resource.name;

            switch (currentRenameMode)
            {
                case RenameMode.加前缀:
                    newName = prefix + newName;
                    break;
                case RenameMode.加后缀:
                    newName = newName + suffix;
                    break;
                case RenameMode.查找替换:
                    newName = newName.Replace(findStr, replaceStr);
                    break;
                case RenameMode.自定义增量:
                    string paddedNumber = currentNumber.ToString().PadLeft(padding, '0');
                    newName = $"{customPrefix}_{paddedNumber}";
                    currentNumber += step;
                    break;
            }

            // 更新重命名映射
            resourceRenameMap[resource] = newName;
        }

        Debug.Log($"已为 {selected.Count} 个资源应用批量重命名规则");
    }

    /// <summary>
    /// 应用重命名
    /// </summary>
    private void ApplyRenaming()
    {
        var resourcesToRename = resourceRenameMap
            .Where(kvp => kvp.Key != null && kvp.Value != kvp.Key.name && selectedResources.ContainsKey(kvp.Key) && selectedResources[kvp.Key])
            .ToList();

        if (resourcesToRename.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有需要重命名的资源", "确定");
            return;
        }

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (var kvp in resourcesToRename)
            {
                var resource = kvp.Key;
                string newName = kvp.Value;

                string path = AssetDatabase.GetAssetPath(resource);
                if (!string.IsNullOrEmpty(path))
                {
                    string error = AssetDatabase.RenameAsset(path, newName);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogError($"重命名资源失败: {resource.name} -> {newName}, 错误: {error}");
                    }
                    else
                    {
                        Debug.Log($"成功重命名资源: {resource.name} -> {newName}");
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();
        Debug.Log($"成功重命名 {resourcesToRename.Count} 个资源");
    }

    /// <summary>
    /// 移动选中资源
    /// </summary>
    private void MoveSelectedResources()
    {
        var selected = selectedResources.Where(kvp => kvp.Value && kvp.Key != null).Select(kvp => kvp.Key).ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选择要移动的资源", "确定");
            return;
        }

        // 确保目标文件夹存在
        if (!AssetDatabase.IsValidFolder(targetFolderPath))
        {
            string parentFolder = Path.GetDirectoryName(targetFolderPath);
            string folderName = Path.GetFileName(targetFolderPath);
            if (string.IsNullOrEmpty(parentFolder)) parentFolder = "Assets";
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (var resource in selected)
            {
                if (resource == null) continue;

                string originalPath = AssetDatabase.GetAssetPath(resource);
                if (!string.IsNullOrEmpty(originalPath))
                {
                    // 使用重命名后的名称（如果存在）
                    string fileName = resourceRenameMap.ContainsKey(resource) &&
                                     resourceRenameMap[resource] != resource.name ?
                                     resourceRenameMap[resource] :
                                     Path.GetFileName(originalPath);

                    // 保持文件扩展名
                    string extension = Path.GetExtension(originalPath);
                    if (!string.IsNullOrEmpty(extension) && !fileName.EndsWith(extension))
                    {
                        fileName += extension;
                    }

                    string newPath = Path.Combine(targetFolderPath, fileName).Replace("\\", "/");

                    if (originalPath != newPath)
                    {
                        string error = AssetDatabase.MoveAsset(originalPath, newPath);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Debug.LogError($"移动资源失败: {resource.name} -> {error}");
                        }
                        else
                        {
                            Debug.Log($"成功移动资源: {resource.name} -> {newPath}");
                        }
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();
        Debug.Log($"成功移动 {selected.Count} 个资源到 {targetFolderPath}");
    }

    /// <summary>
    /// 设置分类选择状态
    /// </summary>
    private void SetCategorySelection(string categoryKey, List<Object> resources, bool selected)
    {
        foreach (var resource in resources)
        {
            if (resource != null)
            {
                selectedResources[resource] = selected;
            }
        }
    }

    /// <summary>
    /// 全局全选
    /// </summary>
    private void SelectAllResources()
    {
        foreach (var resource in selectedResources.Keys.ToList())
        {
            if (resource != null)
            {
                selectedResources[resource] = true;
            }
        }

        // 同时更新所有分类的全选状态
        foreach (var categoryKey in categoryAllSelected.Keys.ToList())
        {
            categoryAllSelected[categoryKey] = true;
        }
    }

    /// <summary>
    /// 清除选择
    /// </summary>
    private void ClearSelection()
    {
        foreach (var resource in selectedResources.Keys.ToList())
        {
            if (resource != null)
            {
                selectedResources[resource] = false;
            }
        }

        // 同时更新所有分类的全选状态
        foreach (var categoryKey in categoryAllSelected.Keys.ToList())
        {
            categoryAllSelected[categoryKey] = false;
        }
    }

    /// <summary>
    /// 选择有问题的资源
    /// </summary>
    private void SelectProblemResources()
    {
        foreach (var resource in allResources)
        {
            if (resource != null)
            {
                selectedResources[resource] = CheckResourceProblems(resource).HasValue;
            }
        }

        // 更新分类全选状态
        foreach (var categoryKey in categoryAllSelected.Keys.ToList())
        {
            var resourcesInCategory = resourcesByType.Values
                .SelectMany(list => list)
                .Where(r => r != null && GetResourceTypeDisplayName(r.GetType()) == categoryKey)
                .ToList();

            categoryAllSelected[categoryKey] = resourcesInCategory.All(r =>
                selectedResources.ContainsKey(r) && selectedResources[r]);
        }
    }

    /// <summary>
    /// 清除扫描结果
    /// </summary>
    private void ClearScanResults()
    {
        allResources.Clear();
        resourcesByType.Clear();
        selectedResources.Clear();
        resourceRenameMap.Clear();
        typeProblemCounts.Clear();
        folderProblemCounts.Clear();
        foldouts.Clear();
        typeFoldouts.Clear();
        folderFoldouts.Clear();
        categoryAllSelected.Clear();
        hasScanned = false;

        Debug.Log("扫描结果已清除");
    }

    /// <summary>
    /// 获取资源类型显示名称
    /// </summary>
    private string GetResourceTypeDisplayName(System.Type type)
    {
        if (type == typeof(Texture2D)) return "贴图";
        if (type == typeof(Material)) return "材质";
        if (type == typeof(Mesh)) return "模型";
        if (type == typeof(AnimationClip)) return "动画";
        if (type == typeof(GameObject)) return "预制体";
        if (type == typeof(AudioClip)) return "音频";
        if (type == typeof(Font)) return "字体";
        if (type == typeof(Shader)) return "着色器";
        if (type == typeof(Sprite)) return "精灵";
        if (type == typeof(TextAsset)) return "文本";
        return type.Name;
    }
}