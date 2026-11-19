using System;
using System.Runtime.InteropServices;
using URUI.Internal.Interop;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Rendering;
using URUI.Styles;
using URUI.Internal.Rendering;

namespace URUI.Tests
{
    public sealed class RenderProgramLayoutTests
    {
        [Test]
        public void NativeDescriptorLayoutsArePointerSized()
        {
            Assert.That(Marshal.SizeOf<ProgramParameterField>(), Is.EqualTo(IntPtr.Size == 8 ? 32 : 24));
            Assert.That(Marshal.SizeOf<ProgramParameterKeyword>(), Is.EqualTo(IntPtr.Size == 8 ? 16 : 12));
        }
        [Test]
        public void ComponentRangesAndOriginsCannotOverlap()
        {
            var number = new StylePropertyDefinition("--number", StyleValueType.Number);
            Assert.Throws<ArgumentException>(() => new RenderParameterLayout(1, new[] { new RenderParameterField(number, 0), new RenderParameterField(number, 0) }));
            Assert.Throws<ArgumentException>(() => new RenderParameterLayout(1, new[] { new RenderParameterField(number, 0, originOffset: 0) }));
            Assert.Throws<ArgumentException>(() => new RenderParameterLayout(1, new[] { new RenderParameterField(new StylePropertyDefinition("--color", StyleValueType.Color), 1) }));
            Assert.DoesNotThrow(() => new RenderParameterField(new StylePropertyDefinition("--image", StyleValueType.Image), 0));
            Assert.Throws<ArgumentException>(() => new RenderParameterField(new StylePropertyDefinition("--image", StyleValueType.Image), 0, keywords: new[] { new RenderKeywordMapping("none", 0) }));
        }
        [Test]
        public void NativeCssIdsFillDeclaredFloat4Components()
        {
            var camera = new GameObject("Program layout camera", typeof(Camera));
            camera.tag = "MainCamera";
            var host = new GameObject("Program layout document");
            var document = HtmlFixtureAssets.Create(host);
            NativeProgramLayout program = null;
            try
            {
                document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/TypedStyle"), "URUI/Tests/TypedStyle.html");
                document.SynchronizeLayout();
                var layout = new RenderParameterLayout(3, new[]{
                    new RenderParameterField(new StylePropertyDefinition("--fx-progress",StyleValueType.Number),0,originOffset:3),
                    new RenderParameterField(new StylePropertyDefinition("--padding",StyleValueType.Pixels),1),
                    new RenderParameterField(new StylePropertyDefinition("--tint",StyleValueType.Color),4),
                    new RenderParameterField(new StylePropertyDefinition("--test-edge",StyleValueType.Keyword),2,keywords:new[]{new RenderKeywordMapping("top",7),new RenderKeywordMapping("left",3)})});
                program = new NativeProgramLayout(document, "generic-layout", layout);
                var rows = new Vector4[3];
                var child = document.Q("child");
                program.ReadValues(child, rows);
                Assert.That(rows[0].x, Is.Zero);
                Assert.That(rows[0].y, Is.EqualTo(4));
                Assert.That(rows[0].z, Is.EqualTo(7));
                Assert.That(rows[1].x, Is.EqualTo(18f / 255).Within(.00001f));
                child.Style.SetNumber("--fx-progress", .625f);
                document.SynchronizeLayout();
                program.ReadValues(child, rows);
                Assert.That(rows[0].x, Is.EqualTo(.625f));
                document.Unload();
                Assert.Throws<InvalidOperationException>(() => program.ReadValues(child, rows));
            }
            finally
            {
                program?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
    }
}
