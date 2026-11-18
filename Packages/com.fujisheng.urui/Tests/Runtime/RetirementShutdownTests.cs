using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Internal.Resources;

namespace URUI.Tests
{
    public sealed class RetirementShutdownTests
    {
        sealed class Resource : IDisposable { internal int Disposals; public void Dispose() { Disposals++; } }
        static readonly BindingFlags StaticFields=BindingFlags.Static|BindingFlags.NonPublic;
        static readonly FieldInfo Pointer=typeof(CommandBuffer).GetField("m_Ptr",BindingFlags.Instance|BindingFlags.NonPublic);
        static IntPtr NativePointer(CommandBuffer buffer)=>(IntPtr)Pointer.GetValue(buffer);

        [Test]public void QuitReleasesFenceCommandsWithoutPretendingUnknownGpuWorkCompleted()
        {
            var type=typeof(ImagePinRetirement);
            var pending=(IList)type.GetField("pending",StaticFields).GetValue(null);
            Assert.That(pending.Count,Is.Zero,"此隔离用例必须从没有其他待退役批次的状态开始。");
            var firstBuffer=new CommandBuffer{name="URUI shutdown proof first fence"};
            var lateBuffer=new CommandBuffer{name="URUI shutdown proof late fence"};
            var first=new Resource();var late=new Resource();
            ImagePinRetirement owner=null;
            try
            {
                LogAssert.Expect(LogType.Warning,"URUI GPU 退役状态无法确认，保留资源：SRP Submit outcome uncertain");
                ImagePinRetirement.Retire(null,new List<ImageSubmissionFence>{new ImageSubmissionFence{barrier=firstBuffer,uncertain=true}},resource:first);
                owner=(ImagePinRetirement)type.GetField("owner",StaticFields).GetValue(null);
                Assert.That(NativePointer(firstBuffer),Is.Not.EqualTo(IntPtr.Zero));
                // Exercise the Player shutdown routine even when running this test in the Editor.
                type.GetMethod("BeginShutdown",StaticFields).Invoke(null,null);
                Assert.That(NativePointer(firstBuffer),Is.EqualTo(IntPtr.Zero),"退出时须在图形设备关闭前释放已录制的CPU命令对象。");
                Assert.That(first.Disposals,Is.Zero,"未知GPU工作仍须保留真实资源租约。");
                Assert.That(pending.Count,Is.EqualTo(1));
                ImagePinRetirement.Retire(null,new List<ImageSubmissionFence>{new ImageSubmissionFence{barrier=lateBuffer,uncertain=true}},resource:late);
                Assert.That(NativePointer(lateBuffer),Is.EqualTo(IntPtr.Zero),"退出后的组件销毁也不能遗留待终结的CommandBuffer。");
                Assert.That(first.Disposals+late.Disposals,Is.Zero);
                Assert.That(pending.Count,Is.EqualTo(2));
            }
            finally
            {
                // 只有本用例创建的假资源，从未交给GPU；不清理其他测试的真实租约。
                pending.Clear();
                if(NativePointer(firstBuffer)!=IntPtr.Zero)firstBuffer.Release();
                if(NativePointer(lateBuffer)!=IntPtr.Zero)lateBuffer.Release();
                if(owner)UnityEngine.Object.DestroyImmediate(owner.gameObject);
                var quitting=type.GetField("quitting",StaticFields);quitting?.SetValue(null,false);
                first.Dispose();late.Dispose();
            }
        }
    }
}
