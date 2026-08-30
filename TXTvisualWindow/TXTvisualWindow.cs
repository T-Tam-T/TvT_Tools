using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class TXTvisualWindow : EditorWindow
{
    private static readonly Regex ChapterTitleRegex = new Regex(
        @"^\s*(第\s*[0-9零一二三四五六七八九十百千万两〇]+[章节回卷部篇]\s*.*|chapter\s*\d+.*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private BookData bookData = new BookData();
    private Vector2 chapterScrollPos;
    private Vector2 textScrollPos;

    private float fontSize = 12f;
    private Color fontColor = Color.white;
    private GUIStyle readingLabelStyle;   // 用于正文的 Label 样式（无高亮）
    private bool chapterListExpanded = false;

    // 缓存当前章节文本
    private string cachedChapterText = "";
    private int cachedChapterIndex = -1;

    // 目录分页
    private int chapterPageIndex = 0;
    private const int CHAPTERS_PER_PAGE = 50;
    private string chapterSearchKeyword = "";

    // 异步索引状态
    private Task<BookData> indexingTask;
    private volatile float indexingProgress;
    private bool preserveChapterAfterIndexing;
    private int previousChapterBeforeIndexing;

    [MenuItem("Tools/TvTTools/TXTvisual")]
    public static void ShowWindow()
    {
        GetWindow<TXTvisualWindow>("TXT 阅读器");
    }

    private void OnEnable()
    {
        // 初始化正文 Label 样式（无高亮、自动换行）
        readingLabelStyle = new GUIStyle(EditorStyles.label);
        readingLabelStyle.wordWrap = true;
        readingLabelStyle.richText = false;   // 纯文本，避免性能开销
        readingLabelStyle.fontSize = (int)fontSize;
        readingLabelStyle.normal.textColor = fontColor;
        // 不设置 hover/active 颜色，避免任何高亮效果

        LoadReadingProgress();
    }

    private void OnDisable()
    {
        SaveReadingProgress();
    }

    private void OnGUI()
    {
        PollIndexingTask();
        HandleFontScaling();
        DrawFileSelection();
        DrawIndexingStatus();

        if (!string.IsNullOrEmpty(bookData.filePath) && bookData.chapters.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            DrawChapterList();
            DrawReadingArea();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void HandleFontScaling()
    {
        Event e = Event.current;
        if (e.type == EventType.ScrollWheel && e.control)
        {
            fontSize += e.delta.y * 0.1f;
            fontSize = Mathf.Clamp(fontSize, 8f, 30f);
            if (readingLabelStyle != null)
                readingLabelStyle.fontSize = (int)fontSize;
            Repaint();
            e.Use();
        }
    }

    private void DrawFileSelection()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("当前文件:", bookData.filePath, GUILayout.MinWidth(200));
        GUI.enabled = indexingTask == null;
        if (GUILayout.Button("选择小说文件", EditorStyles.toolbarButton, GUILayout.Width(100)))
        {
            string path = EditorUtility.OpenFilePanel("选择TXT小说", Application.dataPath, "txt");
            if (!string.IsNullOrEmpty(path))
                LoadBook(path);
        }
        GUI.enabled = indexingTask == null && !string.IsNullOrEmpty(bookData.filePath);
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshCurrentBook();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawIndexingStatus()
    {
        if (indexingTask == null)
            return;

        Rect rect = EditorGUILayout.GetControlRect(false, 18f);
        EditorGUI.ProgressBar(rect, indexingProgress, $"正在构建目录索引... {(indexingProgress * 100f):0}%");
        Repaint();
    }

    private void DrawChapterList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(150));
        chapterListExpanded = EditorGUILayout.Foldout(chapterListExpanded, "目录", true, EditorStyles.foldout);
        if (chapterListExpanded)
        {
            EditorGUI.BeginDisabledGroup(indexingTask != null);
            chapterSearchKeyword = EditorGUILayout.TextField(chapterSearchKeyword);
            EditorGUI.EndDisabledGroup();

            List<int> visibleChapterIndices = GetVisibleChapterIndices();
            int totalChapters = visibleChapterIndices.Count;
            int totalPages = Mathf.CeilToInt((float)totalChapters / CHAPTERS_PER_PAGE);
            if (totalPages == 0) totalPages = 1;
            if (chapterPageIndex >= totalPages) chapterPageIndex = totalPages - 1;
            if (chapterPageIndex < 0) chapterPageIndex = 0;

            // 翻页控件
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = chapterPageIndex > 0;
            if (GUILayout.Button("◀", GUILayout.Width(25)))
            {
                chapterPageIndex--;
            }
            GUI.enabled = chapterPageIndex < totalPages - 1;
            if (GUILayout.Button("▶", GUILayout.Width(25)))
            {
                chapterPageIndex++;
            }
            GUI.enabled = true;
            EditorGUILayout.LabelField($"第 {chapterPageIndex+1}/{totalPages} 页", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            int startIdx = chapterPageIndex * CHAPTERS_PER_PAGE;
            int endIdx = Mathf.Min(startIdx + CHAPTERS_PER_PAGE, totalChapters);

            chapterScrollPos = EditorGUILayout.BeginScrollView(chapterScrollPos, GUILayout.ExpandHeight(true));
            for (int i = startIdx; i < endIdx; i++)
            {
                int chapterIndex = visibleChapterIndices[i];
                string title = GetChapterDisplayTitle(chapterIndex);

                GUI.enabled = (chapterIndex != bookData.currentChapter && indexingTask == null);
                if (GUILayout.Button(title, EditorStyles.label, GUILayout.Width(140)))
                {
                    int targetPage = i / CHAPTERS_PER_PAGE;
                    if (targetPage != chapterPageIndex)
                        chapterPageIndex = targetPage;
                    bookData.currentChapter = chapterIndex;
                    textScrollPos = Vector2.zero;
                    cachedChapterIndex = -1;
                    Repaint();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Label("（点击展开目录）", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawReadingArea()
    {
        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("字体颜色", GUILayout.Width(50));
        Color newColor = EditorGUILayout.ColorField(fontColor, GUILayout.Width(180));
        if (newColor != fontColor)
        {
            fontColor = newColor;
            if (readingLabelStyle != null)
                readingLabelStyle.normal.textColor = fontColor;
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        if (bookData.chapters.Count > bookData.currentChapter)
        {
            string title = GetChapterDisplayTitle(bookData.currentChapter);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Height(20));
        }

        textScrollPos = EditorGUILayout.BeginScrollView(textScrollPos, GUILayout.ExpandHeight(true));
        string fullText = GetChapterFullText();
        // 使用 Label 代替 TextArea，彻底消除鼠标高亮
        EditorGUILayout.LabelField(fullText, readingLabelStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = (bookData.currentChapter > 0);
        if (GUILayout.Button("上一章", GUILayout.Width(80))) GoToPreviousChapter();
        GUI.enabled = (bookData.currentChapter < bookData.chapters.Count - 1);
        if (GUILayout.Button("下一章", GUILayout.Width(80))) GoToNextChapter();
        GUI.enabled = true;
        EditorGUILayout.LabelField($"第 {bookData.currentChapter + 1} 章 / 共 {bookData.chapters.Count} 章", GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private string GetChapterFullText()
    {
        if (cachedChapterIndex == bookData.currentChapter && !string.IsNullOrEmpty(cachedChapterText))
            return cachedChapterText;

        if (bookData.chapters.Count == 0) return "";
        var chapter = bookData.chapters[bookData.currentChapter];
        if (chapter.contentEndOffset <= chapter.contentStartOffset)
            return "（本章无内容）";

        cachedChapterText = TxtRangeReader.ReadTextRange(
            bookData.filePath,
            chapter.contentStartOffset,
            chapter.contentEndOffset,
            bookData.GetEncoding());

        if (string.IsNullOrWhiteSpace(cachedChapterText))
            cachedChapterText = "（本章无内容）";

        cachedChapterIndex = bookData.currentChapter;

        return cachedChapterText;
    }

    private void GoToPreviousChapter()
    {
        if (bookData.currentChapter > 0)
        {
            bookData.currentChapter--;
            textScrollPos = Vector2.zero;
            cachedChapterIndex = -1;
            chapterPageIndex = bookData.currentChapter / CHAPTERS_PER_PAGE;
            Repaint();
        }
    }

    private void GoToNextChapter()
    {
        if (bookData.currentChapter < bookData.chapters.Count - 1)
        {
            bookData.currentChapter++;
            textScrollPos = Vector2.zero;
            cachedChapterIndex = -1;
            chapterPageIndex = bookData.currentChapter / CHAPTERS_PER_PAGE;
            Repaint();
        }
    }

    private void RefreshCurrentBook()
    {
        if (string.IsNullOrEmpty(bookData.filePath) || !File.Exists(bookData.filePath))
            return;
        int previousChapter = bookData.currentChapter;
        LoadBook(bookData.filePath, preserveChapter: true, previousChapter: previousChapter);
    }

    private void LoadBook(string filePath, bool preserveChapter = false, int previousChapter = 0)
    {
        if (!File.Exists(filePath)) return;

        if (TxtChapterIndexCache.TryLoad(filePath, out BookData cachedBook))
        {
            ApplyLoadedBook(cachedBook, preserveChapter, previousChapter);
            return;
        }

        StartIndexingAsync(filePath, preserveChapter, previousChapter);
    }

    private bool IsChapterTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        string trimmed = line.Trim().TrimStart('\uFEFF');

        // 标题通常较短，过长文本多为正文，避免误判造成章节错乱。
        if (trimmed.Length > 60)
            return false;

        return ChapterTitleRegex.IsMatch(trimmed);
    }

    private void SaveReadingProgress()
    {
        TxtReaderPrefs.Save(bookData.filePath, bookData.currentChapter, fontSize, fontColor);
    }

    private void LoadReadingProgress()
    {
        fontSize = TxtReaderPrefs.LoadFontSize(12f);
        fontColor = TxtReaderPrefs.LoadFontColor(Color.white);
        if (readingLabelStyle != null)
        {
            readingLabelStyle.fontSize = (int)fontSize;
            readingLabelStyle.normal.textColor = fontColor;
        }

        string path = TxtReaderPrefs.LoadFilePath();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            LoadBook(path);
            int chapter = TxtReaderPrefs.LoadChapter(0);
            if (chapter < bookData.chapters.Count)
                bookData.currentChapter = chapter;
            chapterPageIndex = bookData.currentChapter / CHAPTERS_PER_PAGE;
        }
    }

    private void StartIndexingAsync(string filePath, bool preserveChapter, int previousChapter)
    {
        if (indexingTask != null)
            return;

        preserveChapterAfterIndexing = preserveChapter;
        previousChapterBeforeIndexing = previousChapter;
        indexingProgress = 0f;
        cachedChapterText = "";
        cachedChapterIndex = -1;

        indexingTask = Task.Run(() =>
        {
            return TxtChapterIndexer.BuildIndex(
                filePath,
                ChapterTitleRegex,
                IsChapterTitle,
                progress => indexingProgress = progress);
        });
    }

    private void PollIndexingTask()
    {
        if (indexingTask == null || !indexingTask.IsCompleted)
            return;

        if (indexingTask.IsFaulted)
        {
            Debug.LogError($"TXT 索引构建失败: {indexingTask.Exception}");
            indexingTask = null;
            indexingProgress = 0f;
            return;
        }

        BookData indexedBook = indexingTask.Result;
        indexingTask = null;
        indexingProgress = 1f;

        if (indexedBook != null)
        {
            TxtChapterIndexCache.Save(indexedBook.filePath, indexedBook);
            ApplyLoadedBook(indexedBook, preserveChapterAfterIndexing, previousChapterBeforeIndexing);
        }
    }

    private void ApplyLoadedBook(BookData loadedBook, bool preserveChapter, int previousChapter)
    {
        if (loadedBook == null)
            return;

        bookData = loadedBook;

        if (preserveChapter && previousChapter < bookData.chapters.Count)
            bookData.currentChapter = previousChapter;
        else
            bookData.currentChapter = 0;

        chapterPageIndex = bookData.currentChapter / CHAPTERS_PER_PAGE;
        textScrollPos = Vector2.zero;
        chapterSearchKeyword = "";
        cachedChapterIndex = -1;
        cachedChapterText = "";
        Repaint();
    }

    private List<int> GetVisibleChapterIndices()
    {
        var indices = new List<int>(bookData.chapters.Count);
        string keyword = (chapterSearchKeyword ?? "").Trim();
        bool hasKeyword = !string.IsNullOrEmpty(keyword);
        bool isNumericKeyword = int.TryParse(keyword, out int keywordNumber);
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i < bookData.chapters.Count; i++)
        {
            if (!hasKeyword)
            {
                indices.Add(i);
                continue;
            }

            var chapter = bookData.chapters[i];
            string title = chapter.title ?? "";
            string displayTitle = chapter.displayTitle ?? "";
            bool numberMatched = isNumericKeyword && chapter.chapterNumber == keywordNumber;
            bool textMatched = title.IndexOf(keyword, comparison) >= 0 ||
                               displayTitle.IndexOf(keyword, comparison) >= 0;
            if (numberMatched || textMatched)
                indices.Add(i);
        }

        return indices;
    }

    private string GetChapterDisplayTitle(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= bookData.chapters.Count)
            return $"第 {chapterIndex + 1} 章";

        string displayTitle = bookData.chapters[chapterIndex].displayTitle;
        if (!string.IsNullOrEmpty(displayTitle))
            return displayTitle;

        string rawTitle = bookData.chapters[chapterIndex].title;
        if (!string.IsNullOrEmpty(rawTitle))
            return rawTitle;

        return $"第 {chapterIndex + 1} 章";
    }
}