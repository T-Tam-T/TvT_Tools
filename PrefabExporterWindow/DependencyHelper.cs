using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 依赖资源分析工具类：负责刷新依赖列表、类型判断、批量勾选等操作。
/// </summary>
public static class DependencyHelper
{
    // ──────────────────────────────────────────────
    //  依赖刷新
    // ──────────────────────────────────────────────

    /// <summary>
    /// 重新扫描 item.prefab 的所有依赖，填充 item.dependencies。
    /// </summary>
    /// <param name="item">目标导出项</param>
    /// <param name="includeScripts">是否包含 .cs 脚本</param>
    /// <param name="includeAllDependencies">是否默认全选依赖</param>
    /// <param name="includeTextures">贴图类型开关</param>
    /// <param name="includeModels">模型类型开关</param>
    /// <param name="includeMaterials">材质球类型开关</param>
    /// <param name="includeAnimations">动画文件类型开关</param>
    /// <param name="includeOthers">其他类型开关</param>
    public static void RefreshDependencies(ExportItem item,
        bool includeScripts,
        bool includeAllDependencies,
        bool includeTextures,
        bool includeModels,
        bool includeMaterials,
        bool includeAnimations,
        bool includeOthers)
    {
        if (item.prefab == null) return;
        item.dependencies.Clear();

        string path = AssetDatabase.GetAssetPath(item.prefab);
        if (string.IsNullOrEmpty(path)) return;

        // 自身
        item.dependencies[path] = (true, GetAssetType(path));

        // 所有依赖
        foreach (string dep in AssetDatabase.GetDependencies(path, true))
        {
            if (!includeScripts && dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (dep == path) continue;

            string type = GetAssetType(dep);
            bool selected = includeAllDependencies
                && IsAssetTypeIncluded(type, includeTextures, includeModels, includeMaterials, includeAnimations, includeOthers);
            item.dependencies[dep] = (selected, type);
        }
    }

    // ──────────────────────────────────────────────
    //  类型判断
    // ──────────────────────────────────────────────

    /// <summary>返回资源路径对应的类型字符串（Texture / Model / Material / Animation / Other）。</summary>
    public static string GetAssetType(string path)
    {
        Type t = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (t == null) return "Unknown";
        if (t == typeof(Texture2D) || t == typeof(Texture) || t == typeof(Sprite))        return "Texture";
        if (t == typeof(Mesh)      || t == typeof(GameObject))                             return "Model";
        if (t == typeof(Material))                                                         return "Material";
        if (t == typeof(AnimationClip)
            || t == typeof(AnimatorController)
            || t == typeof(AnimatorOverrideController))                                    return "Animation";
        return "Other";
    }

    /// <summary>判断某种类型是否在当前过滤条件中被包含。</summary>
    public static bool IsAssetTypeIncluded(string type,
        bool includeTextures, bool includeModels, bool includeMaterials,
        bool includeAnimations, bool includeOthers)
    {
        switch (type)
        {
            case "Texture":   return includeTextures;
            case "Model":     return includeModels;
            case "Material":  return includeMaterials;
            case "Animation": return includeAnimations;
            default:          return includeOthers;
        }
    }

    // ──────────────────────────────────────────────
    //  批量勾选
    // ──────────────────────────────────────────────

    /// <summary>全选或全不选 item 的所有依赖。</summary>
    public static void SelectAllDependencies(ExportItem item, bool select)
    {
        var keys = item.dependencies.Keys.ToList();
        foreach (var k in keys)
            item.dependencies[k] = (select, item.dependencies[k].assetType);
    }

    /// <summary>反选 item 的所有依赖。</summary>
    public static void ToggleAllDependencies(ExportItem item)
    {
        var keys = item.dependencies.Keys.ToList();
        foreach (var k in keys)
            item.dependencies[k] = (!item.dependencies[k].selected, item.dependencies[k].assetType);
    }

    // ──────────────────────────────────────────────
    //  Prefab 验证
    // ──────────────────────────────────────────────

    /// <summary>判断一个 UnityEngine.Object 是否为有效的 Prefab 资源。</summary>
    public static bool IsValidPrefab(UnityEngine.Object obj)
    {
        GameObject go = obj as GameObject;
        if (go == null) return false;
        string path = AssetDatabase.GetAssetPath(go);
        return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }
}
