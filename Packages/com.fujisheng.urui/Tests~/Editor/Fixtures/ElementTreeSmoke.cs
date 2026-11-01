#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace URUI.Tests
{
    /// <summary>跨真实 Editor 帧检查节点树、身份、匿名目标及回调修改期间的事件边界。</summary>
    public sealed class ElementTreeSmoke : MonoBehaviour
    {
        const string Markup = "<rml><head><style>body{margin:0;}#action{position:absolute;left:0;top:0;width:160px;height:100px;background-color:red;}.hit{position:absolute;left:5px;top:5px;width:100px;height:60px;background-color:blue;}</style></head><body><div id='action'><div class='hit'/></div><div id='scope'/></body></rml>";

        [DllImport("URUIBridge")]
        static extern void urui_mouse_move(int x, int y);

        [DllImport("URUIBridge")]
        static extern void urui_mouse_button(int button, int pressed);

        [DllImport("URUIBridge")]
        static extern int urui_get_element_box(string id, [Out] float[] values);

        Document document;
        Element action;
        Element target;
        IDisposable first;
        IDisposable late;
        int frames;
        int targetCalls;
        int parentCalls;
        int lateCalls;
        int skipped;
        bool stopByDetach;
        bool finished;

        static void Need(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message);
            }
        }

        static void Throws<T>(Action operation) where T : Exception
        {
            try
            {
                operation();
            }
            catch (T)
            {
                return;
            }

            throw new Exception("Expected " + typeof(T).Name);
        }

        static void Click()
        {
            urui_mouse_move(30, 30);
            urui_mouse_button(0, 1);
            urui_mouse_button(0, 0);
        }

        void CheckTree()
        {
            Need(document.Root != null && document.Root.Parent == null, "document root boundary");
            var scope = document.Root.Q("scope");
            Need(ReferenceEquals(scope, scope.Q("scope")), "query includes self and canonical wrapper");
            var a = document.CreateElement();
            var b = document.CreateElement("span");
            var c = document.CreateElement();
            Need(a.Name == "" && a.IsValid && !a.IsAttached && a.Parent == null, "anonymous detached creation");
            Throws<InvalidOperationException>(() => a.SetInnerMarkup("<template src='missing'/>"));
            a.Name = b.Name = "same";
            a.AddToClassList("item");
            b.AddToClassList("item");
            a.Add(b);
            scope.Add(a);
            scope.Add(c);
            var matches = scope.Query("same", "item");
            Need(matches.Count == 2 && matches[0] == a && matches[1] == b, "scoped DFS exact filters");
            Need(b.Q("scope") == null && scope.Q("#document") == null, "query never escapes subtree or interprets special IDs");
            a.Name = "renamed";
            Need(ReferenceEquals(a, document.Q("renamed")) && scope.Q("same") == b, "name change does not change identity");
            b.Text = "  <tag>& {{literal}}\n中文 🎮  ";
            Need(b.Text == "  <tag>& {{literal}}\n中文 🎮  ", "exact plain UTF8 text");
            Throws<InvalidOperationException>(() => {
                var ignored = a.Text;
            });
            b.SetAttribute("title", "提示");
            Need(b.GetAttribute("title") == "提示" && b.HasAttribute("title"), "native attribute reads");
            b.RemoveAttribute("title");
            Need(b.GetAttribute("title") == null && !b.HasAttribute("title"), "absent attribute differs from empty");
            b.EnableInClassList("active", true);
            Need(b.ClassListContains("active") && b.GetAttribute("class").Contains("active"), "class/attribute coherence");
            b.RemoveFromClassList("active");
            Need(!b.ClassListContains("active"), "class removal");
            Throws<ArgumentException>(() => b.AddToClassList("two tokens"));
            Throws<ArgumentException>(() => b.Add(scope));
            Throws<ArgumentOutOfRangeException>(() => scope.Insert(99, a));
            Need(scope[0] == a && scope[1] == c, "failed validation leaves tree intact");
            scope.Insert(1, a);
            Need(scope[1] == a, "same-parent final index");
            var children = scope.Children();
            scope.Clear();
            Need(scope.ChildCount == 0 && a.IsValid && b.IsValid && a.Parent == null && b.Parent == a && !b.IsAttached, "clear retains owned subtrees");
            Need(children.Count == 2 && matches.Count == 2, "snapshots retain original order");
            scope.Add(a);
            Need(b.IsAttached && ReferenceEquals(a, scope[0]), "reattach preserves descendants and identity");
            a.Destroy();
            Need(!a.IsValid && !b.IsValid && c.IsValid, "destroy invalidates subtree only");
            Throws<InvalidOperationException>(() => b.Text = "stale");
            Throws<InvalidOperationException>(() => document.Root.Destroy());
            c.Destroy();
        }

        void Update()
        {
            if (finished)
            {
                return;
            }

            frames++;
            try
            {
                if (frames == 2)
                {
                    document = new GameObject("Element Tree").AddComponent<Document>();
                    Need(document.Root == null && document.Query().Count == 0, "unloaded query");
                    document.LoadMarkup(Markup);
                    CheckTree();
                    action = document.Root.Q("action");
                    target = action.Q(className: "hit");
                    Need(target.Name == "" && target.Parent == action, "anonymous click target");
                    action.Style.SetWidth(10);
                    action.Style.Remove("width");
                    Action<ClickEvent> handler = evt => {
                        Need(evt.Target == target && evt.CurrentTarget == target, "actual event identity");
                        targetCalls++;
                        if (targetCalls == 1)
                        {
                            late = action.RegisterCallback<ClickEvent>(_ => lateCalls++);
                        }

                        if (stopByDetach)
                        {
                            target.RemoveFromHierarchy();
                        }
                    };
                    first = target.RegisterCallback(handler);
                    Need(ReferenceEquals(first, target.RegisterCallback(handler)), "duplicate subscription token");
                    IDisposable removed = null;
                    target.RegisterCallback<ClickEvent>(_ => removed.Dispose());
                    removed = target.RegisterCallback<ClickEvent>(_ => skipped++);
                    action.RegisterCallback<ClickEvent>(evt => {
                        Need(evt.Target == target && evt.CurrentTarget == action, "receiver differs from target");
                        parentCalls++;
                    });
                    Throws<NotSupportedException>(() => target.RegisterCallback<int>(_ => {
                    }));
                }

                if (frames == 5)
                {
                    var bounds = new float[6];
                    Need(urui_get_element_box("action", bounds) == 1 && Mathf.Abs(bounds[2] - 160) < .1f, "style removal restores stylesheet");
                    Click();
                }

                if (frames == 7)
                {
                    Need(targetCalls == 1 && parentCalls == 1 && lateCalls == 0 && skipped == 0, "all subscription snapshots precede callback mutation");
                    Click();
                }

                if (frames == 9)
                {
                    Need(targetCalls == 2 && parentCalls == 2 && lateCalls == 1, "new ancestor callback starts next event");
                    stopByDetach = true;
                    Click();
                }

                if (frames == 11)
                {
                    Need(targetCalls == 3 && parentCalls == 2 && lateCalls == 1 && target.IsValid && !target.IsAttached, "detach during callback stops delivery without destruction");
                    action.Add(target);
                    first.Dispose();
                    first.Dispose();
                }

                if (frames == 13)
                {
                    Click();
                }

                if (frames == 15)
                {
                    Need(targetCalls == 3 && parentCalls == 3 && lateCalls == 2 && skipped == 0, "reattach retains remaining subscriptions");
                    target.Name = "reused";
                    Click();
                    target.Destroy();
                    var replacement = document.CreateElement();
                    replacement.Name = "reused";
                    action.Add(replacement);
                    Need(!target.IsValid && !ReferenceEquals(target, document.Q("reused")), "replacement cannot revive old identity");
                }

                if (frames == 17)
                {
                    Need(parentCalls == 3 && lateCalls == 2, "queued click cannot retarget replacement or old parent");
                    var oldRoot = document.Root;
                    var detached = document.CreateElement();
                    document.Unload();
                    Need(!oldRoot.IsValid && !detached.IsValid, "unload invalidates detached ownership");
                    document.LoadMarkup(Markup);
                    Need(!ReferenceEquals(oldRoot, document.Root), "new document root generation");
                    document.Unload();
                    late.Dispose();
                    Destroy(document.gameObject);
                }

                if (frames == 20)
                {
                    Finish(true, "");
                }
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        void Finish(bool passed, string error)
        {
            finished = true;
            File.WriteAllText("Evidence/element-tree.json", JsonUtility.ToJson(new Report { passed = passed, frames = frames, targetCalls = targetCalls, parentCalls = parentCalls, error = error }, true));
            Debug.Log(passed ? "URUI_ELEMENT_TREE_PASS" : "URUI_ELEMENT_TREE_FAIL " + error);
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
        }

        [Serializable]
        class Report
        {
            public bool passed;
            public int frames;
            public int targetCalls;
            public int parentCalls;
            public string error;
        }
    }
}
#endif
