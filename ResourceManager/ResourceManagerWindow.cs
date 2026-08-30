using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Modules;
using ResourceManager.Utilities;
using System.Collections.Generic;
using ResourceManager;


public class ResourceManagerWindow : EditorWindow
{
    private AnalysisSession analysisSession;
    private ResourceCache cache;

    // 各种资源模块
    private OverviewModule overviewModule;
    private ParticleModule particleModule;
    private MaterialModule materialModule;
    private TextureModule textureModule;
    private MeshModule meshModule;
    private AnimationModule animationModule;
    private ExportModule exportModule;
    private CopyModule copyModule;
    private DeduplicateModule deduplicateModule;
    private ColorChangerModule colorChangerModule;
    private RedundancyModule redundancyModule;

    // 状态指示器
    private StatusIndicator statusIndicator;

    private int selectedTabIndex = 0;
    private Vector2 scrollPosition;

    // 全局搜索
    private string searchText = "";
    private string focusedSearchControl = "ResourceManagerSearchField";

    // 拓展选项卡下拉选择
    private int extensionDropdownIndex = 0;
    private static readonly string[] extensionModuleNames = { "改色", "动画", "复制", "去重", "冗余" };

    // 条件设置UI相关
    private Vector2 objectListScroll;
    
    // 新增：拖拽类型计数器
    private int sceneObjectDragCount = 0;
    private int prefabDragCount = 0;

    [MenuItem("Tools/TvTTools/资源管理器", false, 49)]
    public static void ShowWindow()
    {
        GetWindow<ResourceManagerWindow>("资源管理器");
    }

    private void OnEnable()
    {
        analysisSession = new AnalysisSession();
        cache = new ResourceCache();

        // 初始化所有资源模块
        overviewModule = new OverviewModule();
        particleModule = new ParticleModule();
        materialModule = new MaterialModule();
        textureModule = new TextureModule();
        meshModule = new MeshModule();
        animationModule = new AnimationModule();
        exportModule = new ExportModule();
        copyModule = new CopyModule();
        deduplicateModule = new DeduplicateModule();
        colorChangerModule = new ColorChangerModule();
        redundancyModule = new RedundancyModule();

        // 初始化状态指示器
        statusIndicator = new StatusIndicator();
    }

    private void OnGUI()
    {
        if (analysisSession == null)
        {
            OnEnable();
        }

        // 主体区域垂直布局
        EditorGUILayout.BeginVertical();

        // 主体区域水平布局
        EditorGUILayout.BeginHorizontal();

        // 左侧面板
        DrawLeftPanel();

        // 分割线
        GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

        // 右侧面板
        DrawRightPanel();

        EditorGUILayout.EndHorizontal();

        // 底部状态栏
        if (statusIndicator != null)
        {
            statusIndicator.DrawStatusBar(this);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(300));

        // 工具栏
        DrawToolbar();

        // 对象列表
        DrawObjectList();

        // 条件设置面板
        DrawConditionSettingsPanel();

        EditorGUILayout.EndVertical();
    }

    // 主标签名：改色/动画/复制/去重/冗余已移到拓展
    private static readonly string[] mainTabNames = { "概览", "粒子", "材质", "贴图", "网格", "导出" };

    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

        // 全局搜索栏（在标签栏上方）
        DrawSearchBar();

        // 标签栏：主标签(6) + 拓展按钮(2倍宽) + 下拉选择
        EditorGUILayout.BeginHorizontal();

        // 限制 selectedTabIndex 范围 0~5（5=拓展）
        if (selectedTabIndex > 6) selectedTabIndex = 6;

        // 手动绘制每个标签按钮，便于精确控制拓展按钮宽度
        for (int i = 0; i < mainTabNames.Length; i++)
        {
            bool isActive = selectedTabIndex == i;
            GUIStyle tabStyle = new GUIStyle(EditorStyles.toolbarButton);
            if (isActive)
            {
                tabStyle.normal.background = tabStyle.active.background;
                tabStyle.normal.textColor = tabStyle.active.textColor;
            }
            if (GUILayout.Button(mainTabNames[i], tabStyle))
            {
                selectedTabIndex = i;
            }
        }

        // 拓展标签组：「拓展」按钮 + 下拉选择，整体宽度 = 2倍普通标签
        bool isExtActive = selectedTabIndex == 6;
        GUIStyle extTabStyle = new GUIStyle(EditorStyles.toolbarButton);
        extTabStyle.fontSize = 12;
        extTabStyle.alignment = TextAnchor.MiddleCenter;
        if (isExtActive)
        {
            extTabStyle.normal.background = extTabStyle.active.background;
            extTabStyle.normal.textColor = extTabStyle.active.textColor;
        }

