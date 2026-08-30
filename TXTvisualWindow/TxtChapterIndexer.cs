using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class TxtChapterIndexer
{
    private static readonly Regex ChineseChapterRegex = new Regex(
        @"^\s*第\s*([0-9零一二三四五六七八九十百千万两〇]+)\s*([章节回卷部篇])\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishChapterRegex = new Regex(
        @"^\s*chapter\s*([0-9]+)\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BookData BuildIndex(
        string filePath,
        Regex chapterRegex,
        Func<string, bool> chapterTitleMatcher,
        Action<float> onProgress = null)
    {
        if (!File.Exists(filePath))
            return null;

        Encoding encoding = TxtRangeReader.DetectEncoding(filePath);
        long bomLength = TxtRangeReader.GetBomLength(filePath);

        var data = new BookData
        {
            filePath = filePath,
            fileLength = new FileInfo(filePath).Length,
            encodingCodePage = encoding.CodePage
        };

        string currentTitle = null;
        long currentContentStartOffset = 0;
        long prefaceStartOffset = bomLength;
        bool hasPreface = false;

        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long totalLength = stream.Length > 0 ? stream.Length : 1;
            while (stream.Position < stream.Length)
            {
                long lineStartOffset = stream.Position;
                string line = TxtRangeReader.ReadLineWithOffset(stream, encoding, out long nextLineOffset);
                if (line == null) break;

                bool isChapterTitle = chapterTitleMatcher != null
                    ? chapterTitleMatcher(line)
                    : (chapterRegex != null && chapterRegex.IsMatch(line));

                if (isChapterTitle)
                {
                    if (currentTitle != null)
                    {
                        var normalized = NormalizeChapterTitle(currentTitle);
                        data.chapters.Add(new BookData.Chapter
                        {
                            title = currentTitle,
                            displayTitle = normalized.displayTitle,
                            chapterNumber = normalized.chapterNumber,
                            contentStartOffset = currentContentStartOffset,
                            contentEndOffset = lineStartOffset
                        });
                    }
                    else if (hasPreface && lineStartOffset > prefaceStartOffset)
                    {
                        data.chapters.Add(new BookData.Chapter
                        {
                            title = "前言",
                            contentStartOffset = prefaceStartOffset,
                            contentEndOffset = lineStartOffset
                        });
                    }

                    currentTitle = line.Trim().TrimStart('\uFEFF');
                    currentContentStartOffset = nextLineOffset;
                }
                else
                {
                    hasPreface = true;
                }

                if (onProgress != null)
                {
                    float progress = (float)stream.Position / totalLength;
                    if (progress < 0f) progress = 0f;
                    if (progress > 1f) progress = 1f;
                    onProgress(progress);
                }
            }

            if (currentTitle != null)
            {
                var normalized = NormalizeChapterTitle(currentTitle);
                data.chapters.Add(new BookData.Chapter
                {
                    title = currentTitle,
                    displayTitle = normalized.displayTitle,
                    chapterNumber = normalized.chapterNumber,
                    contentStartOffset = currentContentStartOffset,
                    contentEndOffset = stream.Length
                });
            }
            else
            {
                data.chapters.Add(new BookData.Chapter
                {
                    title = Path.GetFileName(filePath),
                    displayTitle = Path.GetFileName(filePath),
                    chapterNumber = -1,
                    contentStartOffset = bomLength,
                    contentEndOffset = stream.Length
                });
            }
        }

        onProgress?.Invoke(1f);
        return data;
    }

    private static (string displayTitle, int chapterNumber) NormalizeChapterTitle(string originalTitle)
    {
        if (string.IsNullOrEmpty(originalTitle))
            return (originalTitle, -1);

        string title = originalTitle.Trim().TrimStart('\uFEFF');

        Match cnMatch = ChineseChapterRegex.Match(title);
        if (cnMatch.Success)
        {
            int number = ParseChapterNumber(cnMatch.Groups[1].Value);
            if (number > 0)
            {
                string suffix = cnMatch.Groups[2].Value;
                string tail = cnMatch.Groups[3].Value;
                string display = $"第{number}{suffix}";
                if (!string.IsNullOrWhiteSpace(tail))
                    display += " " + tail.Trim();
                return (display, number);
            }
        }

        Match enMatch = EnglishChapterRegex.Match(title);
        if (enMatch.Success && int.TryParse(enMatch.Groups[1].Value, out int englishNumber) && englishNumber > 0)
        {
            string tail = enMatch.Groups[2].Value;
            string display = $"Chapter {englishNumber}";
            if (!string.IsNullOrWhiteSpace(tail))
                display += " " + tail.Trim();
            return (display, englishNumber);
        }

        return (title, -1);
    }

    private static int ParseChapterNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return -1;

        raw = raw.Trim();
        if (int.TryParse(raw, out int numeric))
            return numeric;

        return ParseChineseNumber(raw);
    }

    private static int ParseChineseNumber(string value)
    {
        int total = 0;
        int section = 0;
        int number = 0;

        for (int i = 0; i < value.Length; i++)
        {
            int digit = ChineseDigitToInt(value[i]);
            if (digit >= 0)
            {
                number = digit;
                continue;
            }

            int unit = ChineseUnitToInt(value[i]);
            if (unit == 10000)
            {
                section = (section + (number == 0 ? 1 : number)) * 10000;
                total += section;
                section = 0;
                number = 0;
            }
            else if (unit > 0)
            {
                section += (number == 0 ? 1 : number) * unit;
                number = 0;
            }
            else
            {
                return -1;
            }
        }

        return total + section + number;
    }

    private static int ChineseDigitToInt(char c)
    {
        switch (c)
        {
            case '零':
            case '〇':
                return 0;
            case '一':
                return 1;
            case '二':
            case '两':
                return 2;
            case '三':
                return 3;
            case '四':
                return 4;
            case '五':
                return 5;
            case '六':
                return 6;
            case '七':
                return 7;
            case '八':
                return 8;
            case '九':
                return 9;
            default:
                return -1;
        }
    }

    private static int ChineseUnitToInt(char c)
    {
        switch (c)
        {
            case '十':
                return 10;
            case '百':
                return 100;
            case '千':
                return 1000;
            case '万':
                return 10000;
            default:
                return -1;
        }
    }
}
