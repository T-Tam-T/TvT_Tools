// ResourceManager/Core/ConditionManager.cs
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace ResourceManager.Core
{
    [System.Serializable]
    public class ConditionSettings
    {
        // 通用条件
        public bool checkNameSpace = true;
        public Color nameSpaceColor = Color.red;

        public bool checkNameCustom = true; // 改为自定义名称检查
        public string nameCustomString = "zwz"; // 自定义要检查的字符串
        public Color nameCustomColor = new Color(1f, 0.5f, 0f); // 橙色

        public bool checkNameUppercase = true;
        public Color nameUppercaseColor = Color.red;

        // 网格条件
        public bool checkVertexCount = true;
        public int vertexCountWarning = 3000;
        public int vertexCountError = 5000;
        public Color vertexCountWarningColor = Color.yellow;
        public Color vertexCountErrorColor = Color.red;

        public bool checkTriangleCount = true;
        public int triangleCountWarning = 3000;
        public int triangleCountError = 5000;
        public Color triangleCountWarningColor = Color.yellow;
        public Color triangleCountErrorColor = Color.red;

        // 纹理条件
        public bool checkTextureSize = true;
        public int textureSizeWarning = 512;
        public int textureSizeError = 1024;
        public Color textureSizeWarningColor = Color.yellow;
        public Color textureSizeErrorColor = Color.red;

        public bool checkDDSFormat = true;
        public Color ddsFormatColor = Color.red;

        // 纹理格式条件
        public bool checkTGAFormat = true;
        public Color tgaFormatColor = Color.red;

        public bool checkNonPngFormat = true;
        public Color nonPngFormatColor = Color.red;

        // 贴图平台设置
        public bool checkTextureStandaloneOverride = true;  // Standalone Override应关闭
        public Color standaloneOverrideColor = Color.red;

        public bool checkTextureAndroidSettings = true;     // 启用Android检查
        public bool checkAndroidOverride = true;            // Android Override应开启
        public int androidMaxSizeThreshold = 512;            // Android Max Size阈值
        public Color androidOverrideColor = Color.red;      // Override未开启
        public Color androidMaxSizeColor = Color.yellow;    // MaxSize超限
        public Color androidFormatColor = Color.red;        // 格式不正确

        public bool checkTextureIOSSettings = true;         // 启用iOS检查
        public bool checkIOSOverride = true;                // iOS Override应开启
        public int iOSMaxSizeThreshold = 512;                // iOS Max Size阈值
        public Color iosOverrideColor = Color.red;          // Override未开启
        public Color iosMaxSizeColor = Color.yellow;        // MaxSize超限
        public Color iosFormatColor = Color.red;            // 格式不正确

        // 动画条件
        public bool checkClipCount = true;
        public Color clipCountColor = Color.red;

        public bool checkFPS = true;
        public float targetFPS = 30f;
        public Color fpsColor = Color.red;

        public bool checkLooping = true;
        public Color loopingColor = Color.yellow;

        // 粒子系统条件（原有部分保持不变）
        public bool checkStartDelay = true;
        public float startDelayThreshold = 0.1f;
        public Color startDelayColor = Color.red;

        public bool checkMaxParticles = true;
        public int maxParticlesThreshold = 30;
        public Color maxParticlesColor = Color.red;

        public bool checkMaxParticleSize = true;
        public float maxParticleSizeThreshold = 3f;
        public Color maxParticleSizeColor = Color.red;

        public bool checkOrderInLayer = true;
        public int orderInLayerThreshold = 0;
        public Color orderInLayerColor = Color.red;

        public bool checkScalingMode = true;
        public Color scalingModeColor = Color.red;

        public bool checkLayer = true;
        public Color layerColor = Color.red;

        public bool checkActive = true;
        public Color activeColor = Color.red;
        
        // 粒子模块检查 - 现在每个模块都有独立的布尔变量
        public bool checkParticleEmission = true;
        public Color particleEmissionColor = Color.red;
        
        public bool checkParticleShape = true;
        public Color particleShapeColor = Color.red;
        
        public bool checkParticleNoise = true;
        public Color particleNoiseColor = Color.red;
        
        public bool checkParticleVelocityOverLifetime = true;
        public Color particleVelocityOverLifetimeColor = Color.red;
        
        public bool checkParticleColorOverLifetime = true;
        public Color particleColorOverLifetimeColor = Color.red;
        
        public bool checkParticleSizeOverLifetime = true;
        public Color particleSizeOverLifetimeColor = Color.red;
        
        // 新增的粒子模块变量
        public bool checkParticleLimitVelocityOverLifetime = true;
        public bool checkParticleInheritVelocity = true;
        public bool checkParticleForceOverLifetime = true;
        public bool checkParticleColorBySpeed = true;
        public bool checkParticleSizeBySpeed = true;
        public bool checkParticleRotationOverLifetime = true;
        public bool checkParticleRotationBySpeed = true;
        public bool checkParticleExternalForces = true;
        public bool checkParticleCollision = true;
        public bool checkParticleTrigger = true;
        public bool checkParticleSubEmitters = true;
        public bool checkParticleTextureSheetAnimation = true;
        public bool checkParticleLights = true;
        public bool checkParticleTrails = true;
        public bool checkParticleCustomData = true;

        // 冗余检测
        public bool checkRedundancy = true;
        public Color redundancyColor = Color.magenta;

        public string MaxParticleSizeDescription =>
            $"粒子最大渲染尺寸(非Mesh模式)：<color=#{ColorUtility.ToHtmlStringRGB(maxParticleSizeColor)}>MPS(≠{maxParticleSizeThreshold:F1})</color>";

        // 保存设置
        public void SaveSettings()
        {
            string json = JsonUtility.ToJson(this);
            EditorPrefs.SetString("ResourceManager_ConditionSettings", json);
        }

        // 加载设置
        public void LoadSettings()
        {
            if (EditorPrefs.HasKey("ResourceManager_ConditionSettings"))
            {
                string json = EditorPrefs.GetString("ResourceManager_ConditionSettings");
                JsonUtility.FromJsonOverwrite(json, this);
            }
        }
    }

    public class ConditionManager
    {
        private static ConditionManager instance;
        public static ConditionManager Instance => instance ?? (instance = new ConditionManager());

        public ConditionSettings Settings = new ConditionSettings();

        private ConditionManager()
        {
            Settings.LoadSettings();
        }

        // 名称条件检查
        public bool CheckNameConditions(string name, out Color? color)
        {
            color = null;

            if (Settings.checkNameSpace && name.Contains(" "))
            {
                color = Settings.nameSpaceColor;
                return true;
            }

            if (Settings.checkNameCustom && !string.IsNullOrEmpty(Settings.nameCustomString) && 
                name.Contains(Settings.nameCustomString))
            {
                color = Settings.nameCustomColor;
                return true;
            }

            if (Settings.checkNameUppercase && ContainsUppercase(name))
            {
                color = Settings.nameUppercaseColor;
                return true;
            }

            return false;
        }

        // 改为公共方法
        public bool ContainsUppercase(string str)
        {
            foreach (char c in str)
            {
                if (char.IsUpper(c)) return true;
            }
            return false;
        }

        // 网格条件检查
        public bool CheckMeshConditions(Mesh mesh, out Color? color)
        {
            color = null;

            if (Settings.checkVertexCount)
            {
                int vertexCount = mesh.vertexCount;
                if (vertexCount > Settings.vertexCountError)
                {
                    color = Settings.vertexCountErrorColor;
                    return true;
                }

                if (vertexCount > Settings.vertexCountWarning)
                {
                    color = Settings.vertexCountWarningColor;
                    return true;
                }
            }

            if (Settings.checkTriangleCount)
            {
                int triangleCount = mesh.triangles.Length / 3;
                if (triangleCount > Settings.triangleCountError)
                {
                    color = Settings.triangleCountErrorColor;
                    return true;
                }

                if (triangleCount > Settings.triangleCountWarning)
                {
                    color = Settings.triangleCountWarningColor;
                    return true;
                }
            }

            return false;
        }

        // 纹理条件检查
        public bool CheckTextureConditions(Texture2D tex, out Color? color)
        {
            color = null;

            if (Settings.checkTextureSize)
            {
                if (tex.width > Settings.textureSizeError || tex.height > Settings.textureSizeError)
                {
                    color = Settings.textureSizeErrorColor;
                    return true;
                }

                if (tex.width > Settings.textureSizeWarning || tex.height > Settings.textureSizeWarning)
                {
                    color = Settings.textureSizeWarningColor;
                    return true;
                }
            }

            // 纹理格式检查（TGA / DDS / 非 PNG）
            string assetPath = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string lowerPath = assetPath.ToLower();

                if (Settings.checkTGAFormat && lowerPath.EndsWith(".tga"))
                {
                    color = Settings.tgaFormatColor;
                    return true;
                }

                if (Settings.checkDDSFormat && lowerPath.EndsWith(".dds"))
                {
                    color = Settings.ddsFormatColor;
                    return true;
                }

                if (Settings.checkNonPngFormat && !lowerPath.EndsWith(".png"))
                {
                    color = Settings.nonPngFormatColor;
                    return true;
                }
            }

            return false;
        }

        // 动画条件检查
        public bool CheckAnimationConditions(AnimationClip clip, RuntimeAnimatorController controller,
                                            ResourceAnalyzer analyzer, out Color? color)
        {
            color = null;

            if (Settings.checkClipCount && analyzer.AnimatorClipCount.TryGetValue(controller, out int clipCount) &&
                clipCount > 1)
            {
                color = Settings.clipCountColor;
                return true;
            }

            if (Settings.checkFPS && Mathf.Abs(clip.frameRate - Settings.targetFPS) > 0.01f)
            {
                color = Settings.fpsColor;
                return true;
            }

            if (Settings.checkLooping && clip.isLooping)
            {
                color = Settings.loopingColor;
                return true;
            }

            return false;
        }

        // 获取平台问题详细描述列表（不打断现有的单问题返回模式）
        public List<(string problemType, Color color)> GetTexturePlatformProblemDetails(Texture2D tex)
        {
            var problems = new List<(string, Color)>();
            string assetPath = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(assetPath)) return problems;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return problems;

            if (Settings.checkTextureStandaloneOverride)
            {
                try
                {
                    var standaloneSettings = importer.GetPlatformTextureSettings("Standalone");
                    if (standaloneSettings.overridden)
                        problems.Add(("Standalone Override已开启", Settings.standaloneOverrideColor));
                }
                catch (System.Exception) { /* Standalone平台不可用，跳过 */ }
            }

            if (Settings.checkTextureAndroidSettings)
            {
                try
                {
                    var androidSettings = importer.GetPlatformTextureSettings("Android");
                    if (Settings.checkAndroidOverride && !androidSettings.overridden)
                        problems.Add(("Android Override未开启", Settings.androidOverrideColor));
                    if (androidSettings.overridden)
                    {
                        if (androidSettings.maxTextureSize > Settings.androidMaxSizeThreshold)
                            problems.Add(($"Android MaxSize>{Settings.androidMaxSizeThreshold}(当前:{androidSettings.maxTextureSize})", Settings.androidMaxSizeColor));
                        if (!IsAcceptedMobileFormat(androidSettings.format))
                            problems.Add(($"Android格式不是5×5/6×6(当前:{androidSettings.format})", Settings.androidFormatColor));
                    }
                }
                catch (System.Exception) { /* Android平台未安装，跳过 */ }
            }

            if (Settings.checkTextureIOSSettings)
            {
                try
                {
                    var iosSettings = importer.GetPlatformTextureSettings("iPhone");
                    if (Settings.checkIOSOverride && !iosSettings.overridden)
                        problems.Add(("iOS Override未开启", Settings.iosOverrideColor));
                    if (iosSettings.overridden)
                    {
                        if (iosSettings.maxTextureSize > Settings.iOSMaxSizeThreshold)
                            problems.Add(($"iOS MaxSize>{Settings.iOSMaxSizeThreshold}(当前:{iosSettings.maxTextureSize})", Settings.iosMaxSizeColor));
                        if (!IsAcceptedMobileFormat(iosSettings.format))
                            problems.Add(($"iOS格式不是5×5/6×6(当前:{iosSettings.format})", Settings.iosFormatColor));
                    }
                }
                catch (System.Exception) { /* iOS平台未安装，跳过 */ }
            }

            return problems;
        }

        /// <summary>
        /// 检查贴图是否存在平台设置问题（供单问题返回模式的调用方使用，如 OverviewModule / StatusIndicator / TextureModule 的着色逻辑）
        /// </summary>
        public bool CheckTexturePlatformConditions(Texture2D tex, out Color? color, out string problemType)
        {
            color = null;
            problemType = null;
            var problems = GetTexturePlatformProblemDetails(tex);
            if (problems.Count > 0)
            {
                problemType = problems[0].problemType;
                color = problems[0].color;
                return true;
            }
            return false;
        }

        private bool IsAcceptedMobileFormat(TextureImporterFormat format)
        {
            string name = format.ToString();
            return name.Contains("ASTC_5x5") || name.Contains("ASTC_6x6")
                || name.Contains("ASTC_5x6") || name.Contains("ASTC_6x5")
                || name == "ETC2_RGB5" || name == "ETC2_RGBA5"
                || name == "ETC2_RGB6" || name == "ETC2_RGBA6";
        }

        // 粒子系统条件检查
        public bool CheckParticleConditions(ParticleSystem ps, out Color? color)
        {
            color = null;
            
            // 安全检查：确保粒子系统有效
            if (ps == null || ps.Equals(null))
            {
                return false;
            }

            // 在单独的try-catch块中获取所有模块
            ParticleSystem.MainModule main;
            ParticleSystemRenderer renderer = null;
            ParticleSystem.EmissionModule emission;
            ParticleSystem.ShapeModule shape;
            ParticleSystem.NoiseModule noise;
            ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime;
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime;
            ParticleSystem.LimitVelocityOverLifetimeModule limitVelocityOverLifetime;
            ParticleSystem.InheritVelocityModule inheritVelocity;
            ParticleSystem.ForceOverLifetimeModule forceOverLifetime;
            ParticleSystem.ColorBySpeedModule colorBySpeed;
            ParticleSystem.SizeBySpeedModule sizeBySpeed;
            ParticleSystem.RotationOverLifetimeModule rotationOverLifetime;
            ParticleSystem.RotationBySpeedModule rotationBySpeed;
            ParticleSystem.ExternalForcesModule externalForces;
            ParticleSystem.CollisionModule collision;
            ParticleSystem.TriggerModule trigger;
            ParticleSystem.SubEmittersModule subEmitters;
            ParticleSystem.TextureSheetAnimationModule textureSheetAnimation;
            ParticleSystem.LightsModule lights;
            // ParticleSystem.TrailsModule trails; // This does not exist in some Unity versions! Remove it.
            ParticleSystem.CustomDataModule customData;

            try
            {
                main = ps.main;
                renderer = ps.GetComponent<ParticleSystemRenderer>();
                
                emission = ps.emission;
                shape = ps.shape;
                noise = ps.noise;
                velocityOverLifetime = ps.velocityOverLifetime;
                colorOverLifetime = ps.colorOverLifetime;
                sizeOverLifetime = ps.sizeOverLifetime;
                limitVelocityOverLifetime = ps.limitVelocityOverLifetime;
                inheritVelocity = ps.inheritVelocity;
                forceOverLifetime = ps.forceOverLifetime;
                colorBySpeed = ps.colorBySpeed;
                sizeBySpeed = ps.sizeBySpeed;
                rotationOverLifetime = ps.rotationOverLifetime;
                rotationBySpeed = ps.rotationBySpeed;
                externalForces = ps.externalForces;
                collision = ps.collision;
                trigger = ps.trigger;
                subEmitters = ps.subEmitters;
                textureSheetAnimation = ps.textureSheetAnimation;
                lights = ps.lights;
                // trails = ps.trails; // Remove direct TrailsModule usage due to Unity version incompatibility
                customData = ps.customData;
            }
            catch (System.NullReferenceException)
            {
                // 粒子系统已被销毁或无效
                return false;
            }
            catch (System.Exception)
            {
                // 其他异常
                return false;
            }

            // 原有条件检查
            if (Settings.checkStartDelay && main.startDelay.constant > Settings.startDelayThreshold)
            {
                color = Settings.startDelayColor;
                return true;
            }

            if (Settings.checkMaxParticles && main.maxParticles > Settings.maxParticlesThreshold)
            {
                color = Settings.maxParticlesColor;
                return true;
            }

            // 只有在渲染模式不是Mesh时才检查最大粒子尺寸
            try
            {
                if (renderer != null &&
                    Settings.checkMaxParticleSize &&
                    renderer.renderMode != ParticleSystemRenderMode.Mesh &&
                    Mathf.Abs(renderer.maxParticleSize - Settings.maxParticleSizeThreshold) > 0.01f)
                {
                    color = Settings.maxParticleSizeColor;
                    return true;
                }

                if (renderer != null && Settings.checkOrderInLayer &&
                    renderer.sortingOrder != Settings.orderInLayerThreshold)
                {
                    color = Settings.orderInLayerColor;
                    return true;
                }

                // 冗余检测（按21条规则表）
                if (renderer != null && Settings.checkRedundancy)
                {
                    int mode = (int)renderer.renderMode;
                    bool isNoneMode = mode < 0 || mode >= 5;
                    bool isMeshMode = renderer.renderMode == ParticleSystemRenderMode.Mesh;

                    // 组A: R2 — None+enabled+(材质或mesh非空)
                    if (isNoneMode && renderer.enabled
                        && (renderer.sharedMaterial != null || renderer.mesh != null))
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }
                    // 组A: R2纯净版 — None+enabled+无引用
                    if (isNoneMode && renderer.enabled)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组B: R10 — Mesh+mesh空+材质非空
                    if (isMeshMode && renderer.mesh == null && renderer.sharedMaterial != null)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组B: R6 — 非Mesh(非None)+mesh非空
                    if (!isNoneMode && !isMeshMode && renderer.mesh != null)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组C: R4 — 默认粒子材质(Default-Particle / Particles/Standard Unlit未修改)
                    if (renderer.sharedMaterial != null && IsDefaultParticleMaterial(renderer.sharedMaterial))
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组C: R8 — Standard shader默认材质 或 内置mesh
                    if (renderer.sharedMaterial != null && IsStandardDefaultMaterial(renderer.sharedMaterial))
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }
                    if (renderer.mesh != null && IsBuiltInMesh(renderer.mesh))
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组D: R1 — renderer关+mainTexture非空
                    if (!renderer.enabled && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }

                    // 组D: R5 — Trails未启用但trailMaterial非空
                    if (!ps.trails.enabled && renderer.trailMaterial != null)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }
                }

                // 组D: R9 — Shape非Mesh类型+mesh非空
                if (Settings.checkRedundancy && shape.enabled)
                {
                    var st = shape.shapeType;
                    if (st != ParticleSystemShapeType.Mesh
                        && st != ParticleSystemShapeType.MeshRenderer
                        && st != ParticleSystemShapeType.SkinnedMeshRenderer
                        && shape.mesh != null)
                    {
                        color = Settings.redundancyColor;
                        return true;
                    }
                }
            }
            catch (System.NullReferenceException)
            {
                // 忽略渲染器异常
            }

            if (Settings.checkScalingMode &&
                main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            {
                color = Settings.scalingModeColor;
                return true;
            }

            if (Settings.checkLayer && ps.gameObject.layer != LayerMask.NameToLayer("Default"))
            {
                color = Settings.layerColor;
                return true;
            }

            if (Settings.checkActive && !ps.gameObject.activeInHierarchy)
            {
                color = Settings.activeColor;
                return true;
            }

            // 新增粒子模块检查 - 每个模块都有独立的布尔变量
            try
            {
                if (Settings.checkParticleEmission && emission.enabled)
                {
                    color = Settings.particleEmissionColor;
                    return true;
                }

                if (Settings.checkParticleShape && shape.enabled)
                {
                    color = Settings.particleShapeColor;
                    return true;
                }

                if (Settings.checkParticleNoise && noise.enabled)
                {
                    color = Settings.particleNoiseColor;
                    return true;
                }

                if (Settings.checkParticleVelocityOverLifetime && velocityOverLifetime.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleColorOverLifetime && colorOverLifetime.enabled)
                {
                    color = Settings.particleColorOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleSizeOverLifetime && sizeOverLifetime.enabled)
                {
                    color = Settings.particleSizeOverLifetimeColor;
                    return true;
                }

                // 新增的粒子模块检查
                if (Settings.checkParticleLimitVelocityOverLifetime && limitVelocityOverLifetime.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleInheritVelocity && inheritVelocity.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleForceOverLifetime && forceOverLifetime.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleColorBySpeed && colorBySpeed.enabled)
                {
                    color = Settings.particleColorOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleSizeBySpeed && sizeBySpeed.enabled)
                {
                    color = Settings.particleSizeOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleRotationOverLifetime && rotationOverLifetime.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleRotationBySpeed && rotationBySpeed.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleExternalForces && externalForces.enabled)
                {
                    color = Settings.particleVelocityOverLifetimeColor;
                    return true;
                }

                if (Settings.checkParticleCollision && collision.enabled)
                {
                    color = Settings.particleShapeColor;
                    return true;
                }

                if (Settings.checkParticleTrigger && trigger.enabled)
                {
                    color = Settings.particleShapeColor;
                    return true;
                }

                if (Settings.checkParticleSubEmitters && subEmitters.enabled)
                {
                    color = Settings.particleEmissionColor;
                    return true;
                }

                if (Settings.checkParticleTextureSheetAnimation && textureSheetAnimation.enabled)
                {
                    color = Settings.particleNoiseColor;
                    return true;
                }

                if (Settings.checkParticleLights && lights.enabled)
                {
                    color = Settings.particleNoiseColor;
                    return true;
                }

                // ----- The following check for "trails" is rewritten to avoid usage of ParticleSystem.TrailsModule -----
                if (Settings.checkParticleTrails)
                {
                    // ParticleSystem.trails returns a struct in modern Unity, but TrailsModule doesn't exist in all versions.
#if UNITY_2017_1_OR_NEWER
                    if (ps.trails.enabled)
                    {
                        color = Settings.particleNoiseColor;
                        return true;
                    }
#endif
                }
                // ----------------------------------------------------------------------------------------------

                if (Settings.checkParticleCustomData && customData.enabled)
                {
                    color = Settings.particleNoiseColor;
                    return true;
                }
            }
            catch (System.NullReferenceException)
            {
                // 模块可能无效，忽略异常
                return false;
            }

            return false;
        }

        /// <summary>
        /// 冗余检测：Material shader toggle OFF 但对应贴图槽非空
        /// 返回冗余问题列表 (问题描述, 对应贴图, 贴图属性名)
        /// </summary>
        public List<(string problemDesc, Texture texture, string propertyName)> CheckMaterialRedundancy(Material mat)
        {
            var results = new List<(string, Texture, string)>();
            if (mat == null || mat.shader == null) return results;

            Shader shader = mat.shader;
            int propCount = ShaderUtil.GetPropertyCount(shader);

            // 第一遍：收集所有 OFF 状态的 Toggle 属性 (propName, propDesc)
            var disabledToggles = new List<(string name, string desc)>();
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.Float)
                    continue;

                string propName = ShaderUtil.GetPropertyName(shader, i);
                string propDesc = ShaderUtil.GetPropertyDescription(shader, i);

                bool isToggle = propDesc.Contains("Toggle") || propDesc.Contains("开关");
                if (!isToggle) continue;

                float val = mat.GetFloat(propName);
                if (Mathf.Approximately(val, 0f))
                {
                    disabledToggles.Add((propName, propDesc));
                }
            }

            if (disabledToggles.Count == 0) return results;

            // 第二遍：检查每个 OFF 的 toggle 对应的贴图槽是否非空
            foreach (var (toggleName, toggleDesc) in disabledToggles)
            {
                string keyword = ExtractKeywordFromToggle(toggleName);
                if (string.IsNullOrEmpty(keyword)) continue;

                for (int i = 0; i < propCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                        continue;

                    string texName = ShaderUtil.GetPropertyName(shader, i);
                    if (texName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Texture tex = mat.GetTexture(texName);
                        if (tex != null)
                        {
                            results.Add(($"开关 [{toggleDesc}] 关闭但贴图已设置",
                                tex, texName));
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 从 Toggle 属性名提取关键词用于匹配贴图
        /// Eg: _EnableEmission → Emission, _UseNormalMap → Normal, _ToggleDetail → Detail
        /// </summary>
        private string ExtractKeywordFromToggle(string toggleName)
        {
            if (string.IsNullOrEmpty(toggleName)) return null;

            string name = toggleName;
            foreach (var prefix in new[] { "_Enable", "_Use", "_Toggle", "_Has", "_Show" })
            {
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }

            name = name.TrimStart('_');
            if (name.Length < 2) return null;

            return name;
        }

        #region 默认资源检测辅助方法（R4/R8）

        /// <summary>
        /// R4: 判断材质是否为粒子默认材质
        /// 条件1: Unity 内置资源（路径检测），与粒子相关的材质
        /// 条件2: 名称包含 "Default-Particle"
        /// 条件3: Shader 为 "Particles/Standard Unlit" 且无纹理修改、颜色为白色
        /// </summary>
        public bool IsDefaultParticleMaterial(Material mat)
        {
            if (mat == null) return false;

            // 条件1: 通过资源路径检测Unity内置材质（最可靠，不受名称/Shader名变化影响）
            string path = AssetDatabase.GetAssetPath(mat);
            bool isBuiltinAsset = string.IsNullOrEmpty(path)
                || path.Contains("unity_builtin_extra")
                || path.Contains("unity default resources");
            if (isBuiltinAsset)
            {
                if (mat.name.Contains("Default") || mat.name.Contains("Particle")
                    || (mat.shader != null && (mat.shader.name.Contains("Particle")
                        || mat.shader.name.Contains("Particles"))))
                {
                    return true;
                }
            }

            if (mat.name.Contains("Default-Particle")) return true;

            if (mat.shader != null && mat.shader.name == "Particles/Standard Unlit")
            {
                bool hasTexture = false;
                int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                for (int i = 0; i < propCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                        if (mat.GetTexture(propName) != null) { hasTexture = true; break; }
                    }
                }
                if (hasTexture) return false;

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
        /// R8-材质: 判断材质是否为 Standard shader 默认材质（无纹理 + 白色）
        /// 补充: 通过资源路径检测Unity内置Standard材质（不受名称变化影响）
        /// </summary>
        public bool IsStandardDefaultMaterial(Material mat)
        {
            if (mat == null) return false;

            // 补充检测：通过资源路径识别Unity内置默认材质
            string path = AssetDatabase.GetAssetPath(mat);
            bool isBuiltinAsset = string.IsNullOrEmpty(path)
                || path.Contains("unity_builtin_extra")
                || path.Contains("unity default resources");

            if (isBuiltinAsset && mat.shader != null && mat.shader.name == "Standard")
            {
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
        /// R8-模型: 判断 Mesh 是否为 Unity 内置默认模型（Cube/Sphere/Cylinder/Plane/Capsule）
        /// </summary>
        public bool IsBuiltInMesh(Mesh mesh)
        {
            if (mesh == null) return false;
            string path = AssetDatabase.GetAssetPath(mesh);
            bool isBuiltInResource = string.IsNullOrEmpty(path) || path.Contains("unity_builtin_extra");
            if (!isBuiltInResource) return false;

            string name = mesh.name;
            return name == "Cube" || name == "Sphere" || name == "Cylinder"
                || name == "Plane" || name == "Capsule";
        }

        #endregion
    }
}