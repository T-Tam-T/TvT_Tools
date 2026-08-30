using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class TxtChapterIndexCache
{
    private const int CacheVersion = 2;

    [Serializable]
    private class CachePayload
    {
        public int version;
        public string filePath;
        public long fileLength;
        public long fileLastWriteTicks;
        public BookData bookData;
    }

    public static bool TryLoad(string filePath, out BookData bookData)
    {
        bookData = null;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        string cacheFilePath = GetCacheFilePath(filePath);
        if (!File.Exists(cacheFilePath))
            return false;

        try
        {
            string json = File.ReadAllText(cacheFilePath, Encoding.UTF8);
            if (string.IsNullOrEmpty(json))
                return false;

            CachePayload payload = JsonUtility.FromJson<CachePayload>(json);
            if (payload == null || payload.bookData == null)
                return false;
            if (payload.version != CacheVersion)
                return false;

            FileInfo info = new FileInfo(filePath);
            if (payload.fileLength != info.Length)
                return false;
            if (payload.fileLastWriteTicks != info.LastWriteTimeUtc.Ticks)
                return false;

            bookData = payload.bookData;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string filePath, BookData bookData)
    {
        if (string.IsNullOrEmpty(filePath) || bookData == null || !File.Exists(filePath))
            return;

        try
        {
            Directory.CreateDirectory(GetCacheDirectory());
            FileInfo info = new FileInfo(filePath);
            var payload = new CachePayload
            {
                version = CacheVersion,
                filePath = filePath,
                fileLength = info.Length,
                fileLastWriteTicks = info.LastWriteTimeUtc.Ticks,
                bookData = bookData
            };

            string json = JsonUtility.ToJson(payload);
            File.WriteAllText(GetCacheFilePath(filePath), json, Encoding.UTF8);
        }
        catch
        {
            // 缓存失败不影响主流程
        }
    }

    private static string GetCacheDirectory()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "Library", "TXTvisualCache");
    }

    private static string GetCacheFilePath(string filePath)
    {
        string hash = ComputeSha1(filePath.ToLowerInvariant());
        return Path.Combine(GetCacheDirectory(), $"{hash}.json");
    }

    private static string ComputeSha1(string value)
    {
        using (var sha1 = SHA1.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = sha1.ComputeHash(bytes);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
