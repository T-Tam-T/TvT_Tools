using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 导出项数据模型，持有单个 Prefab 的导出信息及其依赖列表。
/// </summary>
public class ExportItem
{
    public GameObject prefab;
    public string exportName;
    public bool foldout = false;

    /// <summary>
    /// Key: 资源路径  Value: (是否勾选导出, 资源类型字符串)
    /// </summary>
    public Dictionary<string, (bool selected, string assetType)> dependencies
        = new Dictionary<string, (bool, string)>();
}
