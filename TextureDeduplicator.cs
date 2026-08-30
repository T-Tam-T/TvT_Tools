using UnityEditor;
using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System;
using Object = UnityEngine.Object;
using UnityEditor.SceneManagement;

public class TextureDeduplicator : EditorWindow
{
    private Vector2 scrollPos;
    private List<TextureGroup> textureGroups = new List<TextureGroup>();
    private Dictionary<string, Texture2D> selectedReplacement = new Dictionary<string, Texture2D>();

    private enum CheckMode { MD5, 像素数据 }
    private CheckMode currentMode = CheckMode.MD5;
    private bool includeReadableCheck = true;
    private bool includeCompressedCheck = false;
    private bool showAdvancedOptions = false;
    private bool skipUnityTextures = true;

    private string status = "就绪";
    private float progress;
    private bool isProcessing;

    private string selectedPath = "Assets";
    private int textureCountInPath;
    private Vector2 folderScrollPos;

    private class TextureGroup
    {
        public Texture2D masterTexture; // 每组中的第一个贴图
        public List<Texture2D> duplicates = new List<Texture2D>(); // 该组的重复贴图
        public string hash; // 用于分组的哈希值
    }

    [MenuItem("Tools/TvTTools/贴图去重工具")]
    public static void ShowWindow()
    {
        GetWindow<TextureDeduplicator>("贴图去重工具");
    }

