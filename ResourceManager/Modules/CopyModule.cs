using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.IO;

namespace ResourceManager.Modules
{
    public class CopyModule
    {
        // 粒子系统相关类
        private class ParticleNode
        {
            public GameObject OriginalGameObject;
            public ParticleSystem ParticleSystem;
            public string OriginalName;
            public bool IsSelected;
            public List<ParticleNode> Children = new List<ParticleNode>();
        }

        // 粒子系统成员变量
        private Dictionary<string, ParticleNode> rootNodes = new Dictionary<string, ParticleNode>();
        private string searchText = "";
        private string replaceText = "";
        private string materialCopyPath = "Assets/CopiedMaterials";
        private Vector2 scrollPosition;
        private Dictionary<GameObject, bool> foldouts = new Dictionary<GameObject, bool>();

        // 资源复制成员变量
        private Dictionary<Object, bool> selectedResources = new Dictionary<Object, bool>();
        private Dictionary<Object, string> resourceRenameMap = new Dictionary<Object, string>();
        private string resourceCopyPath = "Assets/CopiedResources";
        private Vector2 resourceScrollPosition;

        // 折叠状态字典
        private Dictionary<System.Type, bool> _typeFoldouts = new Dictionary<System.Type, bool>();
        private Dictionary<string, bool> _folderFoldouts = new Dictionary<string, bool>();

        // 全选状态字典
        private Dictionary<string, bool> _categoryAllSelected = new Dictionary<string, bool>();
        private Dictionary<string, bool> _folderAllSelected = new Dictionary<string, bool>();

        // 全局搜索过滤
        public string SearchFilter = "";

        // 标签页控制
        private int selectedTabIndex = 0;
        private readonly string[] tabNames = { "粒子系统复制", "资源复制" };

        // 命名方式相关成员变量
        private enum NamingMethod
        {
            FindReplace,    // 查找替换
            PrefixSerial,   // 前缀+序号
            KeepOriginal    // 保持原名称 - 新增选项
        }

        private NamingMethod _namingMethod = NamingMethod.FindReplace;
        private string _prefix = "NewResource";
        private int _startIndex = 1;
        private int _digitCount = 3;

        // 重新指认选项
        private bool _reassignToObjects = true;

        public void Draw(AnalysisSession session)
        {
            selectedTabIndex = GUILayout.Toolbar(selectedTabIndex, tabNames);

            switch (selectedTabIndex)
            {
                case 0: // 粒子系统复制
                    DrawParticleCopyTab(session);
                    break;
                case 1: // 资源复制
                    DrawResourceCopyTab(session);
                    break;
            }
        }

        // 修改粒子系统复制标签页方法
        private void DrawParticleCopyTab(AnalysisSession session)
        {
            GUILayout.Label("粒子系统复制", EditorStyles.boldLabel);

            // 检查是否分析过场景对象
            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析场景对象", MessageType.Info);
                return;
            }

            // 为每个分析对象构建粒子树
            bool needsRebuild = false;
            foreach (var kvp in session.Analyzers)
            {
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                if (!rootNodes.ContainsKey(objectName) || rootNodes[objectName] == null)
                {
                    BuildParticleTree(targetObject, objectName);
                    needsRebuild = true;
                }
            }

            // 检查是否成功构建粒子树
            if (rootNodes.Count == 0 || rootNodes.All(kvp => kvp.Value == null || (kvp.Value.ParticleSystem == null && kvp.Value.Children.Count == 0)))
            {
                EditorGUILayout.HelpBox("未检测到粒子系统", MessageType.Info);
                return;
            }

            // 应用搜索过滤
            string filterStr = string.IsNullOrEmpty(SearchFilter) ? null : SearchFilter.ToLower();

            // 绘制所有对象的粒子树
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            if (filterStr != null)
            {
                int matchCount = rootNodes.Count(kvp => kvp.Value != null && NodeOrChildMatches(kvp.Value, filterStr));
                if (matchCount == 0)
                {
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的粒子系统", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {matchCount} 个匹配对象", MessageType.Info);
                }
            }

