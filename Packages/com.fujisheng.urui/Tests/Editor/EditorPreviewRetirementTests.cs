using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Editor.Inspection;
using URUI.Resources;
using URUI.Internal.Resources;

namespace URUI.Tests.Editor
{
    public sealed class EditorPreviewRetirementTests
    {
        const string Html="<!DOCTYPE html><html><head><style>body{margin:0;width:321px;height:211px;background-color:#507080;}div{width:173px;height:97px;background-color:#e83428;opacity:.5;}</style></head><body><div></div></body></html>";
        [TestCase(1)]
        [TestCase(8)]
        public void DisposeRetiresSubmittedTargetsBeforeTheNextEditorUpdate(int frames)
        {
            RequireGpu();
            var existing=PoolIds();
            var preview=RuntimeInspectionBridge.CreatePreview(321,211);
            RenderTexture[] owned=Array.Empty<RenderTexture>();
            try
            {
                preview.Document.LoadHtml(Html,"/preview-retirement.html");
                for(var i=0;i<frames;i++){preview.Advance(.016f);preview.Render();}
                owned=UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t=>t.name.StartsWith("URUI pooled effect target",StringComparison.Ordinal)&&!existing.Contains(t.GetInstanceID())).ToArray();
                Assert.That(owned.Length,Is.GreaterThanOrEqualTo(2),"正对照必须创建真实图层目标。");
                preview.Dispose();
                Assert.That(owned.Where(t=>t&&t.IsCreated()).Select(t=>t.name),Is.Empty,"域重载前不得把仍存活的纹理留给即将消失的托管退休队列。");
            }
            finally
            {
                preview.Dispose();
                // RED 失败后也等待探针自身提交，再通过原退休协议清理。
                var target=owned.FirstOrDefault(t=>t&&t.IsCreated());
                if(target){WaitForGpu(target);GpuRetirement.Collect();}
            }
        }
        [Test]
        public void DisposeWithoutRenderingPreservesOtherOwnedTargets()
        {
            RequireGpu();
            var other=new RenderTexture(13,17,0,RenderTextureFormat.ARGB32);
            Assert.That(other.Create(),Is.True);
            try
            {
                var preview=RuntimeInspectionBridge.CreatePreview(321,211);
                preview.Dispose();preview.Dispose();
                Assert.That(other.IsCreated(),Is.True);
                Assert.That(preview.Target,Is.Null);
            }
            finally{other.Release();UnityEngine.Object.DestroyImmediate(other);}
        }
        [Test]
        public void RepeatedDisposePreservesTheNextPreview()
        {
            RequireGpu();
            var previous=RuntimeInspectionBridge.CreatePreview(321,211);
            previous.Document.LoadHtml(Html,"/previous-preview.html");previous.Render();previous.Dispose();
            using(var next=RuntimeInspectionBridge.CreatePreview(321,211))
            {
                next.Document.LoadHtml(Html,"/next-preview.html");next.Render();
                previous.Dispose();
                Assert.That(next.Target&&next.Target.IsCreated(),Is.True);
                Assert.DoesNotThrow(next.Render);
            }
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator FailedCompletionPreservesTheRetirementBatch()
        {
            RequireGpu();var releases=0;
            GpuRetirement.Retire(()=>releases++);
            Assert.Throws<InvalidOperationException>(()=>ImagePinRetirement.TryCompleteImmediateRetirements(()=>throw new InvalidOperationException("完成证明失败")));
            Assert.That(releases,Is.Zero);
            FlushTestGpu();
            for(var i=0;i<60&&releases==0;i++){yield return null;GpuRetirement.Collect();}
            Assert.That(releases,Is.EqualTo(1));
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator CompletionDoesNotCoverNewRetirementsCreatedInsideTheProof()
        {
            RequireGpu();var first=0;var later=0;
            GpuRetirement.Retire(()=>first++);
            Assert.That(ImagePinRetirement.TryCompleteImmediateRetirements(()=>GpuRetirement.Retire(()=>later++)),Is.True);
            Assert.That(first,Is.EqualTo(1));Assert.That(later,Is.Zero);
            FlushTestGpu();
            for(var i=0;i<60&&later==0;i++){yield return null;GpuRetirement.Collect();}
            Assert.That(later,Is.EqualTo(1));
        }
        sealed class Counter:IDisposable
        {
            internal int Releases;
            public void Dispose(){Releases++;}
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator CompletionPreservesUncertainAndUnsubmittedWork()
        {
            RequireGpu();
            for(var variant=0;variant<3;variant++)
            {
                var barrier=new CommandBuffer();
                var submission=new ImageSubmissionFence{barrier=barrier,CameraSubmission=true,
                    fence=barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,SynchronisationStageFlags.PixelProcessing)};
                var resource=new Counter();
                ImagePinRetirement.Retire(null,new List<ImageSubmissionFence>{submission},resource:resource);
                submission.AwaitingCamera=variant==0;submission.uncertain=variant==1;submission.CameraSubmission=variant!=2;
                var confirmed=false;
                try
                {
                    Assert.That(ImagePinRetirement.TryCompleteImmediateRetirements(()=>confirmed=true),Is.False);
                    Assert.That(confirmed,Is.False);Assert.That(resource.Releases,Is.Zero);
                }
                finally
                {
                    submission.AwaitingCamera=false;submission.uncertain=false;submission.CameraSubmission=false;submission.Immediate=true;
                    Graphics.ExecuteCommandBuffer(barrier);
                }
                // 改为真实即时提交并提供完成证明；无相机帧时不假定 CPU fence 可见状态已刷新。
                Assert.That(ImagePinRetirement.TryCompleteImmediateRetirements(FlushTestGpu),Is.True);
                Assert.That(resource.Releases,Is.EqualTo(1),"variant="+variant);
                GpuRetirement.Collect();
                Assert.That(resource.Releases,Is.EqualTo(1));
                yield return null;
            }
        }
        static HashSet<int> PoolIds()=>UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t=>t.name.StartsWith("URUI pooled effect target",StringComparison.Ordinal)).Select(t=>t.GetInstanceID()).ToHashSet();
        static void RequireGpu()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null){Assert.Ignore("此回归需要真实 GPU 及退休 fence。");}
        }
        static void WaitForGpu(RenderTexture texture)
        {
            var active=RenderTexture.active;var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply();}
            finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(pixel);}
        }
        static void FlushTestGpu()
        {
            // Batchmode EditMode 没有持续相机帧；完成真实微小读回后再轮询 fence。
            var texture=new RenderTexture(1,1,0,RenderTextureFormat.ARGB32);
            try{Assert.That(texture.Create(),Is.True);WaitForGpu(texture);}
            finally{texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
