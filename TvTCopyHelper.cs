using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// ============================================================
// 设置数据类 — 通过 EditorPrefs 持久化
// ============================================================
/// <summary>
/// TvT 智能复制的全局设置。所有值通过 EditorPrefs 持久化，跨 Unity 会话保留。
/// </summary>
public static class TvTCopySettings
{
    private const string KEY_TEX_SUFFIX   = "TvTCopyHelper_TexSuffix";
    private const string KEY_TEX_POSITION = "TvTCopyHelper_TexPosition";  // 0=追加, 1=前置
    private const string KEY_DIGITS       = "TvTCopyHelper_Digits";       // 统一序号位数 1~4
    private const string KEY_USE_PARENT   = "TvTCopyHelper_UseParentFolder";
    private const string KEY_CUSTOM_NAME  = "TvTCopyHelper_CustomName";

    /// <summary>贴图后缀标记，默认 "_T_"</summary>
    public static string TexSuffix
    {
        get => EditorPrefs.GetString(KEY_TEX_SUFFIX, "_T_");
        set => EditorPrefs.SetString(KEY_TEX_SUFFIX, string.IsNullOrEmpty(value) ? "_T_" : value);
    }

    /// <summary>贴图标记位置：false=追加到末尾，true=添加到开头</summary>
    public static bool TexPrepend
    {
        get => EditorPrefs.GetBool(KEY_TEX_POSITION, false);
        set => EditorPrefs.SetBool(KEY_TEX_POSITION, value);
    }

    /// <summary>统一序号位数（1~4），贴图和材质共用，默认 2</summary>
    public static int Digits
    {
        get => Mathf.Clamp(EditorPrefs.GetInt(KEY_DIGITS, 2), 1, 4);
        set => EditorPrefs.SetInt(KEY_DIGITS, Mathf.Clamp(value, 1, 4));
    }

    /// <summary>材质命名：true=根据父文件夹拼音首字母，false=使用自定义名称</summary>
    public static bool UseParentFolder
    {
        get => EditorPrefs.GetBool(KEY_USE_PARENT, true);
        set => EditorPrefs.SetBool(KEY_USE_PARENT, value);
    }

    /// <summary>自定义材质前缀名称（仅当 UseParentFolder=false 时生效）</summary>
    public static string CustomName
    {
        get => EditorPrefs.GetString(KEY_CUSTOM_NAME, "");
        set => EditorPrefs.SetString(KEY_CUSTOM_NAME, value ?? "");
    }
}

// ============================================================
// 设置窗口
// ============================================================
/// <summary>
/// TvT 智能复制 — 设置窗口。
/// 菜单入口：Assets/TvTTools/TvT 复制设置
/// </summary>
public class TvTCopySettingsWindow : EditorWindow
{
    private string _texSuffix;
    private int    _texPosIndex;     // 0=追加, 1=前置
    private int    _digits;
    private bool   _useParentFolder;
    private string _customName;

    [MenuItem("Assets/TvTTools/TvT 复制设置", false, 99)]
    public static void ShowWindow()
    {
        var win = GetWindow<TvTCopySettingsWindow>("TvT 复制设置");
        win.minSize = new Vector2(380, 340);
        win.maxSize = new Vector2(500, 460);
        win.Show();
    }

