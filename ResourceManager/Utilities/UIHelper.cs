using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace ResourceManager.Utilities
{
    public static class UIHelper
    {
        // 斑马条纹颜色（偶数行使用，深灰色）
        private static readonly Color _zebraColor = new Color(0.64f, 0.64f, 0.64f, 1f);

        /// <summary>
        /// 斑马条纹作用域：在折叠列表项绘制时包裹，为偶数索引项添加背景色差异。
        /// 用法: using (new UIHelper.ZebraScope(index)) { DrawItem(...); }
        /// </summary>
        public struct ZebraScope : System.IDisposable
        {
            private readonly bool _active;
            private readonly Color _originalBgColor;

            public ZebraScope(int index)
            {
                _active = (index % 2 == 0);
                if (_active)
                {
                    _originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = _zebraColor;
                }
                else
                {
                    _originalBgColor = GUI.backgroundColor;
                }
            }

            public void Dispose()
            {
                if (_active)
                {
                    GUI.backgroundColor = _originalBgColor;
                }
            }
        }

        // 绘制可折叠项
        public static bool DrawFoldoutItem(Object resource, string displayName, Dictionary<Object, bool> foldouts)
        {
            if (!foldouts.ContainsKey(resource))
            {
                foldouts[resource] = false;
            }

            GUILayout.BeginHorizontal();
            foldouts[resource] = EditorGUILayout.Foldout(foldouts[resource], displayName);
            EditorGUILayout.ObjectField("", resource, resource.GetType(), false);
            GUILayout.EndHorizontal();

            return foldouts[resource];
        }

        // 绘制带颜色的标签
        public static void DrawColoredLabel(string text, Color color)
        {
            Color original = GUI.color;
            GUI.color = color;
            GUILayout.Label(text);
            GUI.color = original;
        }

        // 绘制使用路径
        public static void DrawUsagePath(string path, Dictionary<Object, bool> foldouts, Object resource)
        {
            // 计算缩进层级
            int indentLevel = path.Split('/').Length - 1;

            GUILayout.BeginHorizontal();

            // 添加缩进
            GUILayout.Space(indentLevel * 15); // 每级缩进15像素

            // 显示简化名称
            string displayName = path.Split('/').LastOrDefault() ?? path;

            if (GUILayout.Button(displayName, EditorStyles.label))
            {
                SelectionHelper.SelectAndPingObject(path,
                    foldouts.ContainsKey(resource) ? foldouts[resource] : false);
            }

            // 添加提示信息
            // 添加中文提示
            if (Event.current.type == EventType.Repaint)
            {
                Rect rect = GUILayoutUtility.GetLastRect();
                GUI.Label(rect, new GUIContent("", $"点击定位: {path}"));
            }

            GUILayout.EndHorizontal();
        }
        // 绘制属性标签
        public static void DrawPropertyLabel(string label, string value, bool highlight = false)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label);
            if (highlight)
            {
                style.normal.textColor = Color.red;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(80)); // 固定标签宽度
            GUILayout.Label(value, style);
            GUILayout.EndHorizontal();
        }

        public static void DrawConditionLabel(string text, Dictionary<string, string> conditions)
        {
            string richText = text;
            foreach (var condition in conditions)
            {
                richText = richText.Replace(condition.Key,
                    $"<color=#{ColorUtility.ToHtmlStringRGB(Color.red)}>{condition.Key}</color>");
            }

            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.richText = true;
            EditorGUILayout.LabelField(richText, style);
        }

        private static string IndentUsagePath(string usagePath)
        {
            string[] parts = usagePath.Split('/');
            int indentLevel = parts.Length - 1;
            return new string(' ', indentLevel * 4) + usagePath;
        }
    }
}