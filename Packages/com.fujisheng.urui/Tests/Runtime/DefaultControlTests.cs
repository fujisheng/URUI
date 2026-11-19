using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Hosting;
using URUI.Events;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class DefaultControlTests
    {
        sealed class Clock : IUIClock
        {
            public double Now
            {
                get;
                set;
            }
            public float Delta => .2f;
        }
        GameObject root;
        GameObject camera;
        Surface surface;
        Document document;
        Clock clock;
        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Default control camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("Default controls");
            surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            clock = new Clock();
            surface.UIClock = clock;
            document = root.AddComponent<Document>();
            document.Surface = surface;
            document.TextResources = new[] { new TextResource("/urui/controls.css", UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
        }
        void Load(string body)
        {
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/controls.css'><style>body{margin:0;}#button{position:absolute;left:10px;top:10px;width:100px;height:50px;}</style></head><body>" + body + "</body></html>", "/default-controls.html");
            Tick();
        }
        void Tick()
        {
            clock.Now += .2;
            document.SynchronizeLayout();
            clock.Now += .2;
            document.SynchronizeLayout();
        }
        void Tint(byte r, byte a = 255)
        {
            var value = document.Styles.GetComputed(document.Q("button")).GetColor("--urui-control-tint");
            Assert.That(value.r, Is.EqualTo(r / 255f).Within(1.1f / 255));
            Assert.That(value.g, Is.EqualTo(r / 255f).Within(1.1f / 255));
            Assert.That(value.b, Is.EqualTo(r / 255f).Within(1.1f / 255));
            Assert.That(value.a, Is.EqualTo(a / 255f).Within(1.1f / 255));
        }
        [Test]
        public void SpaceShowsPressedGraphicUntilReleaseAndClicksOnce()
        {
            Load("<button id='button'></button>");
            var button = document.Q<Button>("button");
            int clicks = 0;
            button.Clicked += () => clicks++;
            Assert.That(button.Focus(), Is.True);
            Tick();
            Tint(245);
            surface.Input.SendKey(KeyCode.Space, true);
            Tick();
            Tint(200);
            Assert.That(clicks, Is.Zero);
            surface.Input.SendKey(KeyCode.Space, false);
            Tick();
            Tint(245);
            Assert.That(clicks, Is.EqualTo(1));
        }
        [Test]
        public void DisablingAPressedButtonCancelsKeyboardActivationAndClearsPressedState()
        {
            Load("<button id='button'></button>");
            var button = document.Q<Button>("button");
            int clicks = 0;
            button.Clicked += () => clicks++;
            button.Focus();
            surface.Input.SendKey(KeyCode.Space, true);
            Tick();
            Tint(200);
            button.SetEnabled(false);
            Tick();
            Tint(200, 128);
            surface.Input.SendKey(KeyCode.Space, false);
            button.SetEnabled(true);
            Tick();
            Tint(255);
            Assert.That(clicks, Is.Zero);
        }
        [Test]
        public void SubmitShowsTransientPressedFeedbackWithoutAnotherClick()
        {
            Load("<button id='button'></button>");
            var button = document.Q<Button>("button");
            int clicks = 0;
            button.Clicked += () => clicks++;
            button.Focus();
            Tick();
            surface.Input.SendKey(KeyCode.Return, true);
            clock.Now += .05;
            document.SynchronizeLayout();
            clock.Now += .05;
            document.SynchronizeLayout();
            Assert.That(document.Styles.GetComputed(button).GetColor("--urui-control-tint").r, Is.LessThan(.94f));
            Tick();
            Tint(245);
            Assert.That(clicks, Is.EqualTo(1));
        }
        [Test]
        public void CloningAPressedButtonDoesNotCopyTransientKeyboardState()
        {
            Load("<button id='button'></button>");
            var button = document.Q<Button>("button");
            button.Focus();
            surface.Input.SendKey(KeyCode.Space, true);
            Tick();
            var clone = (Button)button.Clone();
            clone.Name = "clone";
            clone.Style.SetPixels("left", 200);
            button.Parent.Add(clone);
            Tick();
            Assert.That(document.Styles.GetComputed(clone).GetColor("--urui-control-tint").r, Is.EqualTo(1).Within(.001f));
        }
        [Test]
        public void DisabledAncestorUsesDisabledGraphicFeedback()
        {
            Load("<div id='group'><button id='button'></button></div>");
            document.Q("group").SetEnabled(false);
            Tick();
            Assert.That(document.Q("button").EnabledInHierarchy, Is.False);
            Tint(200, 128);
        }
        [Test]
        public void AuthorSizedButtonsAreNotClampedByTheDefaultTemplateSize()
        {
            Load("<button id='small' style='width:24px;height:24px;padding:0;border-width:0;'></button>");
            Assert.That(document.Q("small").Layout.width, Is.EqualTo(24).Within(.01));
            Assert.That(document.Q("small").Layout.height, Is.EqualTo(24).Within(.01));
        }
        [Test]
        public void DefaultFieldsUseNeutralSkinAndReadonlyPreservesTheSkin()
        {
            Load("<input id='text' type='text'><input id='readonly' type='text' readonly><input id='check' type='checkbox'><input id='range' type='range'>");
            foreach (var id in new[] { "text", "readonly", "check" })
            {
                var c = document.Styles.GetComputed(document.Q(id)).GetColor("background-color");
                Assert.That(c.r, Is.EqualTo(1).Within(.001f), id);
                Assert.That(c.g, Is.EqualTo(1).Within(.001f), id);
                Assert.That(c.b, Is.EqualTo(1).Within(.001f), id);
            }
            Assert.That(document.Q("check").Layout.width, Is.EqualTo(20).Within(.01));
            Assert.That(document.Q("range").Layout.height, Is.EqualTo(20).Within(.01));
            Assert.That(document.Q<TextField>("text").CaretBlinkRate, Is.EqualTo(.85f).Within(.0001f));
        }
        void LoadDropdown()
        {
            Load("<urui-select id='host' style='position:absolute;left:10px;top:10px;width:160px;height:30px;'><select id='input' tabindex='-1'><option value='a' selected>A</option><option value='b'>B</option></select><button id='trigger' class='urui-select-trigger' type='button'><span class='urui-select-value'></span><span class='urui-select-arrow'></span></button><button class='urui-select-option' role='option' hidden></button><div id='popup' class='urui-select-options' role='listbox' hidden></div></urui-select>");
        }
        void ClickTrigger()
        {
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(30, 20), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(30, 20));
            document.SynchronizeLayout();
            document.SynchronizeLayout();
        }
        [UnityTest]
        public IEnumerator DropdownFadesAndStopsHitTestingAsSoonAsItCloses()
        {
            LoadDropdown();
            var popup = document.Q("popup");
            ClickTrigger();
            Assert.That(document.Q("trigger").GetAttribute("aria-expanded"), Is.EqualTo("true"));
            yield return null;
            Assert.That(document.Styles.GetComputed(popup).GetNumber("opacity"), Is.LessThan(.1f));
            clock.Now += .075;
            document.SynchronizeLayout();
            yield return null;
            Assert.That(document.Styles.GetComputed(popup).GetNumber("opacity"), Is.InRange(.3f, .7f));
            Tick();
            yield return null;
            ClickTrigger();
            yield return null;
            Assert.That(popup.HasAttribute("hidden"), Is.True);
            Assert.That(popup.IsVisibleInHierarchy, Is.True, "退场仍绘制，不能立即display:none");
            Assert.That(popup.PickingMode, Is.EqualTo(URUI.Elements.PickingMode.Ignore));
            foreach (var row in popup.Children())
            {
                Assert.That(row.PickingMode, Is.EqualTo(URUI.Elements.PickingMode.Ignore));
            }
            Tick();
            yield return null;
            yield return null;
            Assert.That(popup.IsVisibleInHierarchy, Is.False);
        }
        [UnityTest]
        public IEnumerator ReopeningDuringDropdownExitDoesNotLetAnOldCompletionHideTheNewPopup()
        {
            LoadDropdown();
            var popup = document.Q("popup");
            ClickTrigger();
            Tick();
            yield return null;
            ClickTrigger();
            yield return null;
            Assert.That(popup.IsVisibleInHierarchy, Is.True);
            clock.Now += .05;
            document.SynchronizeLayout();
            ClickTrigger();
            Tick();
            yield return null;
            Assert.That(document.Q("trigger").GetAttribute("aria-expanded"), Is.EqualTo("true"));
            Assert.That(popup.IsVisibleInHierarchy, Is.True);
            document.Unload();
            Assert.That(popup.IsValid, Is.False);
        }
        [Test]
        public void PointerAndFocusFeedbackNeverReplaceAnAuthoredTransparentBackground()
        {
            Load("<button id='button' style='background-color:transparent;'></button>");
            surface.Input.SendPointer(PointerPhase.Move, new Vector2(30, 30));
            Tick();
            Tint(245);
            Assert.That(document.Styles.GetComputed(document.Q("button")).GetColor("background-color").a, Is.Zero);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(30, 30), pressedButtons: 1);
            Tick();
            Tint(200);
            Assert.That(document.Styles.GetComputed(document.Q("button")).GetColor("background-color").a, Is.Zero);
        }
    }
}