    private void OnEnable()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        _texSuffix       = TvTCopySettings.TexSuffix;
        _texPosIndex     = TvTCopySettings.TexPrepend ? 1 : 0;
        _digits          = TvTCopySettings.Digits;
        _useParentFolder = TvTCopySettings.UseParentFolder;
        _customName      = TvTCopySettings.CustomName;
    }

    private void OnGUI()
    {
        // ── 贴图复制设置 ──
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("贴图复制设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // 追加字符
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("追加字符", GUILayout.Width(80));
        GUI.SetNextControlName("TexSuffixField");
        _texSuffix = EditorGUILayout.TextField(_texSuffix, GUILayout.Width(120));
        if (string.IsNullOrEmpty(_texSuffix))
        {
            // 模拟 placeholder
            var rect = GUILayoutUtility.GetLastRect();
            if (GUI.GetNameOfFocusedControl() != "TexSuffixField")
            {
                var placeholderStyle = new GUIStyle(EditorStyles.textField)
                {
                    normal = { textColor = Color.gray }
                };
                EditorGUI.LabelField(rect, "_T_", placeholderStyle);
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);

        // 追加序号
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("追加序号", GUILayout.Width(80));
        _digits = EditorGUILayout.IntSlider(_digits, 1, 4);
        EditorGUILayout.LabelField($"位数: {_digits}", GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);

        // 模式切换
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("标记位置", GUILayout.Width(80));
        _texPosIndex = EditorGUILayout.Popup(_texPosIndex, new[] { "追加到末尾", "添加到开头" }, GUILayout.Width(140));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // 预览
        string digit01 = FormatDigits(1);
        string digit02 = FormatDigits(2);
        string suffix  = _texSuffix;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.LabelField("  命名预览：", EditorStyles.miniBoldLabel);
        if (_texPosIndex == 0) // 追加模式
        {
            EditorGUILayout.LabelField($"    追加模式：myTexture{suffix}{digit01}.png");
            EditorGUILayout.LabelField($"    前置模式：{suffix}myTexture{digit01}.png");
        }
        else
        {
            EditorGUILayout.LabelField($"    追加模式：myTexture{suffix}{digit01}.png");
            EditorGUILayout.LabelField($"    前置模式：{suffix}myTexture{digit01}.png");
        }
        EditorGUILayout.LabelField($"    已有 {suffix} 时递增：myTexture{suffix}{digit02}.png");
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(12);

        // ── 材质命名设置 ──
        EditorGUILayout.LabelField("材质命名设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // 复选框：根据父文件夹首字母
        EditorGUILayout.BeginHorizontal();
        _useParentFolder = EditorGUILayout.Toggle("根据父文件夹首字母", _useParentFolder, GUILayout.Width(200));
        // 显示当前生效的预览
        if (_useParentFolder)
        {
            EditorGUILayout.LabelField("例如: fr" + "_" + digit01, EditorStyles.miniLabel, GUILayout.Width(80));
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);

        // 自定义名称（仅当复选框未勾选时可用）
        EditorGUI.BeginDisabledGroup(_useParentFolder);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("自定义名称", GUILayout.Width(80));
        _customName = EditorGUILayout.TextField(_customName, GUILayout.Width(140));
        if (!_useParentFolder)
        {
            string previewName = string.IsNullOrEmpty(_customName) ? "MyMat" : _customName;
            EditorGUILayout.LabelField($"{previewName}" + "_" + digit01, EditorStyles.miniLabel, GUILayout.Width(100));
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(16);

        // ── 按钮 ──
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("保存设置", GUILayout.Height(28)))
        {
            SaveSettings();
            EditorUtility.DisplayDialog("TvT 复制设置", "设置已保存。", "确定");
        }
        if (GUILayout.Button("恢复默认", GUILayout.Height(28)))
        {
            TvTCopySettings.TexSuffix      = "_T_";
            TvTCopySettings.TexPrepend     = false;
            TvTCopySettings.Digits         = 2;
            TvTCopySettings.UseParentFolder = true;
            TvTCopySettings.CustomName     = "";
            LoadSettings();
            Repaint();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void SaveSettings()
    {
        TvTCopySettings.TexSuffix      = _texSuffix;
        TvTCopySettings.TexPrepend     = _texPosIndex == 1;
        TvTCopySettings.Digits         = _digits;
        TvTCopySettings.UseParentFolder = _useParentFolder;
        TvTCopySettings.CustomName     = _customName;
    }

    private string FormatDigits(int val)
    {
        return _digits switch
        {
            1 => val.ToString("D1"),
            2 => val.ToString("D2"),
            3 => val.ToString("D3"),
            4 => val.ToString("D4"),
            _ => val.ToString("D2"),
        };
    }
}

// ============================================================
// 主工具类
// ============================================================
public class TvTCopyHelper
{
    private const string TVT_MATERIALS_BASE = "Assets/_TvT/Materials/";
    private const string MAT_SEPARATOR = "_";

    // ============================================================
    // MenuItem 入口
    // ============================================================

    [MenuItem("Assets/TvTTools/TvT 智能复制 %#d", false, 20)]
    private static void SmartDuplicate()
    {
        if (IsProjectWindowFocused())
            DuplicateSelectedAssets(null);
        else if (IsHierarchyWindowFocused())
            DuplicateSelectedParticles(null);
    }

    [MenuItem("Assets/TvTTools/TvT 智能复制 %#d", true)]
    private static bool ValidateSmartDuplicate()
    {
        if (IsProjectWindowFocused())
        {
            return Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets)
                .Any(obj => IsMaterial(AssetDatabase.GetAssetPath(obj)) || IsTexture(AssetDatabase.GetAssetPath(obj)));
        }
        if (IsHierarchyWindowFocused())
        {
            return Selection.gameObjects.Any(go => go.GetComponentInChildren<ParticleSystem>(true) != null);
        }
        return false;
    }

    [MenuItem("Assets/TvTTools/TvT 智能复制到目录 %#&d", false, 21)]
    private static void SmartDuplicateToDirectory()
    {
        string absPath = EditorUtility.OpenFolderPanel("选择目标目录", Application.dataPath, "");
        if (string.IsNullOrEmpty(absPath)) return;

        string dataPath = Application.dataPath.Replace("\\", "/");
        string absPathNormalized = absPath.Replace("\\", "/");
        if (!absPathNormalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[TvT 智能复制] 选定的目录不在工程 Assets 下。");
            return;
        }
        string targetDir = "Assets" + absPathNormalized.Substring(dataPath.Length);

        EnsureDirectoryExists(targetDir);

        if (IsProjectWindowFocused())
            DuplicateSelectedAssets(targetDir);
        else if (IsHierarchyWindowFocused())
            DuplicateSelectedParticles(targetDir);
    }

    [MenuItem("Assets/TvTTools/TvT 智能复制到目录 %#&d", true)]
    private static bool ValidateSmartDuplicateToDirectory()
    {
        return ValidateSmartDuplicate();
    }

    [MenuItem("GameObject/TvTTools/TvT 智能复制粒子", false, 20)]
    private static void SmartDuplicateParticles() { DuplicateSelectedParticles(null); }

    [MenuItem("GameObject/TvTTools/TvT 智能复制粒子到目录", false, 21)]
    private static void SmartDuplicateParticlesToDirectory()
    {
        string absPath = EditorUtility.OpenFolderPanel("选择材质目标目录", Application.dataPath, "");
        if (string.IsNullOrEmpty(absPath)) return;

        string dataPath = Application.dataPath.Replace("\\", "/");
        string absPathNormalized = absPath.Replace("\\", "/");
        if (!absPathNormalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[TvT 智能复制粒子] 选择的目录不在工程 Assets 下。");
            return;
        }
        string targetDir = "Assets" + absPathNormalized.Substring(dataPath.Length);
        EnsureDirectoryExists(targetDir);
        DuplicateSelectedParticles(targetDir);
    }

    [MenuItem("GameObject/TvTTools/TvT 智能复制粒子", true)]
    [MenuItem("GameObject/TvTTools/TvT 智能复制粒子到目录", true)]
    private static bool ValidateParticleMenus()
    {
        return IsHierarchyWindowFocused() &&
               Selection.gameObjects.Any(go => go.GetComponentInChildren<ParticleSystem>(true) != null);
    }

    // ============================================================
    // 工具方法
    // ============================================================

    private static bool IsMaterial(string path) => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);

    private static bool IsTexture(string path)
    {
        Type t = AssetDatabase.GetMainAssetTypeAtPath(path);
        return t != null && typeof(Texture).IsAssignableFrom(t);
    }

    private static bool IsProjectWindowFocused()
    {
        var focused = EditorWindow.focusedWindow;
        return focused != null && focused.GetType().Name == "ProjectBrowser";
    }

    private static bool IsHierarchyWindowFocused()
    {
        var focused = EditorWindow.focusedWindow;
        return focused != null && focused.GetType().Name == "SceneHierarchyWindow";
    }

    private static void EnsureDirectoryExists(string assetDir)
    {
        string absDir = Path.Combine(Application.dataPath, assetDir.Substring("Assets/".Length));
        if (!Directory.Exists(absDir))
        {
            Directory.CreateDirectory(absDir);
            AssetDatabase.Refresh();
        }
    }

    /// <summary>格式化数字到指定位数</summary>
    private static string FormatNum(int value, int digits)
    {
        return digits switch
        {
            1 => value.ToString("D1"),
            2 => value.ToString("D2"),
            3 => value.ToString("D3"),
            4 => value.ToString("D4"),
            _ => value.ToString("D2"),
        };
    }

    /// <summary>获取材质命名前缀：根据设置返回拼音首字母或自定义名称</summary>
    private static string GetMaterialPrefix(string parentFolder)
    {
        if (TvTCopySettings.UseParentFolder)
        {
            return PinyinHelper.GetInitials(parentFolder);
        }
        else
        {
            string custom = TvTCopySettings.CustomName;
            if (string.IsNullOrEmpty(custom))
                return "mat";   // 兜底
            return custom;
        }
    }

    // ============================================================
    // Project 窗口复制
    // ============================================================

    private static void DuplicateSelectedAssets(string targetDir)
    {
        var selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        int copiedCount = 0;
        foreach (var obj in selected)
        {
            string sourcePath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(sourcePath)) continue;

            if (IsMaterial(sourcePath))
            {
                if (string.IsNullOrEmpty(targetDir))
                {
                    if (DuplicateMaterial(sourcePath)) copiedCount++;
                }
                else
                {
                    if (DuplicateMaterialToDir(sourcePath, targetDir)) copiedCount++;
                }
            }
            else if (IsTexture(sourcePath))
            {
                if (string.IsNullOrEmpty(targetDir))
                {
                    if (DuplicateTexture(sourcePath)) copiedCount++;
                }
                else
                {
                    if (DuplicateTextureToDir(sourcePath, targetDir)) copiedCount++;
                }
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TvT 智能复制] 完成，共复制 {copiedCount} 个资源。");
    }

    // ============================================================
    // 粒子复制
    // ============================================================

    private static void DuplicateSelectedParticles(string targetDir)
    {
        var selected = Selection.gameObjects;
        int totalMaterials = 0;
        var newSelection = new List<GameObject>();

        foreach (var go in selected)
        {
            GameObject newGo = DuplicateParticleHierarchy(go, targetDir, out int matCount);
            if (newGo != null)
            {
                newSelection.Add(newGo);
                totalMaterials += matCount;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (newSelection.Count > 0)
            Selection.objects = newSelection.ToArray();

        Debug.Log($"[TvT 智能复制粒子] 完成，复制 {newSelection.Count} 个粒子系统，{totalMaterials} 个材质。");
    }

    // ============================================================
    // 材质复制（原地）
    // ============================================================

    private static bool DuplicateMaterial(string sourcePath)
    {
        string normalizedPath = sourcePath.Replace("\\", "/");

        // 从材质当前路径提取父文件夹名作为命名来源
        string dirPath = Path.GetDirectoryName(normalizedPath).Replace("\\", "/");
        string parentFolder = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(parentFolder))
        {
            Debug.LogWarning($"[TvT 智能复制] 无法获取材质 '{sourcePath}' 的父文件夹名称，跳过。");
            return false;
        }

        string prefix = GetMaterialPrefix(parentFolder);
        if (string.IsNullOrEmpty(prefix))
        {
            Debug.LogWarning($"[TvT 智能复制] 无法为文件夹 '{parentFolder}' 生成命名前缀，跳过。");
            return false;
        }

        int digits = TvTCopySettings.Digits;
        int nextNum = FindNextMaterialNumber(dirPath, prefix);
        string targetName = $"{prefix}{MAT_SEPARATOR}{FormatNum(nextNum, digits)}";
        string targetPath = $"{dirPath}/{targetName}.mat";

        targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 无法生成唯一路径: {sourcePath}");
            return false;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 复制材质失败: {sourcePath} → {targetPath}");
            return false;
        }

        Debug.Log($"[TvT 智能复制] 材质: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)}");
        return true;
    }

    private static int FindNextMaterialNumber(string dirPath, string prefix)
    {
        int maxNum = 0;
        string absoluteDir = Path.Combine(Application.dataPath, dirPath.Substring("Assets/".Length));
        if (!Directory.Exists(absoluteDir)) return 1;

        string escapedPrefix = Regex.Escape(prefix);
        var pattern = new Regex($"^{escapedPrefix}{Regex.Escape(MAT_SEPARATOR)}(\\d+)\\.mat$", RegexOptions.IgnoreCase);

        foreach (string file in Directory.GetFiles(absoluteDir, "*.mat"))
        {
            string fileName = Path.GetFileName(file);
            var match = pattern.Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            {
                if (num > maxNum) maxNum = num;
            }
        }
        return maxNum + 1;
    }

    // ============================================================
    // 贴图复制（原地）
    // ============================================================

    private static bool DuplicateTexture(string sourcePath)
    {
        string normalizedPath = sourcePath.Replace("\\", "/");
        string dir  = Path.GetDirectoryName(normalizedPath).Replace("\\", "/");
        string ext  = Path.GetExtension(normalizedPath);
        string baseName = Path.GetFileNameWithoutExtension(normalizedPath);

        string targetName = BuildTextureTargetName(dir, baseName, ext);
        string targetPath = $"{dir}/{targetName}{ext}";
        targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 无法生成唯一路径: {sourcePath}");
            return false;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 复制贴图失败: {sourcePath} → {targetPath}");
            return false;
        }

        Debug.Log($"[TvT 智能复制] 贴图: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)}");
        return true;
    }

    /// <summary>
    /// 构建贴图命名。根据设置决定前后缀位置、后缀标记、序号位数。
    /// 如果名称已包含设定后缀，则只递增序号。
    /// </summary>
    private static string BuildTextureTargetName(string dirPath, string baseName, string ext)
    {
        string suffix  = TvTCopySettings.TexSuffix;
        bool   prepend = TvTCopySettings.TexPrepend;
        int    digits  = TvTCopySettings.Digits;
        string escapedSuffix = Regex.Escape(suffix);

        // 检测名称是否已包含后缀+序号
        var suffixRegex = new Regex($"{escapedSuffix}(\\d+)$", RegexOptions.IgnoreCase);
        var match = suffixRegex.Match(baseName);

        if (match.Success)
        {
            string nameWithoutSuffix = baseName.Substring(0, match.Index);
            int nextNum = FindNextTexNumber(dirPath, nameWithoutSuffix, suffix, ext, prepend);
            return prepend
                ? $"{suffix}{nameWithoutSuffix}{FormatNum(nextNum, digits)}"
                : $"{nameWithoutSuffix}{suffix}{FormatNum(nextNum, digits)}";
        }
        else
        {
            int nextNum = FindNextTexNumber(dirPath, baseName, suffix, ext, prepend);
            return prepend
                ? $"{suffix}{baseName}{FormatNum(nextNum, digits)}"
                : $"{baseName}{suffix}{FormatNum(nextNum, digits)}";
        }
    }

    private static int FindNextTexNumber(string dirPath, string baseName, string suffix, string ext, bool prepend)
    {
        int maxNum = 0;
        string absoluteDir = Path.Combine(Application.dataPath, dirPath.Substring("Assets/".Length));
        if (!Directory.Exists(absoluteDir)) return 1;

        string escapedBase   = Regex.Escape(baseName);
        string escapedSuffix = Regex.Escape(suffix);
        string escapedExt    = Regex.Escape(ext);

        string pattern;
        if (prepend)
            pattern = $"^{escapedSuffix}{escapedBase}(\\d+){escapedExt}$";
        else
            pattern = $"^{escapedBase}{escapedSuffix}(\\d+){escapedExt}$";

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (string file in Directory.GetFiles(absoluteDir))
        {
            string fileName = Path.GetFileName(file);
            var m = regex.Match(fileName);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int num))
            {
                if (num > maxNum) maxNum = num;
            }
        }
        return maxNum + 1;
    }

    // ============================================================
    // 材质复制到目录
    // ============================================================

    private static bool DuplicateMaterialToDir(string sourcePath, string targetDir)
    {
        string normalizedTarget = targetDir.Replace("\\", "/").TrimEnd('/');
        EnsureDirectoryExists(normalizedTarget);

        string parentFolder = Path.GetFileName(normalizedTarget);
        if (string.IsNullOrEmpty(parentFolder))
        {
            Debug.LogWarning($"[TvT 智能复制] 无法获取目标文件夹名: {targetDir}");
            return false;
        }

        string prefix = GetMaterialPrefix(parentFolder);
        if (string.IsNullOrEmpty(prefix))
        {
            Debug.LogWarning($"[TvT 智能复制] 无法为文件夹 '{parentFolder}' 生成命名前缀，跳过。");
            return false;
        }

        int digits = TvTCopySettings.Digits;
        int nextNum = FindNextMaterialNumber(normalizedTarget, prefix);
        string targetName = $"{prefix}{MAT_SEPARATOR}{FormatNum(nextNum, digits)}";
        string targetPath = $"{normalizedTarget}/{targetName}.mat";
        targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 无法生成唯一路径: {sourcePath}");
            return false;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 复制材质失败: {sourcePath} → {targetPath}");
            return false;
        }

        Debug.Log($"[TvT 智能复制] 材质: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)} @ {normalizedTarget}");
        return true;
    }

    // ============================================================
    // 贴图复制到目录
    // ============================================================

    private static bool DuplicateTextureToDir(string sourcePath, string targetDir)
    {
        string normalizedTarget = targetDir.Replace("\\", "/").TrimEnd('/');
        EnsureDirectoryExists(normalizedTarget);

        string ext      = Path.GetExtension(sourcePath);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);

        string targetName = BuildTextureTargetName(normalizedTarget, baseName, ext);
        string targetPath = $"{normalizedTarget}/{targetName}{ext}";
        targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 无法生成唯一路径: {sourcePath}");
            return false;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
        {
            Debug.LogError($"[TvT 智能复制] 复制贴图失败: {sourcePath} → {targetPath}");
            return false;
        }

        Debug.Log($"[TvT 智能复制] 贴图: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)} @ {normalizedTarget}");
        return true;
    }

    // ============================================================
    // 粒子 Hierarchy 复制
    // ============================================================

    private static GameObject DuplicateParticleHierarchy(GameObject sourceGo, string targetDir, out int matCount)
    {
        matCount = 0;

        GameObject newGo = UnityEngine.Object.Instantiate(sourceGo, sourceGo.transform.parent);
        newGo.name = GameObjectUtility.GetUniqueNameForSibling(sourceGo.transform.parent, sourceGo.name);
        Undo.RegisterCreatedObjectUndo(newGo, "TvT 智能复制粒子");

        var sourceRenderers = sourceGo.GetComponentsInChildren<ParticleSystemRenderer>(true);
        var newRenderers = newGo.GetComponentsInChildren<ParticleSystemRenderer>(true);

        if (sourceRenderers.Length == 0) return newGo;

        var materialMap = new Dictionary<Material, Material>();

        foreach (var renderer in sourceRenderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null || materialMap.ContainsKey(mat)) continue;

                string matPath = AssetDatabase.GetAssetPath(mat);
                if (string.IsNullOrEmpty(matPath) || !IsMaterial(matPath))
                {
                    materialMap[mat] = mat;
                    continue;
                }

                string newPath = DuplicateMaterialForParticle(matPath, targetDir);
                if (!string.IsNullOrEmpty(newPath))
                {
                    materialMap[mat] = AssetDatabase.LoadAssetAtPath<Material>(newPath);
                    matCount++;
                }
                else
                {
                    materialMap[mat] = mat;
                }
            }
        }

        for (int i = 0; i < sourceRenderers.Length && i < newRenderers.Length; i++)
        {
            var sourceMats = sourceRenderers[i].sharedMaterials;
            var newMats = new Material[sourceMats.Length];
            bool changed = false;

            for (int j = 0; j < sourceMats.Length; j++)
            {
                if (sourceMats[j] != null && materialMap.TryGetValue(sourceMats[j], out var newMat))
                {
                    newMats[j] = newMat;
                    changed = true;
                }
                else
                {
                    newMats[j] = sourceMats[j];
                }
            }

            if (changed)
            {
                Undo.RecordObject(newRenderers[i], "Assign Copied Materials");
                newRenderers[i].sharedMaterials = newMats;
                EditorUtility.SetDirty(newRenderers[i]);
            }
        }

        return newGo;
    }

    private static string DuplicateMaterialForParticle(string sourcePath, string targetDir)
    {
        string normalizedPath = sourcePath.Replace("\\", "/");
        int digits = TvTCopySettings.Digits;

        if (string.IsNullOrEmpty(targetDir))
        {
            if (normalizedPath.StartsWith(TVT_MATERIALS_BASE, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = normalizedPath.Substring(TVT_MATERIALS_BASE.Length);
                int slashIdx = relativePath.IndexOf('/');
                if (slashIdx < 0) return null;

                string parentFolder = relativePath.Substring(0, slashIdx);
                string prefix = GetMaterialPrefix(parentFolder);
                if (string.IsNullOrEmpty(prefix)) return null;

                string dirPath = TVT_MATERIALS_BASE + parentFolder;
                int nextNum = FindNextMaterialNumber(dirPath, prefix);
                string targetName = $"{prefix}{MAT_SEPARATOR}{FormatNum(nextNum, digits)}";
                string targetPath = $"{dirPath}/{targetName}.mat";
                targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
                if (string.IsNullOrEmpty(targetPath)) return null;

                if (AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    Debug.Log($"[TvT 智能复制粒子] 材质: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)}");
                    return targetPath;
                }
                return null;
            }
            else
            {
                // 不在 _TvT/Materials/ 下，使用当前文件夹名作为命名前缀
                string dir = Path.GetDirectoryName(normalizedPath).Replace("\\", "/");
                string parentFolder = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(parentFolder)) return null;

                string prefix = GetMaterialPrefix(parentFolder);
                if (string.IsNullOrEmpty(prefix)) prefix = "mat";

                int nextNum = FindNextMaterialNumber(dir, prefix);
                string targetName = $"{prefix}{MAT_SEPARATOR}{FormatNum(nextNum, digits)}";
                string targetPath = $"{dir}/{targetName}.mat";
                targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
                if (string.IsNullOrEmpty(targetPath)) return null;

                if (AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    Debug.Log($"[TvT 智能复制粒子] 材质: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)}");
                    return targetPath;
                }
                return null;
            }
        }
        else
        {
            string normalizedTarget = targetDir.Replace("\\", "/").TrimEnd('/');
            EnsureDirectoryExists(normalizedTarget);

            string parentFolder = Path.GetFileName(normalizedTarget);
            if (string.IsNullOrEmpty(parentFolder)) return null;

            string prefix = GetMaterialPrefix(parentFolder);
            if (string.IsNullOrEmpty(prefix)) prefix = "mat";

            int nextNum = FindNextMaterialNumber(normalizedTarget, prefix);
            string targetName = $"{prefix}{MAT_SEPARATOR}{FormatNum(nextNum, digits)}";
            string targetPath = $"{normalizedTarget}/{targetName}.mat";
            targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            if (string.IsNullOrEmpty(targetPath)) return null;

            if (AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                Debug.Log($"[TvT 智能复制粒子] 材质: {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)} @ {normalizedTarget}");
                return targetPath;
            }
            return null;
        }
    }

    // ============================================================
    // 拼音首字母提取工具
    // ============================================================

    private static class PinyinHelper
    {
        private static readonly Dictionary<char, char> pinyinMap;

        static PinyinHelper()
        {
            pinyinMap = new Dictionary<char, char>(4096);
            AddRange('a', "啊阿埃挨哎唉哀皑癌蔼矮艾碍爱隘鞍氨安俺按暗岸胺案肮昂盎凹敖熬翱袄傲奥懊澳");
            AddRange('b', "芭捌扒叭吧笆疤拔靶把坝霸罢爸白柏百摆佰败拜稗斑班搬扳颁板版扮拌伴瓣半办绊邦帮绑榜棒傍谤苞胞包褒剥薄雹保堡饱宝抱报暴豹鲍爆杯碑悲卑北辈背贝钡倍狈备惫焙被奔苯本笨崩绷甭泵蹦迸逼鼻比鄙笔彼碧蓖毕毙毖币庇痹闭敝弊必辟壁臂避陛鞭边编贬扁便变卞辨辩辫遍标彪膘表鳖别彬斌滨宾摈兵冰柄丙秉饼炳并病玻菠播拨波博勃铂箔魄膊泊驳捕卜哺补埠布步怖部簿");
            AddRange('c', "擦猜裁材才财踩采彩菜蔡餐参蚕残惭惨灿苍舱仓沧藏操糙槽曹草厕策侧册测层蹭插叉茬茶查碴搽察岔差拆柴豺搀掺蝉馋缠铲产阐颤昌猖场尝常长偿肠厂敞畅唱倡超抄钞朝嘲潮巢吵炒车扯撤掣彻郴臣辰尘晨忱沉陈趁衬撑称城橙成呈乘程惩诚承逞骋秤吃痴持匙池迟弛驰耻齿侈尺赤翅斥炽充冲虫崇宠抽酬畴踌稠愁筹仇绸瞅丑臭初出橱厨躇锄雏滁除楚础储矗搐触处揣川穿椽传船喘串疮窗幢床闯创吹炊捶锤垂春椿醇唇淳纯蠢戳绰茨磁雌辞慈瓷词此刺赐次聪葱囱匆从丛凑粗醋簇促篡摧崔催脆粹翠村存寸磋撮搓措挫错");
            AddRange('d', "搭达答瘩打大呆歹傣戴带殆代贷袋待逮怠耽担丹单掸胆旦但惮淡诞弹蛋当挡党荡档刀捣蹈倒岛祷导到稻悼道盗德得的灯登等瞪凳邓堤低滴迪敌笛狄涤翟嫡抵底地蒂第帝弟递缔颠掂滇碘点典靛垫电店甸淀殿碉叼雕凋刁掉吊钓调跌爹碟蝶迭叠丁盯叮钉顶鼎锭定订丢东冬董懂动栋侗冻洞兜抖斗陡豆逗痘都督毒犊独读堵睹赌杜镀肚度渡妒端短锻段断缎堆兑队对墩吨蹲敦顿囤钝盾遁掇哆多夺垛躲朵跺舵剁惰堕");
            AddRange('e', "蛾峨鹅俄额讹娥恶厄扼遏鄂饿恩而儿耳尔饵洱二");
            AddRange('f', "发罚筏伐乏阀法藩帆番翻樊矾钒繁凡烦反返范贩犯饭泛坊芳方肪房防妨仿访纺放菲非啡飞肥匪诽吠肺废沸费芬酚吩氛分纷坟焚粉奋份忿愤粪丰封枫蜂峰锋风疯烽逢冯缝讽奉凤佛否夫敷肤孵扶辐幅符伏俘服浮涪福袱弗甫抚辅俯釜斧脯府腐赴副覆赋复傅付阜父腹负富附妇缚咐");
            AddRange('g', "嘎该改概钙盖溉干甘杆柑竿肝赶感秆敢赣冈刚钢缸肛纲岗港杠篙皋高膏羔糕搞镐稿告哥歌搁戈鸽胳疙割革葛格蛤阁隔铬个各给根跟耕更庚羹埂耿梗工攻功恭龚供躬公宫弓巩汞拱贡共钩勾沟苟垢构购够辜菇咕箍估沽孤姑鼓古蛊骨谷股故顾固雇刮瓜剐寡挂褂乖拐怪棺关官冠观管馆罐惯灌贯光广逛瑰规圭硅归龟闺轨鬼诡癸桂柜跪贵刽辊滚棍锅郭国果裹过");
            AddRange('h', "哈骸孩海氦亥害骇酣憨邯韩含涵寒函喊罕翰撼捍旱憾焊汗汉夯杭航壕嚎豪毫郝好耗号浩呵喝荷菏核禾和何合盒貉阂河涸赫褐鹤贺嘿黑痕很狠恨哼亨横衡恒轰哄烘虹鸿洪宏弘红喉侯猴吼厚候后呼乎忽瑚壶葫胡蝴狐糊湖弧虎唬护互沪户花哗华猾滑画划化话槐徊怀淮坏欢环桓还缓换患唤痪焕涣宦幻荒慌黄磺蝗簧皇凰惶煌晃幌恍谎灰挥辉徽恢蛔回毁悔慧惠晦贿秽会烩汇讳诲绘荤昏婚魂浑混豁活伙火获或惑霍货祸");
            AddRange('j', "击圾基机畸积肌饥迹激讥鸡姬绩缉吉极棘辑籍集及急疾汲即嫉级挤几脊己蓟技冀季祭剂悸济寄寂计记既忌际继纪嘉枷夹佳家加颊贾甲假稼价架驾嫁歼监坚尖笺间煎兼肩艰奸缄茧检柬碱拣捡简俭剪减荐鉴践贱见键箭件健舰剑饯渐溅涧建僵姜将浆江疆蒋桨奖讲匠酱降蕉椒礁焦胶交郊浇骄娇嚼搅铰矫脚狡角饺缴绞剿教酵轿较叫窖揭接皆秸街阶截劫节桔杰捷睫竭洁结解姐戒芥界借介届襟筋斤金今津襟紧锦仅谨进靳晋禁近烬浸尽劲荆兢茎睛晶鲸京惊精粳经井警景颈静境敬镜径痉靖竟竞净炯窘揪究纠玖韭久灸九酒厩救旧臼舅咎就疚鞠拘狙疽居驹菊局咀矩举沮聚拒据巨具距踞锯俱句惧炬剧捐鹃娟倦眷卷绢撅攫抉掘倔爵觉决诀绝均菌钧军君峻俊竣浚骏");
            AddRange('k', "喀咖卡开凯慨刊堪勘坎砍看康慷糠扛抗亢炕考拷烤靠坷苛柯棵磕颗科壳咳可渴克刻客课肯啃垦恳坑吭空孔恐控抠口扣寇枯哭窟苦酷库裤夸垮跨块筷快宽款匡筐狂框矿眶旷况亏盔岿窥葵奎魁傀馈愧溃坤昆捆困括扩廓阔");
            AddRange('l', "垃拉喇蜡腊辣啦莱来赖蓝婪栏拦篮阑兰澜揽览懒缆烂滥琅榔狼廊郎朗浪捞劳牢老佬姥酪烙涝勒乐雷镭蕾磊累儡垒擂肋类泪棱楞冷厘梨犁黎篱狸离漓理李里鲤礼莉荔吏栗丽厉励砾历利例俐痢立粒沥隶力璃俩联莲连镰廉怜帘敛脸链恋炼练粮凉梁粱良两辆量晾亮谅撩聊僚疗燎寥辽了撂镣廖料列裂烈劣猎琳林磷霖临邻鳞淋凛赁拎玲菱零龄铃伶羚凌灵陵岭领另令溜琉榴硫留刘瘤流柳六龙聋笼隆垄拢楼娄搂篓漏陋芦卢颅庐炉掳卤虏鲁麓碌露路鹿潞禄录陆驴吕铝侣旅履屡缕虑氯律率滤绿峦挛孪滦卵乱掠略轮伦仑沦纶论萝螺罗逻锣箩裸落洛骆络");
            AddRange('m', "妈麻玛码蚂马骂嘛吗埋买麦卖迈脉瞒馒蛮满蔓曼漫慢芒茫盲忙莽猫茅锚毛矛卯茂冒帽貌贸么玫枚梅酶霉煤没眉媒镁每美寐妹媚门闷们萌蒙檬盟猛梦孟眯醚靡糜迷谜弥米秘觅蜜密幂棉眠绵冕免勉缅面苗描瞄藐秒渺庙妙蔑灭民抿皿敏悯闽明螟鸣铭名命谬摸摹蘑模膜磨摩魔抹末莫墨默沫漠寞谋某亩牡母墓暮幕募慕木目睦牧穆");
            AddRange('n', "拿哪钠那娜纳氖乃奶耐奈南男难囊挠脑恼闹呢馁内嫩能妮霓倪泥尼拟你匿腻逆溺蔫拈年碾撵捻念娘酿鸟尿捏聂孽啮镊镍涅您柠狞凝宁拧泞牛扭钮纽脓浓农弄奴努怒女暖疟挪懦糯诺");
            AddRange('o', "哦欧鸥殴藕呕偶沤");
            AddRange('p', "啪趴爬帕怕琶拍排牌徘湃派攀潘盘磐盼畔判叛乓庞旁胖抛咆刨炮袍跑泡呸胚培裴赔陪配佩沛喷盆砰抨烹澎彭蓬棚硼篷膨朋鹏捧碰坯砒霹批披劈琵毗啤脾疲皮匹痞僻屁譬篇偏片骗飘漂瓢票撇瞥拼频贫品聘乒坪苹萍平凭瓶评屏坡泼颇婆破魄迫剖扑铺仆莆葡菩蒲埔朴圃普浦谱曝瀑");
            AddRange('q', "期欺栖戚妻七凄漆柒沏其棋奇歧崎脐齐旗祈祁骑起岂乞企启契砌器气迄弃汽泣掐恰洽牵扦钎铅千迁签谦乾黔钱钳前潜遣浅谴堑欠歉枪呛腔羌墙蔷强抢橇锹敲悄桥瞧乔侨巧鞘撬翘峭俏窍切茄且怯窃钦侵亲秦琴勤芹擒禽寝沁青轻氢倾卿清擎晴氰情顷请庆琼穷秋丘邱球求囚酋趋区蛆曲躯屈驱渠取娶趣去圈颧权醛泉全拳犬券劝缺炔瘸却鹊榷确雀裙群");
            AddRange('r', "然燃冉染瓤壤攘嚷让饶扰绕惹热壬仁人忍韧任认刃妊纫扔仍日戎茸蓉荣融熔溶容绒冗揉柔肉茹蠕儒孺如辱乳汝入褥软阮蕊瑞锐闰润若弱");
            AddRange('s', "撒洒萨腮鳃塞赛三叁伞散桑嗓丧搔骚扫嫂瑟色涩森僧莎砂杀刹沙纱傻啥煞筛晒珊苫杉山删煽衫闪陕擅赡膳善汕扇缮伤商赏晌上尚裳梢捎稍烧芍勺韶少哨邵绍奢赊蛇舌舍赦摄射涉社设砷申呻伸身深娠绅神沈审婶甚肾慎渗声生甥牲升绳省盛剩胜圣师失狮施湿诗尸虱十石拾时食蚀实识史矢使屎驶始式示士世柿事拭誓逝势是嗜噬适仕侍释饰市恃室视试收手首守寿授售受瘦兽蔬枢梳殊抒输叔舒淑疏书赎孰熟薯暑曙署蜀黍鼠属术述树束戍竖墅数漱恕刷耍衰甩帅栓霜双爽谁水睡税吮瞬顺舜说硕朔烁斯撕嘶思私司丝死肆寺嗣四伺似饲松耸颂送宋讼搜艘擞苏酥俗素速粟僳塑溯宿诉肃酸蒜算虽隋随绥髓碎岁穗遂隧祟孙损笋蓑梭唆缩琐索锁所");
            AddRange('t', "塌他它她塔獭挞蹋踏胎苔抬台泰太态汰坍摊贪瘫滩坛檀痰潭谭谈坦毯碳叹炭汤塘堂棠膛唐糖躺趟烫掏涛滔绦萄桃逃淘陶讨套特藤腾疼梯剔踢锑提题蹄啼体替嚏惕涕剃天添填田甜恬舔挑条迢眺跳贴铁帖厅听烃汀廷停亭庭挺艇通桐酮瞳同铜彤童桶捅筒统痛偷投头透凸秃突图徒途涂屠土吐兔湍团推颓腿蜕褪退吞屯臀拖托脱驮椭拓唾");
            AddRange('w', "挖哇蛙洼娃瓦袜歪外豌弯湾玩顽丸烷完碗挽晚宛婉万腕汪王亡枉网往旺望忘妄威巍微危韦违围唯惟为潍维苇萎委伟伪尾纬未蔚味畏胃喂魏位渭谓尉慰卫瘟温蚊文闻纹吻稳紊问嗡翁瓮挝蜗涡窝我斡卧握沃巫呜钨乌污屋无芜梧吾吴毋武五捂午舞伍侮戊雾晤物勿务悟误");
            AddRange('x', "昔熙析西硒矽晰嘻吸锡牺稀息希悉膝夕惜熄烯溪汐犀檄袭席习媳喜洗系隙戏细瞎虾匣霞辖暇峡侠狭下厦夏吓掀先仙鲜纤咸贤衔闲涎弦嫌显险现献县腺馅羡宪陷限线相厢镶香箱襄湘乡翔祥详想响享项巷像向象萧硝霄削哮嚣销消宵晓小孝校肖啸笑效楔些歇蝎鞋协挟携邪斜谐写械卸蟹懈泄泻谢屑薪芯锌欣辛新忻心信星腥猩兴刑型形邢行醒幸杏性姓兄凶胸匈汹雄熊休修羞朽嗅锈秀袖绣戌需虚须徐许蓄酗叙旭序畜恤絮婿绪续轩喧宣悬旋玄选癣眩绚靴薛学穴雪血勋熏循旬询寻驯巡殉汛训讯逊迅");
            AddRange('y', "压押鸦鸭呀丫芽牙蚜崖衙涯雅哑亚讶焉咽阉烟淹盐严研蜒岩延言颜阎炎沿奄掩眼衍演艳堰燕厌砚雁唁彦焰宴谚验殃央鸯秧杨扬羊洋阳氧仰痒养样漾邀腰妖瑶摇尧遥窑谣姚咬舀药要耀爷野冶也页业叶曳夜液一壹医揖依伊衣颐夷遗移仪胰疑沂宜姨彝椅蚁倚已乙矣以艺抑易邑屹亿役臆逸肄疫亦裔意毅忆义益溢诣议谊译异翼翌茵荫因殷音阴姻吟银淫寅饮尹引隐印英樱婴鹰应莹萤营荧蝇迎赢盈影颖硬映哟拥佣臃痈庸雍踊咏泳永惠勇用幽优悠忧尤由邮铀犹油游酉有友右佑釉诱又幼迂淤于盂榆虞愚舆余俞逾鱼愉渔隅予娱雨与屿禹宇语羽玉域芋郁遇喻御愈欲狱育誉浴寓裕预豫驭鸳渊冤元垣袁原援园员圆猿源缘远苑愿怨院曰约越跃钥岳粤月悦阅耘云郧匀陨运蕴酝晕韵孕");
            AddRange('z', "匝砸杂栽哉灾宰载再在咱攒暂赞赃脏葬遭糟凿藻枣早澡蚤躁噪造皂灶燥责择则泽贼怎增憎曾赠扎喳渣札轧铡闸眨栅榨咋乍炸诈摘斋宅窄债寨瞻毡詹粘沾盏斩辗崭展蘸栈占战站湛绽樟章彰漳张掌涨杖丈帐账仗胀障招昭找沼赵照罩兆召遮折哲蛰者蔗这浙珍斟真甄砧臻贞针侦枕疹诊震振镇阵蒸挣睁征狰争怔整拯正政帧症郑证芝枝支吱蜘知肢脂汁之织职直植殖执值侄址指止趾只旨纸志挚掷至致置帜峙制智秩稚质炙痔滞治窒中忠钟终种肿重仲众舟周州洲诌粥轴肘帚咒皱宙昼骤珠株蛛朱猪诸逐竹烛煮拄瞩嘱主著柱助蛀贮铸筑住注祝驻抓爪拽专砖转撰赚篆桩庄装撞壮状椎锥追赘坠缀谆准捉拙卓桌琢茁酌啄着灼浊咨资姿滋淄紫仔籽子自渍字综纵邹走奏揍租足卒族祖阻组钻纂嘴醉最罪尊遵昨左佐做作坐座");
        }

        private static void AddRange(char initial, string chars)
        {
            foreach (char c in chars)
            {
                if (!pinyinMap.ContainsKey(c))
                    pinyinMap[c] = char.ToLowerInvariant(initial);
            }
        }

        public static string GetInitials(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                char initial = GetPinyinInitial(c);
                if (initial != '\0')
                    sb.Append(initial);
            }
            return sb.ToString().ToLowerInvariant();
        }

        public static char GetPinyinInitial(char c)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
            {
                if (pinyinMap.TryGetValue(c, out char init))
                    return init;
                return '\0';
            }
            else if (char.IsLetter(c))
            {
                return c;
            }
            else
            {
                return '\0';
            }
        }
    }
}
