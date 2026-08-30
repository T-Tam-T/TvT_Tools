using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    /// <summary>
    /// 粒子改色模块 — 读取每个粒子已启用的颜色模块与颜色方式，
    /// 在界面顶部提供目标颜色选择器及自动计算的邻近色/对比色/互补色，
    /// 每个色值可拷贝；点击粒子色彩弹出编辑窗口，可粘贴已拷贝的色值。
    /// </summary>
    public class ColorChangerModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        // ─── 数据结构 ───

        /// <summary>颜色模块类型标识</summary>
        public enum ColorModuleType
        {
            MainStartColor,
            ColorOverLifetime,
            ColorBySpeed
        }

        /// <summary>单个颜色模块的色值信息</summary>
        public class ColorModuleEntry
        {
            public ColorModuleType ModuleType;
            public string ModuleName;          // 显示名称，如 "Start Color"
            public bool IsEnabled;
            public ParticleSystemGradientMode ColorMode;

            // 色值数据（按 mode 取不同字段）
            public Color ConstantColor;
            public Color ColorMin;
            public Color ColorMax;
            public Gradient GradientValue;
            public Gradient GradientMin;
            public Gradient GradientMax;

            /// <summary>获取该模块当前的主要代表色（用于预览）</summary>
            public Color GetRepresentativeColor()
            {
                switch (ColorMode)
                {
                    case ParticleSystemGradientMode.Color:
                        return ConstantColor;
                    case ParticleSystemGradientMode.TwoColors:
                        return Color.Lerp(ColorMin, ColorMax, 0.5f);
                    case ParticleSystemGradientMode.Gradient:
                        return GradientValue != null && GradientValue.colorKeys.Length > 0
                            ? GradientValue.Evaluate(0f) : Color.white;
                    case ParticleSystemGradientMode.TwoGradients:
                        Color cMin = GradientMin != null && GradientMin.colorKeys.Length > 0
                            ? GradientMin.Evaluate(0f) : Color.white;
                        Color cMax = GradientMax != null && GradientMax.colorKeys.Length > 0
                            ? GradientMax.Evaluate(0f) : Color.white;
                        return Color.Lerp(cMin, cMax, 0.5f);
                    case ParticleSystemGradientMode.RandomColor:
                        return GradientValue != null && GradientValue.colorKeys.Length > 0
                            ? GradientValue.Evaluate(0.5f) : Color.white;
                    default:
                        return ConstantColor;
                }
            }
        }

        /// <summary>一个粒子系统的全部色彩信息</summary>
        public class ParticleColorInfo
        {
            public ParticleSystem ParticleSystem;
            public string ParticleName;
            public string ObjectName;         // 所属分析对象名
            public List<ColorModuleEntry> ColorModules = new List<ColorModuleEntry>();
        }

        // ─── UI 状态 ───

        private Dictionary<int, ParticleColorInfo> particleInfos = new Dictionary<int, ParticleColorInfo>();
        private Dictionary<int, bool> particleFoldouts = new Dictionary<int, bool>();
        private Dictionary<string, bool> objectFoldouts = new Dictionary<string, bool>();
        private bool needsScan = true;
        private Vector2 scrollPos;

        // 目标颜色与派生色
        private Color targetColor = new Color(1f, 0.4f, 0.2f, 1f);

        // 全局剪贴板色值（十六进制字符串）
        public static string ClipboardColorHex = "";

        // ─── IMultiObjectModule 实现 ───

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("粒子改色", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先分析对象", MessageType.Info);
                return;
            }

            // 扫描粒子色彩数据
            if (needsScan)
            {
                ScanParticleColors(session);
                needsScan = false;
            }

            // ── 顶部：目标颜色区域 ──
            DrawTargetColorSection();

            EditorGUILayout.Space(5);

            // ── 下方：粒子色彩列表 ──
            DrawParticleColorList(session);
        }

        public void Clear()
        {
            particleInfos.Clear();
            particleFoldouts.Clear();
            objectFoldouts.Clear();
            needsScan = true;
            scrollPos = Vector2.zero;
        }

        // ═══════════════════════════════════════
        //  扫描：从分析会话中提取粒子色值
        // ═══════════════════════════════════════

        private void ScanParticleColors(AnalysisSession session)
        {
            particleInfos.Clear();

            foreach (var kvp in session.Analyzers)
            {
                var analyzer = kvp.Value;
                var targetObject = kvp.Key;
                string objectName = targetObject.name;

                var particles = analyzer.ResourceUsage.Keys
                    .Where(r => r is ParticleSystem)
                    .Cast<ParticleSystem>()
                    .Distinct()
                    .ToList();

                foreach (var ps in particles)
                {
                    if (ps == null || ps.Equals(null)) continue;

                    var info = ExtractParticleColorInfo(ps, objectName);
                    if (info != null && info.ColorModules.Count > 0)
                    {
                        particleInfos[ps.GetInstanceID()] = info;
                    }
                }
            }
        }

        private ParticleColorInfo ExtractParticleColorInfo(ParticleSystem ps, string objectName)
        {
            var info = new ParticleColorInfo
            {
                ParticleSystem = ps,
                ParticleName = ps.name,
                ObjectName = objectName
            };

            try
            {
                // 1. Main.startColor（始终存在）
                var main = ps.main;
                var startColor = main.startColor;
                var mainEntry = CreateColorModuleEntry(
                    ColorModuleType.MainStartColor,
                    "Start Color",
                    true,  // main 模块始终启用
                    startColor
                );
                info.ColorModules.Add(mainEntry);

                // 2. Color Over Lifetime
                var colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    var colEntry = CreateColorModuleEntry(
                        ColorModuleType.ColorOverLifetime,
                        "Color Over Lifetime",
                        true,
                        colorOverLifetime.color
                    );
                    info.ColorModules.Add(colEntry);
                }

                // 3. Color By Speed
                var colorBySpeed = ps.colorBySpeed;
                if (colorBySpeed.enabled)
                {
                    var cbsEntry = CreateColorModuleEntry(
                        ColorModuleType.ColorBySpeed,
                        "Color By Speed",
                        true,
                        colorBySpeed.color
                    );
                    info.ColorModules.Add(cbsEntry);
                }
            }
            catch (System.Exception)
            {
                // 粒子系统可能已失效
                return null;
            }

            return info;
        }

        private ColorModuleEntry CreateColorModuleEntry(
            ColorModuleType moduleType, string moduleName,
            bool isEnabled, ParticleSystem.MinMaxGradient minMaxGradient)
        {
            var entry = new ColorModuleEntry
            {
                ModuleType = moduleType,
                ModuleName = moduleName,
                IsEnabled = isEnabled,
                ColorMode = minMaxGradient.mode
            };

            switch (minMaxGradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    entry.ConstantColor = minMaxGradient.color;
                    break;
                case ParticleSystemGradientMode.Gradient:
                    entry.GradientValue = new Gradient();
                    CopyGradient(minMaxGradient.gradient, entry.GradientValue);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    entry.ColorMin = minMaxGradient.colorMin;
                    entry.ColorMax = minMaxGradient.colorMax;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    entry.GradientMin = new Gradient();
                    entry.GradientMax = new Gradient();
                    CopyGradient(minMaxGradient.gradientMin, entry.GradientMin);
                    CopyGradient(minMaxGradient.gradientMax, entry.GradientMax);
                    break;
                case ParticleSystemGradientMode.RandomColor:
                    entry.GradientValue = new Gradient();
                    CopyGradient(minMaxGradient.gradient, entry.GradientValue);
                    break;
            }

            return entry;
        }

        /// <summary>深拷贝 Gradient（因为 Gradient 是引用类型）</summary>
        private void CopyGradient(Gradient src, Gradient dst)
        {
            if (src == null) return;
            dst.colorKeys = src.colorKeys;
            dst.alphaKeys = src.alphaKeys;
            dst.mode = src.mode;
        }

        // ═══════════════════════════════════════
        //  绘制：目标颜色 + 派生色
        // ═══════════════════════════════════════

        private void DrawTargetColorSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("目标颜色", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // 颜色选择器
            EditorGUI.BeginChangeCheck();
            targetColor = EditorGUILayout.ColorField("选择颜色", targetColor, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
            {
                // 目标颜色变更时无需额外操作，派生色会在绘制时自动计算
            }

            // 当前色值的十六进制 + 拷贝按钮
            string hex = ColorToHex(targetColor);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(80));
            if (GUILayout.Button("复制", GUILayout.Width(45)))
            {
                CopyColorToClipboard(targetColor);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            // ── 派生色展示 ──
            DrawDerivedColors();

            EditorGUILayout.EndVertical();
        }

        private void DrawDerivedColors()
        {
            // 计算派生色
            float h, s, l;
            RGBToHSL(targetColor, out h, out s, out l);

            // 邻近色（色相 ±30°）
            Color similar1 = HSLToRGB(h - 30f / 360f, s, l);
            Color similar2 = targetColor;
            Color similar3 = HSLToRGB(h + 30f / 360f, s, l);

            // 对比色（色相 ±120°）
            Color contrast1 = HSLToRGB(h + 120f / 360f, s, l);
            Color contrast2 = HSLToRGB(h - 120f / 360f, s, l);

            // 互补色（色相 ±180°）
            Color complementary = HSLToRGB(h + 180f / 360f, s, l);

            // ── 邻近色行 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("邻近色:", GUILayout.Width(60));
            DrawColorSwatchWithCopy(similar1, 40, 20);
            DrawColorSwatchWithCopy(similar2, 40, 20);
            DrawColorSwatchWithCopy(similar3, 40, 20);
            EditorGUILayout.EndHorizontal();

            // ── 对比色行 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("对比色:", GUILayout.Width(60));
            DrawColorSwatchWithCopy(contrast1, 40, 20);
            DrawColorSwatchWithCopy(contrast2, 40, 20);
            EditorGUILayout.EndHorizontal();

            // ── 互补色行 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("互补色:", GUILayout.Width(60));
            DrawColorSwatchWithCopy(complementary, 40, 20);
            EditorGUILayout.EndHorizontal();

            // ── 剪贴板状态 ──
            if (!string.IsNullOrEmpty(ClipboardColorHex))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("剪贴板:", GUILayout.Width(60));

                Color clipboardColor;
                if (ColorUtility.TryParseHtmlString(ClipboardColorHex, out clipboardColor))
                {
                    Rect swatchRect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
                    EditorGUI.DrawRect(swatchRect, clipboardColor);
                    GUILayout.Label(ClipboardColorHex, EditorStyles.miniLabel);
                }
                else
                {
                    GUILayout.Label(ClipboardColorHex, EditorStyles.miniLabel);
                }

                if (GUILayout.Button("清空", GUILayout.Width(45)))
                {
                    ClipboardColorHex = "";
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>绘制一个色块 + 十六进制文本 + 拷贝按钮</summary>
        private void DrawColorSwatchWithCopy(Color color, float swatchWidth, float swatchHeight)
        {
            // 色块
            Rect rect = GUILayoutUtility.GetRect(swatchHeight, swatchHeight,
                GUILayout.Width(swatchWidth), GUILayout.Height(swatchHeight));
            EditorGUI.DrawRect(rect, color);
            // 边框
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), Color.black);

            // 点击色块 → 拷贝
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                CopyColorToClipboard(color);
                Event.current.Use();
            }

            GUILayout.Space(2);

            // 十六进制文本
            string hex = ColorToHex(color);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(65));

            // 拷贝按钮
            if (GUILayout.Button("⧉", GUILayout.Width(20), GUILayout.Height(16)))
            {
                CopyColorToClipboard(color);
            }

            GUILayout.Space(4);
        }

        // ═══════════════════════════════════════
        //  绘制：粒子色彩列表
        // ═══════════════════════════════════════

        private void DrawParticleColorList(AnalysisSession session)
        {
            if (particleInfos.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到含色彩模块的粒子系统", MessageType.Info);
                return;
            }

            // 按所属对象分组
            var groupedByObject = particleInfos.Values
                .GroupBy(info => info.ObjectName)
                .OrderBy(g => g.Key);

            // 应用搜索过滤
            string filterStr = string.IsNullOrEmpty(SearchFilter) ? null : SearchFilter.ToLower();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            int totalMatched = 0;
            foreach (var group in groupedByObject)
            {
                string objectName = group.Key;
                var infos = group.ToList();

                // 搜索时过滤粒子
                if (filterStr != null)
                {
                    infos = infos.Where(i => i.ParticleName != null && i.ParticleName.ToLower().Contains(filterStr)).ToList();
                    if (infos.Count == 0) continue;
                    totalMatched += infos.Count;
                }

                EditorGUILayout.BeginVertical("box");

                if (!objectFoldouts.ContainsKey(objectName))
                    objectFoldouts[objectName] = true;

                // 搜索时自动展开
                if (filterStr != null)
                    objectFoldouts[objectName] = true;

                objectFoldouts[objectName] = EditorGUILayout.Foldout(
                    objectFoldouts[objectName],
                    $"{objectName} ({infos.Count}个粒子)", true);

                if (objectFoldouts[objectName])
                {
                    EditorGUI.indentLevel++;
                    for (int pi = 0; pi < infos.Count; pi++)
                    {
                        using (new UIHelper.ZebraScope(pi))
                        {
                            DrawParticleColorInfo(infos[pi]);
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }

            // 搜索时显示结果数量或未找到提示
            if (filterStr != null)
            {
                if (totalMatched == 0)
                    EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的粒子系统", MessageType.Info);
                else
                    EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {totalMatched} 个匹配粒子系统", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawParticleColorInfo(ParticleColorInfo info)
        {
            if (info.ParticleSystem == null || info.ParticleSystem.Equals(null))
            {
                EditorGUILayout.HelpBox("粒子系统已失效", MessageType.Warning);
                return;
            }

            int id = info.ParticleSystem.GetInstanceID();

            if (!particleFoldouts.ContainsKey(id))
                particleFoldouts[id] = false;

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                particleFoldouts[id] = true;

            EditorGUILayout.BeginVertical("helpbox");

            // ── 粒子标题行 ──
            GUILayout.BeginHorizontal();

            particleFoldouts[id] = EditorGUILayout.Foldout(
                particleFoldouts[id], info.ParticleName, true);

            GUILayout.FlexibleSpace();

            // 粒子对象字段（与材质模块同宽200）
            EditorGUILayout.ObjectField("", info.ParticleSystem, typeof(ParticleSystem), false,
                GUILayout.Width(200));

            GUILayout.EndHorizontal();

            // ── 展开后显示色彩模块 ──
            if (particleFoldouts[id])
            {
                EditorGUI.indentLevel++;
                for (int ci = 0; ci < info.ColorModules.Count; ci++)
                {
                    using (new UIHelper.ZebraScope(ci))
                    {
                        DrawColorModuleEntry(info.ParticleSystem, info.ColorModules[ci]);
                    }
                }

                // 「重新扫描」按钮（应对运行时修改）
                if (GUILayout.Button("刷新此粒子色值", GUILayout.Height(20)))
                {
                    RescanSingleParticle(info);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawColorModuleEntry(ParticleSystem ps, ColorModuleEntry entry)
        {
            EditorGUILayout.BeginVertical("box");

            // ── 模块标题行 + 色值预览合并为同一行 ──
            // 左侧：状态图标 + 模块名 + [常量色]标签
            // 右侧：色值预览（色块/渐变条 + hex + 拷贝）—— 与 [常量色] 对齐
            GUILayout.BeginHorizontal();

            // 启用状态图标
            Color stateColor = entry.IsEnabled ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
            Rect stateRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
            EditorGUI.DrawRect(stateRect, stateColor);

            GUILayout.Space(4);

            // 模块名称（不固定宽度，自适应）
            GUILayout.Label(entry.ModuleName, EditorStyles.miniBoldLabel);

            // 颜色方式标签（固定宽度，右侧对齐）
            string modeLabel = GetColorModeLabel(entry.ColorMode);
            GUILayout.Label($"[{modeLabel}]", EditorStyles.miniLabel, GUILayout.Width(100));

            // ── 色值预览（紧跟方式标签，同行右侧） ──
            switch (entry.ColorMode)
            {
                case ParticleSystemGradientMode.Color:
                    DrawConstantColorPreviewInline(ps, entry);
                    break;

                case ParticleSystemGradientMode.TwoColors:
                    DrawRandomTwoColorsPreviewInline(ps, entry);
                    break;

                case ParticleSystemGradientMode.Gradient:
                    DrawGradientPreviewInline(ps, entry, entry.GradientValue);
                    break;

                case ParticleSystemGradientMode.TwoGradients:
                    DrawGradientPreviewInline(ps, entry, entry.GradientMin, "Min");
                    DrawGradientPreviewInline(ps, entry, entry.GradientMax, "Max");
                    break;

                case ParticleSystemGradientMode.RandomColor:
                    DrawGradientPreviewInline(ps, entry, entry.GradientValue);
                    break;
            }

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── 各颜色方式的预览与交互 ──

        // ── Inline 版本：与标题行同行的色值预览 ──

        /// <summary>常量色预览（Inline）— 小色块 + 十六进制 + 拷贝，与文字同行对齐</summary>
        private void DrawConstantColorPreviewInline(ParticleSystem ps, ColorModuleEntry entry)
        {
            Color c = entry.ConstantColor;

            // 小色块（14px高，与文字行对齐）
            Rect rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            EditorGUI.DrawRect(rect, c);
            DrawSwatchBorder(rect);

            // 点击色块 → 弹出编辑
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                OpenColorEditor(ps, entry);
                Event.current.Use();
            }

            GUILayout.Space(2);

            // 十六进制
            string hex = ColorToHex(c);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(65));

            // 拷贝按钮
            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(c);
            }
        }

        /// <summary>双色预览（Inline）— 两个小色块，与文字同行对齐</summary>
        private void DrawRandomTwoColorsPreviewInline(ParticleSystem ps, ColorModuleEntry entry)
        {
            // Min 色
            DrawSmallColorSwatchInline(entry.ColorMin, () => OpenColorEditor(ps, entry));

            GUILayout.Label("→", GUILayout.Width(16));

            // Max 色
            DrawSmallColorSwatchInline(entry.ColorMax, () => OpenColorEditor(ps, entry));
        }

        /// <summary>小型色块 Inline（14px高，可点击弹出编辑 + 可拷贝）</summary>
        private void DrawSmallColorSwatchInline(Color c, System.Action onClick)
        {
            Rect rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            EditorGUI.DrawRect(rect, c);
            DrawSwatchBorder(rect);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                CopyColorToClipboard(c);
                Event.current.Use();
            }

            GUILayout.Space(2);

            string hex = ColorToHex(c);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(60));

            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(c);
            }
        }

        /// <summary>渐变预览（Inline）— 渐变条 + 十六进制 + 拷贝，与文字同行对齐</summary>
        private void DrawGradientPreviewInline(ParticleSystem ps, ColorModuleEntry entry, Gradient gradient, string label = "")
        {
            if (gradient == null) return;

            float barWidth = 80f;
            float barHeight = 14f;

            if (!string.IsNullOrEmpty(label))
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(30));
            }

            // 渐变条（14px高，与文字行对齐）
            Rect barRect = GUILayoutUtility.GetRect(barHeight, barHeight,
                GUILayout.Width((int)barWidth), GUILayout.Height((int)barHeight));

            DrawGradientBar(barRect, gradient);
            DrawSwatchBorder(barRect);

            // 点击 → 弹出编辑
            if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
            {
                OpenColorEditor(ps, entry);
                Event.current.Use();
            }

            GUILayout.Space(2);

            // 渐变首色十六进制 + 拷贝
            Color startColor = gradient.Evaluate(0f);
            string hex = ColorToHex(startColor);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(65));

            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(startColor);
            }
        }

        // ── 旧版预览方法（保留，供展开详情或其他场景使用） ──

        /// <summary>常量色预览 — 色块 + 十六进制 + 拷贝 + 点击编辑</summary>
        private void DrawConstantColorPreview(ParticleSystem ps, ColorModuleEntry entry)
        {
            Color c = entry.ConstantColor;

            // 色块
            Rect rect = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
            EditorGUI.DrawRect(rect, c);
            DrawSwatchBorder(rect);

            // 点击色块 → 弹出编辑
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                OpenColorEditor(ps, entry);
                Event.current.Use();
            }

            GUILayout.Space(4);

            // 十六进制
            string hex = ColorToHex(c);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(65));

            // 拷贝按钮
            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(c);
            }

            GUILayout.FlexibleSpace();
        }

        /// <summary>双色预览 — 两个色块</summary>
        private void DrawRandomTwoColorsPreview(ParticleSystem ps, ColorModuleEntry entry)
        {
            // Min 色
            DrawSmallColorSwatch(entry.ColorMin, () => OpenColorEditor(ps, entry));

            GUILayout.Label("→", GUILayout.Width(16));

            // Max 色
            DrawSmallColorSwatch(entry.ColorMax, () => OpenColorEditor(ps, entry));

            GUILayout.FlexibleSpace();
        }

        /// <summary>小型色块（可点击弹出编辑 + 可拷贝）</summary>
        private void DrawSmallColorSwatch(Color c, System.Action onClick)
        {
            Rect rect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUI.DrawRect(rect, c);
            DrawSwatchBorder(rect);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                CopyColorToClipboard(c);
                Event.current.Use();
            }

            GUILayout.Space(2);

            string hex = ColorToHex(c);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(60));

            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(c);
            }
        }

        /// <summary>渐变预览 — 绘制渐变条 + 点击弹出编辑</summary>
        private void DrawGradientPreview(ParticleSystem ps, ColorModuleEntry entry, Gradient gradient, string label = "")
        {
            if (gradient == null) return;

            float barWidth = 120f;
            float barHeight = 18f;

            if (!string.IsNullOrEmpty(label))
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(30));
            }

            // 渐变条
            Rect barRect = GUILayoutUtility.GetRect(barHeight, barHeight,
                GUILayout.Width((int)barWidth), GUILayout.Height((int)barHeight));

            // 逐像素绘制渐变
            DrawGradientBar(barRect, gradient);

            // 边框
            DrawSwatchBorder(barRect);

            // 点击 → 弹出编辑
            if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
            {
                OpenColorEditor(ps, entry);
                Event.current.Use();
            }

            GUILayout.Space(4);

            // 渐变首色十六进制 + 拷贝
            Color startColor = gradient.Evaluate(0f);
            string hex = ColorToHex(startColor);
            GUILayout.Label(hex, EditorStyles.miniLabel, GUILayout.Width(65));

            if (GUILayout.Button("⧉", GUILayout.Width(20)))
            {
                CopyColorToClipboard(startColor);
            }

            GUILayout.FlexibleSpace();
        }

        /// <summary>用采样法绘制渐变条</summary>
        private void DrawGradientBar(Rect rect, Gradient gradient)
        {
            if (gradient == null) return;

            int steps = (int)rect.width;
            float stepWidth = rect.width / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / steps;
                Color c = gradient.Evaluate(t);
                Rect pixelRect = new Rect(rect.x + i * stepWidth, rect.y, stepWidth + 0.5f, rect.height);
                EditorGUI.DrawRect(pixelRect, c);
            }
        }

        // ═══════════════════════════════════════
        //  弹出编辑窗口
        // ═══════════════════════════════════════

        private void OpenColorEditor(ParticleSystem ps, ColorModuleEntry entry)
        {
            ParticleColorEditorPopup.ShowWindow(ps, entry.ModuleType, entry.ModuleName);
        }

        // ═══════════════════════════════════════
        //  单粒子重新扫描
        // ═══════════════════════════════════════

        private void RescanSingleParticle(ParticleColorInfo info)
        {
            if (info.ParticleSystem == null || info.ParticleSystem.Equals(null)) return;

            var newInfo = ExtractParticleColorInfo(info.ParticleSystem, info.ObjectName);
            if (newInfo != null)
            {
                particleInfos[info.ParticleSystem.GetInstanceID()] = newInfo;
            }
        }

        // ═══════════════════════════════════════
        //  工具方法：色值操作
        // ═══════════════════════════════════════

        /// <summary>将 Color 转为 #RRGGBB 十六进制字符串</summary>
        public static string ColorToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        /// <summary>拷贝色值到剪贴板（系统剪贴板 + 全局变量）</summary>
        public static void CopyColorToClipboard(Color c)
        {
            string hex = ColorToHex(c);
            ClipboardColorHex = hex;
            GUIUtility.systemCopyBuffer = hex;
            Debug.Log($"已拷贝色值: {hex}");
        }

        /// <summary>从剪贴板粘贴色值</summary>
        public static bool PasteColorFromClipboard(out Color color)
        {
            string hex = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(hex)) hex = ClipboardColorHex;

            // 确保有 # 前缀
            if (!hex.StartsWith("#")) hex = "#" + hex;

            if (ColorUtility.TryParseHtmlString(hex, out color))
            {
                ClipboardColorHex = hex;
                return true;
            }

            color = Color.white;
            return false;
        }

        /// <summary>获取颜色方式标签</summary>
        private string GetColorModeLabel(ParticleSystemGradientMode mode)
        {
            switch (mode)
            {
                case ParticleSystemGradientMode.Color:
                    return "常量色";
                case ParticleSystemGradientMode.Gradient:
                    return "渐变";
                case ParticleSystemGradientMode.TwoColors:
                    return "双色随机";
                case ParticleSystemGradientMode.TwoGradients:
                    return "双渐变随机";
                case ParticleSystemGradientMode.RandomColor:
                    return "随机色";
                default:
                    return mode.ToString();
            }
        }

        /// <summary>绘制色块边框</summary>
        private void DrawSwatchBorder(Rect rect)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), new Color(0.2f, 0.2f, 0.2f));
        }

        // ═══════════════════════════════════════
        //  HSL 色彩理论计算
        // ═══════════════════════════════════════

        /// <summary>RGB → HSL（h 为 0~1, s 为 0~1, l 为 0~1）</summary>
        private void RGBToHSL(Color rgb, out float h, out float s, out float l)
        {
            float r = rgb.r, g = rgb.g, b = rgb.b;
            float max = Mathf.Max(r, g, b);
            float min = Mathf.Min(r, g, b);
            l = (max + min) / 2f;

            if (max == min)
            {
                h = s = 0f;
            }
            else
            {
                float d = max - min;
                s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

                if (max == r)
                    h = (g - b) / d + (g < b ? 6f : 0f);
                else if (max == g)
                    h = (b - r) / d + 2f;
                else
                    h = (r - g) / d + 4f;

                h /= 6f;
            }
        }

        /// <summary>HSL → RGB（h 为 0~1 循环, s 为 0~1, l 为 0~1）</summary>
        private Color HSLToRGB(float h, float s, float l)
        {
            // h 循环处理
            h = h % 1f;
            if (h < 0) h += 1f;

            if (s == 0f)
            {
                return new Color(l, l, l, 1f);
            }

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            float rk = HueToRGB(p, q, h + 1f / 3f);
            float gk = HueToRGB(p, q, h);
            float bk = HueToRGB(p, q, h - 1f / 3f);

            return new Color(rk, gk, bk, 1f);
        }

        private float HueToRGB(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
    }
}
