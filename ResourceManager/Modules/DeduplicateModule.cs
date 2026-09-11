using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;

namespace ResourceManager.Modules
{
    /// <summary>
    /// 去重模块
    ///   1) 贴图去重：按「材质」分组，展开显示每一对「重复贴图 → 原贴图」；
    ///      替换时每个材质只克隆一次并一次性换掉其所有贴图（避免多次克隆出多个 _dedup）。
    ///   2) 材质球去重：把一个对象层级里「参数完全一致」的材质合并成同一个材质（可回档）。
    /// </summary>
    public class DeduplicateModule : IMultiObjectModule
    {
        // 全局搜索过滤
        public string SearchFilter = "";

        private const string DedupSuffix = "_dedup";

        // 子标签
        private int subTabIndex = 0;
        private static readonly string[] subTabNames = { "贴图去重", "材质球去重" };

        // ================= 贴图去重 =================
        private string sourceFolder = "Assets";
        private bool usePixelHash = false;
        private bool skipUnityTextures = true;
        private bool showAdvanced = false;
        private bool squareTextureField = true;   // 贴图资源框：true=方形框，false=长条框
        private List<TextureDedupGroup> textureGroups = new List<TextureDedupGroup>();
        private bool textureScanDone = false;
        private Vector2 textureScrollPos;
        private string textureStatus = "就绪";

        // ================= 材质球去重 =================
        private List<MaterialDedupGroup> materialGroups = new List<MaterialDedupGroup>();
        private bool materialScanDone = false;
        private Vector2 materialScrollPos;
        private string materialStatus = "就绪";

        // =====================================================================
        //  数据结构
        // =====================================================================

        /// <summary>一处对材质的引用（渲染器 + 材质槽）</summary>
        private class MatRef
        {
            public GameObject ownerObject;
            public Renderer renderer;
            public int materialIndex;
        }

        /// <summary>材质内一对「重复贴图 → 原贴图」</summary>
        private class TexturePair
        {
            public string label;           // 显示名（属性名 / 纹理帧）
            public string refKind;         // "Material" | "ParticleSprite" | "ParticleTex"
            public string propertyName;    // Material 的 Shader 属性名
            public int spriteIndex;        // ParticleSprite 的精灵索引
            public Texture2D currentTex;
            public string currentPath;
            public Texture2D sourceTex;
            public string sourcePath;
            public bool enabled = true;
        }

        /// <summary>贴图去重分组：一个材质（或一个粒子系统），内含若干对贴图</summary>
        private class TextureDedupGroup
        {
            public string kind;            // "Material" | "Particle"
            public Material mat;
            public string matPath;
            public ParticleSystem ps;
            public string containerName;
            public List<TexturePair> pairs = new List<TexturePair>();
            public List<MatRef> refs = new List<MatRef>();
            public bool selected = false;
            public bool expanded = false;
        }

        /// <summary>材质球去重：组内一个材质及其引用</summary>
        private class MaterialEntry
        {
            public Material mat;
            public string path;
            public List<MatRef> refs = new List<MatRef>();
            public bool animated;      // 是否被动画 K 帧驱动
        }

        /// <summary>材质球去重分组：参数完全一致的一组材质</summary>
        private class MaterialDedupGroup
        {
            public List<MaterialEntry> entries = new List<MaterialEntry>();
            public int keepIndex = 0;      // 保留哪一个（entries 下标）
            public bool selected = false;
            public bool expanded = false;
        }

        // =====================================================================
        //  入口
        // =====================================================================
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

        public void Clear()
        {
            textureGroups.Clear();
            textureScanDone = false;
            textureStatus = "就绪";
            materialGroups.Clear();
            materialScanDone = false;
            materialStatus = "就绪";
        }

