using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Frames
{
    internal sealed class NativeFrameRetirement : MonoBehaviour
    {
        static NativeFrameRetirement owner;
        static int live;
        readonly List<NativeFrameRoot> pending = new List<NativeFrameRoot>();

        internal static int LiveCount => live;

        internal static bool Admission => live < 4;

        internal static NativeFrameRoot Create(CommandBuffer target, Shader legacy, Shader parameter)
        {
            if (!Admission)
            {
                return null;
            }

            var value = new NativeFrameRoot(target, legacy, parameter)
            {
                registered = true
            };
            live++;
            return value;
        }

        static void Done(NativeFrameRoot value)
        {
            if (value.registered)
            {
                value.registered = false;
                live--;
            }
        }

        internal static void Retire(NativeFrameRoot value)
        {
            value.Close();
            if (value.TryDispose())
            {
                Done(value);
                return;
            }

            if (!owner)
            {
                var go = new GameObject("URUI native final-frame retirement")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(go);
                owner = go.AddComponent<NativeFrameRetirement>();
            }

            owner.pending.Add(value);
        }

        void Update()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].TryDispose())
                {
                    Done(pending[i]);
                    pending.RemoveAt(i);
                }
            }
        }
    }
}
