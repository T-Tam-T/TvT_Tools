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
        // 资源复制成员变量
        private Dictionary<Object, bool> selectedResources = new Dictionary<Object, bool>();
        private Dictionary<Object, string> resourceRenameMap = new Dictionary<Object, string>();
        private string resourceCopyPath = "Assets/CopiedResources";
        private Vector2 resourceScrollPosition;

        // 查找/替换命名
        private string searchText = "";
        private string replaceText = "";

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
        private readonly string[] tabNames = { "资源复制", "回档" };

        // 命名方式相关成员变量
        private enum NamingMethod
        {
            FindReplace,    // 查找替换
            PrefixSerial,   // 前缀+序号
            KeepOriginal    // 保持原名称
        }

        private NamingMethod _namingMethod = NamingMethod.FindReplace;
        private string _prefix = "NewResource";
        private int _startIndex = 1;
        private int _digitCount = 3;

        // 重新指认选项
        private bool _reassignToObjects = true;

        // 回档相关
        private string rollbackFolder = "Assets/ResourceManagerRollback";
        private Vector2 rollbackScrollPosition;

        // ----- 回档数据结构 -----

        [System.Serializable]
        private class RollbackSnapshot
        {
            public int version = 1;
            public string timestamp;                 // 唯一时间戳：yyyyMMdd_HHmmss
            public List<RollbackObject> objects = new List<RollbackObject>();
            public List<RollbackTextureRef> materialTextures = new List<RollbackTextureRef>();
        }

        [System.Serializable]
        private class RollbackObject
        {
            public string key;                       // 对象唯一标识（prefab:路径 / scene:名称）
            public string name;                      // 显示名
            public bool isPrefab;
            public List<RollbackMaterialRef> materials = new List<RollbackMaterialRef>();
            public List<RollbackMeshRef> meshes = new List<RollbackMeshRef>();
            public List<RollbackMeshRef> skinnedMeshes = new List<RollbackMeshRef>();
        }

        [System.Serializable]
        private class RollbackMaterialRef
        {
            public string rendererPath;              // 渲染器相对路径
            public int materialIndex;
            public string materialPath;              // 原材质路径
        }

        [System.Serializable]
        private class RollbackMeshRef
        {
            public string componentPath;             // 组件相对路径
            public string meshPath;                  // 原网格路径
        }

        [System.Serializable]
        private class RollbackTextureRef
        {
            public string materialPath;              // 被原地修改的材质资产路径
            public string propertyName;              // Shader 贴图属性名
            public string texturePath;               // 原贴图路径
        }

        // ----- 入口 -----
        public void Draw(AnalysisSession session)
        {
            selectedTabIndex = GUILayout.Toolbar(selectedTabIndex, tabNames);

            switch (selectedTabIndex)
            {
                case 0: // 资源复制
                    DrawResourceCopyTab(session);
                    break;
                case 1: // 回档
                    DrawRollbackTab(session);
                    break;
            }
        }

        // =====================================================================
        //  资源复制标签页
        // =====================================================================
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

                case NamingMethod.KeepOriginal: // 保持原名称
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
                    .Where(r => r != null)
                    .GroupBy(r => r.GetType())
                    .OrderBy(g => g.Key.Name);

                foreach (var typeGroup in resourcesByType)
                {
                    if (!_typeFoldouts.ContainsKey(typeGroup.Key))
                    {
                        _typeFoldouts[typeGroup.Key] = false;
                    }

                    if (filterStr != null)
                        _typeFoldouts[typeGroup.Key] = true;

                    EditorGUILayout.BeginVertical("box");

                    GUILayout.BeginHorizontal();
                    _typeFoldouts[typeGroup.Key] = EditorGUILayout.Foldout(
                        _typeFoldouts[typeGroup.Key],
                        $"{GetTypeDisplayName(typeGroup.Key)} ({typeGroup.Count()}项)",
                        true
                    );

                    GUILayout.FlexibleSpace();

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
                            .Where(r => r != null)
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

                            if (filterStr != null)
                                _folderFoldouts[folderPath] = true;

                            EditorGUILayout.BeginVertical("helpbox");
                            EditorGUI.indentLevel++;

                            string folderName = Path.GetFileName(folderPath);
                            if (string.IsNullOrEmpty(folderName))
                                folderName = folderPath;

                            GUILayout.BeginHorizontal();
                            _folderFoldouts[folderPath] = EditorGUILayout.Foldout(
                                _folderFoldouts[folderPath],
                                $"{folderName} ({folderGroup.Count()}项)",
                                true
                            );

                            GUILayout.FlexibleSpace();

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
            _reassignToObjects = EditorGUILayout.Toggle("复制后重新指认给检测对象（并生成回档文件）", _reassignToObjects);
            EditorGUILayout.HelpBox("启用此选项后，复制的资源将自动替换原对象中使用的资源，并在复制前生成一份回档快照（日期+时间命名），可在「回档」标签页还原。", MessageType.Info);
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
        /// 绘制资源项
        /// </summary>
        private void DrawResourceItem(Object resource)
        {
            if (resource == null) return;

            EditorGUILayout.BeginHorizontal();

            if (!selectedResources.ContainsKey(resource))
            {
                selectedResources[resource] = false;
            }

            EditorGUILayout.Toggle(selectedResources[resource], GUILayout.Width(20));

            string buttonText = selectedResources[resource] ? "取消选择" : "选择";
            if (GUILayout.Button(buttonText, GUILayout.Width(60)))
            {
                selectedResources[resource] = !selectedResources[resource];
                GUI.changed = true;
            }

            if (!resourceRenameMap.ContainsKey(resource))
            {
                resourceRenameMap[resource] = resource.name;
            }

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(
                resourceRenameMap[resource],
                GUILayout.Width(150)
            );

            if (EditorGUI.EndChangeCheck())
            {
                resourceRenameMap[resource] = newName;
            }

            EditorGUILayout.LabelField(
                GetResourceTypeName(resource),
                GUILayout.Width(80)
            );

            GUILayout.FlexibleSpace();

            EditorGUILayout.ObjectField(
                "",
                resource,
                resource.GetType(),
                false,
                GUILayout.ExpandWidth(true)
            );

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
        }

        private void SetTypeSelection(string typeKey, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        private void SetFolderSelection(string folderPath, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        private void SelectAllResources()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = true;
            }
            foreach (var categoryKey in _categoryAllSelected.Keys.ToList())
            {
                _categoryAllSelected[categoryKey] = true;
            }
            foreach (var folderKey in _folderAllSelected.Keys.ToList())
            {
                _folderAllSelected[folderKey] = true;
            }
        }

        private void ClearSelection()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = false;
            }
            foreach (var categoryKey in _categoryAllSelected.Keys.ToList())
            {
                _categoryAllSelected[categoryKey] = false;
            }
            foreach (var folderKey in _folderAllSelected.Keys.ToList())
            {
                _folderAllSelected[folderKey] = false;
            }
        }

        // =====================================================================
        //  资源复制执行 + 回档快照
        // =====================================================================
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

            // ---- 在修改任何引用之前，捕获回档快照（记录“之前”的资源引用） ----
            RollbackSnapshot beforeSnapshot = null;
            if (_reassignToObjects)
            {
                beforeSnapshot = CaptureBeforeSnapshot(session);
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

            foreach (var mat in autoMaterials)
            {
                UpdateMaterialTextures(mat, copiedResources);
            }

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

                // ---- 真正发生了重新指认，保存回档快照 ----
                if (beforeSnapshot != null && beforeSnapshot.objects.Count > 0)
                {
                    beforeSnapshot.timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string snapshotPath = SaveRollbackSnapshot(beforeSnapshot);
                    Debug.Log($"[CopyModule] 已生成回档文件: {snapshotPath}");
                }
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

                case NamingMethod.KeepOriginal:
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

            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in particleSystems)
            {
                if (ps == null) continue;

                var shape = ps.shape;

                if (shape.shapeType == ParticleSystemShapeType.Mesh)
                {
                    if (shape.mesh != null && copiedResources.TryGetValue(shape.mesh, out Object newShapeMesh))
                    {
                        shape.mesh = (Mesh)newShapeMesh;
                    }
                }

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

        // =====================================================================
        //  回档：快照生成 / 读取 / 还原
        // =====================================================================

        /// <summary>
        /// 捕获某个分析对象“当前”的资源引用（材质 + 网格），作为回档基准。
        /// </summary>
        private RollbackSnapshot CaptureBeforeSnapshot(AnalysisSession session)
        {
            var snap = new RollbackSnapshot { version = 1 };
            var seenMaterials = new HashSet<Material>();
            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;
                var objSnap = CaptureObjectSnapshot(go);
                if (objSnap != null &&
                    (objSnap.materials.Count > 0 || objSnap.meshes.Count > 0 || objSnap.skinnedMeshes.Count > 0))
                {
                    snap.objects.Add(objSnap);
                }

                // 收集对象用到的所有材质及其内部贴图引用（用于“材质内原地替换贴图”的回档）
                if (objSnap != null)
                {
                    foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null || renderer.sharedMaterials == null) continue;
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null || seenMaterials.Contains(mat)) continue;
                            seenMaterials.Add(mat);
                            CaptureMaterialTextures(mat, snap.materialTextures);
                        }
                    }
                }
            }
            return snap;
        }

        private void CaptureMaterialTextures(Material mat, List<RollbackTextureRef> list)
        {
            if (mat == null || mat.shader == null) return;
            int propCount = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                    Texture tex = mat.GetTexture(propName);
                    if (tex == null) continue;
                    list.Add(new RollbackTextureRef
                    {
                        materialPath = AssetDatabase.GetAssetPath(mat) ?? "",
                        propertyName = propName,
                        texturePath = AssetDatabase.GetAssetPath(tex) ?? ""
                    });
                }
            }
        }

        private RollbackObject CaptureObjectSnapshot(GameObject go)
        {
            if (go == null) return null;

            var obj = new RollbackObject
            {
                key = GetObjectKey(go),
                name = go.name,
                isPrefab = PrefabUtility.IsPartOfPrefabAsset(go)
            };

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.sharedMaterials == null) continue;
                var mats = renderer.sharedMaterials;
                string rendererPath = GetComponentPath(go, renderer.gameObject);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        obj.materials.Add(new RollbackMaterialRef
                        {
                            rendererPath = rendererPath,
                            materialIndex = i,
                            materialPath = AssetDatabase.GetAssetPath(mats[i]) ?? ""
                        });
                    }
                }
            }

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                obj.meshes.Add(new RollbackMeshRef
                {
                    componentPath = GetComponentPath(go, mf.gameObject),
                    meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh) ?? ""
                });
            }

            foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sm == null || sm.sharedMesh == null) continue;
                obj.skinnedMeshes.Add(new RollbackMeshRef
                {
                    componentPath = GetComponentPath(go, sm.gameObject),
                    meshPath = AssetDatabase.GetAssetPath(sm.sharedMesh) ?? ""
                });
            }

            return obj;
        }

        private string GetObjectKey(GameObject go)
        {
            if (go == null) return "null";
            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                string path = AssetDatabase.GetAssetPath(go);
                return string.IsNullOrEmpty(path) ? ("prefab:" + go.name) : ("prefab:" + path);
            }
            return "scene:" + go.name;
        }

        private string GetComponentPath(GameObject root, GameObject comp)
        {
            if (root == null || comp == null) return "";
            var names = new List<string>();
            var t = comp.transform;
            while (t != null && t != root.transform)
            {
                names.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", names);
        }

        private string SaveRollbackSnapshot(RollbackSnapshot snapshot)
        {
            if (!AssetDatabase.IsValidFolder(rollbackFolder))
            {
                string parent = Path.GetDirectoryName(rollbackFolder);
                string folder = Path.GetFileName(rollbackFolder);
                if (string.IsNullOrEmpty(parent)) parent = "Assets";
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(parent));
                AssetDatabase.CreateFolder(parent, folder);
            }

            string json = JsonUtility.ToJson(snapshot, true);
            string fileName = $"回档_{snapshot.timestamp}.json";
            string assetPath = $"{rollbackFolder}/{fileName}";
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, json, new System.Text.UTF8Encoding(false));
            AssetDatabase.Refresh();
            return assetPath;
        }

        private List<KeyValuePair<string, RollbackSnapshot>> LoadAllSnapshots()
        {
            var result = new List<KeyValuePair<string, RollbackSnapshot>>();
            if (!AssetDatabase.IsValidFolder(rollbackFolder)) return result;

            string fullDir = Path.Combine(Application.dataPath, rollbackFolder.Substring("Assets/".Length));
            if (!Directory.Exists(fullDir)) return result;

            foreach (var file in Directory.GetFiles(fullDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var snap = JsonUtility.FromJson<RollbackSnapshot>(json);
                    if (snap != null)
                    {
                        result.Add(new KeyValuePair<string, RollbackSnapshot>(Path.GetFileName(file), snap));
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CopyModule] 读取回档失败: {file} ({e.Message})");
                }
            }

            // 最新在前
            return result.OrderByDescending(kvp => kvp.Value != null ? kvp.Value.timestamp : "").ToList();
        }

        // =====================================================================
        //  回档标签页
        // =====================================================================
        private int selectedSnapshotIndex = -1;

        /// <summary>
        /// 回档对比行：一次“当前资源 → 回档资源”的变更。
        /// </summary>
        private class RollbackDiffRow
        {
            public string objectName;
            public string slotLabel;
            public string currentPath;
            public string beforePath;
            public string kind;            // material / mesh / skinnedMesh / texture
            public string componentPath;   // material/mesh 的组件相对路径
            public int materialIndex;      // material 的槽位
            public string materialPath;    // texture 的材质路径
            public string propertyName;    // texture 的属性名
        }

        private void DrawRollbackTab(AnalysisSession session)
        {
            GUILayout.Label("回档（资源还原）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "资源复制并重新指认后，会生成按「日期_时间」命名的回档文件。\n" +
                "选择一个回档日期，即可查看「当前资源 → 回档资源」的对比；点击下方「确认回档」进行还原。\n" +
                "若历史资源已被删除，将无法还原并会在 Console 提示。",
                MessageType.Info);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            var snapshots = LoadAllSnapshots();

            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无回档记录。执行一次「资源复制（并重新指认）」后会生成回档文件。", MessageType.Info);
                return;
            }

            // ---- 回档日期下拉（所有快照，最新在前）----
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("回档日期:", GUILayout.Width(70));
            if (selectedSnapshotIndex < 0) selectedSnapshotIndex = 0;
            if (selectedSnapshotIndex >= snapshots.Count) selectedSnapshotIndex = 0;

            string[] dateStrings = snapshots.Select(s => FormatTimestamp(s.Value.timestamp)).ToArray();
            selectedSnapshotIndex = EditorGUILayout.Popup(selectedSnapshotIndex, dateStrings, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            var selected = snapshots[selectedSnapshotIndex];

            EditorGUILayout.Space(6);

            // ---- 变更对比列表（仅显示真正发生变化的引用）----
            var diffRows = BuildRollbackDiffRows(session, selected.Value);

            rollbackScrollPosition = EditorGUILayout.BeginScrollView(rollbackScrollPosition, GUILayout.ExpandHeight(true));

            if (diffRows.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "所选回档日期与当前分析对象没有匹配的变更。\n" +
                    "可能该对象未参与这次复制，或当前资源已与回档一致。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"共 {diffRows.Count} 处变更。点击下方「确认回档」会把「当前资源」还原为「回档资源」。", MessageType.Info);

                int zebra = 0;
                foreach (var objGroup in diffRows.GroupBy(r => r.objectName))
                {
                    EditorGUILayout.LabelField($"— {objGroup.Key} —", EditorStyles.boldLabel);

                    foreach (var row in objGroup)
                    {
                        using (new UIHelper.ZebraScope(zebra))
                        {
                            EditorGUILayout.BeginVertical("box");
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label(row.slotLabel, GUILayout.Width(150));
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField("当前:", EditorStyles.miniLabel, GUILayout.Width(30));
                            EditorGUILayout.SelectableLabel(row.currentPath, EditorStyles.miniLabel, GUILayout.Width(160));
                            GUILayout.Label("→", GUILayout.Width(14));
                            EditorGUILayout.LabelField("回档:", EditorStyles.miniLabel, GUILayout.Width(30));
                            EditorGUILayout.SelectableLabel(row.beforePath, EditorStyles.miniLabel, GUILayout.Width(160));
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.EndVertical();
                        }
                        zebra++;
                    }
                    EditorGUILayout.Space(4);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // ---- 确认回档按钮 ----
            if (GUILayout.Button("确认回档", GUILayout.Height(32)))
            {
                bool allOk = RestoreSnapshot(session, selected.Value, selected.Key);
                EditorUtility.DisplayDialog(
                    "回档完成",
                    allOk
                        ? $"已将匹配对象还原到 {FormatTimestamp(selected.Value.timestamp)}。"
                        : $"存在已删除或不匹配的资源，未能完全还原，请查看 Console。",
                    "确定");
            }
        }

        private string FormatTimestamp(string ts)
        {
            if (string.IsNullOrEmpty(ts)) return "未知时间";
            if (ts.Length >= 15)
            {
                return $"{ts.Substring(0, 4)}-{ts.Substring(4, 2)}-{ts.Substring(6, 2)} {ts.Substring(9, 2)}:{ts.Substring(11, 2)}:{ts.Substring(13, 2)}";
            }
            return ts;
        }

        /// <summary>
        /// 还原单个对象到指定快照。返回是否全部成功（无删除/不匹配）。
        /// </summary>
        private bool RestoreObjectToSnapshot(GameObject go, RollbackSnapshot snapshot, string fileName)
        {
            if (go == null || snapshot == null) return false;
            var objSnap = snapshot.objects.FirstOrDefault(o => o.key == GetObjectKey(go));
            if (objSnap == null) return false;

            bool allOk = true;
            bool fileDirty = false;

            // 还原材质
            foreach (var m in objSnap.materials)
            {
                var targetGo = FindChildByPath(go, m.rendererPath);
                if (targetGo == null) continue;
                var renderer = targetGo.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterials == null) continue;

                var mats = renderer.sharedMaterials;
                if (m.materialIndex < 0 || m.materialIndex >= mats.Length) continue;

                var original = string.IsNullOrEmpty(m.materialPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Material>(m.materialPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史材质已删除，无法还原: {m.materialPath} ({go.name}/{m.rendererPath}[{m.materialIndex}])");
                    continue;
                }

                if (mats[m.materialIndex] != original)
                {
                    mats[m.materialIndex] = original;
                    renderer.sharedMaterials = mats;
                    fileDirty = true;
                }
            }

            // 还原 MeshFilter 网格
            foreach (var mm in objSnap.meshes)
            {
                var targetGo = FindChildByPath(go, mm.componentPath);
                if (targetGo == null) continue;
                var mf = targetGo.GetComponent<MeshFilter>();
                if (mf == null) continue;

                var original = string.IsNullOrEmpty(mm.meshPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Mesh>(mm.meshPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史网格已删除，无法还原: {mm.meshPath} ({go.name}/{mm.componentPath})");
                    continue;
                }

                if (mf.sharedMesh != original)
                {
                    mf.sharedMesh = original;
                    fileDirty = true;
                }
            }

            // 还原 SkinnedMeshRenderer 网格
            foreach (var sm in objSnap.skinnedMeshes)
            {
                var targetGo = FindChildByPath(go, sm.componentPath);
                if (targetGo == null) continue;
                var skinned = targetGo.GetComponent<SkinnedMeshRenderer>();
                if (skinned == null) continue;

                var original = string.IsNullOrEmpty(sm.meshPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Mesh>(sm.meshPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史网格已删除，无法还原: {sm.meshPath} ({go.name}/{sm.componentPath})");
                    continue;
                }

                if (skinned.sharedMesh != original)
                {
                    skinned.sharedMesh = original;
                    fileDirty = true;
                }
            }

            if (fileDirty)
            {
                EditorUtility.SetDirty(go);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[回档] {go.name} 已还原到 {snapshot.timestamp}（来自 {fileName}）");
            }

            return allOk;
        }

        private bool RestoreSnapshot(AnalysisSession session, RollbackSnapshot snapshot, string fileName)
        {
            if (snapshot == null) return false;
            bool allOk = true;
            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;
                if (snapshot.objects.Any(o => o.key == GetObjectKey(go)))
                {
                    if (!RestoreObjectToSnapshot(go, snapshot, fileName))
                        allOk = false;
                }
            }
            if (!RestoreMaterialTextures(snapshot))
                allOk = false;
            return allOk;
        }

        /// <summary>
        /// 还原材质内被原地替换的贴图引用。
        /// </summary>
        private bool RestoreMaterialTextures(RollbackSnapshot snapshot)
        {
            if (snapshot == null || snapshot.materialTextures == null) return true;
            bool allOk = true;
            bool dirty = false;
            foreach (var t in snapshot.materialTextures)
            {
                if (string.IsNullOrEmpty(t.materialPath) || string.IsNullOrEmpty(t.propertyName)) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(t.materialPath);
                if (mat == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 材质已删除，无法还原贴图: {t.materialPath}");
                    continue;
                }
                var original = string.IsNullOrEmpty(t.texturePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture>(t.texturePath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史贴图已删除，无法还原: {t.texturePath} (材质 {t.materialPath} · {t.propertyName})");
                    continue;
                }
                Texture cur = mat.GetTexture(t.propertyName);
                if (cur != original)
                {
                    mat.SetTexture(t.propertyName, original);
                    EditorUtility.SetDirty(mat);
                    dirty = true;
                }
            }
            if (dirty)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return allOk;
        }

        /// <summary>
        /// 构建「当前资源 → 回档资源」的变更对比行（仅包含真正发生变化的引用）。
        /// </summary>
        private List<RollbackDiffRow> BuildRollbackDiffRows(AnalysisSession session, RollbackSnapshot snapshot)
        {
            var rows = new List<RollbackDiffRow>();
            if (snapshot == null) return rows;

            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;
                var objSnap = snapshot.objects.FirstOrDefault(o => o.key == GetObjectKey(go));
                if (objSnap == null) continue;

                foreach (var m in objSnap.materials)
                {
                    var targetGo = FindChildByPath(go, m.rendererPath);
                    if (targetGo == null) continue;
                    var renderer = targetGo.GetComponent<Renderer>();
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    var mats = renderer.sharedMaterials;
                    if (m.materialIndex < 0 || m.materialIndex >= mats.Length) continue;
                    string current = mats[m.materialIndex] != null ? AssetDatabase.GetAssetPath(mats[m.materialIndex]) : "";
                    if (current == m.materialPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = $"材质[{m.materialIndex}]",
                        currentPath = ShortName(current),
                        beforePath = ShortName(m.materialPath),
                        kind = "material",
                        componentPath = m.rendererPath,
                        materialIndex = m.materialIndex
                    });
                }

                foreach (var mm in objSnap.meshes)
                {
                    var targetGo = FindChildByPath(go, mm.componentPath);
                    if (targetGo == null) continue;
                    var mf = targetGo.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    string current = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (current == mm.meshPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = "网格",
                        currentPath = ShortName(current),
                        beforePath = ShortName(mm.meshPath),
                        kind = "mesh",
                        componentPath = mm.componentPath
                    });
                }

                foreach (var sm in objSnap.skinnedMeshes)
                {
                    var targetGo = FindChildByPath(go, sm.componentPath);
                    if (targetGo == null) continue;
                    var skinned = targetGo.GetComponent<SkinnedMeshRenderer>();
                    if (skinned == null || skinned.sharedMesh == null) continue;
                    string current = AssetDatabase.GetAssetPath(skinned.sharedMesh);
                    if (current == sm.meshPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = "蒙皮网格",
                        currentPath = ShortName(current),
                        beforePath = ShortName(sm.meshPath),
                        kind = "skinnedMesh",
                        componentPath = sm.componentPath
                    });
                }
            }

            // 材质内贴图引用变更（快照级，去重显示）
            if (snapshot.materialTextures != null)
            {
                foreach (var t in snapshot.materialTextures)
                {
                    if (string.IsNullOrEmpty(t.materialPath) || string.IsNullOrEmpty(t.propertyName)) continue;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(t.materialPath);
                    if (mat == null) continue;
                    Texture cur = mat.GetTexture(t.propertyName);
                    string current = cur != null ? AssetDatabase.GetAssetPath(cur) : "";
                    if (current == t.texturePath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = "材质: " + mat.name,
                        slotLabel = $"贴图[{t.propertyName}]",
                        currentPath = ShortName(current),
                        beforePath = ShortName(t.texturePath),
                        kind = "texture",
                        materialPath = t.materialPath,
                        propertyName = t.propertyName
                    });
                }
            }

            return rows;
        }

        private string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(无)";
            return Path.GetFileName(path);
        }

        private GameObject FindChildByPath(GameObject root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root;
            var current = root;
            foreach (var seg in path.Split('/'))
            {
                if (string.IsNullOrEmpty(seg)) continue;
                var child = current.transform.Find(seg);
                if (child == null) return null;
                current = child.gameObject;
            }
            return current;
        }

        // ----- 名称相关 -----
        private string GenerateExampleName()
        {
            return $"{_prefix}_{_startIndex.ToString($"D{_digitCount}")}";
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

        public void Clear()
        {
            selectedResources.Clear();
            resourceRenameMap.Clear();
            _typeFoldouts.Clear();
            _folderFoldouts.Clear();
            _categoryAllSelected.Clear();
            _folderAllSelected.Clear();
            selectedSnapshotIndex = -1;
        }
    }
}
