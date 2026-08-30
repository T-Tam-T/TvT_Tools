// ResourceManager/Core/MultiObjectAnalyzer.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Core
{
    [System.Serializable]
    public class AnalysisObject
    {
        public GameObject TargetObject;
        public string ObjectName;
        public bool IsSelected = true;
        public ObjectType Type; // Prefab 或 SceneObject
    }

    public enum ObjectType
    {
        Prefab,
        SceneObject
    }

    [System.Serializable]
    public class AnalysisSession
    {
        public List<AnalysisObject> PrefabObjects = new List<AnalysisObject>();
        public List<AnalysisObject> SceneObjects = new List<AnalysisObject>();
        public Dictionary<GameObject, ResourceAnalyzer> Analyzers = new Dictionary<GameObject, ResourceAnalyzer>();
        public Dictionary<Object, List<string>> CommonResources = new Dictionary<Object, List<string>>();

        public List<AnalysisObject> GetCurrentObjects(ObjectType currentType)
        {
            return currentType == ObjectType.Prefab ? PrefabObjects : SceneObjects;
        }

        public void AnalyzeAll(ObjectType currentType)
        {
            CommonResources.Clear();
            var currentObjects = GetCurrentObjects(currentType);
            var resourceUsageMap = new Dictionary<Object, HashSet<string>>();

            foreach (var analysisObj in currentObjects)
            {
                if (analysisObj.IsSelected && analysisObj.TargetObject != null)
                {
                    if (!Analyzers.ContainsKey(analysisObj.TargetObject))
                    {
                        Analyzers[analysisObj.TargetObject] = new ResourceAnalyzer();
                    }

                    var analyzer = Analyzers[analysisObj.TargetObject];

                    if (currentType == ObjectType.Prefab)
                    {
                        analyzer.SelectedPrefab = analysisObj.TargetObject;
                        analyzer.AnalyzeResources();
                    }
                    else
                    {
                        analyzer.DetectedObject = analysisObj.TargetObject;
                        analyzer.AnalyzeDetectedObject();
                    }

                    // 收集资源使用信息，用于统计共用资源
                    foreach (var kvp in analyzer.ResourceUsage)
                    {
                        if (!resourceUsageMap.ContainsKey(kvp.Key))
                        {
                            resourceUsageMap[kvp.Key] = new HashSet<string>();
                        }
                        resourceUsageMap[kvp.Key].Add(analysisObj.ObjectName);
                    }
                }
            }

            // 统计共用资源，被2个及以上对象使用的资源
            foreach (var kvp in resourceUsageMap)
            {
                if (kvp.Value.Count >= 2)
                {
                    CommonResources[kvp.Key] = kvp.Value.ToList();
                }
            }
        }

        public void Clear()
        {
            PrefabObjects.Clear();
            SceneObjects.Clear();
            Analyzers.Clear();
            CommonResources.Clear();
        }
    }
}