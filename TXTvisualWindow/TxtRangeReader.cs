using System.Collections.Generic;
using System.IO;
using System.Text;

public static class TxtRangeReader
{
    public static Encoding DetectEncoding(string filePath)
    {
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] bom = new byte[4];
            int read = stream.Read(bom, 0, bom.Length);

            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                return Encoding.Unicode;
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            return new UTF8Encoding(false);
        }
    }

    public static long GetBomLength(string filePath)
    {
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] bom = new byte[4];
            int read = stream.Read(bom, 0, bom.Length);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return 3;
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return 2;
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return 2;
            return 0;
        }
    }

    public static string ReadLineWithOffset(FileStream stream, Encoding encoding, out long nextLineOffset)
    {
        if (stream.Position >= stream.Length)
        {
            nextLineOffset = stream.Position;
            return null;
        }

        List<byte> lineBytes = new List<byte>(128);
        bool isUtf16Le = encoding.CodePage == Encoding.Unicode.CodePage;
        bool isUtf16Be = encoding.CodePage == Encoding.BigEndianUnicode.CodePage;

        if (isUtf16Le || isUtf16Be)
        {
            while (stream.Position < stream.Length)
            {
                int first = stream.ReadByte();
                if (first == -1) break;

                int second = stream.ReadByte();
                if (second == -1)
                {
                    lineBytes.Add((byte)first);
                    break;
                }

                byte b1 = (byte)first;
                byte b2 = (byte)second;

                bool isNewLine = (isUtf16Le && b1 == 0x0A && b2 == 0x00)
                                 || (isUtf16Be && b1 == 0x00 && b2 == 0x0A);
                if (isNewLine)
                    break;

                lineBytes.Add(b1);
                lineBytes.Add(b2);
            }
        }
        else
        {
            while (stream.Position < stream.Length)
            {
                int value = stream.ReadByte();
                if (value == -1) break;

                byte b = (byte)value;
                if (b == (byte)'\n')
                    break;

                lineBytes.Add(b);
            }
        }

        nextLineOffset = stream.Position;
        string line = encoding.GetString(lineBytes.ToArray());
        return line.TrimEnd('\r');
    }

    public static string ReadTextRange(string path, long startOffset, long endOffset, Encoding encoding)
    {
        if (endOffset <= startOffset) return string.Empty;

        long length = endOffset - startOffset;
        if (length > int.MaxValue)
            length = int.MaxValue;

        byte[] buffer = new byte[(int)length];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(startOffset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            if (totalRead < buffer.Length)
            {
                byte[] resized = new byte[totalRead];
                System.Buffer.BlockCopy(buffer, 0, resized, 0, totalRead);
                buffer = resized;
            }
        }

        string content = encoding.GetString(buffer);
        return content.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
