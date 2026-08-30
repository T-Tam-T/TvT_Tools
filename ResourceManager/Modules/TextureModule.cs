using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceManager.Modules
{
    public class TextureModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("贴图资源", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            // 收集所有贴图并按问题分类
            var allTextures = new List<(Texture2D texture, ResourceAnalyzer analyzer, string objectName)>();
            ConditionManager conditionManager = ConditionManager.Instance;

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                var textures = analyzer.ResourceUsage.Keys
                    .Where(r => r is Texture2D)
                    .Cast<Texture2D>()
                    .Distinct()
                    .ToList();

                foreach (var tex in textures)
                {
                    allTextures.Add((tex, analyzer, objectName));
                }
            }

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                allTextures = allTextures.Where(t => t.texture.name.ToLower().Contains(filter)).ToList();
            }

            if (allTextures.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的贴图", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未找到贴图资源", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {allTextures.Count} 个匹配贴图", MessageType.Info);
            }

            // 分类：问题贴图和正常贴图
            var problemTextures = allTextures.Where(t =>
                conditionManager.CheckNameConditions(t.texture.name, out _) ||
                conditionManager.CheckTextureConditions(t.texture, out _) ||
                conditionManager.CheckTexturePlatformConditions(t.texture, out _, out _)).ToList();
            var normalTextures = allTextures.Except(problemTextures).ToList();

            // 显示问题贴图
            if (problemTextures.Count > 0)
            {
                DrawTextureGroup("问题贴图", problemTextures, true);
                EditorGUILayout.Space(5);
            }

            // 显示正常贴图
            if (normalTextures.Count > 0)
            {
                DrawTextureGroup("正常贴图", normalTextures, false);
            }

            // 显示公用贴图
            DrawCommonTextures(session);
        }

        private void DrawTextureGroup(string title, List<(Texture2D texture, ResourceAnalyzer analyzer, string objectName)> textures, bool isProblem)
        {
            if (!objectFoldouts.ContainsKey(title))
            {
                objectFoldouts[title] = !isProblem; // 问题贴图默认展开
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[title] = true;

            EditorGUILayout.BeginVertical("box");
            objectFoldouts[title] = EditorGUILayout.Foldout(
                objectFoldouts[title], $"{title} ({textures.Count}个)", true);

            if (objectFoldouts[title])
            {
                EditorGUI.indentLevel++;
                for (int ti = 0; ti < textures.Count; ti++)
                {
                    using (new UIHelper.ZebraScope(ti))
                    {
                        DrawTexture(textures[ti].texture, textures[ti].analyzer, textures[ti].objectName);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCommonTextures(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            var commonTextures = session.CommonResources.Keys
                .Where(r => r is Texture2D)
                .Cast<Texture2D>()
                .ToList();

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                commonTextures = commonTextures.Where(t => t.name.ToLower().Contains(filter)).ToList();
            }

            if (commonTextures.Count == 0) return;

            EditorGUILayout.BeginVertical("box");

            string commonKey = "公用纹理";

            if (!objectFoldouts.ContainsKey(commonKey))
            {
                objectFoldouts[commonKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[commonKey] = true;

            objectFoldouts[commonKey] = EditorGUILayout.Foldout(
                objectFoldouts[commonKey], $"公用纹理 ({commonTextures.Count}个)", true);

                if (objectFoldouts[commonKey])
                {
                    EditorGUI.indentLevel++;
                    for (int ti = 0; ti < commonTextures.Count; ti++)
                    {
                        using (new UIHelper.ZebraScope(ti))
                        {
                            // 找到包含此纹理的分析器
                            var analyzer = session.Analyzers.Values.FirstOrDefault(a =>
                                a.ResourceUsage.ContainsKey(commonTextures[ti]));
                            if (analyzer != null)
                            {
                                DrawTexture(commonTextures[ti], analyzer, "共用");
                            }
                        }
                    }
                    EditorGUI.indentLevel--;
                }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawTexture(Texture2D tex, ResourceAnalyzer analyzer, string objectName = null)
        {
            EditorGUILayout.BeginVertical("helpbox");

            // 获取纹理属性
            string resolution = $"{tex.width}x{tex.height}";
            string extension = Path.GetExtension(AssetDatabase.GetAssetPath(tex));
            string wrapMode = tex.wrapMode.ToString();
            string mipMaps = tex.mipmapCount > 1 ? "是" : "否";

            // 使用ConditionManager检查条件
            ConditionManager conditionManager = ConditionManager.Instance;
            Color? color = null;

            // 先检查名称条件
            if (conditionManager.CheckNameConditions(tex.name, out Color? nameColor))
            {
                color = nameColor;
            }
            // 如果没有名称问题，再检查纹理条件
            else if (conditionManager.CheckTextureConditions(tex, out Color? textureColor))
            {
                color = textureColor;
            }
            // 如果没有其他问题，再检查平台条件
            else if (conditionManager.CheckTexturePlatformConditions(tex, out Color? platformColor, out _))
            {
                color = platformColor;
            }

            Color originalColor = GUI.color;
            if (color.HasValue)
            {
                GUI.color = color.Value;
            }

            GUILayout.BeginHorizontal();

            // 问题标记
            if (color.HasValue)
            {
                Color original = GUI.color;
                GUI.color = color.Value;
                GUILayout.Label("●", GUILayout.Width(12));
                GUI.color = original;
            }
            else
            {
                GUILayout.Space(12);
            }

            if (!foldouts.ContainsKey(tex))
            {
                foldouts[tex] = false;
            }
            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[tex] = true;
            foldouts[tex] = EditorGUILayout.Foldout(foldouts[tex], tex.name, true);

            GUILayout.FlexibleSpace();

            // 简化的属性显示
            GUILayout.Label(resolution, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(extension))
            {
                Color original = GUI.color;
                if (conditionManager.Settings.checkDDSFormat && extension.ToLower() == ".dds")
                {
                    GUI.color = conditionManager.Settings.ddsFormatColor;
                }
                GUILayout.Label(extension, EditorStyles.miniLabel);
                GUI.color = original;
            }

            // 所属对象
            if (!string.IsNullOrEmpty(objectName))
            {
                GUILayout.Label($"[{objectName}]", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            // 对象字段
            EditorGUILayout.ObjectField("", tex, typeof(Texture2D), false, GUILayout.Width(100));

            GUILayout.EndHorizontal();

            GUI.color = originalColor;

            // 展开显示详细信息
            if (foldouts[tex])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                // 显示详细属性
                UIHelper.DrawPropertyLabel("尺寸:", $"{tex.width}x{tex.height}");
                UIHelper.DrawPropertyLabel("格式:", tex.format.ToString());
                if (tex.mipmapCount > 1)
                    UIHelper.DrawPropertyLabel("Mipmaps:", "启用");

                // 显示文件路径
                string assetPath = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    UIHelper.DrawPropertyLabel("路径:", assetPath);

                    // 显示平台设置
                    TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer != null)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.LabelField("平台设置:", EditorStyles.miniBoldLabel);

                        Color origColor = GUI.color;

                        // Standalone
                        TextureImporterPlatformSettings standaloneSettings = null;
                        try { standaloneSettings = importer.GetPlatformTextureSettings("Standalone"); } catch { }
                        bool standaloneWarning = conditionManager.Settings.checkTextureStandaloneOverride && standaloneSettings != null && standaloneSettings.overridden;
                        if (standaloneWarning) GUI.color = conditionManager.Settings.standaloneOverrideColor;
                        EditorGUILayout.LabelField($"  Standalone: Override={(standaloneSettings != null && standaloneSettings.overridden ? "是" : "否")}, MaxSize={(standaloneSettings?.maxTextureSize ?? 0)}, Format={(standaloneSettings?.format.ToString() ?? "N/A")}");
                        if (standaloneWarning) GUI.color = origColor;

                        // Android
                        TextureImporterPlatformSettings androidSettings = null;
                        try { androidSettings = importer.GetPlatformTextureSettings("Android"); } catch { }
                        EditorGUILayout.LabelField($"  Android: Override={(androidSettings != null && androidSettings.overridden ? "是" : "否")}, MaxSize={(androidSettings?.maxTextureSize ?? 0)}, Format={(androidSettings?.format.ToString() ?? "N/A")}");

                        // iOS
                        TextureImporterPlatformSettings iosSettings = null;
                        try { iosSettings = importer.GetPlatformTextureSettings("iPhone"); } catch { }
                        EditorGUILayout.LabelField($"  iOS: Override={(iosSettings != null && iosSettings.overridden ? "是" : "否")}, MaxSize={(iosSettings?.maxTextureSize ?? 0)}, Format={(iosSettings?.format.ToString() ?? "N/A")}");

                        // 显示所有平台问题
                        var platformProblems = conditionManager.GetTexturePlatformProblemDetails(tex);
                        if (platformProblems.Count > 0)
                        {
                            EditorGUILayout.Space(2);
                            EditorGUILayout.LabelField("平台问题:", EditorStyles.miniBoldLabel);
                            foreach (var (problem, pColor) in platformProblems)
                            {
                                GUI.color = pColor;
                                EditorGUILayout.LabelField($"  ⚠ {problem}");
                                GUI.color = origColor;
                            }
                        }
                    }
                }

                // 显示使用位置路径
                if (analyzer.ResourceUsage.ContainsKey(tex))
                {
                    EditorGUILayout.LabelField("使用位置:", EditorStyles.miniBoldLabel);
                    foreach (var usage in analyzer.ResourceUsage[tex])
                    {
                        UIHelper.DrawUsagePath(usage, foldouts, tex);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTextureProperties(string resolution, string extension, string wrapMode, string mipMaps, ConditionManager conditionManager)
        {
            GUILayout.BeginHorizontal();

            // 绘制左括号
            GUILayout.Label("[", GUILayout.ExpandWidth(false));

            // 分辨率部分
            GUILayout.Label($"分辨率:{resolution},", GUILayout.ExpandWidth(false));

            // 后缀部分 - 特殊处理DDS格式
            Color originalColor = GUI.color;
            if (conditionManager.Settings.checkDDSFormat &&
                extension.ToLower() == ".dds")
            {
                GUI.color = conditionManager.Settings.ddsFormatColor;
            }
            GUILayout.Label($"后缀:{extension},", GUILayout.ExpandWidth(false));
            GUI.color = originalColor;

            // 裁剪方式部分
            GUILayout.Label($"裁剪方式:{wrapMode},", GUILayout.ExpandWidth(false));

            // LOD部分
            GUILayout.Label($"LOD:{mipMaps}", GUILayout.ExpandWidth(false));

            // 绘制右括号
            GUILayout.Label("]", GUILayout.ExpandWidth(false));

            GUILayout.EndHorizontal();
        }

        public void Clear()
        {
            foldouts.Clear();
            objectFoldouts.Clear();
        }

        // 原有的 Draw 方法保持不变
        public void Draw(ResourceAnalyzer analyzer, ResourceCache cache)
        {
            // 实现可以调用多对象版本或保持原有逻辑
        }
    }
}