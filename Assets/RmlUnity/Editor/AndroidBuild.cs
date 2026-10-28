using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RmlUnity.Editor
{
    // Invoked by GameCI with: buildMethod: RmlUnity.Editor.AndroidBuild.Build
    public static class AndroidBuild
    {
        public static void Build()
        {
            const string scenePath = "Assets/RmlUnity/Demo/RmlUnityDemo.unity";
            const string outputRelative = "build/Android/RmlUnity-MVP.apk";
            const string nativePath = "Assets/Plugins/Android/libs/arm64-v8a/libRmlUnityBridge.so";

            if (!File.Exists(nativePath))
                throw new FileNotFoundException("Missing Android ARM64 RmlUi native plugin. Run the Native build job first.", nativePath);

            RmlUnityMenu.CreateDemo();
            if (!File.Exists(scenePath))
                throw new FileNotFoundException("Could not generate the Android demo scene.", scenePath);

            PlayerSettings.companyName = "RmlUnity";
            PlayerSettings.productName = "RmlUnity MVP";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.fujisheng.rmlunity.mvp");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.useCustomKeystore = false; // MVP: debug-signed APK only.
            EditorUserBuildSettings.buildAppBundle = false;
            // GameCI passes -buildTarget Android when launching Unity. Switching
            // target in batch mode is unsupported and would fail this build.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Unity was not started with -buildTarget Android.");

            var output = Path.GetFullPath(outputRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new Exception("RmlUnity Android APK build failed. Check Unity Editor logs.");

            Debug.Log("RmlUnity Android APK: " + output);
        }
    }
}