        // 拓展按钮（左半）
        if (GUILayout.Button("拓展", extTabStyle))
        {
            selectedTabIndex = 6;
        }

        // 拓展下拉选择（右半，嵌在拓展标签内部）
        int newDropdown = EditorGUILayout.Popup(extensionDropdownIndex, extensionModuleNames, EditorStyles.toolbarPopup, GUILayout.Width(50));
        if (newDropdown != extensionDropdownIndex)
        {
            extensionDropdownIndex = newDropdown;
            // 下拉变化时自动切到拓展
            if (selectedTabIndex != 6)
            {
                selectedTabIndex = 6;
            }
        }

        EditorGUILayout.EndHorizontal();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // 主选项卡内容（0~5）
        switch (selectedTabIndex)
        {
            case 0: // 概览
                overviewModule.SearchFilter = searchText;
                if (overviewModule is IMultiObjectModule multiOverview)
                    multiOverview.DrawMultiObject(analysisSession, cache);
                break;
            case 1: // 粒子
                particleModule.SearchFilter = searchText;
                if (particleModule is IMultiObjectModule multiParticle)
                    multiParticle.DrawMultiObject(analysisSession, cache);
                break;
            case 2: // 材质
                materialModule.SearchFilter = searchText;
                if (materialModule is IMultiObjectModule multiMaterial)
                    multiMaterial.DrawMultiObject(analysisSession, cache);
                break;
            case 3: // 贴图
                textureModule.SearchFilter = searchText;
                if (textureModule is IMultiObjectModule multiTexture)
                    multiTexture.DrawMultiObject(analysisSession, cache);
                break;
            case 4: // 网格
                meshModule.SearchFilter = searchText;
                if (meshModule is IMultiObjectModule multiMesh)
                    multiMesh.DrawMultiObject(analysisSession, cache);
                break;
            case 5: // 导出
                exportModule.SearchFilter = searchText;
                if (exportModule is IMultiObjectModule multiExport)
                    multiExport.DrawMultiObject(analysisSession, cache);
                break;
            case 6: // 拓展
                DrawExtensionModule();
                break;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 全局搜索栏：输入关键词过滤当前标签页的资源
    /// </summary>
    private void DrawSearchBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("搜索:", EditorStyles.miniLabel, GUILayout.Width(35));

        // Escape 键清除搜索
        if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape &&
            GUI.GetNameOfFocusedControl() == focusedSearchControl)
        {
            searchText = "";
            GUI.FocusControl(null);
            Repaint();
        }

