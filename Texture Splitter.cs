using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class TextureSplitter : EditorWindow
{
    private Texture2D sourceTexture;
    private int gridRows = 1;
    private int gridCols = 1;
    private int rotationAngle = 90; // 90, 180 or 270
    private Texture2D resultTexture;
    private Vector2 scrollPosition;
    private bool showAdvanced = false;

    [MenuItem("Tools/TvTTools/纹理分割器")]
    public static void ShowWindow()
    {
        GetWindow<TextureSplitter>("纹理分割器");
    }

    private void OnGUI()
    {
        GUILayout.Label("纹理分割设置", EditorStyles.boldLabel);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("源纹理", sourceTexture, typeof(Texture2D), false);
        gridRows = EditorGUILayout.IntField("网格行数 (M)", gridRows);
        gridCols = EditorGUILayout.IntField("网格列数 (N)", gridCols);
        rotationAngle = EditorGUILayout.IntPopup("旋转角度", rotationAngle,
            new string[] { "90°", "180°", "270°" }, new int[] { 90, 180, 270 });

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "高级选项");
        if (showAdvanced)
        {
            EditorGUILayout.HelpBox("此工具不需要源纹理开启Read/Write选项", MessageType.Info);
        }

        if (GUILayout.Button("处理纹理") && sourceTexture)
        {
            ProcessTexture();
        }

        if (resultTexture)
        {
            GUILayout.Space(20);
            GUILayout.Label("结果纹理", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            float aspect = (float)resultTexture.width / resultTexture.height;
            float previewWidth = Mathf.Min(position.width - 30, 500);
            Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewWidth / aspect);

            // 使用DrawTextureTransparent支持透明通道预览
            EditorGUI.DrawTextureTransparent(previewRect, resultTexture, ScaleMode.ScaleToFit);

            EditorGUILayout.EndScrollView();

            GUILayout.Label($"结果尺寸: {resultTexture.width} x {resultTexture.height}", EditorStyles.boldLabel);
            GUILayout.Label($"宽高比: {aspect:F2}", EditorStyles.boldLabel);

            if (GUILayout.Button("保存结果"))
            {
                SaveTexture();
            }
        }
    }

    private void ProcessTexture()
    {
        if (gridRows <= 0 || gridCols <= 0)
        {
            Debug.LogError("网格尺寸必须为正整数");
            return;
        }

        // 使用RenderTexture绕过Read/Write限制
        RenderTexture rt = RenderTexture.GetTemporary(
            sourceTexture.width,
            sourceTexture.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);

        // 将源纹理复制到RenderTexture
        Graphics.Blit(sourceTexture, rt);

        // 创建临时纹理用于读取像素
        Texture2D tempSource = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.ARGB32, false);

        // 从RenderTexture读取像素数据
        RenderTexture.active = rt;
        tempSource.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
        tempSource.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        // 计算小图尺寸（支持任意尺寸）
        int subWidth = Mathf.FloorToInt(tempSource.width / (float)gridCols);
        int subHeight = Mathf.FloorToInt(tempSource.height / (float)gridRows);
        int totalTiles = gridRows * gridCols;

        // 确定旋转后小图的尺寸
        int rotatedWidth = rotationAngle == 180 ? subWidth : subHeight;
        int rotatedHeight = rotationAngle == 180 ? subHeight : subWidth;

        // 计算小图原始宽高比（旋转后可能变化）
        float tileAspect = (float)rotatedWidth / rotatedHeight;

        // 优化新布局行列数计算（考虑小图宽高比）
        (int newRows, int newCols) = CalculateOptimalLayout(totalTiles, tileAspect);

        Debug.Log($"原始布局: {gridRows}行{gridCols}列, 新布局: {newRows}行{newCols}列");
        Debug.Log($"旋转角度: {rotationAngle}°, 旋转后小图尺寸: {rotatedWidth}x{rotatedHeight}");

        // 计算新纹理尺寸
        int newWidth = newCols * rotatedWidth;
        int newHeight = newRows * rotatedHeight;

        // 创建新纹理 - 使用RGBA32格式支持透明通道
        resultTexture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };

        // 填充背景为透明
        Color[] transparentPixels = new Color[newWidth * newHeight];
        for (int i = 0; i < transparentPixels.Length; i++)
        {
            transparentPixels[i] = Color.clear;
        }
        resultTexture.SetPixels(transparentPixels);
        resultTexture.Apply();

        // 提取并处理所有小图
        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                // 计算实际裁剪区域（防止越界）
                int startX = Mathf.Min(col * subWidth, tempSource.width - subWidth);
                int startY = Mathf.Min((gridRows - 1 - row) * subHeight, tempSource.height - subHeight);

                // 提取小图 - 确保处理透明通道
                Color[] pixels = tempSource.GetPixels(startX, startY, subWidth, subHeight);

                // 创建小图纹理 - 使用RGBA32格式支持透明通道
                Texture2D tile = new Texture2D(subWidth, subHeight, TextureFormat.RGBA32, false);
                tile.SetPixels(pixels);
                tile.Apply();

                // 旋转小图
                Texture2D rotatedTile = RotateTexture(tile, rotationAngle);

                // 计算在结果纹理中的位置
                int index = row * gridCols + col;
                int resultRow = index / newCols;
                int resultCol = index % newCols;

                // 计算目标位置
                int posX = resultCol * rotatedWidth;
                int posY = (newRows - 1 - resultRow) * rotatedHeight;

                // 确保目标位置在纹理范围内
                if (posX >= 0 && posY >= 0 &&
                    posX + rotatedTile.width <= resultTexture.width &&
                    posY + rotatedTile.height <= resultTexture.height)
                {
                    resultTexture.SetPixels(
                        posX,
                        posY,
                        rotatedTile.width,
                        rotatedTile.height,
                        rotatedTile.GetPixels()
                    );
                }
                else
                {
                    Debug.LogWarning($"小图 {index} 超出边界: {posX},{posY} ({rotatedTile.width}x{rotatedTile.height})");
                }

                // 销毁临时纹理
                DestroyImmediate(tile);
                DestroyImmediate(rotatedTile);
            }
        }

        resultTexture.Apply();

        // 清理临时纹理
        DestroyImmediate(tempSource);
    }

    // 优化布局计算：考虑小图宽高比
    private (int rows, int cols) CalculateOptimalLayout(int totalTiles, float tileAspect)
    {
        float bestScore = float.MaxValue;
        int bestRows = 1;
        int bestCols = totalTiles;

        // 尝试所有可能的行列组合
        for (int rows = 1; rows <= totalTiles; rows++)
        {
            int cols = Mathf.CeilToInt((float)totalTiles / rows);

            // 计算新布局的宽高比
            float layoutAspect = (cols * tileAspect) / rows;

            // 计算与理想正方形(1:1)的差距
            float aspectDiff = Mathf.Abs(1f - layoutAspect);

            // 考虑空白区域（如果布局位置多于实际小图数）
            float emptyRatio = (rows * cols - totalTiles) / (float)(rows * cols);

            // 综合评分：宽高比差距 + 空白区域惩罚
            float score = aspectDiff + emptyRatio * 0.5f;

            // 优先选择更接近正方形的布局
            if (score < bestScore)
            {
                bestScore = score;
                bestRows = rows;
                bestCols = cols;
            }
        }

        return (bestRows, bestCols);
    }

    // 支持90、180、270度旋转（保留透明通道）
    private Texture2D RotateTexture(Texture2D originalTexture, int angle)
    {
        int width = originalTexture.width;
        int height = originalTexture.height;
        Color[] originalPixels = originalTexture.GetPixels();
        Color[] rotatedPixels = new Color[width * height];

        switch (angle)
        {
            case 90: // 顺时针90度
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int newX = height - 1 - y;
                        int newY = x;
                        rotatedPixels[newY * height + newX] = originalPixels[y * width + x];
                    }
                }
                return CreateRotatedTexture(height, width, rotatedPixels);

            case 180: // 180度
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int newX = width - 1 - x;
                        int newY = height - 1 - y;
                        rotatedPixels[newY * width + newX] = originalPixels[y * width + x];
                    }
                }
                return CreateRotatedTexture(width, height, rotatedPixels);

            case 270: // 逆时针90度（相当于顺时针270度）
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int newX = y;
                        int newY = width - 1 - x;
                        rotatedPixels[newY * height + newX] = originalPixels[y * width + x];
                    }
                }
                return CreateRotatedTexture(height, width, rotatedPixels);

            default:
                Debug.LogWarning($"不支持的旋转角度: {angle}°, 使用原始纹理");
                return originalTexture;
        }
    }

    private Texture2D CreateRotatedTexture(int width, int height, Color[] pixels)
    {
        // 使用RGBA32格式确保透明通道支持
        Texture2D rotatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        rotatedTexture.SetPixels(pixels);
        rotatedTexture.Apply();
        return rotatedTexture;
    }

    private void SaveTexture()
    {
        string path = EditorUtility.SaveFilePanel(
            "保存纹理",
            "Assets",
            "重组纹理.png",
            "png"
        );

        if (!string.IsNullOrEmpty(path))
        {
            // 使用PNG编码确保保留透明通道
            byte[] pngData = resultTexture.EncodeToPNG();
            File.WriteAllBytes(path, pngData);

            if (path.StartsWith(Application.dataPath))
            {
                string assetPath = "Assets" + path.Substring(Application.dataPath.Length);
                AssetDatabase.ImportAsset(assetPath);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            Debug.Log($"纹理已保存至: {path}");
        }
    }
}