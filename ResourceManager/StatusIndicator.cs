using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ResourceManager
{
    public class StatusIndicator
    {
        // 状态数据
        private StatusData statusData = new StatusData();
        private bool isAnalyzing = false;
        private float analyzingProgress = 0f;
        private int analyzingCurrent = 0;
        private int analyzingTotal = 0;

        // UI状态
        private bool showDetailPanel = false;
        private Vector2 detailScrollPosition = Vector2.zero;
        private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();

        // 缓存
        private AnalysisSession cachedSession;
        private bool needsUpdate = true;

        // 指示灯状态枚举
        public enum IndicatorState
        {
            None,      // 灰色 - 未触发/禁用
            Normal,    // 绿色 - 正常/启用状态
            Warning,   // 黄色 - 警告级别触发
            Special,   // 橙色 - 特殊警告触发
            Error      // 红色 - 错误级别触发
        }

        // 状态数据结构
        private class StatusData
        {
            public int objectCount = 0;
            public int totalResourceCount = 0;
            public int problemResourceCount = 0;
            public int commonResourceCount = 0;

            // 各类别问题统计
            public Dictionary<string, int> nameProblems = new Dictionary<string, int>();
            public Dictionary<string, int> meshProblems = new Dictionary<string, int>();
            public Dictionary<string, int> textureProblems = new Dictionary<string, int>();
            public Dictionary<string, int> animationProblems = new Dictionary<string, int>();
            public Dictionary<string, int> particleProblems = new Dictionary<string, int>();

            public void Clear()
            {
                objectCount = 0;
                totalResourceCount = 0;
                problemResourceCount = 0;
                commonResourceCount = 0;
                nameProblems.Clear();
                meshProblems.Clear();
                textureProblems.Clear();
                animationProblems.Clear();
                particleProblems.Clear();
            }
        }

        // 公共接口：更新状态
        public void UpdateStatus(AnalysisSession session)
        {
            if (session == null)
            {
                statusData.Clear();
                needsUpdate = false;
                return;
            }

            cachedSession = session;
            needsUpdate = true;
            CalculateStatus(session);
        }

        // 公共接口：清空状态
        public void ClearStatus()
        {
            statusData.Clear();
            isAnalyzing = false;
            analyzingProgress = 0f;
            analyzingCurrent = 0;
            analyzingTotal = 0;
            needsUpdate = false;
        }

        // 公共接口：设置分析状态
        public void SetAnalyzing(bool analyzing, int current = 0, int total = 0)
        {
            isAnalyzing = analyzing;
            analyzingCurrent = current;
            analyzingTotal = total;
            if (total > 0)
            {
                analyzingProgress = (float)current / total;
            }
        }

        // 绘制状态指示器
        public void DrawStatusBar(EditorWindow window)
        {
            if (needsUpdate && cachedSession != null)
            {
                CalculateStatus(cachedSession);
                needsUpdate = false;
            }

            // 当前版本不再绘制状态栏，仅保留逻辑计算，方便后续扩展。
        }

        // // 绘制状态文本
        // private void DrawStatusText()
        // {
        //     string statusText = GetStatusText();
        //     GUILayout.Label(statusText, EditorStyles.label);
        // }

        // // 获取状态文本
        // private string GetStatusText()
        // {
        //     if (isAnalyzing)
        //     {
        //         return $"[分析中...] | 已扫描: {analyzingCurrent}/{analyzingTotal}个资源";
        //     }

        //     if (statusData.objectCount == 0)
        //     {
        //         return "[未分析]";
        //     }

        //     if (statusData.problemResourceCount == 0)
        //     {
        //         return $"[状态良好] | 对象: {statusData.objectCount} | 总资源: {statusData.totalResourceCount} | 问题: 0";
        //     }

        //     return $"[就绪] | 对象: {statusData.objectCount} | 总资源: {statusData.totalResourceCount} | 问题: {statusData.problemResourceCount} | 共用: {statusData.commonResourceCount}";
        // }

        // 绘制指示灯
        private void DrawIndicators()
        {
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));

            // N - 命名问题指示灯
            DrawIndicator("N", GetNameIndicatorState(), "命名问题");

            // M - 网格问题指示灯
            DrawIndicator("M", GetMeshIndicatorState(), "网格问题");

            // T - 纹理问题指示灯
            DrawIndicator("T", GetTextureIndicatorState(), "纹理问题");

            // A - 动画问题指示灯
            DrawIndicator("A", GetAnimationIndicatorState(), "动画问题");

            // P - 粒子问题指示灯
            DrawIndicator("P", GetParticleIndicatorState(), "粒子问题");

            // S - 场景对象状态灯
            DrawIndicator("S", GetSceneIndicatorState(), "场景对象");

            // C - 条件设置状态灯
            DrawIndicator("C", GetConditionIndicatorState(), "条件设置");

            GUILayout.EndHorizontal();
        }

        // 绘制单个指示灯
        private void DrawIndicator(string label, IndicatorState state, string tooltip)
        {
            Color color = GetIndicatorColor(state);
            Color originalColor = GUI.color;

            // 创建指示灯区域
            GUILayout.BeginVertical(GUILayout.Width(20), GUILayout.Height(20));
            
            // 绘制指示灯圆点
            Rect rect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
            
            // 绘制指示灯（使用圆形）
            GUI.color = color;
            EditorGUI.DrawRect(rect, color);
            GUI.color = originalColor;

            // 绘制边框
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), Color.black);

            // 绘制标签
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(16));

            GUILayout.EndVertical();

            // 鼠标悬停提示
            if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
            {
                GUI.tooltip = tooltip;
            }

            // 点击交互
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                OnIndicatorClicked(label);
                Event.current.Use();
            }

            GUILayout.Space(2);
        }

        // 获取指示灯颜色
        private Color GetIndicatorColor(IndicatorState state)
        {
            switch (state)
            {
                case IndicatorState.None:
                    return Color.gray;
                case IndicatorState.Normal:
                    return Color.green;
                case IndicatorState.Warning:
                    return Color.yellow;
                case IndicatorState.Special:
                    return new Color(1f, 0.5f, 0f); // 橙色
                case IndicatorState.Error:
                    return Color.red;
                default:
                    return Color.gray;
            }
        }

        // 获取各类别指示灯状态
        private IndicatorState GetNameIndicatorState()
        {
            if (statusData.nameProblems.Count == 0) return IndicatorState.None;
            int total = statusData.nameProblems.Values.Sum();
            if (total > 0) return IndicatorState.Error;
            return IndicatorState.None;
        }

        private IndicatorState GetMeshIndicatorState()
        {
            if (statusData.meshProblems.Count == 0) return IndicatorState.None;
            int total = statusData.meshProblems.Values.Sum();
            if (total > 0) return IndicatorState.Warning;
            return IndicatorState.None;
        }

        private IndicatorState GetTextureIndicatorState()
        {
            if (statusData.textureProblems.Count == 0) return IndicatorState.None;
            int total = statusData.textureProblems.Values.Sum();
            if (total > 0) return IndicatorState.Warning;
            return IndicatorState.None;
        }

        private IndicatorState GetAnimationIndicatorState()
        {
            if (statusData.animationProblems.Count == 0) return IndicatorState.None;
            int total = statusData.animationProblems.Values.Sum();
            if (total > 0) return IndicatorState.Warning;
            return IndicatorState.None;
        }

        private IndicatorState GetParticleIndicatorState()
        {
            if (statusData.particleProblems.Count == 0) return IndicatorState.None;
            int total = statusData.particleProblems.Values.Sum();
            if (total > 0) return IndicatorState.Warning;
            return IndicatorState.None;
        }

        private IndicatorState GetSceneIndicatorState()
        {
            if (cachedSession == null || cachedSession.SceneObjects.Count == 0)
                return IndicatorState.None;
            return IndicatorState.Normal;
        }

        private IndicatorState GetConditionIndicatorState()
        {
            ConditionManager conditionManager = ConditionManager.Instance;
            if (conditionManager == null || conditionManager.Settings == null)
                return IndicatorState.None;
            return IndicatorState.Normal;
        }

        // 指示灯点击事件
        private void OnIndicatorClicked(string label)
        {
            // TODO: 实现筛选功能
            Debug.Log($"点击了指示灯: {label}");
        }

        // 绘制详细面板
        private void DrawDetailPanel()
        {
            EditorGUILayout.BeginVertical("box");
            detailScrollPosition = EditorGUILayout.BeginScrollView(detailScrollPosition, GUILayout.MaxHeight(250));

            EditorGUI.indentLevel++;

            // 命名规范
            DrawCategoryDetail("命名规范", statusData.nameProblems);

            // 网格
            DrawCategoryDetail("网格", statusData.meshProblems);

            // 贴图
            DrawCategoryDetail("贴图", statusData.textureProblems);

            // 动画
            DrawCategoryDetail("动画", statusData.animationProblems);

            // 粒子
            DrawCategoryDetail("粒子", statusData.particleProblems);

            EditorGUI.indentLevel--;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // 绘制类别详情
        private void DrawCategoryDetail(string categoryName, Dictionary<string, int> problems)
        {
            if (problems.Count == 0) return;

            int total = problems.Values.Sum();
            if (!categoryFoldouts.ContainsKey(categoryName))
            {
                categoryFoldouts[categoryName] = false;
            }

            categoryFoldouts[categoryName] = EditorGUILayout.Foldout(
                categoryFoldouts[categoryName], $"{categoryName} ({total}个问题)", true);

            if (categoryFoldouts[categoryName])
            {
                EditorGUI.indentLevel++;
                foreach (var kvp in problems)
                {
                    EditorGUILayout.LabelField($"├─ {kvp.Key}: {kvp.Value}个");
                }
                EditorGUI.indentLevel--;
            }
        }

        // 计算状态数据
        private void CalculateStatus(AnalysisSession session)
        {
            statusData.Clear();

            if (session == null) return;

            statusData.objectCount = session.Analyzers.Count;
            ConditionManager conditionManager = ConditionManager.Instance;

            HashSet<UnityEngine.Object> allResources = new HashSet<UnityEngine.Object>();
            HashSet<UnityEngine.Object> problemResources = new HashSet<UnityEngine.Object>();

            // 遍历所有分析器
            foreach (var analyzer in session.Analyzers.Values)
            {
                foreach (var resource in analyzer.ResourceUsage.Keys)
                {
                    allResources.Add(resource);

                    // 检查命名问题
                    if (resource != null && conditionManager.CheckNameConditions(resource.name, out _))
                    {
                        problemResources.Add(resource);
                        string problemType = GetNameProblemType(resource.name, conditionManager);
                        if (!statusData.nameProblems.ContainsKey(problemType))
                            statusData.nameProblems[problemType] = 0;
                        statusData.nameProblems[problemType]++;
                    }

                    // 检查网格问题
                    if (resource is Mesh mesh)
                    {
                        if (conditionManager.CheckMeshConditions(mesh, out _))
                        {
                            problemResources.Add(resource);
                            string problemType = GetMeshProblemType(mesh, conditionManager);
                            if (!statusData.meshProblems.ContainsKey(problemType))
                                statusData.meshProblems[problemType] = 0;
                            statusData.meshProblems[problemType]++;
                        }
                    }

                    // 检查贴图问题
                    if (resource is Texture2D texture)
                    {
                        if (conditionManager.CheckTextureConditions(texture, out _))
                        {
                            problemResources.Add(resource);
                            string problemType = GetTextureProblemType(texture, conditionManager);
                            if (!statusData.textureProblems.ContainsKey(problemType))
                                statusData.textureProblems[problemType] = 0;
                            statusData.textureProblems[problemType]++;
                        }

                        // 检查贴图平台设置问题
                        if (conditionManager.CheckTexturePlatformConditions(texture, out _, out string platformType))
                        {
                            problemResources.Add(resource);
                            if (!statusData.textureProblems.ContainsKey(platformType))
                                statusData.textureProblems[platformType] = 0;
                            statusData.textureProblems[platformType]++;
                        }
                    }

                    // 检查动画问题
                    if (resource is AnimationClip clip)
                    {
                        // 需要找到对应的controller
                        RuntimeAnimatorController controller = FindControllerForClip(clip, analyzer);
                        if (controller != null && conditionManager.CheckAnimationConditions(clip, controller, analyzer, out _))
                        {
                            problemResources.Add(resource);
                            string problemType = GetAnimationProblemType(clip, controller, analyzer, conditionManager);
                            if (!statusData.animationProblems.ContainsKey(problemType))
                                statusData.animationProblems[problemType] = 0;
                            statusData.animationProblems[problemType]++;
                        }
                    }

                    // 检查粒子问题
                    if (resource is ParticleSystem ps)
                    {
                        if (conditionManager.CheckParticleConditions(ps, out _))
                        {
                            problemResources.Add(resource);
                            string problemType = GetParticleProblemType(ps, conditionManager);
                            if (!statusData.particleProblems.ContainsKey(problemType))
                                statusData.particleProblems[problemType] = 0;
                            statusData.particleProblems[problemType]++;
                        }
                    }
                }
            }

            statusData.totalResourceCount = allResources.Count;
            statusData.problemResourceCount = problemResources.Count;
            statusData.commonResourceCount = session.CommonResources.Count;
        }

        // 辅助方法：获取问题类型
        private string GetNameProblemType(string name, ConditionManager conditionManager)
        {
            if (conditionManager.Settings.checkNameSpace && name.Contains(" "))
                return "空格命名";
            if (conditionManager.Settings.checkNameCustom && !string.IsNullOrEmpty(conditionManager.Settings.nameCustomString) && 
                name.Contains(conditionManager.Settings.nameCustomString))
                return $"包含\"{conditionManager.Settings.nameCustomString}\"";
            if (conditionManager.Settings.checkNameUppercase && conditionManager.ContainsUppercase(name))
                return "首字母大写";
            return "未知";
        }

        private string GetMeshProblemType(Mesh mesh, ConditionManager conditionManager)
        {
            if (conditionManager.Settings.checkVertexCount)
            {
                if (mesh.vertexCount > conditionManager.Settings.vertexCountError)
                    return $"顶点超限(>{conditionManager.Settings.vertexCountError})";
                if (mesh.vertexCount > conditionManager.Settings.vertexCountWarning)
                    return $"顶点警告(>{conditionManager.Settings.vertexCountWarning})";
            }
            if (conditionManager.Settings.checkTriangleCount)
            {
                int triangleCount = mesh.triangles.Length / 3;
                if (triangleCount > conditionManager.Settings.triangleCountError)
                    return $"三角面超限(>{conditionManager.Settings.triangleCountError})";
                if (triangleCount > conditionManager.Settings.triangleCountWarning)
                    return $"三角面警告(>{conditionManager.Settings.triangleCountWarning})";
            }
            return "未知";
        }

        private string GetTextureProblemType(Texture2D texture, ConditionManager conditionManager)
        {
            if (conditionManager.Settings.checkTextureSize)
            {
                if (texture.width > conditionManager.Settings.textureSizeError || texture.height > conditionManager.Settings.textureSizeError)
                    return $"尺寸超限(>{conditionManager.Settings.textureSizeError})";
                if (texture.width > conditionManager.Settings.textureSizeWarning || texture.height > conditionManager.Settings.textureSizeWarning)
                    return $"尺寸警告(>{conditionManager.Settings.textureSizeWarning})";
            }
            if (conditionManager.Settings.checkDDSFormat)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(path) && path.ToLower().EndsWith(".dds"))
                    return "禁止DDS格式";
            }
            return "未知";
        }

        private string GetAnimationProblemType(AnimationClip clip, RuntimeAnimatorController controller,
            ResourceAnalyzer analyzer, ConditionManager conditionManager)
        {
            int clipCount = 0;
            if (conditionManager.Settings.checkClipCount && analyzer.AnimatorClipCount.TryGetValue(controller, out clipCount) && clipCount > 1)
                return "剪辑数大于1";
            if (conditionManager.Settings.checkFPS && Mathf.Abs(clip.frameRate - conditionManager.Settings.targetFPS) > 0.01f)
                return $"FPS(≠{conditionManager.Settings.targetFPS})";
            if (conditionManager.Settings.checkLooping && clip.isLooping)
                return "需为循环";
            return "未知";
        }

        private string GetParticleProblemType(ParticleSystem ps, ConditionManager conditionManager)
        {
            var main = ps.main;
            if (conditionManager.Settings.checkStartDelay && main.startDelay.constant > conditionManager.Settings.startDelayThreshold)
                return $"StartDelay(>{conditionManager.Settings.startDelayThreshold})";
            if (conditionManager.Settings.checkMaxParticles && main.maxParticles > conditionManager.Settings.maxParticlesThreshold)
                return $"MaxParticles(>{conditionManager.Settings.maxParticlesThreshold})";
            // 其他粒子问题类型...
            return "未知";
        }

        private RuntimeAnimatorController FindControllerForClip(AnimationClip clip, ResourceAnalyzer analyzer)
        {
            foreach (var resource in analyzer.ResourceUsage.Keys)
            {
                if (resource is RuntimeAnimatorController controller)
                {
                    if (controller.animationClips != null && controller.animationClips.Contains(clip))
                        return controller;
                }
            }
            return null;
        }
    }
}

