using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SLGNamingTool
{
    // ── 开关：是否启用 FX_ 前缀智能补全 ──
    private static bool EnableFXPrefix = true;
    private const string FX_PREF_KEY = "SLGNaming_EnableFXPrefix";

    static SLGNamingTool()
    {
        EnableFXPrefix = EditorPrefs.GetBool(FX_PREF_KEY, true);
    }

    // ── 菜单项：切换 FX_ 前缀补全功能（勾选状态） ──
    [MenuItem("Tools/TvTTools/SLG规范化命名/切换 FX_ 前缀智能补全")]
    private static void ToggleFXPrefix()
    {
        EnableFXPrefix = !EnableFXPrefix;
        EditorPrefs.SetBool(FX_PREF_KEY, EnableFXPrefix);
        Debug.Log($"FX_ 前缀智能补全功能已{(EnableFXPrefix ? "开启" : "关闭")}");
    }

    [MenuItem("Tools/TvTTools/SLG规范化命名/切换 FX_ 前缀智能补全", true)]
    private static bool ToggleFXPrefixValidate()
    {
        Menu.SetChecked("Tools/TvTTools/SLG规范化命名/切换 FX_ 前缀智能补全", EnableFXPrefix);
        return true;
    }

    // ── 核心功能：SLG规范化命名及路径 ──
    [MenuItem("Assets/TvTTools/SLG规范化命名及路径", priority = 5)]
    private static void NormalizeSLGNaming()
    {
        foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string folderPath = AssetDatabase.GetAssetPath(obj);
            if (!AssetDatabase.IsValidFolder(folderPath))
                continue;

            string folderName = Path.GetFileName(folderPath);

            // 1. 根据开关决定是否智能补全为 "FX_" 开头
            if (EnableFXPrefix)
            {
                folderName = EnsureFXPrefix(folderName);
            }

            // 2. 仅将首字母转为大写（其余不变）
            string displayName = CapitalizeFirstLetter(folderName);

            string fullFolderPath = Path.Combine(Application.dataPath, folderPath.Substring("Assets/".Length));
            if (!Directory.Exists(fullFolderPath))
                continue;

            var files = Directory.GetFiles(fullFolderPath);
            var materials = new List<string>();
            var textures   = new List<string>();
            var prefabs    = new List<string>();
            var models     = new List<string>();

            foreach (var file in files)
            {
                if (file.EndsWith(".meta")) continue;

                string relPath = "Assets" + file.Substring(Application.dataPath.Length).Replace("\\", "/");

                AssetImporter importer = AssetImporter.GetAtPath(relPath);
                if (importer is ModelImporter)
                {
                    models.Add(relPath);
                    continue;
                }

                System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(relPath);
                if (assetType == typeof(Material))
                {
                    materials.Add(relPath);
                }
                else if (typeof(Texture).IsAssignableFrom(assetType))
                {
                    textures.Add(relPath);
                }
                else if (assetType == typeof(GameObject) && relPath.EndsWith(".prefab"))
                {
                    prefabs.Add(relPath);
                }
            }

            materials.Sort();
            textures.Sort();
            prefabs.Sort();
            models.Sort();

            ProcessType(folderPath, displayName, "Materials", "M_",  materials);
            ProcessType(folderPath, displayName, "Textures",  "T_",  textures);
            ProcessType(folderPath, displayName, "Perfabs",   "PF_", prefabs);
            ProcessType(folderPath, displayName, "Models",    "MD_", models);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SLG规范化命名及路径完成。");
    }

    private static void ProcessType(string folderPath, string displayFolderName, string subFolderName, string prefix, List<string> assets)
    {
        if (assets.Count == 0) return;

        string subFolderPath = folderPath + "/" + subFolderName;
        if (!AssetDatabase.IsValidFolder(subFolderPath))
        {
            AssetDatabase.CreateFolder(folderPath, subFolderName);
        }

        int index = 1;
        foreach (var assetPath in assets)
        {
            string extension = Path.GetExtension(assetPath);
            string newName = $"{prefix}{displayFolderName}_{index:D2}";
            string newPath = subFolderPath + "/" + newName + extension;

            if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null)
            {
                Debug.LogWarning($"目标文件已存在，跳过: {newPath}");
                continue;
            }

            string error = AssetDatabase.MoveAsset(assetPath, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"移动失败: {assetPath} -> {newPath}，错误: {error}");
            }
            else
            {
                index++;
            }
        }
    }

    /// <summary>
    /// 智能补全：保证字符串以 "FX_" 开头，按字符缺省补充
    /// </summary>
    private static string EnsureFXPrefix(string name)
    {
        const string target = "FX_";
        int i = 0; // 目标索引
        int j = 0; // 原始名称索引
        StringBuilder sb = new StringBuilder();

        while (i < target.Length)
        {
            if (j < name.Length && name[j] == target[i])
            {
                // 字符匹配，直接使用原字符并推进两个索引
                sb.Append(name[j]);
                j++;
                i++;
            }
            else
            {
                // 缺少目标字符，补入目标字符，只推进目标索引
                sb.Append(target[i]);
                i++;
            }
        }

        // 拼接剩余部分
        if (j < name.Length)
            sb.Append(name.Substring(j));

        return sb.ToString();
    }

    /// <summary>
    /// 将首字母转为大写，其余不变
    /// </summary>
    private static string CapitalizeFirstLetter(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length == 1) return name.ToUpper();
        return char.ToUpper(name[0]) + name.Substring(1);
    }

    [MenuItem("Assets/TvTTools/SLG规范化命名及路径", validate = true, priority = 5)]
    private static bool ValidateNormalizeSLGNaming()
    {
        foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            if (AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj)))
                return true;
        }
        return false;
    }
}