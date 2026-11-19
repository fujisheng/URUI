using System.Linq;
using UnityEditor;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal static class RuntimeDebugWindows
    {
        const string MenuPath = "Window/URUI/Runtime Debugging";
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenu()
            => EditorApplication.delayCall += () => URUI.Editor.Localization.EditorMenus.Register(MenuPath, 120, Open);

        [MenuItem(MenuPath, false, 120)]
        internal static void Open()
        {
            OpenPanel<RuntimeInspectorWindow>("URUI Inspector", "InspectorWindow");
            OpenHierarchy();
        }

        internal static void OpenHierarchy() => OpenPanel<RuntimeHierarchyWindow>("URUI Hierarchy", "SceneHierarchyWindow");

        static void OpenPanel<T>(string title, string siblingType) where T : EditorWindow
        {
            var sibling = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(window => window.docked && window.GetType().Name == siblingType);
            if (sibling)
            {
                EditorWindow.GetWindow<T>(L.Tr(title), true, sibling.GetType());
            }
            else
            {
                EditorWindow.GetWindow<T>(L.Tr(title));
            }
        }
    }
}
