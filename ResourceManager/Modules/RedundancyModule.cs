using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    /// <summary>
    /// 冗余检测模块 — 独立标签页，显示冗余资源，按检测内容分组
    /// 仿 ExportModule 布局：类型分组 → 文件夹分组 → 资源项
    /// 
    /// 规则来源：粒子材质球清理规则表（21条，5组）
    /// 组A: R2/R3/R7 — 渲染模式为None系列
    /// 组B: R6/R10   — 渲染模式与资源类型不匹配
    /// 组C: R4/R8    — 默认资源冗余（独立，不限模式）
    /// 组D: R1/R5/R9 — 专项冗余
    /// 组E: R11-R21  — 模块状态冗余
    /// </summary>
    public class RedundancyModule : IMultiObjectModule
    {
        public string SearchFilter = "";

        // 冗余条目
        private class RedundancyItem
        {
            public Object Resource;          // 实际冗余资源（贴图/材质/Mesh等）
            public Object OwnerObject;       // 所属对象（粒子系统/GameObject）
            public string OwnerName;
            public string RuleId;            // 规则编号（R1-R21）
            public string RuleGroup;         // 规则组（A/B/C/D/E）
            public string ProblemDesc;       // 问题描述（动作指令）
            public string Category;          // 检测类别（用于分组）
            public string ResourcePath;      // 资源路径
        }

        private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> typeFoldouts = new Dictionary<string, bool>();
        private Dictionary<Object, bool> itemFoldouts = new Dictionary<Object, bool>();

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("冗余检测", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清理冗余", GUILayout.Width(80)))
            {
                CleanRedundancy(session);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先进行分析", MessageType.Info);
                return;
            }

            // 收集所有冗余项
            List<RedundancyItem> allItems = CollectAllRedundancy(session);

            // 搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                string filter = SearchFilter.ToLower();
                allItems = allItems.Where(item =>
                    (item.OwnerName != null && item.OwnerName.ToLower().Contains(filter)) ||
                    (item.ProblemDesc != null && item.ProblemDesc.ToLower().Contains(filter)) ||
                    (item.Resource != null && item.Resource.name.ToLower().Contains(filter)) ||
                    (item.Category != null && item.Category.ToLower().Contains(filter)) ||
                    (item.RuleId != null && item.RuleId.ToLower().Contains(filter))
                ).ToList();
            }

            if (allItems.Count == 0)
            {
                if (!string.IsNullOrEmpty(SearchFilter))
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的冗余资源", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("✓ 未检测到冗余资源！", MessageType.Info);
                return;
            }

            // 搜索时显示结果数量
            if (!string.IsNullOrEmpty(SearchFilter))
            {
                EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {allItems.Count} 个冗余资源", MessageType.Info);
            }

            // 按检测类别分组绘制
            DrawByCategory(allItems);
        }

        #region 收集冗余

        /// <summary>
        /// 收集所有冗余资源（覆盖21条规则）
        /// </summary>
        private List<RedundancyItem> CollectAllRedundancy(AnalysisSession session)
        {
            var items = new List<RedundancyItem>();
            var settings = ConditionManager.Instance.Settings;
            if (!settings.checkRedundancy) return items;

            var scannedPS = new HashSet<ParticleSystem>();

            foreach (var kvp in session.Analyzers)
            {
                var targetObject = kvp.Key;
                var analyzer = kvp.Value;
                string objectName = targetObject.name;

                // 遍历所有资源
                foreach (var resource in analyzer.ResourceUsage.Keys)
                {
                    if (resource == null) continue;

                    if (resource is ParticleSystem ps && scannedPS.Add(ps))
                    {
                        CollectParticleRedundancy(ps, objectName, items);
                        CollectModuleStateRedundancy(ps, objectName, items);
                    }
                }

                // 材质 Shader toggle 冗余
                CollectMaterialRedundancy(analyzer, objectName, items);

                // 深度遍历子节点粒子
                if (targetObject != null)
                {
                    CollectDeepParticleRedundancy(targetObject, objectName, items, scannedPS);
                }
            }

            return items;
        }

        /// <summary>
        /// 检测粒子系统冗余 — 组A/B/C/D（R1-R10）
        /// 
        /// 组A: None模式系列
        ///   R2: renderer.enabled + renderMode==None → 关闭渲染模式
        ///   R3: R2 + sharedMaterial!=null → 清理材质球，关闭渲染模式
        ///   R7: R2 + mesh!=null → 清理模型，关闭渲染模式
        /// 组B: 模式不匹配
        ///   R6: renderMode!=Mesh + mesh!=null → 清理模型
        ///   R10: renderMode==Mesh + mesh==null + sharedMaterial!=null → 清理材质球，改None，关渲染
        /// 组C: 默认资源冗余（独立规则，不限模式）
        ///   R4: Default-Particle 或 Particles/Standard Unlit(未修改) → 清理默认材质球
        ///   R8: Standard shader(无纹理+白色) → 清理默认材质球; 内置Cube/Sphere → 清理默认模型
        /// 组D: 专项冗余
        ///   R1: renderer.enabled==false + mainTexture!=null → 清理贴图
        ///   R5: trails.enabled==false + trailMaterial!=null → 清理条带材质球
        ///   R9: shape非Mesh类型 + mesh/meshRenderer/skinnedMeshRenderer!=null → 清理发射器模型
        /// </summary>
        private void CollectParticleRedundancy(ParticleSystem ps,
            string objectName, List<RedundancyItem> items)
        {
            if (ps == null || ps.Equals(null)) return;

            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                var shape = ps.shape;
                // 控制台输出
                // Debug.Log($"粒子 [{ps.name}] 渲染模式：{renderer.trailMaterial}");
                
                if (renderer != null)
                {
                    int mode = (int)renderer.renderMode;
                    bool isNoneMode = mode < 0 || mode >= 5;
                    bool isMeshMode = renderer.renderMode == ParticleSystemRenderMode.Mesh;

                    Material mat = renderer.sharedMaterial;
                    Mesh mesh = renderer.mesh;
                    bool isDefaultParticleMat = IsDefaultParticleMaterial(mat);
                    bool isStandardDefaultMat = IsStandardDefaultMaterial(mat);
                    bool isBuiltInMesh = IsBuiltInMesh(mesh);

                    // ===== 组A: None模式系列 (R2/R3/R7) =====
                    if (isNoneMode && renderer.enabled && renderer.trailMaterial == null)
                    {
                        // R3: None+enabled+材质非空 → 清理材质球，关闭渲染模式
                        if (mat != null)
                        {
                            string matLabel = isDefaultParticleMat ? $"默认材质球：{mat.name}" : $"材质球：{mat.name}";
                            items.Add(new RedundancyItem
                            {
                                Resource = mat,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R3",
                                RuleGroup = "A",
                                ProblemDesc = $"粒子 [{ps.name}] 清理{matLabel}，关闭渲染模式",
                                Category = "材质球",
                                ResourcePath = AssetDatabase.GetAssetPath(mat)
                            });
                        }

                        // R7: None+enabled+mesh非空 → 清理模型，关闭渲染模式
                        if (mesh != null)
                        {
                            string meshLabel = isBuiltInMesh ? $"默认模型：{mesh.name}" : $"模型：{mesh.name}";
                            items.Add(new RedundancyItem
                            {
                                Resource = mesh,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R7",
                                RuleGroup = "A",
                                ProblemDesc = $"粒子 [{ps.name}] 清理{meshLabel}，关闭渲染模式",
                                Category = "模型",
                                ResourcePath = AssetDatabase.GetAssetPath(mesh)
                            });
                        }

                        // R2: None+enabled 且无材质无模型 → 仅关闭渲染模式
                        if (mat == null && mesh == null)
                        {
                            items.Add(new RedundancyItem
                            {
                                Resource = renderer,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R2",
                                RuleGroup = "A",
                                ProblemDesc = $"粒子 [{ps.name}] 渲染模式为None但渲染模块启用，关闭渲染模式",
                                Category = "渲染模块",
                                ResourcePath = ""
                            });
                        }
                    }

                    // ===== 组B: 模式不匹配 (R6/R10) =====

                    // R10: Mesh+mesh空+材质非空 → 清理材质球，改None，关渲染
                    if (isMeshMode && mesh == null && mat != null)
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = mat,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R10",
                            RuleGroup = "B",
                            ProblemDesc = $"粒子 [{ps.name}] 清理材质球：{mat.name}，渲染模式改None，关闭渲染",
                            Category = "材质球",
                            ResourcePath = AssetDatabase.GetAssetPath(mat)
                        });
                    }

                    // R6: renderMode!=Mesh(非None) + mesh非空 → 清理模型
                    if (!isNoneMode && !isMeshMode && mesh != null)
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = mesh,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R6",
                            RuleGroup = "B",
                            ProblemDesc = $"粒子 [{ps.name}] 清理模型：{mesh.name}（渲染模式非Mesh）",
                            Category = "模型",
                            ResourcePath = AssetDatabase.GetAssetPath(mesh)
                        });
                    }

                    // ===== 组C: 默认资源冗余 (R4/R8) — 独立规则，不限模式 =====
                    // 注意：避免与组A重复（None+enabled已在R3/R7中处理）

                    // R4: 默认粒子材质（非None模式才独立报告，None模式由R3覆盖）
                    if (mat != null && isDefaultParticleMat && !(isNoneMode && renderer.enabled))
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = mat,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R4",
                            RuleGroup = "C",
                            ProblemDesc = $"粒子 [{ps.name}] 清理默认材质球：{mat.name}",
                            Category = "默认资源",
                            ResourcePath = AssetDatabase.GetAssetPath(mat)
                        });
                    }

                    // R8-材质: Standard shader(无纹理+白色) → 清理默认材质球
                    if (mat != null && isStandardDefaultMat && !(isNoneMode && renderer.enabled))
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = mat,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R8",
                            RuleGroup = "C",
                            ProblemDesc = $"粒子 [{ps.name}] 清理默认材质球：{mat.name}（Standard shader，无纹理）",
                            Category = "默认资源",
                            ResourcePath = AssetDatabase.GetAssetPath(mat)
                        });
                    }

                    // R8-模型: 内置Cube/Sphere等 → 清理默认模型（避免与组A/组B重复）
                    if (mesh != null && isBuiltInMesh
                        && !(isNoneMode && renderer.enabled)   // 组A已覆盖
                        && !(isMeshMode && mesh == null)       // 不可能（mesh!=null）
                        && !(!isNoneMode && !isMeshMode))      // 组B(R6)已覆盖非None非Mesh
                    {
                        // 仅在 Mesh模式下使用内置mesh → 报告R8
                        if (isMeshMode)
                        {
                            items.Add(new RedundancyItem
                            {
                                Resource = mesh,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R8",
                                RuleGroup = "C",
                                ProblemDesc = $"粒子 [{ps.name}] 清理默认模型：{mesh.name}（Mesh模式使用内置模型）",
                                Category = "默认资源",
                                ResourcePath = AssetDatabase.GetAssetPath(mesh)
                            });
                        }
                    }

                    // ===== 组D: 专项冗余 (R1/R5) =====

                    // R1: renderer.enabled==false + mainTexture!=null → 清理贴图
                    if (!renderer.enabled && mat != null && mat.mainTexture != null)
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = mat.mainTexture,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R1",
                            RuleGroup = "D",
                            ProblemDesc = $"粒子 [{ps.name}] 清理贴图：{mat.mainTexture.name}（渲染模块关闭但贴图已设置）",
                            Category = "贴图",
                            ResourcePath = AssetDatabase.GetAssetPath(mat.mainTexture)
                        });
                    }

                    // R5: trails.enabled==false + trailMaterial!=null → 清理条带材质球
