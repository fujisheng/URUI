using UnityEngine;

namespace URUI.Internal.Rendering.Parameters
{
    // Keeps leases alive after producer teardown. No polling interval is treated
    // as evidence of completion; Uncertain packets deliberately remain retained.
    internal sealed class ParameterRetirement : MonoBehaviour
    {
        static ParameterRetirement owner;
        readonly System.Collections.Generic.List<ParameterTableRenderer> retired = new System.Collections.Generic.List<ParameterTableRenderer>();

        internal static int Count => owner ? owner.retired.Count : 0;

        internal static bool Admission => !owner || owner.retired.Count < 4;

        internal static void Retire(ParameterTableRenderer renderer)
        {
            if (renderer.TryDispose())
            {
                return;
            }

            if (!owner)
            {
                var obj = new GameObject("URUI parameter fence retirement")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(obj);
                owner = obj.AddComponent<ParameterRetirement>();
            }

            owner.retired.Add(renderer);
        }

        void Update()
        {
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                if (retired[i].TryDispose())
                {
                    retired.RemoveAt(i);
                }
            }
        }
    }
}