        GUI.SetNextControlName(focusedSearchControl);
        searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarTextField);

        if (!string.IsNullOrEmpty(searchText))
        {
            if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                searchText = "";
                GUI.FocusControl(null);
                Repaint();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 根据下拉选择绘制拓展模块内容
    /// </summary>
    private void DrawExtensionModule()
    {
        // 拓展标签标题提示
        EditorGUILayout.HelpBox(
            $"当前拓展模块：{extensionModuleNames[extensionDropdownIndex]}  （可通过顶部下拉切换）",
            MessageType.Info);

        switch (extensionDropdownIndex)
        {
            case 0: // 改色
                colorChangerModule.SearchFilter = searchText;
                if (colorChangerModule is IMultiObjectModule multiColorChanger)
                    multiColorChanger.DrawMultiObject(analysisSession, cache);
                break;
            case 1: // 动画
                animationModule.SearchFilter = searchText;
                if (animationModule is IMultiObjectModule multiAnimation)
                    multiAnimation.DrawMultiObject(analysisSession, cache);
                break;
            case 2: // 复制
                copyModule.SearchFilter = searchText;
                copyModule.Draw(analysisSession);
                break;
            case 3: // 去重
                deduplicateModule.SearchFilter = searchText;
                deduplicateModule.DrawMultiObject(analysisSession, cache);
                break;
            case 4: // 冗余
                redundancyModule.SearchFilter = searchText;
                if (redundancyModule is IMultiObjectModule multiRedundancy)
                    multiRedundancy.DrawMultiObject(analysisSession, cache);
                break;
        }
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 清空分析
        if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            ClearAnalysis();
        }

        // 分析所有对象
        if (GUILayout.Button("分析所选对象", EditorStyles.toolbarButton))
        {
            cache.Clear();
            // 分析所有勾选的对象
            AnalyzeAllSelectedObjects();
            copyModule.Clear();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawObjectList()
    {
        GUILayout.Label("对象列表", EditorStyles.boldLabel);

        // 拖拽投放区（支持从Hierarchy拖拽场景对象和从Project拖拽预制体）
        Rect dropRect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, GetDragDropHintText(), EditorStyles.helpBox);
        HandleDragAndDrop(dropRect);

        // 对象滚动列表
        objectListScroll = EditorGUILayout.BeginScrollView(objectListScroll, GUILayout.ExpandHeight(true));

        // 合并Prefab和场景对象
        var allObjects = new List<AnalysisObject>();
        allObjects.AddRange(analysisSession.PrefabObjects);
        allObjects.AddRange(analysisSession.SceneObjects);

        // 显示对象
        for (int i = 0; i < allObjects.Count; i++)
        {
            var analysisObj = allObjects[i];
            EditorGUILayout.BeginHorizontal();

            // 是否选中
            analysisObj.IsSelected = EditorGUILayout.Toggle(analysisObj.IsSelected, GUILayout.Width(20));

            // 目标对象
            analysisObj.TargetObject = EditorGUILayout.ObjectField(
                analysisObj.TargetObject, typeof(GameObject), true) as GameObject;

            // 对象名和类型判断
            if (analysisObj.TargetObject != null)
            {
                analysisObj.ObjectName = analysisObj.TargetObject.name;

                // 判断是否Prefab
                if (PrefabUtility.IsPartOfPrefabAsset(analysisObj.TargetObject))
                {
                    analysisObj.Type = ObjectType.Prefab;
                }
                else
                {
                    analysisObj.Type = ObjectType.SceneObject;
                }
            }

            // 类型标签（使用不同颜色区分）
            string typeLabel = analysisObj.Type == ObjectType.Prefab ? "[预制体]" : "[场景]";
            Color originalColor = GUI.color;
            GUI.color = analysisObj.Type == ObjectType.Prefab ? new Color(0.2f, 0.6f, 1f) : new Color(0.8f, 0.4f, 0.2f);
            GUILayout.Label(typeLabel, GUILayout.Width(50));
            GUI.color = originalColor;

            // 移除按钮
            if (GUILayout.Button("删除", GUILayout.Width(60)))
            {
                if (analysisObj.Type == ObjectType.Prefab)
                {
                    analysisSession.PrefabObjects.Remove(analysisObj);
                }
                else
                {
                    analysisSession.SceneObjects.Remove(analysisObj);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        // 添加/清空按钮
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 添加对象"))
        {
            // 新增默认场景对象
            analysisSession.SceneObjects.Add(new AnalysisObject
            {
                Type = ObjectType.SceneObject,
                ObjectName = "新对象"
            });
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("- 清空列表"))
        {
            analysisSession.PrefabObjects.Clear();
            analysisSession.SceneObjects.Clear();
        }
        GUILayout.EndHorizontal();
    }

    private string GetDragDropHintText()
    {
        if (sceneObjectDragCount > 0 && prefabDragCount > 0)
        {
            return $"准备添加: {sceneObjectDragCount}个场景对象 + {prefabDragCount}个预制体 (松开鼠标以添加)";
        }
        else if (sceneObjectDragCount > 0)
        {
            return $"准备添加: {sceneObjectDragCount}个场景对象 (松开鼠标以添加)";
        }
        else if (prefabDragCount > 0)
        {
            return $"准备添加: {prefabDragCount}个预制体 (松开鼠标以添加)";
        }
        else
        {
            return "拖拽支持:\n1. 从Hierarchy拖入场景对象\n2. 从Project拖入预制体资源\n3. 支持多选批量拖拽";
        }
    }

    private void HandleDragAndDrop(Rect dropRect)
    {
        Event evt = Event.current;
        if (evt == null) return;

        // 只在鼠标位于投放区时响应
        if (!dropRect.Contains(evt.mousePosition)) 
        {
            if (evt.type == EventType.DragExited)
            {
                sceneObjectDragCount = 0;
                prefabDragCount = 0;
                Repaint();
            }
            return;
        }

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            sceneObjectDragCount = 0;
            prefabDragCount = 0;

            if (DragAndDrop.objectReferences != null)
            {
                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    var obj = DragAndDrop.objectReferences[i];
                    if (obj is GameObject go)
                    {
                        if (PrefabUtility.IsPartOfPrefabAsset(go))
                        {
                            prefabDragCount++;
                        }
                        else
                        {
                            sceneObjectDragCount++;
                        }
                    }
                }
            }

            if (sceneObjectDragCount > 0 || prefabDragCount > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        GameObject go = obj as GameObject;
                        if (go == null) continue;

                        // 判断是预制体还是场景对象
                        bool isPrefab = PrefabUtility.IsPartOfPrefabAsset(go);
                        var targetList = isPrefab ? analysisSession.PrefabObjects : analysisSession.SceneObjects;
                        var otherList = isPrefab ? analysisSession.SceneObjects : analysisSession.PrefabObjects;

                        // 检查是否已存在
                        bool exists = false;
                        for (int i = 0; i < targetList.Count; i++)
                        {
                            if (targetList[i].TargetObject == go) 
                            { 
                                exists = true; 
                                break; 
                            }
                        }
                        
                        // 检查另一个列表
                        if (!exists)
                        {
                            for (int i = 0; i < otherList.Count; i++)
                            {
                                if (otherList[i].TargetObject == go) 
                                { 
                                    exists = true; 
                                    break; 
                                }
                            }
                        }
                        
                        if (exists) continue;

                        // 添加对象
                        targetList.Add(new AnalysisObject
                        {
                            TargetObject = go,
                            ObjectName = go.name,
                            Type = isPrefab ? ObjectType.Prefab : ObjectType.SceneObject,
                            IsSelected = true
                        });
                    }

                    // 重置计数器
                    sceneObjectDragCount = 0;
                    prefabDragCount = 0;

                    GUI.changed = true;
                    Repaint();
                }
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }

            evt.Use();
        }
        
        if (evt.type == EventType.DragExited)
        {
            sceneObjectDragCount = 0;
            prefabDragCount = 0;
            Repaint();
        }
    }

    private void DrawConditionSettingsPanel()
    {
        EditorGUILayout.Space();

        // 条件设置按钮
        if (GUILayout.Button("设置判定条件", GUILayout.Height(30)))
        {
            ConditionSettingsWindow.ShowWindowFromButton();
        }
    }

    // 分析所有勾选对象
    private void AnalyzeAllSelectedObjects()
    {
        // 分析预制体
        foreach (var analysisObj in analysisSession.PrefabObjects)
        {
            if (analysisObj.IsSelected && analysisObj.TargetObject != null)
            {
                if (!analysisSession.Analyzers.ContainsKey(analysisObj.TargetObject))
                {
                    analysisSession.Analyzers[analysisObj.TargetObject] = new ResourceAnalyzer();
                }

                var analyzer = analysisSession.Analyzers[analysisObj.TargetObject];
                analyzer.SelectedPrefab = analysisObj.TargetObject;
                analyzer.AnalyzeResources();
            }
        }

        // 分析场景对象
        foreach (var analysisObj in analysisSession.SceneObjects)
        {
            if (analysisObj.IsSelected && analysisObj.TargetObject != null)
            {
                if (!analysisSession.Analyzers.ContainsKey(analysisObj.TargetObject))
                {
                    analysisSession.Analyzers[analysisObj.TargetObject] = new ResourceAnalyzer();
                }

                var analyzer = analysisSession.Analyzers[analysisObj.TargetObject];
                analyzer.DetectedObject = analysisObj.TargetObject;
                analyzer.AnalyzeDetectedObject();
            }
        }

        // 查找公共资源
        analysisSession.CommonResources.Clear();
        var resourceUsageMap = new Dictionary<Object, System.Collections.Generic.HashSet<string>>();

        foreach (var analyzer in analysisSession.Analyzers.Values)
        {
            foreach (var kvp in analyzer.ResourceUsage)
            {
                if (!resourceUsageMap.ContainsKey(kvp.Key))
                {
                    resourceUsageMap[kvp.Key] = new System.Collections.Generic.HashSet<string>();
                }
                resourceUsageMap[kvp.Key].Add(kvp.Value[0].Split('/')[0]); // 记录资源来源
            }
        }

        // 至少被2个对象共用的，加入公共资源
        foreach (var kvp in resourceUsageMap)
        {
            if (kvp.Value.Count >= 2)
            {
                analysisSession.CommonResources[kvp.Key] = new System.Collections.Generic.List<string>(kvp.Value);
            }
        }

        // 更新状态指示器
        if (statusIndicator != null)
        {
            statusIndicator.UpdateStatus(analysisSession);
        }

        Repaint();
    }

    // 清空所有分析数据
    private void ClearAnalysis()
    {
        analysisSession.Clear();
        cache.Clear();
        selectedTabIndex = 0;
        scrollPosition = Vector2.zero;

        overviewModule?.Clear();
        particleModule?.Clear();
        materialModule?.Clear();
        textureModule?.Clear();
        meshModule?.Clear();
        animationModule?.Clear();
        exportModule?.Clear();
        copyModule?.Clear();
        deduplicateModule?.Clear();
        colorChangerModule?.Clear();
        redundancyModule?.Clear();

        // 清空状态指示器
        statusIndicator?.ClearStatus();

        Repaint();
        Debug.Log("分析结果已清空");
    }
}