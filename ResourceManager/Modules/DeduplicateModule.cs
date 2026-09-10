using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System;

namespace ResourceManager.Modules
{
    public class DeduplicateModule : IMultiObjectModule
    {
        // ----- 数据 -----
        // 全局搜索过滤
        public string SearchFilter = "";

        private string sourceFolder = "Assets";
        private bool usePixelHash = false;
        private bool skipUnityTextures = true;

        private List<TextureGroup> textureGroups = new List<TextureGroup>();
        // 引用维度的替换选择：key = "引用唯一标识", value = 目标贴图
        private Dictionary<string, Texture2D> textureReplacements = new Dictionary<string, Texture2D>();
        private Vector2 scrollPos;
        private bool isProcessing = false;
        private string status = "就绪";
        private float progress;

        private bool showAdvanced = false;

        // 分组折叠状态
        private Dictionary<int, bool> groupFoldouts = new Dictionary<int, bool>();

        // ----- 内部类 -----

        /// <summary>
        /// 记录某处对贴图的引用位置（支持粒子系统 与 模型 MeshRenderer/SkinnedMeshRenderer 材质球）
        /// </summary>
        [System.Serializable]
        public class TextureRef
        {
            public GameObject ownerObject;              // 所属根对象（预制体/场景对象）
            public string refType;                      // 引用类型: "ParticleMaterial"/"ParticleSprite"/"ParticleTex"/"MeshMaterial"
            public ParticleSystem particleSystem;       // 所属粒子系统（仅粒子引用）
            public Renderer renderer;                   // 所属渲染器（仅 MeshMaterial / 模型引用）
            public int materialIndex;                   // 材质槽索引（Material 类型）
            public string propertyName;                 // Shader 属性名（Material 类型）
            public int spriteIndex;                     // 精灵索引（TextureSheetSprite 类型）

            public bool IsParticleRef => refType != "MeshMaterial";
            public bool IsMeshRef => refType == "MeshMaterial";

            public string DisplayLabel
            {
                get
                {
                    switch (refType)
                    {
                        case "ParticleMaterial": return $"粒子·材质[{materialIndex}]·{propertyName}";
                        case "ParticleSprite": return $"粒子·纹理帧[精灵:{spriteIndex}]";
                        case "ParticleTex": return "粒子·纹理帧[Tex模式]";
                        case "MeshMaterial": return $"模型·材质[{materialIndex}]·{propertyName}";
                        default: return refType;
                    }
                }
            }

            // 用于生成唯一 key
            public string UniqueKey => $"{refType}_{ReferenceInstanceId}_{materialIndex}_{propertyName}_{spriteIndex}";

            private int ReferenceInstanceId
            {
                get
                {
                    if (particleSystem != null) return particleSystem.GetInstanceID();
                    if (renderer != null) return renderer.GetInstanceID();
                    return ownerObject != null ? ownerObject.GetInstanceID() : 0;
                }
            }
        }

        private class TextureGroup
        {
            public Texture2D sourceTexture;
            public string sourcePath;                   // 源贴图路径，用于去重判断
            public List<DuplicateEntry> duplicates = new List<DuplicateEntry>();
            public string hash;
        }

        /// <summary>
        /// 一张重复贴图 + 使用它的引用列表（粒子 或 模型材质球）
        /// </summary>
        private class DuplicateEntry
        {
            public Texture2D texture;
            public string texturePath;
            public List<TextureRef> refs = new List<TextureRef>();
        }

        // ----- 子标签：贴图去重 / 材质球去重 -----
        private int subTabIndex = 0;
        private static readonly string[] subTabNames = { "贴图去重", "材质球去重" };

        // ----- IMultiObjectModule 实现 -----
        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先在左侧「对象列表」中添加并分析对象，然后再执行去重操作。", MessageType.Info);
                return;
            }

            subTabIndex = GUILayout.Toolbar(subTabIndex, subTabNames);
            EditorGUILayout.Space(5);

