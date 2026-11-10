using UnityEditor;
namespace URUI.Effect.Editor
{
    [InitializeOnLoad]
    static class EffectEditorRetirement
    {
        static EffectEditorRetirement(){EditorApplication.update+=URUI.Resources.GpuRetirement.Collect;}
    }
}
