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
    /// <summary>分别验证实际控件交互；单项失败不掩盖其余组件缺口。</summary>
    public sealed class ControlCompletenessSmoke : MonoBehaviour
    {
        [Serializable]
        sealed class Result
        {
            public bool passed;
            public int assertions;
            public List<string> passedCases = new List<string>();
            public List<string> failures = new List<string>();
        }
        readonly Result result = new Result();
        Document document;
        const string Markup = @"<rml><head><style>
body { margin:0; width:100%; height:100%; font-family:__URUI_FONT__; }
#scroll {position:absolute;left:300px;top:200px;width:160px;height:80px;}
#list {position:absolute;left:300px;top:300px;width:160px;height:100px;}
#slider {position:absolute;left:240px;top:20px;width:100px;height:100px;}
</style></head><body>
<button id='button'>Action</button><input id='toggle' type='checkbox'/>
<input id='radio-a' type='radio' name='group' checked='checked'/><input id='radio-b' type='radio' name='group' disabled='disabled'/><input id='radio-c' type='radio' name='group'/>
<textarea id='text' data-urui-multiline='false'>old</textarea>
<input id='integer' type='number' value='2'/><input id='float' type='text' data-urui-type='URUI.Controls.FloatField' value='2'/>
<input id='slider' type='range' min='0' max='100' value='50'/>
<select id='dropdown'><option value='a'>A</option><option value='b'>B</option></select>
<progress-bar id='progress'/><foldout id='foldout' text='Group'><label>Content</label></foldout>
<scroll-view id='scroll'><div id='scroll-end' style='display:block;margin-top:400px;width:20px;height:20px;'/></scroll-view>
<list-view id='list'/></body></rml>";

        IEnumerator Start()
        {
            document = gameObject.AddComponent<Document>();
            document.Input.UseUnityInput = false;
            var cases = new (string name, Action test)[]
            {
                ("toggle_keyboard", ToggleKeyboard), ("radio_navigation", RadioNavigation),
                ("button_reload_boundary", ButtonReloadBoundary), ("delayed_integer_commit", DelayedInteger),
                ("readonly_text_and_composition", ReadOnlyText), ("invalid_float_commit", InvalidFloat),
                ("numeric_step", NumericStep), ("slider_direction", SliderDirection),
                ("slider_step_change", SliderStepChange), ("progress_range", ProgressRange),
                ("foldout_value_contract", FoldoutContract), ("scroll_keyboard", ScrollKeyboard),
                ("scroll_to_child", ScrollToChild), ("list_keyboard", ListKeyboard),
                ("list_binding_cleanup", ListBindingCleanup), ("default_control_styles", DefaultStyles),
                ("scroll_viewport", ScrollViewport), ("list_binding_reload", ListBindingReload),
                ("business_styles_override", BusinessStylesOverride), ("headless_document_styles", HeadlessStyles),
                ("inverted_slider_step", InvertedSliderStep), ("delayed_mode_switch", DelayedModeSwitch),
                ("progress_rml_range", ProgressRmlRange)
            };
            foreach (var test in cases)
            {
                try
                {
                    document.LoadMarkup(test.name == "business_styles_override" ? Markup.Replace("<style>", "<style>#button { height:47px; }") : Markup);
                    if (test.name.StartsWith("list_"))
                    {
                        var items = new List<string>();
                        for (int i = 0; i < 100; i++)
                        {
                            items.Add("Item " + i);
                        }

                        var list = document.Q<ListView>("list");
                        list.ItemsSource = items;
                    }
                }
                catch (Exception error)
                {
                    result.failures.Add(test.name + ": setup: " + error);
                    continue;
                }
                yield return null;
                yield return null;
                yield return null;
                try { test.test(); result.passedCases.Add(test.name); }
                catch (Exception error) { result.failures.Add(test.name + ": " + error); }
            }
            result.passed = result.failures.Count == 0;
            string evidence = "Evidence";
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--urui-evidence");
            if (index >= 0 && index + 1 < args.Length)
            {
                evidence = args[index + 1];
            }

            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "control-completeness.json"), JsonUtility.ToJson(result, true));
            document.Unload();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(result.passed ? 0 : 1);
#else
            Application.Quit(result.passed ? 0 : 1);
