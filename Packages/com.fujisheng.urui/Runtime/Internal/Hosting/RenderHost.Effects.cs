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
            internal LayerSnapshot Layers;
            internal long Generation;
        }
        readonly List<EffectSubmission> effectSubmitted=new();
        internal void QueueEffectSubmission(URUI.Documents.Document owner,Action callback)
            =>effectSubmitted.Add(new EffectSubmission { Owner=owner,Callback=callback });
        bool effectSubmitHook;
        readonly Dictionary<URUI.Documents.Document,List<EffectInput>> imageEffectInputs=new(),elementEffectInputs=new();
        readonly Dictionary<ulong,ulong> imageEffectSlots=new();
        readonly Dictionary<int,ulong> imageEffectInputKeys=new();
        readonly List<URUI.Documents.Document> removedEffectOwners=new();
        void ResetEffectInputs(Dictionary<URUI.Documents.Document,List<EffectInput>> grouped)
        {
            removedEffectOwners.Clear();
            foreach(var pair in grouped)if(!pair.Key||!pair.Key.IsLoaded)removedEffectOwners.Add(pair.Key);else pair.Value.Clear();
            foreach(var owner in removedEffectOwners)grouped.Remove(owner);
        }
        void SubscribeEffectSubmissions(){if(effectSubmitHook)return;Camera.onPostRender+=EffectCameraSubmitted;effectSubmitHook=true;}
        void UnsubscribeEffectSubmissions(){if(!effectSubmitHook)return;Camera.onPostRender-=EffectCameraSubmitted;effectSubmitHook=false;effectSubmitted.Clear();}
        void EffectCameraSubmitted(Camera camera){if(active&&isActiveAndEnabled&&camera==targetCamera&&!UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline)NotifyEffectSubmission();}
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
                    if(submission.Layers!=null&&submission.Generation==layerCacheGeneration)submittedLayers=submission.Layers;
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
        void PrepareImageEffects()
        {
            foreach (var owner in OwnerSurface.DocumentsSnapshot()) { if (owner && owner.IsLoaded) { owner.BeginEffectPreparation(); } }
            usedEffectGeometries.Clear();
            var grouped=imageEffectInputs;ResetEffectInputs(grouped);
            imageEffectSlots.Clear();imageEffectInputKeys.Clear();
            foreach (var paint in paintCommands.Values) if (paint.reserved == 1) {
                var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("图片效果所属文档已失效。");
                if (!grouped.TryGetValue(owner, out var uses)) grouped.Add(owner, uses = new());
                var command = commands[paint.command_index];
                // 几何在尺寸/乘色动画中会替换；实例按节点的有序绘制槽复用。
                imageEffectSlots.TryGetValue(paint.element,out var slot);
                imageEffectSlots[paint.element]=++slot;
                imageEffectInputKeys.Add(paint.command_index,slot);
                uses.Add(new EffectInput(paint.element, PaintName(paint.shader), LoadTexture(command.texture), URUI.Internal.Resources.BorrowedTextureRegistry.IsBorrowed(command.texture), FreezeEffectGeometry(command.geometry),input:slot));
            }
            PruneEffectGeometries();
            foreach (var owner in OwnerSurface.DocumentsSnapshot()) if (owner && owner.IsLoaded)
                owner.PrepareEffects(grouped.TryGetValue(owner, out var uses) ? uses : Array.Empty<EffectInput>());
            foreach (var paint in paintCommands.Values) if (paint.reserved == 1) {
                var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("图片效果所属文档已失效。");
                var bounds = owner.EffectInkBounds(paint.element, owner.EffectKindForName(PaintName(paint.shader)),imageEffectInputKeys[paint.command_index]);
                if (NativeMethods.urui_effect_ink_bounds(paint.command_index, bounds.x, bounds.y, bounds.width, bounds.height) != 1)
                    throw new InvalidOperationException("图片效果范围无法进入当前绘制记录。");
            }
        }
        bool DrawImageEffect(PaintCommandNative paint, CommandNative command, Matrix4x4 transform, Mesh geometry, ref PhaseFrame phase)
        {
            // Prepare may retire previous resources and their old image pins. Acquire the input again for this recording.
            LoadTexture(command.texture);
            var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("图片效果所属文档已失效。");
            var context=paintContexts.Open(owner.WrapElement(paint.element),buffer,
                transform*Matrix4x4.Translate(new Vector3(command.tx,command.ty,0)),
                new Vector4(command.clip_x,command.clip_y,command.clip_x+command.clip_w,command.clip_y+command.clip_h),
                command.ClipEnabled,maskSourceLayers.Contains(activeLayer));
            painterPhase = phase;
            painterWrote = false;
            try{return owner.RecordEffect(paint.element,context,geometry,owner.EffectSubmissionSink,owner.EffectKindForName(PaintName(paint.shader)),imageEffectInputKeys[paint.command_index]);}
            finally{context.Close();phase=painterPhase;if(painterWrote)SelectLayer(activeLayer);}
        }
    }
}
