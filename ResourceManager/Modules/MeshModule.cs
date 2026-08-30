using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    public class MeshModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("网格资源", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            // 收集所有网格并按问题分类
            var allMeshes = new List<(Mesh mesh, ResourceAnalyzer analyzer, string objectName)>();
            ConditionManager conditionManager = ConditionManager.Instance;

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                var meshes = analyzer.ResourceUsage.Keys
                    .Where(r => r is Mesh)
                    .Cast<Mesh>()
                    .Distinct()
                    .ToList();

                foreach (var mesh in meshes)
                {
                    allMeshes.Add((mesh, analyzer, objectName));
                }
            }

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                allMeshes = allMeshes.Where(m => m.mesh.name.ToLower().Contains(filter)).ToList();
            }

            if (allMeshes.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的网格", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未找到网格资源", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {allMeshes.Count} 个匹配网格", MessageType.Info);
            }

            // 分类：问题网格和正常网格
            var problemMeshes = allMeshes.Where(m => 
                conditionManager.CheckNameConditions(m.mesh.name, out _) ||
                conditionManager.CheckMeshConditions(m.mesh, out _)).ToList();
            var normalMeshes = allMeshes.Except(problemMeshes).ToList();

            // 显示问题网格
            if (problemMeshes.Count > 0)
            {
                DrawMeshGroup("问题网格", problemMeshes, true);
                EditorGUILayout.Space(5);
            }

            // 显示正常网格
            if (normalMeshes.Count > 0)
            {
                DrawMeshGroup("正常网格", normalMeshes, false);
            }

            // 显示公用网格
            DrawCommonMeshes(session);
        }

        private void DrawMeshGroup(string title, List<(Mesh mesh, ResourceAnalyzer analyzer, string objectName)> meshes, bool isProblem)
        {
            if (!objectFoldouts.ContainsKey(title))
            {
                objectFoldouts[title] = !isProblem; // 问题网格默认展开
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[title] = true;

            EditorGUILayout.BeginVertical("box");
            objectFoldouts[title] = EditorGUILayout.Foldout(
                objectFoldouts[title], $"{title} ({meshes.Count}个)", true);

            if (objectFoldouts[title])
            {
                EditorGUI.indentLevel++;
                for (int mi = 0; mi < meshes.Count; mi++)
                {
                    using (new UIHelper.ZebraScope(mi))
                    {
                        DrawMesh(meshes[mi].mesh, meshes[mi].analyzer, meshes[mi].objectName);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCommonMeshes(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            var commonMeshes = session.CommonResources.Keys
                .Where(r => r is Mesh)
                .Cast<Mesh>()
                .ToList();

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                commonMeshes = commonMeshes.Where(m => m.name.ToLower().Contains(filter)).ToList();
            }

            if (commonMeshes.Count == 0) return;

            EditorGUILayout.BeginVertical("box");

            string commonKey = "公用网格";

            if (!objectFoldouts.ContainsKey(commonKey))
            {
                objectFoldouts[commonKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[commonKey] = true;

            objectFoldouts[commonKey] = EditorGUILayout.Foldout(
                objectFoldouts[commonKey], $"公用网格 ({commonMeshes.Count}个)", true);

                if (objectFoldouts[commonKey])
                {
                    EditorGUI.indentLevel++;
                    for (int mi = 0; mi < commonMeshes.Count; mi++)
                    {
                        using (new UIHelper.ZebraScope(mi))
                        {
                            // 找到包含此网格的分析器
                            var analyzer = session.Analyzers.Values.FirstOrDefault(a =>
                                a.ResourceUsage.ContainsKey(commonMeshes[mi]));
                            if (analyzer != null)
                            {
                                DrawMesh(commonMeshes[mi], analyzer, "共用");
                            }
                        }
                    }
                    EditorGUI.indentLevel--;
                }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawMesh(Mesh mesh, ResourceAnalyzer analyzer, string objectName = null)
        {
            EditorGUILayout.BeginVertical("helpbox");

            int triangleCount = mesh.triangles.Length / 3;
            int vertexCount = mesh.vertexCount;
            string boundsSize = FormatBounds(mesh.bounds.size);

            // 使用ConditionManager检查条件
            ConditionManager conditionManager = ConditionManager.Instance;
            Color? color = null;

            // 先检查名称条件
            if (conditionManager.CheckNameConditions(mesh.name, out Color? nameColor))
            {
                color = nameColor;
            }
            // 如果没有名称问题，再检查网格条件
            else if (conditionManager.CheckMeshConditions(mesh, out Color? meshColor))
            {
                color = meshColor;
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

            if (!foldouts.ContainsKey(mesh))
            {
                foldouts[mesh] = false;
            }
            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[mesh] = true;
            foldouts[mesh] = EditorGUILayout.Foldout(foldouts[mesh], mesh.name, true);

            GUILayout.FlexibleSpace();

            // 简化的属性显示
            GUILayout.Label($"顶点:{vertexCount} 三角面:{triangleCount}", EditorStyles.miniLabel);

            // 所属对象
            if (!string.IsNullOrEmpty(objectName))
            {
                GUILayout.Label($"[{objectName}]", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            // 对象字段（与材质模块同宽200）
            EditorGUILayout.ObjectField("", mesh, typeof(Mesh), false, GUILayout.Width(200));

            GUILayout.EndHorizontal();

            GUI.color = originalColor;

            // 展开显示详细信息
            if (foldouts[mesh])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                // 显示详细属性
                UIHelper.DrawPropertyLabel("顶点数:", vertexCount.ToString());
                UIHelper.DrawPropertyLabel("三角面数:", triangleCount.ToString());
                UIHelper.DrawPropertyLabel("包围盒:", boundsSize);
                if (mesh.subMeshCount > 1)
                    UIHelper.DrawPropertyLabel("子网格:", mesh.subMeshCount.ToString());

                // 显示使用位置路径
                if (analyzer.ResourceUsage.ContainsKey(mesh))
                {
                    EditorGUILayout.LabelField("使用位置:", EditorStyles.miniBoldLabel);
                    foreach (var usage in analyzer.ResourceUsage[mesh])
                    {
                        UIHelper.DrawUsagePath(usage, foldouts, mesh);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMeshProperties(int vertexCount, int triangleCount, string boundsSize, ConditionManager conditionManager, Mesh mesh)
        {
            GUILayout.BeginHorizontal();

            // 开始方括号
            GUILayout.Label("[", GUILayout.ExpandWidth(false));

            // 顶点数 - 检查条件
            bool highlightVertices = conditionManager.Settings.checkVertexCount &&
                                    (vertexCount > conditionManager.Settings.vertexCountWarning ||
                                     vertexCount > conditionManager.Settings.vertexCountError);
            DrawColoredProperty($"顶点:{vertexCount},", highlightVertices,
                               vertexCount > conditionManager.Settings.vertexCountError ?
                               conditionManager.Settings.vertexCountErrorColor :
                               conditionManager.Settings.vertexCountWarningColor);

            // 三角面数 - 检查条件
            bool highlightTriangles = conditionManager.Settings.checkTriangleCount &&
                                     (triangleCount > conditionManager.Settings.triangleCountWarning ||
                                      triangleCount > conditionManager.Settings.triangleCountError);
            DrawColoredProperty($"三角面:{triangleCount},", highlightTriangles,
                               triangleCount > conditionManager.Settings.triangleCountError ?
                               conditionManager.Settings.triangleCountErrorColor :
                               conditionManager.Settings.triangleCountWarningColor);

            // 包围盒
            GUILayout.Label($"包围盒:{boundsSize}", GUILayout.ExpandWidth(false));

            // 结束方括号
            GUILayout.Label("]", GUILayout.ExpandWidth(false));

            GUILayout.EndHorizontal();
        }

        private void DrawColoredProperty(string text, bool highlight, Color highlightColor)
        {
            Color originalColor = GUI.color;
            if (highlight)
            {
                GUI.color = highlightColor;
            }
            GUILayout.Label(text, GUILayout.ExpandWidth(false));
            GUI.color = originalColor;
        }

        private string FormatBounds(Vector3 size)
        {
            return $"{size.x:F1},{size.y:F1},{size.z:F1}";
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