        // =====================================================================
        //  贴图去重（按材质分组）
        // =====================================================================
        private void DrawTextureDedupTab(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("贴图去重（按材质分组，与源文件夹对比）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "以【材质】为单位显示：展开后可看到该材质里每一对「重复贴图 → 原贴图」。\n" +
                "替换时每个材质只克隆一次（生成一个 _dedup），并一次性替换它所有勾选的贴图，避免多次克隆。\n" +
                "替换前会生成回档文件，可在「拓展 → 回档」中还原。",
                MessageType.Info);

            // 源文件夹
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

            EditorGUILayout.BeginHorizontal();
            skipUnityTextures = EditorGUILayout.Toggle("跳过Unity内置贴图", skipUnityTextures, GUILayout.Width(170));
            GUILayout.FlexibleSpace();
            squareTextureField = EditorGUILayout.ToggleLeft("方形资源框", squareTextureField, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "高级选项");
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                usePixelHash = EditorGUILayout.Toggle("像素哈希模式（慢但精确）", usePixelHash);
                if (usePixelHash)
                    EditorGUILayout.HelpBox("像素模式需要贴图开启 Read/Write，否则会跳过。", MessageType.Warning);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描重复贴图（按材质分组）", GUILayout.Height(28)))
            {
                ScanTextureDuplicates(session);
            }
            if (GUILayout.Button("清除结果", GUILayout.Height(28)))
            {
                textureGroups.Clear();
                textureScanDone = false;
                textureStatus = "就绪";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", GUILayout.Height(24)))
                SetAllTextureSelected(true);
            if (GUILayout.Button("取消全选", GUILayout.Height(24)))
                SetAllTextureSelected(false);
            EditorGUI.BeginDisabledGroup(textureGroups.Count == 0);
            if (GUILayout.Button("替换选中项", GUILayout.Height(24)))
                ReplaceSelectedTextureGroups(session);
            if (GUILayout.Button("一键全部替换", GUILayout.Height(24)))
                ReplaceAllTextureGroups(session);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // 状态栏
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label($"状态: {textureStatus}");
            GUILayout.FlexibleSpace();
            if (textureScanDone)
            {
                int pairCount = textureGroups.Sum(g => g.pairs.Count);
                int selCount = textureGroups.Count(g => g.selected);
                GUILayout.Label($"材质 {textureGroups.Count} 个 / 可替换贴图 {pairCount} 处 / 选中 {selCount}");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (!textureScanDone)
            {
                GUILayout.Label("请执行扫描。", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (textureGroups.Count == 0)
            {
                GUILayout.Label("未发现可替换的重复贴图。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            textureScrollPos = EditorGUILayout.BeginScrollView(textureScrollPos, GUILayout.ExpandHeight(true));
            for (int i = 0; i < textureGroups.Count; i++)
            {
                DrawTextureGroup(textureGroups[i], i, session);
            }
            EditorGUILayout.EndScrollView();
        }

        private void SetAllTextureSelected(bool selected)
        {
            foreach (var g in textureGroups) g.selected = selected;
        }

        private void DrawTextureGroup(TextureDedupGroup g, int index, AnalysisSession session)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();

            // 批量选择
            g.selected = EditorGUILayout.Toggle(g.selected, GUILayout.Width(18));

            // 折叠 + 名称
            string title = g.kind == "Material"
                ? $"{g.containerName}  [{g.pairs.Count}个可替换贴图 / {g.refs.Count}处引用]"
                : $"{g.containerName}（粒子纹理帧）  [{g.pairs.Count}个可替换贴图]";
            g.expanded = EditorGUILayout.Foldout(g.expanded, title, true);

            GUILayout.FlexibleSpace();

            if (g.kind == "Material")
            {
                EditorGUILayout.ObjectField(g.mat, typeof(Material), false, GUILayout.Width(150));
                if (GUILayout.Button("定位", GUILayout.Width(45)))
                    LocateObject(g.mat);
            }
            else
            {
                EditorGUILayout.ObjectField(g.ps, typeof(ParticleSystem), false, GUILayout.Width(150));
                if (GUILayout.Button("定位", GUILayout.Width(45)))
                    LocateObject(g.ps);
            }

            // 单独替换（保留）
            if (GUILayout.Button("替换", GUILayout.Width(50)))
            {
                ReplaceTextureGroup(g, session);
            }

            EditorGUILayout.EndHorizontal();

            if (g.expanded)
            {
                EditorGUI.indentLevel++;
                for (int pi = 0; pi < g.pairs.Count; pi++)
                {
                    var p = g.pairs[pi];
                    using (new UIHelper.ZebraScope(pi))
                    {
                        EditorGUILayout.BeginHorizontal();

                        p.enabled = EditorGUILayout.Toggle(p.enabled, GUILayout.Width(18));
                        GUILayout.Label(p.label, GUILayout.Width(140));

                        EditorGUILayout.LabelField("重复:", EditorStyles.miniLabel, GUILayout.Width(32));
                        DrawTextureField(p.currentTex);

                        GUILayout.Label("→", GUILayout.Width(14));

                        EditorGUILayout.LabelField("原:", EditorStyles.miniLabel, GUILayout.Width(24));
                        DrawTextureField(p.sourceTex);

                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void ScanTextureDuplicates(AnalysisSession session)
        {
            textureGroups.Clear();
            textureScanDone = true;
            textureStatus = "扫描中...";

            if (!AssetDatabase.IsValidFolder(sourceFolder))
            {
                textureStatus = "源文件夹无效。";
                EditorUtility.DisplayDialog("错误", "源文件夹路径无效。", "确定");
                return;
            }

            // 1. 源贴图：hash -> 源贴图
            var sourceByHash = new Dictionary<string, Texture2D>();
            foreach (var t in GetTexturesInFolder(sourceFolder))
            {
                string h = ComputeHash(t);
                if (!string.IsNullOrEmpty(h) && !sourceByHash.ContainsKey(h))
                    sourceByHash[h] = t;
            }
            if (sourceByHash.Count == 0)
            {
                textureStatus = "源文件夹中没有贴图。";
                return;
            }

            // 2. 收集分析对象使用的材质及其引用
            var matRefs = new Dictionary<Material, List<MatRef>>();
            var matOrder = new List<Material>();

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
                        var m = mats[i];
                        if (m == null) continue;
                        if (!matRefs.TryGetValue(m, out var list))
                        {
                            list = new List<MatRef>();
                            matRefs[m] = list;
                            matOrder.Add(m);
                        }
                        list.Add(new MatRef { ownerObject = go, renderer = renderer, materialIndex = i });
                    }
                }
            }

            // 3. 每个材质：找出「与源文件夹重复」的贴图属性
            foreach (var mat in matOrder)
            {
                if (mat == null || mat.shader == null) continue;

                var group = new TextureDedupGroup
                {
                    kind = "Material",
                    mat = mat,
                    matPath = AssetDatabase.GetAssetPath(mat),
                    containerName = mat.name,
                    refs = matRefs[mat]
                };

                int n = ShaderUtil.GetPropertyCount(mat.shader);
                for (int i = 0; i < n; i++)
                {
                    if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    string prop = ShaderUtil.GetPropertyName(mat.shader, i);
                    var tex = mat.GetTexture(prop) as Texture2D;
                    if (tex == null) continue;

                    string h = ComputeHash(tex);
                    if (string.IsNullOrEmpty(h)) continue;
                    if (!sourceByHash.TryGetValue(h, out var src) || src == null) continue;

                    string cp = AssetDatabase.GetAssetPath(tex);
                    string sp = AssetDatabase.GetAssetPath(src);
                    if (string.IsNullOrEmpty(cp) || string.IsNullOrEmpty(sp)) continue;
                    if (string.Equals(cp, sp, StringComparison.OrdinalIgnoreCase)) continue;   // 同一文件

                    group.pairs.Add(new TexturePair
                    {
                        refKind = "Material",
                        label = prop,
                        propertyName = prop,
                        currentTex = tex,
                        currentPath = cp,
                        sourceTex = src,
                        sourcePath = sp
                    });
                }

                if (group.pairs.Count > 0) textureGroups.Add(group);
            }

            // 4. 粒子纹理帧（非材质，保留原有支持）
            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null) continue;

                    var group = new TextureDedupGroup
                    {
                        kind = "Particle",
                        ps = ps,
                        containerName = ps.name
                    };

                    var sheet = ps.textureSheetAnimation;
                    if (sheet.enabled)
                    {
                        var so = new SerializedObject(ps);
                        var sheetProp = so.FindProperty("m_TextureSheetAnimation");

                        if (sheet.mode == ParticleSystemAnimationMode.Sprites)
                        {
                            var spritesProp = sheetProp?.FindPropertyRelative("m_Sprites");
                            if (spritesProp != null)
                            {
                                for (int i = 0; i < spritesProp.arraySize; i++)
                                {
                                    var sprite = spritesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                                    if (sprite == null || sprite.texture == null) continue;
                                    var pair = BuildParticlePair(sprite.texture, sourceByHash,
                                        $"纹理帧[精灵:{i}]", "ParticleSprite", i);
                                    if (pair != null) group.pairs.Add(pair);
                                }
                            }
                        }
                        else
                        {
                            var texProp = sheetProp?.FindPropertyRelative("m_Texture");
                            var tex = texProp?.objectReferenceValue as Texture2D;
                            if (tex != null)
                            {
                                var pair = BuildParticlePair(tex, sourceByHash, "纹理帧[Tex模式]", "ParticleTex", -1);
                                if (pair != null) group.pairs.Add(pair);
                            }
                        }
                    }

                    if (group.pairs.Count > 0) textureGroups.Add(group);
                }
            }

            textureGroups = textureGroups
                .OrderByDescending(g => g.pairs.Count)
                .ThenBy(g => g.containerName)
                .ToList();

            textureStatus = $"扫描完成：{textureGroups.Count} 个材质/对象含重复贴图。";
        }

        private TexturePair BuildParticlePair(Texture2D tex, Dictionary<string, Texture2D> sourceByHash,
            string label, string refKind, int spriteIndex)
        {
            string h = ComputeHash(tex);
            if (string.IsNullOrEmpty(h)) return null;
            if (!sourceByHash.TryGetValue(h, out var src) || src == null) return null;

            string cp = AssetDatabase.GetAssetPath(tex);
            string sp = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(cp) || string.IsNullOrEmpty(sp)) return null;
            if (string.Equals(cp, sp, StringComparison.OrdinalIgnoreCase)) return null;

            return new TexturePair
            {
                refKind = refKind,
                label = label,
                spriteIndex = spriteIndex,
                currentTex = tex,
                currentPath = cp,
                sourceTex = src,
                sourcePath = sp
            };
        }

        private void ReplaceSelectedTextureGroups(AnalysisSession session)
        {
            var targets = textureGroups.Where(g => g.selected).ToList();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有选中任何材质。请先勾选（或用「全选」）。", "确定");
                return;
            }
            if (!EditorUtility.DisplayDialog("确认替换",
                $"将替换 {targets.Count} 个材质/对象中的重复贴图（每个材质只克隆一次）。\n会生成回档文件。\n\n确定继续？",
                "确定", "取消"))
                return;

            DoReplaceTextureGroups(targets, session);
        }

        private void ReplaceAllTextureGroups(AnalysisSession session)
        {
            if (textureGroups.Count == 0) return;
            if (!EditorUtility.DisplayDialog("确认替换",
                $"将替换全部 {textureGroups.Count} 个材质/对象中的重复贴图。\n会生成回档文件。\n\n确定继续？",
                "确定", "取消"))
                return;

            DoReplaceTextureGroups(textureGroups.ToList(), session);
        }

        private void DoReplaceTextureGroups(List<TextureDedupGroup> targets, AnalysisSession session)
        {
            // 回档快照（替换前）
            var snapshot = RollbackStore.Capture(session, "贴图去重");

            int matClones = 0;
            int pairCount = 0;

            foreach (var g in targets)
            {
                var enabled = g.pairs.Where(p => p.enabled && p.sourceTex != null).ToList();
                if (enabled.Count == 0) continue;

                if (g.kind == "Material")
                {
                    if (ReplaceInMaterial(g, enabled)) matClones++;
                }
                else
                {
                    ReplaceInParticle(g, enabled);
                }
                pairCount += enabled.Count;
                g.selected = false;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string snapPath = RollbackStore.Save(snapshot);
            string msg = $"已替换 {pairCount} 处贴图，克隆了 {matClones} 个材质。\n回档文件: {snapPath}";
            Debug.Log($"[贴图去重] {msg}");
            EditorUtility.DisplayDialog("替换完成", msg, "确定");

            // 重新扫描
            ScanTextureDuplicates(session);
        }

        private void ReplaceTextureGroup(TextureDedupGroup g, AnalysisSession session)
        {
            var enabled = g.pairs.Where(p => p.enabled && p.sourceTex != null).ToList();
            if (enabled.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有勾选可替换的贴图。", "确定");
                return;
            }

            var snapshot = RollbackStore.Capture(session, "贴图去重");

            if (g.kind == "Material")
                ReplaceInMaterial(g, enabled);
            else
                ReplaceInParticle(g, enabled);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string snapPath = RollbackStore.Save(snapshot);
            Debug.Log($"[贴图去重] {g.containerName}: 替换 {enabled.Count} 处贴图。回档文件: {snapPath}");

            ScanTextureDuplicates(session);
        }

        /// <summary>克隆该材质一次，把所有勾选的贴图属性一次性换掉，并重新指认。</summary>
        private bool ReplaceInMaterial(TextureDedupGroup g, List<TexturePair> enabled)
        {
            var mat = g.mat;
            if (mat == null) return false;

            // 克隆一次
            Material clone = new Material(mat);
            string dir = string.IsNullOrEmpty(g.matPath)
                ? "Assets/DeduplicatedMaterials"
                : Path.GetDirectoryName(g.matPath).Replace("\\", "/");
            if (string.IsNullOrEmpty(dir)) dir = "Assets/DeduplicatedMaterials";
            EnsureFolder(dir);

            string baseName = string.IsNullOrEmpty(g.matPath)
                ? mat.name
                : Path.GetFileNameWithoutExtension(g.matPath);
            baseName = StripDedupSuffix(baseName);
            string clonePath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}{DedupSuffix}.mat");
            AssetDatabase.CreateAsset(clone, clonePath);

            foreach (var p in enabled)
            {
                clone.SetTexture(p.propertyName, p.sourceTex);
            }
            EditorUtility.SetDirty(clone);

            // 重新指认所有引用该材质的渲染器
            int changed = 0;
            foreach (var r in g.refs)
            {
                if (r == null || r.renderer == null) continue;
                var mats = r.renderer.sharedMaterials;
                if (r.materialIndex < 0 || r.materialIndex >= mats.Length) continue;
                if (mats[r.materialIndex] != mat) continue;

                Undo.RecordObject(r.renderer, "贴图去重");
                mats[r.materialIndex] = clone;
                r.renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(r.renderer);
                changed++;
            }

            Debug.Log($"[贴图去重] 材质 {mat.name} → {Path.GetFileName(clonePath)}（替换 {enabled.Count} 处贴图，{changed} 处引用）");
            return true;
        }

        /// <summary>粒子纹理帧：直接改粒子系统属性（无材质克隆）。</summary>
        private void ReplaceInParticle(TextureDedupGroup g, List<TexturePair> enabled)
        {
            var ps = g.ps;
            if (ps == null) return;

            var so = new SerializedObject(ps);
            var sheetProp = so.FindProperty("m_TextureSheetAnimation");
            if (sheetProp == null) return;

            foreach (var p in enabled)
            {
                if (p.refKind == "ParticleTex")
                {
                    var texProp = sheetProp.FindPropertyRelative("m_Texture");
                    if (texProp != null) texProp.objectReferenceValue = p.sourceTex;
                }
                else // ParticleSprite
                {
                    var spritesProp = sheetProp.FindPropertyRelative("m_Sprites");
                    if (spritesProp == null) continue;
                    if (p.spriteIndex < 0 || p.spriteIndex >= spritesProp.arraySize) continue;

                    var oldSprite = spritesProp.GetArrayElementAtIndex(p.spriteIndex).objectReferenceValue as Sprite;
                    string spriteName = oldSprite != null ? oldSprite.name : null;
                    var newSprites = AssetDatabase.LoadAllAssetsAtPath(p.sourcePath).OfType<Sprite>().ToList();
                    var newSprite = newSprites.FirstOrDefault(s => s.name == spriteName) ?? newSprites.FirstOrDefault();
                    if (newSprite != null)
                        spritesProp.GetArrayElementAtIndex(p.spriteIndex).objectReferenceValue = newSprite;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ps);
            Debug.Log($"[贴图去重] 粒子 {ps.name}: 替换 {enabled.Count} 处纹理帧");
        }

        // =====================================================================
        //  材质球去重（参数完全一致 → 合并为同一个材质）
        // =====================================================================
        private void DrawMaterialDedupTab(AnalysisSession session)
        {
            GUILayout.Label("材质球去重（参数完全一致即合并）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "统计分析对象（预制体/物体）层级里的【全部材质】，只要参数完全一致就归为一组，可合并成同一个材质。\n" +
                "每组可下拉选择「保留哪一个」（默认优先不含 _dedup 的），右侧「定位」可在 Project 中高亮该材质。\n" +
                "被动画 K 帧驱动的材质不参与合并；替换前会生成回档文件。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描参数一致的材质", GUILayout.Height(28)))
            {
                ScanMaterialDuplicates(session);
            }
            if (GUILayout.Button("清除结果", GUILayout.Height(28)))
            {
                materialGroups.Clear();
                materialScanDone = false;
                materialStatus = "就绪";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", GUILayout.Height(24)))
                SetAllMaterialSelected(true);
            if (GUILayout.Button("取消全选", GUILayout.Height(24)))
                SetAllMaterialSelected(false);
            EditorGUI.BeginDisabledGroup(materialGroups.Count == 0);
            if (GUILayout.Button("合并选中项", GUILayout.Height(24)))
                ReplaceSelectedMaterialGroups(session);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label($"状态: {materialStatus}");
            GUILayout.FlexibleSpace();
            if (materialScanDone)
            {
                int selCount = materialGroups.Count(g => g.selected);
                GUILayout.Label($"分组 {materialGroups.Count} / 选中 {selCount}");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (!materialScanDone)
            {
                GUILayout.Label("请执行扫描。", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (materialGroups.Count == 0)
            {
                GUILayout.Label("未发现参数完全一致的材质。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            materialScrollPos = EditorGUILayout.BeginScrollView(materialScrollPos, GUILayout.ExpandHeight(true));
            for (int i = 0; i < materialGroups.Count; i++)
            {
                DrawMaterialDedupGroup(materialGroups[i], i, session);
            }
            EditorGUILayout.EndScrollView();
        }

        private void SetAllMaterialSelected(bool selected)
        {
            foreach (var g in materialGroups) g.selected = selected;
        }

        private void DrawMaterialDedupGroup(MaterialDedupGroup g, int index, AnalysisSession session)
        {
            using (new UIHelper.ZebraScope(index))
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();

                g.selected = EditorGUILayout.Toggle(g.selected, GUILayout.Width(18));

                int totalRefs = g.entries.Sum(e => e.refs.Count);
                g.expanded = EditorGUILayout.Foldout(g.expanded,
                    $"{g.entries.Count} 个材质参数一致（共 {totalRefs} 处引用）", true);

                GUILayout.FlexibleSpace();

                // 保留哪一个（下拉）
                GUILayout.Label("保留:", GUILayout.Width(36));
                string[] names = g.entries
                    .Select(e => e.mat != null ? e.mat.name : "(空)")
                    .ToArray();
                if (g.keepIndex < 0 || g.keepIndex >= names.Length) g.keepIndex = 0;
                int newKeep = EditorGUILayout.Popup(g.keepIndex, names, GUILayout.Width(180));
                if (newKeep != g.keepIndex)
                {
                    g.keepIndex = newKeep;
                    LocateObject(g.entries[g.keepIndex].mat);
                }

                if (GUILayout.Button("定位", GUILayout.Width(45)))
                    LocateObject(g.entries[g.keepIndex].mat);

                GUILayout.Space(6);

                bool canMerge = CanMerge(g);
                EditorGUI.BeginDisabledGroup(!canMerge);
                if (GUILayout.Button("合并到保留项", GUILayout.Width(100)))
                {
                    ReplaceMaterialGroup(g, session);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();

                if (!canMerge)
                {
                    EditorGUILayout.HelpBox("除保留项外没有被动画驱动以外的可替换材质（其余材质被动画 K 帧驱动，已排除）。", MessageType.Warning);
                }

                if (g.expanded)
                {
                    EditorGUI.indentLevel++;
                    for (int ei = 0; ei < g.entries.Count; ei++)
                    {
                        var e = g.entries[ei];
                        using (new UIHelper.ZebraScope(ei))
                        {
                            EditorGUILayout.BeginHorizontal();

                            bool isKeep = (ei == g.keepIndex);
                            GUILayout.Label(isKeep ? "★保留" : "　", GUILayout.Width(44));

                            EditorGUILayout.ObjectField(e.mat, typeof(Material), false, GUILayout.Width(160));
                            GUILayout.Label($"{e.refs.Count} 处引用", EditorStyles.miniLabel, GUILayout.Width(80));
                            GUILayout.Label(e.path ?? "", EditorStyles.miniLabel);

                            if (e.animated)
                                GUILayout.Label("[动画驱动·不参与]", EditorStyles.miniLabel, GUILayout.Width(110));

                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("定位", GUILayout.Width(45)))
                                LocateObject(e.mat);

                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }
        }

        private bool CanMerge(MaterialDedupGroup g)
        {
            if (g == null || g.entries.Count < 2) return false;
            if (g.keepIndex < 0 || g.keepIndex >= g.entries.Count) return false;
            return g.entries.Where((e, i) => i != g.keepIndex && !e.animated).Any();
        }

        private void ScanMaterialDuplicates(AnalysisSession session)
        {
            materialGroups.Clear();
            materialScanDone = true;
            materialStatus = "扫描中...";

            // 1. 收集分析对象层级里的所有材质及其引用
            var matRefs = new Dictionary<Material, List<MatRef>>();
            var order = new List<Material>();

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
                        var m = mats[i];
                        if (m == null) continue;
                        if (!matRefs.TryGetValue(m, out var list))
                        {
                            list = new List<MatRef>();
                            matRefs[m] = list;
                            order.Add(m);
                        }
                        list.Add(new MatRef { ownerObject = go, renderer = renderer, materialIndex = i });
                    }
                }
            }

            // 2. 按"参数签名"分组
            var bySig = new Dictionary<string, List<Material>>();
            var sigOrder = new List<string>();
            foreach (var m in order)
            {
                string sig = MaterialSignature(m);
                if (string.IsNullOrEmpty(sig)) continue;
                if (!bySig.TryGetValue(sig, out var list))
                {
                    list = new List<Material>();
                    bySig[sig] = list;
                    sigOrder.Add(sig);
                }
                list.Add(m);
            }

            // 3. 组装分组（>=2 个不同材质才算候选）
            foreach (var sig in sigOrder)
            {
                var mats = bySig[sig];
                if (mats.Count < 2) continue;

                var group = new MaterialDedupGroup();
                foreach (var m in mats)
                {
                    var refs = matRefs[m];
                    bool animated = refs.Any(r => IsObjectMaterialAnimated(r.ownerObject));
                    group.entries.Add(new MaterialEntry
                    {
                        mat = m,
                        path = AssetDatabase.GetAssetPath(m),
                        refs = refs,
                        animated = animated
                    });
                }

                group.keepIndex = PickDefaultKeepIndex(group.entries);
                materialGroups.Add(group);
            }

            materialGroups = materialGroups
                .OrderByDescending(g => g.entries.Count)
                .ThenBy(g => g.entries.Count > 0 && g.entries[0].mat != null ? g.entries[0].mat.name : "")
                .ToList();

            materialStatus = $"扫描完成：{materialGroups.Count} 组参数一致的材质。";
        }

        private int PickDefaultKeepIndex(List<MaterialEntry> entries)
        {
            int best = 0;
            int bestScore = int.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.mat == null) continue;

                int score = 0;
                bool hasDedup = !string.IsNullOrEmpty(e.mat.name) &&
                                e.mat.name.IndexOf(DedupSuffix, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasDedup) score += 1000;              // 优先不含 _dedup
                score += Mathf.Min(e.refs.Count, 500);     // 引用多者优先
                if (!e.animated) score += 100;             // 未被动画驱动者优先

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>计算材质的"参数签名"，签名相同即参数完全一致。</summary>
        private string MaterialSignature(Material m)
        {
            if (m == null || m.shader == null) return null;

            var sb = new StringBuilder(256);
            sb.Append(m.shader.name).Append('|').Append(m.renderQueue).Append('|');

            var kw = m.shaderKeywords;
            if (kw != null && kw.Length > 0)
                sb.Append(string.Join(",", kw.OrderBy(k => k)));
            sb.Append('|');

            int n = ShaderUtil.GetPropertyCount(m.shader);
            for (int i = 0; i < n; i++)
            {
                string p = ShaderUtil.GetPropertyName(m.shader, i);
                sb.Append(p).Append('=');
                switch (ShaderUtil.GetPropertyType(m.shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        var t = m.GetTexture(p);
                        sb.Append(t != null ? AssetDatabase.GetAssetPath(t) : "null");
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        sb.Append(m.GetColor(p).ToString("F6"));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        sb.Append(m.GetVector(p).ToString("F6"));
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        sb.Append(m.GetFloat(p).ToString("F6"));
                        break;
                    case ShaderUtil.ShaderPropertyType.Int:
                        sb.Append(m.GetInt(p));
                        break;
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private void ReplaceSelectedMaterialGroups(AnalysisSession session)
        {
            var targets = materialGroups.Where(g => g.selected && CanMerge(g)).ToList();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有可合并的选中分组（请勾选，或所选分组没有可替换材质）。", "确定");
                return;
            }
            if (!EditorUtility.DisplayDialog("确认合并",
                $"将合并 {targets.Count} 组参数一致的材质。\n会生成回档文件。\n\n确定继续？", "确定", "取消"))
                return;

            DoMergeMaterialGroups(targets, session);
        }

        private void ReplaceMaterialGroup(MaterialDedupGroup g, AnalysisSession session)
        {
            if (!CanMerge(g))
            {
                EditorUtility.DisplayDialog("提示", "该分组没有可合并的材质。", "确定");
                return;
            }
            DoMergeMaterialGroups(new List<MaterialDedupGroup> { g }, session);
        }

        private void DoMergeMaterialGroups(List<MaterialDedupGroup> targets, AnalysisSession session)
        {
            // 回档快照（合并前）
            var snapshot = RollbackStore.Capture(session, "材质球去重");

            int mergedMats = 0;
            int changedRefs = 0;

            foreach (var g in targets)
            {
                if (!CanMerge(g)) continue;

                var keep = g.entries[g.keepIndex];
                foreach (var e in g.entries)
                {
                    if (e == keep || e.animated) continue;

                    foreach (var r in e.refs)
                    {
                        if (r == null || r.renderer == null) continue;
                        var mats = r.renderer.sharedMaterials;
                        if (r.materialIndex < 0 || r.materialIndex >= mats.Length) continue;
                        if (mats[r.materialIndex] == keep.mat) continue;

                        Undo.RecordObject(r.renderer, "材质球去重");
                        mats[r.materialIndex] = keep.mat;
                        r.renderer.sharedMaterials = mats;
                        EditorUtility.SetDirty(r.renderer);
                        changedRefs++;
                    }
                    mergedMats++;
                }

                g.selected = false;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string snapPath = RollbackStore.Save(snapshot);
            string msg = $"已合并 {mergedMats} 个材质（{changedRefs} 处引用）。\n回档文件: {snapPath}";
            Debug.Log($"[材质球去重] {msg}");
            EditorUtility.DisplayDialog("合并完成", msg, "确定");

            ScanMaterialDuplicates(session);
        }

        // =====================================================================
        //  工具
        // =====================================================================

        private void LocateObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        /// <summary>贴图资源框：方形（默认）或长条。</summary>
        private void DrawTextureField(Texture2D tex)
        {
            if (squareTextureField)
                EditorGUILayout.ObjectField(tex, typeof(Texture2D), false, GUILayout.Width(56), GUILayout.Height(56));
            else
                EditorGUILayout.ObjectField(tex, typeof(Texture2D), false, GUILayout.Width(150));
        }

        private static string StripDedupSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.LastIndexOf(DedupSuffix, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return name.Substring(0, idx);
            return name;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)) return;
            assetFolder = assetFolder.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(assetFolder)) return;

            string parent = Path.GetDirectoryName(assetFolder);
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;

            parent = parent.Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(assetFolder))
                AssetDatabase.CreateFolder(parent, name);
        }

        // ----- 动画检测（材质是否被 K 帧驱动）-----

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

        // ----- 贴图通用工具 -----

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
    }
}
