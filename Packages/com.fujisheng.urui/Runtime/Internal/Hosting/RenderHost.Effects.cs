using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Rendering;
using URUI.Rendering.Effects;
namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        struct EffectSubmission
        {
            internal URUI.Documents.Document Owner;
            internal Action Callback;
            internal int ProcessingPasses;
        }
        readonly List<EffectSubmission> effectSubmitted=new();
        internal void QueueEffectSubmission(URUI.Documents.Document owner,Action callback)
            =>effectSubmitted.Add(new EffectSubmission { Owner=owner,Callback=callback });
        bool effectSubmitHook,builtinCameraRendering,builtinDetachedForAdmission;
        int builtinCameraFrame;
        URUI.Hosting.Surface builtinDispatchOwner;
        URUI.Internal.Resources.ImageSubmissionFence builtinSubmission;
        readonly Dictionary<ulong,ulong> imageEffectSlots=new();
        readonly Dictionary<int,ulong> imageEffectInputKeys=new();
        void SubscribeEffectSubmissions()
        {
            if(effectSubmitHook)return;
            Camera.onPreRender+=EffectCameraPreparing;Camera.onPostRender+=EffectCameraSubmitted;effectSubmitHook=true;
        }
        void UnsubscribeEffectSubmissions()
        {
            if(!effectSubmitHook)return;
            Camera.onPreRender-=EffectCameraPreparing;Camera.onPostRender-=EffectCameraSubmitted;effectSubmitHook=false;effectSubmitted.Clear();
        }
        bool HasBuiltinSubmissionResources()
        {
            if(parameterPages?.Current!=null||HasLayerResources||planResources?.HasRecordingGeometry==true)return true;
            foreach(var recording in externalRecordings.Values)if(recording.HasCommands)return true;
            return false;
        }
        void EndBuiltinCameraWindow()
        {
            builtinCameraRendering=false;builtinSubmission=null;
            var owner=builtinDispatchOwner;builtinDispatchOwner=null;
            if(!ReferenceEquals(owner,null)){owner.EndDispatch();owner.FinishNativeCall();}
        }
        void RecoverAbortedBuiltinCamera()
        {
            if(!builtinCameraRendering||builtinCameraFrame==Time.frameCount)return;
            if(builtinSubmission!=null){builtinSubmission.uncertain=true;builtinSubmission.AwaitingCamera=false;imageSubmissionFaulted=true;}
            DetachRecordedBuffers();builtinDetachedForAdmission=true;
            EndBuiltinCameraWindow();
            Debug.LogError("URUI Built-in 相机未完成提交，保留无法确认的 GPU 资源。");
        }
        void EffectCameraPreparing(Camera camera)
        {
            if(camera!=targetCamera||UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline)return;
            RecoverAbortedBuiltinCamera();
            if(!active||!isActiveAndEnabled)return;
            PrepareGeometryRecording();
            if(!active||!isActiveAndEnabled)return;
            if(recordedBuffers.Count==0)return;
            URUI.Internal.Resources.ImagePinRetirement.Collect();CollectImageSubmissions();
            URUI.Internal.Resources.ImageSubmissionFence submission=null;var fresh=false;
            try
            {
                if(imageSubmissionFaulted)throw new InvalidOperationException("URUI GPU 提交状态不确定，保留相关资源。");
                if(HasBuiltinSubmissionResources())
                {
                    foreach(var pending in imageSubmissions)
                        if(pending.CameraSubmission&&pending.CameraFrame==Time.frameCount&&!pending.uncertain&&!pending.AwaitingCamera&&
                            pending.Recording==recordingSerial&&pending.CanPinParameters(parameterPages?.Current)){submission=pending;break;}
                    fresh=submission==null;
                    if(fresh)submission=ReserveImageSubmission();
                    submission.CameraSubmission=true;submission.CameraFrame=Time.frameCount;submission.Recording=recordingSerial;
                    submission.AwaitingCamera=true;
                    submission.pins.Append(borrowedTextures);submission.PinParameters(parameterPages?.Current);
                    PinLayerWrites(submission);planResources?.PinSubmission(submission.Geometry);externalGeometryInputs?.Programs.PinSubmission(submission.Programs);
                    submission.barrier??=new UnityEngine.Rendering.CommandBuffer{name="URUI camera submission fence"};
                    submission.barrier.Clear();
                    submission.fence=submission.barrier.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.CPUSynchronisation,UnityEngine.Rendering.SynchronisationStageFlags.PixelProcessing);
                }
                // 准入和保活先于 AfterEverything 绘制。相机窗口复用现有分发事务，
                // 回调内重载/卸载仍排队到真实提交之后，不改写正在消费的缓冲。
                builtinSubmission=submission;builtinCameraFrame=Time.frameCount;builtinCameraRendering=true;
                builtinDispatchOwner=OwnerSurface;builtinDispatchOwner.BeginDispatch();
                if(builtinDetachedForAdmission)
                {
                    if(attachedBuffers.Count==0)foreach(var commands in recordedBuffers){targetCamera.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.AfterEverything,commands);attachedBuffers.Add(commands);}
                    builtinDetachedForAdmission=false;
                }
            }
            catch
            {
                DetachRecordedBuffers();builtinDetachedForAdmission=true;
                if(submission!=null)
                {
                    if(fresh){imageSubmissions.Remove(submission);submission.AwaitingCamera=false;submission.Release();}
                    else{submission.uncertain=true;submission.AwaitingCamera=false;imageSubmissionFaulted=true;}
                }
                if(builtinCameraRendering)EndBuiltinCameraWindow();
                throw;
            }
        }
        void EffectCameraSubmitted(Camera camera)
        {
            if(camera!=targetCamera||UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline||!builtinCameraRendering)return;
            try
            {
                if(builtinSubmission!=null)
                {
                    Graphics.ExecuteCommandBuffer(builtinSubmission.barrier);builtinSubmission.AwaitingCamera=false;
                }
                NotifyEffectSubmission();
            }
            catch
            {
                if(builtinSubmission!=null){builtinSubmission.uncertain=true;builtinSubmission.AwaitingCamera=false;imageSubmissionFaulted=true;}
                throw;
            }
            finally{EndBuiltinCameraWindow();}
        }
        void NotifyEffectSubmission()
        {
            var count=effectSubmitted.Count;
            for(var i=0;i<count;i++)
            {
                var submission=effectSubmitted[i];
                try
                {
                    if(submission.Callback!=null)submission.Owner.EffectSubmitted(submission.Callback);
                    EffectStatistics.FilterProcessingPasses+=submission.ProcessingPasses;
                }
                catch(Exception error){Debug.LogException(error);}
            }
        }
        string PaintName(ulong shader)
        {
            if(paintShaderNames.TryGetValue(shader,out var cached))return cached;
            var count=NativeMethods.urui_paint_shader_name(shader,paintName,paintName.Length);
            if(count<=0) throw new InvalidOperationException("效果程序名已失效。");
            if(count>paintName.Length) { paintName=new byte[count]; if(NativeMethods.urui_paint_shader_name(shader,paintName,count)!=count) throw new InvalidOperationException("效果程序名变化。"); }
            var name=Encoding.UTF8.GetString(paintName,0,count-1);paintShaderNames.Add(shader,name);return name;
        }
    }
}
