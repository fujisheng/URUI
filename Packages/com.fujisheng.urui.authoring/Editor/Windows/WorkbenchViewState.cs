using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace URUI.Authoring.Windows
{
    [FilePath("Library/URUI/Authoring/ViewState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class WorkbenchViewState : ScriptableSingleton<WorkbenchViewState>
    {
        [Serializable]
        internal sealed class Canvas
        {
            public string Path;
            public float Zoom = 1;
            public Vector2 Pan;
            public Vector2Int Size;
        }

        [SerializeField]
        List<Canvas> canvases = new List<Canvas>();

        internal Canvas ForPage(string path)
        {
            var state = canvases.Find(item => item.Path == path);
            if (state != null)
            {
                return state;
            }
            state = new Canvas { Path = path };
            canvases.Add(state);
            return state;
        }

        internal void SaveView() => Save(true);
    }
}
