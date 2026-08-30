using UnityEngine;
using UnityEditor;
using ResourceManager.Modules;
using System.Collections.Generic;

/// <summary>
/// 粒子颜色编辑弹出窗口 — 模仿 Unity 粒子 Inspector 的调色方式，
/// 支持切换颜色模式（常量/渐变/双色随机/双渐变随机/随机色），
/// 编辑对应色值字段，并将拷贝的色值粘贴到任意字段。
/// </summary>
public class ParticleColorEditorPopup : EditorWindow
{
    // ─── 静态入口 ───

    private static ParticleColorEditorPopup currentWindow;

    public static void ShowWindow(ParticleSystem ps, ColorChangerModule.ColorModuleType moduleType, string moduleName)
    {
        if (currentWindow != null)
        {
            currentWindow.Close();
        }

        var window = CreateInstance<ParticleColorEditorPopup>();
        window.titleContent = new GUIContent($"改色 — {ps.name}/{moduleName}");
        window.targetPS = ps;
        window.moduleType = moduleType;
        window.moduleName = moduleName;
        window.minSize = new Vector2(320, 200);

        // 初始化时读取当前值
        window.ReadCurrentValues();

        window.ShowUtility();
        currentWindow = window;
    }

    // ─── 状态 ───

    private ParticleSystem targetPS;
    private ColorChangerModule.ColorModuleType moduleType;
    private string moduleName;

    // 当前编辑的色值
    private ParticleSystemGradientMode currentMode;
    private Color constantColor;
    private Color colorMin;
    private Color colorMax;
    private Gradient gradientValue;
    private Gradient gradientMin;
    private Gradient gradientMax;

    // 粘贴目标选择
    private int pasteTargetIndex = 0; // 0=ConstantColor/主色, 1=ColorMin, 2=ColorMax
    private string[] pasteTargetNames = { "常量色 / 主色", "色值 Min", "色值 Max" };

    // 渐变粘贴目标
    private int gradientPasteTarget = 0; // 0=Gradient主, 1=GradientMin, 2=GradientMax
    private string[] gradientPasteTargetNames = { "渐变主", "渐变 Min", "渐变 Max" };

    // ─── 生命周期 ───

    private void OnDestroy()
    {
        currentWindow = null;
    }

    // ─── 主绘制 ───

