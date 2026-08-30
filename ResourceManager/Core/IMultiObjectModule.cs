// ResourceManager/Core/IMultiObjectModule.cs
using UnityEngine;

namespace ResourceManager.Core
{
    public interface IMultiObjectModule
    {
        void DrawMultiObject(AnalysisSession session, ResourceCache cache);
        void Clear();
    }
}