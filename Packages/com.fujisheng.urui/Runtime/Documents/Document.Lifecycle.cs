using System;
using System.Threading;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        void Awake() { ownerThread = Thread.CurrentThread.ManagedThreadId; }
        void OnEnable()
        {
            if (!loadRequested) return;
            if (compiledSource) Load(compiledSource);
            else if (requestedMarkup != null) LoadContent(requestedMarkup, source, sourceUrl);
            else if (source) Load(source, sourceUrl);
        }
        void Start()
        {
            if (!loadRequested || IsLoaded) return;
            if (compiledSource) Load(compiledSource);
            else if (source) Load(source, sourceUrl);
        }
        void OnDisable() { ReleaseContent(); }
        void OnDestroy()
        {
            ReleaseContent();
            if (!ReferenceEquals(surface, null)) surface.Forget(this);
            Loaded = null; Unloaded = null; layoutUpdated = null; layoutCallbacks = null;
        }
        internal void SuspendForSurface() { ReleaseContent(); }
        internal void RestoreForSurface() { if (this && isActiveAndEnabled && loadRequested) OnEnable(); }

        void NotifyLifecycle(Action<Document> callbacks)
        {
            if (callbacks == null) return;
            BeginControlUpdate();
            try {
                foreach (Action<Document> callback in callbacks.GetInvocationList()) {
                    try { callback(this); } catch (Exception error) { Debug.LogException(error); }
                }
            } finally { EndControlUpdate(); }
        }

        void RetireContent(ContentState state, bool notify)
        {
            var selected = content;
            content = state;
            try
            {
                host.PrepareDocumentRetirement();
                if (NativeMethods.urui_document_close(state.Handle) != 1)
                    throw new InvalidOperationException("URUI 文档关闭失败：" + NativeMethods.LastError());
                state.Retired = true;
                host.ReleaseDocumentResources(state.Handle);
                foreach (var element in state.Elements.Values) element.Invalidate();
                state.Elements.Clear();
                state.ElementSnapshot = null;
                state.EffectLayouts.Clear();
                ReleaseEffects(); ReleasePainters();
                surface.Unbind(state.Handle);
                if (notify) NotifyLifecycle(Unloaded);
            }
            finally { content = selected; }
        }

        void ReleaseContent()
        {
            if (DeferLifecycle(ReleaseContent)) return;
            if (activeContent == null) return;
            changingContent = true;
            surface.BeginTransaction();
            try {
                content = activeContent;
                RetireContent(activeContent, true);
                content = activeContent = null;
            } finally {
                changingContent = false;
                surface.EndTransaction();
                FinishNativeCall();
            }
        }
    }
}
