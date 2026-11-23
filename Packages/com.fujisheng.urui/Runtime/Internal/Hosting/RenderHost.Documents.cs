using System;
using Unity.Collections.LowLevel.Unsafe;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using URUI.Resources;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        static readonly Unity.Profiling.ProfilerMarker registerResourcesMarker = new("URUI.Document.RegisterResources");

        internal unsafe void RegisterDocumentResources(ulong handle, string sourceUrl, TextResource[] textResources, ImageAsset[] images)
        {
            using var sample = registerResourcesMarker.Auto();
            if (NativeMethods.urui_document_resource_api_version() != NativeMethods.ExpectedDocumentResourceApiVersion)
            {
                throw new NotSupportedException($"文档资源事务需要 URUI 原生资源协议 v{NativeMethods.ExpectedDocumentResourceApiVersion}。");
            }
            foreach (var resource in textResources)
            {
                // GetData 借用资产的原生只读区；原生资源表同步复制或共享快照，不保存此指针。
                var bytes = resource.Asset.GetData<byte>();
                if (NativeMethods.urui_document_register_resource(SurfaceHandle, handle, resource.Source,
                    (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes), bytes.Length) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("URUI 文档资源注册失败：" + resource.Source + "：" + NativeMethods.LastError(SurfaceHandle));
                }
                GC.KeepAlive(resource.Asset);
            }
            var bindings = new ImageBinding[images.Length];
            for (var index = 0; index < images.Length; ++index)
            {
                bindings[index] = new ImageBinding
                {
                    source = NativeMethods.ResolveResourceUrl(sourceUrl, images[index].Source),
                    texture = images[index].Texture
                };
            }
            borrowedTextures.Register(SurfaceHandle, handle, bindings);
            for (var index = 0; index < images.Length; ++index)
            {
                foreach (var sprite in images[index].SpriteSpan)
                {
                    var rect = sprite.Region;
                    if (NativeMethods.urui_document_register_sprite(SurfaceHandle, handle, sprite.Name, bindings[index].source,
                        rect.x, rect.y, rect.width, rect.height, sprite.DisplayScale) != NativeStatus.Ok)
                    {
                        throw new InvalidOperationException("URUI 图集区域注册失败：" + sprite.Name + "：" + NativeMethods.LastError(SurfaceHandle));
                    }
                }
            }
        }

        internal void PrepareDocumentRetirement() => RetirePaintResources(null, null);
        internal void ReleaseDocumentResources(ulong handle) => borrowedTextures.Unregister(handle);
    }
}
