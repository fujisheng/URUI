using L = URUI.Editor.Localization.EditorText;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
namespace URUI.Authoring.Windows
{
    public sealed class UruiAnimationWindow : EditorWindow
    {
        TimelineView timeline;
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Animation", 100, Open);
            };
        }
        [MenuItem("Window/URUI/Animation")]
        public static void Open() => GetWindow<UruiAnimationWindow>(L.Tr("URUI 动画"));
        void OnEnable()
        {
            AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
            timeline = new TimelineView(Repaint);
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Repaint;
            EditorApplication.update += Tick;
        }
        void OnDisable()
        {
            AnimationEnvironment.Stop();
            AuthoringEnvironment.SetMode(WorkspaceMode.Layout);
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Repaint;
            EditorApplication.update -= Tick;
        }
        void Tick()
        {
            if (AnimationEnvironment.Playing)
                Repaint();
        }
        void OnGUI()
        {
            L.Title(this, "URUI 动画");
            timeline.Draw();
        }
    }
}
