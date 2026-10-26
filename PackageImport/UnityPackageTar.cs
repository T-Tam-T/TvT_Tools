using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TvT.EditorTools
{
    /// <summary>
    /// 极简 .unitypackage 读取器，不依赖 UnityEditor，可单独测试。
    ///
    /// .unitypackage 就是 gzip 压缩的 tar。包内每个资源占一个以 GUID 命名的目录：
    ///   {GUID}/asset        资源本体（文件夹条目没有这个文件）
    ///   {GUID}/asset.meta   资源对应的 .meta（里面写着同一个 GUID；文件夹还带 folderAsset: yes）
    ///   {GUID}/pathname     导入后应放到的工程路径，如 "Assets/PandaShaderV2.3/Assert/ATV/Resources/VAT_mesh.asset"
    ///   {GUID}/preview.png  预览图（可无）
    /// </summary>
    internal static class UnityPackageTar
    {
        internal const int HeaderSize = 512;
        internal const string KindAsset = "asset";
        internal const string KindMeta = "asset.meta";
        internal const string KindPathname = "pathname";

        internal sealed class Item
        {
            public string Guid;
            public string Path;
            public bool IsFolder;
            public long Size;
            public string MetaGuid;
            public bool MetaIsFolder;
            public bool HasAsset;
        }

        /// <summary>单遍扫描包：读出每个 GUID 的目标路径 / 是否为文件夹 / 大小 / meta 信息。</summary>
        internal static List<Item> ReadItems(string packagePath)
        {
            var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

            using (FileStream fs = File.OpenRead(packagePath))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                byte[] header = new byte[HeaderSize];
                string longName = null;

                while (ReadExact(gz, header))
                {
                    if (IsZeroBlock(header))
                        break;

                    string name = ReadField(header, 0, 100);
                    long size = ParseNumeric(header, 124, 12);
                    byte type = header[156];
                    string prefix = ReadField(header, 345, 155);
                    if (prefix.Length > 0 && name.Length > 0)
                        name = prefix + "/" + name;

                    // GNU 长文件名扩展
                    if (type == (byte)'L')
                    {
                        longName = ReadText(gz, size);
                        SkipPadding(gz, size);
                        continue;
                    }
                    if (type == (byte)'K' || type == (byte)'x' || type == (byte)'g')
                    {
                        SkipEntry(gz, size);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(longName))
                    {
                        name = longName;
                        longName = null;
                    }

                    // 目录条目：跳过
                    if (type == (byte)'5' || name.EndsWith("/", StringComparison.Ordinal))
                    {
                        SkipEntry(gz, size);
                        continue;
                    }

                    // 只关心 {32位GUID}/kind 形式
                    if (name.Length < 34 || name[32] != '/')
                    {
                        SkipEntry(gz, size);
                        continue;
                    }
                    string guid = name.Substring(0, 32);
                    string kind = name.Substring(33);
                    if (!IsHex(guid) || kind.IndexOf('/') >= 0)
                    {
                        SkipEntry(gz, size);
                        continue;
                    }

                    Item item = GetItem(map, guid);

                    if (kind == KindAsset)
                    {
                        item.HasAsset = true;
                        item.Size = size;
                        SkipEntry(gz, size);
                    }
                    else if (kind == KindMeta)
                    {
                        string text = ReadText(gz, size);
                        SkipPadding(gz, size);
                        item.MetaGuid = ExtractGuid(text);
                        item.MetaIsFolder = text.IndexOf("folderAsset: yes", StringComparison.Ordinal) >= 0;
                    }
                    else if (kind == KindPathname)
                    {
                        string text = ReadText(gz, size);
                        SkipPadding(gz, size);
                        if (!string.IsNullOrEmpty(text))
                            item.Path = text.Replace('\\', '/');
                    }
                    else
                    {
                        SkipEntry(gz, size);
                    }
                }
            }

            var list = new List<Item>();
            foreach (Item item in map.Values)
            {
                if (string.IsNullOrEmpty(item.Path))
                    continue;
                // 纯文件夹条目：GUID 目录里没有 asset 文件
                item.IsFolder = !item.HasAsset || item.MetaIsFolder;
                list.Add(item);
            }
            list.Sort(delegate(Item a, Item b) { return string.CompareOrdinal(a.Path, b.Path); });
            return list;
        }

        /// <summary>第二遍扫描包，把选中 GUID 的 asset + asset.meta 按原始路径写到 destRoot 下。返回写出的资源个数。</summary>
        internal static int Extract(string packagePath, string destRoot, IList<Item> items, ICollection<string> selectedGuids)
        {
            if (items == null || selectedGuids == null || selectedGuids.Count == 0)
                return 0;

            var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            foreach (Item it in items)
                map[it.Guid] = it;

            string root = Path.GetFullPath(destRoot);
            Directory.CreateDirectory(root);
            int written = 0;

            using (FileStream fs = File.OpenRead(packagePath))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                byte[] header = new byte[HeaderSize];
                string longName = null;

                while (ReadExact(gz, header))
                {
                    if (IsZeroBlock(header))
                        break;

                    string name = ReadField(header, 0, 100);
                    long size = ParseNumeric(header, 124, 12);
                    byte type = header[156];
                    string prefix = ReadField(header, 345, 155);
                    if (prefix.Length > 0 && name.Length > 0)
                        name = prefix + "/" + name;

                    if (type == (byte)'L')
                    {
                        longName = ReadText(gz, size);
                        SkipPadding(gz, size);
                        continue;
                    }
                    if (type == (byte)'K' || type == (byte)'x' || type == (byte)'g')
                    {
                        SkipEntry(gz, size);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(longName))
                    {
                        name = longName;
                        longName = null;
                    }

                    if (type == (byte)'5' || name.EndsWith("/", StringComparison.Ordinal) || name.Length < 34 || name[32] != '/')
                    {
                        SkipEntry(gz, size);
                        continue;
                    }

                    string guid = name.Substring(0, 32);
                    string kind = name.Substring(33);
                    Item item;
                    if (!IsHex(guid) || kind.IndexOf('/') >= 0 || !map.TryGetValue(guid, out item) || !selectedGuids.Contains(guid))
                    {
                        SkipEntry(gz, size);
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.Path))
                    {
                        SkipEntry(gz, size);
                        continue;
                    }

                    string dest = Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar));
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    if (kind == KindAsset)
                    {
                        using (FileStream outFs = File.Create(dest))
                            CopyTo(gz, outFs, size);
                        SkipPadding(gz, size);
                        written++;
                    }
                    else if (kind == KindMeta)
                    {
                        string metaPath = dest + ".meta";
                        using (FileStream outFs = File.Create(metaPath))
                            CopyTo(gz, outFs, size);
                        SkipPadding(gz, size);
                    }
                    else
                    {
                        SkipEntry(gz, size);
                    }
                }
            }

            // 空的文件夹条目（没有子文件也要建出来）
            foreach (Item it in items)
            {
                if (!it.IsFolder || !selectedGuids.Contains(it.Guid) || string.IsNullOrEmpty(it.Path))
                    continue;
                Directory.CreateDirectory(Path.Combine(root, it.Path.Replace('/', Path.DirectorySeparatorChar)));
            }

            return written;
        }

        // ---------------------------------------------------------------- helpers

        static Item GetItem(Dictionary<string, Item> map, string guid)
        {
            Item item;
            if (!map.TryGetValue(guid, out item))
            {
                item = new Item();
                item.Guid = guid;
                map[guid] = item;
            }
            return item;
        }

        static bool ReadExact(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    break;
                read += n;
            }
            return read == buffer.Length;
        }

        static bool IsZeroBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
                if (block[i] != 0)
                    return false;
            return true;
        }

        static string ReadField(byte[] header, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && header[end] != 0)
                end++;
            return Encoding.UTF8.GetString(header, offset, end - offset);
        }

        static long ParseNumeric(byte[] header, int offset, int length)
        {
            if ((header[offset] & 0x80) != 0)
            {
                // GNU base-256 大数字（超长文件）
                long value = header[offset] & 0x7f;
                for (int i = 1; i < length; i++)
                    value = (value << 8) | header[offset + i];
                return value;
            }

            long oct = 0;
            bool any = false;
            for (int i = 0; i < length; i++)
            {
                byte b = header[offset + i];
                if (b == 0)
                    break;
                if (b == (byte)' ')
                    continue;
                if (b < (byte)'0' || b > (byte)'7')
                    continue;
                oct = (oct * 8) + (b - (byte)'0');
                any = true;
            }
            return any ? oct : 0;
        }

        /// <summary>跳过该项数据，并吃掉 tar 的 512 字节对齐填充。</summary>
        static void SkipEntry(Stream s, long size)
        {
            Skip(s, size);
            SkipPadding(s, size);
        }

        static void SkipPadding(Stream s, long size)
        {
            if (size <= 0)
                return;
            int pad = (int)((HeaderSize - (size % HeaderSize)) % HeaderSize);
            Skip(s, pad);
        }

        static string ReadText(Stream s, long size)
        {
            if (size <= 0)
                return string.Empty;
            if (size > 4L * 1024 * 1024)
            {
                Skip(s, size);
                return string.Empty;
            }
            byte[] buffer = new byte[size];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    throw new EndOfStreamException("unitypackage 内容不完整");
                read += n;
            }
            return Encoding.UTF8.GetString(buffer).Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
        }

        static void Skip(Stream s, long count)
        {
            if (count <= 0)
                return;
            byte[] buffer = new byte[81920];
            long left = count;
            while (left > 0)
            {
                int n = s.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0)
                    throw new EndOfStreamException("unitypackage 内容不完整");
                left -= n;
            }
        }

        static void CopyTo(Stream src, Stream dst, long count)
        {
            if (count <= 0)
                return;
            byte[] buffer = new byte[81920];
            long left = count;
            while (left > 0)
            {
                int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0)
                    throw new EndOfStreamException("unitypackage 内容不完整");
                dst.Write(buffer, 0, n);
                left -= n;
            }
        }

        static bool IsHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (!IsHexChar(s[i]))
                    return false;
            return true;
        }

        static string ExtractGuid(string metaText)
        {
            if (string.IsNullOrEmpty(metaText))
                return string.Empty;
            int idx = metaText.IndexOf("guid:", StringComparison.Ordinal);
            if (idx < 0)
                return string.Empty;
            idx += 5;
            while (idx < metaText.Length && (metaText[idx] == ' ' || metaText[idx] == '\t'))
                idx++;
            int end = idx;
            while (end < metaText.Length && IsHexChar(metaText[end]))
                end++;
            return end > idx ? metaText.Substring(idx, end - idx) : string.Empty;
        }

        static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
