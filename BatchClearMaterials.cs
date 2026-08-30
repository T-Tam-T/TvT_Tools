using UnityEditor;
using UnityEngine;

public static class BatchClearMaterials
{
    [MenuItem("Assets/TvTTools/去除模型材质", priority = 5)]
    private static void ClearSelectedMaterials()
    {
        Object[] selected = Selection.objects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("未选中任何对象或资源。");
            return;
        }

        int modelImporterCount = 0;

        foreach (Object obj in selected)
        {
            // 处理模型资源（如 FBX）的导入设置
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path))
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
                    {
                        importer.materialImportMode = ModelImporterMaterialImportMode.None;
                        importer.SaveAndReimport();
                    }
                    modelImporterCount++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"处理完成：已处理 ModelImporter={modelImporterCount} 个");
    }

    [MenuItem("Assets/TvTTools/去除材质贴图", validate = true, priority = 5)]
    private static bool ValidateClearMaterials()
    {
        if (Selection.objects == null || Selection.objects.Length == 0)
            return false;

        // 至少有一个选中对象是模型资源
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                return true;
        }
        return false;
    }
}
