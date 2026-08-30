using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    public class MaterialModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<Shader, List<Material>> shaderToMaterials = new Dictionary<Shader, List<Material>>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();

        // 修改：为每个分析对象创建独立的shader折叠状态
        private Dictionary<string, Dictionary<string, bool>> objectShaderFoldouts = new Dictionary<string, Dictionary<string, bool>>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("材质", EditorStyles.boldLabel);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            // 使用与整理模块相同的逻辑：直接从 ResourceUsage 收集材质
            var allMaterials = new Dictionary<string, List<Material>>();

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                // 直接从 ResourceUsage 获取材质，不使用缓存
                var materials = analyzer.ResourceUsage.Keys
                    .Where(r => r is Material)
                    .Cast<Material>()
                    .Distinct()
                    .ToList();

                // 应用搜索过滤
                if (!string.IsNullOrEmpty(SearchFilter))
                {
                    string filter = SearchFilter.ToLower();
                    materials = materials.Where(m => m.name.ToLower().Contains(filter)).ToList();
                }

                if (materials.Count > 0)
                {
                    allMaterials[objectName] = materials;

                    // 初始化每个对象的shader折叠状态字典
                    if (!objectShaderFoldouts.ContainsKey(objectName))
                    {
                        objectShaderFoldouts[objectName] = new Dictionary<string, bool>();
                    }
                }
            }

            if (allMaterials.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的材质", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("没有找到材质", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                int totalMatches = allMaterials.Values.Sum(list => list.Count);
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {totalMatches} 个匹配材质", MessageType.Info);
            }

            // 显示公用材质
            DrawCommonMaterials(session);

            // 显示每个对象的材质
            foreach (var kvp in allMaterials)
            {
                string objectName = kvp.Key;
                var materials = kvp.Value;
                var targetObject = session.Analyzers.Keys.FirstOrDefault(go => go.name == objectName);

                if (targetObject == null) continue;

                var analyzer = session.Analyzers[targetObject];

                EditorGUILayout.BeginVertical("box");

                if (!objectFoldouts.ContainsKey(objectName))
                {
                    objectFoldouts[objectName] = false;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    objectFoldouts[objectName] = true;

                objectFoldouts[objectName] = EditorGUILayout.Foldout(
                    objectFoldouts[objectName], $"{objectName} ({materials.Count}个)", true);

                if (objectFoldouts[objectName])
                {
                    EditorGUI.indentLevel++;
                    // 使用当前对象对应的分析器和对象名称
                    DrawObjectMaterials(materials, analyzer, objectName);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawCommonMaterials(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            var commonMaterials = session.CommonResources.Keys
                .Where(r => r is Material)
                .Cast<Material>()
                .ToList();

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                commonMaterials = commonMaterials.Where(m => m.name.ToLower().Contains(filter)).ToList();
            }

            if (commonMaterials.Count == 0) return;

            EditorGUILayout.BeginVertical("box");

            string commonKey = "公用材质";

            if (!objectFoldouts.ContainsKey(commonKey))
            {
                objectFoldouts[commonKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[commonKey] = true;

            objectFoldouts[commonKey] = EditorGUILayout.Foldout(
                objectFoldouts[commonKey], $"公用材质 ({commonMaterials.Count}个)", true);

            if (objectFoldouts[commonKey])
            {
                EditorGUI.indentLevel++;
                // 使用第一个分析器来显示公用材质的使用位置
                var analyzer = session.Analyzers.Values.FirstOrDefault();
                if (analyzer != null)
                {
                    // 初始化公用材质的shader折叠状态
                    if (!objectShaderFoldouts.ContainsKey(commonKey))
                    {
                        objectShaderFoldouts[commonKey] = new Dictionary<string, bool>();
                    }

                    DrawObjectMaterials(commonMaterials, analyzer, commonKey);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawObjectMaterials(List<Material> materials, ResourceAnalyzer analyzer, string objectIdentifier)
        {
            if (materials.Count > 0)
            {
                // 按Shader分组材质
                GroupMaterialsByShader(materials);

                // 绘制按Shader分组的材质列表
                DrawShaderGroups(analyzer, objectIdentifier);
            }
            else
            {
                EditorGUILayout.HelpBox("没有找到材质", MessageType.Info);
            }
        }

        private void GroupMaterialsByShader(List<Material> materials)
        {
            shaderToMaterials.Clear();

            foreach (var mat in materials)
            {
                Shader shader = mat.shader;
                if (shader == null) continue;

                if (!shaderToMaterials.ContainsKey(shader))
                {
                    shaderToMaterials[shader] = new List<Material>();
                }

                shaderToMaterials[shader].Add(mat);
            }
        }

        private void DrawShaderGroups(ResourceAnalyzer analyzer, string objectIdentifier)
        {
            // 按Shader名称排序
            var sortedShaders = shaderToMaterials.Keys
                .OrderBy(s => s.name)
                .ToList();

            int si = 0;
            foreach (var shader in sortedShaders)
            {
                string shaderKey = shader.name;

                // 获取当前对象的shader折叠状态字典
                var currentShaderFoldouts = objectShaderFoldouts[objectIdentifier];

                if (!currentShaderFoldouts.ContainsKey(shaderKey))
                {
                    currentShaderFoldouts[shaderKey] = false;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    currentShaderFoldouts[shaderKey] = true;

                using (new UIHelper.ZebraScope(si))
                {
                    EditorGUILayout.BeginVertical("helpbox");

                    // 绘制Shader行
                    DrawShaderHeader(shader, currentShaderFoldouts, shaderKey);

                    // 如果Shader展开，绘制其下的材质
                    if (currentShaderFoldouts[shaderKey])
                    {
                        EditorGUI.indentLevel++;

                        // 按材质名称排序
                        var sortedMaterials = shaderToMaterials[shader]
                            .OrderBy(m => m.name)
                            .ToList();

                        for (int mi = 0; mi < sortedMaterials.Count; mi++)
                        {
                            using (new UIHelper.ZebraScope(mi))
                            {
                                DrawMaterial(sortedMaterials[mi], analyzer);
                            }
                        }

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                }
                si++;
            }
        }

        private void DrawShaderHeader(Shader shader, Dictionary<string, bool> currentShaderFoldouts, string shaderKey)
        {
            GUILayout.BeginHorizontal();

            currentShaderFoldouts[shaderKey] = EditorGUILayout.Foldout(currentShaderFoldouts[shaderKey], shader.name, true);

            GUILayout.FlexibleSpace();

            // 材质计数
            int materialCount = shaderToMaterials[shader].Count;
            GUILayout.Label($"({materialCount}个材质)", EditorStyles.miniLabel);

            // Shader对象字段
            EditorGUILayout.ObjectField("", shader, typeof(Shader), false, GUILayout.Width(200));

            GUILayout.EndHorizontal();
        }

        private void DrawMaterial(Material mat, ResourceAnalyzer analyzer)
        {
            EditorGUILayout.BeginVertical("box");

            ConditionManager conditionManager = ConditionManager.Instance;
            Color? highlightColor = null;

            // 检查名称条件
            if (conditionManager.CheckNameConditions(mat.name, out Color? nameColor))
            {
                highlightColor = nameColor;
            }

            Color originalColor = GUI.color;
            if (highlightColor.HasValue)
            {
                GUI.color = highlightColor.Value;
            }

            // 设置材质的折叠状态
            if (!foldouts.ContainsKey(mat))
            {
                foldouts[mat] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[mat] = true;

            // 材质头
            GUILayout.BeginHorizontal();

            foldouts[mat] = EditorGUILayout.Foldout(foldouts[mat], mat.name, true);

            GUILayout.FlexibleSpace();

            // 显示材质属性
            GUILayout.Label($"Shader: {mat.shader?.name ?? "None"}", EditorStyles.miniLabel);

            // 对象字段放在最右边
            EditorGUILayout.ObjectField("", mat, typeof(Material), false, GUILayout.Width(200));

            GUILayout.EndHorizontal();

            GUI.color = originalColor;

            // 展开显示详细信息
            if (foldouts[mat])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                // 显示材质属性
                UIHelper.DrawPropertyLabel("Shader:", mat.shader?.name ?? "None");
                UIHelper.DrawPropertyLabel("渲染队列:", mat.renderQueue.ToString());

                // 显示使用位置路径
                if (analyzer.ResourceUsage.ContainsKey(mat))
                {
                    EditorGUILayout.LabelField("使用位置:", EditorStyles.miniBoldLabel);
                    foreach (var usage in analyzer.ResourceUsage[mat])
                    {
                        UIHelper.DrawUsagePath(usage, foldouts, mat);
                    }
                }

                // 显示材质纹理
                DrawMaterialTextures(mat);

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMaterialTextures(Material material)
        {
            if (material == null || material.shader == null) return;

            EditorGUILayout.LabelField("纹理属性:", EditorStyles.miniBoldLabel);

            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);

            bool hasTextures = false;

            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    Texture texture = material.GetTexture(propertyName);

                    if (texture != null)
                    {
                        hasTextures = true;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(propertyName, GUILayout.Width(120));
                        EditorGUILayout.ObjectField(texture, typeof(Texture), false);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            if (!hasTextures)
            {
                EditorGUILayout.LabelField("  无纹理", EditorStyles.miniLabel);
            }
        }

        public void Clear()
        {
            foldouts.Clear();
            shaderToMaterials.Clear();
            objectFoldouts.Clear();
            objectShaderFoldouts.Clear();
        }

        // 保持原有的单对象方法用于兼容
        public void Draw(ResourceAnalyzer analyzer, ResourceCache cache)
        {
            // 实现可以调用多对象版本或保持原有逻辑
        }
    }
}