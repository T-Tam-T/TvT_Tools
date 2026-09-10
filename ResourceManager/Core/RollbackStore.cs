using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceManager.Core
{
    // =====================================================================
    //  共享回档存储
    //  —— 由 CopyModule（资源复制）与 DeduplicateModule（材质球去重）共用。
    //     回档文件为 JSON，按 日期_时间 命名，存放于 Assets/ResourceManagerRollback/。
    // =====================================================================

    [System.Serializable]
    public class RollbackSnapshot
    {
        public int version = 1;
        public string timestamp;                                  // yyyyMMdd_HHmmss
        public string source = "";                                // 来源说明（复制 / 材质球去重 ...）
        public List<RollbackObject> objects = new List<RollbackObject>();
        public List<RollbackTextureRef> materialTextures = new List<RollbackTextureRef>();
    }

    [System.Serializable]
    public class RollbackObject
    {
        public string key;                                        // prefab:路径 / scene:名称
        public string name;
        public bool isPrefab;
        public List<RollbackMaterialRef> materials = new List<RollbackMaterialRef>();
        public List<RollbackMeshRef> meshes = new List<RollbackMeshRef>();
        public List<RollbackMeshRef> skinnedMeshes = new List<RollbackMeshRef>();
    }

    [System.Serializable]
    public class RollbackMaterialRef
    {
        public string rendererPath;                               // 渲染器相对路径
        public int materialIndex;
        public string materialPath;                               // 原材质路径
    }

    [System.Serializable]
    public class RollbackMeshRef
    {
        public string componentPath;
        public string meshPath;
    }

    [System.Serializable]
    public class RollbackTextureRef
    {
        public string materialPath;                               // 被原地修改的材质
        public string propertyName;
        public string texturePath;                                // 原贴图路径
    }

    public static class RollbackStore
    {
        public const string DefaultFolder = "Assets/ResourceManagerRollback";

        // ----- 标识 / 路径 -----

        public static string ObjectKey(GameObject go)
        {
            if (go == null) return "null";
            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                string path = AssetDatabase.GetAssetPath(go);
                return string.IsNullOrEmpty(path) ? ("prefab:" + go.name) : ("prefab:" + path);
            }
            return "scene:" + go.name;
        }

        public static string ComponentPath(GameObject root, GameObject comp)
        {
            if (root == null || comp == null) return "";
            var names = new List<string>();
            var t = comp.transform;
            while (t != null && t != root.transform)
            {
                names.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", names);
        }

        public static GameObject FindChildByPath(GameObject root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root;
            var current = root;
            foreach (var seg in path.Split('/'))
            {
                if (string.IsNullOrEmpty(seg)) continue;
                var child = current.transform.Find(seg);
                if (child == null) return null;
                current = child.gameObject;
            }
            return current;
        }

        public static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(无)";
            return Path.GetFileName(path);
        }

        public static string FormatTimestamp(string ts)
        {
            if (string.IsNullOrEmpty(ts)) return "未知时间";
            if (ts.Length >= 15)
                return $"{ts.Substring(0, 4)}-{ts.Substring(4, 2)}-{ts.Substring(6, 2)} {ts.Substring(9, 2)}:{ts.Substring(11, 2)}:{ts.Substring(13, 2)}";
            return ts;
        }

        // ----- 捕获“当前”状态 -----

        /// <summary>捕获单个对象当前的材质/网格引用。</summary>
        public static RollbackObject CaptureObject(GameObject go)
        {
            if (go == null) return null;

            var obj = new RollbackObject
            {
                key = ObjectKey(go),
                name = go.name,
                isPrefab = PrefabUtility.IsPartOfPrefabAsset(go)
            };

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.sharedMaterials == null) continue;
                var mats = renderer.sharedMaterials;
                string rendererPath = ComponentPath(go, renderer.gameObject);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        obj.materials.Add(new RollbackMaterialRef
                        {
                            rendererPath = rendererPath,
                            materialIndex = i,
                            materialPath = AssetDatabase.GetAssetPath(mats[i]) ?? ""
                        });
                    }
                }
            }

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                obj.meshes.Add(new RollbackMeshRef
                {
                    componentPath = ComponentPath(go, mf.gameObject),
                    meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh) ?? ""
                });
            }

            foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sm == null || sm.sharedMesh == null) continue;
                obj.skinnedMeshes.Add(new RollbackMeshRef
                {
                    componentPath = ComponentPath(go, sm.gameObject),
                    meshPath = AssetDatabase.GetAssetPath(sm.sharedMesh) ?? ""
                });
            }

            return obj;
        }

        /// <summary>捕获材质内部的贴图引用（用于“材质内原地替换贴图”的回档）。</summary>
        public static void CaptureMaterialTextures(Material mat, List<RollbackTextureRef> list)
        {
            if (mat == null || mat.shader == null || list == null) return;
            int propCount = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                    Texture tex = mat.GetTexture(propName);
                    if (tex == null) continue;
                    list.Add(new RollbackTextureRef
                    {
                        materialPath = AssetDatabase.GetAssetPath(mat) ?? "",
                        propertyName = propName,
                        texturePath = AssetDatabase.GetAssetPath(tex) ?? ""
                    });
                }
            }
        }

        /// <summary>捕获整个分析会话（所有对象 + 材质内贴图）。</summary>
        public static RollbackSnapshot Capture(AnalysisSession session, string source = "")
        {
            var snap = new RollbackSnapshot
            {
                version = 1,
                timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                source = source ?? ""
            };
            if (session == null) return snap;

            var seenMaterials = new HashSet<Material>();
            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;

                var objSnap = CaptureObject(go);
                if (objSnap != null &&
                    (objSnap.materials.Count > 0 || objSnap.meshes.Count > 0 || objSnap.skinnedMeshes.Count > 0))
                {
                    snap.objects.Add(objSnap);
                }

                if (objSnap == null) continue;
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat == null || seenMaterials.Contains(mat)) continue;
                        seenMaterials.Add(mat);
                        CaptureMaterialTextures(mat, snap.materialTextures);
                    }
                }
            }
            return snap;
        }

        // ----- 存 / 取 -----

        public static string Save(RollbackSnapshot snapshot, string folder = DefaultFolder)
        {
            if (snapshot == null) return null;
            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder;

            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = Path.GetDirectoryName(folder);
                string name = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(parent)) parent = "Assets";
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(parent));
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(parent, name);
            }

            if (string.IsNullOrEmpty(snapshot.timestamp))
                snapshot.timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string json = JsonUtility.ToJson(snapshot, true);
            string fileName = $"回档_{snapshot.timestamp}.json";
            string assetPath = $"{folder}/{fileName}";
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, json, new System.Text.UTF8Encoding(false));
            AssetDatabase.Refresh();
            return assetPath;
        }

        public static List<KeyValuePair<string, RollbackSnapshot>> LoadAll(string folder = DefaultFolder)
        {
            var result = new List<KeyValuePair<string, RollbackSnapshot>>();
            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder;
            if (!AssetDatabase.IsValidFolder(folder)) return result;

            string fullDir = Path.Combine(Application.dataPath, folder.Substring("Assets/".Length));
            if (!Directory.Exists(fullDir)) return result;

            foreach (var file in Directory.GetFiles(fullDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var snap = JsonUtility.FromJson<RollbackSnapshot>(json);
                    if (snap != null)
                        result.Add(new KeyValuePair<string, RollbackSnapshot>(Path.GetFileName(file), snap));
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[回档] 读取回档失败: {file} ({e.Message})");
                }
            }

            return result.OrderByDescending(kvp => kvp.Value != null ? kvp.Value.timestamp : "").ToList();
        }

        // ----- 还原 -----

        /// <summary>还原单个对象的材质/网格引用。返回是否全部成功（无删除/不匹配）。</summary>
        public static bool RestoreObject(GameObject go, RollbackSnapshot snapshot, string fileName)
        {
            if (go == null || snapshot == null) return false;
            var objSnap = snapshot.objects.FirstOrDefault(o => o.key == ObjectKey(go));
            if (objSnap == null) return false;

            bool allOk = true;
            bool dirty = false;

            foreach (var m in objSnap.materials)
            {
                var targetGo = FindChildByPath(go, m.rendererPath);
                if (targetGo == null) continue;
                var renderer = targetGo.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterials == null) continue;

                var mats = renderer.sharedMaterials;
                if (m.materialIndex < 0 || m.materialIndex >= mats.Length) continue;

                var original = string.IsNullOrEmpty(m.materialPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Material>(m.materialPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史材质已删除，无法还原: {m.materialPath} ({go.name}/{m.rendererPath}[{m.materialIndex}])");
                    continue;
                }

                if (mats[m.materialIndex] != original)
                {
                    mats[m.materialIndex] = original;
                    renderer.sharedMaterials = mats;
                    dirty = true;
                }
            }

            foreach (var mm in objSnap.meshes)
            {
                var targetGo = FindChildByPath(go, mm.componentPath);
                if (targetGo == null) continue;
                var mf = targetGo.GetComponent<MeshFilter>();
                if (mf == null) continue;

                var original = string.IsNullOrEmpty(mm.meshPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Mesh>(mm.meshPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史网格已删除，无法还原: {mm.meshPath} ({go.name}/{mm.componentPath})");
                    continue;
                }

                if (mf.sharedMesh != original)
                {
                    mf.sharedMesh = original;
                    dirty = true;
                }
            }

            foreach (var sm in objSnap.skinnedMeshes)
            {
                var targetGo = FindChildByPath(go, sm.componentPath);
                if (targetGo == null) continue;
                var skinned = targetGo.GetComponent<SkinnedMeshRenderer>();
                if (skinned == null) continue;

                var original = string.IsNullOrEmpty(sm.meshPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Mesh>(sm.meshPath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史网格已删除，无法还原: {sm.meshPath} ({go.name}/{sm.componentPath})");
                    continue;
                }

                if (skinned.sharedMesh != original)
                {
                    skinned.sharedMesh = original;
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(go);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[回档] {go.name} 已还原（来自 {fileName}）");
            }

            return allOk;
        }

        /// <summary>还原材质内被原地替换的贴图引用。</summary>
        public static bool RestoreMaterialTextures(RollbackSnapshot snapshot)
        {
            if (snapshot == null || snapshot.materialTextures == null) return true;
            bool allOk = true;
            bool dirty = false;
            foreach (var t in snapshot.materialTextures)
            {
                if (string.IsNullOrEmpty(t.materialPath) || string.IsNullOrEmpty(t.propertyName)) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(t.materialPath);
                if (mat == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 材质已删除，无法还原贴图: {t.materialPath}");
                    continue;
                }
                var original = string.IsNullOrEmpty(t.texturePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture>(t.texturePath);
                if (original == null)
                {
                    allOk = false;
                    Debug.LogWarning($"[回档] 历史贴图已删除，无法还原: {t.texturePath} (材质 {t.materialPath} · {t.propertyName})");
                    continue;
                }
                Texture cur = mat.GetTexture(t.propertyName);
                if (cur != original)
                {
                    mat.SetTexture(t.propertyName, original);
                    EditorUtility.SetDirty(mat);
                    dirty = true;
                }
            }
            if (dirty)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return allOk;
        }

        /// <summary>把分析会话中的匹配对象 + 材质内贴图整体还原到快照。</summary>
        public static bool Restore(AnalysisSession session, RollbackSnapshot snapshot, string fileName)
        {
            if (snapshot == null) return false;
            bool allOk = true;
            if (session != null)
            {
                foreach (var kvp in session.Analyzers)
                {
                    var go = kvp.Key;
                    if (go == null) continue;
                    if (snapshot.objects.Any(o => o.key == ObjectKey(go)))
                    {
                        if (!RestoreObject(go, snapshot, fileName))
                            allOk = false;
                    }
                }
            }
            if (!RestoreMaterialTextures(snapshot))
                allOk = false;
            return allOk;
        }
    }
}
