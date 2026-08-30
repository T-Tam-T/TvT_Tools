using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;

/// <summary>
/// Excel (.xlsx) 读取工具类。
/// 提供工作表枚举、共享字符串、普通列映射、高级拆分拼接映射等功能。
/// </summary>
public static class ExcelReader
{
    // ──────────────────────────────────────────────
    //  工作表枚举
    // ──────────────────────────────────────────────

    /// <summary>
    /// 从 ZipArchive 中读取所有工作表名称及其在压缩包内的路径。
    /// Key: 工作表显示名称   Value: 压缩包内路径（如 xl/worksheets/sheet1.xml）
    /// </summary>
    public static Dictionary<string, string> LoadSheetNamesAndPaths(ZipArchive archive)
    {
        var result = new Dictionary<string, string>();

        ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry == null) return result;

        XmlDocument workbookDoc = new XmlDocument();
        using (Stream stream = workbookEntry.Open())
            workbookDoc.Load(stream);

        XmlNamespaceManager nsManager = new XmlNamespaceManager(workbookDoc.NameTable);
        nsManager.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        nsManager.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        XmlNodeList sheetNodes = workbookDoc.SelectNodes("//x:workbook/x:sheets/x:sheet", nsManager);
        if (sheetNodes == null) return result;

        // 读取 rId → 路径 映射
        ZipArchiveEntry relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        var relIdToPath = new Dictionary<string, string>();
        if (relsEntry != null)
        {
            XmlDocument relsDoc = new XmlDocument();
            using (Stream relsStream = relsEntry.Open())
                relsDoc.Load(relsStream);
            XmlNamespaceManager relsNs = new XmlNamespaceManager(relsDoc.NameTable);
            relsNs.AddNamespace("rel", "http://schemas.openxmlformats.org/package/2006/relationships");
            XmlNodeList relNodes = relsDoc.SelectNodes("//rel:Relationship", relsNs);
            if (relNodes != null)
            {
                foreach (XmlNode relNode in relNodes)
                {
                    string id = relNode.Attributes?["Id"]?.Value;
                    string target = relNode.Attributes?["Target"]?.Value;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                        relIdToPath[id] = "xl/" + target.Replace("\\", "/");
                }
            }
        }

        foreach (XmlNode sheetNode in sheetNodes)
        {
            string name = sheetNode.Attributes?["name"]?.Value;
            string rId  = sheetNode.Attributes?["r:id"]?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            if (!string.IsNullOrEmpty(rId) && relIdToPath.TryGetValue(rId, out string path))
                result[name] = path;
            else
                result[name] = $"xl/worksheets/sheet{result.Count + 1}.xml";
        }

