using UnityEngine;
using UnityEditor;
using ResourceManager.Core;

namespace ResourceManager
{
    public class ConditionSettingsWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private ConditionManager conditionManager;
        private ConditionSettings settings;
        
        // 折叠状态 — 默认全部折叠
        private bool namingFoldout = false;
        private bool meshFoldout = false;
        private bool textureFoldout = false;
        private bool texturePlatformFoldout = false; // 贴图平台设置折叠
        private bool animationFoldout = false;
        private bool particleFoldout = false;
        private bool particleModulesFoldout = false; // 粒子模块折叠

        [MenuItem("Tools/TvTTools/条件设置", false, 50)]
        public static void ShowWindow()
        {
            GetWindow<ConditionSettingsWindow>("条件设置");
        }

        public static void ShowWindowFromButton()
        {
            var window = GetWindow<ConditionSettingsWindow>("条件设置");
            window.Show();
        }

        private void OnEnable()
        {
            conditionManager = ConditionManager.Instance;
            settings = conditionManager.Settings;
        }

        private void OnGUI()
        {
            if (conditionManager == null || settings == null)
            {
                OnEnable();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // === 命名规范相关 ===
            namingFoldout = EditorGUILayout.Foldout(namingFoldout, "命名规范", true, EditorStyles.foldoutHeader);
            if (namingFoldout)
            {
                EditorGUI.indentLevel++;
                
                DrawSimpleCondition("名称含空格", ref settings.checkNameSpace, ref settings.nameSpaceColor);
                DrawSimpleCondition($"名称含有'{settings.nameCustomString}'", ref settings.checkNameCustom, ref settings.nameCustomColor);
                
                // 自定义字符串设置
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(154);
                GUILayout.Label("自定义字符串:", GUILayout.Width(80));
                settings.nameCustomString = EditorGUILayout.TextField(settings.nameCustomString, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();

                DrawSimpleCondition("名称首字母大写", ref settings.checkNameUppercase, ref settings.nameUppercaseColor);
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // === 网格相关 ===
            meshFoldout = EditorGUILayout.Foldout(meshFoldout, "网格相关", true, EditorStyles.foldoutHeader);
            if (meshFoldout)
            {
                EditorGUI.indentLevel++;
                
                DrawSingleThresholdCondition("顶点数", ref settings.checkVertexCount, "警告阈值", 
                    ref settings.vertexCountWarning, ref settings.vertexCountWarningColor);
                DrawSingleThresholdCondition("", ref settings.checkVertexCount, "错误阈值", 
                    ref settings.vertexCountError, ref settings.vertexCountErrorColor, true);
                DrawSingleThresholdCondition("三角面数", ref settings.checkTriangleCount, "警告阈值", 
                    ref settings.triangleCountWarning, ref settings.triangleCountWarningColor);
                DrawSingleThresholdCondition("", ref settings.checkTriangleCount, "错误阈值", 
                    ref settings.triangleCountError, ref settings.triangleCountErrorColor, true);
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // === 贴图相关 ===
            textureFoldout = EditorGUILayout.Foldout(textureFoldout, "贴图相关", true, EditorStyles.foldoutHeader);
            if (textureFoldout)
            {
                EditorGUI.indentLevel++;
                
                DrawSingleThresholdCondition("贴图尺寸", ref settings.checkTextureSize, "警告阈值", 
                    ref settings.textureSizeWarning, ref settings.textureSizeWarningColor);
                DrawSingleThresholdCondition("", ref settings.checkTextureSize, "错误阈值", 
                    ref settings.textureSizeError, ref settings.textureSizeErrorColor, true);
                DrawSimpleCondition("禁止DDS格式", ref settings.checkDDSFormat, ref settings.ddsFormatColor);
                DrawSimpleCondition("禁止TGA格式", ref settings.checkTGAFormat, ref settings.tgaFormatColor);
                DrawSimpleCondition("禁止非PNG格式", ref settings.checkNonPngFormat, ref settings.nonPngFormatColor);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // === 贴图平台设置 ===
            texturePlatformFoldout = EditorGUILayout.Foldout(texturePlatformFoldout, "贴图平台设置", true, EditorStyles.foldoutHeader);
            if (texturePlatformFoldout)
            {
                EditorGUI.indentLevel++;

                // Standalone
                GUILayout.BeginVertical("box");
                GUILayout.Label("Standalone (Windows/Mac/Linux)", EditorStyles.boldLabel);
                DrawSimpleCondition("Override应关闭", ref settings.checkTextureStandaloneOverride, ref settings.standaloneOverrideColor);
                EditorGUILayout.HelpBox("Standalone平台的Override应保持关闭，使用默认值", MessageType.Info);
                GUILayout.EndVertical();

                EditorGUILayout.Space();

                // Android
                GUILayout.BeginVertical("box");
                GUILayout.Label("Android", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                settings.checkTextureAndroidSettings = EditorGUILayout.ToggleLeft("启用Android检测", settings.checkTextureAndroidSettings, GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();

                if (settings.checkTextureAndroidSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawSimpleCondition("Override应开启", ref settings.checkAndroidOverride, ref settings.androidOverrideColor);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(154);
                    GUILayout.Label("MaxSize阈值:", GUILayout.Width(80));
                    settings.androidMaxSizeThreshold = EditorGUILayout.IntField(settings.androidMaxSizeThreshold, GUILayout.Width(60));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("标记色:", GUILayout.Width(40));
                    settings.androidMaxSizeColor = EditorGUILayout.ColorField("", settings.androidMaxSizeColor, GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.Label("格式: 接受5×5/6×6(ETC2/ASTC)", GUILayout.Width(200));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("标记色:", GUILayout.Width(40));
                    settings.androidFormatColor = EditorGUILayout.ColorField("", settings.androidFormatColor, GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel--;
                }
                GUILayout.EndVertical();

                EditorGUILayout.Space();

                // iOS
                GUILayout.BeginVertical("box");
                GUILayout.Label("iOS", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                settings.checkTextureIOSSettings = EditorGUILayout.ToggleLeft("启用iOS检测", settings.checkTextureIOSSettings, GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();

                if (settings.checkTextureIOSSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawSimpleCondition("Override应开启", ref settings.checkIOSOverride, ref settings.iosOverrideColor);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(154);
                    GUILayout.Label("MaxSize阈值:", GUILayout.Width(80));
                    settings.iOSMaxSizeThreshold = EditorGUILayout.IntField(settings.iOSMaxSizeThreshold, GUILayout.Width(60));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("标记色:", GUILayout.Width(40));
                    settings.iosMaxSizeColor = EditorGUILayout.ColorField("", settings.iosMaxSizeColor, GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.Label("格式: 接受5×5/6×6(ASTC)", GUILayout.Width(200));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("标记色:", GUILayout.Width(40));
                    settings.iosFormatColor = EditorGUILayout.ColorField("", settings.iosFormatColor, GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel--;
                }
                GUILayout.EndVertical();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // === 动画相关 ===
            animationFoldout = EditorGUILayout.Foldout(animationFoldout, "动画相关", true, EditorStyles.foldoutHeader);
            if (animationFoldout)
            {
                EditorGUI.indentLevel++;
                
                DrawSimpleCondition("剪辑数大于1", ref settings.checkClipCount, ref settings.clipCountColor);
                DrawSingleThresholdCondition("FPS(目标≤30)", ref settings.checkFPS, "目标值", 
                    ref settings.targetFPS, ref settings.fpsColor);
                DrawSimpleCondition("需为循环", ref settings.checkLooping, ref settings.loopingColor);
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // === 粒子相关 ===
            particleFoldout = EditorGUILayout.Foldout(particleFoldout, "粒子相关", true, EditorStyles.foldoutHeader);
            if (particleFoldout)
            {
                EditorGUI.indentLevel++;

                // 冗余检测（第一行，默认勾选）
                DrawSimpleCondition("冗余检测（粒子/材质）", ref settings.checkRedundancy, ref settings.redundancyColor);
                EditorGUILayout.Space();

                DrawSingleThresholdCondition("StartDelay(>0.1)", ref settings.checkStartDelay, "阈值", 
                    ref settings.startDelayThreshold, ref settings.startDelayColor);
                DrawSingleThresholdCondition("MaxParticles(>30)", ref settings.checkMaxParticles, "阈值", 
                    ref settings.maxParticlesThreshold, ref settings.maxParticlesColor);
                DrawSingleThresholdCondition("最大粒子尺寸(≥3)", ref settings.checkMaxParticleSize, "阈值", 
                    ref settings.maxParticleSizeThreshold, ref settings.maxParticleSizeColor);
                DrawSingleThresholdCondition("OrderInLayer(≥0)", ref settings.checkOrderInLayer, "阈值", 
                    ref settings.orderInLayerThreshold, ref settings.orderInLayerColor);
                DrawSimpleCondition("ScalingMode(≠Hierarchy)", ref settings.checkScalingMode, ref settings.scalingModeColor);
                DrawSimpleCondition("Layer(≠Default)", ref settings.checkLayer, ref settings.layerColor);
                DrawSimpleCondition("必须Active(激活)", ref settings.checkActive, ref settings.activeColor);

                EditorGUILayout.Space();

                // === 粒子模块检查 ===
                particleModulesFoldout = EditorGUILayout.Foldout(particleModulesFoldout, "粒子模块检查", true);
                if (particleModulesFoldout)
                {
                    EditorGUI.indentLevel++;
                    
                    // 使用Flow布局来自动换行
                    GUILayout.BeginVertical("box");
                    
                    // 第一行：基本模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("Emission模块", ref settings.checkParticleEmission);
                    DrawParticleModuleCondition("Shape模块", ref settings.checkParticleShape);
                    DrawParticleModuleCondition("Noise模块", ref settings.checkParticleNoise);
                    GUILayout.EndHorizontal();
                    
                    // 第二行：生命周期模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("VelocityOverLifetime模块", ref settings.checkParticleVelocityOverLifetime);
                    DrawParticleModuleCondition("ColorOverLifetime模块", ref settings.checkParticleColorOverLifetime);
                    DrawParticleModuleCondition("SizeOverLifetime模块", ref settings.checkParticleSizeOverLifetime);
                    GUILayout.EndHorizontal();
                    
                    // 第三行：速度相关模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("LimitVelocityOverLifetime模块", ref settings.checkParticleLimitVelocityOverLifetime);
                    DrawParticleModuleCondition("InheritVelocity模块", ref settings.checkParticleInheritVelocity);
                    DrawParticleModuleCondition("ForceOverLifetime模块", ref settings.checkParticleForceOverLifetime);
                    GUILayout.EndHorizontal();
                    
                    // 第四行：速度影响模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("ColorBySpeed模块", ref settings.checkParticleColorBySpeed);
                    DrawParticleModuleCondition("SizeBySpeed模块", ref settings.checkParticleSizeBySpeed);
                    DrawParticleModuleCondition("RotationOverLifetime模块", ref settings.checkParticleRotationOverLifetime);
                    GUILayout.EndHorizontal();
                    
                    // 第五行：旋转和外部力模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("RotationBySpeed模块", ref settings.checkParticleRotationBySpeed);
                    DrawParticleModuleCondition("ExternalForces模块", ref settings.checkParticleExternalForces);
                    DrawParticleModuleCondition("Collision模块", ref settings.checkParticleCollision);
                    GUILayout.EndHorizontal();
                    
                    // 第六行：碰撞和触发模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("Trigger模块", ref settings.checkParticleTrigger);
                    DrawParticleModuleCondition("SubEmitters模块", ref settings.checkParticleSubEmitters);
                    DrawParticleModuleCondition("TextureSheetAnimation模块", ref settings.checkParticleTextureSheetAnimation);
                    GUILayout.EndHorizontal();
                    
                    // 第七行：视觉效果模块
                    GUILayout.BeginHorizontal();
                    DrawParticleModuleCondition("Lights模块", ref settings.checkParticleLights);
                    DrawParticleModuleCondition("Trails模块", ref settings.checkParticleTrails);
                    DrawParticleModuleCondition("CustomData模块", ref settings.checkParticleCustomData);
                    GUILayout.EndHorizontal();
                    
                    GUILayout.EndVertical();
                    
                    // 颜色设置（所有模块使用同一个颜色）
                    EditorGUILayout.Space();
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20); // 缩进
                    GUILayout.Label("所有模块标记色:", GUILayout.Width(100));
                    
                    // 更新所有粒子模块的颜色为同一个颜色
                    Color newColor = EditorGUILayout.ColorField("", settings.particleEmissionColor, GUILayout.Width(60));
                    settings.particleEmissionColor = newColor;
                    settings.particleShapeColor = newColor;
                    settings.particleNoiseColor = newColor;
                    settings.particleVelocityOverLifetimeColor = newColor;
                    settings.particleColorOverLifetimeColor = newColor;
                    settings.particleSizeOverLifetimeColor = newColor;
                    
                    GUILayout.EndHorizontal();
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // 保存按钮
            if (GUILayout.Button("保存设定", GUILayout.Height(30)))
            {
                settings.SaveSettings();
                Debug.Log("条件设置已保存");
            }

            EditorGUILayout.EndScrollView();
        }

        // 基础条件检测项
        private void DrawSimpleCondition(string label, ref bool check, ref Color color)
        {
            EditorGUILayout.BeginHorizontal();
            check = EditorGUILayout.ToggleLeft(label, check, GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            GUILayout.Label("标记色:", GUILayout.Width(40));
            color = EditorGUILayout.ColorField("", color, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }

        // 粒子模块条件（简化版本，不带颜色）
        private void DrawParticleModuleCondition(string label, ref bool check)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(180));
            check = EditorGUILayout.ToggleLeft(label, check, GUILayout.Width(160));
            GUILayout.EndHorizontal();
        }

        // 单数值阈值条件 - float
        private void DrawSingleThresholdCondition(string label, ref bool check, string thresholdLabel, 
            ref float threshold, ref Color color, bool isSecondLine = false)
        {
            EditorGUILayout.BeginHorizontal();

            if (isSecondLine && string.IsNullOrEmpty(label))
            {
                GUILayout.Space(154);
            }
            else
            {
                check = EditorGUILayout.ToggleLeft(label, check, GUILayout.Width(150));
            }

            GUILayout.Label(thresholdLabel + ":", GUILayout.Width(60));
            threshold = EditorGUILayout.FloatField(threshold, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.Label("标记色:", GUILayout.Width(40));
            color = EditorGUILayout.ColorField("", color, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }

        // 单数值阈值条件 - int
        private void DrawSingleThresholdCondition(string label, ref bool check, string thresholdLabel, 
            ref int threshold, ref Color color, bool isSecondLine = false)
        {
            EditorGUILayout.BeginHorizontal();

            if (isSecondLine && string.IsNullOrEmpty(label))
            {
                GUILayout.Space(154);
            }
            else
            {
                check = EditorGUILayout.ToggleLeft(label, check, GUILayout.Width(150));
            }

            GUILayout.Label(thresholdLabel + ":", GUILayout.Width(60));
            threshold = EditorGUILayout.IntField(threshold, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.Label("标记色:", GUILayout.Width(40));
            color = EditorGUILayout.ColorField("", color, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }
    }
}