    private void OnGUI()
    {
        if (targetPS == null || targetPS.Equals(null))
        {
            EditorGUILayout.HelpBox("粒子系统已失效", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginVertical();

        // ── 标题 ──
        GUILayout.Label($"粒子: {targetPS.name}", EditorStyles.boldLabel);
        GUILayout.Label($"模块: {moduleName}", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(5);

        // ── 颜色模式选择（模仿 Unity 粒子 Inspector） ──
        DrawColorModeSelector();

        EditorGUILayout.Space(5);

        // ── 根据模式绘制对应字段 ──
        DrawColorFieldsByMode();

        EditorGUILayout.Space(8);

        // ── 粘贴区域 ──
        DrawPasteSection();

        EditorGUILayout.Space(8);

        // ── 操作按钮 ──
        DrawActionButtons();

        EditorGUILayout.EndVertical();
    }

    // ═══════════════════════════════════════
    //  读取当前值
    // ═══════════════════════════════════════

    private void ReadCurrentValues()
    {
        if (targetPS == null) return;

        try
        {
            ParticleSystem.MinMaxGradient minMaxGradient = GetMinMaxGradient();

            currentMode = minMaxGradient.mode;

            switch (currentMode)
            {
                case ParticleSystemGradientMode.Color:
                    constantColor = minMaxGradient.color;
                    break;
                case ParticleSystemGradientMode.Gradient:
                    gradientValue = new Gradient();
                    CopyGradient(minMaxGradient.gradient, gradientValue);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    colorMin = minMaxGradient.colorMin;
                    colorMax = minMaxGradient.colorMax;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    gradientMin = new Gradient();
                    gradientMax = new Gradient();
                    CopyGradient(minMaxGradient.gradientMin, gradientMin);
                    CopyGradient(minMaxGradient.gradientMax, gradientMax);
                    break;
                case ParticleSystemGradientMode.RandomColor:
                    gradientValue = new Gradient();
                    CopyGradient(minMaxGradient.gradient, gradientValue);
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"读取粒子色值失败: {e.Message}");
        }
    }

    /// <summary>根据模块类型获取对应的 MinMaxGradient</summary>
    private ParticleSystem.MinMaxGradient GetMinMaxGradient()
    {
        switch (moduleType)
        {
            case ColorChangerModule.ColorModuleType.MainStartColor:
                return targetPS.main.startColor;
            case ColorChangerModule.ColorModuleType.ColorOverLifetime:
                return targetPS.colorOverLifetime.color;
            case ColorChangerModule.ColorModuleType.ColorBySpeed:
                return targetPS.colorBySpeed.color;
            default:
                return targetPS.main.startColor;
        }
    }

    /// <summary>深拷贝 Gradient</summary>
    private void CopyGradient(Gradient src, Gradient dst)
    {
        if (src == null) return;
        dst.colorKeys = src.colorKeys;
        dst.alphaKeys = src.alphaKeys;
        dst.mode = src.mode;
    }

    // ═══════════════════════════════════════
    //  绘制：颜色模式选择器
    // ═══════════════════════════════════════

    private void DrawColorModeSelector()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("颜色方式:", GUILayout.Width(70));

        string[] modeOptions = {
            "常量色 (Color)",
            "渐变 (Gradient)",
            "双色随机 (Two Colors)",
            "双渐变随机 (Two Gradients)",
            "随机色 (Random Color)"
        };

        int selectedIndex = (int)currentMode;
        selectedIndex = EditorGUILayout.Popup(selectedIndex, modeOptions, GUILayout.Width(200));

        ParticleSystemGradientMode newMode = (ParticleSystemGradientMode)selectedIndex;

        if (newMode != currentMode)
        {
            // 模式切换时做合理转换
            HandleModeChange(currentMode, newMode);
            currentMode = newMode;
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>模式切换时的值转换逻辑</summary>
    private void HandleModeChange(ParticleSystemGradientMode oldMode, ParticleSystemGradientMode newMode)
    {
        // 尽量保留旧值到新模式的合理字段
        Color preserveColor = GetRepresentativeColorFromOldMode(oldMode);

        switch (newMode)
        {
            case ParticleSystemGradientMode.Color:
                constantColor = preserveColor;
                break;
            case ParticleSystemGradientMode.Gradient:
                if (gradientValue == null) gradientValue = new Gradient();
                gradientValue.colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(preserveColor, 0f),
                    new GradientColorKey(preserveColor, 1f)
                };
                gradientValue.alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                break;
            case ParticleSystemGradientMode.TwoColors:
                colorMin = preserveColor;
                colorMax = preserveColor;
                break;
            case ParticleSystemGradientMode.TwoGradients:
                if (gradientMin == null) gradientMin = new Gradient();
                if (gradientMax == null) gradientMax = new Gradient();
                gradientMin.colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(preserveColor, 0f),
                    new GradientColorKey(preserveColor, 1f)
                };
                gradientMin.alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                gradientMax.colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(preserveColor, 0f),
                    new GradientColorKey(preserveColor, 1f)
                };
                gradientMax.alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                break;
            case ParticleSystemGradientMode.RandomColor:
                if (gradientValue == null) gradientValue = new Gradient();
                gradientValue.colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(preserveColor, 0f),
                    new GradientColorKey(Color.white, 1f)
                };
                gradientValue.alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                break;
        }
    }

    private Color GetRepresentativeColorFromOldMode(ParticleSystemGradientMode oldMode)
    {
        switch (oldMode)
        {
            case ParticleSystemGradientMode.Color:
                return constantColor;
            case ParticleSystemGradientMode.TwoColors:
                return Color.Lerp(colorMin, colorMax, 0.5f);
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:
                return gradientValue != null ? gradientValue.Evaluate(0f) : Color.white;
            case ParticleSystemGradientMode.TwoGradients:
                Color cMin = gradientMin != null ? gradientMin.Evaluate(0f) : Color.white;
                Color cMax = gradientMax != null ? gradientMax.Evaluate(0f) : Color.white;
                return Color.Lerp(cMin, cMax, 0.5f);
            default:
                return Color.white;
        }
    }

