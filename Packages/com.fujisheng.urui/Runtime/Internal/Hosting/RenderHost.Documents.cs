using System;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using URUI.Resources;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        internal void RegisterDocumentResources(ulong handle, string sourceUrl, TextResource[] textResources, ImageAsset[] images)
        {
            if (NativeMethods.urui_document_resource_api_version() != 3)
                throw new NotSupportedException("文档资源事务需要 URUI 原生资源协议 v3。");
            foreach (var resource in textResources)
            {
                var bytes = resource.Asset.bytes;
                if (NativeMethods.urui_document_register_resource(handle, resource.Source, bytes, bytes.Length) != 1)
                    throw new InvalidOperationException("URUI 文档资源注册失败：" + resource.Source + "：" + NativeMethods.LastError());
            }
            var bindings = new ImageBinding[images.Length];
            for (var index = 0; index < images.Length; ++index)
                bindings[index] = new ImageBinding { source = ResolveImageUrl(sourceUrl, images[index].Source), texture = images[index].Texture };
            borrowedTextures.Register(handle, bindings);
            for (var index = 0; index < images.Length; ++index)
            {
                foreach (var sprite in images[index].Sprites)
                {
                    var rect = sprite.Region;
                    if (NativeMethods.urui_document_register_sprite(handle, sprite.Name, bindings[index].source,
                        rect.x, rect.y, rect.width, rect.height, sprite.DisplayScale) != 1)
                        throw new InvalidOperationException("URUI 图集区域注册失败：" + sprite.Name + "：" + NativeMethods.LastError());
                }
            }
        }

        static string ResolveImageUrl(string documentUrl, string source)
        {
            var required = NativeMethods.urui_resolve_resource_url(documentUrl, source, null, 0);
            if (required <= 0) throw new InvalidOperationException("URUI 图片 URL 无效：" + source);
            var bytes = new byte[required];
            if (NativeMethods.urui_resolve_resource_url(documentUrl, source, bytes, bytes.Length) != required)
                throw new InvalidOperationException("URUI 图片 URL 解析失败：" + source);
            return System.Text.Encoding.UTF8.GetString(bytes, 0, required - 1);
        }

        internal void PrepareDocumentRetirement() => RetirePaintResources(new System.Collections.Generic.List<IDisposable>());
        internal void ReleaseDocumentResources(ulong handle) => borrowedTextures.Unregister(handle);
    }
}
