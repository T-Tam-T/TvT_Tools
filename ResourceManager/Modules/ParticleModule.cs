using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    public class ParticleModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("粒子系统", EditorStyles.boldLabel);
            DrawConditionHeader();

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先进行分析", MessageType.Info);
                return;
            }

            // 使用相同的多对象逻辑，直接从 ResourceUsage 获取粒子系统
            var allParticles = new Dictionary<string, List<ParticleSystem>>();

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                // 直接从 ResourceUsage 获取 ParticleSystem 使用情况
                var particles = analyzer.ResourceUsage.Keys
                    .Where(r => r is ParticleSystem)
                    .Cast<ParticleSystem>()
                    .Distinct()
                    .ToList();

                // 应用搜索过滤
                if (!string.IsNullOrEmpty(SearchFilter))
                {
                    string filter = SearchFilter.ToLower();
                    particles = particles.Where(ps => ps.name.ToLower().Contains(filter)).ToList();
                }

                if (particles.Count > 0)
                {
                    allParticles[objectName] = particles;
                }
            }

            if (allParticles.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的粒子系统", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未找到粒子系统", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                int totalMatches = allParticles.Values.Sum(list => list.Count);
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {totalMatches} 个匹配粒子系统", MessageType.Info);
            }

            // 显示共有粒子系统
            DrawCommonParticles(session);

            // 显示每个对象的粒子系统
            foreach (var kvp in allParticles)
            {
                string objectName = kvp.Key;
                var particles = kvp.Value;
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
                    objectFoldouts[objectName], $"{objectName} ({particles.Count}个)", true);

                if (objectFoldouts[objectName])
                {
                    EditorGUI.indentLevel++;
                    for (int pi = 0; pi < particles.Count; pi++)
                    {
                        using (new UIHelper.ZebraScope(pi))
                        {
                            DrawParticleSystem(particles[pi], analyzer);
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawCommonParticles(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            var commonParticles = session.CommonResources.Keys
                .Where(r => r is ParticleSystem)
                .Cast<ParticleSystem>()
                .ToList();

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                commonParticles = commonParticles.Where(ps => ps.name.ToLower().Contains(filter)).ToList();
            }

            if (commonParticles.Count == 0) return;

            EditorGUILayout.BeginVertical("box");

            string commonKey = "共有粒子系统";

            if (!objectFoldouts.ContainsKey(commonKey))
            {
                objectFoldouts[commonKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[commonKey] = true;

            objectFoldouts[commonKey] = EditorGUILayout.Foldout(
                objectFoldouts[commonKey], $"共有粒子系统 ({commonParticles.Count}个)", true);

            if (objectFoldouts[commonKey])
            {
                EditorGUI.indentLevel++;
                for (int pi = 0; pi < commonParticles.Count; pi++)
                {
                    using (new UIHelper.ZebraScope(pi))
                    {
                        // 找到包含此公共粒子系统的分析器
                        var analyzer = session.Analyzers.Values.FirstOrDefault(a =>
                            a.ResourceUsage.ContainsKey(commonParticles[pi]));
                        if (analyzer != null)
                        {
                            DrawParticleSystem(commonParticles[pi], analyzer);
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawConditionHeader()
        {
            GUIStyle richTextStyle = new GUIStyle(EditorStyles.label);
            richTextStyle.richText = true;

            ConditionSettings settings = ConditionManager.Instance.Settings;

            string header = $"[开始延迟：<color=#{ColorUtility.ToHtmlStringRGB(settings.startDelayColor)}>s(>{settings.startDelayThreshold:F1})</color> " +
                            $"最大粒子数：<color=#{ColorUtility.ToHtmlStringRGB(settings.maxParticlesColor)}>MP(>{settings.maxParticlesThreshold})</color> " +
                            settings.MaxParticleSizeDescription + " " +
                            $"排序层级不等：<color=#{ColorUtility.ToHtmlStringRGB(settings.orderInLayerColor)}>OI(≠{settings.orderInLayerThreshold})</color> " +
                            $"缩放模式非层级：<color=#{ColorUtility.ToHtmlStringRGB(settings.scalingModeColor)}>SM(≠Hierarchy)</color> " +
                            $"层级非默认：<color=#{ColorUtility.ToHtmlStringRGB(settings.layerColor)}>L(≠Default)</color> " +
                            $"未激活：<color=#{ColorUtility.ToHtmlStringRGB(settings.activeColor)}>At(未)</color>]";

            EditorGUILayout.LabelField(header, richTextStyle);
            GUILayout.Space(5);
        }

// 修改DrawParticleSystem方法中的粒子系统检查
        private void DrawParticleSystem(ParticleSystem ps, ResourceAnalyzer analyzer)
        {
            // 安全检查
            if (ps == null || ps.Equals(null))
            {
                EditorGUILayout.HelpBox("粒子系统已失效或已被销毁", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical("helpbox");

            // 使用ConditionManager检查粒子系统条件
            ConditionManager conditionManager = ConditionManager.Instance;
            
            // 安全的检查
            Color? color = null;
            bool hasCondition = false;
            try
            {
                hasCondition = conditionManager.CheckParticleConditions(ps, out color);
            }
            catch (System.NullReferenceException)
            {
                // 粒子系统可能已被销毁
                EditorGUILayout.HelpBox("无法检查粒子系统条件", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 获取粒子系统属性
            ParticleSystem.MainModule main;
            ParticleSystemRenderer renderer = null;
            
            try
            {
                main = ps.main;
                renderer = ps.GetComponent<ParticleSystemRenderer>();
            }
            catch (System.NullReferenceException)
            {
                EditorGUILayout.HelpBox("无法获取粒子系统属性", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            float startDelay = main.startDelay.constant;
            int maxParticles = main.maxParticles;

            float maxParticleSize = renderer != null ? renderer.maxParticleSize : 0f;
            int orderInLayer = renderer != null ? renderer.sortingOrder : 0;
            ParticleSystemScalingMode scalingMode = main.scalingMode;

            // 获取Layer和Active状态
            int layer = ps.gameObject.layer;
            string layerInitial = GetLayerInitial(layer);
            bool isActive = ps.gameObject.activeInHierarchy;
            string activeState = isActive ? "是" : "未";

            Color originalColor = GUI.color;
            if (color.HasValue)
            {
                GUI.color = color.Value;
            }

            GUILayout.BeginHorizontal();

            if (!foldouts.ContainsKey(ps))
            {
                foldouts[ps] = false;
            }
            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[ps] = true;
            foldouts[ps] = EditorGUILayout.Foldout(foldouts[ps], ps.name, true);

            GUILayout.FlexibleSpace();

            // 参数行
            try
            {
                DrawParticleProperties(startDelay, maxParticles, maxParticleSize, orderInLayer,
                                    scalingMode, layerInitial, activeState, conditionManager, renderer);
            }
            catch (System.NullReferenceException)
            {
                // 忽略绘制异常
            }

            // 右侧ObjectField（与材质模块同宽200）
            EditorGUILayout.ObjectField("", ps, typeof(ParticleSystem), false, GUILayout.Width(200));

            GUILayout.EndHorizontal();

            GUI.color = originalColor;

            // 展开显示详细信息
            if (foldouts[ps])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                try
                {
                    // 详细属性
                    UIHelper.DrawPropertyLabel("开始延迟:", $"{startDelay:F2}s");
                    UIHelper.DrawPropertyLabel("最大粒子数:", maxParticles.ToString());
                    UIHelper.DrawPropertyLabel("最大粒子尺寸:", $"{maxParticleSize:F2}");
                    UIHelper.DrawPropertyLabel("排序层级:", orderInLayer.ToString());
                    UIHelper.DrawPropertyLabel("缩放模式:", scalingMode.ToString());
                    UIHelper.DrawPropertyLabel("层级:", LayerMask.LayerToName(layer));
                    UIHelper.DrawPropertyLabel("激活状态:", isActive ? "是" : "未");

                    // 使用路径
                    if (analyzer.ResourceUsage.ContainsKey(ps))
                    {
                        EditorGUILayout.LabelField("使用位置:", EditorStyles.miniBoldLabel);
                        foreach (var usage in analyzer.ResourceUsage[ps])
                        {
                            UIHelper.DrawUsagePath(usage, foldouts, ps);
                        }
                    }
                }
                catch (System.NullReferenceException)
                {
                    EditorGUILayout.HelpBox("粒子系统已失效", MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawParticleProperties(float startDelay, int maxParticles, float maxParticleSize,
                                          int orderInLayer, ParticleSystemScalingMode scalingMode,
                                          string layerInitial, string activeState,
                                          ConditionManager conditionManager, ParticleSystemRenderer renderer)
        {
            GUILayout.BeginHorizontal();

            // 起始标记
            GUILayout.Label("[", GUILayout.ExpandWidth(false));

            // 开始延迟
            bool highlightStartDelay = conditionManager.Settings.checkStartDelay &&
                                       startDelay > conditionManager.Settings.startDelayThreshold;
            DrawColoredProperty($"s:{startDelay:0.##},", highlightStartDelay, conditionManager.Settings.startDelayColor);

            // 最大粒子数
            bool highlightMaxParticles = conditionManager.Settings.checkMaxParticles &&
                                         maxParticles > conditionManager.Settings.maxParticlesThreshold;
            DrawColoredProperty($"MP:{maxParticles},", highlightMaxParticles, conditionManager.Settings.maxParticlesColor);

            // 最大粒子尺寸
            bool highlightMaxSize = renderer != null &&
                conditionManager.Settings.checkMaxParticleSize &&
                renderer.renderMode != ParticleSystemRenderMode.Mesh &&
                Mathf.Abs(maxParticleSize - conditionManager.Settings.maxParticleSizeThreshold) > 0.01f;
            DrawColoredProperty($"MPS:{maxParticleSize:0.##},", highlightMaxSize, conditionManager.Settings.maxParticleSizeColor);

            // 排序层级
            bool highlightOrderInLayer = conditionManager.Settings.checkOrderInLayer &&
                                         orderInLayer != conditionManager.Settings.orderInLayerThreshold;
            DrawColoredProperty($"Ol:{orderInLayer},", highlightOrderInLayer, conditionManager.Settings.orderInLayerColor);

            // 缩放模式
            bool highlightScalingMode = conditionManager.Settings.checkScalingMode &&
                                        scalingMode != ParticleSystemScalingMode.Hierarchy;
            DrawColoredProperty($"SM:{GetScalingModeInitial(scalingMode)},", highlightScalingMode, conditionManager.Settings.scalingModeColor);

            // 层级
            bool highlightLayer = conditionManager.Settings.checkLayer &&
                                  layerInitial != "D"; // Default
            DrawColoredProperty($"L:{layerInitial},", highlightLayer, conditionManager.Settings.layerColor);

            // 激活状态
            bool highlightActive = conditionManager.Settings.checkActive && activeState == "未";
            DrawColoredProperty($"At:{activeState}", highlightActive, conditionManager.Settings.activeColor);

            // 结束标记
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

        // 获取缩放模式首字母
        private string GetScalingModeInitial(ParticleSystemScalingMode mode)
        {
            switch (mode)
            {
                case ParticleSystemScalingMode.Hierarchy: return "H";
                case ParticleSystemScalingMode.Local: return "L";
                case ParticleSystemScalingMode.Shape: return "S";
                default: return "?";
            }
        }

        // 获取层级首字母
        private string GetLayerInitial(int layer)
        {
            string layerName = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName)) return "D"; // Default

            // 主要类别首字母
            if (layerName.StartsWith("UI")) return "U";
            if (layerName.StartsWith("Effect")) return "E";
            if (layerName.StartsWith("Character")) return "C";

            return layerName.Substring(0, 1);
        }

        public void Clear()
        {
            foldouts.Clear();
            objectFoldouts.Clear();
        }

        // 保留原有的单对象方法，仅占位
        public void Draw(ResourceAnalyzer analyzer, ResourceCache cache)
        {
            // 实现可扩展的单对象版本，保留原有逻辑
        }
    }
}