        return result;
    }

    /// <summary>
    /// 根据工作表名称从 ZipArchive 中加载对应的 XmlDocument。
    /// sheetNameToPath 由 LoadSheetNamesAndPaths 返回。
    /// </summary>
    public static XmlDocument GetWorksheetDocument(ZipArchive archive,
        string sheetName, Dictionary<string, string> sheetNameToPath)
    {
        if (!sheetNameToPath.TryGetValue(sheetName, out string sheetPath))
            return null;
        ZipArchiveEntry entry = archive.GetEntry(sheetPath);
        if (entry == null) return null;

        XmlDocument doc = new XmlDocument();
        using (Stream stream = entry.Open())
            doc.Load(stream);
        return doc;
    }

    // ──────────────────────────────────────────────
    //  共享字符串
    // ──────────────────────────────────────────────

    /// <summary>读取 xl/sharedStrings.xml，返回索引→字符串的字典。</summary>
    public static Dictionary<int, string> ReadSharedStrings(ZipArchive archive)
    {
        var dict = new Dictionary<int, string>();
        ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return dict;

        XmlDocument doc = new XmlDocument();
        using (Stream stream = entry.Open())
            doc.Load(stream);

        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        XmlNodeList siNodes = doc.SelectNodes("//x:sst/x:si", ns);
        if (siNodes == null) return dict;

        int idx = 0;
        foreach (XmlNode si in siNodes)
        {
            var textNodes = si.SelectNodes(".//x:t", ns);
            dict[idx] = (textNodes == null || textNodes.Count == 0)
                ? ""
                : string.Concat(textNodes.Cast<XmlNode>().Select(t => t.InnerText));
            idx++;
        }
        return dict;
    }

    // ──────────────────────────────────────────────
    //  单元格读取
    // ──────────────────────────────────────────────

    /// <summary>从行节点中读取指定列字母（A/B/C…）的单元格文本值。</summary>
    public static string GetCellValueByColumn(XmlNode rowNode, string columnLetter,
        Dictionary<int, string> sharedStrings, XmlNamespaceManager nsManager)
    {
        string xpath = $"x:c[starts-with(@r, '{columnLetter}')]";
        XmlNode cellNode = rowNode.SelectSingleNode(xpath, nsManager);
        if (cellNode == null) return string.Empty;

        string cellType = cellNode.Attributes?["t"]?.Value;
        XmlNode valueNode = cellNode.SelectSingleNode("x:v", nsManager);
        string raw = valueNode?.InnerText ?? "";

        if (cellType == "inlineStr")
        {
            XmlNode inline = cellNode.SelectSingleNode("x:is/x:t", nsManager);
            return inline?.InnerText ?? "";
        }
        if (cellType == "s" && int.TryParse(raw, out int idx) && sharedStrings.TryGetValue(idx, out string s))
            return s;
        return raw;
    }

    // ──────────────────────────────────────────────
    //  普通列映射（sourceColumn → targetColumn）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 读取 Excel 文件（默认第一个工作表），按指定列构建 源名称→导出名称 的映射。
    /// 支持源名称列中以换行/逗号等分隔的多个 Prefab 名称（自动追加 _1/_2…）。
    /// </summary>
    public static Dictionary<string, string> ReadNameMappingsFromExcel(
        string absolutePath, string sourceCol, string targetCol)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (FileStream fs = File.OpenRead(absolutePath))
        using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            var sheetMap = LoadSheetNamesAndPaths(archive);
            string firstSheet = sheetMap.Keys.FirstOrDefault();
            if (string.IsNullOrEmpty(firstSheet)) return result;

            string sheetPath = sheetMap[firstSheet];
            ZipArchiveEntry sheetEntry = archive.GetEntry(sheetPath);
            if (sheetEntry == null) return result;

            XmlDocument sheetDoc = new XmlDocument();
            using (Stream stream = sheetEntry.Open())
                sheetDoc.Load(stream);

            XmlNamespaceManager nsManager = new XmlNamespaceManager(sheetDoc.NameTable);
            nsManager.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Dictionary<int, string> sharedStrings = ReadSharedStrings(archive);
            XmlNodeList rowNodes = sheetDoc.SelectNodes("//x:worksheet/x:sheetData/x:row", nsManager);
            if (rowNodes == null) return result;

            string srcCol = sourceCol.ToUpper();
            string tgtCol = targetCol.ToUpper();

            foreach (XmlNode rowNode in rowNodes)
            {
                string sourceName = GetCellValueByColumn(rowNode, srcCol, sharedStrings, nsManager);
                string targetName = GetCellValueByColumn(rowNode, tgtCol, sharedStrings, nsManager);
                if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
                    continue;

                targetName = targetName.Trim();
                var sourceNames = ParseSourcePrefabNames(sourceName);

                if (sourceNames.Count == 1)
                {
                    if (!result.ContainsKey(sourceNames[0]))
                        result.Add(sourceNames[0], targetName);
                }
                else
                {
                    for (int i = 0; i < sourceNames.Count; i++)
                    {
                        string mapped = $"{targetName}_{i + 1}";
                        if (!result.ContainsKey(sourceNames[i]))
                            result.Add(sourceNames[i], mapped);
                    }
                }
            }
        }
        return result;
    }

    // ──────────────────────────────────────────────
    //  高级拆分拼接映射
    // ──────────────────────────────────────────────

    /// <summary>
    /// 高级映射：前缀列 + 映射列（多行键值对）→ 资源名→导出名称。
    /// </summary>
    public static Dictionary<string, string> ReadAdvancedMappingsFromExcel(
        string absolutePath,
        string selectedMappingFile,
        string selectedSheetName,
        Dictionary<string, string> sheetNameToPath,
        string prefixColumn,
        string mappingColumn,
        string keyValueSeparator,
        string joinSeparator,
        string customLineSeparator,
        bool descriptionFirst)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (FileStream fs = File.OpenRead(absolutePath))
        using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            // 若工作表路径表尚未填充，先刷新
            if (sheetNameToPath.Count == 0)
            {
                var freshMap = LoadSheetNamesAndPaths(archive);
                foreach (var kv in freshMap)
                    sheetNameToPath[kv.Key] = kv.Value;
            }

            if (!sheetNameToPath.ContainsKey(selectedSheetName))
            {
                Debug.LogError($"工作表 '{selectedSheetName}' 不存在于文件中");
                return result;
            }

            XmlDocument sheetDoc = GetWorksheetDocument(archive, selectedSheetName, sheetNameToPath);
            if (sheetDoc == null) return result;

            XmlNamespaceManager nsManager = new XmlNamespaceManager(sheetDoc.NameTable);
            nsManager.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Dictionary<int, string> sharedStrings = ReadSharedStrings(archive);
            XmlNodeList rowNodes = sheetDoc.SelectNodes("//x:worksheet/x:sheetData/x:row", nsManager);
            if (rowNodes == null) return result;

            string actualLineSep = customLineSeparator
                .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            string prefixCol  = prefixColumn.ToUpper();
            string mappingCol = mappingColumn.ToUpper();

            foreach (XmlNode rowNode in rowNodes)
            {
                string prefix  = GetCellValueByColumn(rowNode, prefixCol,  sharedStrings, nsManager)?.Trim();
                string mapping = GetCellValueByColumn(rowNode, mappingCol, sharedStrings, nsManager)?.Trim();
                if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(mapping)) continue;

                string[] lines = mapping.Split(new[] { actualLineSep }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    int sepIdx = trimmed.IndexOf(keyValueSeparator, StringComparison.Ordinal);

                    // F 列不含分隔符：纯资源名（如 "PHLL_Effect_02"），直接用 D 列作为导出名称
                    if (sepIdx < 0)
                    {
                        if (!string.IsNullOrEmpty(trimmed) && !result.ContainsKey(trimmed))
                            result.Add(trimmed, prefix);
                        continue;
                    }

                    string description, resourceName;
                    if (descriptionFirst)
                    {
                        description  = trimmed.Substring(0, sepIdx).Trim();
                        resourceName = trimmed.Substring(sepIdx + keyValueSeparator.Length).Trim();
                    }
                    else
                    {
                        resourceName = trimmed.Substring(0, sepIdx).Trim();
                        description  = trimmed.Substring(sepIdx + keyValueSeparator.Length).Trim();
                    }

                    if (string.IsNullOrEmpty(resourceName) || string.IsNullOrEmpty(description))
                        continue;

                    string exportName = prefix + joinSeparator + description;
                    if (!result.ContainsKey(resourceName))
                        result.Add(resourceName, exportName);
                }
            }
        }
        return result;
    }

    // ──────────────────────────────────────────────
    //  辅助
    // ──────────────────────────────────────────────

    /// <summary>将源名称字符串拆分为多个 Prefab 名称（支持换行/逗号/分号等）。</summary>
    public static List<string> ParseSourcePrefabNames(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return new List<string>();
        char[] separators = { '\r', '\n', ',', '\uff0c', ';', '\uff1b', '|', '\u3001', '\t' };
        return source.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .Where(s => !string.IsNullOrEmpty(s))
                     .ToList();
    }
}
