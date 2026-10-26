using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TvT.EditorTools
{
    /// <summary>
    /// 把 Unity 内置的「Import Unity Package」窗口替换成 <see cref="UnityPackageImportWindow"/>。
    ///
    /// 为什么只能在窗口出现后替换：拖入 .unitypackage 时是 Unity 原生拖拽处理 → 直接打开 internal 的
    /// 内置窗口（UnityEditor.PackageImport），没有公开的 DragDrop-before 回调可以拦在它前面；
    /// 内置窗口的类型和过滤逻辑也改不了。所以这里做「最小延迟替换」：
    ///   - 每个 editor update 只做 O(1) 检查：EditorWindow.focusedWindow / mouseOverWindow
    ///     （导入窗口弹出时会抢焦点），发现内置窗口 → 反射读包路径 → Close() → 打开同款窗口；
    ///   - 全量 Resources.FindObjectsOfTypeAll&lt;EditorWindow&gt;() 只发生在窗口焦点变化时（且 0.1s 节流），
    ///     外加 2s 一次兜底扫描，避免"每 0.15s 扫全部窗口"带来的卡顿；
    ///   - 替换延迟 ≈ 1 帧。
    ///
    /// 已知限制：若内置窗口是模态窗口（modal utility），Unity 的 update 回调在它关闭前可能不执行，
    /// 此时替换不会生效；兜底是 UnityPackageImportWindow 的 [OnOpenAsset]（双击）与自有的菜单/右键入口。
    /// 开关存在 EditorPrefs，可在同款窗口底部或菜单 Tools > Unity Package Inspector 里切换。
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityPackageImportWatchdog
    {
        internal const string PrefKey = "TvT.UnityPackageImportWatchdog.Enabled";
        internal const string MenuPath = "Tools/Unity Package Inspector/接管内置导入窗口 (Watchdog)";

        const string BuiltinTitle = "Import Unity Package";

        /// <summary>兜底全量扫描的间隔（秒）。窗口焦点变化的即时扫描不受此限制。</summary>
        const double SafetyScanInterval = 2.0;

        /// <summary>两次全量扫描之间的最小间隔（秒），避免焦点频繁切换时抖动。</summary>
        const double MinFullScanInterval = 0.1;

        static double _nextSafetyScan;
        static double _lastFullScanTime = -10.0;
        static EditorWindow _lastFocusedWindow;
        static bool _replacing;

        static UnityPackageImportWatchdog()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            _nextSafetyScan = 0.0; // 域重载后立刻补一次，接住重载前就开着的旧窗口
        }

        internal static bool Enabled
        {
            get { return EditorPrefs.GetBool(PrefKey, true); }
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                if (value)
                {
                    _nextSafetyScan = 0.0;
                    _lastFocusedWindow = null;
                }
                Debug.Log("[UnityPackageImportWatchdog] 接管内置导入窗口：" + (value ? "开启" : "关闭"));
            }
        }

        [MenuItem(MenuPath, false, 60)]
        static void ToggleFromMenu()
        {
            Enabled = !Enabled;
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleFromMenuValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void Tick()
        {
            if (!Enabled || _replacing)
                return;

            double now = EditorApplication.timeSinceStartup;

            // ① 每帧 O(1)：只看焦点窗口 / 鼠标下的窗口。导入窗口一弹出就会抢焦点，
            //    所以绝大多数情况在这一步就能发现，不需要遍历窗口。
            EditorWindow focused = EditorWindow.focusedWindow;
            if (TryReplace(focused))
                return;
            EditorWindow mouseOver = EditorWindow.mouseOverWindow;
            if (mouseOver != focused && TryReplace(mouseOver))
                return;

            // ② 只在焦点变化时做一次全量扫描（内部还有 0.1s 节流），接住没拿焦点的旧窗口
            if (focused != _lastFocusedWindow)
            {
                _lastFocusedWindow = focused;
                if (ScanAll(now))
                    return;
            }

            // ③ 低频兜底扫描（默认 2s）
            if (now >= _nextSafetyScan)
            {
                _nextSafetyScan = now + SafetyScanInterval;
                ScanAll(now);
            }
        }

        static bool TryReplace(EditorWindow win)
        {
            if (win == null || !IsBuiltinImportWindow(win))
                return false;
            Replace(win);
            return true;
        }

        static bool ScanAll(double now)
        {
            if (now - _lastFullScanTime < MinFullScanInterval)
                return false;
            _lastFullScanTime = now;

            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                EditorWindow win = windows[i];
                if (win == null || !IsBuiltinImportWindow(win))
                    continue;
                Replace(win);
                return true;
            }
            return false;
        }

        static void Replace(EditorWindow builtin)
        {
            string packagePath = TryReadPackagePath(builtin);
            _replacing = true;
            try
            {
                builtin.Close();

                if (string.IsNullOrEmpty(packagePath))
                {
                    Debug.LogWarning("[UnityPackageImportWatchdog] 已关掉内置导入窗口，但没从它身上读到包路径；打开同款窗口后请手动选择包。");
                }
                else
                {
                    Debug.Log("[UnityPackageImportWatchdog] 已替换内置导入窗口。包：" + packagePath);
                }

                UnityPackageImportWindow.Open(packagePath);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _replacing = false;
            }
        }

        static bool IsBuiltinImportWindow(EditorWindow win)
        {
            Type type = win.GetType();
            if (type == typeof(UnityPackageImportWindow))
                return false; // 自己，别误伤

            if (type.Namespace == null || !type.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal))
                return false;

            string name = type.Name ?? string.Empty;
            bool nameLooksRight = name == "PackageImport"
                || name.Contains("ImportPackage")
                || name.Contains("ImportWindow");
            if (!nameLooksRight)
                return false;

            GUIContent title = win.titleContent;
            return title != null && title.text == BuiltinTitle;
        }

        /// <summary>内置窗口存包路径的字段名各版本不一样，直接扫所有 string 字段找形如 *.unitypackage 的值。</summary>
        static string TryReadPackagePath(EditorWindow win)
        {
            FieldInfo[] fields = win.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(string))
                    continue;

                string value;
                try
                {
                    value = fields[i].GetValue(win) as string;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(value))
                    continue;
                if (value.IndexOf(".unitypackage", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (File.Exists(value))
                    return value;

                try
                {
                    string full = Path.GetFullPath(Path.Combine(
                        Directory.GetParent(Application.dataPath).FullName, value));
                    if (File.Exists(full))
                        return full;
                }
                catch
                {
                    // 字段里不是路径就继续找
                }
            }
            return null;
        }
    }
}