#endif
        }
        void Need(bool value, string reason)
        {
            result.assertions++;
            if (!value)
            {
                throw new Exception(reason);
            }
        }
        void Key(KeyCode key)
        {
            document.Input.SendKey(key, true);
            document.Input.SendKey(key, false);
        }
        void ReplaceText(string text)
        {
            document.Input.SendKey(KeyCode.A, true, InputModifiers.Control);
            document.Input.SendKey(KeyCode.A, false, InputModifiers.Control);
            document.Input.SendText(text);
        }
        void ToggleKeyboard()
        {
            var toggle = document.Q<Toggle>("toggle");
            int changes = 0;
            toggle.RegisterValueChangedCallback(evt => changes++);
            Need(toggle.Focus(), "toggle focus");
            document.Input.SendKey(KeyCode.Space, true);
            Need(!toggle.Value, "space press waits for release");
            document.Input.SendKey(KeyCode.Space, false);
            Need(toggle.Value && changes == 1, "space release toggles once");
            document.Input.SendKey(KeyCode.Space, false);
            Need(changes == 1, "orphan release does not toggle");
        }
        void RadioNavigation()
        {
            var first = document.Q<RadioButton>("radio-a");
            var last = document.Q<RadioButton>("radio-c");
            first.Focus(); Key(KeyCode.RightArrow);
            Need(!first.Value && last.Value && last.Focused, "radio arrow skips disabled group member");
            Key(KeyCode.RightArrow);
            Need(first.Value && first.Focused && !last.Value, "radio navigation wraps within group");
        }
        void ButtonReloadBoundary()
        {
            var button = document.Q<Button>("button"); int late = 0;
            button.Clicked += () => document.LoadMarkup("<rml><body><label id='new'>new</label></body></rml>");
            button.Clicked += () => late++;
            button.Focus(); Key(KeyCode.Return);
            Need(late == 0 && !button.IsValid && document.Q("new") != null, "reload stops remaining Clicked handlers");
        }
        void DelayedInteger()
        {
            var field = document.Q<IntegerField>("integer"); int changes = 0, submits = 0;
            field.IsDelayed = true;
            field.Submitted += value => submits++;
            field.RegisterValueChangedCallback(evt => changes++);
            field.Focus(); ReplaceText("42");
            Need(field.Value == 2 && changes == 0, "delayed input leaves committed value unchanged");
            Key(KeyCode.Return);
            Need(field.Value == 42 && changes == 1 && submits == 1, "enter commits and submits once");
            document.Q<Button>("button").Focus();
            Need(changes == 1, "blur after submit does not duplicate change");
        }
        void ReadOnlyText()
        {
            var text = document.Q<TextField>("text");
            text.IsReadOnly = true; text.Focus();
            ReplaceText("changed"); document.Input.SendComposition("preview"); document.Input.CommitComposition("commit");
            Need(text.Value == "old", "readonly rejects direct text and composition");
            text.IsReadOnly = false; ReplaceText("editable");
            Need(text.Value == "editable", "readonly can be reversed without losing focus");
        }
        void InvalidFloat()
        {
            var field = document.Q<FloatField>("float"); int exceptions = 0;
            Application.LogCallback log = (message, trace, type) => { if (type == LogType.Exception) { exceptions++; } };
            Application.logMessageReceived += log;
            try
            {
                field.Focus(); ReplaceText("NaN"); Key(KeyCode.Return);
                Need(field.Value == 2 && exceptions == 0, "nonfinite edit restores last valid value without exception");
            }
            finally { Application.logMessageReceived -= log; }
        }
        void NumericStep()
        {
            var field = document.Q<IntegerField>("integer"); field.Step = 3;
            field.Focus(); Key(KeyCode.UpArrow);
            Need(field.Value == 5, "numeric up arrow applies configured step");
            field.Value = int.MaxValue; Key(KeyCode.UpArrow);
            Need(field.Value == int.MaxValue, "integer stepping saturates instead of overflowing");
        }
        void SliderDirection()
        {
            var slider = document.Q<Slider>("slider"); slider.Direction = URUI.Controls.SliderDirection.Vertical;
            var bounds = slider.WorldBound;
            var point = new Vector2(bounds.center.x, bounds.yMin + bounds.height * .8f);
            document.Input.SendPointer(PointerPhase.Down, point);
            document.Input.SendPointer(PointerPhase.Up, point);
            Need(slider.Value >= 75 && slider.Value <= 85, "vertical slider uses y coordinate");
            slider.Inverted = true; slider.Focus(); Key(KeyCode.DownArrow);
            Need(slider.Value < 80, "inverted slider reverses keyboard direction");
        }
        void SliderStepChange()
        {
            var slider = document.Q<Slider>("slider"); slider.Value = 5; slider.Step = 3;
            Need(slider.Value == 6, "changing step requantizes current value");
        }
        void ProgressRange()
        {
            var progress = document.Q<ProgressBar>("progress"); progress.LowValue = 20; progress.HighValue = 80;
            progress.Value = 50;
            var fill = progress.Q(className: "urui-progress-fill");
            Need(fill.Style.Width.Value == 50 && fill.Style.Width.Unit == LengthUnit.Percent, "progress fill uses configured range");
        }
        void FoldoutContract()
        {
            var foldout = document.Q<Foldout>("foldout");
            var field = foldout as INotifyValueChanged<bool>;
            Need(field != null, "foldout participates in generic value controls");
            field.SetValueWithoutNotify(false); Need(!field.Value && foldout.ChildCount == 1, "quiet collapse preserves content");
        }
        void ScrollKeyboard()
        {
            var scroll = document.Q<ScrollView>("scroll"); Need(scroll.Focus(), "scroll view participates in focus navigation");
            Key(KeyCode.PageDown); Need(scroll.ScrollOffset.y > 40, "page down scrolls the viewport");
            Key(KeyCode.Home); Need(scroll.ScrollOffset.y == 0, "home returns to start");
        }
        void ScrollToChild()
        {
            var scroll = document.Q<ScrollView>("scroll");
            scroll.ScrollTo(document.Q("scroll-end"));
            Need(scroll.ScrollOffset.y >= 330, "scroll to child reveals its bottom edge");
        }
        void ListKeyboard()
        {
            var list = document.Q<ListView>("list"); Need(list.Focus(), "list focus");
            int chosen = -1; list.ItemChosen += index => chosen = index;
            Key(KeyCode.DownArrow); Need(list.SelectedIndex == 0, "first arrow selects first item");
            Key(KeyCode.End); Need(list.SelectedIndex == 99 && list.ScrollOffset.y > 1000, "end selects and reveals final item");
            Key(KeyCode.Return); Need(chosen == 99, "enter chooses current item");
        }
        void ListBindingCleanup()
        {
            var list = document.Q<ListView>("list"); int oldCleanup = 0, newCleanup = 0;
            list.UnbindItem = (row, index) => oldCleanup++;
            list.RefreshItems(); int count = list.RealizedItemCount;
            list.UnbindItem = (row, index) => newCleanup++;
            list.RefreshItems();
            Need(oldCleanup == count && newCleanup == 0, "each binding retains its matching cleanup delegate");
        }
        void DefaultStyles()
        {
            var button = document.Q<Button>("button");
            Need(button.Layout.height >= 24 && button.Layout.width >= 40, "button has usable default dimensions");
            string background = button.ResolvedStyle.Get("background-color");
            Need(!string.IsNullOrEmpty(background) && background != "#00000000", "button visible without sample-only skin");
            button.Style.Height = 47;
        }
        void ScrollViewport()
        {
            var scroll = document.Q<ScrollView>("scroll");
            scroll.ScrollOffset = new Vector2(100, 0);
            Need(scroll.ScrollOffset.x == 0, "vertical scrollbar does not create unintended horizontal overflow");
        }
        void ListBindingReload()
        {
            var list = document.Q<ListView>("list"); bool requested = false;
            list.BindItem = (row, index) =>
            {
                if (!requested)
                {
                    requested = true;
                    document.LoadMarkup("<rml><body><label id='binding-loaded'>ready</label></body></rml>");
                }
            };
            list.RefreshItems();
            Need(!list.IsValid && document.Q("binding-loaded") != null, "bind callback reload completes safely after row update returns");
        }
        void BusinessStylesOverride() => Need(document.Q<Button>("button").Layout.height == 47, "authored stylesheet overrides basic control sizing");
        void HeadlessStyles()
        {
            document.LoadMarkup("<!-- <head> ignored </head> --><rml><body><button id='headless'>Ready</button></body></rml>");
            Need(document.Q<Button>("headless") != null, "basic styles do not invalidate a document without head");
        }
        void InvertedSliderStep()
        {
            var slider = document.Q<Slider>("slider"); slider.Step = 3; slider.Value = 81;
            slider.Inverted = true;
            Need(slider.Value == 81, "inversion preserves value when range is not divisible by step");
            slider.Focus(); Key(KeyCode.RightArrow);
            Need(slider.Value == 78, "inverted keyboard step follows semantic step grid");
        }
        void DelayedModeSwitch()
        {
            var field = document.Q<IntegerField>("integer"); field.IsDelayed = true;
            field.Focus(); ReplaceText("42"); field.IsDelayed = false;
            Need(field.Value == 42, "turning off delayed mode commits current editing text");
        }
        void ProgressRmlRange()
        {
            document.LoadMarkup("<rml><body><progress-bar id='range' min='20' max='80' value='35'/></body></rml>");
            Need(document.Q<ProgressBar>("range").Q(className: "urui-progress-fill").Style.Width.Value == 25,
                "RML progress bounds determine actual fill ratio");
        }
    }
}
