using UnityEditor;
using URUI.Internal.Resources;

namespace URUI.Editor.Inspection
{
    [InitializeOnLoad]
    static class EditorResourceRetirement
    {
        static EditorResourceRetirement()
        {
            EditorApplication.update += ImagePinRetirement.Collect;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                {
                    ImagePinRetirement.OnExitingPlayMode();
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    ImagePinRetirement.OnEnteredEditMode();
                }
            };
        }
    }
}
