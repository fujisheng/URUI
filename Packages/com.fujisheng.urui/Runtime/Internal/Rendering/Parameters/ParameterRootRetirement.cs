using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Resources;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Parameters
{
    internal sealed class ParameterRootRetirement : MonoBehaviour
    {
        static ParameterRootRetirement owner;
        static int live;
        readonly List<ParameterRootRecording> pending = new List<ParameterRootRecording>(4);

        internal static int LiveCount => live;

        internal static bool Admission => live < 4;

        internal static ParameterRootRecording Create(
            CommandBuffer target,
            Shader legacy,
            Shader parameter,
            bool shareResources = false,
            bool reuseBatches = false,
            bool reuseBatchScratch = false,
            bool lazySourceMeshes = false,
            bool nativePrepared = false,
            bool nativeIncremental = false,
            bool nativePinned = false,
            BorrowedTextureRegistry borrowedTextures = null)
        {
            if (!Admission)
            {
                return null;
            }

            var result = new ParameterRootRecording(target, legacy, parameter, shareResources, reuseBatches, reuseBatchScratch, lazySourceMeshes, nativePrepared, nativeIncremental, nativePinned, borrowedTextures)
            {
                registered = true
            };
            live++;
            return result;
        }

        static void Completed(ParameterRootRecording value)
        {
            if (value.registered)
            {
                value.registered = false;
                live--;
            }
        }

        internal static void Retire(ParameterRootRecording value)
        {
            value.Close();
            if (value.TryDispose())
            {
                Completed(value);
                return;
            }

            if (!owner)
            {
                var go = new GameObject("URUI root retirement")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(go);
                owner = go.AddComponent<ParameterRootRetirement>();
            }

            owner.pending.Add(value);
        }

        void Update()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].TryDispose())
                {
                    Completed(pending[i]);
                    pending.RemoveAt(i);
                }
            }
        }
    }
}
