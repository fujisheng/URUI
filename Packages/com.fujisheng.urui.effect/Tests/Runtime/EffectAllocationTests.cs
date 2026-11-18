using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Documents;
using URUI.Hosting;
using URUI.Effect.Rendering;

namespace URUI.Effect.Tests
{
    public sealed class EffectAllocationTests
    {
        sealed class Clock : IUiClock { public double Now { get; set; } = 1; public float Delta => .01f; }
        static object probe;
        static long Allocations(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action(); recorder.Stop();
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            long count = 0;
            for (var i = 0; i < recorder.Count; ++i) { count += recorder.GetSample(i).Count; }
            return count;
        }
        static void ValidateChannel()
        {
            Assert.That(Allocations(() => { }), Is.Zero);
            Assert.That(Allocations(() => { probe = new byte[4096]; GC.KeepAlive(probe); }), Is.GreaterThan(0));
            probe = null;
        }

        sealed class CountingClock:IUiClock
        {
            internal double Value=1;internal int Reads;
            public double Now{get{Reads++;return Value;}}public float Delta=>.01f;
        }
        [Test]
        public void EquivalentUiTopologiesShareTriangleFactsAcrossFiveThousandSources()
        {
            var type=typeof(EffectModule).Assembly.GetType("URUI.Effect.Rendering.UiEffectGeometry",true);
            var acquire=type.GetMethod("Acquire",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var leases=new System.Collections.Generic.List<System.IDisposable>();Mesh first=null;
            URUI.Rendering.Effects.EffectGeometry Source(int index,bool red=false) {
                var x=index*3f;var width=2+index%7;
                return (URUI.Rendering.Effects.EffectGeometry)System.Activator.CreateInstance(typeof(URUI.Rendering.Effects.EffectGeometry),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{(ulong)(index+1),new[]{new Vector3(x,0),new Vector3(x+width,0),new Vector3(x+width,3),new Vector3(x,3)},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},new Color32[]{red?Color.red:Color.white,red?Color.red:Color.white,red?Color.red:Color.white,red?Color.red:Color.white},new[]{0,1,2,0,2,3}},null);
            }
            Mesh Acquire(URUI.Rendering.Effects.EffectGeometry source) {
                var lease=(System.IDisposable)acquire.Invoke(null,new object[]{source,ShadowMode.None,1});leases.Add(lease);
                return (Mesh)lease.GetType().GetProperty("Mesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lease);
            }
            try {
                for(var i=0;i<5000;i++){var mesh=Acquire(Source(i));if(!first)first=mesh;else Assert.That(mesh,Is.SameAs(first),"源身份和尺寸不同，但归一化三角流相同，应复用模板及其事实。");}
                Assert.That(Acquire(Source(5001,true)),Is.Not.SameAs(first),"非均匀颜色/颜色变化必须参与拓扑身份。");
            } finally {foreach(var lease in leases)lease.Dispose();}
        }
        [UnityTest] public IEnumerator UiImageProgramsBatchAndKeepNumericUpdatesAllocationFree()=>VerifyUiPrograms(false,false);
        [UnityTest] public IEnumerator UiElementProgramsKeepNumericUpdatesAllocationFree()=>VerifyUiPrograms(true,false);
        [UnityTest] public IEnumerator UiShadowDistanceOnlyUpdatesGpuParameters()=>VerifyUiPrograms(false,true);
        static IEnumerator VerifyUiPrograms(bool captured,bool shadow)
        {
            using var fixture=new EffectRenderFixture();
            var images="<img id='subject' src='input.png' style='left:0;width:32px;'>"+
                "<img src='input.png' style='left:40px;width:32px;'><img src='input.png' style='left:80px;width:32px;'><img src='input.png' style='left:120px;width:32px;'>";
            var subtree="<div id='subject'><img src='input.png' style='position:absolute;width:80px;height:128px;'><img src='input.png' style='position:absolute;left:32px;width:80px;height:128px;'></div>";
            fixture.Load("--urui-fx-tone-filter:grayscale;--urui-fx-tone-intensity:.5;"+(shadow?"--urui-fx-shadow-mode:shadow3;--urui-fx-shadow-distance-x:2;--urui-fx-shadow-distance-y:2;":""),captured?subtree:images,captured?"#subject":"img");
            var element=fixture.Document.Q("subject");var frame=0;
            Action cycle=()=>{element.Style.Set(shadow?"--urui-fx-shadow-distance-x":"--urui-fx-tone-intensity",++frame%2==0?(shadow?"2":".5"):(shadow?"6":".75"));fixture.Document.SynchronizeLayout();};
            for(var i=0;i<300;i++)cycle();
            object renderer=null;foreach(var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))if(component.GetType().FullName=="URUI.Internal.Hosting.RenderHost")renderer=component;
            object Read(string name)=>renderer.GetType().GetProperty(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(renderer);
            if(!captured)Assert.That(Read("RecordedGeometryDraws"),Is.EqualTo(1),"兼容 UIEffect 图像只因参数差异不能拆成逐节点回调。");
            var uploaded=Read("GeometryUploadBytes");ValidateChannel();Assert.That(Allocations(()=>{for(var i=0;i<32;i++)cycle();}),Is.Zero);
            Assert.That(Read("GeometryUploadBytes"),Is.EqualTo(uploaded));yield return null;
        }
        sealed class CallbackGuardFactory:URUI.Rendering.Effects.IEffectFactory
        {
            readonly URUI.Rendering.Effects.IEffectFactory factory;
            internal CallbackGuardFactory(URUI.Rendering.Effects.IEffectFactory factory){this.factory=factory;}
            public URUI.Rendering.Effects.IElementEffect Create(URUI.Rendering.Effects.EffectContext context)=>new CallbackGuardInstance(factory.Create(context),context.Element);
        }
        sealed class CallbackGuardInstance:URUI.Rendering.Effects.IElementEffect,URUI.Rendering.Effects.IParameterImageEffect,
            URUI.Rendering.Effects.IParameterImageRuntime,URUI.Rendering.Effects.IParameterImageGeometry,URUI.Rendering.Effects.IParameterImageResources
        {
            readonly URUI.Rendering.Effects.IElementEffect inner;readonly URUI.Elements.Element element;
            internal CallbackGuardInstance(URUI.Rendering.Effects.IElementEffect inner,URUI.Elements.Element element){this.inner=inner;this.element=element;}
            void Guard(string member)=>Assert.Throws<InvalidOperationException>(()=>element.Style.SetNumber("opacity",.5f),member+" 必须处于效果只读回调范围。");
            public Material Program{get{Guard("Program");return ((URUI.Rendering.Effects.IParameterImageEffect)inner).Program;}}
            public URUI.Rendering.RenderTimeInput TimeInput{get{Guard("TimeInput");return ((URUI.Rendering.Effects.IParameterImageEffect)inner).TimeInput;}}
            public URUI.Rendering.ExternalGeometryInput Geometry{get{Guard("Geometry");return ((URUI.Rendering.Effects.IParameterImageGeometry)inner).Geometry;}}
            public Matrix4x4 GeometryTransform{get{Guard("GeometryTransform");return ((URUI.Rendering.Effects.IParameterImageGeometry)inner).GeometryTransform;}}
            public int RuntimeRowCount{get{Guard("RuntimeRowCount");return ((URUI.Rendering.Effects.IParameterImageRuntime)inner).RuntimeRowCount;}}
            public void CopyRuntimeParameters(Span<Vector4> values){Guard("CopyRuntimeParameters");((URUI.Rendering.Effects.IParameterImageRuntime)inner).CopyRuntimeParameters(values);}
            public void CommitResources(){Guard("CommitResources");((URUI.Rendering.Effects.IParameterImageResources)inner).CommitResources();}
            public void PrepareProgram(in URUI.Rendering.Effects.EffectProgramPreparation value)=>((URUI.Rendering.Effects.IParameterImageEffect)inner).PrepareProgram(value);
            public void GetPreviewParameters(Span<Vector4> values)=>((URUI.Rendering.Effects.IParameterImageEffect)inner).GetPreviewParameters(values);
            public void Prepare(URUI.Rendering.Effects.EffectFrame value)=>inner.Prepare(value);
            public void Record(URUI.Rendering.Effects.EffectRenderContext value)=>inner.Record(value);
            public void Dispose()=>inner.Dispose();
        }
        [UnityTest] public IEnumerator TypedProgramSubmissionGettersAndRuntimeCopyCannotMutateDom()
        {
            using var fixture=new EffectRenderFixture();Assert.That(fixture.Document.Effects.TryGet("ui",out var definition),Is.True);
            fixture.Document.Effects.Register(new URUI.Rendering.Effects.EffectDefinition("guarded",URUI.Rendering.Effects.EffectKind.Image,
                new CallbackGuardFactory(definition.Factory),"guarded-ui","premultiplied active",renderProgram:definition.RenderProgram,renderParameters:definition.RenderParameters));
            fixture.Load("--urui-effect:none;--urui-image-effect:guarded;");
            Assert.DoesNotThrow(()=>fixture.Document.SynchronizeLayout());yield return null;
        }
        sealed class ProgramBinding:URUI.Rendering.Effects.IEffectParameterBinding
        {
            internal readonly URUI.Elements.Element Element;internal int Layouts,Writes,Coordinates;internal bool Fail;
            internal ProgramBinding(URUI.Elements.Element element){Element=element;}
            void Guard()=>Assert.Throws<InvalidOperationException>(()=>Element.Style.SetNumber("opacity",.5f));
            public URUI.Rendering.Effects.EffectLayout ResolveLayout(URUI.Rendering.Effects.EffectLayout layout){Guard();Layouts++;return layout;}
            public URUI.Rendering.Effects.EffectProgramCoordinates ResolveCoordinates(URUI.Rendering.Effects.EffectProgramCoordinates coordinates){Guard();Coordinates++;return coordinates;}
            public void ResolveProgramParameters(string program,Span<Vector4> values)
            {Guard();Assert.That(program,Is.EqualTo("ui"));Assert.That(values.Length,Is.EqualTo(44));Writes++;if(Fail)throw new InvalidOperationException("parameter binding probe");}
        }
        [UnityTest] public IEnumerator TypedProgramBindingKeepsLayoutLazyAndGuardsDomAcrossFailureAndRelease()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("--urui-fx-tone-filter:grayscale;");fixture.Document.SynchronizeLayout();
            var probe=new ProgramBinding(fixture.Document.Q("subject"));var lease=fixture.Document.Effects.BindProgramParameters(probe.Element,probe);
            fixture.Document.SynchronizeLayout();Assert.That(probe.Writes,Is.EqualTo(1),"提交必须使用准备时已冻结的数值，不再次调用参数解析器。");Assert.That(probe.Coordinates,Is.GreaterThan(0));Assert.That(probe.Layouts,Is.Zero);
            fixture.Document.Effects.TryGetFrame(probe.Element,out var retained);Assert.That(probe.Layouts,Is.EqualTo(1));var width=retained.Layout.ContentRect.width;
            probe.Fail=true;var error=Assert.Throws<InvalidOperationException>(()=>fixture.Document.SynchronizeLayout());Assert.That(error.Message,Does.Contain("parameter binding probe"));
            probe.Fail=false;fixture.Document.SynchronizeLayout();probe.Element.Style.SetNumber("opacity",.75f);
            lease.Dispose();var count=probe.Writes;fixture.Document.SynchronizeLayout();Assert.That(probe.Writes,Is.EqualTo(count));Assert.That(retained.Layout.ContentRect.width,Is.EqualTo(width));
            yield return null;
        }
        [UnityTest] public IEnumerator TypedControllerParametersMatchIndependentSnapshotBindingPixels()
        {
            using var fixture=new EffectRenderFixture();var preset=ScriptableObject.CreateInstance<EffectPreset>();
            try {
                for(var mode=0;mode<3;mode++) {
                    fixture.Load(mode==0?"":"--urui-fx-color-hue-shift:.2;--urui-fx-color-alpha:.45;--urui-fx-tone-intensity:.6 !important;");
                    preset.SetParameters(new EffectParameters{ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f,
                        ColorFilter=mode==0?ColorFilter.Contrast:ColorFilter.HsvModifier,Color=new Color(.1f,.2f,.3f,.7f),ColorIntensity=.8f,
                        SamplingFilter=mode==2?SamplingFilter.RgbShift:SamplingFilter.None,SamplingIntensity=.2f,
                        ShadowMode=ShadowMode.Shadow3,ShadowDistance=new Vector2(2,-1),ShadowIteration=2,ShadowColor=Color.yellow,
                        GradationMode=GradationMode.DiagonalToLeftBottom,GradationColor1=Color.red,GradationColor2=Color.cyan,GradationOffset=.2f});
                    var controller=fixture.Module.GetController(fixture.Document.Q("subject"));controller.Apply(preset);
                    controller.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual,Channels=EffectTweenChannels.Tone|EffectTweenChannels.Color}).SetTime(.35);
                    var typed=fixture.Render();fixture.Document.Effects.TryGetFrame(controller.Element,out var frame);var element=controller.Element;
                    controller.Dispose();using var referenceBinding=fixture.Document.Effects.BindParameters(element,layout=>layout.WithStyle(frame.Style));
                    var snapshot=fixture.Render();float maximum=0;
                    for(var i=0;i<typed.Length;i++)for(var channel=0;channel<4;channel++)maximum=Mathf.Max(maximum,Mathf.Abs(typed[i][channel]-snapshot[i][channel]));
                    Assert.That(maximum,Is.LessThanOrEqualTo(1f/255),"类型化绑定与独立不可变样式快照必须输出相同像素，模式="+mode);
                    yield return null;
                }
            }finally{UnityEngine.Object.DestroyImmediate(preset);}
        }
        [UnityTest] public IEnumerator ControllerPlaybackUpdatesFixedParametersWithoutAllocation()=>VerifyControllerPrograms(false);
        [UnityTest] public IEnumerator LinkedControllerPlaybackUpdatesFixedParametersWithoutAllocation()=>VerifyControllerPrograms(true);
        static IEnumerator VerifyControllerPrograms(bool linked)
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("--urui-fx-tone-filter:grayscale;", "<img id='subject' src='input.png'><img id='target' src='input.png' style='left:0px;'>", "img");
            var source=fixture.Module.GetController(fixture.Document.Q("subject"));
            var target=linked?fixture.Module.GetController(fixture.Document.Q("target")):null;
            using var link=target?.Link(source);
            var playback=source.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual,Channels=EffectTweenChannels.Tone});
            var frame=0;Action cycle=()=>{playback.SetTime(++frame%2==0?.25:.75);fixture.Document.SynchronizeLayout();};
            for(var i=0;i<300;i++)cycle();
            Action window=()=>{for(var i=0;i<32;i++)cycle();};window();
            fixture.Document.Effects.TryGetFrame(source.Element,out var retained);
            var oldValue=retained.Style.GetNumber("--urui-fx-tone-intensity");
            ValidateChannel();Assert.That(Allocations(window),Is.Zero,"内置控制器播放与联动需要复用私有参数，公开样式仅按需创建且不可变。");
            Assert.That(retained.Style.GetNumber("--urui-fx-tone-intensity"),Is.EqualTo(oldValue));
            playback.SetTime(.4);fixture.Document.SynchronizeLayout();
            Assert.That(source.CreateParameters().ToneIntensity,Is.EqualTo(.4f).Within(.001));
            if(linked)Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.4f).Within(.001));
            yield return null;
        }
        [UnityTest] public IEnumerator WaveProgramsUseNativeBatchesWithoutGeometryUploadsOnParameterChanges()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("--urui-effect:none;--urui-image-effect:ui-wave;",
                "<img id='subject' class='wave-image' src='input.png' style='left:0;width:32px;'>"+
                "<img class='wave-image' src='input.png' style='left:40px;width:32px;'>"+
                "<img class='wave-image' src='input.png' style='left:80px;width:32px;'>"+
                "<img class='wave-image' src='input.png' style='left:120px;width:32px;'>","img");
            var element=fixture.Document.Q("subject");var frame=0;
            Action cycle=()=>{element.Style.Set("--urui-wave-amplitude",++frame%2==0?"18px":"20px");fixture.Document.SynchronizeLayout();};
            for(var i=0;i<300;i++)cycle();
            object renderer=null;foreach(var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))if(component.GetType().FullName=="URUI.Internal.Hosting.RenderHost")renderer=component;
            object Read(string name)=>renderer.GetType().GetProperty(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(renderer);
            Assert.That(Read("RecordedGeometryDraws"),Is.EqualTo(1),"Wave 使用通用原生图像程序合批，参数差异不拆成逐节点回调。");
            var uploaded=Read("GeometryUploadBytes");ValidateChannel();
            Assert.That(Allocations(()=>{for(var i=0;i<32;i++)cycle();}),Is.Zero);
            Assert.That(Read("GeometryUploadBytes"),Is.EqualTo(uploaded));
            yield return null;
        }
        [UnityTest] public IEnumerator UntimedImageProgramsDoNotUploadUnusedClockRows()=>ClockDependency(false);
        [UnityTest] public IEnumerator MaterialStateCapturedAtFirstUseDeclaresItsRealTimeDependency()=>ClockDependency(true);
        static IEnumerator ClockDependency(bool changeBeforeFirstUse)
        {
            using var fixture=new EffectRenderFixture();var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                var definition=TextureColorEffect.Define("dependency",material);
                if(changeBeforeFirstUse)material.SetFloat("_Speed",1);
                fixture.Document.Effects.Register(definition);fixture.Load("--urui-effect:none;--urui-image-effect:dependency;");
                var clock=new CountingClock();using var binding=fixture.Document.Effects.BindClock(fixture.Document.Q("subject"),clock);
                for(var i=0;i<300;i++){clock.Value+=.01;fixture.Document.SynchronizeLayout();}
                object renderer=null;foreach(var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))if(component.GetType().FullName=="URUI.Internal.Hosting.RenderHost")renderer=component;
                var upload=renderer.GetType().GetProperty("ParameterUploadBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                var before=(long)upload.GetValue(renderer);clock.Value+=.01;fixture.Document.SynchronizeLayout();var bytes=(long)upload.GetValue(renderer)-before;
                if(changeBeforeFirstUse)Assert.That(bytes,Is.GreaterThan(0),"时钟依赖来自实际冻结材质，不能使用定义创建时的旧值。");
                else Assert.That(bytes,Is.Zero,"不消费时间的程序不应上传无用的时钟行。");
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
        [UnityTest] public IEnumerator ManyImagesFreezeOneClockAndUploadOnlyItsSharedRow()
        {
            using var fixture=new EffectRenderFixture();var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));material.SetFloat("_Speed",1);
            var bindings=new System.Collections.Generic.List<IDisposable>();
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("clock-program",material));
                var markup=new System.Text.StringBuilder();for(var i=0;i<128;i++)markup.Append("<img id='n").Append(i).Append("' src='input.png' style='left:").Append(i%16*8).Append("px;top:").Append(i/16*8).Append("px;width:8px;height:8px;'>");
                fixture.Load("--urui-effect:none;--urui-image-effect:clock-program;",markup.ToString(),"img");
                var clock=new CountingClock();for(var i=0;i<128;i++)bindings.Add(fixture.Document.Effects.BindClock(fixture.Document.Q("n"+i),clock));
                for(var i=0;i<300;i++){clock.Value+=.01;fixture.Document.SynchronizeLayout();}
                object renderer=null;foreach(var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))if(component.GetType().FullName=="URUI.Internal.Hosting.RenderHost")renderer=component;
                var upload=renderer.GetType().GetProperty("ParameterUploadBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                var before=(long)upload.GetValue(renderer);clock.Reads=0;clock.Value+=.01;fixture.Document.SynchronizeLayout();
                Assert.That(clock.Reads,Is.EqualTo(1),"同一输入窗口的同一个时钟只冻结一次。");
                Assert.That((long)upload.GetValue(renderer)-before,Is.LessThanOrEqualTo(4096),"时间变化只上传共享时钟行所在的一个数据页。");
                Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("n0"),out var first),Is.True);
                Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("n127"),out var last),Is.True);
                Assert.That(first.Time,Is.EqualTo(clock.Value));Assert.That(last.Time,Is.EqualTo(first.Time));
            }
            finally{foreach(var binding in bindings)binding.Dispose();UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
        [UnityTest] public IEnumerator RegisteredResourceKeysDoNotMaterializeFramesOnNumericChanges()=>ResolvedProgram(false);
        [UnityTest] public IEnumerator RawResourceKeysDoNotMaterializeFramesOnNumericChanges()=>ResolvedProgram(true);
        static IEnumerator ResolvedProgram(bool raw)
        {
            using var fixture=new EffectRenderFixture();var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));var resolutions=0;
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.DefineResolved("resolved",_=>{resolutions++;return material;},"--test-preset","--test-phase"));
                fixture.Load("--urui-effect:none;--urui-image-effect:resolved;"+(raw?"--test-preset:a;":"")+
                    "} @property --test-phase{syntax:'<number>';inherits:false;initial-value:0;} "+
                    (raw?"":"@property --test-preset{syntax:'a | b';inherits:false;initial-value:a;}")+" #subject{");
                var element=fixture.Document.Q("subject");var frame=0;
                Action cycle=()=>{element.Style.Set("--test-phase",++frame%2==0?".25":".75");fixture.Document.SynchronizeLayout();};
                for(var i=0;i<300;i++)cycle();ValidateChannel();
                Assert.That(Allocations(()=>{for(var i=0;i<32;i++)cycle();}),Is.Zero,"资源键未变时，不应因其他数值变化构造完整公开样式快照。");
                Assert.That(resolutions,Is.EqualTo(1));element.Style.Set("--test-preset","b");cycle();Assert.That(resolutions,Is.EqualTo(2));
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
        [UnityTest] public IEnumerator TypedImageTintAndUvDoNotAllocateUnobservedGeometrySnapshots()
        {
            using var fixture=new EffectRenderFixture();var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("parameters",material));
                fixture.Load("--urui-effect:none;--urui-image-effect:parameters;");
                var element=fixture.Document.Q("subject");var frame=0;
                Action cycle=()=>{var odd=++frame%2==0;element.Style.Set("--urui-graphic-tint",odd?"rgba(128,64,32,.5)":"rgba(64,128,32,.75)");element.SetAttribute("data-urui-rect",odd?"0 0 1 1":"1 0 1 1");fixture.Document.SynchronizeLayout();};
                for(var i=0;i<300;i++)cycle();ValidateChannel();
                Assert.That(Allocations(()=>{for(var i=0;i<32;i++)cycle();}),Is.Zero,"纯乘色和 UV 参数不能逐帧创建公开几何副本。");
                Assert.That(fixture.Document.Effects.TryGetFrame(element,out var published),Is.True);
                var first=published.SourceGeometry.Colors[0];var uv=published.SourceGeometry.UVs[0];cycle();
                Assert.That(published.SourceGeometry.Colors[0],Is.EqualTo(first));Assert.That(published.SourceGeometry.UVs[0],Is.EqualTo(uv));
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
        [UnityTest] public IEnumerator TypedImageNumericStylesDoNotMaterializePublicFrames()
        {
            using var fixture=new EffectRenderFixture();
            var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("parameters",material,"--test-phase"));
                fixture.Load("--urui-effect:none;--urui-image-effect:parameters;} @property --test-phase{syntax:'<number>';inherits:false;initial-value:0;} #subject{");
                var element=fixture.Document.Q("subject");var frame=0;
                Action cycle=()=>{element.Style.Set("--test-phase",++frame%2==0?".25":".75");fixture.Document.SynchronizeLayout();};
                for(var i=0;i<300;i++)cycle();ValidateChannel();
                Assert.That(Allocations(()=>{for(var i=0;i<32;i++)cycle();}),Is.Zero,"私有参数准备不能逐帧分配公开 EffectFrame 的样式/布局所有者。");
                Assert.That(fixture.Document.Effects.TryGetFrame(element,out var published),Is.True);
                var retained=published.Style.GetNumber("--test-phase");cycle();
                Assert.That(published.Style.GetNumber("--test-phase"),Is.EqualTo(retained),"按需观察的公开快照仍须保持不可变。");
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
        [Test]
        public void ChangingSchedulerInputsDoNotAllocateUpdateObjects()
        {
            var scheduler = new EffectScheduler();
            var source = new object();
            scheduler.TryPrepare(new EffectInput(source, 100, Vector2.one, 0, "allocation", RenderTextureFormat.ARGBHalf), out var warm);
            warm.Submitted();
            ValidateChannel();
            Action refresh = () =>
            {
                for (ulong i = 0; i < 10; ++i)
                {
                    scheduler.TryPrepare(new EffectInput(source, i, Vector2.one, 0, "allocation", RenderTextureFormat.ARGBHalf), out var update);
                    update.Submitted();
                }
            };
            refresh();
            Assert.That(Allocations(refresh), Is.Zero);
        }

        [UnityTest]
        public IEnumerator TimeDrivenSurfaceUpdatesReuseMaterialAndSubmissionCallbacks() => VerifySurface(false);

        [UnityTest]
        public IEnumerator ChangingMaterialPropertiesReuseGpuRetirementRecords() => VerifySurface(true);

        static IEnumerator VerifySurface(bool changeMaterial)
        {
            var cameraObject = new GameObject("Effect allocation camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            var root = new GameObject("Effect allocation document");
            var surface = root.AddComponent<Surface>(); surface.Input.UseUnityInput = false; surface.Viewport = new Vector2Int(128,128);
            var clock = new Clock(); surface.UiClock = clock;
            var document = root.AddComponent<Document>(); document.Surface = surface;
            using var output = new TextureEffectSurface(16,16);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            EffectModule module = null;
            IDisposable binding = null;
            try
            {
                module = new EffectModule(document);
                document.ImageResolver = uri => output.Output.Acquire();
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{width:16px;height:16px;--urui-image-effect:surface-output;}</style></head><body><img id='effect' src='surface.png'></body></html>", "/surface-allocations.html");
                binding = module.BindSurface(document.Q("effect"), new SurfaceEffectInput(output, Texture2D.whiteTexture, material, () => 1, usesTime:true));
                var frameIndex = 0;
                Action refresh = () => { clock.Now += .01; if (changeMaterial) { material.SetFloat("_Brightness", frameIndex % 2 == 0 ? .5f : .75f); } document.SynchronizeLayout(); };
                for (frameIndex = 0; frameIndex < 300; ++frameIndex) { refresh(); camera.Render(); document.SynchronizeLayout(); yield return null; }
                ValidateChannel();
                long allocations = 0;
                var samples = new long[10];
                var noise = new long[10];
                Action empty = () => { };
                var passes = TextureEffectSurface.GetStatistics().ProcessingPasses;
                for (frameIndex = 0; frameIndex < 10; ++frameIndex)
                {
                    noise[frameIndex] = Allocations(empty);
                    samples[frameIndex] = Allocations(refresh);
                    allocations += samples[frameIndex];
                    camera.Render(); document.SynchronizeLayout(); yield return null;
                }
                Assert.That(allocations, Is.Zero, "samples=" + string.Join(",",samples) + ";empty=" + string.Join(",",noise));
                Assert.That(noise, Is.All.Zero, "空采样存在噪声，不能用于零分配验收。");
                Assert.That(TextureEffectSurface.GetStatistics().ProcessingPasses, Is.EqualTo(passes + 10));
            }
            finally
            {
                binding?.Dispose(); module?.Dispose();
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                URUI.Resources.GpuRetirement.Retire(() => URUI.Resources.GpuRetirement.DestroyOwnedObject(material));
            }
        }
    }
}
