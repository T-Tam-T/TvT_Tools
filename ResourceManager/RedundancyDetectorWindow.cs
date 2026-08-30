using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceManager.Core; // 依赖现有的 ResourceAnalyzer

namespace ResourceManager.Tools
{
    /// <summary>
    /// 资源条目（用于未引用资源列表）
    /// </summary>
    [System.Serializable]
    public class ResourceEntry
    {
        public Object asset;
        public string assetPath;
        public string newName;      // 可编辑的新名称（不含扩展名）
        public bool isSelected;
        public string resourceType;
    }

    /// <summary>
    /// 未引用资源检测与移动工具
    /// </summary>
    public class OrphanResourceMover : EditorWindow
    {
        #region 数据
        private List<GameObject> prefabs = new List<GameObject>();
        private string scanFolder = "Assets";
        private bool includeSubfolders = true;
        private string targetFolder = "Assets/MovedResources";

        private List<ResourceEntry> orphanResources = new List<ResourceEntry>();
        private bool isAnalyzing = false;
        private Vector2 scrollLeft;
        private Vector2 scrollRight;

        // 分组全选状态
        private Dictionary<string, bool> typeAllSelected = new Dictionary<string, bool>();
        #endregion

        [MenuItem("Tools/TvTTools/未引用资源检测与移动", false, 38)]
        public static void ShowWindow()
        {
            var window = GetWindow<OrphanResourceMover>("孤资源移动器");
            window.minSize = new Vector2(900, 600);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
            DrawStatusBar();
        }

