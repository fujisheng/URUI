using System;
using System.Threading;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        bool destroying;

        void Awake()
        {
            ownerThread = Thread.CurrentThread.ManagedThreadId;
        }

        void OnEnable()
        {
            if (!loadRequested)
            {
                return;
            }
            if (compiledSource)
            {
                Load(compiledSource);
            }
            else if (requestedMarkup != null)
            {
                LoadContent(requestedMarkup, source, sourceUrl);
            }
            else if (source)
            {
                Load(source, sourceUrl);
            }
        }

        void Start()
        {
            if (!loadRequested || IsLoaded)
            {
                return;
            }
            if (compiledSource)
            {
                Load(compiledSource);
            }
            else if (source)
            {
                Load(source, sourceUrl);
            }
        }

        void OnDisable()
        {
            ReleaseContent(clearDelegatesAfterRelease: false);
        }

        void OnDestroy()
        {
            destroying = true;
            // Preserve Unloaded subscribers when ReleaseContent is deferred until the
            // native dispatch unwinds; clear delegates only after retirement completes.
            if (!ReleaseContent(clearDelegatesAfterRelease: true))
            {
                ClearLifecycleDelegates();
            }
            if (!ReferenceEquals(surface, null))
            {
                surface.Forget(this);
            }
        }

        void ClearLifecycleDelegates()
        {
            Loaded = null;
            Unloaded = null;
            layoutUpdated = null;
            layoutCallbacks = null;
        }

        internal void SuspendForSurface()
        {
            ReleaseContent(clearDelegatesAfterRelease: false);
        }

        internal void RestoreForSurface()
        {
            if (this && !destroying && isActiveAndEnabled && loadRequested)
            {
                OnEnable();
            }
        }

        void NotifyLifecycle(Action<Document> callbacks)
        {
            if (callbacks == null)
            {
                return;
            }
            BeginControlUpdate();
            try
            {
                foreach (Action<Document> callback in callbacks.GetInvocationList())
                {
                    try
                    {
                        callback(this);
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                    }
                }
            }
            finally
            {
                EndControlUpdate();
            }
        }

        void RetireContent(ContentState state, bool notify)
        {
            var surfaceHandle = state.SurfaceHandle;
            if (!ReferenceEquals(host, null) && host.Ready)
            {
                host.PrepareDocumentRetirement();
                if (NativeMethods.urui_document_close(surfaceHandle, state.Handle) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("URUI 文档关闭失败：" + NativeMethods.LastError(surfaceHandle));
                }
                host.ReleaseDocumentResources(state.Handle);
            }
            state.Retired = true;
            state.InvalidationSerial++;
            foreach (var element in state.Elements.Values)
            {
                element.Invalidate();
            }
            state.Elements.Clear();
            state.ElementSnapshot = null;
            state.EffectLayouts.Clear();
            ReleaseEffects(state);
            ReleasePainters(state);
            if (!ReferenceEquals(surface, null))
            {
                surface.Unbind(surfaceHandle, state.Handle);
                if (ReferenceEquals(activeContent, state))
                {
                    surface.MarkDocumentUnloaded(this);
                }
                if (candidateContent == null && !surface.HasLoadedDocuments && !ReferenceEquals(host, null) && host.Ready)
                {
                    host.ReleaseEmptySurfaceTargets();
                }
            }
            if (notify)
            {
                NotifyLifecycle(Unloaded);
            }
        }

        // Returns true if deferred so OnDestroy keeps Unloaded subscribers alive until drain.
        bool ReleaseContent(bool clearDelegatesAfterRelease)
        {
            if (DeferLifecycle(() => ReleaseContent(clearDelegatesAfterRelease)))
            {
                return true;
            }
            if (activeContent == null)
            {
                if (clearDelegatesAfterRelease)
                {
                    ClearLifecycleDelegates();
                }
                return false;
            }
            var target = activeContent;
            changingContent = true;
            surface.BeginTransaction();
            try
            {
                content = activeContent = null;
                surface.MarkDocumentUnloaded(this);
                RetireContent(target, notify: true);
            }
            finally
            {
                changingContent = false;
                surface.EndTransaction();
                if (clearDelegatesAfterRelease)
                {
                    ClearLifecycleDelegates();
                }
                FinishNativeCall();
            }
            return false;
        }
    }
}
