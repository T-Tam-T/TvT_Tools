//[file name]: ExportModule.cs
//[file content begin]
﻿using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.IO;

namespace ResourceManager.Modules
{
    public class ExportModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> typeFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> folderFoldouts = new Dictionary<string, bool>();
        private Dictionary<Object, bool> selectedResources = new Dictionary<Object, bool>();
        private Dictionary<string, bool> categoryAllSelected = new Dictionary<string, bool>();
        private Dictionary<Object, string> resourceRenameMap = new Dictionary<Object, string>();

        // 批量重命名相关
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

        // 添加文件夹全选字典
        private Dictionary<string, bool> folderAllSelected = new Dictionary<string, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("资源整理", EditorStyles.boldLabel);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            // 目标文件夹设置
            DrawTargetFolderSettings();

            // 批量重命名面板
            DrawBulkRenamePanel();

            // 显示公用资源
            DrawCommonResources(session);

            // 显示每个分析对象的资源
            DrawObjectResources(session);

            // 操作按钮
            DrawActionButtons();
        }

        private void DrawTargetFolderSettings()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("目标文件夹设置", EditorStyles.boldLabel);

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

            // 添加材质球主贴图命名按钮
            EditorGUILayout.Space(5);
            if (GUILayout.Button("材质球按主贴图命名+序号"))
            {
                RenameMaterialsByMainTexture();
            }

            if (GUILayout.Button("应用重命名到选中资源"))
            {
                ApplyBulkRename();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }

        /// <summary>
        /// 将材质球按主贴图名称+序号重命名
        /// </summary>
        private void RenameMaterialsByMainTexture()
        {
            var selectedMaterials = selectedResources
                .Where(kvp => kvp.Value && kvp.Key is Material)
                .Select(kvp => kvp.Key as Material)
                .ToList();

            if (selectedMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要重命名的材质球", "确定");
                return;
            }

            // 按主贴图名称分组
            var materialsByTexture = new Dictionary<string, List<Material>>();
            var textureNameMapping = new Dictionary<Material, string>();

            foreach (var material in selectedMaterials)
            {
                string mainTextureName = GetMainTextureName(material);
                if (string.IsNullOrEmpty(mainTextureName))
                {
                    mainTextureName = "UnknownTexture";
                }

                if (!materialsByTexture.ContainsKey(mainTextureName))
                {
                    materialsByTexture[mainTextureName] = new List<Material>();
                }

                materialsByTexture[mainTextureName].Add(material);
                textureNameMapping[material] = mainTextureName;
            }

            // 为每个主贴图名称下的材质球添加序号
            foreach (var kvp in materialsByTexture)
            {
                string baseName = kvp.Key;
                var materials = kvp.Value;

                // 按材质球名称排序，保持一致性
                materials = materials.OrderBy(m => m.name).ToList();

                for (int i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    string newName;

                    if (materials.Count == 1)
                    {
                        newName = baseName;
                    }
                    else
                    {
                        newName = $"{baseName}_{(i + 1).ToString("D2")}";
                    }

                    // 更新重命名映射
                    if (resourceRenameMap.ContainsKey(material))
                    {
                        resourceRenameMap[material] = newName;
                    }
                    else
                    {
                        resourceRenameMap[material] = newName;
                    }
                }
            }

            Debug.Log($"已为 {selectedMaterials.Count} 个材质球应用主贴图命名规则");
        }

        /// <summary>
        /// 获取材质球的主贴图名称
        /// </summary>
        private string GetMainTextureName(Material material)
        {
            if (material == null || material.shader == null)
                return null;

            // 常见的主贴图属性名称
            string[] mainTextureProperties = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_Diffuse" };

            foreach (string propertyName in mainTextureProperties)
            {
                if (material.HasProperty(propertyName))
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture.name;
                    }
                }
            }

            // 如果没有找到主贴图，尝试获取第一个贴图
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);

            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture.name;
                    }
                }
            }

            return null;
        }

        private void DrawCommonResources(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            // 公用资源标题
            if (!objectFoldouts.ContainsKey("公用资源"))
            {
                objectFoldouts["公用资源"] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts["公用资源"] = true;

            EditorGUILayout.BeginVertical("box");
            objectFoldouts["公用资源"] = EditorGUILayout.Foldout(
                objectFoldouts["公用资源"], "公用资源", true);

            if (objectFoldouts["公用资源"])
            {
                // 按资源类型分组公用资源
                var commonResourcesByType = session.CommonResources.Keys
                    .Where(r => IsSupportedResourceType(r))
                    .Where(r => string.IsNullOrEmpty(SearchFilter) || r.name.ToLower().Contains(SearchFilter.ToLower()))
                    .GroupBy(r => r.GetType())
                    .OrderBy(g => GetResourceTypeDisplayName(g.Key));

                foreach (var typeGroup in commonResourcesByType)
                {
                    string typeName = GetResourceTypeDisplayName(typeGroup.Key);
                    string categoryKey = $"公用资源_{typeName}";

                    DrawResourceCategory(categoryKey, typeName, typeGroup.ToList(), session.CommonResources);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawObjectResources(AnalysisSession session)
        {
            foreach (var kvp in session.Analyzers)
            {
                var targetObject = kvp.Key;
                var analyzer = kvp.Value;
                string objectName = targetObject.name;

                if (!objectFoldouts.ContainsKey(objectName))
                {
                    objectFoldouts[objectName] = false;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    objectFoldouts[objectName] = true;

                EditorGUILayout.BeginVertical("box");
                objectFoldouts[objectName] = EditorGUILayout.Foldout(
                    objectFoldouts[objectName], objectName, true);

                if (objectFoldouts[objectName])
                {
                    // 按资源类型分组
                    var resourcesByType = analyzer.ResourceUsage.Keys
                        .Where(r => IsSupportedResourceType(r))
                        .Where(r => string.IsNullOrEmpty(SearchFilter) || r.name.ToLower().Contains(SearchFilter.ToLower()))
                        .GroupBy(r => r.GetType())
                        .OrderBy(g => GetResourceTypeDisplayName(g.Key));

                    foreach (var typeGroup in resourcesByType)
                    {
                        string typeName = GetResourceTypeDisplayName(typeGroup.Key);
                        string categoryKey = $"{objectName}_{typeName}";

                        DrawResourceCategory(categoryKey, typeName, typeGroup.ToList(), analyzer.ResourceUsage);
                    }
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
            }
        }

        private void DrawResourceCategory(string categoryKey, string typeName, List<Object> resources, Dictionary<Object, List<string>> resourceUsage)
        {
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUI.indentLevel++;

            // 分类标题和全选按钮
            GUILayout.BeginHorizontal();

            if (!typeFoldouts.ContainsKey(categoryKey))
            {
                typeFoldouts[categoryKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                typeFoldouts[categoryKey] = true;

            typeFoldouts[categoryKey] = EditorGUILayout.Foldout(typeFoldouts[categoryKey], typeName, true);

            GUILayout.FlexibleSpace();

            // 全选按钮
            if (!categoryAllSelected.ContainsKey(categoryKey))
            {
                categoryAllSelected[categoryKey] = false;
            }

            EditorGUI.BeginChangeCheck();
            categoryAllSelected[categoryKey] = EditorGUILayout.Toggle("全选", categoryAllSelected[categoryKey]);
            if (EditorGUI.EndChangeCheck())
            {
                SetCategorySelection(categoryKey, resources, categoryAllSelected[categoryKey]);
            }

            GUILayout.EndHorizontal();

            if (typeFoldouts[categoryKey])
            {
                // 按文件夹路径分组
                var resourcesByFolder = resources
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

                    // 搜索时自动展开
                    if (!string.IsNullOrEmpty(SearchFilter))
                        folderFoldouts[folderKey] = true;

                    EditorGUILayout.BeginVertical("box");
                    EditorGUI.indentLevel++;

                    // 文件夹标题和全选按钮
                    GUILayout.BeginHorizontal();

                    // 显示完整路径
                    folderFoldouts[folderKey] = EditorGUILayout.Foldout(
                        folderFoldouts[folderKey], folderPath, true);

                    GUILayout.FlexibleSpace();

                    // 文件夹全选按钮 - 新增
                    if (!folderAllSelected.ContainsKey(folderKey))
                    {
                        folderAllSelected[folderKey] = false;
                    }

                    EditorGUI.BeginChangeCheck();
                    folderAllSelected[folderKey] = EditorGUILayout.Toggle("全选", folderAllSelected[folderKey]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetFolderSelection(folderKey, folderGroup.ToList(), folderAllSelected[folderKey]);
                    }

                    GUILayout.EndHorizontal();

                    if (folderFoldouts[folderKey])
                    {
                        // 绘制文件夹内的资源
                        var resourceList = folderGroup.ToList();
                        for (int ri = 0; ri < resourceList.Count; ri++)
                        {
                            var resource = resourceList[ri];
                            using (new UIHelper.ZebraScope(ri))
                            {
                                DrawResourceItem(resource, resourceUsage.ContainsKey(resource) ? resourceUsage[resource] : new List<string>());
                            }
                        }
                    }

                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawResourceItem(Object resource, List<string> usagePaths)
        {
            // 初始化选择状态
            if (!selectedResources.ContainsKey(resource))
            {
                selectedResources[resource] = false;
            }

            EditorGUILayout.BeginHorizontal();

            // 选择状态显示框（只读）
            EditorGUILayout.Toggle(selectedResources[resource], GUILayout.Width(20));

            // 选择/取消选择按钮
            string buttonText = selectedResources[resource] ? "取消选择" : "选择";
            if (GUILayout.Button(buttonText, GUILayout.Width(60)))
            {
                selectedResources[resource] = !selectedResources[resource];
                GUI.changed = true;
            }

            // 应用按钮 - 直接将命名应用到资源
            if (GUILayout.Button("应用", GUILayout.Width(60)))
            {
                ApplyRenameForResource(resource);
            }

            // 资源名称（可编辑）
            if (!resourceRenameMap.ContainsKey(resource))
            {
                resourceRenameMap[resource] = resource.name;
            }

            // 名称文本框 - 自适应长度，最短为原来的1.5倍（150 * 1.5 = 225px）
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(
                resourceRenameMap[resource],
                GUILayout.MinWidth(225),
                GUILayout.ExpandWidth(true)
            );

            if (EditorGUI.EndChangeCheck())
            {
                resourceRenameMap[resource] = newName;
            }

            // 对象字段（资源框）- 放在最右边
            EditorGUILayout.ObjectField("", resource, resource.GetType(), false, GUILayout.Width(200));

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 将单个资源的重命名直接应用到 AssetDatabase
        /// </summary>
        private void ApplyRenameForResource(Object resource)
        {
            if (!resourceRenameMap.ContainsKey(resource))
                return;

            string newName = resourceRenameMap[resource];
            if (newName == resource.name)
            {
                EditorUtility.DisplayDialog("提示", "名称未改变，无需应用", "确定");
                return;
            }

            string path = AssetDatabase.GetAssetPath(resource);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("错误", "无法获取资源路径", "确定");
                return;
            }

            string error = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"重命名资源失败: {resource.name} -> {newName}, 错误: {error}");
                EditorUtility.DisplayDialog("错误", $"重命名失败: {error}", "确定");
            }
            else
            {
                Debug.Log($"成功重命名资源: {resource.name} -> {newName}");
            }
        }

        private void DrawActionButtons()
        {
            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("移动选中资源", GUILayout.Height(30)))
            {
                MoveSelectedResources();
            }

            if (GUILayout.Button("应用重命名", GUILayout.Height(30)))
            {
                ApplyRenaming();
            }

            if (GUILayout.Button("全局全选", GUILayout.Height(30)))
            {
                SelectAllResources();
            }

            if (GUILayout.Button("清除选择", GUILayout.Height(30)))
            {
                ClearSelection();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyBulkRename()
        {
            var selected = selectedResources.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要重命名的资源", "确定");
                return;
            }

            int currentNumber = startNumber;

            foreach (var resource in selected)
            {
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
                if (resourceRenameMap.ContainsKey(resource))
                {
                    resourceRenameMap[resource] = newName;
                }
                else
                {
                    resourceRenameMap[resource] = newName;
                }
            }

            Debug.Log($"已为 {selected.Count} 个资源应用批量重命名规则");
        }

        private void ApplyRenaming()
        {
            var resourcesToRename = resourceRenameMap
                .Where(kvp => kvp.Value != kvp.Key.name && selectedResources.ContainsKey(kvp.Key) && selectedResources[kvp.Key])
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

        private void SetCategorySelection(string categoryKey, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        /// <summary>
        /// 设置文件夹选择状态 - 新增方法
        /// </summary>
        private void SetFolderSelection(string folderKey, List<Object> resources, bool selected)
        {
            foreach (var resource in resources)
            {
                selectedResources[resource] = selected;
            }
        }

        private void MoveSelectedResources()
        {
            var selected = selectedResources.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
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

        private void SelectAllResources()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = true;
            }

            // 同时更新所有分类的全选状态
            foreach (var categoryKey in categoryAllSelected.Keys.ToList())
            {
                categoryAllSelected[categoryKey] = true;
            }

            // 同时更新所有文件夹的全选状态 - 新增
            foreach (var folderKey in folderAllSelected.Keys.ToList())
            {
                folderAllSelected[folderKey] = true;
            }
        }

        private void ClearSelection()
        {
            foreach (var resource in selectedResources.Keys.ToList())
            {
                selectedResources[resource] = false;
            }

            // 同时更新所有分类的全选状态
            foreach (var categoryKey in categoryAllSelected.Keys.ToList())
            {
                categoryAllSelected[categoryKey] = false;
            }

            // 同时更新所有文件夹的全选状态 - 新增
            foreach (var folderKey in folderAllSelected.Keys.ToList())
            {
                folderAllSelected[folderKey] = false;
            }
        }

        private bool IsSupportedResourceType(Object resource)
        {
            return resource is Material ||
                   resource is Mesh ||
                   resource is Texture2D ||
                   resource is AnimationClip;
        }

        private string GetResourceTypeDisplayName(System.Type type)
        {
            if (type == typeof(Material)) return "材质";
            if (type == typeof(Mesh)) return "模型";
            if (type == typeof(Texture2D)) return "贴图";
            if (type == typeof(AnimationClip)) return "动画";
            return type.Name;
        }

        public void Clear()
        {
            objectFoldouts.Clear();
            typeFoldouts.Clear();
            folderFoldouts.Clear();
            selectedResources.Clear();
            categoryAllSelected.Clear();
            resourceRenameMap.Clear();
            folderAllSelected.Clear(); // 新增清理
        }

        // 保持原有的单对象方法用于兼容
        public void Draw(ResourceAnalyzer analyzer, ResourceCache cache)
        {
            // 实现可以调用多对象版本或保持原有逻辑
        }
    }
}
//[file content end]