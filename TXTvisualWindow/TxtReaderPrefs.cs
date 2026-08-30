using UnityEditor;
using UnityEngine;
using System.Globalization;

public static class TxtReaderPrefs
{
    private const string PREFS_FILE_PATH = "TXTvisual_FilePath";
    private const string PREFS_CHAPTER = "TXTvisual_Chapter";
    private const string PREFS_FONT_SIZE = "TXTvisual_FontSize";
    private const string PREFS_FONT_COLOR = "TXTvisual_FontColor";

    public static void Save(string filePath, int chapter, float fontSize, Color fontColor)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        EditorPrefs.SetString(PREFS_FILE_PATH, filePath);
        EditorPrefs.SetInt(PREFS_CHAPTER, chapter);
        EditorPrefs.SetFloat(PREFS_FONT_SIZE, fontSize);
        string colorRaw = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}",
            fontColor.r,
            fontColor.g,
            fontColor.b,
            fontColor.a);
        EditorPrefs.SetString(PREFS_FONT_COLOR, colorRaw);
    }

    public static string LoadFilePath()
    {
        return EditorPrefs.GetString(PREFS_FILE_PATH, "");
    }

    public static int LoadChapter(int defaultValue)
    {
        return EditorPrefs.GetInt(PREFS_CHAPTER, defaultValue);
    }

    public static float LoadFontSize(float defaultValue)
    {
        return EditorPrefs.GetFloat(PREFS_FONT_SIZE, defaultValue);
    }

    public static Color LoadFontColor(Color defaultColor)
    {
        string raw = EditorPrefs.GetString(PREFS_FONT_COLOR, "");
        if (string.IsNullOrEmpty(raw))
            return defaultColor;

        string[] parts = raw.Split('|');
        if (parts.Length == 4 &&
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
        {
            return new Color(r, g, b, a);
        }

        // 兼容旧版本的 HTML 色值存档。
        if (ColorUtility.TryParseHtmlString("#" + raw, out Color color))
            return color;

        return defaultColor;
    }
}