            foreach (var kvp in rootNodes)
            {
                string objectName = kvp.Key;
                var rootNode = kvp.Value;

                if (rootNode != null)
                {
                    // 搜索过滤：跳过不匹配的对象
                    if (filterStr != null && !NodeOrChildMatches(rootNode, filterStr))
                        continue;

                    // 搜索时自动展开
                    if (filterStr != null)
                        foldouts[rootNode.OriginalGameObject] = true;

                    // 绘制对象标题
                    if (!foldouts.ContainsKey(rootNode.OriginalGameObject))
                    {
                        foldouts[rootNode.OriginalGameObject] = false;
                    }

                    foldouts[rootNode.OriginalGameObject] = EditorGUILayout.Foldout(
                        foldouts[rootNode.OriginalGameObject],
                        $"{objectName} (粒子系统)",
                        true
                    );

                    if (foldouts[rootNode.OriginalGameObject])
                    {
                        EditorGUI.indentLevel++;
                        DrawParticleTree(rootNode);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.Space(10);
                }
            }

            EditorGUILayout.EndScrollView();

            // 绘制底部面板
            DrawParticleBottomPanel();
        }

        // 资源复制标签页
        private void DrawResourceCopyTab(AnalysisSession session)
        {
            GUILayout.Label("资源复制(贴图、材质、模型)", EditorStyles.boldLabel);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析资源", MessageType.Info);
                return;
            }

            // 收集所有分析器中的资源
            var allResources = new Dictionary<Object, List<string>>();
            foreach (var analyzer in session.Analyzers.Values)
            {
                foreach (var kvp in analyzer.ResourceUsage)
                {
                    if (kvp.Key is Texture2D || kvp.Key is Material || kvp.Key is Mesh)
                    {
                        if (!allResources.ContainsKey(kvp.Key))
                        {
                            allResources[kvp.Key] = new List<string>();
                        }
                        allResources[kvp.Key].AddRange(kvp.Value);
                    }
                }
            }

