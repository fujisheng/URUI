using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;

namespace URUI.Authoring.Windows
{
    public static class LayoutCommands
    {
        public static AuthoringEdit[] Preset(string key, string name)
        {
            Dictionary<string, string> values;
            switch (name)
            {
                case "row":
                    values = new Dictionary<string, string>
                    {
                        ["display"] = "flex",
                        ["flex-direction"] = "row",
                        ["align-items"] = "center",
                        ["gap"] = "8px"
                    };
                    break;
                case "column":
                    values = new Dictionary<string, string>
                    {
                        ["display"] = "flex",
                        ["flex-direction"] = "column",
                        ["gap"] = "8px"
                    };
                    break;
                case "center":
                    values = new Dictionary<string, string>
                    {
                        ["display"] = "flex",
                        ["justify-content"] = "center",
                        ["align-items"] = "center"
                    };
                    break;
                case "fill":
                    values = new Dictionary<string, string>
                    {
                        ["position"] = "absolute",
                        ["left"] = "0px",
                        ["top"] = "0px",
                        ["right"] = "0px",
                        ["bottom"] = "0px",
                        ["width"] = "auto",
                        ["height"] = "auto",
                        ["margin"] = "0px"
                    };
                    break;
                default:
                    throw new InvalidOperationException("未知布局预设：" + name);
            }
            var edits = new List<AuthoringEdit>();
            foreach (var pair in values)
            {
                edits.Add(new AuthoringEdit
                {
                    Node = key,
                    Property = "style." + pair.Key,
                    Value = pair.Value
                });
            }
            return edits.ToArray();
        }
        internal static void Draw(ElementSelectionProxy proxy)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var pair in new[] { new[] { L.Tr("横向排列"), "row" }, new[] { L.Tr("纵向排列"), "column" }, new[] { L.Tr("内容居中"), "center" }, new[] { L.Tr("铺满父级"), "fill" } })
                {
                    if (GUILayout.Button(pair[0]))
                    {
                        AuthoringEnvironment.Try(() =>
                                            {
                                                if (AuthoringEnvironment.Mode != WorkspaceMode.Layout)
                                                {
                                                    throw new InvalidOperationException("请先退出录制，再修改布局预设。");
                                                }
                                                var author = AuthoringEnvironment.Author;
                                                SessionUndoBridge.Record(author, () => author.Execute(Preset(proxy.SourceKey, pair[1]), author.Revision));
                                            });
                    }
                }
            }
        }
    }
}
