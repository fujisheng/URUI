using System;
using System.Threading;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>
    /// 同步解析取得的图片租约。返回给 URUI 后，将 Dispose 权交给 URUI。
    /// </summary>
    /// <remarks>
    /// 无需 Read/Write。纹理必须保持完整 mip0 常驻且尺寸/格式不变。RenderTexture 必须是已创建的持久单采样 2D 颜色目标，输出直通 alpha。释放回调在主线程、原生缓存与 GPU 引用均结束后执行；为空则仅借用图片，URUI 不销毁它。
    /// </remarks>
    public sealed class ImageLease : IDisposable
    {
        Action release;
        internal bool HasRelease => release != null;
        public Texture Texture
        {
            get;
        }

        public ImageLease(Texture texture, Action release = null)
        {
            if (!texture)
            {
                throw new ArgumentException("图片租约必须包含有效纹理。", nameof(texture));
            }
            Texture = texture;
            this.release = release;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref release, null)?.Invoke();
        }
    }
}
