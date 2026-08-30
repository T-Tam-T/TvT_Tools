using UnityEngine;
using UnityEditor;
using System.IO;

namespace ResourceManager.Utilities
{
    public static class PathHelper
    {
        // 获取资源路径
        public static string GetResourcePath(Object resource)
        {
            return AssetDatabase.GetAssetPath(resource);
        }

        // 获取资源所在文件夹
        public static string GetResourceFolder(Object resource)
        {
            string path = GetResourcePath(resource);
            return Path.GetDirectoryName(path);
        }

        // 获取资源文件名
        public static string GetResourceFileName(Object resource)
        {
            string path = GetResourcePath(resource);
            return Path.GetFileName(path);
        }

        // 规范化路径（统一使用斜杠）
        public static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}