    private void OnEnable()
    {
        status = "点击'扫描贴图'开始";
        ScanTextureCountInPath();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawPathSelector();
        DrawSettingsPanel();
        DrawStatusBar();

        if (!isProcessing)
        {
            DrawTextureGroups();
        }
        else
        {
            DrawProgressView();
        }
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (GUILayout.Button("扫描贴图", EditorStyles.toolbarButton))
            {
                FindDuplicateTextures();
            }

            if (GUILayout.Button("替换选中重复项", EditorStyles.toolbarButton) && textureGroups.Count > 0)
            {
                ReplaceSelectedDuplicates();
            }

            if (GUILayout.Button("清除结果", EditorStyles.toolbarButton))
            {
                ClearResults();
            }

            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawPathSelector()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("扫描路径", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        {
            EditorGUILayout.LabelField("当前路径:", GUILayout.Width(60));
            EditorGUILayout.LabelField(selectedPath, EditorStyles.textField);

            if (GUILayout.Button("更改", GUILayout.Width(60)))
            {
                string newPath = EditorUtility.OpenFolderPanel("选择贴图扫描路径", selectedPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    // 转换为相对路径
                    if (newPath.StartsWith(Application.dataPath))
                    {
                        selectedPath = "Assets" + newPath.Substring(Application.dataPath.Length);
                        ScanTextureCountInPath();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("错误", "请选择项目内的路径", "确定");
                    }
                }
            }
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.LabelField($"包含贴图数量: {textureCountInPath}", EditorStyles.miniLabel);
        EditorGUILayout.Space();
    }

    private void ScanTextureCountInPath()
    {
        textureCountInPath = AssetDatabase.FindAssets("t:Texture2D", new[] { selectedPath }).Length;
    }

    private void DrawSettingsPanel()
    {
        EditorGUILayout.LabelField("检查设置", EditorStyles.boldLabel);

        // 检查模式选择
        EditorGUILayout.BeginHorizontal();
        {
            EditorGUILayout.LabelField("检查方式:", GUILayout.Width(70));
            currentMode = (CheckMode)EditorGUILayout.EnumPopup(currentMode, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            // 高级选项切换
            showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "高级选项", true);
        }
        EditorGUILayout.EndHorizontal();

        // 跳过Unity内置贴图
        skipUnityTextures = EditorGUILayout.Toggle("跳过Unity内置贴图", skipUnityTextures);

        // 高级选项
        if (showAdvancedOptions)
        {
            EditorGUI.indentLevel++;

            if (currentMode == CheckMode.MD5)
            {
                includeReadableCheck = EditorGUILayout.Toggle("包含可读贴图", includeReadableCheck);
                includeCompressedCheck = EditorGUILayout.Toggle("包含压缩贴图", includeCompressedCheck);

                EditorGUILayout.HelpBox(
                    "MD5模式: 通过文件内容哈希值检测重复贴图，速度快但可能忽略压缩格式不同的相同内容。",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "像素模式: 通过比较实际像素数据检测重复贴图，速度慢但结果精确。\n" +
                    "注意: 此模式需要贴图设置为可读(Read/Write Enabled)，否则将跳过检查。",
                    MessageType.Warning
                );
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        {
            GUILayout.Label($"状态: {status}");
            GUILayout.FlexibleSpace();

            if (isProcessing)
            {
                Rect rect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(200));
                EditorGUI.ProgressBar(rect, progress, $"处理中: {progress:P0}");

                if (GUILayout.Button("取消", GUILayout.Width(60)))
                {
                    isProcessing = false;
                    status = "操作已取消";
                }
            }
            else
            {
                int totalDuplicates = textureGroups.Sum(g => g.duplicates.Count);
                GUILayout.Label($"分组: {textureGroups.Count}  重复贴图: {totalDuplicates}");
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTextureGroups()
    {
        if (textureGroups.Count == 0)
        {
            GUILayout.Label("没有发现重复贴图分组", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        scrollPos = GUILayout.BeginScrollView(scrollPos);

        foreach (var group in textureGroups)
        {
            DrawTextureGroup(group);
        }

        GUILayout.EndScrollView();
    }

    private void DrawTextureGroup(TextureGroup group)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        {
            // 分组标题
            EditorGUILayout.LabelField($"重复分组 (哈希: {group.hash.Substring(0, 12)}...)", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            {
                // 左侧: 主贴图
                EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(position.width * 0.3f));
                {
                    EditorGUILayout.LabelField("主贴图 (保留)", EditorStyles.centeredGreyMiniLabel);
                    DrawTextureItem(group.masterTexture, false);
                }
                EditorGUILayout.EndVertical();

                // 右侧: 重复贴图
                EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(position.width * 0.7f - 20));
                {
                    EditorGUILayout.LabelField("重复贴图 (可删除)", EditorStyles.centeredGreyMiniLabel);

                    foreach (var duplicate in group.duplicates)
                    {
                        DrawTextureItem(duplicate, true);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);
    }

    private void DrawProgressView()
    {
        GUILayout.FlexibleSpace();

        Rect rect = GUILayoutUtility.GetRect(200, 20);
        rect.x = (position.width - 200) / 2;
        EditorGUI.ProgressBar(rect, progress, $"处理中: {progress:P0}");

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(100)))
            {
                isProcessing = false;
                status = "操作已取消";
            }
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
    }

    private void DrawTextureItem(Texture2D texture, bool isDuplicate)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        GUIStyle style = new GUIStyle(GUI.skin.button);

        if (isDuplicate)
        {
            style.normal.textColor = Color.red;
            style.hover.textColor = new Color(1, 0.5f, 0.5f);
        }

        GUILayout.BeginHorizontal(GUI.skin.box);
        {
            // 贴图预览
            EditorGUILayout.ObjectField(
                texture,
                typeof(Texture2D),
                false,
                GUILayout.Width(50),
                GUILayout.Height(50)
            );

            // 贴图信息
            GUILayout.BeginVertical();
            {
                GUILayout.Label(Path.GetFileName(path), style);
                GUILayout.Label($"尺寸: {texture.width}x{texture.height}", EditorStyles.miniLabel);
                GUILayout.Label($"格式: {texture.format}", EditorStyles.miniLabel);
                GUILayout.Label($"路径: {Path.GetDirectoryName(path)}", EditorStyles.miniLabel);

                // 显示贴图设置
                if (isDuplicate)
                {
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        GUILayout.Label($"压缩: {importer.textureCompression}", EditorStyles.miniLabel);
                        GUILayout.Label($"可读: {importer.isReadable}", EditorStyles.miniLabel);
                    }
                }
            }
            GUILayout.EndVertical();

            // 替换选择器（仅重复贴图）
            if (isDuplicate)
            {
                GUILayout.FlexibleSpace();

                selectedReplacement.TryGetValue(path, out Texture2D current);

                EditorGUI.BeginChangeCheck();
                Texture2D selected = (Texture2D)EditorGUILayout.ObjectField(
                    current != null ? current : texture,
                    typeof(Texture2D),
                    false,
                    GUILayout.Width(150)
                );

                if (EditorGUI.EndChangeCheck())
                {
                    if (selected != null)
                    {
                        selectedReplacement[path] = selected;
                    }
                    else
                    {
                        selectedReplacement.Remove(path);
                    }
                }

                // 快速选择主贴图按钮
                if (GUILayout.Button("使用主贴图", GUILayout.Width(80)))
                {
                    selectedReplacement[path] = textureGroups
                        .First(g => g.duplicates.Contains(texture))
                        .masterTexture;
                }
            }
        }
        GUILayout.EndHorizontal();
    }

    private void ClearResults()
    {
        textureGroups.Clear();
        selectedReplacement.Clear();
        status = "结果已清除";
    }

    private void FindDuplicateTextures()
    {
        try
        {
            isProcessing = true;
            status = "正在扫描贴图资源...";
            progress = 0;
            Repaint();

            textureGroups.Clear();
            selectedReplacement.Clear();

            string[] allTextures = AssetDatabase.FindAssets("t:Texture2D", new[] { selectedPath });
            Dictionary<string, TextureGroup> hashGroups = new Dictionary<string, TextureGroup>();
            int totalCount = allTextures.Length;
            int processed = 0;

            foreach (string guid in allTextures)
            {
                if (!isProcessing)
                {
                    status = "操作已取消";
                    return;
                }

                processed++;
                progress = (float)processed / totalCount;
                Repaint();

                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (texture == null) continue;

                // 跳过Unity内置贴图
                if (skipUnityTextures && IsUnityBuiltinTexture(path))
                {
                    continue;
                }

                string hash = "";
                if (currentMode == CheckMode.MD5)
                {
                    hash = CalculateTextureHash(path);
                }
                else
                {
                    hash = CalculatePixelHash(texture);
                    if (string.IsNullOrEmpty(hash))
                    {
                        Debug.LogWarning($"跳过不可读贴图: {path}");
                        continue;
                    }
                }

                if (!hashGroups.ContainsKey(hash))
                {
                    hashGroups[hash] = new TextureGroup
                    {
                        masterTexture = texture,
                        hash = hash
                    };
                }
                else
                {
                    hashGroups[hash].duplicates.Add(texture);
                }
            }

            // 只保留有重复的分组
            textureGroups = hashGroups.Values
                .Where(g => g.duplicates.Count > 0)
                .OrderByDescending(g => g.duplicates.Count)
                .ToList();

            status = $"扫描完成! 发现 {textureGroups.Count} 个重复分组";
        }
        catch (Exception e)
        {
            Debug.LogError($"贴图扫描错误: {e.Message}");
            status = $"错误: {e.Message}";
        }
        finally
        {
            isProcessing = false;
        }
    }

    private bool IsUnityBuiltinTexture(string path)
    {
        // 常见的Unity内置资源路径
        return path.Contains("Resources/unity_builtin_extra") ||
               path.Contains("Library/") ||
               path.Contains("DefaultResources") ||
               path.Contains("Built-in");
    }

    private string CalculateTextureHash(string path)
    {
        try
        {
            using (var md5 = MD5.Create())
            {
                byte[] data = File.ReadAllBytes(path);
                byte[] hash = md5.ComputeHash(data);
                return BitConverter.ToString(hash);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"计算MD5哈希失败: {path}\n{e.Message}");
            return Guid.NewGuid().ToString(); // 返回唯一值避免错误分组
        }
    }

    private string CalculatePixelHash(Texture2D texture)
    {
        // 检查贴图是否可读
        if (!texture.isReadable)
        {
            return null;
        }

        try
        {
            // 创建临时贴图确保可读性
            RenderTexture rt = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear
            );

            Graphics.Blit(texture, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tempTex = new Texture2D(texture.width, texture.height);
            tempTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tempTex.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            // 计算像素哈希
            using (var md5 = MD5.Create())
            {
                byte[] pixelData = tempTex.GetRawTextureData();
                byte[] hash = md5.ComputeHash(pixelData);
                DestroyImmediate(tempTex);
                return BitConverter.ToString(hash);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"计算像素哈希失败: {texture.name}\n{e.Message}");
            return null;
        }
    }

    private void ReplaceSelectedDuplicates()
    {
        try
        {
            if (textureGroups.Count == 0 || selectedReplacement.Count == 0)
            {
                EditorUtility.DisplayDialog("操作提示", "没有选择需要替换的重复贴图", "确定");
                return;
            }

            isProcessing = true;
            status = "正在替换重复贴图...";
            progress = 0;
            Repaint();

            int totalCount = selectedReplacement.Count;
            int processed = 0;
            int replacedCount = 0;
            int failedCount = 0;

            // 收集所有需要替换的贴图
            var replacements = new List<(Texture2D oldTex, Texture2D newTex)>();
            foreach (var kvp in selectedReplacement)
            {
                string path = kvp.Key;
                Texture2D oldTex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (oldTex != null && kvp.Value != null)
                {
                    replacements.Add((oldTex, kvp.Value));
                }
            }

            // 先处理所有引用替换
            foreach (var (oldTex, newTex) in replacements)
            {
                if (!isProcessing)
                {
                    status = "操作已取消";
                    return;
                }

                processed++;
                progress = (float)processed / totalCount;
                Repaint();

                string oldPath = AssetDatabase.GetAssetPath(oldTex);

                // 查找所有引用
                string[] dependencies = AssetDatabase.GetDependencies(oldPath, false);

                bool success = true;
                foreach (string dependency in dependencies)
                {
                    if (!ReplaceReferences(dependency, oldTex, newTex))
                    {
                        success = false;
                        failedCount++;
                    }
                    else
                    {
                        replacedCount++;
                    }
                }
            }

            // 再删除贴图资源
            foreach (var (oldTex, _) in replacements)
            {
                string oldPath = AssetDatabase.GetAssetPath(oldTex);
                if (AssetDatabase.DeleteAsset(oldPath))
                {
                    Debug.Log($"成功删除贴图: {oldPath}");
                }
                else
                {
                    Debug.LogError($"删除贴图失败: {oldPath}");
                    failedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            status = $"替换完成! 成功替换 {replacedCount} 个引用, {failedCount} 个失败";
            EditorUtility.DisplayDialog(
                "操作完成",
                $"贴图替换完成!\n成功替换引用: {replacedCount} 个\n失败: {failedCount} 个",
                "确定"
            );

            // 重新扫描更新分组
            FindDuplicateTextures();
        }
        catch (Exception e)
        {
            Debug.LogError($"替换操作失败: {e.Message}\n{e.StackTrace}");
            status = $"错误: {e.Message}";
            EditorUtility.DisplayDialog("错误", $"替换操作失败: {e.Message}", "确定");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private bool ReplaceReferences(string assetPath, Texture2D oldTex, Texture2D newTex)
    {
        try
        {
            bool changed = false;

            // 处理材质
            if (assetPath.EndsWith(".mat"))
            {
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                changed = ReplaceMaterialReferences(mat, oldTex, newTex);
            }
            // 处理预制体
            else if (assetPath.EndsWith(".prefab"))
            {
                GameObject prefab = PrefabUtility.LoadPrefabContents(assetPath);
                changed = ReplaceInGameObjects(prefab, oldTex, newTex);

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefab, assetPath);
                }

                PrefabUtility.UnloadPrefabContents(prefab);
            }
            // 处理场景
            else if (assetPath.EndsWith(".unity"))
            {
                // 注意: 场景处理需要更复杂的实现
                // 这里简化为仅处理当前打开场景
                foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (ReplaceInGameObjects(go, oldTex, newTex))
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
            }

            return changed;
        }
        catch (Exception e)
        {
            Debug.LogError($"替换引用失败: {assetPath}\n{e.Message}");
            return false;
        }
    }

    private bool ReplaceMaterialReferences(Material mat, Texture2D oldTex, Texture2D newTex)
    {
        if (mat == null) return false;

        bool changed = false;
        SerializedObject so = new SerializedObject(mat);
        SerializedProperty prop = so.FindProperty("m_SavedProperties");
        SerializedProperty texProps = prop.FindPropertyRelative("m_TexEnvs");

        for (int i = 0; i < texProps.arraySize; i++)
        {
            SerializedProperty texProp = texProps.GetArrayElementAtIndex(i)
                .FindPropertyRelative("second")
                .FindPropertyRelative("m_Texture");

            if (texProp.objectReferenceValue == oldTex)
            {
                Debug.Log($"在材质 {mat.name} 中将 {oldTex.name} 替换为 {newTex.name}");
                texProp.objectReferenceValue = newTex;
                changed = true;
            }
        }

        if (changed)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mat);
        }

        return changed;
    }

    private bool ReplaceInGameObjects(GameObject go, Texture2D oldTex, Texture2D newTex)
    {
        bool changed = false;

        // 处理所有渲染器
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterials == null) continue;

            Material[] materials = renderer.sharedMaterials;
            bool materialsChanged = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;

                // 创建材质实例副本，避免修改原始材质
                Material newMaterial = new Material(mat);
                bool matChanged = false;

                // 获取材质的所有贴图属性
                int propertyCount = ShaderUtil.GetPropertyCount(mat.shader);
                for (int p = 0; p < propertyCount; p++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, p) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(mat.shader, p);
                        if (mat.GetTexture(propName) == oldTex)
                        {
                            Debug.Log($"在材质 {mat.name} 的属性 {propName} 中将 {oldTex.name} 替换为 {newTex.name}");
                            newMaterial.SetTexture(propName, newTex);
                            matChanged = true;
                        }
                    }
                }

                if (matChanged)
                {
                    materials[i] = newMaterial;
                    materialsChanged = true;
                    changed = true;
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (materialsChanged)
            {
                renderer.sharedMaterials = materials;
            }
        }

        // 处理UI贴图
        UnityEngine.UI.Image[] uiImages = go.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var image in uiImages)
        {
            if (image.sprite != null && image.sprite.texture == oldTex)
            {
                // 查找新贴图对应的精灵
                string spritePath = AssetDatabase.GetAssetPath(newTex);
                Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>().ToArray();

                if (sprites.Length > 0)
                {
                    // 尝试匹配同名精灵
                    string oldName = image.sprite.name;
                    Sprite newSprite = sprites.FirstOrDefault(s => s.name == oldName);

                    if (newSprite == null)
                    {
                        newSprite = sprites[0];
                    }

                    Debug.Log($"在UI Image中将 {image.sprite.name} 替换为 {newSprite.name}");
                    image.sprite = newSprite;
                    changed = true;
                    EditorUtility.SetDirty(image);
                }
            }
        }

        // 处理RawImage
        UnityEngine.UI.RawImage[] rawImages = go.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
        foreach (var rawImage in rawImages)
        {
            if (rawImage.texture == oldTex)
            {
                Debug.Log($"在RawImage中将 {oldTex.name} 替换为 {newTex.name}");
                rawImage.texture = newTex;
                changed = true;
                EditorUtility.SetDirty(rawImage);
            }
        }

        return changed;
    }
}