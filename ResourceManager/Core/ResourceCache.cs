using System.Collections.Generic;
using UnityEngine;

namespace ResourceManager.Core
{
    public class ResourceCache
    {
        private Dictionary<System.Type, List<Object>> cachedResources = new Dictionary<System.Type, List<Object>>();

        public void Clear()
        {
            cachedResources.Clear();
        }

        public List<Object> GetCachedResources(System.Type type, ResourceAnalyzer analyzer)
        {
            if (cachedResources.TryGetValue(type, out var resources))
            {
                return resources;
            }

            resources = analyzer.GetResourcesByType(type);
            cachedResources[type] = resources;
            return resources;
        }
    }
}