#if UNITY_2017_1_OR_NEWER
                    var trails = ps.trails;
                    if (!trails.enabled && renderer.trailMaterial != null)
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = renderer.trailMaterial,
                            OwnerObject = ps.gameObject,
                            OwnerName = objectName,
                            RuleId = "R5",
                            RuleGroup = "D",
                            ProblemDesc = $"粒子 [{ps.name}] 清理条带材质球：{renderer.trailMaterial.name}",
                            Category = "材质球",
                            ResourcePath = AssetDatabase.GetAssetPath(renderer.trailMaterial)
                        });
                    }
#endif
                }

                // ===== 组D: R9 发射器模型 =====
                if (shape.enabled)
                {
                    var shapeType = shape.shapeType;
                    bool isMeshShape = shapeType == ParticleSystemShapeType.Mesh
                        || shapeType == ParticleSystemShapeType.MeshRenderer
                        || shapeType == ParticleSystemShapeType.SkinnedMeshRenderer;

                    // R9: 非Mesh类型 + mesh/meshRenderer/skinnedMeshRenderer != null
                    if (!isMeshShape)
                    {
                        bool hasShapeMeshRef = shape.mesh != null;
#if UNITY_2019_1_OR_NEWER
                        // meshRenderer/skinnedMeshRenderer 在2019+可用
                        if (shapeType == ParticleSystemShapeType.MeshRenderer && shape.mesh != null)
                            hasShapeMeshRef = true;
                        if (shapeType == ParticleSystemShapeType.SkinnedMeshRenderer && shape.mesh != null)
                            hasShapeMeshRef = true;
#endif
                        if (hasShapeMeshRef)
                        {
                            items.Add(new RedundancyItem
                            {
                                Resource = shape.mesh,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R9",
                                RuleGroup = "D",
                                ProblemDesc = $"粒子 [{ps.name}] 清理发射器模型：{shape.mesh.name}（Shape类型为{shapeType}）",
                                Category = "模型",
                                ResourcePath = AssetDatabase.GetAssetPath(shape.mesh)
                            });
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 跳过异常粒子
            }
        }

        /// <summary>
        /// 检测模块状态冗余 — 组E（R11-R21）
        /// 
        /// R11: PS.enabled==false + renderer.enabled==true → 状态冲突
        /// R12: emission.enabled + 速率全0 + burstCount==0 → 关闭发射模块
        /// R13: emission.enabled==false + burstCount>0 → 清理突发数据
        /// R14: velocityOverLifetime.enabled + x/y/z全0 → 关闭限速模块
        /// R15: velocityOverLifetime.enabled==false + space!=Self → 重置参数
        /// R16: colorOverLifetime.enabled + gradient仅1帧且==startColor → 关闭颜色模块
        /// R17: sizeOverLifetime.enabled + curve恒定==startSize → 关闭大小模块
        /// R18: rotationOverLifetime.enabled + x/y/z全恒定0 → 关闭旋转模块
        /// R19: collision.enabled + planeCount==0 + sendCollisionMessages==false → 关闭碰撞模块
        /// R20: trigger.enabled + colliderCount==0 → 关闭触发模块
        /// R21: subEmitters.enabled + 任意system==null → 清理无效子发射器
        /// </summary>
        private void CollectModuleStateRedundancy(ParticleSystem ps,
            string objectName, List<RedundancyItem> items)
        {
            if (ps == null || ps.Equals(null)) return;

            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                var main = ps.main;
                var emission = ps.emission;
                var velocityOverLifetime = ps.velocityOverLifetime;
                var colorOverLifetime = ps.colorOverLifetime;
                var sizeOverLifetime = ps.sizeOverLifetime;
                var rotationOverLifetime = ps.rotationOverLifetime;
                var collision = ps.collision;
                var trigger = ps.trigger;
                var subEmitters = ps.subEmitters;

                // R11: GameObject inactive + renderer.enabled==true → 状态冲突
                // 注：Unity 2019.4 中 ParticleSystem 无 .enabled 属性，用 gameObject.activeSelf 代替
                if (!ps.gameObject.activeSelf && renderer != null && renderer.enabled)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R11",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 状态冲突：粒子系统关闭但渲染模块启用",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R12: emission.enabled + 速率全0 + burstCount==0 → 关闭发射模块
                if (emission.enabled && IsMinMaxCurveZero(emission.rateOverTime)
                    && IsMinMaxCurveZero(emission.rateOverDistance) && emission.burstCount == 0)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R12",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 发射模块启用但速率全为0，关闭发射模块或设置有效速率",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R13: emission.enabled==false + burstCount>0 → 清理突发数据
                if (!emission.enabled && emission.burstCount > 0)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R13",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 发射模块关闭但突发数据({emission.burstCount}个)未清理",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R14: velocityOverLifetime.enabled + x/y/z全0 → 关闭限速模块
                if (velocityOverLifetime.enabled
                    && IsMinMaxCurveZero(velocityOverLifetime.x)
                    && IsMinMaxCurveZero(velocityOverLifetime.y)
                    && IsMinMaxCurveZero(velocityOverLifetime.z))
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R14",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 限速模块启用但x/y/z全为0，关闭限速模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R15: velocityOverLifetime.enabled==false + space!=Local → 重置参数
                if (!velocityOverLifetime.enabled && velocityOverLifetime.space != ParticleSystemSimulationSpace.Local)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R15",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 限速模块关闭但space={velocityOverLifetime.space}，重置参数",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R16: colorOverLifetime.enabled + gradient仅1帧且==startColor → 关闭颜色模块
                if (colorOverLifetime.enabled && IsColorModuleRedundant(colorOverLifetime, main.startColor))
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R16",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 颜色模块启用但颜色与初始颜色一致，关闭颜色模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R17: sizeOverLifetime.enabled + curve恒定==startSize → 关闭大小模块
                if (sizeOverLifetime.enabled && IsSizeModuleRedundant(sizeOverLifetime, main.startSize))
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R17",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 大小模块启用但大小与初始大小一致，关闭大小模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R18: rotationOverLifetime.enabled + x/y/z全恒定0 → 关闭旋转模块
                if (rotationOverLifetime.enabled
                    && IsMinMaxCurveZero(rotationOverLifetime.x)
                    && IsMinMaxCurveZero(rotationOverLifetime.y)
                    && IsMinMaxCurveZero(rotationOverLifetime.z))
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R18",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 旋转模块启用但x/y/z全为0，关闭旋转模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R19: collision.enabled + planeCount==0 + sendCollisionMessages==false → 关闭碰撞模块
                if (collision.enabled && collision.planeCount == 0 && !collision.sendCollisionMessages)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R19",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 碰撞模块启用但无碰撞平面，关闭碰撞模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }

                // R20: trigger.enabled + colliderCount==0 → 关闭触发模块
#if UNITY_2017_1_OR_NEWER
                if (trigger.enabled && trigger.colliderCount == 0)
                {
                    items.Add(new RedundancyItem
                    {
                        Resource = ps,
                        OwnerObject = ps.gameObject,
                        OwnerName = objectName,
                        RuleId = "R20",
                        RuleGroup = "E",
                        ProblemDesc = $"粒子 [{ps.name}] 触发模块启用但无碰撞体，关闭触发模块",
                        Category = "模块状态",
                        ResourcePath = ""
                    });
                }
#endif

                // R21: subEmitters.enabled + 任意system==null → 清理无效子发射器
                if (subEmitters.enabled)
                {
                    int count = subEmitters.subEmittersCount;
                    for (int i = 0; i < count; i++)
                    {
                        var subSystem = subEmitters.GetSubEmitterSystem(i);
                        if (subSystem == null || subSystem.Equals(null))
                        {
                            items.Add(new RedundancyItem
                            {
                                Resource = ps,
                                OwnerObject = ps.gameObject,
                                OwnerName = objectName,
                                RuleId = "R21",
                                RuleGroup = "E",
                                ProblemDesc = $"粒子 [{ps.name}] 子发射器[{i}]引用为空，清理无效引用",
                                Category = "模块状态",
                                ResourcePath = ""
                            });
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 跳过异常粒子
            }
        }

        /// <summary>
        /// 材质 Shader toggle 冗余检测（辅助规则，独立于21条主规则）
        /// </summary>
        private void CollectMaterialRedundancy(ResourceAnalyzer analyzer,
            string objectName, List<RedundancyItem> items)
        {
            var conditionManager = ConditionManager.Instance;
            var checkedMaterials = new HashSet<Material>();

            foreach (var resource in analyzer.ResourceUsage.Keys)
            {
                if (resource is Material mat && !checkedMaterials.Contains(mat))
                {
                    checkedMaterials.Add(mat);
                    var redundancies = conditionManager.CheckMaterialRedundancy(mat);
                    foreach (var (desc, tex, propName) in redundancies)
                    {
                        items.Add(new RedundancyItem
                        {
                            Resource = tex,
                            OwnerObject = mat,
                            OwnerName = objectName,
                            RuleId = "ShaderToggle",
                            RuleGroup = "辅助",
                            ProblemDesc = $"材质 [{mat.name}] {desc}：{propName}={tex.name}",
                            Category = "贴图",
                            ResourcePath = AssetDatabase.GetAssetPath(tex)
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 深度遍历场景对象，检测未在 ResourceUsage 中记录的子节点粒子冗余
        /// </summary>
        private void CollectDeepParticleRedundancy(GameObject root, string objectName,
            List<RedundancyItem> items, HashSet<ParticleSystem> scannedPS)
        {
            var allPS = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in allPS)
            {
                if (ps == null || ps.Equals(null)) continue;
                if (!scannedPS.Add(ps)) continue;
                CollectParticleRedundancy(ps, objectName, items);
                CollectModuleStateRedundancy(ps, objectName, items);
            }
        }

        #endregion

        #region MinMaxCurve/Gradient 辅助检测

        /// <summary>
        /// 判断 MinMaxCurve 是否等效于0
        /// </summary>
        private bool IsMinMaxCurveZero(ParticleSystem.MinMaxCurve curve)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant)
                return Mathf.Approximately(curve.constant, 0f);
            if (curve.mode == ParticleSystemCurveMode.TwoConstants)
                return Mathf.Approximately(curve.constantMin, 0f) && Mathf.Approximately(curve.constantMax, 0f);
            if (curve.mode == ParticleSystemCurveMode.Curve)
            {
                if (curve.curve == null || curve.curve.length == 0) return true;
                // 曲线所有关键帧值为0
                foreach (var key in curve.curve.keys)
                    if (Mathf.Abs(key.value) > 0.01f) return false;
                return true;
            }
            if (curve.mode == ParticleSystemCurveMode.TwoCurves)
            {
                if (curve.curveMin == null || curve.curveMax == null) return true;
                foreach (var key in curve.curveMin.keys)
                    if (Mathf.Abs(key.value) > 0.01f) return false;
                foreach (var key in curve.curveMax.keys)
                    if (Mathf.Abs(key.value) > 0.01f) return false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断颜色模块是否冗余（R16: gradient仅1帧且颜色==初始颜色）
        /// </summary>
        private bool IsColorModuleRedundant(ParticleSystem.ColorOverLifetimeModule colModule,
            ParticleSystem.MinMaxGradient startColor)
        {
            var color = colModule.color;
            Color32 startColorValue = GetStartColorValue(startColor);

            if (color.mode == ParticleSystemGradientMode.Color)
                return ColorsApproxEqual(color.color, startColorValue);
            if (color.mode == ParticleSystemGradientMode.TwoColors)
                return ColorsApproxEqual(color.colorMin, startColorValue) && ColorsApproxEqual(color.colorMax, startColorValue);
            if (color.mode == ParticleSystemGradientMode.Gradient)
            {
                if (color.gradient == null) return true;
                // 仅1个关键帧且颜色==初始颜色
                if (color.gradient.colorKeys.Length <= 1 && color.gradient.alphaKeys.Length <= 2)
                {
                    if (color.gradient.colorKeys.Length == 1)
                        return ColorsApproxEqual(color.gradient.colorKeys[0].color, startColorValue);
                    return true; // 无颜色关键帧视为冗余
                }
                return false;
            }
            if (color.mode == ParticleSystemGradientMode.TwoGradients)
            {
                // 两个gradient都仅1帧且颜色一致
                bool minOk = color.gradientMin == null || (color.gradientMin.colorKeys.Length <= 1);
                bool maxOk = color.gradientMax == null || (color.gradientMax.colorKeys.Length <= 1);
                return minOk && maxOk;
            }
            return false;
        }

        private Color32 GetStartColorValue(ParticleSystem.MinMaxGradient startColor)
        {
            if (startColor.mode == ParticleSystemGradientMode.Color)
                return startColor.color;
            if (startColor.mode == ParticleSystemGradientMode.TwoColors)
                return startColor.colorMax; // 取最大值作为参考
            if (startColor.mode == ParticleSystemGradientMode.Gradient && startColor.gradient != null)
                return startColor.gradient.colorKeys.Length > 0 ? startColor.gradient.colorKeys[0].color : Color.white;
            return Color.white;
        }

        private bool ColorsApproxEqual(Color32 a, Color32 b)
        {
            return Mathf.Abs(a.r - b.r) < 3 && Mathf.Abs(a.g - b.g) < 3
                && Mathf.Abs(a.b - b.b) < 3 && Mathf.Abs(a.a - b.a) < 3;
        }

        private bool ColorsApproxEqual(Color a, Color32 b)
        {
            return Mathf.Abs(a.r * 255 - b.r) < 3 && Mathf.Abs(a.g * 255 - b.g) < 3
                && Mathf.Abs(a.b * 255 - b.b) < 3 && Mathf.Abs(a.a * 255 - b.a) < 3;
        }

        /// <summary>
        /// 判断大小模块是否冗余（R17: curve恒定==startSize）
        /// </summary>
        private bool IsSizeModuleRedundant(ParticleSystem.SizeOverLifetimeModule sizeModule,
            ParticleSystem.MinMaxCurve startSize)
        {
            var size = sizeModule.size;
            float startSizeValue = GetStartSizeValue(startSize);

            if (size.mode == ParticleSystemCurveMode.Constant)
                return Mathf.Approximately(size.constant, startSizeValue);
            if (size.mode == ParticleSystemCurveMode.TwoConstants)
                return Mathf.Approximately(size.constantMin, startSizeValue)
                    && Mathf.Approximately(size.constantMax, startSizeValue);
            if (size.mode == ParticleSystemCurveMode.Curve)
            {
                if (size.curve == null) return true;
                // 曲线恒定值 == startSize（所有关键帧值相同）
                if (size.curve.length == 1 && Mathf.Approximately(size.curve.keys[0].value, startSizeValue))
                    return true;
                return false;
            }
            return false;
        }

        private float GetStartSizeValue(ParticleSystem.MinMaxCurve startSize)
        {
            if (startSize.mode == ParticleSystemCurveMode.Constant)
                return startSize.constant;
            if (startSize.mode == ParticleSystemCurveMode.TwoConstants)
                return startSize.constantMax;
            if (startSize.mode == ParticleSystemCurveMode.Curve && startSize.curve != null && startSize.curve.length > 0)
                return startSize.curve.keys[0].value;
            return 1f;
        }

        #endregion

        #region 默认资源检测辅助方法

        /// <summary>
        /// R4: 判断材质是否为粒子默认材质
        /// 条件1: Unity 内置资源（路径检测），与粒子相关的材质
        /// 条件2: 名称包含 "Default-Particle"
        /// 条件3: Shader 为 "Particles/Standard Unlit" 且未修改任何属性（无贴图、颜色为默认白色）
        /// </summary>
        private bool IsDefaultParticleMaterial(Material mat)
        {
            if (mat == null) return false;

            // 条件1: 通过资源路径检测Unity内置材质（最可靠，不受名称/Shader名变化影响）
            string path = AssetDatabase.GetAssetPath(mat);
            bool isBuiltinAsset = string.IsNullOrEmpty(path)
                || path.Contains("unity_builtin_extra")
                || path.Contains("unity default resources");
            if (isBuiltinAsset)
            {
                // Unity内置资源，检查是否为粒子相关（名称或Shader含Particle关键词）
                if (mat.name.Contains("Default") || mat.name.Contains("Particle")
                    || (mat.shader != null && (mat.shader.name.Contains("Particle")
                        || mat.shader.name.Contains("Particles"))))
                {
                    return true;
                }
            }

            // 条件2: 名称包含 "Default-Particle"（适配用户自定义的默认粒子材质实例）
            if (mat.name.Contains("Default-Particle")) return true;

            // 条件3: Shader 为 Particles/Standard Unlit 且未修改属性
            if (mat.shader != null && mat.shader.name == "Particles/Standard Unlit")
            {
                // 检查是否有贴图被设置（非空即为修改）
                bool hasTexture = false;
                int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                for (int i = 0; i < propCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                        if (mat.GetTexture(propName) != null)
                        {
                            hasTexture = true;
                            break;
                        }
                    }
                }
                if (hasTexture) return false;

                // 检查颜色是否为默认白色 (1,1,1,1)
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    if (Mathf.Abs(c.r - 1f) > 0.01f || Mathf.Abs(c.g - 1f) > 0.01f
                        || Mathf.Abs(c.b - 1f) > 0.01f || Mathf.Abs(c.a - 1f) > 0.01f)
                        return false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// R8-材质部分: 判断材质是否为Standard shader默认材质
        /// 条件: shader.name == "Standard" 且 mainTexture == null 且 color == Color.white
        /// 补充: 通过资源路径检测Unity内置Standard材质（不受名称变化影响）
        /// </summary>
        private bool IsStandardDefaultMaterial(Material mat)
        {
            if (mat == null) return false;

            // 补充检测：通过资源路径识别Unity内置默认材质
            string path = AssetDatabase.GetAssetPath(mat);
            bool isBuiltinAsset = string.IsNullOrEmpty(path)
                || path.Contains("unity_builtin_extra")
                || path.Contains("unity default resources");

            if (isBuiltinAsset && mat.shader != null && mat.shader.name == "Standard")
            {
                // Unity内置Standard材质，检查是否为未修改的默认状态
                if (mat.mainTexture == null)
                {
                    if (!mat.HasProperty("_Color")) return true;
                    Color c = mat.GetColor("_Color");
                    if (Mathf.Abs(c.r - 1f) <= 0.01f && Mathf.Abs(c.g - 1f) <= 0.01f
                        && Mathf.Abs(c.b - 1f) <= 0.01f && Mathf.Abs(c.a - 1f) <= 0.01f)
                        return true;
                }
                return false;
            }

            if (mat.shader == null || mat.shader.name != "Standard") return false;
            if (mat.mainTexture != null) return false;

            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                if (Mathf.Abs(c.r - 1f) > 0.01f || Mathf.Abs(c.g - 1f) > 0.01f
                    || Mathf.Abs(c.b - 1f) > 0.01f || Mathf.Abs(c.a - 1f) > 0.01f)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// R8-模型部分: 判断 Mesh 是否为 Unity 内置默认模型
        /// 条件: mesh 为内置资源（如 Cube、Sphere、Cylinder、Plane、Capsule）
        /// 且路径为空或含 unity_builtin_extra（排除用户自定义同名 Mesh）
        /// </summary>
        private bool IsBuiltInMesh(Mesh mesh)
        {
            if (mesh == null) return false;

            string path = AssetDatabase.GetAssetPath(mesh);
            bool isBuiltInResource = string.IsNullOrEmpty(path) || path.Contains("unity_builtin_extra");

            if (!isBuiltInResource) return false;

            // 检查是否为常见内置模型名
            string name = mesh.name;
            return name == "Cube" || name == "Sphere" || name == "Cylinder"
                || name == "Plane" || name == "Capsule";
        }

        #endregion

        #region UI绘制

        /// <summary>
        /// 按检测类别分组绘制
        /// </summary>
        private void DrawByCategory(List<RedundancyItem> items)
        {
            var grouped = items.GroupBy(i => i.Category).OrderBy(g => GetCategoryOrder(g.Key));

            foreach (var categoryGroup in grouped)
            {
                string category = categoryGroup.Key;
                var catItems = categoryGroup.ToList();

                if (!categoryFoldouts.ContainsKey(category))
                    categoryFoldouts[category] = false;

                if (!string.IsNullOrEmpty(SearchFilter))
                    categoryFoldouts[category] = true;

                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                categoryFoldouts[category] = EditorGUILayout.Foldout(
                    categoryFoldouts[category],
                    $"■ {category} ({catItems.Count}个冗余)",
                    true,
                    EditorStyles.foldoutHeader);
                EditorGUILayout.EndHorizontal();

                if (categoryFoldouts[category])
                {
                    EditorGUI.indentLevel++;

                    // 按规则组分组
                    var byGroup = catItems.GroupBy(i => i.RuleGroup ?? "?").OrderBy(g => g.Key);

                    int gi = 0;
                    foreach (var group in byGroup)
                    {
                        var groupItems = group.ToList();

                        using (new UIHelper.ZebraScope(gi))
                        {
                            EditorGUILayout.BeginVertical("helpbox");
                            EditorGUILayout.LabelField($"组{group.Key} ({groupItems.Count}个)", EditorStyles.miniBoldLabel);

                            EditorGUI.indentLevel++;
                            for (int ii = 0; ii < groupItems.Count; ii++)
                            {
                                using (new UIHelper.ZebraScope(ii))
                                {
                                    DrawRedundancyItem(groupItems[ii]);
                                }
                            }
                            EditorGUI.indentLevel--;
                            EditorGUILayout.EndVertical();
                        }
                        gi++;
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }

        private void DrawRedundancyItem(RedundancyItem item)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            if (!itemFoldouts.ContainsKey(item.OwnerObject ?? item.Resource))
                itemFoldouts[item.OwnerObject ?? item.Resource] = false;

            Color originalColor = GUI.color;
            GUI.color = ConditionManager.Instance.Settings.redundancyColor;
            GUILayout.Label("■", GUILayout.Width(15));
            GUI.color = originalColor;

            // 规则编号 + 问题描述
            string ruleTag = !string.IsNullOrEmpty(item.RuleId) ? $"[{item.RuleId}] " : "";
            itemFoldouts[item.OwnerObject ?? item.Resource] = EditorGUILayout.Foldout(
                itemFoldouts[item.OwnerObject ?? item.Resource],
                ruleTag + item.ProblemDesc,
                true);

            GUILayout.FlexibleSpace();

            if (item.Resource != null && GUILayout.Button("定位", GUILayout.Width(40)))
            {
                Selection.activeObject = item.Resource;
                EditorGUIUtility.PingObject(item.Resource);
            }

            if (item.OwnerObject != null && GUILayout.Button("源", GUILayout.Width(30)))
            {
                Selection.activeObject = item.OwnerObject;
                EditorGUIUtility.PingObject(item.OwnerObject);
            }

            EditorGUILayout.EndHorizontal();

            if (itemFoldouts[item.OwnerObject ?? item.Resource])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();

                EditorGUILayout.LabelField("所属对象:", item.OwnerName);
                EditorGUILayout.LabelField("检测类别:", item.Category);
                if (!string.IsNullOrEmpty(item.RuleId))
                    EditorGUILayout.LabelField("规则编号:", $"{item.RuleId} (组{item.RuleGroup})");

                if (item.Resource != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("冗余资源:", GUILayout.Width(60));
                    EditorGUILayout.ObjectField(item.Resource, item.Resource.GetType(), false);
                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(item.ResourcePath))
                    {
                        EditorGUILayout.LabelField("资源路径:", item.ResourcePath, EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private int GetCategoryOrder(string category)
        {
            if (category == "材质球") return 0;
            if (category == "模型") return 1;
            if (category == "贴图") return 2;
            if (category == "默认资源") return 3;
            if (category == "渲染模块") return 4;
            if (category == "模块状态") return 5;
            return 99;
        }

        private string GetResourceTypeName(System.Type type)
        {
            if (type == typeof(Material)) return "材质球";
            if (type == typeof(Mesh)) return "模型Mesh";
            if (type == typeof(Texture2D)) return "贴图";
            if (type == typeof(Texture)) return "贴图";
            if (type == typeof(ParticleSystem)) return "粒子系统";
            if (type == typeof(ParticleSystemRenderer)) return "渲染器";
            return type.Name;
        }

        private string GetCategoryColor(string category)
        {
            if (category == "材质球") return "#FF4488";
            if (category == "模型") return "#4488FF";
            if (category == "贴图") return "#8844FF";
            if (category == "默认资源") return "#FF8844";
            if (category == "渲染模块") return "#44FFFF";
            if (category == "模块状态") return "#88FF44";
            return "#FF44FF";
        }

        #endregion

        #region 清理冗余

        /// <summary>
        /// 清理冗余资源（覆盖21条规则）
        /// 组A: R2/R3/R7 — 关闭渲染/清理材质/清理模型
        /// 组B: R6/R10   — 清理模型/清理材质+改模式
        /// 组C: R4/R8    — 清理默认材质/清理默认模型
        /// 组D: R1/R5/R9 — 清理贴图/条带材质/发射器模型
        /// 组E: R12-R21  — 关闭各模块/清理无效引用
        /// </summary>
        private void CleanRedundancy(AnalysisSession session)
        {
            var scannedPS = new HashSet<ParticleSystem>();
            int r2Count = 0, r3Count = 0, r4Count = 0, r5Count = 0, r6Count = 0;
            int r7Count = 0, r8MatCount = 0, r8MeshCount = 0, r9Count = 0, r10Count = 0;
            int r11Count = 0, r12Count = 0, r13Count = 0, r14Count = 0, r15Count = 0;
            int r16Count = 0, r17Count = 0, r18Count = 0, r19Count = 0, r20Count = 0;
            int r21Count = 0;

            foreach (var kvp in session.Analyzers)
            {
                var targetObject = kvp.Key;
                var analyzer = kvp.Value;

                foreach (var resource in analyzer.ResourceUsage.Keys)
                {
                    if (resource is ParticleSystem ps && scannedPS.Add(ps))
                    {
                        CleanParticleAllRules(ps,
                            ref r2Count, ref r3Count, ref r4Count, ref r5Count, ref r6Count,
                            ref r7Count, ref r8MatCount, ref r8MeshCount, ref r9Count, ref r10Count,
                            ref r11Count, ref r12Count, ref r13Count, ref r14Count, ref r15Count,
                            ref r16Count, ref r17Count, ref r18Count, ref r19Count, ref r20Count,
                            ref r21Count);
                    }
                }

                var allChildren = targetObject.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in allChildren)
                {
                    if (ps == null || ps.Equals(null)) continue;
                    if (!scannedPS.Add(ps)) continue;
                    CleanParticleAllRules(ps,
                        ref r2Count, ref r3Count, ref r4Count, ref r5Count, ref r6Count,
                        ref r7Count, ref r8MatCount, ref r8MeshCount, ref r9Count, ref r10Count,
                        ref r11Count, ref r12Count, ref r13Count, ref r14Count, ref r15Count,
                        ref r16Count, ref r17Count, ref r18Count, ref r19Count, ref r20Count,
                        ref r21Count);
                }
            }

            // 汇总报告
            string msg = "清理完成：";
            var counts = new List<string>();
            if (r2Count > 0) counts.Add($"R2关闭渲染({r2Count})");
            if (r3Count > 0) counts.Add($"R3材质({r3Count})");
            if (r4Count > 0) counts.Add($"R4默认材质({r4Count})");
            if (r5Count > 0) counts.Add($"R5条带材质({r5Count})");
            if (r6Count > 0) counts.Add($"R6模型({r6Count})");
            if (r7Count > 0) counts.Add($"R7模型({r7Count})");
            if (r8MatCount > 0) counts.Add($"R8默认材质({r8MatCount})");
            if (r8MeshCount > 0) counts.Add($"R8默认模型({r8MeshCount})");
            if (r9Count > 0) counts.Add($"R9发射器({r9Count})");
            if (r10Count > 0) counts.Add($"R10材质+改模式({r10Count})");
            if (r11Count > 0) counts.Add($"R11启用粒子({r11Count})");
            if (r12Count > 0) counts.Add($"R12关闭发射({r12Count})");
            if (r13Count > 0) counts.Add($"R13清理突发({r13Count})");
            if (r14Count > 0) counts.Add($"R14关闭限速({r14Count})");
            if (r15Count > 0) counts.Add($"R15重置限速({r15Count})");
            if (r16Count > 0) counts.Add($"R16关闭颜色({r16Count})");
            if (r17Count > 0) counts.Add($"R17关闭大小({r17Count})");
            if (r18Count > 0) counts.Add($"R18关闭旋转({r18Count})");
            if (r19Count > 0) counts.Add($"R19关闭碰撞({r19Count})");
            if (r20Count > 0) counts.Add($"R20关闭触发({r20Count})");
            if (r21Count > 0) counts.Add($"R21清理子发射器({r21Count})");
            msg += counts.Count > 0 ? string.Join(" ", counts) : "未发现冗余资源";
            Debug.Log(msg);
        }

        /// <summary>
        /// 对单个粒子系统执行所有适用的清理规则
        /// </summary>
        private void CleanParticleAllRules(ParticleSystem ps,
            ref int r2, ref int r3, ref int r4, ref int r5, ref int r6,
            ref int r7, ref int r8Mat, ref int r8Mesh, ref int r9, ref int r10,
            ref int r11, ref int r12, ref int r13, ref int r14, ref int r15,
            ref int r16, ref int r17, ref int r18, ref int r19, ref int r20,
            ref int r21)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            bool dirty = false;

            // ===== 组A: None模式系列 =====
            if (renderer != null)
            {
                int mode = (int)renderer.renderMode;
                bool isNoneMode = mode < 0 || mode >= 5;
                bool isMeshMode = renderer.renderMode == ParticleSystemRenderMode.Mesh;

                Material mat = renderer.sharedMaterial;
                Mesh mesh = renderer.mesh;

                if (isNoneMode && renderer.enabled)
                {
                    Undo.RecordObject(renderer, "组A: 清理None模式冗余");

                    // R3: 清理材质球
                    if (mat != null)
                    {
                        renderer.sharedMaterial = null;
                        r3++;
                        dirty = true;
                    }

                    // R7: 清理模型
                    if (mesh != null)
                    {
                        renderer.mesh = null;
                        r7++;
                        dirty = true;
                    }

                    // R2: 关闭渲染模块
                    renderer.enabled = false;
                    r2++;
                    dirty = true;
                }

                // ===== 组B: 模式不匹配 =====

                // R10: Mesh+mesh空+材质 → 清理材质, 改None, 关渲染
                if (isMeshMode && mesh == null && mat != null)
                {
                    Undo.RecordObject(renderer, "R10: 清理材质球，改None，关渲染");
                    renderer.sharedMaterial = null;
                    renderer.renderMode = ParticleSystemRenderMode.None;
                    renderer.enabled = false;
                    r10++;
                    dirty = true;
                }

                // R6: 非None非Mesh+mesh → 清理模型
                if (!isNoneMode && !isMeshMode && mesh != null)
                {
                    if (!dirty) Undo.RecordObject(renderer, "R6: 清理模型");
                    renderer.mesh = null;
                    r6++;
                    dirty = true;
                }

                // ===== 组C: 默认资源冗余 =====

                // R4: 默认粒子材质 → 清理
                if (mat != null && IsDefaultParticleMaterial(mat)
                    && !(isNoneMode && renderer.enabled)) // 组A已处理
                {
                    if (!dirty) Undo.RecordObject(renderer, "R4: 清理默认材质球");
                    renderer.sharedMaterial = null;
                    r4++;
                    dirty = true;
                }

                // R8-材质: Standard shader默认材质 → 清理
                if (mat != null && IsStandardDefaultMaterial(mat)
                    && !(isNoneMode && renderer.enabled)) // 组A已处理
                {
                    if (!dirty) Undo.RecordObject(renderer, "R8: 清理Standard默认材质球");
                    renderer.sharedMaterial = null;
                    r8Mat++;
                    dirty = true;
                }

                // R8-模型: 内置mesh(Cube/Sphere) → 清理（避免与组A/组B重复）
                if (mesh != null && IsBuiltInMesh(mesh)
                    && !(isNoneMode && renderer.enabled)
                    && !(!isNoneMode && !isMeshMode))
                {
                    if (isMeshMode)
                    {
                        if (!dirty) Undo.RecordObject(renderer, "R8: 清理内置模型");
                        renderer.mesh = null;
                        r8Mesh++;
                        dirty = true;
                    }
                }

                // ===== 组D: 专项冗余 =====

                // R5: 条带材质
#if UNITY_2017_1_OR_NEWER
                var trails = ps.trails;
                if (!trails.enabled && renderer.trailMaterial != null)
                {
                    if (!dirty) Undo.RecordObject(renderer, "R5: 清理条带材质球");
                    renderer.trailMaterial = null;
                    r5++;
                    dirty = true;
                }
#endif
            }

            // ===== 组D: R9 发射器模型 =====
            var shape = ps.shape;
            if (shape.enabled)
            {
                var shapeType = shape.shapeType;
                bool isMeshShape = shapeType == ParticleSystemShapeType.Mesh
                    || shapeType == ParticleSystemShapeType.MeshRenderer
                    || shapeType == ParticleSystemShapeType.SkinnedMeshRenderer;

                if (!isMeshShape && shape.mesh != null)
                {
                    Undo.RecordObject(ps, "R9: 清理发射器模型");
                    shape.mesh = null;
                    r9++;
                    dirty = true;
                }
            }

            // ===== 组E: 模块状态冗余 =====
            var main = ps.main;
            var emission = ps.emission;
            var velocityOverLifetime = ps.velocityOverLifetime;
            var colorOverLifetime = ps.colorOverLifetime;
            var sizeOverLifetime = ps.sizeOverLifetime;
            var rotationOverLifetime = ps.rotationOverLifetime;
            var collision = ps.collision;
            var trigger = ps.trigger;
            var subEmitters = ps.subEmitters;

            // R11: GameObject inactive + renderer.enabled==true → 启用 GameObject
            // 注：Unity 2019.4 中 ParticleSystem 无 .enabled 属性，用 GameObject.activeSelf 代替
            if (!ps.gameObject.activeSelf && renderer != null && renderer.enabled)
            {
                Undo.RecordObject(ps.gameObject, "R11: 启用GameObject");
                ps.gameObject.SetActive(true);
                r11++;
                dirty = true;
            }

            // R12: emission enabled + 速率全0 + burstCount==0 → 关闭发射模块
            if (emission.enabled && IsMinMaxCurveZero(emission.rateOverTime)
                && IsMinMaxCurveZero(emission.rateOverDistance) && emission.burstCount == 0)
            {
                Undo.RecordObject(ps, "R12: 关闭发射模块");
                emission.enabled = false;
                r12++;
                dirty = true;
            }

            // R13: emission disabled + burstCount>0 → 清理突发数据
            if (!emission.enabled && emission.burstCount > 0)
            {
                Undo.RecordObject(ps, "R13: 清理突发数据");
                var bursts = new ParticleSystem.Burst[0];
                emission.SetBursts(bursts);
                r13++;
                dirty = true;
            }

            // R14: velocityOverLifetime enabled + x/y/z全0 → 关闭
            if (velocityOverLifetime.enabled
                && IsMinMaxCurveZero(velocityOverLifetime.x)
                && IsMinMaxCurveZero(velocityOverLifetime.y)
                && IsMinMaxCurveZero(velocityOverLifetime.z))
            {
                Undo.RecordObject(ps, "R14: 关闭限速模块");
                velocityOverLifetime.enabled = false;
                r14++;
                dirty = true;
            }

            // R15: velocityOverLifetime disabled + space!=Local → 重置space
            if (!velocityOverLifetime.enabled && velocityOverLifetime.space != ParticleSystemSimulationSpace.Local)
            {
                Undo.RecordObject(ps, "R15: 重置限速模块space");
                velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
                r15++;
                dirty = true;
            }

            // R16: colorOverLifetime enabled + 颜色冗余 → 关闭
            if (colorOverLifetime.enabled && IsColorModuleRedundant(colorOverLifetime, main.startColor))
            {
                Undo.RecordObject(ps, "R16: 关闭颜色模块");
                colorOverLifetime.enabled = false;
                r16++;
                dirty = true;
            }

            // R17: sizeOverLifetime enabled + 大小冗余 → 关闭
            if (sizeOverLifetime.enabled && IsSizeModuleRedundant(sizeOverLifetime, main.startSize))
            {
                Undo.RecordObject(ps, "R17: 关闭大小模块");
                sizeOverLifetime.enabled = false;
                r17++;
                dirty = true;
            }

            // R18: rotationOverLifetime enabled + x/y/z全0 → 关闭
            if (rotationOverLifetime.enabled
                && IsMinMaxCurveZero(rotationOverLifetime.x)
                && IsMinMaxCurveZero(rotationOverLifetime.y)
                && IsMinMaxCurveZero(rotationOverLifetime.z))
            {
                Undo.RecordObject(ps, "R18: 关闭旋转模块");
                rotationOverLifetime.enabled = false;
                r18++;
                dirty = true;
            }

            // R19: collision enabled + planeCount==0 + sendCollisionMessages==false → 关闭
            if (collision.enabled && collision.planeCount == 0 && !collision.sendCollisionMessages)
            {
                Undo.RecordObject(ps, "R19: 关闭碰撞模块");
                collision.enabled = false;
                r19++;
                dirty = true;
            }

            // R20: trigger enabled + colliderCount==0 → 关闭
#if UNITY_2017_1_OR_NEWER
            if (trigger.enabled && trigger.colliderCount == 0)
            {
                Undo.RecordObject(ps, "R20: 关闭触发模块");
                trigger.enabled = false;
                r20++;
                dirty = true;
            }
#endif

            // R21: subEmitters enabled + 任意system==null → 清理无效引用
            if (subEmitters.enabled)
            {
                int subCount = subEmitters.subEmittersCount;
                bool subDirty = false;
                for (int i = 0; i < subCount; i++)
                {
                    var subSystem = subEmitters.GetSubEmitterSystem(i);
                    if (subSystem == null || subSystem.Equals(null))
                    {
                        if (!dirty && !subDirty) Undo.RecordObject(ps, "R21: 清理无效子发射器");
                        subEmitters.SetSubEmitterSystem(i, null);
                        r21++;
                        subDirty = true;
                    }
                }
                dirty = dirty || subDirty;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(ps.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(ps.gameObject);
            }
        }

        #endregion

        public void Clear()
        {
            categoryFoldouts.Clear();
            typeFoldouts.Clear();
            itemFoldouts.Clear();
        }
    }
}
