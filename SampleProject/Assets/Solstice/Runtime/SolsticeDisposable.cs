using System;
using System.Collections.Generic;

namespace Solstice
{
    /// <summary>
    /// Lightweight disposable container for event and UI subscriptions within a page controller.
    /// </summary>
    public sealed class CompositeDisposable : IDisposable
    {
        readonly List<IDisposable> items = new();
        bool disposed;

        public void Add(IDisposable item)
        {
            if (item == null) return;
            if (disposed)
            {
                item.Dispose();
                return;
            }
            items.Add(item);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                items[i]?.Dispose();
            }
            items.Clear();
        }
    }

    internal sealed class DisposableAction : IDisposable
    {
        Action action;
        public DisposableAction(Action action) => this.action = action;

        public void Dispose()
        {
            var a = action;
            action = null;
            a?.Invoke();
        }
    }
}
