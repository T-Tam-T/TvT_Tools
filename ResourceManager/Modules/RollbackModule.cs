using UnityEngine;
using UnityEditor;
using ResourceManager.Core;
using ResourceManager.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Modules
{
    /// <summary>
    /// 回档模块（独立标签页）。
    /// 读取 Assets/ResourceManagerRollback/ 下的回档文件，对比「当前资源 → 回档资源」并还原。
    /// 回档文件来源：「资源复制」与「材质球去重」。
    /// </summary>
    public class RollbackModule : IMultiObjectModule
    {
        // 全局搜索过滤（预留）
        public string SearchFilter = "";

        private Vector2 scrollPos;
        private int selectedSnapshotIndex = -1;

        /// <summary>回档对比行：一次“当前资源 → 回档资源”的变更。</summary>
        private class RollbackDiffRow
        {
            public string objectName;
            public string slotLabel;
            public string currentPath;
            public string beforePath;
            public string kind;            // material / mesh / skinnedMesh / texture
            public string componentPath;   // material/mesh 的组件相对路径
            public int materialIndex;      // material 的槽位
            public string materialPath;    // texture 的材质路径
            public string propertyName;    // texture 的属性名
        }

        public void DrawMultiObject(AnalysisSession session, ResourceCache cache)
        {
            GUILayout.Label("回档（资源还原）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "回档文件由「资源复制」和「材质球去重」在修改引用前自动生成，位于 Assets/ResourceManagerRollback/。\n" +
                "选择一个回档日期，即可查看「当前资源 → 回档资源」的对比；点击下方「确认回档」进行还原。\n" +
                "若历史资源已被删除，将无法还原并会在 Console 提示。",
                MessageType.Info);

            if (session.Analyzers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先在左侧「对象列表」中添加并分析对象，然后再回档。", MessageType.Info);
                return;
            }

            var snapshots = RollbackStore.LoadAll();

            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "暂无回档记录。执行一次「复制 → 资源复制（并重新指认）」或「去重 → 材质球去重」后会生成回档文件。",
                    MessageType.Info);
                return;
            }

            // ---- 回档日期下拉（所有快照，最新在前）----
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("回档日期:", GUILayout.Width(70));
            if (selectedSnapshotIndex < 0) selectedSnapshotIndex = 0;
            if (selectedSnapshotIndex >= snapshots.Count) selectedSnapshotIndex = 0;

            string[] dateStrings = snapshots.Select(s =>
            {
                string t = RollbackStore.FormatTimestamp(s.Value.timestamp);
                return string.IsNullOrEmpty(s.Value.source) ? t : $"{t} · {s.Value.source}";
            }).ToArray();
            selectedSnapshotIndex = EditorGUILayout.Popup(selectedSnapshotIndex, dateStrings, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            var selected = snapshots[selectedSnapshotIndex];

            EditorGUILayout.Space(6);

            // ---- 变更对比列表（仅显示真正发生变化的引用）----
            var diffRows = BuildRollbackDiffRows(session, selected.Value);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

            if (diffRows.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "所选回档日期与当前分析对象没有匹配的变更。\n" +
                    "可能该对象未参与这次修改，或当前资源已与回档一致。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"共 {diffRows.Count} 处变更。点击下方「确认回档」会把「当前资源」还原为「回档资源」。", MessageType.Info);

                int zebra = 0;
                foreach (var objGroup in diffRows.GroupBy(r => r.objectName))
                {
                    EditorGUILayout.LabelField($"— {objGroup.Key} —", EditorStyles.boldLabel);

                    foreach (var row in objGroup)
                    {
                        using (new UIHelper.ZebraScope(zebra))
                        {
                            EditorGUILayout.BeginVertical("box");
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label(row.slotLabel, GUILayout.Width(150));
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField("当前:", EditorStyles.miniLabel, GUILayout.Width(30));
                            EditorGUILayout.SelectableLabel(row.currentPath, EditorStyles.miniLabel, GUILayout.Width(160));
                            GUILayout.Label("→", GUILayout.Width(14));
                            EditorGUILayout.LabelField("回档:", EditorStyles.miniLabel, GUILayout.Width(30));
                            EditorGUILayout.SelectableLabel(row.beforePath, EditorStyles.miniLabel, GUILayout.Width(160));
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.EndVertical();
                        }
                        zebra++;
                    }
                    EditorGUILayout.Space(4);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // ---- 确认回档按钮 ----
            if (GUILayout.Button("确认回档", GUILayout.Height(32)))
            {
                bool allOk = RollbackStore.Restore(session, selected.Value, selected.Key);
                EditorUtility.DisplayDialog(
                    "回档完成",
                    allOk
                        ? $"已将匹配对象还原到 {RollbackStore.FormatTimestamp(selected.Value.timestamp)}。"
                        : $"存在已删除或不匹配的资源，未能完全还原，请查看 Console。",
                    "确定");
            }
        }

        /// <summary>
        /// 构建「当前资源 → 回档资源」的变更对比行（仅包含真正发生变化的引用）。
        /// </summary>
        private List<RollbackDiffRow> BuildRollbackDiffRows(AnalysisSession session, RollbackSnapshot snapshot)
        {
            var rows = new List<RollbackDiffRow>();
            if (snapshot == null) return rows;

            foreach (var kvp in session.Analyzers)
            {
                var go = kvp.Key;
                if (go == null) continue;
                var objSnap = snapshot.objects.FirstOrDefault(o => o.key == RollbackStore.ObjectKey(go));
                if (objSnap == null) continue;

                foreach (var m in objSnap.materials)
                {
                    var targetGo = RollbackStore.FindChildByPath(go, m.rendererPath);
                    if (targetGo == null) continue;
                    var renderer = targetGo.GetComponent<Renderer>();
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    var mats = renderer.sharedMaterials;
                    if (m.materialIndex < 0 || m.materialIndex >= mats.Length) continue;
                    string current = mats[m.materialIndex] != null ? AssetDatabase.GetAssetPath(mats[m.materialIndex]) : "";
                    if (current == m.materialPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = $"材质[{m.materialIndex}]",
                        currentPath = RollbackStore.ShortName(current),
                        beforePath = RollbackStore.ShortName(m.materialPath),
                        kind = "material",
                        componentPath = m.rendererPath,
                        materialIndex = m.materialIndex
                    });
                }

                foreach (var mm in objSnap.meshes)
                {
                    var targetGo = RollbackStore.FindChildByPath(go, mm.componentPath);
                    if (targetGo == null) continue;
                    var mf = targetGo.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    string current = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (current == mm.meshPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = "网格",
                        currentPath = RollbackStore.ShortName(current),
                        beforePath = RollbackStore.ShortName(mm.meshPath),
                        kind = "mesh",
                        componentPath = mm.componentPath
                    });
                }

                foreach (var sm in objSnap.skinnedMeshes)
                {
                    var targetGo = RollbackStore.FindChildByPath(go, sm.componentPath);
                    if (targetGo == null) continue;
                    var skinned = targetGo.GetComponent<SkinnedMeshRenderer>();
                    if (skinned == null || skinned.sharedMesh == null) continue;
                    string current = AssetDatabase.GetAssetPath(skinned.sharedMesh);
                    if (current == sm.meshPath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = go.name,
                        slotLabel = "蒙皮网格",
                        currentPath = RollbackStore.ShortName(current),
                        beforePath = RollbackStore.ShortName(sm.meshPath),
                        kind = "skinnedMesh",
                        componentPath = sm.componentPath
                    });
                }
            }

            // 材质内贴图引用变更（快照级，去重显示）
            if (snapshot.materialTextures != null)
            {
                foreach (var t in snapshot.materialTextures)
                {
                    if (string.IsNullOrEmpty(t.materialPath) || string.IsNullOrEmpty(t.propertyName)) continue;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(t.materialPath);
                    if (mat == null) continue;
                    Texture cur = mat.GetTexture(t.propertyName);
                    string current = cur != null ? AssetDatabase.GetAssetPath(cur) : "";
                    if (current == t.texturePath) continue;
                    rows.Add(new RollbackDiffRow
                    {
                        objectName = "材质: " + mat.name,
                        slotLabel = $"贴图[{t.propertyName}]",
                        currentPath = RollbackStore.ShortName(current),
                        beforePath = RollbackStore.ShortName(t.texturePath),
                        kind = "texture",
                        materialPath = t.materialPath,
                        propertyName = t.propertyName
                    });
                }
            }

            return rows;
        }

        public void Clear()
        {
            scrollPos = Vector2.zero;
            selectedSnapshotIndex = -1;
        }
    }
}