        #region 左侧面板：设置与预制体管理
        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320), GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("扫描设置", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("资源文件夹:", GUILayout.Width(80));
            scanFolder = EditorGUILayout.TextField(scanFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
                SelectScanFolder();
            EditorGUILayout.EndHorizontal();
            includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", includeSubfolders);

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("移动目标", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("目标文件夹:", GUILayout.Width(80));
            targetFolder = EditorGUILayout.TextField(targetFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
                SelectTargetFolder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("引用源预制体", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从文件夹添加预制体", GUILayout.Height(25)))
                AddPrefabsFromFolder();
            if (GUILayout.Button("清空预制体", GUILayout.Height(25)))
                prefabs.Clear();
            EditorGUILayout.EndHorizontal();

            Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "拖拽预制体到此区域\n支持多选", EditorStyles.helpBox);
            HandlePrefabDragDrop(dropRect);

            scrollLeft = EditorGUILayout.BeginScrollView(scrollLeft, GUILayout.Height(200));
            for (int i = 0; i < prefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                prefabs[i] = EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false) as GameObject;
                if (GUILayout.Button("移除", GUILayout.Width(45)))
                    prefabs.RemoveAt(i--);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("开始检测未引用资源", GUILayout.Height(35)))
            {
                AnalyzeOrphanResources();
            }

            EditorGUILayout.EndVertical();
        }

        private void SelectScanFolder()
        {
            string path = EditorUtility.OpenFolderPanel("选择要扫描的资源文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                scanFolder = "Assets" + path.Substring(Application.dataPath.Length);
        }

        private void SelectTargetFolder()
        {
            string path = EditorUtility.OpenFolderPanel("选择目标移动文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                targetFolder = "Assets" + path.Substring(Application.dataPath.Length);
        }

        private void AddPrefabsFromFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("选择预制体所在文件夹", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder)) return;
            string relativeFolder = "Assets" + folder.Substring(Application.dataPath.Length);
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { relativeFolder });
            int added = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && !prefabs.Contains(prefab))
                {
                    prefabs.Add(prefab);
                    added++;
                }
            }
            Debug.Log($"添加了 {added} 个预制体");
        }

        private void HandlePrefabDragDrop(Rect dropRect)
        {
            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;
            if (evt.type == EventType.DragUpdated)
            {
                bool hasPrefab = DragAndDrop.objectReferences.Any(obj => obj is GameObject && PrefabUtility.IsPartOfPrefabAsset(obj));
                DragAndDrop.visualMode = hasPrefab ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go) && !prefabs.Contains(go))
                        prefabs.Add(go);
                }
                evt.Use();
                Repaint();
            }
        }
        #endregion

        #region 右侧面板：未引用资源列表
        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("未引用资源列表", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (orphanResources.Count == 0 && !isAnalyzing)
            {
                EditorGUILayout.HelpBox("未检测到未引用资源，请先点击「开始检测未引用资源」。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var groups = orphanResources.GroupBy(r => r.resourceType).OrderBy(g => g.Key);
            scrollRight = EditorGUILayout.BeginScrollView(scrollRight, GUILayout.ExpandHeight(true));

            foreach (var group in groups)
            {
                string typeName = group.Key;
                if (!typeAllSelected.ContainsKey(typeName))
                    typeAllSelected[typeName] = false;

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                bool newAllSelected = EditorGUILayout.ToggleLeft($"{typeName} ({group.Count()})", typeAllSelected[typeName], GUILayout.Width(150));
                if (newAllSelected != typeAllSelected[typeName])
                {
                    typeAllSelected[typeName] = newAllSelected;
                    foreach (var entry in group)
                        entry.isSelected = newAllSelected;
                }
                if (GUILayout.Button("全选", GUILayout.Width(50)))
                {
                    typeAllSelected[typeName] = true;
                    foreach (var entry in group) entry.isSelected = true;
                }
                if (GUILayout.Button("全不选", GUILayout.Width(60)))
                {
                    typeAllSelected[typeName] = false;
                    foreach (var entry in group) entry.isSelected = false;
                }
                EditorGUILayout.EndHorizontal();

                foreach (var entry in group)
                {
                    EditorGUILayout.BeginHorizontal();
                    entry.isSelected = EditorGUILayout.Toggle(entry.isSelected, GUILayout.Width(20));
                    EditorGUILayout.LabelField(entry.asset.name, GUILayout.Width(180));
                    entry.newName = EditorGUILayout.TextField(entry.newName, GUILayout.Width(200));
                    EditorGUILayout.ObjectField(entry.asset, entry.asset.GetType(), false, GUILayout.Width(150));
                    if (GUILayout.Button("定位", GUILayout.Width(50)))
                    {
                        Selection.activeObject = entry.asset;
                        EditorGUIUtility.PingObject(entry.asset);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("移动选中的资源", GUILayout.Height(30)))
            {
                MoveSelectedResources();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(isAnalyzing ? "分析中..." : $"就绪 | 未引用资源数: {orphanResources.Count}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region 核心逻辑：检测未引用资源
        private async void AnalyzeOrphanResources()
        {
            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请至少添加一个预制体作为引用源。", "确定");
                return;
            }
            if (!AssetDatabase.IsValidFolder(scanFolder))
            {
                EditorUtility.DisplayDialog("错误", "扫描文件夹路径无效。", "确定");
                return;
            }

            isAnalyzing = true;
            Repaint();

            HashSet<string> referencedAssets = new HashSet<string>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                ResourceAnalyzer analyzer = new ResourceAnalyzer();
                analyzer.SelectedPrefab = prefab;
                analyzer.AnalyzeResources();
                foreach (var res in analyzer.ResourceUsage.Keys)
                {
                    string path = AssetDatabase.GetAssetPath(res);
                    if (!string.IsNullOrEmpty(path))
                        referencedAssets.Add(path);
                }
            }

            List<string> allAssetPaths = new List<string>();
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string fullScanPath = Path.GetFullPath(scanFolder);
            string[] supportedExtensions = { ".mat", ".png", ".jpg", ".tga", ".dds", ".fbx", ".obj", ".prefab", ".controller", ".anim" };
            foreach (string ext in supportedExtensions)
            {
                foreach (string file in Directory.GetFiles(fullScanPath, "*" + ext, searchOption))
                {
                    string relative = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                    if (!allAssetPaths.Contains(relative))
                        allAssetPaths.Add(relative);
                }
            }

            orphanResources.Clear();
            typeAllSelected.Clear();
            foreach (string path in allAssetPaths)
            {
                if (referencedAssets.Contains(path)) continue;
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset == null) continue;
                // 排除预制体本身（可选）
                if (asset is GameObject && PrefabUtility.IsPartOfPrefabAsset(asset)) continue;

                orphanResources.Add(new ResourceEntry
                {
                    asset = asset,
                    assetPath = path,
                    newName = asset.name,
                    isSelected = true,
                    resourceType = asset.GetType().Name
                });
            }

            isAnalyzing = false;
            Repaint();
            Debug.Log($"检测完成，找到 {orphanResources.Count} 个未引用资源。");
        }
        #endregion

        #region 移动逻辑与重名处理
        private void MoveSelectedResources()
        {
            var selected = orphanResources.Where(r => r.isSelected).ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请至少选中一个资源。", "确定");
                return;
            }

            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                string parent = Path.GetDirectoryName(targetFolder).Replace('\\', '/');
                string folderName = Path.GetFileName(targetFolder);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    EditorUtility.DisplayDialog("错误", $"父文件夹不存在: {parent}", "确定");
                    return;
                }
                AssetDatabase.CreateFolder(parent, folderName);
            }

            var existingAssets = new List<Object>();
            var existingPaths = Directory.GetFiles(Path.GetFullPath(targetFolder), "*", SearchOption.TopDirectoryOnly)
                .Select(f => "Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/'))
                .Where(p => !AssetDatabase.IsValidFolder(p))
                .ToList();
            foreach (string path in existingPaths)
            {
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null) existingAssets.Add(asset);
            }

            MoveDialog.ShowWindow(selected, targetFolder, existingAssets, OnMoveConfirmed);
        }

        private void OnMoveConfirmed(List<ResourceEntry> resourcesToMove, string targetFolderPath)
        {
            int moved = 0;
            int skipped = 0;
            foreach (var entry in resourcesToMove)
            {
                string oldPath = entry.assetPath;
                string newFileName = entry.newName + Path.GetExtension(oldPath);
                string newPath = Path.Combine(targetFolderPath, newFileName).Replace('\\', '/');

                if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null)
                {
                    bool overwrite = EditorUtility.DisplayDialog("文件重名",
                        $"目标文件夹已存在 {newFileName}\n是否覆盖？\n(选择「取消」将跳过此文件)",
                        "覆盖", "跳过");
                    if (!overwrite)
                    {
                        skipped++;
                        continue;
                    }
                    if (!AssetDatabase.DeleteAsset(newPath))
                    {
                        Debug.LogWarning($"删除原文件失败: {newPath}");
                        continue;
                    }
                }

                string error = AssetDatabase.MoveAsset(oldPath, newPath);
                if (string.IsNullOrEmpty(error))
                {
                    moved++;
                    Debug.Log($"移动成功: {oldPath} -> {newPath}");
                }
                else
                {
                    Debug.LogError($"移动失败: {oldPath} -> {error}");
                }
            }
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"移动完成：成功 {moved} 个，跳过 {skipped} 个。", "确定");

            orphanResources.RemoveAll(r => resourcesToMove.Contains(r));
            Repaint();
        }
        #endregion
    }

    /// <summary>
    /// 移动确认对话框：显示目标文件夹现有资源 + 待移动资源列表，支持编辑名称和选择
    /// </summary>
    public class MoveDialog : EditorWindow
    {
        private List<ResourceEntry> resourcesToMove;
        private string targetFolder;
        private List<Object> existingAssets;
        private System.Action<List<ResourceEntry>, string> onConfirm;
        private Vector2 scrollExisting, scrollMoving;

        public static void ShowWindow(List<ResourceEntry> resources, string folder, List<Object> existing, System.Action<List<ResourceEntry>, string> callback)
        {
            var window = CreateInstance<MoveDialog>();
            window.resourcesToMove = resources;
            window.targetFolder = folder;
            window.existingAssets = existing;
            window.onConfirm = callback;
            window.titleContent = new GUIContent("移动资源确认");
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"目标文件夹: {targetFolder}", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("目标文件夹中现有资源", EditorStyles.boldLabel);
            scrollExisting = EditorGUILayout.BeginScrollView(scrollExisting, GUILayout.Height(150));
            foreach (var asset in existingAssets)
            {
                EditorGUILayout.ObjectField(asset.name, asset, asset.GetType(), false);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("待移动资源（可修改名称，取消勾选则跳过）", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", GUILayout.Width(60)))
            {
                foreach (var r in resourcesToMove) r.isSelected = true;
            }
            if (GUILayout.Button("全不选", GUILayout.Width(60)))
            {
                foreach (var r in resourcesToMove) r.isSelected = false;
            }
            EditorGUILayout.EndHorizontal();

            scrollMoving = EditorGUILayout.BeginScrollView(scrollMoving, GUILayout.Height(250));
            for (int i = 0; i < resourcesToMove.Count; i++)
            {
                var r = resourcesToMove[i];
                EditorGUILayout.BeginHorizontal();
                r.isSelected = EditorGUILayout.Toggle(r.isSelected, GUILayout.Width(20));
                EditorGUILayout.LabelField(r.asset.name, GUILayout.Width(150));
                r.newName = EditorGUILayout.TextField(r.newName, GUILayout.Width(200));
                EditorGUILayout.ObjectField(r.asset, r.asset.GetType(), false, GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("移动", GUILayout.Height(30)))
            {
                var selected = resourcesToMove.Where(r => r.isSelected).ToList();
                if (selected.Count == 0)
                {
                    EditorUtility.DisplayDialog("提示", "请至少勾选一个资源。", "确定");
                    return;
                }
                onConfirm?.Invoke(selected, targetFolder);
                Close();
            }
            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}