    // ═══════════════════════════════════════
    //  绘制：按模式显示对应编辑字段
    // ═══════════════════════════════════════

    private void DrawColorFieldsByMode()
    {
        EditorGUILayout.BeginVertical("box");

        switch (currentMode)
        {
            case ParticleSystemGradientMode.Color:
                DrawConstantColorField();
                break;

            case ParticleSystemGradientMode.Gradient:
                DrawGradientField(gradientValue, "渐变");
                break;

            case ParticleSystemGradientMode.TwoColors:
                DrawTwoColorsField();
                break;

            case ParticleSystemGradientMode.TwoGradients:
                DrawTwoGradientsField();
                break;

            case ParticleSystemGradientMode.RandomColor:
                DrawGradientField(gradientValue, "渐变 (随机取色)");
                break;
        }

        EditorGUILayout.EndVertical();
    }

    // ── 常量色编辑 ──
    private void DrawConstantColorField()
    {
        GUILayout.Label("常量色:", EditorStyles.miniBoldLabel);
        constantColor = EditorGUILayout.ColorField("色值", constantColor);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("十六进制:", GUILayout.Width(60));
        GUILayout.Label(ColorChangerModule.ColorToHex(constantColor), EditorStyles.miniLabel);
        if (GUILayout.Button("复制", GUILayout.Width(45)))
        {
            ColorChangerModule.CopyColorToClipboard(constantColor);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ── 双色随机编辑 ──
    private void DrawTwoColorsField()
    {
        GUILayout.Label("双色随机:", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("色值 Min:", GUILayout.Width(70));
        colorMin = EditorGUILayout.ColorField(colorMin, GUILayout.Width(80));

        // 粘贴按钮
        if (GUILayout.Button("粘贴→", GUILayout.Width(50)))
        {
            PasteColorToField(ref colorMin);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("色值 Max:", GUILayout.Width(70));
        colorMax = EditorGUILayout.ColorField(colorMax, GUILayout.Width(80));

        // 粘贴按钮
        if (GUILayout.Button("粘贴→", GUILayout.Width(50)))
        {
            PasteColorToField(ref colorMax);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // 预览条
        DrawTwoColorsPreviewBar(colorMin, colorMax);
    }

    // ── 渐变编辑 ──
    private void DrawGradientField(Gradient gradient, string label)
    {
        GUILayout.Label(label + ":", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();
        Gradient newGradient = EditorGUILayout.GradientField(gradient, GUILayout.Height(30));
        if (EditorGUI.EndChangeCheck())
        {
            // 根据目标替换对应渐变
            switch (currentMode)
            {
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    gradientValue = newGradient;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    if (gradientPasteTarget == 0 || gradientPasteTarget == 1)
                        gradientMin = newGradient;
                    else
                        gradientMax = newGradient;
                    break;
            }
        }

        // 渐变首色拷贝
        if (gradient != null && gradient.colorKeys.Length > 0)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("首色:", GUILayout.Width(40));
            Color startColor = gradient.Evaluate(0f);
            Rect rect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
            EditorGUI.DrawRect(rect, startColor);
            GUILayout.Label(ColorChangerModule.ColorToHex(startColor), EditorStyles.miniLabel);
            if (GUILayout.Button("复制首色", GUILayout.Width(60)))
            {
                ColorChangerModule.CopyColorToClipboard(startColor);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ── 双渐变编辑 ──
    private void DrawTwoGradientsField()
    {
        GUILayout.Label("双渐变随机:", EditorStyles.miniBoldLabel);

        // 渐变选择
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("当前编辑:", GUILayout.Width(70));
        gradientPasteTarget = EditorGUILayout.Popup(gradientPasteTarget, gradientPasteTargetNames, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();

        Gradient currentGradient = gradientPasteTarget <= 1 ? gradientMin : gradientMax;
        if (currentGradient == null) currentGradient = new Gradient();

        EditorGUI.BeginChangeCheck();
        Gradient newGradient = EditorGUILayout.GradientField(currentGradient, GUILayout.Height(30));
        if (EditorGUI.EndChangeCheck())
        {
            if (gradientPasteTarget <= 1)
                gradientMin = newGradient;
            else
                gradientMax = newGradient;
        }

        EditorGUILayout.Space(3);

        // 渐变 Min 预览
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("渐变 Min:", GUILayout.Width(70));
        if (gradientMin != null)
        {
            Rect barRect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(120), GUILayout.Height(18));
            DrawMiniGradientBar(barRect, gradientMin);
        }
        EditorGUILayout.EndHorizontal();

        // 渐变 Max 预览
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("渐变 Max:", GUILayout.Width(70));
        if (gradientMax != null)
        {
            Rect barRect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(120), GUILayout.Height(18));
            DrawMiniGradientBar(barRect, gradientMax);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ═══════════════════════════════════════
    //  绘制：粘贴区域
    // ═══════════════════════════════════════

    private void DrawPasteSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("粘贴色值", EditorStyles.miniBoldLabel);

        // 显示剪贴板当前值
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("剪贴板:", GUILayout.Width(60));

        if (!string.IsNullOrEmpty(ColorChangerModule.ClipboardColorHex))
        {
            Color clipboardColor;
            if (ColorUtility.TryParseHtmlString(ColorChangerModule.ClipboardColorHex, out clipboardColor))
            {
                Rect rect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
                EditorGUI.DrawRect(rect, clipboardColor);
                GUILayout.Label(ColorChangerModule.ClipboardColorHex, EditorStyles.miniLabel, GUILayout.Width(70));

                // 快速粘贴：根据当前模式
                switch (currentMode)
                {
                    case ParticleSystemGradientMode.Color:
                        if (GUILayout.Button("粘贴到常量色", GUILayout.Width(90)))
                        {
                            PasteColorToField(ref constantColor);
                        }
                        break;
                    case ParticleSystemGradientMode.TwoColors:
                        pasteTargetIndex = EditorGUILayout.Popup(pasteTargetIndex, pasteTargetNames, GUILayout.Width(100));
                        if (GUILayout.Button("粘贴", GUILayout.Width(50)))
                        {
                            if (pasteTargetIndex == 0 || pasteTargetIndex == 1)
                                PasteColorToField(ref colorMin);
                            else
                                PasteColorToField(ref colorMax);
                        }
                        break;
                    case ParticleSystemGradientMode.Gradient:
                    case ParticleSystemGradientMode.RandomColor:
                        if (GUILayout.Button("设为渐变首色", GUILayout.Width(90)))
                        {
                            if (gradientValue != null)
                            {
                                PasteColorToGradientStart(gradientValue);
                            }
                        }
                        break;
                    case ParticleSystemGradientMode.TwoGradients:
                        if (GUILayout.Button("设 Min 渐变首色", GUILayout.Width(95)))
                        {
                            if (gradientMin != null) PasteColorToGradientStart(gradientMin);
                        }
                        if (GUILayout.Button("设 Max 渐变首色", GUILayout.Width(95)))
                        {
                            if (gradientMax != null) PasteColorToGradientStart(gradientMax);
                        }
                        break;
                }
            }
            else
            {
                GUILayout.Label("格式无效", EditorStyles.miniLabel);
            }
        }
        else
        {
            GUILayout.Label("(空)", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    // ═══════════════════════════════════════
    //  绘制：操作按钮
    // ═══════════════════════════════════════

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("应用修改", GUILayout.Height(28), GUILayout.Width(120)))
        {
            ApplyChanges();
        }

        if (GUILayout.Button("重新读取", GUILayout.Height(28), GUILayout.Width(90)))
        {
            ReadCurrentValues();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("关闭", GUILayout.Height(28), GUILayout.Width(60)))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ═══════════════════════════════════════
    //  应用修改到粒子系统
    // ═══════════════════════════════════════

    private void ApplyChanges()
    {
        if (targetPS == null || targetPS.Equals(null))
        {
            Debug.LogWarning("粒子系统已失效，无法应用");
            return;
        }

        Undo.RecordObject(targetPS, "Change Particle Color");

        try
        {
            ParticleSystem.MinMaxGradient newMinMax = CreateMinMaxGradientFromCurrentValues();

            switch (moduleType)
            {
                case ColorChangerModule.ColorModuleType.MainStartColor:
                    var main = targetPS.main;
                    main.startColor = newMinMax;
                    break;
                case ColorChangerModule.ColorModuleType.ColorOverLifetime:
                    var col = targetPS.colorOverLifetime;
                    col.color = newMinMax;
                    break;
                case ColorChangerModule.ColorModuleType.ColorBySpeed:
                    var cbs = targetPS.colorBySpeed;
                    cbs.color = newMinMax;
                    break;
            }

            Debug.Log($"已应用颜色修改到 {targetPS.name} / {moduleName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"应用颜色修改失败: {e.Message}");
        }
    }

    /// <summary>从当前编辑值构建 MinMaxGradient</summary>
    private ParticleSystem.MinMaxGradient CreateMinMaxGradientFromCurrentValues()
    {
        ParticleSystem.MinMaxGradient minMax = new ParticleSystem.MinMaxGradient();

        switch (currentMode)
        {
            case ParticleSystemGradientMode.Color:
                minMax = new ParticleSystem.MinMaxGradient(constantColor);
                break;
            case ParticleSystemGradientMode.Gradient:
                minMax = new ParticleSystem.MinMaxGradient(gradientValue);
                break;
            case ParticleSystemGradientMode.TwoColors:
                minMax = new ParticleSystem.MinMaxGradient(colorMin, colorMax);
                break;
            case ParticleSystemGradientMode.TwoGradients:
                minMax = new ParticleSystem.MinMaxGradient(gradientMin, gradientMax);
                break;
            case ParticleSystemGradientMode.RandomColor:
                minMax.mode = ParticleSystemGradientMode.RandomColor;
                minMax.gradient = gradientValue;
                break;
        }

        return minMax;
    }

    // ═══════════════════════════════════════
    //  粘贴色值辅助
    // ═══════════════════════════════════════

    /// <summary>从剪贴板粘贴色值到指定 Color 字段</summary>
    private void PasteColorToField(ref Color targetField)
    {
        if (ColorChangerModule.PasteColorFromClipboard(out Color pastedColor))
        {
            targetField = pastedColor;
            Debug.Log($"已粘贴色值 {ColorChangerModule.ColorToHex(pastedColor)}");
        }
        else
        {
            Debug.LogWarning("剪贴板中无有效色值");
        }
    }

    /// <summary>将剪贴板色值设为渐变的首色 key</summary>
    private void PasteColorToGradientStart(Gradient gradient)
    {
        if (ColorChangerModule.PasteColorFromClipboard(out Color pastedColor))
        {
            if (gradient.colorKeys.Length > 0)
            {
                var keys = gradient.colorKeys;
                keys[0].color = pastedColor;
                gradient.colorKeys = keys;
            }
            else
            {
                gradient.colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(pastedColor, 0f),
                    new GradientColorKey(pastedColor, 1f)
                };
                gradient.alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
            }
            Debug.Log($"已粘贴渐变首色 {ColorChangerModule.ColorToHex(pastedColor)}");
        }
        else
        {
            Debug.LogWarning("剪贴板中无有效色值");
        }
    }

    // ═══════════════════════════════════════
    //  辅助绘制
    // ═══════════════════════════════════════

    /// <summary>绘制双色预览条（Min 到 Max 的渐变）</summary>
    private void DrawTwoColorsPreviewBar(Color min, Color max)
    {
        Rect barRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true), GUILayout.Height(18));
        int steps = (int)barRect.width;
        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / steps;
            Color c = Color.Lerp(min, max, t);
            Rect pixelRect = new Rect(barRect.x + i, barRect.y, 1.5f, barRect.height);
            EditorGUI.DrawRect(pixelRect, c);
        }
        // 边框
        DrawRectBorder(barRect);
    }

    /// <summary>绘制迷你渐变条</summary>
    private void DrawMiniGradientBar(Rect rect, Gradient gradient)
    {
        if (gradient == null) return;
        int steps = (int)rect.width;
        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / steps;
            Color c = gradient.Evaluate(t);
            Rect pixelRect = new Rect(rect.x + i, rect.y, 1.5f, rect.height);
            EditorGUI.DrawRect(pixelRect, c);
        }
        DrawRectBorder(rect);
    }

    /// <summary>绘制矩形边框</summary>
    private void DrawRectBorder(Rect rect)
    {
        Color borderColor = new Color(0.2f, 0.2f, 0.2f);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), borderColor);
    }
}
