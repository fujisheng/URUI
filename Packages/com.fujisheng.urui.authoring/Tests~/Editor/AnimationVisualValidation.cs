using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
namespace URUI.Authoring.Tests
{
    public static class AnimationVisualValidation
    {
        static int frames;
        static readonly List<string> errors = new List<string>();
        public static void Run()
        {
            try
            {
                Application.logMessageReceived += (message, stack, type) =>
                {
                    if (type == LogType.Error || type == LogType.Exception)
                    {
                        errors.Add(message + "\n" + stack);
                    }
                };
                string sample = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AuthoringEnvironment).Assembly).resolvedPath, "Samples~/Animation Demo");
                Directory.CreateDirectory("Assets/AnimationDemo");
                foreach (string file in Directory.GetFiles(sample))
                {
                    if (!file.EndsWith(".meta"))
                    {
                        File.Copy(file, "Assets/AnimationDemo/" + Path.GetFileName(file), true);
                    }
                }
                AssetDatabase.Refresh();
                AuthoringEnvironment.Open("Assets/AnimationDemo/Reward.html");
                AnimationEnvironment.EnsureWorkspace();
                AnimationEnvironment.SelectTrack("reward-in");
                AnimationEnvironment.Seek(.2);
                AuthoringWindow.OpenAnimation();
                GetWindow();
                EditorApplication.ExecuteMenuItem("Window/General/Inspector");
                Directory.CreateDirectory("Evidence");
                EditorApplication.update += Tick;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
        static void GetWindow()
        {
            var window = EditorWindow.GetWindow<AuthoringWindow>();
            window.position = new Rect(20, 50, 1220, 820);
            window.Focus();
        }
        static void Tick()
        {
            frames++;
            if (frames < 80)
            {
                return;
            }
            if (errors.Count != 0)
            {
                File.WriteAllText("Evidence/animation-visual-failure.txt", string.Join("\n", errors));
                EditorApplication.update -= Tick;
                EditorApplication.Exit(1);
                return;
            }
            if (frames == 80)
            {
                AuthoringEnvironment.Preview.Capture(Path.GetFullPath("Evidence/reward.png"));
                File.WriteAllText("Evidence/animation-visual.json", "{\"guiFrames\":80,\"errors\":0,\"renderedRevision\":" + AuthoringEnvironment.Preview.RenderedRevision + ",\"frameId\":" + AuthoringEnvironment.Preview.FrameId + "}");
                Debug.Log("URUI Animation Visual READY");
            }
            if (File.Exists("Library/URUI/Authoring/finish-visual"))
            {
                EditorApplication.update -= Tick;
                AnimationEnvironment.Stop();
                EditorApplication.Exit(0);
            }
        }
    }
}
