using System.IO;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 自动设置 Assets/0ljy/tex 下贴图的压缩格式
/// - Windows/Mac/Linux Override 关闭
/// - Android & iOS Override 开启
/// - Max Size 根据贴图实际尺寸设置，最大 512
/// - Max Size ≤ 128 → ASTC 6×6, > 128 → ASTC 5×5
/// </summary>
public class TextureImportConfig : AssetPostprocessor
{
    private const string TargetFolder = "Assets/0ljy";

    private void OnPreprocessTexture()
    {
        string path = assetPath;

        // 仅处理目标文件夹下的贴图
        if (!path.StartsWith(TargetFolder))
            return;

        TextureImporter importer = (TextureImporter)assetImporter;

        // 1. 关闭 Windows/Mac/Linux Override
        importer.ClearPlatformTextureSettings("Standalone");

        // 2. 读取贴图原始尺寸，计算 Max Size
        int sourceWidth, sourceHeight;
        GetTextureDimensions(path, out sourceWidth, out sourceHeight);
        int maxSize = CalculateMaxSize(sourceWidth, sourceHeight);
        TextureImporterFormat format = GetTargetFormat(maxSize);

        // 3. 设置 Android
        TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
        androidSettings.overridden = true;
        androidSettings.maxTextureSize = maxSize;
        androidSettings.format = format;
        importer.SetPlatformTextureSettings(androidSettings);

        // 4. 设置 iOS
        TextureImporterPlatformSettings iosSettings = importer.GetPlatformTextureSettings("iPhone");
        iosSettings.overridden = true;
        iosSettings.maxTextureSize = maxSize;
        iosSettings.format = format;
        importer.SetPlatformTextureSettings(iosSettings);

        Debug.Log($"[TextureImportConfig] {path} → MaxSize={maxSize}, Format={format}, " +
                  $"Source=({sourceWidth}x{sourceHeight})");
    }

    /// <summary>根据实际宽高计算 Max Size（向下取 2 的幂，最大 512）
    /// 非正方形贴图按最短边长设置，正方形贴图使用统一边长</summary>
    private static int CalculateMaxSize(int width, int height)
    {
        int maxDim = Mathf.Min(width, height);

        // 向下取到最接近的 2 的幂
        int size = 1;
        while (size * 2 <= maxDim)
            size *= 2;

        // 上限 512
        return Mathf.Min(size, 512);
    }

    /// <summary>根据 Max Size 决定 Format</summary>
    private static TextureImporterFormat GetTargetFormat(int maxSize)
    {
        return maxSize <= 128
            ? TextureImporterFormat.ASTC_6x6
            : TextureImporterFormat.ASTC_5x5;
    }

    /// <summary>从原始文件读取贴图尺寸（支持 PNG / JPG / TGA）</summary>
    private static void GetTextureDimensions(string assetPath, out int width, out int height)
    {
        width = 0;
        height = 0;

        string dataPath = Application.dataPath;
        string fullPath = Path.Combine(dataPath, assetPath.Substring("Assets/".Length));

        if (!File.Exists(fullPath))
            return;

        byte[] bytes = File.ReadAllBytes(fullPath);

        // PNG
        if (bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return;
        }

        // JPG
        if (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            int offset = 2;
            while (offset < bytes.Length - 9)
            {
                if (bytes[offset] == 0xFF)
                {
                    byte marker = bytes[offset + 1];
                    if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                    {
                        height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                        width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                        return;
                    }
                    offset += 2;
                    int segLen = (bytes[offset] << 8) | bytes[offset + 1];
                    offset += segLen;
                }
                else
                {
                    offset++;
                }
            }
            return;
        }

        // TGA (uncompressed true-color)
        if (bytes.Length > 18 && (bytes[2] == 2 || bytes[2] == 3))
        {
            width = (bytes[13] << 8) | bytes[12];
            height = (bytes[15] << 8) | bytes[14];
        }
    }

    // ===================================================================
    // 菜单：批量应用设置到现有贴图
    // ===================================================================

    [MenuItem("Tools/TvTTools/贴图压缩格式/应用设置到 0ljy/tex")]
    private static void ApplyToExistingTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { TargetFolder });

        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            // --- Windows/Mac/Linux 关闭 ---
            importer.ClearPlatformTextureSettings("Standalone");

            // --- 读取原始尺寸 ---
            int srcW, srcH;
            GetTextureDimensions(path, out srcW, out srcH);
            int maxSize = CalculateMaxSize(srcW, srcH);
            TextureImporterFormat fmt = GetTargetFormat(maxSize);

            // --- Android ---
            TextureImporterPlatformSettings aSet = importer.GetPlatformTextureSettings("Android");
            aSet.overridden = true;
            aSet.maxTextureSize = maxSize;
            aSet.format = fmt;
            importer.SetPlatformTextureSettings(aSet);

            // --- iOS ---
            TextureImporterPlatformSettings iSet = importer.GetPlatformTextureSettings("iPhone");
            iSet.overridden = true;
            iSet.maxTextureSize = maxSize;
            iSet.format = fmt;
            importer.SetPlatformTextureSettings(iSet);

            importer.SaveAndReimport();
            count++;

            Debug.Log($"[TextureImportConfig] Applied to {path} → MaxSize={maxSize}, Format={fmt}");
        }

        Debug.Log($"[TextureImportConfig] Done! {count} textures updated.");
        EditorUtility.DisplayDialog("完成", $"已更新 {count} 张贴图的压缩设置。", "确定");
    }

    [MenuItem("Tools/TvTTools/贴图压缩格式/预览当前设置")]
    private static void PreviewCurrentSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { TargetFolder });

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== {TargetFolder} 下贴图压缩设置预览 ===\n");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            int srcW, srcH;
            GetTextureDimensions(path, out srcW, out srcH);
            int maxSize = CalculateMaxSize(srcW, srcH);

            var android = importer.GetPlatformTextureSettings("Android");
            var ios = importer.GetPlatformTextureSettings("iPhone");

            sb.AppendLine($"{Path.GetFileName(path)}  ({srcW}x{srcH})");
            sb.AppendLine($"  Android: override={android.overridden}, maxSize={android.maxTextureSize}, format={android.format}");
            sb.AppendLine($"  iOS:     override={ios.overridden}, maxSize={ios.maxTextureSize}, format={ios.format}");
            sb.AppendLine($"  预期:    MaxSize={maxSize}, Format={GetTargetFormat(maxSize)}\n");
        }

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("预览", sb.ToString(), "确定");
    }
}
