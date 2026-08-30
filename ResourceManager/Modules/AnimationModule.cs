using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;
using ResourceManager;

namespace ResourceManager.Modules
{
    public class AnimationModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("动画", EditorStyles.boldLabel);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先进行分析", MessageType.Info);
                return;
            }

            // 使用相同的多对象逻辑，直接从 ResourceUsage 获取Animator
            var allAnimators = new Dictionary<string, List<RuntimeAnimatorController>>();

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                // 直接从 ResourceUsage 获取 RuntimeAnimatorController 使用情况
                var animators = analyzer.ResourceUsage.Keys
                    .Where(r => r is RuntimeAnimatorController)
                    .Cast<RuntimeAnimatorController>()
                    .Distinct()
                    .ToList();

                if (animators.Count > 0)
                {
                    allAnimators[objectName] = animators;
                }
            }

            if (allAnimators.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的动画", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未找到动画", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" — 在动画剪辑中过滤", MessageType.Info);
            }

            // 显示共有动画
            DrawCommonAnimations(session);

            // 显示每个对象的动画
            foreach (var kvp in allAnimators)
            {
                string objectName = kvp.Key;
                var animators = kvp.Value;
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
                    objectFoldouts[objectName], $"{objectName} ({animators.Count}个)", true);

                if (objectFoldouts[objectName])
                {
                    EditorGUI.indentLevel++;
                    foreach (var controller in animators)
                    {
                        DrawAnimatorClips(controller, analyzer);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawCommonAnimations(AnalysisSession session)
        {
            if (session.CommonResources.Count == 0) return;

            var commonAnimators = session.CommonResources.Keys
                .Where(r => r is RuntimeAnimatorController)
                .Cast<RuntimeAnimatorController>()
                .ToList();

            // 注意：共有动画的搜索过滤在 DrawAnimatorClips 的剪辑级别进行
            if (commonAnimators.Count == 0) return;

            EditorGUILayout.BeginVertical("box");

            string commonKey = "共有动画";

            if (!objectFoldouts.ContainsKey(commonKey))
            {
                objectFoldouts[commonKey] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                objectFoldouts[commonKey] = true;

            objectFoldouts[commonKey] = EditorGUILayout.Foldout(
                objectFoldouts[commonKey], $"共有动画 ({commonAnimators.Count}个)", true);

            if (objectFoldouts[commonKey])
            {
                EditorGUI.indentLevel++;
                foreach (var controller in commonAnimators)
                {
                    // 找到包含此RuntimeAnimatorController的分析器
                    var analyzer = session.Analyzers.Values.FirstOrDefault(a =>
                        a.ResourceUsage.ContainsKey(controller));
                    if (analyzer != null)
                    {
                        DrawAnimatorClips(controller, analyzer);
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawAnimatorClips(RuntimeAnimatorController controller, ResourceAnalyzer analyzer)
        {
            var allClips = new List<AnimationClip>();
            var clipToAnimator = new Dictionary<AnimationClip, RuntimeAnimatorController>();

            AnimationClip[] clips = controller.animationClips;
            foreach (var clip in clips)
            {
                if (clip != null)
                {
                    // 应用搜索过滤
                    if (!string.IsNullOrEmpty(SearchFilter))
                    {
                        string filter = SearchFilter.ToLower();
                        if (!clip.name.ToLower().Contains(filter))
                            continue;
                    }

                    allClips.Add(clip);
                    if (!clipToAnimator.ContainsKey(clip))
                    {
                        clipToAnimator[clip] = controller;
                    }
                }
            }

            // 搜索时如果没有匹配的剪辑，跳过此 Animator
            if (!string.IsNullOrEmpty(SearchFilter) && allClips.Count == 0)
                return;

            if (allClips.Count > 0)
            {
                var distinctClips = allClips.Distinct().ToList();
                for (int ci = 0; ci < distinctClips.Count; ci++)
                {
                    using (new UIHelper.ZebraScope(ci))
                    {
                        DrawAnimationClip(distinctClips[ci], clipToAnimator[distinctClips[ci]], analyzer);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("未找到动画片段", MessageType.Info);
            }
        }

        private void DrawAnimationClip(AnimationClip clip, RuntimeAnimatorController controller, ResourceAnalyzer analyzer)
        {
            EditorGUILayout.BeginVertical("helpbox");

            // 使用ConditionManager进行条件判断
            ConditionManager conditionManager = ConditionManager.Instance;
            Color? highlightColor = null;

            // 检查动画条件
            if (conditionManager.CheckAnimationConditions(clip, controller, analyzer, out Color? animationColor))
            {
                highlightColor = animationColor;
            }
            // 如果未命中动画条件，再检查命名条件
            else if (conditionManager.CheckNameConditions(clip.name, out Color? nameColor))
            {
                highlightColor = nameColor;
            }

            float fps = clip.frameRate;
            bool fpsNot30 = Mathf.Abs(fps - conditionManager.Settings.targetFPS) > 0.01f;
            bool isLooping = clip.isLooping;
            float length = clip.length;

            Color originalColor = GUI.color;
            if (highlightColor.HasValue)
            {
                GUI.color = highlightColor.Value;
            }

            GUILayout.BeginHorizontal();

            if (!foldouts.ContainsKey(clip))
            {
                foldouts[clip] = false;
            }
            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[clip] = true;
            foldouts[clip] = EditorGUILayout.Foldout(foldouts[clip], clip.name, true);

            GUILayout.FlexibleSpace();

            // 绘制动画属性
            DrawAnimationProperties(length, fps, isLooping, fpsNot30, conditionManager);

            // 条件设置按钮
            if (GUILayout.Button("设置条件", GUILayout.Width(70)))
            {
                ConditionSettingsWindow.ShowWindowFromButton();
            }

            // 右侧只读的对象域
            EditorGUILayout.ObjectField("", clip, typeof(AnimationClip), false, GUILayout.Width(100));

            GUILayout.EndHorizontal();

            GUI.color = originalColor;

            // 展开显示详细信息
            if (foldouts[clip])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                // 显示详细属性
                UIHelper.DrawPropertyLabel("时长:", $"{length:F2}s");
                UIHelper.DrawPropertyLabel("FPS:", fps.ToString());
                UIHelper.DrawPropertyLabel("循环:", isLooping ? "是" : "否");
                UIHelper.DrawPropertyLabel("帧数:", $"{(length * fps):F0}");

                // 显示使用路径
                if (analyzer.ResourceUsage.ContainsKey(controller))
                {
                    EditorGUILayout.LabelField("使用路径:", EditorStyles.miniBoldLabel);
                    foreach (var usage in analyzer.ResourceUsage[controller])
                    {
                        UIHelper.DrawUsagePath(usage, foldouts, clip);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationProperties(float length, float fps, bool isLooping, bool fpsNot30, ConditionManager conditionManager)
        {
            GUILayout.BeginHorizontal();

            // 开始括号
            GUILayout.Label("[", EditorStyles.label, GUILayout.ExpandWidth(false));

            // 时长
            GUILayout.Label($"时长: {length:F2}s, ", EditorStyles.label, GUILayout.ExpandWidth(false));

            // FPS - 如果不是30显示高亮色
            if (fpsNot30 && conditionManager.Settings.checkFPS)
            {
                Color original = GUI.color;
                GUI.color = conditionManager.Settings.fpsColor;
                GUILayout.Label($"FPS: {fps}, ", EditorStyles.label, GUILayout.ExpandWidth(false));
                GUI.color = original;
            }
            else
            {
                GUILayout.Label($"FPS: {fps}, ", EditorStyles.label, GUILayout.ExpandWidth(false));
            }

            // 循环信息 - 如果为循环则显示高亮色
            GUILayout.Label("循环: ", EditorStyles.label, GUILayout.ExpandWidth(false));
            if (isLooping && conditionManager.Settings.checkLooping)
            {
                Color original = GUI.color;
                GUI.color = conditionManager.Settings.loopingColor;
                GUILayout.Label("是", EditorStyles.label, GUILayout.ExpandWidth(false));
                GUI.color = original;
            }
            else
            {
                GUILayout.Label("否", EditorStyles.label, GUILayout.ExpandWidth(false));
            }

            // 结束括号
            GUILayout.Label("]", EditorStyles.label, GUILayout.ExpandWidth(false));

            GUILayout.EndHorizontal();
        }

        public void Clear()
        {
            foldouts.Clear();
            objectFoldouts.Clear();
        }

        // 原来的 Draw 方法保留占位，不实现
        public void Draw(ResourceAnalyzer analyzer, ResourceCache cache)
        {
            // 实例化兼容的单对象版本时保留原逻辑
        }
    }
}