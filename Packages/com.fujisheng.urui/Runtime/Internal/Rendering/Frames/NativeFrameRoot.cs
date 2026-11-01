using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Frames
{
    // An attached camera buffer owns draw recording. Native CPU views never outlive
    // the synchronous Unity API calls; immutable Unity resources retire separately.
    internal sealed partial class NativeFrameRoot
    {
        readonly Dictionary<ulong, MeshEntry> meshes = new Dictionary<ulong, MeshEntry>();
        readonly Dictionary<(ulong, ulong), TextureEntry> textures = new Dictionary<(ulong, ulong), TextureEntry>();
        readonly List<Retirement> retired = new List<Retirement>();
        readonly Material parameter;
        readonly Material legacy;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly Action<Mesh> failedUpload;
        Plan building;
        readonly int thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        BorrowedFrame source;
        readonly CommandBuffer attachment;
        CommandBuffer root;
        Plan current;
        GraphicsBuffer buffer;
        int capacity;
        long retainedBytes;
        bool recording;
        bool closing;
        bool closed;
        bool disposed;
        internal bool registered;

        internal int RetiredLimit => 32;

        internal CommandBuffer Attachment => attachment;

        internal bool CloseComplete => closed;

        internal int UnsafeFrame { get; private set; } = -1;
        internal bool Faulted
        {
            get;
            private set;
        }
        internal bool CanFallback { get; private set; } = true;
        internal string FailureReason
        {
            get;
            private set;
        }

        internal NativeFrameRoot(CommandBuffer target, Shader legacyShader, Shader parameterShader)
        {
            attachment = target;
            root = target;
            failedUpload = RetireFailedUpload;
            source = new BorrowedFrame();
            try
            {
                legacy = new Material(legacyShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                parameter = new Material(parameterShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            catch
            {
                if (legacy)
                {
                    UnityEngine.Object.Destroy(legacy);
                }

                if (parameter)
                {
                    UnityEngine.Object.Destroy(parameter);
                }

                source.Dispose();
                throw;
            }
        }

        bool CorrectThread => thread == System.Threading.Thread.CurrentThread.ManagedThreadId;
    }
}
