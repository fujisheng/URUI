using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;

namespace URUI.Tests
{
    public sealed class ResolvedLengthTests
    {
        delegate bool ReadLength(string name, out Length value);
        static object probe;
        static long Allocations(Action action)
        {
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action();recorder.Stop();var valid=recorder.Valid;var wrapped=recorder.WrappedAround;long count=0;
            for(var i=0;i<recorder.Count;i++)count+=recorder.GetSample(i).Count;
            Assert.That(valid,Is.True);Assert.That(wrapped,Is.False);return count;
        }
        [Test]
        public void RepeatedEnumStyleWritesReuseValidatedCanonicalValues()
        {
            var camera=new GameObject("Enum style camera",typeof(Camera));camera.tag="MainCamera";
            var root=new GameObject("Enum styles");
            try
            {
                var surface=root.AddComponent<Surface>();surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                document.LoadHtml("<!doctype html><html><head></head><body><div id='subject'></div></body></html>","/enums.html");
                var style=document.Q("subject").Style;
                Action write=()=>{for(var i=0;i<32;i++)style.Position=Position.Absolute;};
                for(var i=0;i<10;i++)write();
                Assert.That(Allocations(()=>probe=new byte[16]),Is.GreaterThan(0));
                Assert.That(Allocations(write),Is.Zero);
                Assert.That(style.Get("position"),Is.EqualTo("absolute"));
                style.FlexDirection=FlexDirection.RowReverse;Assert.That(style.Get("flex-direction"),Is.EqualTo("row-reverse"));
                Assert.Throws<ArgumentOutOfRangeException>(()=>style.Position=(Position)int.MaxValue);
                style.Position=Position.Unset;Assert.That(style.Get("position"),Is.Null);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera);probe=null;}
        }

        [Test]
        public void ResolvedLengthsReadCurrentNumericValuesWithoutAllocatingStrings()
        {
            var method=typeof(ElementResolvedStyle).GetMethod("TryGetLength",BindingFlags.Instance|BindingFlags.Public,null,new[]{typeof(string),typeof(Length).MakeByRefType()},null);
            Assert.That(method,Is.Not.Null,"空间绑定需要不物化 CSS 字符串的长度读取。");
            var camera=new GameObject("Resolved lengths camera",typeof(Camera));camera.tag="MainCamera";
            var root=new GameObject("Resolved lengths");
            try
            {
                var surface=root.AddComponent<Surface>();surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#subject{position:absolute;left:12.5px;right:auto;width:50px;height:30px;margin-left:25%;transform-origin:25% 10px;}</style></head><body><div id='subject'></div></body></html>","/lengths.html");
                document.SynchronizeLayout();var subject=document.Q("subject");
                var read=(ReadLength)Delegate.CreateDelegate(typeof(ReadLength),subject.ResolvedStyle,method);
                Assert.That(read("left",out var left),Is.True);Assert.That(left.Unit,Is.EqualTo(LengthUnit.Pixel));Assert.That(left.Value,Is.EqualTo(12.5f));
                Assert.That(read("right",out var right),Is.True);Assert.That(right.Unit,Is.EqualTo(LengthUnit.Auto));
                Assert.That(read("--urui-transform-origin-x",out var origin),Is.True);Assert.That(origin.Unit,Is.EqualTo(LengthUnit.Percent));Assert.That(origin.Value,Is.EqualTo(25));
                Assert.That(read("background-color",out _),Is.False);Assert.That(read("--absent",out _),Is.False);
                void Step(int i){subject.Style.SetPixels("margin-left",i+.125f);document.SynchronizeLayout();if(!read("margin-left",out var length)||length.Unit!=LengthUnit.Pixel||length.Value!=i+.125f)throw new InvalidOperationException("必须读到最新的原生长度，不能复用过期文本。");}
                for(var i=0;i<300;i++)Step(i);
                Assert.That(Allocations(()=>probe=new byte[16]),Is.GreaterThan(0));
                Assert.That(Allocations(()=>{for(var i=300;i<332;i++)Step(i);}),Is.Zero);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera);probe=null;}
        }
    }
}
