using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

namespace ResourceManager.Utilities
{
    public static class SelectionHelper
    {
        public static void SelectAndPingObject(string usagePath, bool isExpanded)
        {
            try
            {
                // 1. 移除路径中的"Root/"前缀
                string cleanPath = usagePath.StartsWith("Root/") ?
                    usagePath.Substring(5) : usagePath;

                // 2. 智能路径优化
                string optimizedPath = OptimizePath(cleanPath);

                // 3. 尝试在场景中找到匹配的对象
                GameObject foundObject = FindObjectByPath(optimizedPath);

                if (foundObject != null)
                {
                    Selection.activeObject = foundObject;
                    EditorGUIUtility.PingObject(foundObject);
                    Debug.Log($"成功定位对象: {optimizedPath}");
                }
                else
                {
                    Debug.LogWarning($"找不到对象: {optimizedPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"选中对象失败: {e.Message}\n路径: {usagePath}");
            }
        }

        // 智能路径优化
        private static string OptimizePath(string path)
        {
            // 分割路径
            string[] parts = path.Split('/');

            // 处理常见模式
            if (parts.Length > 2)
            {
                // 检查最后一部分是否是组件名称
                string lastPart = parts.Last();
                if (lastPart.StartsWith("ParticleSystem") ||
                    lastPart.StartsWith("Renderer") ||
                    lastPart.StartsWith("Mesh") ||
                    lastPart.StartsWith("Animator") ||
                    lastPart.StartsWith("Texture"))
                {
                    // 移除最后一部分（组件名）
                    return string.Join("/", parts.Take(parts.Length - 1));
                }

                // 检查是否有重复的对象名
                if (parts.Length > 3 && parts[parts.Length - 2] == parts[parts.Length - 3])
                {
                    // 移除最后两部分（重复对象名和组件名）
                    return string.Join("/", parts.Take(parts.Length - 2));
                }
            }

            return path;
        }

        // 改进的对象查找方法
        private static GameObject FindObjectByPath(string path)
        {
            // 方法1：尝试完整路径匹配（支持任意层级）
            GameObject foundByFullPath = FindByFullPath(path);
            if (foundByFullPath != null) return foundByFullPath;

            // 方法2：尝试部分匹配（最后2-3级）
            GameObject foundByPartialPath = FindByPartialPath(path);
            if (foundByPartialPath != null) return foundByPartialPath;

            // 方法3：尝试名称匹配（最后一级）
            return FindByName(path.Split('/').LastOrDefault());
        }

        // 通过完整路径查找对象（支持任意层级）
        private static GameObject FindByFullPath(string path)
        {
            // 获取场景中所有对象
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();

            foreach (GameObject obj in allObjects)
            {
                // 获取对象的完整路径
                string fullPath = GetFullPath(obj.transform);

                if (fullPath == path)
                {
                    return obj;
                }
            }

            return null;
        }

        // 获取对象的完整路径
        private static string GetFullPath(Transform transform)
        {
            if (transform.parent == null)
                return transform.name;

            return GetFullPath(transform.parent) + "/" + transform.name;
        }

        // 通过部分路径查找对象（最后2-3级）
        private static GameObject FindByPartialPath(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length < 2) return null;

            // 尝试匹配最后2级
            string lastTwo = $"{parts[parts.Length - 2]}/{parts[parts.Length - 1]}";
            GameObject found = FindByFullPath(lastTwo);
            if (found != null) return found;

            // 尝试匹配最后3级
            if (parts.Length > 2)
            {
                string lastThree = $"{parts[parts.Length - 3]}/{parts[parts.Length - 2]}/{parts[parts.Length - 1]}";
                found = FindByFullPath(lastThree);
                if (found != null) return found;
            }

            return null;
        }

        // 通过名称查找对象
        private static GameObject FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name == name)
                {
                    return obj;
                }
            }

            return null;
        }
    }
}