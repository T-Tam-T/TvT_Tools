using System;
using System.Collections.Generic;
using System.Text;

[Serializable]
public class BookData
{
    public List<Chapter> chapters = new List<Chapter>();
    public int currentChapter = 0;
    public string filePath = "";
    public long fileLength = 0;
    public int encodingCodePage = Encoding.UTF8.CodePage;

    public Encoding GetEncoding()
    {
        try
        {
            return Encoding.GetEncoding(encodingCodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    [Serializable]
    public class Chapter
    {
        public string title;
        public string displayTitle;
        public int chapterNumber = -1;
        public long contentStartOffset;
        public long contentEndOffset;
    }
}