            if (subTabIndex == 0)
                DrawTextureDedupTab(session, cache);
            else
                DrawMaterialDedupTab(session);
        }

        private void DrawTextureDedupTab(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("贴图去重（与源文件夹对比）", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // ----- 源文件夹选择 -----
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("源文件夹（参考）:", GUILayout.Width(100));
            sourceFolder = EditorGUILayout.TextField(sourceFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
            {
                string path = EditorUtility.OpenFolderPanel("选择源文件夹", sourceFolder, "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                    sourceFolder = "Assets" + path.Substring(Application.dataPath.Length);
            }
            EditorGUILayout.EndHorizontal();

            skipUnityTextures = EditorGUILayout.Toggle("跳过Unity内置贴图", skipUnityTextures);

            // ----- 高级选项 -----
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "高级选项");
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                usePixelHash = EditorGUILayout.Toggle("像素哈希模式（慢但精确）", usePixelHash);
                if (usePixelHash)
                    EditorGUILayout.HelpBox("像素模式需要贴图开启 Read/Write，否则会跳过。", MessageType.Warning);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);

            // ----- 操作按钮 -----
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描粒子/模型系统中的重复贴图", GUILayout.Height(30)))
            {
                ScanDuplicates(session);
            }
            if (GUILayout.Button("替换选中项", GUILayout.Height(30)) && textureGroups.Count > 0)
            {
                ReplaceSelected(session);
            }
            if (GUILayout.Button("清除结果", GUILayout.Height(30)))
            {
                ClearResults();
            }
            EditorGUILayout.EndHorizontal();

            // ----- 紧急修复按钮行 -----
            if (GUILayout.Button("🔧 修复未保存材质（解决无法保存到Project的问题）", GUILayout.Height(24)))
            {
                FixUnsavedMaterials(session);
            }

            // ----- 状态栏 -----
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label($"状态: {status}");
            GUILayout.FlexibleSpace();
            if (isProcessing)
            {
                Rect rect = GUILayoutUtility.GetRect(200, 18);
                EditorGUI.ProgressBar(rect, progress, $"处理中: {progress:P0}");
                if (GUILayout.Button("取消", GUILayout.Width(60)))
                    isProcessing = false;
            }
            else
            {
                int totalDups = textureGroups.Sum(g => g.duplicates.Sum(d => d.texture != null ? 1 : 0));
                int totalRefs = textureGroups.Sum(g => g.duplicates.Sum(d => d.refs.Count));
                GUILayout.Label($"分组: {textureGroups.Count}  重复贴图: {totalDups}  引用: {totalRefs}");
            }
            EditorGUILayout.EndHorizontal();

            // ----- 结果列表 -----
            if (textureGroups.Count > 0 && !isProcessing)
            {
                // 应用搜索过滤
                string filterStr = string.IsNullOrEmpty(SearchFilter) ? null : SearchFilter.ToLower();
                int matchCount = 0;
                if (filterStr != null)
                {
                    matchCount = textureGroups.Count(g =>
                        (g.sourceTexture != null && g.sourceTexture.name.ToLower().Contains(filterStr)) ||
                        g.duplicates.Any(d => d.texture != null && d.texture.name.ToLower().Contains(filterStr))
                    );
                    if (matchCount == 0)
                    {
                        EditorGUILayout.HelpBox($"未找到匹配 \"{SearchFilter}\" 的贴图分组", MessageType.Info);
                        return;
                    }
                    EditorGUILayout.HelpBox($"搜索 \"{SearchFilter}\" → 找到 {matchCount} 个匹配分组", MessageType.Info);
                }

                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
                for (int i = 0; i < textureGroups.Count; i++)
                {
                    if (filterStr != null)
                    {
                        bool matches = (textureGroups[i].sourceTexture != null && textureGroups[i].sourceTexture.name.ToLower().Contains(filterStr)) ||
                            textureGroups[i].duplicates.Any(d => d.texture != null && d.texture.name.ToLower().Contains(filterStr));
                        if (!matches) continue;
                    }
                    DrawTextureGroup(textureGroups[i], i, session);
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("暂无重复贴图分组，请执行扫描。", EditorStyles.centeredGreyMiniLabel);
            }
        }

        // =====================================================================
        //  材质球去重
        //  把参数完全一致（且未被动画驱动）的 _dedup 材质替换回原名材质，并可回档。
        // =====================================================================

        private const string DedupSuffix = "_dedup";
        private List<MaterialDedupGroup> materialGroups = new List<MaterialDedupGroup>();
        private bool materialScanDone = false;
        private bool deleteDedupAsset = false;   // 替换后是否删除 _dedup 材质资产
        private Vector2 materialScrollPos;

        /// <summary>材质球去重：某处对 _dedup 材质的引用</summary>
        public class MaterialRef
        {
            public GameObject ownerObject;
            public Renderer renderer;
            public int materialIndex;
        }

        private class MaterialDedupGroup
        {
            public Material dedupMat;
            public string dedupPath;
            public Material originalMat;
            public string originalPath;
            public List<MaterialRef> refs = new List<MaterialRef>();
            public bool identical;      // 参数是否完全一致
            public bool animated;       // 是否被动画 K 帧驱动
        }

        private void DrawMaterialDedupTab(AnalysisSession session)
        {
            GUILayout.Label("材质球去重（_dedup 回收）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "当 _dedup 材质与同名的原材质【参数完全一致】且【未被动画 K 帧】时，可把引用替换回原材质。\n" +
                "替换前会生成回档文件，可在「拓展 → 复制 → 回档」中还原。",
                MessageType.Info);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先在左侧「对象列表」中添加并分析对象，然后再执行扫描。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描 _dedup 材质（与原名材质对比）", GUILayout.Height(28)))
            {
                ScanMaterialDuplicates(session);
            }
            if (GUILayout.Button("清除结果", GUILayout.Height(28)))
            {
                materialGroups.Clear();
                materialScanDone = false;
            }
            EditorGUILayout.EndHorizontal();

            deleteDedupAsset = EditorGUILayout.Toggle("替换后删除 _dedup 材质资产（删除后该材质无法回档）", deleteDedupAsset);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (!materialScanDone)
            {
                GUILayout.Label("状态: 未扫描");
            }
            else
            {
                int total = materialGroups.Count;
                int ok = materialGroups.Count(g => g.identical && !g.animated);
                GUILayout.Label($"状态: 候选 {total} 个，其中可合并 {ok} 个");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (!materialScanDone)
            {
                GUILayout.Label("请执行扫描。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (materialGroups.Count == 0)
            {
                GUILayout.Label("未发现 _dedup 材质（或找不到对应的原名材质）。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            materialScrollPos = EditorGUILayout.BeginScrollView(materialScrollPos, GUILayout.ExpandHeight(true));
            for (int i = 0; i < materialGroups.Count; i++)
            {
                DrawMaterialDedupGroup(materialGroups[i], i, session);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawMaterialDedupGroup(MaterialDedupGroup g, int index, AnalysisSession session)
        {
            using (new UIHelper.ZebraScope(index))
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(g.dedupMat, typeof(Material), false, GUILayout.Width(160));
                GUILayout.Label("→", GUILayout.Width(16));
                EditorGUILayout.ObjectField(g.originalMat, typeof(Material), false, GUILayout.Width(160));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{g.refs.Count} 处引用", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    $"{Path.GetFileName(g.dedupPath)}  →  {Path.GetFileName(g.originalPath)}",
                    EditorStyles.miniLabel);

                if (!g.identical)
                    EditorGUILayout.HelpBox("参数不一致：不能合并（与原材质参数不同）。", MessageType.Warning);
                else if (g.animated)
                    EditorGUILayout.HelpBox("该材质被动画驱动（存在 K 帧）：不能合并，否则动画会失效。", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("参数完全一致且未被动画驱动，可安全合并回原名材质。", MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(!g.identical || g.animated);
                if (GUILayout.Button("替换为原名材质", GUILayout.Width(140)))
                {
                    ReplaceMaterialGroup(g, session);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }
        }

        private void ScanMaterialDuplicates(AnalysisSession session)
        {
            materialGroups.Clear();
            materialScanDone = true;

            var dedupMaterials = new Dictionary<Material, List<MaterialRef>>();

            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;

                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null || string.IsNullOrEmpty(mat.name)) continue;
                        if (!mat.name.EndsWith(DedupSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!dedupMaterials.ContainsKey(mat))
                            dedupMaterials[mat] = new List<MaterialRef>();
                        dedupMaterials[mat].Add(new MaterialRef
                        {
                            ownerObject = go,
                            renderer = renderer,
                            materialIndex = i
                        });
                    }
                }
            }

            foreach (var kvp in dedupMaterials)
            {
                var dm = kvp.Key;
                string dp = AssetDatabase.GetAssetPath(dm);

                string origName;
                if (!TryGetOriginalName(dm.name, out origName)) continue;

                var om = FindOriginalMaterial(dp, origName);
                if (om == null) continue;

                string op = AssetDatabase.GetAssetPath(om);
                bool identical = AreMaterialsIdentical(dm, om);
                bool animated = kvp.Value.Any(r => IsObjectMaterialAnimated(r.ownerObject));

                materialGroups.Add(new MaterialDedupGroup
                {
                    dedupMat = dm,
                    dedupPath = dp,
                    originalMat = om,
                    originalPath = op,
                    refs = kvp.Value,
                    identical = identical,
                    animated = animated
                });
            }

            // 可合并的排前面
            materialGroups = materialGroups
                .OrderByDescending(g => g.identical && !g.animated)
                .ThenBy(g => g.dedupMat != null ? g.dedupMat.name : "")
                .ToList();
        }

        private bool TryGetOriginalName(string dedupName, out string originalName)
        {
            originalName = null;
            if (string.IsNullOrEmpty(dedupName)) return false;
            // 兼容 "X_dedup" 与 "X_dedup 1" 这类后缀
            int idx = dedupName.LastIndexOf(DedupSuffix, StringComparison.OrdinalIgnoreCase);
            if (idx <= 0) return false;
            originalName = dedupName.Substring(0, idx);
            return originalName.Length > 0;
        }

        private Material FindOriginalMaterial(string dedupPath, string originalName)
        {
            // 先找同目录下的同名材质
            if (!string.IsNullOrEmpty(dedupPath))
            {
                string dir = Path.GetDirectoryName(dedupPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    dir = dir.Replace("\\", "/");
                    var sameDir = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{originalName}.mat");
                    if (sameDir != null) return sameDir;
                }
            }

            // 再全局查找
            foreach (var guid in AssetDatabase.FindAssets($"{originalName} t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p).Equals(originalName, StringComparison.OrdinalIgnoreCase))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                    if (m != null) return m;
                }
            }
            return null;
        }

        /// <summary>两份材质是否所有参数完全一致（Shader / 渲染队列 / 关键字 / 全部属性）。</summary>
        private bool AreMaterialsIdentical(Material a, Material b)
        {
            if (a == null || b == null) return false;
            if (a.shader != b.shader) return false;
            if (a.renderQueue != b.renderQueue) return false;

            string[] ka = a.shaderKeywords;
            string[] kb = b.shaderKeywords;
            if ((ka == null) != (kb == null)) return false;
            if (ka != null && kb != null)
            {
                if (ka.Length != kb.Length) return false;
                foreach (var k in ka)
                {
                    if (!kb.Contains(k)) return false;
                }
            }

            Shader shader = a.shader;
            if (shader == null) return true;

            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                string prop = ShaderUtil.GetPropertyName(shader, i);
                switch (ShaderUtil.GetPropertyType(shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        if (a.GetTexture(prop) != b.GetTexture(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (a.GetColor(prop) != b.GetColor(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        if (a.GetVector(prop) != b.GetVector(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (!Mathf.Approximately(a.GetFloat(prop), b.GetFloat(prop))) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Int:
                        if (a.GetInt(prop) != b.GetInt(prop)) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>对象（或其子对象）的动画是否驱动了材质参数。</summary>
        private bool IsObjectMaterialAnimated(GameObject root)
        {
            if (root == null) return false;

            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null || animator.runtimeAnimatorController == null) continue;
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (ClipAnimatesMaterial(clip)) return true;
                }
            }

            foreach (var anim in root.GetComponentsInChildren<Animation>(true))
            {
                if (anim == null) continue;
                foreach (AnimationState state in anim)
                {
                    if (state != null && state.clip != null && ClipAnimatesMaterial(state.clip))
                        return true;
                }
            }

            return false;
        }

        private bool ClipAnimatesMaterial(AnimationClip clip)
        {
            if (clip == null) return false;

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (IsMaterialPropertyName(b.propertyName)) return true;
            }
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (IsMaterialPropertyName(b.propertyName)) return true;
            }
            return false;
        }

        private bool IsMaterialPropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;
            return propertyName.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   propertyName.IndexOf("m_Materials", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ReplaceMaterialGroup(MaterialDedupGroup g, AnalysisSession session)
        {
            if (g == null || g.originalMat == null) return;
            if (!g.identical)
            {
                EditorUtility.DisplayDialog("提示", "参数不一致，不能合并。", "确定");
                return;
            }
            if (g.animated)
            {
                EditorUtility.DisplayDialog("提示", "该材质被动画驱动，不能合并。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认替换",
                $"将把 {g.refs.Count} 处对「{g.dedupMat.name}」的引用替换为「{g.originalMat.name}」。\n" +
                (deleteDedupAsset ? "并删除 _dedup 材质资产（删除后该材质无法回档）。\n" : "") +
                "\n替换前会生成回档文件。确定继续？", "确定", "取消"))
                return;

            // 回档快照（替换前）
            var snapshot = RollbackStore.Capture(session, "材质球去重");

            int changed = 0;
            foreach (var r in g.refs)
            {
                if (r == null || r.renderer == null) continue;
                var mats = r.renderer.sharedMaterials;
                if (r.materialIndex < 0 || r.materialIndex >= mats.Length) continue;
                if (mats[r.materialIndex] == g.originalMat) continue;

                Undo.RecordObject(r.renderer, "材质球去重");
                mats[r.materialIndex] = g.originalMat;
                r.renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(r.renderer);
                changed++;
            }

            AssetDatabase.SaveAssets();
            string snapPath = RollbackStore.Save(snapshot);

            string deleted = "";
            if (deleteDedupAsset && g.dedupMat != null && !string.IsNullOrEmpty(g.dedupPath))
            {
                deleted = AssetDatabase.DeleteAsset(g.dedupPath)
                    ? $"\n已删除 {Path.GetFileName(g.dedupPath)}"
                    : $"\n删除 {Path.GetFileName(g.dedupPath)} 失败";
            }

            AssetDatabase.Refresh();

            string msg = $"已将 {changed} 处引用替换为「{g.originalMat.name}」。{deleted}\n回档文件: {snapPath}";
            Debug.Log($"[材质球去重] {msg}");
            EditorUtility.DisplayDialog("替换完成", msg, "确定");

            ScanMaterialDuplicates(session);
        }

        public void Clear()
        {
            ClearResults();
            materialGroups.Clear();
            materialScanDone = false;
        }

        // ----- UI绘制 -----

        /// <summary>
        /// 绘制一个可折叠的重复分组：源贴图 + 每张重复贴图及其使用引用列表
        /// </summary>
        private void DrawTextureGroup(TextureGroup group, int groupIndex, AnalysisSession session)
        {
            // 初始化折叠状态
            if (!groupFoldouts.ContainsKey(groupIndex))
                groupFoldouts[groupIndex] = false;

            // 搜索时自动展开
            if (!string.IsNullOrEmpty(SearchFilter))
                groupFoldouts[groupIndex] = true;

            EditorGUILayout.BeginVertical("box");

            // --- 折叠标题行 ---
            int dupCount = group.duplicates.Count;
            int refCount = group.duplicates.Sum(d => d.refs.Count);

            // 统计粒子/模型引用数
            int particleRefCount = group.duplicates.Sum(d => d.refs.Count(r => r.IsParticleRef));
            int meshRefCount = group.duplicates.Sum(d => d.refs.Count(r => r.IsMeshRef));
            string typeSummary = (particleRefCount > 0 ? $"{particleRefCount}粒子" : "") +
                                 (particleRefCount > 0 && meshRefCount > 0 ? "/" : "") +
                                 (meshRefCount > 0 ? $"{meshRefCount}模型" : "");

            string sourceName = group.sourceTexture != null ? Path.GetFileName(AssetDatabase.GetAssetPath(group.sourceTexture)) : "未知";

            bool newFoldout = EditorGUILayout.Foldout(groupFoldouts[groupIndex],
                $"重复分组: {sourceName} (哈希: {group.hash.Substring(0, Math.Min(12, group.hash.Length))}...)  [{dupCount}张重复, {refCount}个引用]",
                true);
            groupFoldouts[groupIndex] = newFoldout;

            // 始终显示简化的单行摘要（折叠时也能看到关键信息）
            DrawGroupSummaryRow(group);

            if (newFoldout)
            {
                EditorGUI.indentLevel++;

                // === 源贴图区域 ===
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField("源贴图（保留）", EditorStyles.centeredGreyMiniLabel);
                DrawSourceTextureItem(group.sourceTexture);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(4);

                // === 重复贴图区域（每张 + 使用它的引用列表）===
                int di = 0;
                foreach (var dupEntry in group.duplicates)
                {
                    if (dupEntry.texture == null) continue;

                    using (new UIHelper.ZebraScope(di))
                    {
                        EditorGUILayout.BeginVertical(GUI.skin.box);

                    // 重复贴图标题行
                    EditorGUILayout.BeginHorizontal();
                    string dupPath = AssetDatabase.GetAssetPath(dupEntry.texture);
                    EditorGUILayout.ObjectField(dupEntry.texture, typeof(Texture2D), false,
                        GUILayout.Width(40), GUILayout.Height(40));
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(Path.GetFileName(dupPath), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"尺寸: {dupEntry.texture.width}x{dupEntry.texture.height}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"格式: {dupEntry.texture.format}", EditorStyles.miniLabel);
                    // 引用统计（粒子 + 模型）
                    int pCnt = dupEntry.refs.Count(r => r.IsParticleRef);
                    int mCnt = dupEntry.refs.Count(r => r.IsMeshRef);
                    string refSummary = (pCnt > 0 ? $"{pCnt}粒子" : "") +
                                        (pCnt > 0 && mCnt > 0 ? "/" : "") +
                                        (mCnt > 0 ? $"{mCnt}模型" : "") +
                                        (dupEntry.refs.Count == 0 ? "0引用" : "");
                    EditorGUILayout.LabelField($"被 {refSummary} 引用", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    // 该贴图的全局替换快捷按钮
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("全部替换为源贴图", GUILayout.Width(130)))
                    {
                        // 立即执行替换（不依赖二次点击"替换选中项"）
                        var refs = new List<TextureRef>(dupEntry.refs);
                        ReplaceDuplicateImmediate(refs, group.sourceTexture);
                        status = $"已将 {refs.Count} 个引用替换为源贴图";
                        // 在下一帧重新扫描以更新 UI 结果
                        EditorApplication.delayCall += () =>
                        {
                            ScanDuplicates(session);
                        };
                    }

                    // 全局 ObjectField 替换目标选择（该贴图的所有引用统一用这个）
                    Texture2D globalReplacement = GetGlobalReplacementForDuplicate(dupEntry.texturePath);
                    Texture2D newGlobalTex = EditorGUILayout.ObjectField(globalReplacement, typeof(Texture2D), false,
                        GUILayout.Width(100)) as Texture2D;
                    if (newGlobalTex != globalReplacement)
                    {
                        SetGlobalReplacementForDuplicate(dupEntry.texturePath, newGlobalTex, dupEntry.refs);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(4);

                    // === 引用列表（粒子系统 + 模型材质球，每个单独控制）===
                    if (dupEntry.refs.Count > 0)
                    {
                        EditorGUILayout.LabelField("使用此贴图的引用:", EditorStyles.centeredGreyMiniLabel);

                        // 分开显示粒子引用 与 模型引用，便于阅读
                        var particleRefs = dupEntry.refs.Where(r => r.IsParticleRef).ToList();
                        var meshRefs = dupEntry.refs.Where(r => r.IsMeshRef).ToList();

                        if (particleRefs.Count > 0)
                        {
                            EditorGUILayout.LabelField($"— 粒子系统 ({particleRefs.Count}) —", EditorStyles.miniBoldLabel);
                            foreach (var pRef in particleRefs)
                            {
                                DrawTextureRefItem(pRef, group.sourceTexture, dupEntry.texture);
                            }
                        }

                        if (meshRefs.Count > 0)
                        {
                            EditorGUILayout.LabelField($"— 模型材质球 ({meshRefs.Count}) —", EditorStyles.miniBoldLabel);
                            foreach (var mRef in meshRefs)
                            {
                                DrawTextureRefItem(mRef, group.sourceTexture, dupEntry.texture);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("未追踪到具体引用信息", MessageType.Warning);
                    }

                    EditorGUILayout.EndVertical();
                    }
                    GUILayout.Space(4);
                    di++;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
        }

        /// <summary>
        /// 折叠时显示的单行摘要
        /// </summary>
        private void DrawGroupSummaryRow(TextureGroup group)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // 源贴图小缩略
            if (group.sourceTexture != null)
            {
                EditorGUILayout.ObjectField(group.sourceTexture, typeof(Texture2D), false,
                    GUILayout.Width(32), GUILayout.Height(32));
            }

            // 每张重复贴图的小缩略 + 箭头
            foreach (var dup in group.duplicates)
            {
                if (dup.texture == null) continue;
                EditorGUILayout.LabelField("→", GUILayout.Width(16));
                EditorGUILayout.ObjectField(dup.texture, typeof(Texture2D), false,
                    GUILayout.Width(32), GUILayout.Height(32));

                // 显示涉及的引用数量（粒子 + 模型）
                int pCnt = dup.refs.Count(r => r.IsParticleRef);
                int mCnt = dup.refs.Count(r => r.IsMeshRef);
                string summary = (pCnt > 0 ? $"{pCnt}粒子" : "") +
                                 (pCnt > 0 && mCnt > 0 ? "/" : "") +
                                 (mCnt > 0 ? $"{mCnt}模型" : "");
                EditorGUILayout.LabelField($"({summary})", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceTextureItem(Texture2D tex)
        {
            if (tex == null) { EditorGUILayout.LabelField("(无效)"); return; }
            string path = AssetDatabase.GetAssetPath(tex);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(tex, typeof(Texture2D), false, GUILayout.Width(64), GUILayout.Height(64));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Path.GetFileName(path));
            EditorGUILayout.LabelField($"{tex.width}x{tex.height}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(tex.format.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制单个引用条目：粒子系统/模型材质球 + 引用详情 + 单独的替换控件
        /// </summary>
        private void DrawTextureRefItem(TextureRef pRef, Texture2D sourceTex, Texture2D dupTex)
        {
            if (pRef == null) return;
            if (pRef.IsParticleRef && pRef.particleSystem == null) return;
            if (pRef.IsMeshRef && pRef.renderer == null) return;

            EditorGUILayout.BeginHorizontal(GUI.skin.box);

            // 引用对象字段（粒子系统 或 模型渲染器）
            if (pRef.IsParticleRef)
            {
                EditorGUILayout.ObjectField(pRef.particleSystem, typeof(ParticleSystem), false, GUILayout.Width(160));
            }
            else
            {
                EditorGUILayout.ObjectField(pRef.renderer, typeof(Renderer), false, GUILayout.Width(160));
            }

            // 引用类型标签
            EditorGUILayout.LabelField(pRef.DisplayLabel, EditorStyles.miniLabel, GUILayout.Width(150));

            // 所属对象名
            string ownerName = pRef.ownerObject != null ? pRef.ownerObject.name : "?";
            EditorGUILayout.LabelField($"({ownerName})", EditorStyles.miniLabel, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            // 当前选择的替换目标
            string key = pRef.UniqueKey;
            if (!textureReplacements.ContainsKey(key))
                textureReplacements[key] = null; // null 表示未设置替换

            Texture2D currentReplacement = textureReplacements[key];

            // 替换目标选择
            Texture2D selected = EditorGUILayout.ObjectField(currentReplacement, typeof(Texture2D), false,
                GUILayout.Width(100)) as Texture2D;
            if (selected != currentReplacement)
                textureReplacements[key] = selected;

            // 快捷按钮：设为源贴图 / 清除
            if (GUILayout.Button("源贴图", GUILayout.Width(45)))
                textureReplacements[key] = sourceTex;
            if (GUILayout.Button("清除", GUILayout.Width(35)))
                textureReplacements[key] = null;

            EditorGUILayout.EndHorizontal();
        }

        // ----- 核心扫描逻辑（粒子贴图 + 模型材质贴图） -----
        private void ScanDuplicates(AnalysisSession session)
        {
            if (!AssetDatabase.IsValidFolder(sourceFolder))
            {
                EditorUtility.DisplayDialog("错误", "源文件夹路径无效。", "确定");
                return;
            }

            isProcessing = true;
            status = "扫描中...";
            progress = 0;
            textureGroups.Clear();
            textureReplacements.Clear();
            groupFoldouts.Clear();

            try
            {
                // 1. 收集源文件夹所有贴图
                var sourceTextures = GetTexturesInFolder(sourceFolder);
                if (sourceTextures.Count == 0)
                {
                    status = "源文件夹中没有贴图。";
                    isProcessing = false;
                    return;
                }

                // 2. 从当前分析会话中收集所有粒子系统 + 模型材质球使用的贴图（含引用归属信息）
                var textureMap = CollectTexturesWithRefs(session);
                if (textureMap.Count == 0)
                {
                    status = "当前分析对象中没有粒子系统或模型，且它们未使用贴图。";
                    isProcessing = false;
                    return;
                }

                // 3. 计算源贴图哈希 → path 映射
                var sourceHashes = new Dictionary<string, Texture2D>();      // hash → 源贴图
                var sourcePaths = new HashSet<string>();                     // 源贴图路径集合

                int total = sourceTextures.Count;
                int processed = 0;
                foreach (var tex in sourceTextures)
                {
                    if (!isProcessing) return;
                    processed++;
                    progress = (float)processed / (total + textureMap.Count) * 0.5f;
                    string hash = ComputeHash(tex);
                    if (!string.IsNullOrEmpty(hash) && !sourceHashes.ContainsKey(hash))
                    {
                        sourceHashes[hash] = tex;
                        sourcePaths.Add(AssetDatabase.GetAssetPath(tex));
                    }
                }

                // 4. 计算目标贴图哈希，并与源对比
                //    key=hash, value=(贴图, 引用列表)
                var targetHashes = new Dictionary<string, List<KeyValuePair<Texture2D, List<TextureRef>>>>();

                total = textureMap.Count;
                processed = 0;
                foreach (var kvp in textureMap)
                {
                    if (!isProcessing) return;
                    processed++;
                    progress = 0.5f + (float)processed / (total + sourceTextures.Count) * 0.5f;
                    string hash = ComputeHash(kvp.Key);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        if (!targetHashes.ContainsKey(hash))
                            targetHashes[hash] = new List<KeyValuePair<Texture2D, List<TextureRef>>>();
                        targetHashes[hash].Add(kvp);
                    }
                }

                // 5. 创建分组（修复：排除与源贴图是同一文件的情况）
                foreach (var kvp in targetHashes)
                {
                    if (!sourceHashes.ContainsKey(kvp.Key)) continue;

                    Texture2D srcTex = sourceHashes[kvp.Key];
                    string srcPath = AssetDatabase.GetAssetPath(srcTex);

                    var group = new TextureGroup
                    {
                        sourceTexture = srcTex,
                        sourcePath = srcPath,
                        hash = kvp.Key
                    };

                    foreach (var texAndRefs in kvp.Value)
                    {
                        Texture2D dupTex = texAndRefs.Key;
                        string dupPath = AssetDatabase.GetAssetPath(dupTex);

                        // ===== 核心修复：跳过与源贴图是同一文件的"重复"项 =====
                        if (string.Equals(srcPath, dupPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // 也检查是否已在其他分组中出现过的源贴图（避免 A→B 和 B→A 同时出现的情况）
                        // 这里不需要额外处理，因为 source 是固定的 reference folder 的

                        group.duplicates.Add(new DuplicateEntry
                        {
                            texture = dupTex,
                            texturePath = dupPath,
                            refs = texAndRefs.Value
                        });
                    }

                    // 只在有真正的不同文件重复时才添加分组
                    if (group.duplicates.Count > 0)
                        textureGroups.Add(group);
                }

                status = $"扫描完成！发现 {textureGroups.Count} 个重复分组。";
            }
            catch (Exception e)
            {
                Debug.LogError($"扫描出错: {e.Message}\n{e.StackTrace}");
                status = $"错误: {e.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// 收集所有粒子系统 与 模型材质球使用的贴图，并记录每个贴图被哪些对象、以什么方式引用
        /// 返回: Texture2D → List&lt;TextureRef&gt;
        /// </summary>
        private Dictionary<Texture2D, List<TextureRef>> CollectTexturesWithRefs(AnalysisSession session)
        {
            var result = new Dictionary<Texture2D, List<TextureRef>>();

            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;

                // ----- 粒子系统 -----
                var particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particleSystems)
                {
                    if (ps == null) continue;
                    CollectParticleRefs(go, ps, result);
                }

                // ----- 模型（MeshRenderer / SkinnedMeshRenderer 材质球） -----
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    // 粒子渲染器已在粒子部分单独处理，跳过避免重复
                    if (renderer is ParticleSystemRenderer) continue;
                    // 只处理模型渲染器
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    CollectRendererRefs(go, renderer, result);
                }
            }

            return result;
        }

        /// <summary>
        /// 收集单个粒子系统使用的贴图引用（渲染器材质 + TextureSheetAnimation）
        /// </summary>
        private void CollectParticleRefs(GameObject go, ParticleSystem ps,
            Dictionary<Texture2D, List<TextureRef>> result)
        {
            if (ps == null) return;

            // 1. 渲染器材质中的贴图
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.sharedMaterials != null)
            {
                for (int matIdx = 0; matIdx < renderer.sharedMaterials.Length; matIdx++)
                {
                    var mat = renderer.sharedMaterials[matIdx];
                    if (mat == null || mat.shader == null) continue;

                    int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int p = 0; p < propCount; p++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, p) == ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            string propName = ShaderUtil.GetPropertyName(mat.shader, p);
                            var tex = mat.GetTexture(propName) as Texture2D;
                            if (tex != null)
                            {
                                if (!result.ContainsKey(tex))
                                    result[tex] = new List<TextureRef>();
                                result[tex].Add(new TextureRef
                                {
                                    particleSystem = ps,
                                    ownerObject = go,
                                    refType = "ParticleMaterial",
                                    materialIndex = matIdx,
                                    propertyName = propName
                                });
                            }
                        }
                    }
                }
            }

            // 2. TextureSheetAnimation 模块
            var texSheet = ps.textureSheetAnimation;
            if (texSheet.enabled)
            {
                if (texSheet.mode == ParticleSystemAnimationMode.Sprites)
                {
                    var so = new SerializedObject(ps);
                    var sheetProp = so.FindProperty("m_TextureSheetAnimation");
                    var spritesProp = sheetProp.FindPropertyRelative("m_Sprites");
                    for (int i = 0; i < spritesProp.arraySize; i++)
                    {
                        var sprite = spritesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                        if (sprite != null && sprite.texture != null)
                        {
                            Texture2D tex = sprite.texture;
                            if (!result.ContainsKey(tex))
                                result[tex] = new List<TextureRef>();
                            result[tex].Add(new TextureRef
                            {
                                particleSystem = ps,
                                ownerObject = go,
                                refType = "ParticleSprite",
                                spriteIndex = i
                            });
                        }
                    }
                }
                else // Texture 模式
                {
                    var so = new SerializedObject(ps);
                    var sheetProp = so.FindProperty("m_TextureSheetAnimation");
                    var texProp = sheetProp?.FindPropertyRelative("m_Texture");
                    var tex = texProp?.objectReferenceValue as Texture2D;
                    if (tex != null)
                    {
                        if (!result.ContainsKey(tex))
                            result[tex] = new List<TextureRef>();
                        result[tex].Add(new TextureRef
                        {
                            particleSystem = ps,
                            ownerObject = go,
                            refType = "ParticleTex"
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 收集单个模型渲染器（MeshRenderer / SkinnedMeshRenderer）材质球中的贴图引用
        /// </summary>
        private void CollectRendererRefs(GameObject go, Renderer renderer,
            Dictionary<Texture2D, List<TextureRef>> result)
        {
            if (renderer == null || renderer.sharedMaterials == null) return;

            for (int matIdx = 0; matIdx < renderer.sharedMaterials.Length; matIdx++)
            {
                var mat = renderer.sharedMaterials[matIdx];
                if (mat == null || mat.shader == null) continue;

                int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                for (int p = 0; p < propCount; p++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, p) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(mat.shader, p);
                        var tex = mat.GetTexture(propName) as Texture2D;
                        if (tex != null)
                        {
                            if (!result.ContainsKey(tex))
                                result[tex] = new List<TextureRef>();
                            result[tex].Add(new TextureRef
                            {
                                renderer = renderer,
                                ownerObject = go,
                                refType = "MeshMaterial",
                                materialIndex = matIdx,
                                propertyName = propName
                            });
                        }
                    }
                }
            }
        }

        // ----- 通用工具方法 -----
        private List<Texture2D> GetTexturesInFolder(string folder)
        {
            var list = new List<Texture2D>();
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (skipUnityTextures && IsUnityBuiltinTexture(path)) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) list.Add(tex);
            }
            return list;
        }

        private string ComputeHash(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return null;

            if (usePixelHash)
            {
                if (!tex.isReadable) return null;
                try
                {
                    var pixels = tex.GetPixels32();
                    using (var md5 = MD5.Create())
                    {
                        byte[] data = new byte[pixels.Length * 4];
                        Buffer.BlockCopy(pixels, 0, data, 0, data.Length);
                        byte[] hash = md5.ComputeHash(data);
                        return BitConverter.ToString(hash);
                    }
                }
                catch { return null; }
            }
            else
            {
                try
                {
                    using (var md5 = MD5.Create())
                    {
                        byte[] fileData = File.ReadAllBytes(path);
                        byte[] hash = md5.ComputeHash(fileData);
                        return BitConverter.ToString(hash);
                    }
                }
                catch { return null; }
            }
        }

        private bool IsUnityBuiltinTexture(string path)
        {
            return path.Contains("Resources/unity_builtin_extra") ||
                   path.Contains("Library/") ||
                   path.Contains("DefaultResources") ||
                   path.Contains("Built-in");
        }

        // ----- 全局替换辅助方法（针对某张贴图的所有引用统一操作）-----

        private Texture2D GetGlobalReplacementForDuplicate(string texturePath)
        {
            // 从当前分组中寻找该贴图，返回任一已设置的替换值作为全局代表
            foreach (var group in textureGroups)
            {
                foreach (var dup in group.duplicates)
                {
                    if (string.Equals(dup.texturePath, texturePath, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var r in dup.refs)
                        {
                            if (textureReplacements.TryGetValue(r.UniqueKey, out var t) && t != null)
                                return t;
                        }
                    }
                }
            }
            return null; // 未设置全局值时返回 null
        }

        private void SetGlobalReplacementForDuplicate(string texturePath, Texture2D newTex, List<TextureRef> refs)
        {
            foreach (var pRef in refs)
                textureReplacements[pRef.UniqueKey] = newTex;
        }

        // ----- 立即替换单个重复条目（由「全部替换为源贴图」按钮触发）-----
        /// <summary>
        /// 立即将该重复贴图的所有引用（粒子 + 模型材质球）替换为源贴图，并录制 Undo。
        /// 不依赖 textureReplacements 字典，直接修改材质和粒子系统。
        /// </summary>
        private void ReplaceDuplicateImmediate(List<TextureRef> refs, Texture2D sourceTexture)
        {
            if (refs.Count == 0 || sourceTexture == null) return;

            // 录制 Undo：所有受影响的对象
            var affectedGOs = refs
                .Where(r => r.ownerObject != null)
                .Select(r => r.ownerObject)
                .Distinct()
                .ToList();

            foreach (var go in affectedGOs)
                Undo.RecordObject(go, "Texture Deduplication");

            // ----- 处理粒子引用 -----
            var particleRefs = refs.Where(r => r.IsParticleRef && r.particleSystem != null).ToList();
            var byPs = particleRefs.GroupBy(r => r.particleSystem);

            foreach (var psGroup in byPs)
            {
                var ps = psGroup.Key;
                if (ps == null) continue;

                // 录制粒子系统渲染器的 Undo
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    Undo.RecordObject(renderer, "Texture Deduplication");

                // 构建各类替换列表
                var matRefs = psGroup
                    .Where(r => r.refType == "ParticleMaterial")
                    .Select(r => new KeyValuePair<TextureRef, Texture2D>(r, sourceTexture))
                    .ToList();

                var spriteRefs = psGroup
                    .Where(r => r.refType == "ParticleSprite")
                    .Select(r => new KeyValuePair<TextureRef, Texture2D>(r, sourceTexture))
                    .ToList();

                var texRefs = psGroup
                    .Where(r => r.refType == "ParticleTex")
                    .Select(r => new KeyValuePair<TextureRef, Texture2D>(r, sourceTexture))
                    .ToList();

                ProcessMaterialReplacements(renderer, matRefs);
                ProcessTextureSheetReplacements(ps, spriteRefs, texRefs);
            }

            // ----- 处理模型材质球引用 -----
            var meshRefs = refs.Where(r => r.IsMeshRef && r.renderer != null).ToList();
            var byRenderer = meshRefs.GroupBy(r => r.renderer);

            foreach (var rendererGroup in byRenderer)
            {
                var renderer = rendererGroup.Key;
                if (renderer == null) continue;

                Undo.RecordObject(renderer, "Texture Deduplication");

                var matRefs = rendererGroup
                    .Select(r => new KeyValuePair<TextureRef, Texture2D>(r, sourceTexture))
                    .ToList();

                ProcessMaterialReplacements(renderer, matRefs);
            }

            AssetDatabase.SaveAssets();
        }

        // ----- 替换逻辑 -----
        private void ReplaceSelected(AnalysisSession session)
        {
            // 过滤出有效替换（非 null 的项）
            var validReplacements = new List<KeyValuePair<TextureRef, Texture2D>>();
            foreach (var kvp in textureReplacements)
            {
                if (kvp.Value == null) continue;

                // 反查 TextureRef
                var pRef = FindRefByKey(session, kvp.Key);
                if (pRef != null)
                    validReplacements.Add(new KeyValuePair<TextureRef, Texture2D>(pRef, kvp.Value));
            }

            if (validReplacements.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有选择需要替换的项目。请为引用勾选替换目标贴图后再执行。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认替换",
                $"将对 {validReplacements.Count} 个引用执行贴图替换。\n注意：会修改材质和粒子系统的贴图引用。\n\n确定继续？", "确定", "取消"))
                return;

            isProcessing = true;
            status = "替换中...";
            progress = 0;

            try
            {
                int total = validReplacements.Count;
                int idx = 0;

                // ----- 粒子引用 -----
                var particleRepls = validReplacements.Where(r => r.Key.IsParticleRef).ToList();
                var byParticleSystem = particleRepls.GroupBy(r => r.Key.particleSystem);

                foreach (var psGroup in byParticleSystem)
                {
                    if (!isProcessing) break;

                    var ps = psGroup.Key;
                    if (ps == null) continue;

                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer == null) continue;

                    // 处理材质类引用
                    var matReplacements = psGroup.Where(r => r.Key.refType == "ParticleMaterial").ToList();
                    ProcessMaterialReplacements(renderer, matReplacements);

                    // 处理 TextureSheetAnimation 类引用
                    var sheetSpriteReplacements = psGroup.Where(r => r.Key.refType == "ParticleSprite").ToList();
                    var sheetTexReplacements = psGroup.Where(r => r.Key.refType == "ParticleTex").ToList();
                    ProcessTextureSheetReplacements(ps, sheetSpriteReplacements, sheetTexReplacements);

                    idx++;
                    progress = (float)idx / total;
                }

                // ----- 模型材质球引用 -----
                var meshRepls = validReplacements.Where(r => r.Key.IsMeshRef).ToList();
                var byRenderer = meshRepls.GroupBy(r => r.Key.renderer);

                foreach (var rendererGroup in byRenderer)
                {
                    if (!isProcessing) break;

                    var renderer = rendererGroup.Key;
                    if (renderer == null) continue;

                    ProcessMaterialReplacements(renderer, rendererGroup.ToList());

                    idx++;
                    progress = (float)idx / total;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                status = $"替换完成！共处理 {validReplacements.Count} 个引用。";
                EditorUtility.DisplayDialog("完成", $"贴图替换完成，共 {validReplacements.Count} 处引用已更新。", "确定");

                // 重新扫描以更新结果
                ScanDuplicates(session);
            }
            catch (Exception e)
            {
                Debug.LogError($"替换出错: {e.Message}\n{e.StackTrace}");
                status = $"错误: {e.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// 处理渲染器（粒子渲染器 / MeshRenderer / SkinnedMeshRenderer）材质中的贴图替换
        /// </summary>
        private void ProcessMaterialReplacements(Renderer renderer, List<KeyValuePair<TextureRef, Texture2D>> replacements)
        {
            if (replacements.Count == 0 || renderer == null) return;

            var mats = renderer.sharedMaterials;
            bool anyChanged = false;

            // 按 materialIndex 分组
            var byMatIdx = replacements.GroupBy(r => r.Key.materialIndex);
            foreach (var matGroup in byMatIdx)
            {
                int matIdx = matGroup.Key;
                if (matIdx < 0 || matIdx >= mats.Length) continue;

                var mat = mats[matIdx];
                if (mat == null || mat.shader == null) continue;

                bool matChanged = false;
                foreach (var repl in matGroup)
                {
                    string propName = repl.Key.propertyName;
                    Texture2D oldTex = mat.GetTexture(propName) as Texture2D;
                    if (oldTex != null && oldTex != repl.Value)
                    {
                        if (!matChanged)
                        {
                            // 首次修改时克隆材质并保存为 asset
                            mat = new Material(mat);
                            string originalPath = AssetDatabase.GetAssetPath(mats[matIdx]);
                            string cloneDir = "Assets/DeduplicatedMaterials";
                            string cloneName = (mats[matIdx] != null ? mats[matIdx].name : "material") + "_dedup.mat";
                            string clonePath;
                            if (!string.IsNullOrEmpty(originalPath))
                            {
                                // 保存在原始材质同目录下，避免路径问题
                                cloneDir = Path.GetDirectoryName(originalPath);
                                cloneName = Path.GetFileNameWithoutExtension(originalPath) + "_dedup.mat";
                            }
                            if (!AssetDatabase.IsValidFolder(cloneDir))
                            {
                                // 逐级创建目录
                                string parent = Path.GetDirectoryName(cloneDir);
                                string folder = Path.GetFileName(cloneDir);
                                if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                                    AssetDatabase.CreateFolder("Assets", "DeduplicatedMaterials");
                                else if (!AssetDatabase.IsValidFolder(cloneDir))
                                    AssetDatabase.CreateFolder(parent, folder);
                            }
                            clonePath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(cloneDir, cloneName));
                            AssetDatabase.CreateAsset(mat, clonePath);
                            EditorUtility.SetDirty(mat);

                            mats[matIdx] = mat;
                            matChanged = true;
                            anyChanged = true;
                        }
                        mat.SetTexture(propName, repl.Value);
                        EditorUtility.SetDirty(mat);
                    }
                }
            }

            if (anyChanged)
                renderer.sharedMaterials = mats;
        }

        /// <summary>
        /// 处理 TextureSheetAnimation 中的贴图替换（仅粒子系统）
        /// </summary>
        private void ProcessTextureSheetReplacements(ParticleSystem ps,
            List<KeyValuePair<TextureRef, Texture2D>> spriteReplacements,
            List<KeyValuePair<TextureRef, Texture2D>> texReplacements)
        {
            if ((spriteReplacements.Count == 0 && texReplacements.Count == 0) || ps == null) return;

            var so = new SerializedObject(ps);
            var sheetProp = so.FindProperty("m_TextureSheetAnimation");

            // Sprites 模式
            if (spriteReplacements.Count > 0)
            {
                var spritesProp = sheetProp.FindPropertyRelative("m_Sprites");
                bool changed = false;

                foreach (var repl in spriteReplacements)
                {
                    int sprIdx = repl.Key.spriteIndex;
                    if (sprIdx < 0 || sprIdx >= spritesProp.arraySize) continue;

                    var sprite = spritesProp.GetArrayElementAtIndex(sprIdx).objectReferenceValue as Sprite;
                    if (sprite == null) continue;

                    // 尝试从新贴图中找同名 Sprite
                    string spriteName = sprite.name;
                    var newTex = repl.Value;
                    string newPath = AssetDatabase.GetAssetPath(newTex);
                    var newSprites = AssetDatabase.LoadAllAssetsAtPath(newPath).OfType<Sprite>().ToList();
                    var newSprite = newSprites.FirstOrDefault(s => s.name == spriteName) ?? newSprites.FirstOrDefault();

                    if (newSprite != null)
                    {
                        spritesProp.GetArrayElementAtIndex(sprIdx).objectReferenceValue = newSprite;
                        changed = true;
                    }
                }

                if (changed)
                    so.ApplyModifiedProperties();
            }

            // Texture 模式
            if (texReplacements.Count > 0)
            {
                var textureProp = sheetProp?.FindPropertyRelative("m_Texture");
                if (textureProp != null)
                {
                    // 取最后一个 tex 类型的替换目标（通常只有一个）
                    var lastTexRepl = texReplacements.LastOrDefault();
                    if (lastTexRepl.Value != null)
                    {
                        textureProp.objectReferenceValue = lastTexRepl.Value;
                        so.ApplyModifiedProperties();
                    }
                }
            }
        }

        /// <summary>
        /// 通过 UniqueKey 反查 TextureRef
        /// </summary>
        private TextureRef FindRefByKey(AnalysisSession session, string uniqueKey)
        {
            // 在所有分组的所有 duplicate entry 中搜索
            foreach (var group in textureGroups)
            {
                foreach (var dup in group.duplicates)
                {
                    foreach (var pRef in dup.refs)
                    {
                        if (pRef.UniqueKey == uniqueKey)
                            return pRef;
                    }
                }
            }
            return null;
        }

        private void ClearResults()
        {
            textureGroups.Clear();
            textureReplacements.Clear();
            groupFoldouts.Clear();
            status = "结果已清除";
        }

        // ----- 紧急修复：将已替换但未保存的内存材质写入磁盘 -----
        /// <summary>
        /// 扫描当前会话中所有渲染器（粒子 + 模型）的材质引用，将无 asset 路径的内存材质保存到磁盘。
        /// 用于修复旧版本替换逻辑遗留的"替换后无法保存到 Project"问题。
        /// </summary>
        private void FixUnsavedMaterials(AnalysisSession session)
        {
            var unsavedMats = new HashSet<Material>();

            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var mat = renderer.sharedMaterials[i];
                        if (mat == null) continue;

                        // 检查是否为内存材质（无 asset 路径）
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))
                            unsavedMats.Add(mat);
                    }
                }
            }

            if (unsavedMats.Count == 0)
            {
                EditorUtility.DisplayDialog("检查完成", "未发现需要修复的内存材质。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("修复未保存材质",
                $"发现 {unsavedMats.Count} 个内存中的材质（缺少磁盘文件）。\n\n将把它们保存到 Assets/DeduplicatedMaterials/ 目录。\n\n确定继续？", "确定", "取消"))
                return;

            // 确保目录存在
            if (!AssetDatabase.IsValidFolder("Assets/DeduplicatedMaterials"))
                AssetDatabase.CreateFolder("Assets", "DeduplicatedMaterials");

            int savedCount = 0;
            foreach (var mat in unsavedMats)
            {
                // 用渲染器/材质名生成文件名
                string matName = mat.name;
                if (string.IsNullOrEmpty(matName)) matName = "UnnamedMaterial";

                string savePath = AssetDatabase.GenerateUniqueAssetPath(
                    $"Assets/DeduplicatedMaterials/{matName}.mat");
                AssetDatabase.CreateAsset(mat, savePath);
                EditorUtility.SetDirty(mat);
                savedCount++;
            }

            // 刷新以确保引用正确
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("修复完成",
                $"已将 {savedCount} 个内存材质保存到 Assets/DeduplicatedMaterials/。\n\n现在可以正常保存场景/Prefab 到 Project 了。", "确定");
            status = $"已修复 {savedCount} 个未保存材质";
        }
    }
}