            // 应用搜索过滤
            string filterStr = string.IsNullOrEmpty(SearchFilter) ? null : SearchFilter.ToLower();
            if (filterStr != null)
            {
                var filtered = new Dictionary<Object, List<string>>();
                foreach (var kvp in allResources)
                {
                    if (kvp.Key != null && kvp.Key.name != null && kvp.Key.name.ToLower().Contains(filterStr))
                        filtered[kvp.Key] = kvp.Value;
                }
                allResources = filtered;
            }

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            // 资源保存路径设置
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("资源保存路径:", GUILayout.Width(100));
            resourceCopyPath = EditorGUILayout.TextField(resourceCopyPath, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
            {
                string path = EditorUtility.SaveFolderPanel("选择资源保存路径", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        resourceCopyPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        Debug.LogError("路径必须在Assets目录下");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 命名方式选择区域
            EditorGUILayout.Space(10);
            GUILayout.Label("资源命名方式:", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("命名方式:", GUILayout.Width(60));
            _namingMethod = (NamingMethod)EditorGUILayout.EnumPopup(_namingMethod);
            EditorGUILayout.EndHorizontal();

            switch (_namingMethod)
            {
                case NamingMethod.FindReplace:
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("查找:", GUILayout.Width(40));
                    searchText = EditorGUILayout.TextField(searchText, GUILayout.ExpandWidth(true));
                    GUILayout.Label("替换:", GUILayout.Width(40));
                    replaceText = EditorGUILayout.TextField(replaceText, GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();
                    break;

                case NamingMethod.PrefixSerial:
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("前缀:", GUILayout.Width(40));
                    _prefix = EditorGUILayout.TextField(_prefix, GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("起始序号:", GUILayout.Width(60));
                    _startIndex = EditorGUILayout.IntField(_startIndex, GUILayout.Width(80));
                    GUILayout.Label("位数:", GUILayout.Width(40));
                    _digitCount = EditorGUILayout.IntSlider(_digitCount, 1, 6, GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("示例名称:", GUILayout.Width(60));
                    GUILayout.Label(GenerateExampleName(), EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox($"命名格式: [前缀]_[序号]\n例如: {_prefix}_001, {_prefix}_002", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    break;

                case NamingMethod.KeepOriginal: // 新增：保持原名称
                    EditorGUILayout.HelpBox("将保持资源的原始名称不变", MessageType.Info);
                    break;
            }

            EditorGUILayout.Space(10);
            GUILayout.Label("选择要复制的资源:", EditorStyles.boldLabel);

            // 全局操作按钮
            DrawGlobalSelectionButtons();

            resourceScrollPosition = EditorGUILayout.BeginScrollView(
                resourceScrollPosition,
                GUILayout.ExpandHeight(true)
            );

            if (allResources.Count == 0)
            {
                if (filterStr != null)
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的资源", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未找到可复制的资源", MessageType.Info);
            }
            else
            {
                if (filterStr != null)
                    EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {allResources.Count} 个匹配资源", MessageType.Info);

                var resourcesByType = allResources.Keys
                    .Where(r => r != null) // 添加空值检查
                    .GroupBy(r => r.GetType())
                    .OrderBy(g => g.Key.Name);

                foreach (var typeGroup in resourcesByType)
                {
                    if (!_typeFoldouts.ContainsKey(typeGroup.Key))
                    {
                        _typeFoldouts[typeGroup.Key] = false;
                    }

                    // 搜索时自动展开
                    if (filterStr != null)
                        _typeFoldouts[typeGroup.Key] = true;

                    EditorGUILayout.BeginVertical("box");

                    // 类型标题和全选按钮
                    GUILayout.BeginHorizontal();
                    _typeFoldouts[typeGroup.Key] = EditorGUILayout.Foldout(
                        _typeFoldouts[typeGroup.Key],
                        $"{GetTypeDisplayName(typeGroup.Key)} ({typeGroup.Count()}项)",
                        true
                    );

                    GUILayout.FlexibleSpace();

                    // 类型全选按钮
                    string typeKey = typeGroup.Key.Name;
                    if (!_categoryAllSelected.ContainsKey(typeKey))
                    {
                        _categoryAllSelected[typeKey] = false;
                    }

                    EditorGUI.BeginChangeCheck();
                    _categoryAllSelected[typeKey] = EditorGUILayout.Toggle("全选", _categoryAllSelected[typeKey]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetTypeSelection(typeKey, typeGroup.ToList(), _categoryAllSelected[typeKey]);
                    }

                    GUILayout.EndHorizontal();

                    if (_typeFoldouts[typeGroup.Key])
                    {
                        var resourcesByFolder = typeGroup
                            .Where(r => r != null) // 添加空值检查
                            .GroupBy(r => {
                                string path = AssetDatabase.GetAssetPath(r);
                                return string.IsNullOrEmpty(path) ? "未知路径" : Path.GetDirectoryName(path);
                            })
                            .OrderBy(g => g.Key);

                        foreach (var folderGroup in resourcesByFolder)
                        {
                            string folderPath = folderGroup.Key;
                            if (string.IsNullOrEmpty(folderPath)) continue;

                            if (!_folderFoldouts.ContainsKey(folderPath))
                            {
                                _folderFoldouts[folderPath] = false;
                            }

                            // 搜索时自动展开
                            if (filterStr != null)
                                _folderFoldouts[folderPath] = true;

                            EditorGUILayout.BeginVertical("helpbox");
                            EditorGUI.indentLevel++;

                            string folderName = Path.GetFileName(folderPath);
                            if (string.IsNullOrEmpty(folderName))
                                folderName = folderPath;

                            // 文件夹标题和全选按钮
                            GUILayout.BeginHorizontal();
                            _folderFoldouts[folderPath] = EditorGUILayout.Foldout(
                                _folderFoldouts[folderPath],
                                $"{folderName} ({folderGroup.Count()}项)",
                                true
                            );

                            GUILayout.FlexibleSpace();

                            // 文件夹全选按钮
                            if (!_folderAllSelected.ContainsKey(folderPath))
                            {
                                _folderAllSelected[folderPath] = false;
                            }

                            EditorGUI.BeginChangeCheck();
                            _folderAllSelected[folderPath] = EditorGUILayout.Toggle("全选", _folderAllSelected[folderPath]);
                            if (EditorGUI.EndChangeCheck())
                            {
                                SetFolderSelection(folderPath, folderGroup.ToList(), _folderAllSelected[folderPath]);
                            }

                            GUILayout.EndHorizontal();

                            if (_folderFoldouts[folderPath])
                            {
                                var resourceList = folderGroup.ToList();
                                for (int ri = 0; ri < resourceList.Count; ri++)
                                {
                                    var resource = resourceList[ri];
                                    if (resource == null) continue;

                                    using (new UIHelper.ZebraScope(ri))
                                    {
                                        DrawResourceItem(resource);
                                    }
                                }
                            }

                            EditorGUI.indentLevel--;
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.Space(5);
                        }
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(10);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.FlexibleSpace();

            // 重新指认选项
            EditorGUILayout.BeginVertical("box");
            _reassignToObjects = EditorGUILayout.Toggle("复制后重新指认给检测对象", _reassignToObjects);
            EditorGUILayout.HelpBox("启用此选项后，复制的资源将自动替换原对象中使用的资源", MessageType.Info);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("复制选中的资源", GUILayout.Height(30)))
            {
                CopySelectedResources(session);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制全局选择按钮
        /// </summary>
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

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        /// <summary>
        /// 绘制资源项（与整理模块相同的样式）
        /// </summary>
        private void DrawResourceItem(Object resource)
        {
            if (resource == null) return; // 添加空值检查

            EditorGUILayout.BeginHorizontal();

            // 选择状态显示框（只读）
            if (!selectedResources.ContainsKey(resource))
            {
                selectedResources[resource] = false;
            }

            // 显示当前选择状态（不可交互）
            EditorGUILayout.Toggle(selectedResources[resource], GUILayout.Width(20));

            // 选择/取消选择按钮
            string buttonText = selectedResources[resource] ? "取消选择" : "选择";
            if (GUILayout.Button(buttonText, GUILayout.Width(60)))
            {
                selectedResources[resource] = !selectedResources[resource];
                // 强制重绘界面
                GUI.changed = true;
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

            // 资源类型标签
            EditorGUILayout.LabelField(
                GetResourceTypeName(resource),
                GUILayout.Width(80)
            );

            // 弹性空间，将资源框推到最右边
            GUILayout.FlexibleSpace();

            // 对象字段（资源框）- 放在最右边
            EditorGUILayout.ObjectField(
                "",
                resource,
                resource.GetType(),
                false,
                GUILayout.ExpandWidth(true)
            );

            EditorGUILayout.EndHorizontal();

            // 分割线
            EditorGUILayout.Space(2);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
        }

        /// <summary>
        /// 设置类型选择状态
        /// </summary>
        private void SetTypeSelection(string typeKey, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        /// <summary>
        /// 设置文件夹选择状态
        /// </summary>
        private void SetFolderSelection(string folderPath, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        /// <summary>
        /// 全局全选
        /// </summary>
        private void SelectAllResources()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = true;
            }

            // 同时更新所有分类的全选状态
            foreach (var categoryKey in _categoryAllSelected.Keys.ToList())
            {
                _categoryAllSelected[categoryKey] = true;
            }

            foreach (var folderKey in _folderAllSelected.Keys.ToList())
            {
                _folderAllSelected[folderKey] = true;
            }
        }

        /// <summary>
        /// 清除选择
        /// </summary>
        private void ClearSelection()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = false;
            }

            // 同时更新所有分类的全选状态
            foreach (var categoryKey in _categoryAllSelected.Keys.ToList())
            {
                _categoryAllSelected[categoryKey] = false;
            }

            foreach (var folderKey in _folderAllSelected.Keys.ToList())
            {
                _folderAllSelected[folderKey] = false;
            }
        }

        /// <summary>
        /// 检查节点或其子节点是否匹配搜索关键词
        /// </summary>
        private bool NodeOrChildMatches(ParticleNode node, string filterStr)
        {
            if (node == null) return false;
            if (node.OriginalName != null && node.OriginalName.ToLower().Contains(filterStr))
                return true;
            foreach (var child in node.Children)
            {
                if (NodeOrChildMatches(child, filterStr)) return true;
            }
            return false;
        }

        // 构建粒子树
        private void BuildParticleTree(GameObject root, string objectName)
        {
            var rootNode = new ParticleNode
            {
                OriginalGameObject = root,
                ParticleSystem = root.GetComponent<ParticleSystem>(),
                OriginalName = root.name
            };

            BuildTreeRecursive(root, rootNode);
            rootNodes[objectName] = rootNode;
        }

        private void BuildTreeRecursive(GameObject current, ParticleNode parentNode)
        {
            foreach (Transform child in current.transform)
            {
                var particle = child.GetComponent<ParticleSystem>();
                var node = new ParticleNode
                {
                    OriginalGameObject = child.gameObject,
                    ParticleSystem = particle,
                    OriginalName = child.name
                };

                parentNode.Children.Add(node);
                BuildTreeRecursive(child.gameObject, node);
            }
        }

        // 绘制粒子树
    private void DrawParticleTree(ParticleNode node, int depth = 0)
    {
        // 搜索过滤：跳过不匹配的节点
        string filterStr = string.IsNullOrEmpty(SearchFilter) ? null : SearchFilter.ToLower();
        if (filterStr != null && !NodeOrChildMatches(node, filterStr))
            return;

        // 搜索时自动展开子节点
        if (filterStr != null && foldouts.ContainsKey(node.OriginalGameObject))
            foldouts[node.OriginalGameObject] = true;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(depth * 15);

            // 显示选择框（仅当有粒子系统时）
            if (node.ParticleSystem != null)
            {
                bool newSelected = EditorGUILayout.Toggle(node.IsSelected, GUILayout.Width(20));
                if (newSelected != node.IsSelected)
                {
                    node.IsSelected = newSelected;
                    SetChildrenSelected(node, newSelected);
                }
            }
            else
            {
                GUILayout.Space(24); // 保持对齐
            }

            // 显示节点名称（灰色显示无粒子系统的节点）
            bool hasParticleSystem = node.ParticleSystem != null;
            Color originalColor = GUI.color;
            if (!hasParticleSystem) GUI.color = Color.gray;

            // 显示折叠组件
            if (!foldouts.ContainsKey(node.OriginalGameObject))
            {
                foldouts[node.OriginalGameObject] = false;
            }
            foldouts[node.OriginalGameObject] = EditorGUILayout.Foldout(
                foldouts[node.OriginalGameObject],
                $"{node.OriginalName} {(hasParticleSystem ? "(Particle)" : "")}"
            );

            GUI.color = originalColor;
            EditorGUILayout.EndHorizontal();

            // 递归绘制子节点
            if (foldouts[node.OriginalGameObject])
            {
                foreach (var child in node.Children)
                {
                    DrawParticleTree(child, depth + 1);
                }
            }
        }

        private void SetChildrenSelected(ParticleNode node, bool selected)
        {
            node.IsSelected = selected;
            foreach (var child in node.Children)
            {
                SetChildrenSelected(child, selected);
            }
        }

        private void CollectSelectedParticles(ParticleNode node, List<ParticleNode> selected)
        {
            if (node.IsSelected && node.ParticleSystem != null)
            {
                selected.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectSelectedParticles(child, selected);
            }
        }

        private void DrawParticleBottomPanel()
        {
            EditorGUILayout.Space(10);
            GUILayout.BeginVertical("Box");

            GUILayout.BeginHorizontal();
            GUILayout.Label("查找:", GUILayout.Width(40));
            searchText = EditorGUILayout.TextField(searchText);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("替换:", GUILayout.Width(40));
            replaceText = EditorGUILayout.TextField(replaceText);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("材质路径:", GUILayout.Width(60));
            materialCopyPath = EditorGUILayout.TextField(materialCopyPath);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
            {
                string path = EditorUtility.SaveFolderPanel("选择材质保存路径", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        materialCopyPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        Debug.LogError("路径必须在Assets目录下");
                    }
                }
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("复制粒子系统及材质", GUILayout.Height(30)))
            {
                CopySelectedParticles();
            }

            GUILayout.EndVertical();
        }

        private void CopySelectedParticles()
        {
            if (rootNodes.Count == 0) return;

            // 确保材质保存路径存在
            if (!AssetDatabase.IsValidFolder(materialCopyPath))
            {
                AssetDatabase.CreateFolder("Assets", "CopiedMaterials");
                materialCopyPath = "Assets/CopiedMaterials";
            }

            // 收集所有选中的粒子系统
            var selectedParticles = new List<ParticleNode>();
            foreach (var rootNode in rootNodes.Values)
            {
                if (rootNode != null)
                {
                    CollectSelectedParticles(rootNode, selectedParticles);
                }
            }

            // 过滤掉没有粒子系统的节点
            selectedParticles = selectedParticles.Where(node => node.ParticleSystem != null).ToList();

            if (selectedParticles.Count == 0)
            {
                Debug.LogWarning("没有选中任何粒子系统");
                return;
            }

            // 创建父对象
            GameObject parentObject = new GameObject("CopiedParticles");

            foreach (var node in selectedParticles)
            {
                // 复制粒子系统游戏对象
                GameObject copiedParticle = Object.Instantiate(node.ParticleSystem.gameObject);
                copiedParticle.transform.SetParent(parentObject.transform);

                // 重命名游戏对象
                string newName = node.OriginalName.Replace(searchText, replaceText);
                copiedParticle.name = newName;

                // 复制并替换材质
                var renderer = copiedParticle.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    Material[] sharedMaterials = renderer.sharedMaterials;
                    Material[] copiedMaterials = new Material[sharedMaterials.Length];

                    for (int i = 0; i < sharedMaterials.Length; i++)
                    {
                        if (sharedMaterials[i] != null)
                        {
                            // 复制材质
                            Material copiedMaterial = new Material(sharedMaterials[i]);
                            string materialName = sharedMaterials[i].name.Replace(searchText, replaceText);
                            copiedMaterial.name = materialName;

                            // 保存材质
                            string materialPath = $"{materialCopyPath}/{materialName}.mat";
                            AssetDatabase.CreateAsset(copiedMaterial, materialPath);

                            copiedMaterials[i] = copiedMaterial;
                        }
                    }

                    // 应用新材质
                    renderer.sharedMaterials = copiedMaterials;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"成功复制 {selectedParticles.Count} 个粒子系统");
        }

        // 资源复制相关方法
        private void CopySelectedResources(AnalysisSession session)
        {
            if (selectedResources.Count == 0 || !selectedResources.Any(kvp => kvp.Value))
            {
                Debug.LogWarning("没有选中任何资源");
                return;
            }

            if (!AssetDatabase.IsValidFolder(resourceCopyPath))
            {
                string parentFolder = Path.GetDirectoryName(resourceCopyPath);
                string folderName = Path.GetFileName(resourceCopyPath);
                if (string.IsNullOrEmpty(parentFolder)) parentFolder = "Assets";
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }

            Dictionary<Object, Object> copiedResources = new Dictionary<Object, Object>();
            int currentIndex = _startIndex;

            var textures = selectedResources
                .Where(kvp => kvp.Value && kvp.Key is Texture2D)
                .Select(kvp => kvp.Key)
                .ToList();

            // 自动检测场景中引用选中贴图的材质，在贴图复制后原地替换引用
            var autoMaterials = new List<Material>();
            if (_reassignToObjects && textures.Count > 0)
            {
                foreach (var gameObject in session.Analyzers.Keys)
                {
                    if (gameObject == null) continue;
                    var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null) continue;
                            if (autoMaterials.Contains(mat)) continue;
                            // 跳过用户已手动选中的材质
                            if (selectedResources.ContainsKey(mat) && selectedResources[mat]) continue;

                            Shader shader = mat.shader;
                            if (shader == null) continue;
                            int propCount = ShaderUtil.GetPropertyCount(shader);
                            for (int i = 0; i < propCount; i++)
                            {
                                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                                {
                                    string propName = ShaderUtil.GetPropertyName(shader, i);
                                    Texture tex = mat.GetTexture(propName);
                                    if (tex != null && textures.Contains(tex))
                                    {
                                        autoMaterials.Add(mat);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (autoMaterials.Count > 0)
                {
                    Debug.Log($"[CopyModule] 自动检测到 {autoMaterials.Count} 个材质引用选中的贴图，将在原材质上原地替换贴图引用");
                }
            }

            foreach (var texture in textures)
            {
                CopyResource(texture, ref currentIndex, copiedResources);
            }

            // 在自动检测到的原始材质上原地替换贴图引用（不复制材质球）
            // 正确流程：选中贴图 α → 复制 α' → 材质 M 的 α 引用原地指向 α'
            foreach (var mat in autoMaterials)
            {
                UpdateMaterialTextures(mat, copiedResources);
            }

            // 处理用户手动选中的材质（这些才需要复制并替换）
            var userMaterials = selectedResources
                .Where(kvp => kvp.Value && kvp.Key is Material)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var material in userMaterials)
            {
                Material newMaterial = CopyResource(material, ref currentIndex, copiedResources) as Material;

                if (newMaterial != null)
                {
                    UpdateMaterialTextures(newMaterial, copiedResources);
                }
            }

            var meshes = selectedResources
                .Where(kvp => kvp.Value && kvp.Key is Mesh)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var mesh in meshes)
            {
                CopyResource(mesh, ref currentIndex, copiedResources);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 只有在用户选择重新指认时才执行
            if (_reassignToObjects && copiedResources.Count > 0)
            {
                foreach (var gameObject in session.Analyzers.Keys)
                {
                    if (gameObject != null)
                    {
                        ReassignResources(gameObject, copiedResources);
                    }
                }
                Debug.Log($"成功将 {copiedResources.Count} 个资源重新指认给分析对象");
            }

            Debug.Log($"成功复制 {copiedResources.Count} 个资源到 {resourceCopyPath}");
        }

        private Object CopyResource(Object resource, ref int currentIndex, Dictionary<Object, Object> copiedResources)
        {
            if (resource == null) return null;

            string originalPath = AssetDatabase.GetAssetPath(resource);
            if (string.IsNullOrEmpty(originalPath)) return null;

            string newName;
            switch (_namingMethod)
            {
                case NamingMethod.PrefixSerial:
                    newName = $"{_prefix}_{currentIndex.ToString($"D{_digitCount}")}";
                    currentIndex++;
                    break;

                case NamingMethod.KeepOriginal: // 新增：保持原名称
                    newName = resource.name;
                    break;

                case NamingMethod.FindReplace:
                default:
                    newName = resourceRenameMap.ContainsKey(resource) ?
                             resourceRenameMap[resource] :
                             resource.name.Replace(searchText, replaceText);
                    break;
            }

            string extension = Path.GetExtension(originalPath);
            string newPath = $"{resourceCopyPath}/{newName}{extension}";

            // 处理文件名冲突
            int counter = 1;
            string basePath = newPath;
            while (AssetDatabase.LoadAssetAtPath(newPath, resource.GetType()) != null)
            {
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(basePath);
                newPath = $"{resourceCopyPath}/{nameWithoutExtension}_{counter}{extension}";
                counter++;
            }

            if (AssetDatabase.CopyAsset(originalPath, newPath))
            {
                Object newResource = AssetDatabase.LoadAssetAtPath(newPath, resource.GetType());
                if (newResource != null)
                {
                    copiedResources[resource] = newResource;
                    Debug.Log($"成功复制资源: {resource.name} -> {newName}");
                    return newResource;
                }
            }
            else
            {
                Debug.LogError($"复制资源失败: {resource.name}");
            }

            return null;
        }

        private void UpdateMaterialTextures(Material newMaterial, Dictionary<Object, Object> copiedResources)
        {
            Shader shader = newMaterial.shader;
            if (shader == null) return;

            bool materialChanged = false;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    Texture originalTexture = newMaterial.GetTexture(propertyName);

                    if (originalTexture != null && copiedResources.TryGetValue(originalTexture, out Object newTexture))
                    {
                        newMaterial.SetTexture(propertyName, (Texture)newTexture);
                        materialChanged = true;
                        Debug.Log($"更新材质 {newMaterial.name} 的贴图 {propertyName}: {originalTexture.name} -> {newTexture.name}");
                    }
                }
            }

            if (materialChanged)
            {
                EditorUtility.SetDirty(newMaterial);
            }
        }

        private void ReassignResources(GameObject root, Dictionary<Object, Object> copiedResources)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && copiedResources.TryGetValue(materials[i], out Object newMaterial))
                    {
                        materials[i] = (Material)newMaterial;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in meshFilters)
            {
                if (filter.sharedMesh != null && copiedResources.TryGetValue(filter.sharedMesh, out Object newMesh))
                {
                    filter.sharedMesh = (Mesh)newMesh;
                }
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer skinnedRenderer in skinnedMeshRenderers)
            {
                if (skinnedRenderer.sharedMesh != null && copiedResources.TryGetValue(skinnedRenderer.sharedMesh, out Object newMesh))
                {
                    skinnedRenderer.sharedMesh = (Mesh)newMesh;
                }
            }

            // 处理 ParticleSystem 中引用的 Mesh（Shape 模块 & Renderer Mesh 模式）
            // 复制模型后需同步更新粒子系统的 mesh 引用
            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in particleSystems)
            {
                if (ps == null) continue;

                var shape = ps.shape;

                // Shape 类型为 Mesh 时：直接更新 shape.mesh 引用
                // （MeshRenderer / SkinnedMeshRenderer 类型会通过上方 MeshFilter/SkinnedMeshRenderer
                //   的更新自动感知，无需额外处理）
                if (shape.shapeType == ParticleSystemShapeType.Mesh)
                {
                    if (shape.mesh != null && copiedResources.TryGetValue(shape.mesh, out Object newShapeMesh))
                    {
                        shape.mesh = (Mesh)newShapeMesh;
                    }
                }

                // 处理 ParticleSystemRenderer 的 mesh 引用（Render Mode = Mesh 时）
                var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
                if (psRenderer != null && psRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    Mesh[] rendererMeshes = new Mesh[psRenderer.meshCount];
                    psRenderer.GetMeshes(rendererMeshes);
                    bool rendererMeshChanged = false;
                    for (int mi = 0; mi < rendererMeshes.Length; mi++)
                    {
                        if (rendererMeshes[mi] != null && copiedResources.TryGetValue(rendererMeshes[mi], out Object newRendererMesh))
                        {
                            rendererMeshes[mi] = (Mesh)newRendererMesh;
                            rendererMeshChanged = true;
                        }
                    }
                    if (rendererMeshChanged)
                    {
                        psRenderer.SetMeshes(rendererMeshes);
                    }
                }
            }

            Debug.Log($"成功将 {copiedResources.Count} 个资源重新指认给分析对象");
        }

        private string GetTypeDisplayName(System.Type type)
        {
            if (type == typeof(Texture2D)) return "贴图";
            if (type == typeof(Material)) return "材质";
            if (type == typeof(Mesh)) return "模型";
            return type.Name;
        }

        private string GetResourceTypeName(Object resource)
        {
            if (resource is Texture2D) return "贴图";
            if (resource is Material) return "材质";
            if (resource is Mesh) return "模型";
            return resource.GetType().Name;
        }

        private string GenerateExampleName()
        {
            return $"{_prefix}_{_startIndex.ToString($"D{_digitCount}")}";
        }

        public void Clear()
        {
            rootNodes.Clear();
            foldouts.Clear();
            selectedResources.Clear();
            resourceRenameMap.Clear();
            _typeFoldouts.Clear();
            _folderFoldouts.Clear();
            _categoryAllSelected.Clear();
            _folderAllSelected.Clear();
        }
    }
}