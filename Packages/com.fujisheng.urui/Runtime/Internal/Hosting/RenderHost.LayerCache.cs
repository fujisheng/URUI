using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Rendering.Effects;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        // 原生 geometry 句柄不可变；命令/矩阵、滤镜值、RT租约和Unity纹理变化共同约束缓存。
        // 任意自定义paint、RenderTexture和未声明纯函数的滤镜都继续实时处理。
        sealed class LayerSnapshot
        {
            internal CommandNative[] draws;
            internal float[] transforms;
            internal LayerCommandNative[] layers;
            internal FilterInvocation[] filters;
            internal TextureDependency[] textures;
            internal ulong[] directShaders;
            internal int width,height;
            internal RenderTextureFormat format;
        }
        LayerSnapshot submittedLayers;
        bool reuseLayerContents;
        long layerCacheGeneration;
        readonly List<TextureDependency> layerTextureDependencies=new();
        readonly HashSet<int> layerSeenTextures=new();
        void InvalidateLayerCache(){submittedLayers=null;reuseLayerContents=false;layerCacheGeneration++;}
        static bool SameFilter(FilterInvocation a,FilterInvocation b)=>a.Name==b.Name&&a.Value==b.Value&&a.Sigma==b.Sigma&&a.Offset==b.Offset&&a.Color==b.Color&&a.Region==b.Region;
        static bool SameDraw(CommandNative a,CommandNative b)=>a.geometry==b.geometry&&a.texture==b.texture&&a.tx==b.tx&&a.ty==b.ty&&a.flags==b.flags&&a.clip_x==b.clip_x&&a.clip_y==b.clip_y&&a.clip_w==b.clip_w&&a.clip_h==b.clip_h;
        static bool SameLayer(LayerCommandNative a,LayerCommandNative b)=>a.command_index==b.command_index&&a.kind==b.kind&&a.source==b.source&&a.target==b.target&&a.mask==b.mask&&a.blend==b.blend&&a.flags==b.flags&&a.clip_x==b.clip_x&&a.clip_y==b.clip_y&&a.clip_w==b.clip_w&&a.clip_h==b.clip_h;
        static TextureDependency Depend(Texture t)=>new TextureDependency{texture=t,update=t.updateCount,width=t.width,height=t.height,filter=t.filterMode,u=t.wrapModeU,v=t.wrapModeV,w=t.wrapModeW,bias=t.mipMapBias,aniso=t.anisoLevel};
        static bool Alive(TextureDependency d)
        {
            var t=d.texture;return t&&t.updateCount==d.update&&t.width==d.width&&t.height==d.height&&t.filterMode==d.filter&&t.wrapModeU==d.u&&t.wrapModeV==d.v&&t.wrapModeW==d.w&&t.mipMapBias==d.bias&&t.anisoLevel==d.aniso;
        }
        void PrepareLayerCache(int count)
        {
            reuseLayerContents=false;
            if(layerCount==0||filterInvocations.Count==0||elementFilterElements.Count!=0){InvalidateLayerCache();return;}
            foreach (var paint in paintCommands.Values) { if (paint.reserved != 2) { InvalidateLayerCache(); return; } }
            for (var index = 0; index < layerCount; ++index)
                if (layerCommands[index].kind == 4 && layerCommands[index].source == 0) { InvalidateLayerCache(); return; }
            foreach(var filter in filterInvocations)if(!OwnerSurface.FilterOwner(layerCommands[filter.Key].mask).IsFilterCacheable(filter.Value.Name)){InvalidateLayerCache();return;}
            var textures=layerTextureDependencies;var seen=layerSeenTextures;textures.Clear();seen.Clear();
            for(int i=0;i<count;i++)
            {
                var texture=LoadTexture(commands[i].texture);
                if(!(texture is Texture2D)){InvalidateLayerCache();return;}
                if(seen.Add(texture.GetInstanceID()))textures.Add(Depend(texture));
            }
            var previous=submittedLayers;
            if(previous!=null&&previous.width==UiWidth&&previous.height==UiHeight&&previous.format==(targetCamera.allowHDR?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32)&&previous.draws.Length==count&&previous.layers.Length==layerCount&&previous.filters.Length==filterInvocations.Count)
            {
                bool same=true;
                for(int i=0;same&&i<count;i++)same=SameDraw(previous.draws[i],commands[i]);
                for (int i=0;same&&i<count;i++) same=previous.directShaders[i]==(paintCommands.TryGetValue(i,out var paint)?paint.shader:0);
                for(int i=0;same&&i<count*16;i++)same=previous.transforms[i]==commandTransforms[i];
                for(int i=0;same&&i<layerCount;i++)same=SameLayer(previous.layers[i],layerCommands[i]);
                int index=0;foreach(var filter in filterInvocations.Values){if(!same)break;same=SameFilter(previous.filters[index++],filter);}
                foreach(var dependency in previous.textures){if(!same)break;same=Alive(dependency);}
                if(same){reuseLayerContents=true;return;}
            }
            var snapshot=new LayerSnapshot{width=UiWidth,height=UiHeight,format=targetCamera.allowHDR?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32,draws=new CommandNative[count],transforms=new float[count*16],layers=new LayerCommandNative[layerCount],filters=new List<FilterInvocation>(filterInvocations.Values).ToArray(),textures=textures.ToArray(),directShaders=new ulong[count]};
            Array.Copy(commands,snapshot.draws,count);Array.Copy(commandTransforms,snapshot.transforms,count*16);Array.Copy(layerCommands,snapshot.layers,layerCount);
            // 原生颜色链句柄不可变；参数改变会取得新句柄，不能复用旧子树像素。
            foreach (var paint in paintCommands.Values) { snapshot.directShaders[paint.command_index]=paint.shader; }
            var generation=++layerCacheGeneration;submittedLayers=null;
            effectSubmitted.Add(new EffectSubmission { Layers=snapshot,Generation=generation });
        }
    }
}
