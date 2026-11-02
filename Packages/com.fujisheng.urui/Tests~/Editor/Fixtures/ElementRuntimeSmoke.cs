using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace URUI.Tests
{
    /// <summary>真实 Unity 文档、输入和控件行为验收。</summary>
    public sealed class ElementRuntimeSmoke : MonoBehaviour
    {
        [Serializable]
        sealed class Result
        {
            public bool passed;
            public string error;
            public int assertions;
            public int expectedDiagnosticExceptions;
        }

        Document document;
        readonly Result result = new Result();
        const string Markup = @"<rml><head><style>
body{margin:0;font-family:__URUI_FONT__;color:white;font-size:18px;}
#panel{display:block;position:absolute;left:20px;top:20px;width:200px;height:200px;}
#hit{display:block;width:100px;height:40px;background-color:red;}
#save{display:block;width:100px;height:40px;}
</style></head><body><div id='panel'><div id='hit'/><button id='save'>Save</button><input id='name' type='text' value='old'/><input id='enabled' type='checkbox'/></div></body></rml>";

        IEnumerator Start()
        {
            document = gameObject.AddComponent<Document>();
            document.Input.UseUnityInput = false;
            try
            {
                document.LoadMarkup(Markup);
            }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }

            yield return null;
            yield return null;
            try
            {
                CheckElements();
                CheckEvents();
                CheckControls();
                CheckAdditionalControls();
            }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }

            yield return null;
            yield return null;
            try
            {
                CheckLayoutAndList();
            }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }

            yield return null;
            try
            {
                CheckReload();
                document.RegisterElementType("late-loader", context => new LateLoader(context));
                document.Root.SetInnerMarkup("<late-loader style='position:absolute;left:0;top:0;width:100px;height:100px;display:block;'/>");
            }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }
            yield return null;
            yield return null;
            try
            {
                document.Input.SendPointer(PointerPhase.Down, new Vector2(10, 10));
                Require(document.Q<Label>("late-loaded") != null, "lazy factory initialization defers reload until native callback returns");
                document.LoadMarkup("<rml><body><input id='focus-reloader' type='text'/></body></rml>");
            }
            catch (Exception error) { Finish(error); yield break; }
            yield return null;
            yield return null;
            try
            {
                var field = document.Q<TextField>("focus-reloader");
                field.RegisterCallback<FocusEvent>(evt => document.LoadMarkup("<rml><body><label id='focus-loaded'>ready</label></body></rml>"));
                field.Focus();
                Require(!field.IsValid && document.Q<Label>("focus-loaded") != null, "programmatic focus finishes deferred reload before returning");
                result.passed = true;
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        void Require(bool value, string message)
        {
            result.assertions++;
            if (!value)
            {
                throw new Exception(message);
            }
        }

        void CheckElements()
        {
            var button = document.Root.Q<Button>("save");
            Require(button != null && ReferenceEquals(button, document.Q("save")), "RML typed identity");
            Require(document.Root.Q<TextField>("name").Value == "old", "RML typed field value");
            var panel = document.Q("panel");
            Require(panel.Query<Button>().First() == button, "typed query order");
            Require(panel.Query<Element>().Where(value => value.Name == "hit").ToList().Count == 1, "query predicate");
            document.RegisterElementType("card", context => new Card(context));
            var card = document.Create<Card>();
            var label = document.Create<Label>();
            label.Text = "content";
            card.Add(label);
            panel.Add(card);
            Require(card.ChildCount == 1 && card.Hierarchy.ChildCount == 2, "logical and physical children");
            card.Clear();
            Require(card.ChildCount == 0 && label.IsValid && !label.IsAttached, "content clear retains identity");
            Require(card.Hierarchy.ChildCount == 2, "content clear preserves decorations");
            card.Add(label);
            Require(ReferenceEquals(label, card[0]), "reparent identity");
            card.SetEnabled(false);
            Require(!label.EnabledInHierarchy && label.EnabledSelf, "enabled inheritance");
            card.SetEnabled(true);
            Require(label.EnabledInHierarchy, "enabled restoration");
            panel.Remove(card);
            Require(card.IsValid && !card.IsAttached, "detached control remains usable");
            panel.Add(card);
            card.Destroy();
            Require(!label.IsValid, "destroy subtree invalidates wrappers");
            var typed = document.Create<Toggle>();
            bool rejectedTypeRemoval = false;
            try { typed.RemoveAttribute("type"); }
            catch (InvalidOperationException) { rejectedTypeRemoval = true; }
            Require(rejectedTypeRemoval, "removing type cannot silently change a typed native control");
            document.RegisterElementType("invalidation-probe", context => new InvalidationProbe(context));
            var probe = document.Create<InvalidationProbe>();
            Application.LogCallback diagnostic = (message, trace, type) =>
            {
                if (message.Contains("EXPECTED_INVALIDATION_PROBE"))
                {
                    result.expectedDiagnosticExceptions++;
                }
            };
            Application.logMessageReceived += diagnostic;
            try
            {
                probe.Destroy();
                Require(!probe.IsValid && document.IsLoaded && result.expectedDiagnosticExceptions == 1,
                    "custom invalidation failure preserves document cleanup");
            }
            finally { Application.logMessageReceived -= diagnostic; }
        }

        void CheckEvents()
        {
            var panel = document.Q("panel");
            var hit = document.Q("hit");
            var order = new List<string>();
            var capture = panel.RegisterCallback<PointerDownEvent>(evt => order.Add("capture"), TrickleDown.TrickleDown);
            var target = hit.RegisterCallback<PointerDownEvent>(evt => order.Add("target"));
            var bubble = panel.RegisterCallback<PointerDownEvent>(evt => order.Add("bubble"));
            document.Input.SendPointer(PointerPhase.Down, new Vector2(25, 25));
            document.Input.SendPointer(PointerPhase.Up, new Vector2(25, 25));
            Require(string.Join(",", order) == "capture,target,bubble", "event propagation order");
            capture.Dispose(); target.Dispose(); bubble.Dispose();
            int reached = 0;
            capture = panel.RegisterCallback<PointerDownEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            target = hit.RegisterCallback<PointerDownEvent>(evt => reached++);
            document.Input.SendPointer(PointerPhase.Down, new Vector2(25, 25));
            document.Input.SendPointer(PointerPhase.Cancel, new Vector2(25, 25));
            Require(reached == 0, "stop propagation reaches native dispatch");
            capture.Dispose(); target.Dispose();
            hit.Name = "";
            int moves = 0;
            using (hit.RegisterCallback<PointerDownEvent>(evt => hit.CapturePointer(evt.PointerId)))
            using (hit.RegisterCallback<PointerMoveEvent>(evt => moves++))
            {
                document.Input.SendPointer(PointerPhase.Down, new Vector2(25, 25), 11, true);
                document.Input.SendPointer(PointerPhase.Move, new Vector2(600, 400), 11, true);
                Require(hit.HasPointerCapture(11) && moves == 1, "anonymous captured target outside bounds");
                document.Input.SendPointer(PointerPhase.Cancel, new Vector2(600, 400), 11, true);
                Require(!hit.HasPointerCapture(11), "cancel releases capture");
            }
            hit.Name = "hit";
        }

        ListView list;
        List<string> items;
        Element sized;
        Slider firstSlider, secondSlider;
        int binds, unbinds;

        void CheckControls()
        {
            var toggle = document.Q<Toggle>("enabled");
            int changes = 0;
            toggle.RegisterValueChangedCallback(evt => changes++);
            toggle.SetValueWithoutNotify(true);
            Require(toggle.Value && changes == 0, "silent field write");
            toggle.Value = false;
            toggle.Value = false;
            Require(!toggle.Value && changes == 1, "one value notification and deduplication");
            var text = document.Q<TextField>("name");
            text.SetValueWithoutNotify("");
            text.Focus();
            document.Input.SendText("中文🎮");
            Require(text.Value == "中文🎮", "committed Unicode input");
            int textChanges = 0;
            text.RegisterValueChangedCallback(evt => textChanges++);
            document.Input.SendComposition("拼音");
            document.Input.CancelComposition();
            Require(text.Value == "中文🎮" && textChanges == 0, "IME preview cancellation is silent");
            document.Input.SendComposition("测试");
            document.Input.CommitComposition("测试");
            Require(text.Value == "中文🎮测试" && textChanges == 1, "IME commits once");
            document.Input.SendKey(KeyCode.Backspace, true);
            Require(text.Value == "中文🎮测", "IME caret follows Unicode commit");
            var slider = document.Create<Slider>();
            slider.LowValue = 0; slider.HighValue = 10; slider.Value = 100;
            Require(slider.Value == 10, "slider clamps range");
            var a = document.Create<RadioButton>();
            var b = document.Create<RadioButton>();
            a.GroupName = "test"; b.GroupName = "test";
            document.Root.Add(a); document.Root.Add(b);
            a.Value = true; b.Value = true;
            Require(!a.Value && b.Value, "radio exclusivity");
            var foldout = document.Create<Foldout>();
            var child = document.Create<Label>();
            document.Root.Add(foldout); foldout.Add(child);
            foldout.Text = "Group";
            Require(child.IsValid && foldout.ChildCount == 1, "composite text preserves children");
            sized = document.Create<Element>();
            sized.Name = "sized";
            sized.Style.Display = DisplayStyle.Block;
            sized.Style.Width = Length.Percent(50);
            sized.Style.Height = 30;
            document.Q("panel").Add(sized);
            firstSlider = document.Create<Slider>();
            secondSlider = document.Create<Slider>();
            foreach (var value in new[] { firstSlider, secondSlider })
            {
                value.Style.Position = Position.Absolute;
                value.Style.Left = 20;
                value.Style.Width = 100;
                value.Style.Height = 20;
                document.Root.Add(value);
            }
            firstSlider.Style.Top = 260;
            secondSlider.Style.Top = 300;
            items = new List<string>();
            for (int i = 0; i < 1000; i++)
            {
                items.Add("Row " + i);
            }

            list = document.Create<ListView>();
            list.Style.Height = 120;
            list.Style.Width = 200;
            list.Style.Position = Position.Absolute;
            list.Style.Left = 300;
            list.Style.Top = 20;
            list.FixedItemHeight = 24;
            list.MakeItem = () => document.Create<Label>();
            list.BindItem = (element, index) => { binds++; element.Text = items[index]; };
            list.UnbindItem = (element, index) => unbinds++;
            list.ItemsSource = items;
            document.Root.Add(list);
        }

        void CheckLayoutAndList()
        {
            Require(Mathf.Abs(sized.Layout.width - 100) < .1f, "percentage layout comes from native geometry: " + sized.Layout);
            Require(sized.Style.Width.Unit == LengthUnit.Percent && sized.Style.Width.Value == 50, "inline style preserves unit");
            var local = new Vector2(3, 4);
            Require(Vector2.Distance(sized.WorldToLocal(sized.LocalToWorld(local)), local) < .01f, "coordinate round trip");
            Require(list.RealizedItemCount > 0 && list.RealizedItemCount <= 9, "list virtualizes visible rows");
            var pressedRow = list.ContentContainer.Children()[0];
            Require(pressedRow.Layout.width >= list.ViewportRect.width - 1, "realized row fills the list viewport: " + pressedRow.Layout + " / " + list.ViewportRect);
            pressedRow.SendEvent(new PointerDownEvent());
            list.ScrollOffset = new Vector2(0, 12000);
            list.RefreshItems();
            pressedRow.SendEvent(new ClickEvent());
            Require(list.SelectedIndex == -1, "click across row rebind does not select replacement data");
            Require(list.RealizedItemCount <= 9 && unbinds > 0 && binds > 0, "list reuses and unbinds old rows");
            items.RemoveRange(2, items.Count - 2);
            list.RefreshItems();
            Require(list.RealizedItemCount == 2, "shrinking data retires surplus rows");
            var one = firstSlider.WorldBound.center;
            var two = secondSlider.WorldBound.center;
            document.Input.SendPointer(PointerPhase.Down, one, 21, true);
            document.Input.SendPointer(PointerPhase.Down, two, 22, true);
            document.Input.SendPointer(PointerPhase.Move, one + new Vector2(40, 0), 21, true);
            document.Input.SendPointer(PointerPhase.Move, two - new Vector2(40, 0), 22, true);
            Require(firstSlider.Value > 80 && secondSlider.Value < 20, "two sliders drag independently");
            document.Input.SendPointer(PointerPhase.Cancel, one, 21, true);
            document.Input.SendPointer(PointerPhase.Cancel, two, 22, true);
            document.Input.SendPointer(PointerPhase.Down, one, 31, true);
            firstSlider.SetEnabled(false);
            Require(!firstSlider.HasPointerCapture(31), "disabling slider releases active capture");
            firstSlider.SetEnabled(true);
            document.Input.SendPointer(PointerPhase.Down, one, 32, true);
            document.Input.SendPointer(PointerPhase.Move, one + new Vector2(40, 0), 32, true);
            document.Input.SendPointer(PointerPhase.Up, one + new Vector2(40, 0), 32, true);
            Require(firstSlider.Value > 80, "slider accepts new drag after reenable");
            firstSlider.TabIndex = 200;
            secondSlider.TabIndex = 100;
            secondSlider.Focus();
            document.Input.SendKey(KeyCode.Tab, true);
            Require(firstSlider.Focused, "tab follows configured order");
            var panel = document.Q("panel");
            document.Q<TextField>("name").Focus();
            panel.SetEnabled(false);
            Require(document.FocusedElement == null || !panel.Contains(document.FocusedElement), "disabling parent clears child focus");
            panel.SetEnabled(true);
        }

        void CheckAdditionalControls()
        {
            var integer = document.Create<IntegerField>();
            integer.Value = 42;
            Require(integer.Value == 42, "integer field round trip");
            var number = document.Create<FloatField>();
            number.Value = 1.25f;
            Require(Mathf.Abs(number.Value - 1.25f) < .001f, "float field invariant round trip");
            var text = document.Create<TextField>();
            text.Multiline = true;
            text.Value = "line1\nline2";
            Require(text.Value == "line1\nline2", "C# text field multiline");
            text.Multiline = false;
            text.Value = "one\ntwo";
            Require(text.Value == "one two", "single-line values normalize pasted line breaks");
            var dropdown = document.Create<Dropdown>();
            dropdown.Choices = new[] { "A", "中文", "<literal>" };
            dropdown.SelectedIndex = 1;
            Require(dropdown.Value == "中文" && dropdown.SelectedIndex == 1, "dropdown choices and value");
            var progress = document.Create<ProgressBar>();
            progress.Value = 120;
            Require(progress.Value == 100, "progress clamps range");
            var button = document.Q<Button>("save");
            int clicked = 0;
            button.Clicked += () => clicked++;
            button.Focus();
            document.Input.SendKey(KeyCode.Return, true);
            Require(clicked == 1, "button keyboard activation once");
            var toggle = document.Q<Toggle>("enabled");
            toggle.SetValueWithoutNotify(false);
            using (toggle.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                button.SetEnabled(false);
            }))
            {
                var rect = toggle.WorldBound;
                document.Input.SendPointer(PointerPhase.Down, rect.center);
                document.Input.SendPointer(PointerPhase.Up, rect.center);
            }
            Require(toggle.Value, "stop propagation preserves default checkbox behavior");
            button.SetEnabled(true);
        }

        void CheckReload()
        {
            var old = document.Root;
            int expectedUnbinds = unbinds + list.RealizedItemCount;
            var button = document.Q<Button>("save");
            button.RegisterCallback<ClickEvent>(evt => document.LoadMarkup("<rml><body><label id='new'>new</label></body></rml>"));
            document.Input.SendPointer(PointerPhase.Down, new Vector2(30, 70));
            document.Input.SendPointer(PointerPhase.Up, new Vector2(30, 70));
            Require(!old.IsValid && document.Q<Label>("new") != null, "reload inside native callback is deferred safely");
            Require(unbinds == expectedUnbinds, "document reload unbinds all realized rows");
        }

        void Finish(Exception error)
        {
            if (error != null)
            {
                result.error = error.ToString();
                Debug.LogException(error);
            }
            string evidence = "Evidence";
            var arguments = Environment.GetCommandLineArgs();
            int argument = Array.IndexOf(arguments, "--urui-evidence");
            if (argument >= 0 && argument + 1 < arguments.Length)
            {
                evidence = arguments[argument + 1];
            }
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "element-runtime.json"), JsonUtility.ToJson(result, true));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(result.passed ? 0 : 1);
#else
            Application.Quit(result.passed ? 0 : 1);
#endif
        }

        sealed class Card : Element
        {
            Element content;
            public Card(ElementContext context) : base(context) { }
            public override Element ContentContainer => content ?? this;
            protected override void OnInitialize()
            {
                Hierarchy.Add(Document.Create<Label>());
                content = Document.Create<Element>();
                Hierarchy.Add(content);
            }
        }

        sealed class InvalidationProbe : Element
        {
            public InvalidationProbe(ElementContext context) : base(context) { }
            protected override void OnInvalidated() => throw new InvalidOperationException("EXPECTED_INVALIDATION_PROBE");
        }

        sealed class LateLoader : Element
        {
            public LateLoader(ElementContext context) : base(context) { }
            protected override void OnInitialize() => Document.LoadMarkup("<rml><body><label id='late-loaded'>ready</label></body></rml>");
        }
    }
}
