using System;
using System.Collections.Generic;
using UnityEngine;
namespace URUI.Internal.Rendering
{
    // Leases are returned only by the host's last-reference/GPU-fence retirement path.
    internal sealed class EffectTargetPool : IDisposable
    {
        readonly Dictionary<(int,int,RenderTextureFormat,bool),Stack<RenderTexture>> free=new();
        static int liveTargets,leasedTargets;static long liveColorBytes,leasedColorBytes,idleColorBytes,createdTargets,reuses;
        internal static URUI.Rendering.Effects.EffectTargetStatistics Statistics=>new(liveTargets,leasedTargets,liveColorBytes,leasedColorBytes,idleColorBytes,createdTargets,reuses);
        bool disposed;
        long freeBytes;
        static long Bytes(RenderTexture texture)=>(long)texture.width*texture.height*(texture.format==RenderTextureFormat.ARGBHalf?8:4);
        internal sealed class Lease : IDisposable
        {
            EffectTargetPool owner; readonly bool linear; internal RenderTexture Texture {get;private set;}
            internal Lease(EffectTargetPool owner,RenderTexture texture,bool linear){this.owner=owner;this.linear=linear;Texture=texture;leasedTargets++;leasedColorBytes+=Bytes(texture);}
            public void Dispose(){if(owner==null)return;var previous=owner;owner=null;previous.Return(Texture,linear);Texture=null;}
        }
        internal Lease Rent(int width,int height,RenderTextureFormat format,bool linear=false)
        {
            if(disposed)throw new ObjectDisposedException(nameof(EffectTargetPool));
            if(width<1||height<1||width>8192||height>8192)throw new ArgumentOutOfRangeException(nameof(width));
            if(!SystemInfo.SupportsRenderTextureFormat(format))throw new NotSupportedException("图层目标格式不受支持："+format);
            var key=(width,height,format,linear);
            if(free.TryGetValue(key,out var entries)&&entries.Count>0){var reused=entries.Pop();freeBytes-=Bytes(reused);idleColorBytes-=Bytes(reused);reuses++;return new Lease(this,reused,linear);}
            var texture=new RenderTexture(width,height,0,format,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.Default){name="URUI pooled effect target",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            if(!texture.Create()){DestroyObject(texture);throw new InvalidOperationException("无法创建图层效果目标。");}
            liveTargets++;liveColorBytes+=Bytes(texture);createdTargets++;return new Lease(this,texture,linear);
        }
        void Return(RenderTexture texture,bool linear)
        {
            leasedTargets--;leasedColorBytes-=Bytes(texture);
            if(disposed){Destroy(texture);return;}
            var key=(texture.width,texture.height,texture.format,linear);
            if(!free.TryGetValue(key,out var entries))free.Add(key,entries=new Stack<RenderTexture>());
            // Bound free storage; in-flight leases are never evicted or reused early.
            if(entries.Count>=4||freeBytes+Bytes(texture)>64L*1024*1024){Destroy(texture);return;}entries.Push(texture);freeBytes+=Bytes(texture);idleColorBytes+=Bytes(texture);
        }
        static void Destroy(RenderTexture texture){if(texture){liveTargets--;liveColorBytes-=Bytes(texture);texture.Release();DestroyObject(texture);}}
        static void DestroyObject(RenderTexture texture)
        {
            // 退役可能跨越退出 PlayMode；fence 已完成后按实际执行模式销毁。
            if(Application.isPlaying)UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
        public void Dispose(){if(disposed)return;disposed=true;idleColorBytes-=freeBytes;foreach(var entries in free.Values)foreach(var texture in entries)Destroy(texture);free.Clear();freeBytes=0;}
    }
}
