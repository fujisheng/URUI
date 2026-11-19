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
        }
    }
}
