using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    public class OverviewModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> problemTypeFoldouts = new Dictionary<string, bool>();

        // 问题资源数据结构
        private class ProblemResource
        {
            public UnityEngine.Object Resource;
            public string ResourceName;
            public string ProblemType;
            public string Category;
            public Color ProblemColor;
            public string ObjectName;
            public List<string> UsagePaths = new List<string>();
        }

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("问题概览", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先进行分析", MessageType.Info);
                return;
            }

            // 收集所有问题资源
            List<ProblemResource> allProblems = CollectAllProblems(session);

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                allProblems = allProblems.Where(p =>
                    (p.ResourceName != null && p.ResourceName.ToLower().Contains(filter)) ||
                    (p.ObjectName != null && p.ObjectName.ToLower().Contains(filter)) ||
                    (p.ProblemType != null && p.ProblemType.ToLower().Contains(filter))
                ).ToList();
            }

            if (allProblems.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的资源", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("✓ 未发现问题，所有资源符合规则！", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {allProblems.Count} 个匹配资源", MessageType.Info);
            }

            // 按类别分组显示
            DrawProblemsByCategory(allProblems);
        }

        private List<ProblemResource> CollectAllProblems(AnalysisSession session)
        {
            List<ProblemResource> problems = new List<ProblemResource>();
            ConditionManager conditionManager = ConditionManager.Instance;

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                foreach (var resource in analyzer.ResourceUsage.Keys)
                {
                    if (resource == null) continue;

                    // 检查命名问题
                    if (conditionManager.CheckNameConditions(resource.name, out Color? nameColor))
                    {
                        problems.Add(new ProblemResource
                        {
                            Resource = resource,
                            ResourceName = resource.name,
                            ProblemType = GetNameProblemType(resource.name, conditionManager),
                            Category = "命名规范",
                            ProblemColor = nameColor.Value,
                            ObjectName = objectName,
                            UsagePaths = analyzer.ResourceUsage[resource].ToList()
                        });
                    }

                    // 检查网格问题
                    if (resource is Mesh mesh)
                    {
                        if (conditionManager.CheckMeshConditions(mesh, out Color? meshColor))
                        {
                            problems.Add(new ProblemResource
                            {
                                Resource = resource,
                                ResourceName = resource.name,
                                ProblemType = GetMeshProblemType(mesh, conditionManager),
                                Category = "网格",
                                ProblemColor = meshColor.Value,
                                ObjectName = objectName,
                                UsagePaths = analyzer.ResourceUsage[resource].ToList()
                            });
                        }
                    }

                    // 检查贴图问题
                    if (resource is Texture2D texture)
                    {
                        if (conditionManager.CheckTextureConditions(texture, out Color? textureColor))
                        {
                            problems.Add(new ProblemResource
                            {
                                Resource = resource,
                                ResourceName = resource.name,
                                ProblemType = GetTextureProblemType(texture, conditionManager),
                                Category = "贴图",
                                ProblemColor = textureColor.Value,
                                ObjectName = objectName,
                                UsagePaths = analyzer.ResourceUsage[resource].ToList()
                            });
                        }

                        // 检查贴图平台设置问题
                        if (conditionManager.CheckTexturePlatformConditions(texture, out Color? platformColor, out string platformProblemType))
                        {
                            problems.Add(new ProblemResource
                            {
                                Resource = resource,
                                ResourceName = resource.name,
                                ProblemType = platformProblemType,
                                Category = "贴图平台设置",
                                ProblemColor = platformColor.Value,
                                ObjectName = objectName,
                                UsagePaths = analyzer.ResourceUsage[resource].ToList()
                            });
                        }
                    }

                    // 检查动画问题
                    if (resource is AnimationClip clip)
                    {
                        RuntimeAnimatorController controller = FindControllerForClip(clip, analyzer);
                        if (controller != null && conditionManager.CheckAnimationConditions(clip, controller, analyzer, out Color? animColor))
                        {
                            problems.Add(new ProblemResource
                            {
                                Resource = resource,
                                ResourceName = resource.name,
                                ProblemType = GetAnimationProblemType(clip, controller, analyzer, conditionManager),
                                Category = "动画",
                                ProblemColor = animColor.Value,
                                ObjectName = objectName,
                                UsagePaths = analyzer.ResourceUsage[controller].ToList()
                            });
                        }
                    }

                    // 检查粒子问题
                    if (resource is ParticleSystem ps)
                    {
                        AddParticleProblems(ps, analyzer, conditionManager, problems, objectName);
                    }
                }
            }

            // 冗余检测：材质 Shader toggle OFF 但对应贴图槽非空
            if (conditionManager.Settings.checkRedundancy)
            {
                var checkedMaterials = new HashSet<Material>();
                foreach (var kvp in session.Analyzers)
                {
                    var analyzer = kvp.Value;
                    var targetObject = kvp.Key;
                    string objectName = targetObject.name;

                    foreach (var resource in analyzer.ResourceUsage.Keys)
                    {
                        if (resource is Material mat && !checkedMaterials.Contains(mat))
                        {
                            checkedMaterials.Add(mat);
                            var redundancies = conditionManager.CheckMaterialRedundancy(mat);
                            foreach (var (desc, tex, propName) in redundancies)
                            {
                                problems.Add(new ProblemResource
                                {
                                    Resource = mat,
                                    ResourceName = mat.name,
                                    ProblemType = $"Shader Toggle冗余({desc}：{propName}={tex.name})",
                                    Category = "冗余检测",
                                    ProblemColor = conditionManager.Settings.redundancyColor,
                                    ObjectName = objectName,
                                    UsagePaths = analyzer.ResourceUsage.ContainsKey(mat)
                                        ? analyzer.ResourceUsage[mat].ToList()
                                        : new List<string>()
                                });
                            }
                        }
                    }
                }
            }

            return problems;
        }
        
        private void AddParticleProblems(ParticleSystem ps, ResourceAnalyzer analyzer, ConditionManager conditionManager, 
                                         List<ProblemResource> problems, string objectName)
        {
            // 安全检查
            if (ps == null || ps.Equals(null))
            {
                return;
            }

            try
            {
                var main = ps.main;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                var emission = ps.emission;
                var shape = ps.shape;
                var noise = ps.noise;
                var velocityOverLifetime = ps.velocityOverLifetime;
                var colorOverLifetime = ps.colorOverLifetime;
                var sizeOverLifetime = ps.sizeOverLifetime;
                var limitVelocityOverLifetime = ps.limitVelocityOverLifetime;
                var inheritVelocity = ps.inheritVelocity;
                var forceOverLifetime = ps.forceOverLifetime;
                var colorBySpeed = ps.colorBySpeed;
                var sizeBySpeed = ps.sizeBySpeed;
                var rotationOverLifetime = ps.rotationOverLifetime;
                var rotationBySpeed = ps.rotationBySpeed;
                var externalForces = ps.externalForces;
                var collision = ps.collision;
                var trigger = ps.trigger;
                var subEmitters = ps.subEmitters;
                var textureSheetAnimation = ps.textureSheetAnimation;
                var lights = ps.lights;
                var trails = ps.trails;
                var customData = ps.customData;

                // 粒子基础问题
                if (conditionManager.Settings.checkStartDelay && main.startDelay.constant > conditionManager.Settings.startDelayThreshold)
                {
                    problems.Add(CreateParticleProblem(ps, "StartDelay", conditionManager.Settings.startDelayColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (conditionManager.Settings.checkMaxParticles && main.maxParticles > conditionManager.Settings.maxParticlesThreshold)
                {
                    problems.Add(CreateParticleProblem(ps, "MaxParticles", conditionManager.Settings.maxParticlesColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (renderer != null && conditionManager.Settings.checkMaxParticleSize &&
                    renderer.renderMode != ParticleSystemRenderMode.Mesh &&
                    Mathf.Abs(renderer.maxParticleSize - conditionManager.Settings.maxParticleSizeThreshold) > 0.01f)
                {
                    problems.Add(CreateParticleProblem(ps, "MaxParticleSize", conditionManager.Settings.maxParticleSizeColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (renderer != null && conditionManager.Settings.checkOrderInLayer &&
                    renderer.sortingOrder != conditionManager.Settings.orderInLayerThreshold)
                {
                    problems.Add(CreateParticleProblem(ps, "OrderInLayer", conditionManager.Settings.orderInLayerColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (conditionManager.Settings.checkScalingMode &&
                    main.scalingMode != ParticleSystemScalingMode.Hierarchy)
                {
                    problems.Add(CreateParticleProblem(ps, "ScalingMode", conditionManager.Settings.scalingModeColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (conditionManager.Settings.checkLayer && ps.gameObject.layer != LayerMask.NameToLayer("Default"))
                {
                    problems.Add(CreateParticleProblem(ps, "Layer", conditionManager.Settings.layerColor, 
                        analyzer, objectName, "粒子基础"));
                }
                
                if (conditionManager.Settings.checkActive && !ps.gameObject.activeInHierarchy)
                {
                    problems.Add(CreateParticleProblem(ps, "Active", conditionManager.Settings.activeColor, 
                        analyzer, objectName, "粒子基础"));
                }

                // 冗余检测（按 Excel 规则表）
                if (renderer != null && conditionManager.Settings.checkRedundancy)
                {
                    int mode = (int)renderer.renderMode;
                    bool isNoneMode = mode < 0 || mode >= 5;
                    bool isMeshMode = renderer.renderMode == ParticleSystemRenderMode.Mesh;

                    // 规则2: RenderMode=None + renderer.enabled + 非默认资源 → 简单清理提示
                    if (isNoneMode && renderer.enabled)
                    {
                        if (renderer.sharedMaterial != null && renderer.sharedMaterial.name != "ParticlesUnlit")
                        {
                            problems.Add(CreateParticleProblem(ps, "清理材质：" + renderer.sharedMaterial.name,
                                conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                        }
                        if (renderer.mesh != null && renderer.mesh.name != "Cube")
                        {
                            problems.Add(CreateParticleProblem(ps, "清理模型：" + renderer.mesh.name,
                                conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                        }

                        // 规则7: 默认资源 ParticlesUnlit/Cube → 完整清理操作
                        if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "ParticlesUnlit")
                        {
                            problems.Add(CreateParticleProblem(ps, "清理材质：" + renderer.sharedMaterial.name + "，设置渲染模式为None，关闭渲染",
                                conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                        }
                        if (renderer.mesh != null && renderer.mesh.name == "Cube")
                        {
                            problems.Add(CreateParticleProblem(ps, "清理模型：" + renderer.mesh.name + "，设置渲染模式为None，关闭渲染",
                                conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                        }
                    }

                    // 规则3: RenderMode=Mesh + mesh空 + 材质非空
                    if (isMeshMode && renderer.mesh == null && renderer.sharedMaterial != null)
                    {
                        problems.Add(CreateParticleProblem(ps, "清理材质：" + renderer.sharedMaterial.name + "（改None关渲染）",
                            conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                    }

                    // 规则4: Trails未启用 + trailMaterial非空
                    if (!ps.trails.enabled && renderer.trailMaterial != null)
                    {
                        problems.Add(CreateParticleProblem(ps, "清理条带材质：" + renderer.trailMaterial.name,
                            conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                    }

                    // 规则5: RenderMode≠Mesh(非None) + mesh非空（None模式已在规则2覆盖）
                    if (!isNoneMode && !isMeshMode && renderer.mesh != null)
                    {
                        problems.Add(CreateParticleProblem(ps, "清理渲染模型：" + renderer.mesh.name,
                            conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                    }
                }

                // 规则6: Shape≠Mesh/MeshRenderer/SkinnedMeshRenderer + mesh非空
                if (conditionManager.Settings.checkRedundancy && shape.enabled)
                {
                    var st = shape.shapeType;
                    if (st != ParticleSystemShapeType.Mesh
                        && st != ParticleSystemShapeType.MeshRenderer
                        && st != ParticleSystemShapeType.SkinnedMeshRenderer
                        && shape.mesh != null)
                    {
                        problems.Add(CreateParticleProblem(ps, "清理发射器模型：" + shape.mesh.name,
                            conditionManager.Settings.redundancyColor, analyzer, objectName, "冗余检测"));
                    }
                }
                
                // 粒子模块问题 - 只检查被勾选的模块
                if (conditionManager.Settings.checkParticleEmission && emission.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Emission模块", conditionManager.Settings.particleEmissionColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleShape && shape.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Shape模块", conditionManager.Settings.particleShapeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleNoise && noise.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Noise模块", conditionManager.Settings.particleNoiseColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleVelocityOverLifetime && velocityOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "VelocityOverLifetime模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleColorOverLifetime && colorOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "ColorOverLifetime模块", conditionManager.Settings.particleColorOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleSizeOverLifetime && sizeOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "SizeOverLifetime模块", conditionManager.Settings.particleSizeOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                // 新增粒子模块检查 - 只检查被勾选的模块
                if (conditionManager.Settings.checkParticleLimitVelocityOverLifetime && limitVelocityOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "LimitVelocityOverLifetime模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleInheritVelocity && inheritVelocity.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "InheritVelocity模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleForceOverLifetime && forceOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "ForceOverLifetime模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleColorBySpeed && colorBySpeed.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "ColorBySpeed模块", conditionManager.Settings.particleColorOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleSizeBySpeed && sizeBySpeed.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "SizeBySpeed模块", conditionManager.Settings.particleSizeOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleRotationOverLifetime && rotationOverLifetime.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "RotationOverLifetime模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleRotationBySpeed && rotationBySpeed.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "RotationBySpeed模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleExternalForces && externalForces.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "ExternalForces模块", conditionManager.Settings.particleVelocityOverLifetimeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleCollision && collision.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Collision模块", conditionManager.Settings.particleShapeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleTrigger && trigger.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Trigger模块", conditionManager.Settings.particleShapeColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleSubEmitters && subEmitters.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "SubEmitters模块", conditionManager.Settings.particleEmissionColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleTextureSheetAnimation && textureSheetAnimation.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "TextureSheetAnimation模块", conditionManager.Settings.particleNoiseColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleLights && lights.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Lights模块", conditionManager.Settings.particleNoiseColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleTrails && trails.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "Trails模块", conditionManager.Settings.particleNoiseColor, 
                        analyzer, objectName, "粒子模块"));
                }
                
                if (conditionManager.Settings.checkParticleCustomData && customData.enabled)
                {
                    problems.Add(CreateParticleProblem(ps, "CustomData模块", conditionManager.Settings.particleNoiseColor, 
                        analyzer, objectName, "粒子模块"));
                }
            }
            catch (System.NullReferenceException)
            {
                // 粒子系统无效，跳过
                return;
            }
            catch (System.Exception)
            {
                // 其他异常，跳过
                return;
            }
        }   
        
        private ProblemResource CreateParticleProblem(ParticleSystem ps, string problemType, Color problemColor, 
                                                     ResourceAnalyzer analyzer, string objectName, string category = "粒子")
        {
            return new ProblemResource
            {
                Resource = ps,
                ResourceName = ps.name,
                ProblemType = problemType,
                Category = category,
                ProblemColor = problemColor,
                ObjectName = objectName,
                UsagePaths = analyzer.ResourceUsage.ContainsKey(ps) ? analyzer.ResourceUsage[ps].ToList() : new List<string>()
            };
        }

        private void DrawProblemsByCategory(List<ProblemResource> problems)
        {
            // 按类别分组
            var groupedProblems = problems.GroupBy(p => p.Category);

            int ci = 0;
            foreach (var categoryGroup in groupedProblems.OrderBy(g => g.Key))
            {
                string category = categoryGroup.Key;
                var categoryProblems = categoryGroup.ToList();

                if (!categoryFoldouts.ContainsKey(category))
                {
                    categoryFoldouts[category] = true;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    categoryFoldouts[category] = true;

                // 类别标题
                using (new UIHelper.ZebraScope(ci))
                {
                    EditorGUILayout.BeginVertical("box");
                    GUILayout.BeginHorizontal();

                    categoryFoldouts[category] = EditorGUILayout.Foldout(
                        categoryFoldouts[category],
                        $"{category} ({categoryProblems.Count}个问题)",
                        true);

                    // 冗余检测分类不显示具体问题概览
                    if (category != "冗余检测")
                    {
                        // 显示问题数量统计
                        var problemTypeGroups = categoryProblems.GroupBy(p => p.ProblemType);
                        GUILayout.FlexibleSpace();
                        foreach (var typeGroup in problemTypeGroups)
                        {
                            GUILayout.Label($"{typeGroup.Key}: {typeGroup.Count()}个", EditorStyles.miniLabel);
                            GUILayout.Space(10);
                        }
                    }

                    GUILayout.EndHorizontal();

                    if (categoryFoldouts[category])
                    {
                        EditorGUI.indentLevel++;
                        
                        // 如果是粒子模块，按模块类型分组显示
                        if (category == "粒子模块")
                        {
                            DrawParticleModuleProblems(categoryProblems);
                        }
                        else
                        {
                            DrawProblemList(categoryProblems);
                        }
                        
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.Space(5);
                ci++;
            }
        }

        private void DrawParticleModuleProblems(List<ProblemResource> particleModuleProblems)
        {
            // 按模块类型分组
            var modulesByType = particleModuleProblems.GroupBy(p => p.ProblemType);

            int mi = 0;
            foreach (var moduleGroup in modulesByType)
            {
                string moduleType = moduleGroup.Key;
                var typeProblems = moduleGroup.ToList();

                if (!problemTypeFoldouts.ContainsKey(moduleType))
                {
                    problemTypeFoldouts[moduleType] = false;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    problemTypeFoldouts[moduleType] = true;

                using (new UIHelper.ZebraScope(mi))
                {
                    EditorGUILayout.BeginVertical("helpbox");

                    // 粒子模块类型标题
                    GUILayout.BeginHorizontal();
                    problemTypeFoldouts[moduleType] = EditorGUILayout.Foldout(
                        problemTypeFoldouts[moduleType],
                        $"● {moduleType} ({typeProblems.Count}个)",
                        true);
                    GUILayout.EndHorizontal();

                    if (problemTypeFoldouts[moduleType])
                    {
                        EditorGUI.indentLevel++;
                        // 显示每个问题资源
                        for (int pi = 0; pi < typeProblems.Count; pi++)
                        {
                            using (new UIHelper.ZebraScope(pi))
                            {
                                DrawProblemItem(typeProblems[pi]);
                            }
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.Space(3);
                mi++;
            }
        }

        private void DrawProblemList(List<ProblemResource> problems)
        {
            // 按问题类型分组
            var groupedByType = problems.GroupBy(p => p.ProblemType);

            int ti = 0;
            foreach (var typeGroup in groupedByType)
            {
                string problemType = typeGroup.Key;
                var typeProblems = typeGroup.ToList();

                if (!problemTypeFoldouts.ContainsKey(problemType))
                {
                    problemTypeFoldouts[problemType] = false;
                }

                // 搜索时自动展开
                if (!string.IsNullOrEmpty(SearchFilter))
                    problemTypeFoldouts[problemType] = true;

                using (new UIHelper.ZebraScope(ti))
                {
                    EditorGUILayout.BeginVertical("helpbox");

                    // 问题类型标题（可折叠）
                    GUILayout.BeginHorizontal();
                    problemTypeFoldouts[problemType] = EditorGUILayout.Foldout(
                        problemTypeFoldouts[problemType],
                        $"● {problemType} ({typeProblems.Count}个)",
                        true);
                    GUILayout.EndHorizontal();

                    if (problemTypeFoldouts[problemType])
                    {
                        EditorGUI.indentLevel++;
                        // 显示每个问题资源
                        for (int pi = 0; pi < typeProblems.Count; pi++)
                        {
                            using (new UIHelper.ZebraScope(pi))
                            {
                                DrawProblemItem(typeProblems[pi]);
                            }
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.Space(3);
                ti++;
            }
        }

        private void DrawProblemItem(ProblemResource problem)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();

            // 资源名称（带颜色高亮）
            Color originalColor = GUI.color;
            GUI.color = problem.ProblemColor;
            GUILayout.Label("■", GUILayout.Width(15));
            GUI.color = originalColor;

            if (!foldouts.ContainsKey(problem.Resource))
            {
                foldouts[problem.Resource] = false;
            }

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                foldouts[problem.Resource] = true;

            foldouts[problem.Resource] = EditorGUILayout.Foldout(
                foldouts[problem.Resource],
                problem.ResourceName,
                true);

            GUILayout.FlexibleSpace();

            // 所属对象
            GUILayout.Label($"[{problem.ObjectName}]", EditorStyles.miniLabel, GUILayout.Width(100));

            // 资源对象字段（与材质模块同宽200）
            EditorGUILayout.ObjectField("", problem.Resource, problem.Resource.GetType(), false, GUILayout.Width(200));

            EditorGUILayout.EndHorizontal();

            // 展开显示详细信息
            if (foldouts[problem.Resource])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                EditorGUILayout.LabelField("问题类型:", problem.ProblemType);
                EditorGUILayout.LabelField("所属对象:", problem.ObjectName);
                EditorGUILayout.LabelField("使用路径:", EditorStyles.miniBoldLabel);

                foreach (var path in problem.UsagePaths)
                {
                    UIHelper.DrawUsagePath(path, foldouts, problem.Resource);
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // 辅助方法：获取问题类型
        private string GetNameProblemType(string name, ConditionManager conditionManager)
        {
            if (conditionManager.Settings.checkNameSpace && name.Contains(" "))
                return "名称含空格";
            
            if (conditionManager.Settings.checkNameCustom && !string.IsNullOrEmpty(conditionManager.Settings.nameCustomString) && 
                name.Contains(conditionManager.Settings.nameCustomString))
                return $"名称含有'{conditionManager.Settings.nameCustomString}'";
            
            if (conditionManager.Settings.checkNameUppercase && conditionManager.ContainsUppercase(name))
                return "名称首字母大写";
            
            return "命名问题";
        }

        private string GetMeshProblemType(Mesh mesh, ConditionManager conditionManager)
        {
            if (conditionManager.Settings.checkVertexCount)
            {
                if (mesh.vertexCount > conditionManager.Settings.vertexCountError)
                    return $"顶点数超限(>{conditionManager.Settings.vertexCountError})";
                if (mesh.vertexCount > conditionManager.Settings.vertexCountWarning)
                    return $"顶点数警告(>{conditionManager.Settings.vertexCountWarning})";
            }
            if (conditionManager.Settings.checkTriangleCount)
            {
                int triangleCount = mesh.triangles.Length / 3;
                if (triangleCount > conditionManager.Settings.triangleCountError)
                    return $"三角面超限(>{conditionManager.Settings.triangleCountError})";
                if (triangleCount > conditionManager.Settings.triangleCountWarning)
                    return $"三角面警告(>{conditionManager.Settings.triangleCountWarning})";
            }
            return "网格问题";
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
            return "贴图问题";
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
            return "动画问题";
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

        public void Clear()
        {
            foldouts.Clear();
            categoryFoldouts.Clear();
            problemTypeFoldouts.Clear();
        }
    }
}