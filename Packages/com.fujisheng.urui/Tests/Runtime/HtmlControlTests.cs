using System;
using NUnit.Framework;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Events;
using URUI.Hosting;

namespace URUI.Tests
{
    public sealed class HtmlControlTests
    {
        GameObject root, camera;
        Document document;
        Surface surface;
        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("HTML control camera", typeof(Camera)); camera.tag = "MainCamera";
            root = new GameObject("HTML controls"); surface = root.AddComponent<Surface>(); surface.Input.UseUnityInput = false;
            document = root.AddComponent<Document>(); document.Surface = surface;
        }
        [TearDown]
        public void TearDown() { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera); }
        void Load(string body) => document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}input,button,label,select,textarea{width:100px;height:30px;}</style></head><body>" + body + "</body></html>", "ui/controls.html");
        [TestCase("", 50f)]
        [TestCase("min='10' max='30'", 20f)]
        [TestCase("min='0' max='10' step='6' value='11'", 6f)]
        [TestCase("min='10' max='5' value='100'", 10f)]
        [TestCase("min='0' max='10' step='any' value='2.25'", 2.25f)]
        [TestCase("min='NaN' max='Infinity' step='0' value='bogus'", 50f)]
        [TestCase("step='3' value='4'", 4f)]
        [TestCase("min='0.5' max='3.5' step='1' value='1'", 1.5f)]
        public void HtmlRangeUsesStandardDefaultsBoundsAndStepBase(string attributes, float expected)
        {
            Load("<input id='input' type='range' " + attributes + ">");
            Assert.That(document.Q<Slider>("input").Value, Is.EqualTo(expected).Within(.0001f));
        }

        [Test]
        public void AuthoredSliderSkinTracksRangeSilentWritesAndVerticalDirection()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#fill{position:absolute;width:100%;height:100%;}#thumb{position:absolute;width:10px;height:10px;}</style></head><body>"+
                "<urui-slider id='host' style='display:block;position:relative;width:200px;height:40px;'>"+
                "<input id='input' type='range' min='0' max='100' value='50' style='position:absolute;width:100%;height:100%;opacity:0;'>"+
                "<div id='fill'></div><div id='thumb'></div></urui-slider></body></html>","ui/slider-skin.html");
            var control=document.Q<Slider>("input");var fill=document.Q("fill");var thumb=document.Q("thumb");var host=document.Q("host");
            using var visual=new SliderVisual(control,fill,thumb);var changes=0;control.RegisterValueChangedCallback(_=>changes++);
            document.SynchronizeLayout();Assert.That(fill.Layout.width,Is.EqualTo(100).Within(.01f));
            control.SetAttribute("min","20");document.SynchronizeLayout();Assert.That(fill.Layout.width,Is.EqualTo(75).Within(.01f));
            control.SetValueWithoutNotify(80);document.SynchronizeLayout();Assert.That(fill.Layout.width,Is.EqualTo(150).Within(.01f));
            host.Style.SetWidth(40);host.Style.SetHeight(200);control.Direction=SliderDirection.Vertical;document.SynchronizeLayout();
            Assert.That(fill.Layout.height,Is.EqualTo(150).Within(.01f));Assert.That(fill.Layout.width,Is.EqualTo(40).Within(.01f));
            Assert.That(thumb.Layout.y,Is.EqualTo(150).Within(.01f));Assert.AreEqual("vertical",host.GetAttribute("data-urui-orientation"));
            Assert.AreEqual(0,changes);
        }

        [Test]
        public void IntegerSliderSupportsTheSameAuthoredSkinSurface()
        {
            Load("<urui-slider style='display:block;position:relative;width:200px;height:40px;'>"+
                "<input id='input' type='range' data-urui-type='URUI.Controls.IntegerSlider' min='0' max='20' value='5' style='position:absolute;width:100%;height:100%;opacity:0;'>"+
                "<div id='fill' style='height:100%;'></div><div id='thumb' style='position:absolute;width:10px;height:10px;'></div></urui-slider>");
            var control=document.Q<IntegerSlider>("input");var fill=document.Q("fill");
            using var visual=new IntegerSliderVisual(control,fill,document.Q("thumb"));var changes=0;
            control.RegisterValueChangedCallback(_=>changes++);document.SynchronizeLayout();
            Assert.That(fill.Layout.width,Is.EqualTo(50).Within(.01f));
            visual.SetValueWithoutNotify(15);document.SynchronizeLayout();Assert.That(fill.Layout.width,Is.EqualTo(150).Within(.01f));
            Assert.AreEqual(0,changes);visual.Dispose();control.SetValueWithoutNotify(10);document.SynchronizeLayout();
            Assert.That(fill.Layout.width,Is.EqualTo(150).Within(.01f));
        }

        [Test]
        public void RangeDirectionUsesOnlyTheUruiAttribute()
        {
            Load("<input id='input' type='range' data-urui-orientation='vertical'><input id='unsupported' type='range' orientation='vertical'>");
            var slider=document.Q<Slider>("input");
            Assert.AreEqual(SliderDirection.Vertical,slider.Direction);
            Assert.AreEqual(SliderDirection.Horizontal,document.Q<Slider>("unsupported").Direction);
            slider.Direction=SliderDirection.Horizontal;
            Assert.AreEqual("horizontal",slider.GetAttribute("data-urui-orientation"));
            Assert.IsFalse(slider.HasAttribute("orientation"));
        }

        [Test]
        public void RangeValueWritesKeepAuthoredStepBaseAndAttribute()
        {
            Load("<input id='input' type='range' step='3' value='4'>");
            var slider = document.Q<Slider>("input");
            slider.Value = 5; Assert.AreEqual(4, slider.Value); Assert.AreEqual("4", slider.GetAttribute("value"));
            slider.Value = 6; Assert.AreEqual(7, slider.Value); Assert.AreEqual("4", slider.GetAttribute("value"));
            slider.SetAttribute("max", "5"); Assert.AreEqual(4, slider.Value);
        }

        [Test]
        public void InvertedRangeKeepsSemanticStepGridWithoutToggleNotifications()
        {
            Load("<input id='input' type='range' min='0' max='10' step='3' value='3'>");
            var slider = document.Q<Slider>("input"); var changes = 0;
            slider.RegisterValueChangedCallback(_ => changes++);
            slider.Inverted = true; Assert.AreEqual(3, slider.Value); Assert.AreEqual(0, changes);
            slider.Value = 7; Assert.AreEqual(6, slider.Value); Assert.AreEqual(1, changes);
            slider.Inverted = false; Assert.AreEqual(6, slider.Value); Assert.AreEqual(1, changes);
        }

        [Test]
        public void ScrollViewKeepsAnExplicitAuthorContentContainerAndItsWidth()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}" +
                "urui-scroll-view{display:block;width:100px;height:80px;overflow:auto;scrollbar-width:none;}" +
                ".urui-scroll-content{display:block;width:300px;height:400px;}" +
                "</style></head><body><urui-scroll-view id='scroll'><div id='content' class='urui-scroll-content'></div></urui-scroll-view></body></html>",
                "ui/authored-scroll.html");
            var control = document.Q<ScrollView>("scroll"); var content = document.Q("content");
            Assert.AreSame(content, control.ContentContainer);
            Assert.AreSame(control, content.Parent); Assert.AreEqual(1, control.Hierarchy.ChildCount);
            document.SynchronizeLayout();
            Assert.That(content.Layout.width, Is.EqualTo(300).Within(.01f));
            Assert.That(control.ViewportRect.width, Is.EqualTo(100).Within(.01f));
            control.ScrollOffset = new Vector2(125, 220);
            Assert.That(control.ScrollOffset.x, Is.EqualTo(125).Within(.01f));
            Assert.That(control.ScrollOffset.y, Is.EqualTo(220).Within(.01f));
        }

        [Test]
        public void ScrollViewRejectsAmbiguousAuthorContentWithoutReplacingPage()
        {
            Load("<div id='kept'></div>"); var kept = document.Q("kept");
            Assert.Throws<InvalidOperationException>(() => Load("<urui-scroll-view><div>Missing content container</div></urui-scroll-view>"));
            Assert.IsTrue(kept.IsValid);
            Assert.Throws<InvalidOperationException>(() => Load("<urui-scroll-view><div class='urui-scroll-content'></div><div class='urui-scroll-content'></div></urui-scroll-view>"));
            Assert.IsTrue(kept.IsValid);
        }

        [Test]
        public void AuthoredScrollbarTracksOffsetMinimumSizeDragAndPageInput()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/ScrollSkin"), "/urui/tests/scroll.html");
            var type = typeof(Document).Assembly.GetType("URUI.Controls.ScrollbarVisual");
            Assert.IsNotNull(type, "滚动条应使用作者轨道和滑钮节点。");
            var viewport = document.Q<ScrollView>("viewport"); var track = document.Q("track"); var thumb = document.Q("thumb");
            void Refresh() { document.SynchronizeLayout(); document.SynchronizeLayout(); }
            Refresh();
            Assert.That(viewport.ViewportRect.width, Is.EqualTo(100).Within(.01f));
            Assert.That(thumb.Layout.height, Is.EqualTo(18).Within(.01f));
            viewport.ScrollOffset = new Vector2(0, 160); Refresh();
            Assert.That(thumb.Layout.y, Is.EqualTo(31).Within(.01f));
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(115, 50), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Move, new Vector2(115, 81), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(115, 81));
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(320).Within(.01f));
            viewport.ScrollOffset = Vector2.zero; Refresh();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(115, 60), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(115, 60));
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(80).Within(.01f));
            track.Focus(); surface.Input.SendKey(KeyCode.End, true);
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(320).Within(.01f));
            thumb.Style.Set("font-size", "10px"); thumb.Style.Set("min-height", "2em"); Refresh();
            Assert.That(thumb.Layout.height, Is.EqualTo(20).Within(.01f));
            thumb.Style.Set("min-height", "25%"); Refresh(); Assert.That(thumb.Layout.height, Is.EqualTo(20).Within(.01f));
            thumb.Style.Set("min-height", "auto"); Refresh(); Assert.That(thumb.Layout.height, Is.EqualTo(16).Within(.01f));
            document.Q("content").Style.SetHeight(40); document.Q("first").Style.SetHeight(40); document.Q("second").Style.SetHeight(0); Refresh();
            Assert.IsFalse(track.IsVisibleInHierarchy);
        }

        [Test]
        public void AuthoredScrollbarSupportsHorizontalLayoutAndDisabledDrag()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/ScrollSkin"), "/urui/tests/scroll.html");
            var viewport = document.Q<ScrollView>("viewport"); var track = document.Q("track"); var thumb = document.Q("thumb");
            var frame = document.Q("frame"); frame.SetAttribute("data-urui-orientation", "horizontal");
            frame.Style.SetWidth(80); frame.Style.SetHeight(112); track.SetAttribute("data-urui-orientation", "horizontal");
            document.Q("content").Style.SetWidth(400); document.Q("content").Style.SetHeight(100);
            document.Q("first").Style.SetWidth(400); document.Q("first").Style.SetHeight(100); document.Q("second").Style.SetHeight(0);
            document.SynchronizeLayout(); document.SynchronizeLayout();
            Assert.That(viewport.ViewportRect.width, Is.EqualTo(80).Within(.01f));
            Assert.That(viewport.ViewportRect.height, Is.EqualTo(100).Within(.01f));
            Assert.That(thumb.Layout.width, Is.EqualTo(18).Within(.01f));
            viewport.ScrollOffset = new Vector2(160, 0); document.SynchronizeLayout(); document.SynchronizeLayout();
            Assert.That(thumb.Layout.x, Is.EqualTo(31).Within(.01f));
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(50, 115), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Move, new Vector2(81, 115), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(81, 115));
            Assert.That(viewport.ScrollOffset.x, Is.EqualTo(320).Within(.01f));
            viewport.SetEnabled(false); document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(20, 115), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(20, 115));
            Assert.That(viewport.ScrollOffset.x, Is.EqualTo(320).Within(.01f));
            Assert.AreEqual("true", track.GetAttribute("aria-disabled"));
        }

        [Test]
        public void FoldoutPreservesAuthorHeaderAndUsesUruiStateAttributes()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/OtherControls"), "/urui/tests/other.html");
            var control = document.Q<Foldout>("fold"); var header = document.Q<Button>("header");
            Assert.AreSame(header, control.Hierarchy.Children()[1]); // Formatting text remains before the author button.
            Assert.AreSame(document.Q("fold-content"), control.ContentContainer);
            Assert.IsFalse(control.Value); Assert.AreEqual("Section", control.Text);
            var changes = 0; control.RegisterValueChangedCallback(_ => changes++);
            document.SynchronizeLayout(); header.Focus(); surface.Input.SendKey(KeyCode.RightArrow, true);
            Assert.IsTrue(control.Value); Assert.AreEqual(1, changes);
            control.SetValueWithoutNotify(false); Assert.AreEqual(1, changes);
            Assert.AreEqual("false", control.GetAttribute("data-urui-expanded"));
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(20, 20), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(20, 20));
            Assert.IsTrue(control.Value); Assert.AreEqual(2, changes);
            control.SetAttribute("data-urui-label", "Updated"); Assert.AreEqual("Updated", control.Text);
            control.SetAttribute("data-urui-expanded", "false"); Assert.IsFalse(control.Value);
            document.SynchronizeLayout(); Assert.IsFalse(document.Q("fold-content").IsVisibleInHierarchy);
        }

        [Test]
        public void ProgressPreservesAuthorSlotsAndProjectsTypedAndAttributeWrites()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/OtherControls"), "/urui/tests/other.html");
            var control = document.Q<ProgressBar>("progress"); var fill = document.Q("fill");
            Assert.AreSame(control, fill.Parent); Assert.AreSame(document.Q("progress-content"), control.ContentContainer);
            Assert.AreEqual("Loading", control.Text);
            var changes = 0; control.RegisterValueChangedCallback(_ => changes++);
            document.SynchronizeLayout(); Assert.That(fill.Layout.width, Is.EqualTo(50).Within(.01f));
            control.SetValueWithoutNotify(25); document.SynchronizeLayout();
            Assert.That(fill.Layout.width, Is.EqualTo(75).Within(.01f)); Assert.AreEqual(0, changes);
            control.SetAttribute("value", "15"); document.SynchronizeLayout();
            Assert.AreEqual(15, control.Value); Assert.That(fill.Layout.width, Is.EqualTo(25).Within(.01f));
            control.HighValue = 40; Assert.AreEqual("40", control.GetAttribute("max"));
            control.SetAttribute("data-urui-label", "Updated"); Assert.AreEqual("Updated", control.Text);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void ProgressValueFromButtonCallbackNotifiesOnce()
        {
            Load("<button id='go'>Go</button><urui-progress-bar id='progress' value='0' style='display:block;position:relative;width:100px;height:20px;'></urui-progress-bar>");
            var control = document.Q<ProgressBar>("progress"); var changes = 0;
            control.RegisterValueChangedCallback(_ => changes++);
            document.Q<Button>("go").Clicked += () => control.Value = 50;
            document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(20, 10), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(20, 10));
            Assert.AreEqual(50, control.Value); Assert.AreEqual(1, changes);
            document.SynchronizeLayout(); Assert.That(control.Q(className: "urui-progress-fill").Layout.width, Is.EqualTo(50).Within(.01f));
        }

        [Test]
        public void ProgressValueFromNativeRangeCallbackProjectsAndNotifiesOnce()
        {
            Load("<input id='slider' type='range' min='0' max='100' value='0'><urui-progress-bar id='progress' value='0' style='display:block;position:relative;width:100px;height:20px;'></urui-progress-bar>");
            var progress = document.Q<ProgressBar>("progress"); var changes = 0;
            progress.RegisterValueChangedCallback(_ => changes++);
            document.Q<Slider>("slider").RegisterValueChangedCallback(evt => progress.Value = evt.NewValue);
            document.Q<Slider>("slider").SetAttribute("min", "10");
            Assert.AreEqual(10, progress.Value); Assert.AreEqual(1, changes);
            document.SynchronizeLayout(); Assert.That(progress.Q(className: "urui-progress-fill").Layout.width, Is.EqualTo(10).Within(.01f));
        }

        [Test]
        public void CustomControlLegacyAttributesDoNotDriveUruiState()
        {
            Load("<urui-foldout id='fold' text='Legacy' expanded='false'></urui-foldout><urui-progress-bar id='progress' text='Legacy'></urui-progress-bar>");
            Assert.IsTrue(document.Q<Foldout>("fold").Value);
            Assert.AreEqual("", document.Q<Foldout>("fold").Text); Assert.AreEqual("", document.Q<ProgressBar>("progress").Text);
        }

        [Test]
        public void IncompleteAuthorControlSlotsPreserveThePreviousDocument()
        {
            Load("<div id='kept'></div>"); var kept = document.Q("kept");
            Assert.Throws<InvalidOperationException>(() => Load("<urui-foldout><button class='urui-foldout-header'>Missing content</button></urui-foldout>"));
            Assert.IsTrue(kept.IsValid);
            Assert.Throws<InvalidOperationException>(() => Load("<urui-progress-bar><div class='urui-progress-fill'></div></urui-progress-bar>"));
            Assert.IsTrue(kept.IsValid);
        }

        [Test]
        public void CanonicalSliderSkinInitializesFromHtmlWithoutControllerGlue()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SliderSkin"), "/urui/tests/slider.html");
            document.SynchronizeLayout();
            Assert.That(document.Q("fill").Layout.width, Is.EqualTo(50).Within(.01f));
            document.Q<Slider>("input").SetValueWithoutNotify(75); document.SynchronizeLayout();
            Assert.That(document.Q("fill").Layout.width, Is.EqualTo(75).Within(.01f));
        }

        [Test]
        public void CanonicalSelectSkinInitializesFromHtmlWithoutControllerGlue()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            Assert.AreEqual("A", document.Q("value").Text); Assert.AreEqual(3, document.Q("popup").ChildCount);
            document.Q<Dropdown>("input").SetValueWithoutNotify("c");
            Assert.AreEqual("C", document.Q("value").Text);
        }

        [Test]
        public void CanonicalScrollSkinInitializesFromHtmlWithoutControllerGlue()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/ScrollSkin"), "/urui/tests/scroll.html");
            document.SynchronizeLayout(); document.Q<ScrollView>("viewport").ScrollOffset = new Vector2(0, 160);
            document.SynchronizeLayout(); document.SynchronizeLayout();
            Assert.That(document.Q("thumb").Layout.y, Is.EqualTo(31).Within(.01f));
        }

        [Test]
        public void CanonicalSelectCloneRebindsOneSetOfGeneratedOptions()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            var clone = document.Q("select-host").Clone();
            Assert.AreEqual(3, clone.Q("popup").ChildCount);
            Assert.AreEqual("A", clone.Q("value").Text);
        }

        [Test]
        public void UnchangedAutomaticSelectKeepsItsLabelTextNode()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            var text = document.Q("value")[0];
            document.SynchronizeLayout(); document.SynchronizeLayout();
            Assert.IsTrue(text.IsValid, "没有状态变化时不应重建标签的文本子节点。");
        }

        [Test]
        public void CanonicalSkinInAReplacedFragmentInitializesWithoutQueriesOrGlue()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/controls.css'></head><body><div id='mount'></div></body></html>", "/urui/tests/fragment.html");
            document.Q("mount").SetInnerHtml("<urui-slider style='width:100px;'><input type='range' value='50'>" +
                "<div id='fragment-fill' class='urui-slider-fill'></div><div class='urui-slider-thumb'></div></urui-slider>", "/urui/tests/slider-fragment.html");
            var fill = document.Q("fragment-fill");
            document.SynchronizeLayout(); document.SynchronizeLayout();
            Assert.That(fill.Layout.width, Is.EqualTo(50).Within(.01f));
        }

        [Test]
        public void AuthoredSelectSkinTracksSilentValuesChoicesAndDisabledOptions()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            var type = typeof(Document).Assembly.GetType("URUI.Controls.DropdownVisual");
            Assert.IsNotNull(type, "下拉框皮肤应组合作者按钮和列表，保留标准 select 作为值来源。");
            var control = document.Q<Dropdown>("input"); var popup = document.Q("popup");
            var host = document.Q("select-host");
            var changes = 0;
            using var callback = control.RegisterValueChangedCallback(_ => changes++);
            Assert.AreEqual("A", document.Q("value").Text); Assert.AreEqual(3, popup.ChildCount);
            document.SynchronizeLayout();
            Assert.IsFalse(popup.IsVisibleInHierarchy); Assert.IsFalse(document.Q("prototype").IsVisibleInHierarchy);
            control.SetValueWithoutNotify("c"); Assert.AreEqual("C", document.Q("value").Text); Assert.AreEqual(0, changes);
            document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 26), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 26));
            Assert.IsFalse(popup.HasAttribute("hidden")); document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 52), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 52));
            Assert.AreEqual("a", control.Value); Assert.AreEqual(1, changes); Assert.IsTrue(popup.HasAttribute("hidden"));
            document.SynchronizeLayout(); Assert.IsFalse(popup.IsVisibleInHierarchy);
            document.Q("trigger").Focus(); surface.Input.SendKey(KeyCode.DownArrow, true);
            Assert.AreEqual("c", control.Value); Assert.AreEqual(2, changes);
            control.SetEnabled(false); Assert.IsFalse(document.Q("trigger").EnabledSelf);
            control.SetEnabled(true); control.Choices = new[] { "D", "E" };
            Assert.AreEqual(2, popup.ChildCount); Assert.AreEqual("D", document.Q("value").Text);
            document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 26), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 26));
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(120, 120), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(120, 120));
            Assert.IsTrue(popup.HasAttribute("hidden"), "点击控件外的空白区域也应收起列表。");
            control[1].SetAttribute("hidden", "");
            document.SynchronizeLayout(); Assert.IsTrue(popup[1].HasAttribute("hidden"));
            document.Q("trigger").Focus(); surface.Input.SendKey(KeyCode.DownArrow, true);
            Assert.AreEqual("D", control.Value, "键盘应跳过隐藏选项。");
            host.Destroy(); Assert.IsFalse(popup.IsValid);
        }

        [Test]
        public void AuthoredSelectSkinClosesOnUnload()
        {
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            var popup = document.Q("popup");
            document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 26), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 26));
            Assert.IsFalse(popup.HasAttribute("hidden"));
            document.Unload(); Assert.IsFalse(popup.IsValid);
            Load("<button id='next'>Next</button>"); document.SynchronizeLayout();
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(20, 20), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(20, 20));
            Assert.IsTrue(document.Q("next").IsValid);
        }

        [Test]
        public void SelectOptionsRemainDirectAuthorChildrenThroughChoiceReplacement()
        {
            Load("<select id='select'><option id='first' value='a'>A</option><option id='second' value='b' selected>B</option></select>");
            var control = document.Q<Dropdown>("select");
            Assert.AreEqual(2, control.ChildCount);
            Assert.AreSame(control, document.Q("first").Parent);
            Assert.AreSame(document.Q("second"), control[1]);
            var removed = document.Q("first");
            control.Choices = new[] { "C", "D", "E" };
            document.SynchronizeLayout();
            Assert.IsFalse(removed.IsValid); Assert.AreEqual(3, control.ChildCount);
            Assert.AreEqual("option", control[0].TagName); Assert.AreSame(control, control[0].Parent);
            control.SetValueWithoutNotify("E");
            Assert.AreEqual(2, control.SelectedIndex); Assert.AreEqual(3, control.ChildCount);
        }

        [Test]
        public void StandardControlsUseTypedElementsAndBooleanPresenceSemantics()
        {
            Load("<button id='button' disabled='false'>Disabled</button><input id='check' type='checkbox' checked='false'><input id='text' value='initial' readonly='false'><textarea id='area' readonly>Area</textarea><select id='select'><option value='a'>A</option><option value='b' selected='false'>B</option></select><label id='label' for='check'>Check</label>");
            Assert.IsFalse(document.Q<Button>("button").EnabledSelf);
            Assert.IsTrue(document.Q<Toggle>("check").Value);
            Assert.IsTrue(document.Q<TextField>("text").IsReadOnly);
            Assert.AreEqual("initial", document.Q<TextField>("text").Value);
            Assert.IsTrue(document.Q<TextField>("area").IsReadOnly);
            Assert.IsNotNull(document.Q<Dropdown>("select"));
            Assert.AreEqual("check", document.Q<Label>("label").For);
        }
        [TestCase("TEXT", typeof(TextField))]
        [TestCase("CHECKBOX", typeof(Toggle))]
        [TestCase("RaNgE", typeof(Slider))]
        [TestCase("NUMBER", typeof(FloatField))]
        public void HtmlInputTypeIsAsciiCaseInsensitive(string type, Type expected)
        {
            Load("<input id='input' type='" + type + "'>");
            Assert.AreEqual(expected, document.Q("input").GetType());
        }
        [Test]
        public void NumberAcceptsDecimalsAndExplicitIntegerControlRemainsAvailable()
        {
            Load("<input id='number' type='number' value='1.25'><input id='integer' type='number' data-urui-type='URUI.Controls.IntegerField' value='3'>");
            Assert.AreEqual(1.25f, document.Q<FloatField>("number").Value);
            Assert.AreEqual(3, document.Q<IntegerField>("integer").Value);
        }
        [Test]
        public void NumberLiveValueDoesNotRewriteAuthoredValueOrStepBase()
        {
            Load("<input id='number' type='number' step='2' value='1.25'>");
            var field = document.Q<FloatField>("number"); field.Value = 4;
            Assert.AreEqual("1.25", field.GetAttribute("value"));
            field.Focus(); surface.Input.SendKey(KeyCode.UpArrow, true); surface.Input.SendKey(KeyCode.UpArrow, false);
            Assert.AreEqual(5.25f, field.Value); Assert.AreEqual("1.25", field.GetAttribute("value"));
            field.SetAttribute("value", "100"); Assert.AreEqual(5.25f, field.Value);
            surface.Input.SendKey(KeyCode.UpArrow, true); surface.Input.SendKey(KeyCode.UpArrow, false);
            Assert.AreEqual(6, field.Value);
        }
        [TestCase("min='2' max='5' step='2' value='3'", 4f, 2f)]
        [TestCase("min='2' max='5' step='2' value='4'", 4f, 2f)]
        [TestCase("min='-1' max='1' step='0.25' value='0.5'", .75f, .5f)]
        [TestCase("step='any' value='1.25'", 2.25f, 1.25f)]
        [TestCase("min='0' max='10' step='any' value='1.25'", 2.25f, 1.25f)]
        [TestCase("step='bogus' value='1.25'", 2.25f, 1.25f)]
        public void NumberKeyboardSteppingUsesAuthoredStepBaseAndBounds(string attributes, float up, float down)
        {
            Load("<input id='number' type='number' data-urui-type='URUI.Controls.FloatField' " + attributes + ">");
            var field = document.Q<FloatField>("number"); field.Focus();
            surface.Input.SendKey(KeyCode.UpArrow, true); surface.Input.SendKey(KeyCode.UpArrow, false);
            Assert.AreEqual(up, field.Value);
            surface.Input.SendKey(KeyCode.DownArrow, true); surface.Input.SendKey(KeyCode.DownArrow, false);
            Assert.AreEqual(down, field.Value);
        }
        [Test]
        public void ReadonlyInputRejectsTextAndReleasesOnAttributeRemoval()
        {
            Load("<input id='input' type='text' value='initial' readonly>");
            var field = document.Q<TextField>("input"); field.Focus();
            surface.Input.SendText("X"); Assert.AreEqual("initial", field.Value);
            field.RemoveAttribute("readonly"); surface.Input.SendText("X");
            StringAssert.Contains("X", field.Value);
        }
        [Test]
        public void LabelActivatesOnlyItsAssociatedControl()
        {
            Load("<label id='label' for='check'>Check</label><input id='check' type='checkbox'>");
            document.SynchronizeLayout(); var point = document.Q("label").WorldBound.center;
            surface.Input.SendPointer(PointerPhase.Down, point, pressedButtons: 1); surface.Input.SendPointer(PointerPhase.Up, point);
            Assert.IsTrue(document.Q<Toggle>("check").Value);
        }
        [Test]
        public void UruiCustomControlsUseExplicitNamesAndRequireClosingTags()
        {
            Load("<urui-scroll-view id='scroll'></urui-scroll-view><urui-foldout id='fold'></urui-foldout><urui-list-view id='list'></urui-list-view>");
            Assert.IsNotNull(document.Q<ScrollView>("scroll")); Assert.IsNotNull(document.Q<Foldout>("fold")); Assert.IsNotNull(document.Q<ListView>("list"));
            var previous = document.Q("scroll");
            Assert.Throws<InvalidOperationException>(() => Load("<urui-scroll-view/>")); Assert.IsTrue(previous.IsValid);
        }
        [Test]
        public void HtmlTabindexOrdersNavigationAndNegativeValuesKeepProgrammaticFocus()
        {
            Load("<button id='ordinary'>Ordinary</button><button id='second' tabindex='2'>Second</button><div id='first' tabindex='1' style='width:100px;height:30px;'>First</div><button id='excluded' tabindex='-1'>Excluded</button><button id='disabled' tabindex='0' disabled>Disabled</button>");
            document.SynchronizeLayout();
            surface.Input.SendKey(KeyCode.Tab, true); Assert.AreSame(document.Q("first"), document.FocusedElement);
            surface.Input.SendKey(KeyCode.Tab, true); Assert.AreSame(document.Q("second"), document.FocusedElement);
            surface.Input.SendKey(KeyCode.Tab, true); Assert.AreSame(document.Q("ordinary"), document.FocusedElement);
            surface.Input.SendKey(KeyCode.Tab, true); Assert.AreSame(document.Q("first"), document.FocusedElement);
            var excluded = document.Q("excluded"); Assert.IsTrue(excluded.Focus());
            surface.Input.SendKey(KeyCode.Tab, true); Assert.AreSame(document.Q("first"), document.FocusedElement);
            excluded.TabIndex = -2; Assert.AreEqual("-2", excluded.GetAttribute("tabindex")); Assert.AreEqual(-2, excluded.TabIndex);
            document.Q("first").TabIndex = int.MaxValue;
            document.Q("second").Focus(); surface.Input.SendKey(KeyCode.Tab, true);
            Assert.AreSame(document.Q("first"), document.FocusedElement);
        }
    }
}
