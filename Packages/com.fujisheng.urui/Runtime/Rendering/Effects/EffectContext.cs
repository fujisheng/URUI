using System;
using System.Collections.Generic;
namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 实例资源所有者；隐藏、移除、卸载和构造失败均走宿主 GPU 退役。
    /// </summary>
    public sealed class EffectContext : IDisposable
    {
        readonly Action<IDisposable> retire;
        readonly List<IDisposable> owned = new();
        bool disposed;
        internal bool Recording;
        public EffectDefinition Definition
        {
            get;
        }
        public ulong ElementToken
        {
            get;
        }
        /// <summary>
        /// 仅用于已显式注册的实例输入身份；Prepare/Record 中不能查询或修改此节点。
        /// </summary>
        public URUI.Elements.Element Element
        {
            get;
        }
        internal EffectContext(EffectDefinition definition, URUI.Elements.Element element, ulong token, Action<IDisposable> retire)
        {
            Definition = definition;
            Element = element;
            ElementToken = token;
            this.retire = retire;
        }
        public T Own<T>(T resource) where T : IDisposable
        {
            RequirePreparation();
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            owned.Add(resource);
            return resource;
        }
        /// <summary>
        /// 资源辅助器在解析或创建新资源前检查调用阶段，防止 Record 内发生资源副作用。
        /// </summary>
        public void RequirePreparation()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectContext));
            }
            if (Recording)
            {
                throw new InvalidOperationException("Record 中不能取得或替换资源。");
            }
        }
        /// <summary>
        /// Prepare 替换资源时使用；在途 GPU 帧完成前不销毁旧资源。
        /// </summary>
        public void Retire(IDisposable resource)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectContext));
            }
            if (Recording)
            {
                throw new InvalidOperationException("Record 中不能替换资源。");
            }
            if (resource == null || !owned.Contains(resource))
            {
                throw new ArgumentException("资源不属于当前实例。", nameof(resource));
            }
            retire(resource);
            owned.Remove(resource);
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                try
                {
                    owned[i].Dispose();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
            owned.Clear();
        }
    }
}
