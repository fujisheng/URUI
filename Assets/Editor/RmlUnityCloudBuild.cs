using System;
using System.IO;
using RmlUnity.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Unity Build Automation pre-export hook.
/// Configure the fully qualified method name:
/// RmlUnityCloudBuild.PreExport
///
/// Unity Build Automation invokes this *inside* an activated Unity Editor.
/// Unlike GameCI's GitHub-hosted Editor, it does not require us to upload a
/// manually activated Unity Personal license.
/// </summary>
public static class RmlUnityCloudBuild
{
    private const string NativePlugin =
        "Assets/Plugins/Android/libs/arm64-v8a/libRmlUnityBridge.so";
    private const string DemoScene =
        "Assets/RmlUnity/Demo/RmlUnityDemo.unity";

    public static void PreExport()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException(
                "RmlUnity MVP requires Android as the Build Automation platform.");

        if (!File.Exists(NativePlugin))
            throw new FileNotFoundException(
                "RmlUi ARM64 .so missing. First run the GitHub Actions workflow " +
                "'Publish Android ARM64 Native Plugin' and wait for it to commit " +
                "Assets/Plugins/Android/libs/arm64-v8a/libRmlUnityBridge.so.",
                NativePlugin);

        // The MVP scene is generated from source by the editor menu helper,
        // so a designer does not have to serialize a scene on their computer.
        RmlUnityMenu.CreateDemo();

        if (!File.Exists(DemoScene))
            throw new FileNotFoundException(
                "Could not generate the RmlUnity demo scene", DemoScene);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(DemoScene, true)
        };

        PlayerSettings.companyName = "RmlUnity";
        PlayerSettings.productName = "RmlUnity MVP";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android,
            "com.fujisheng.rmlunity.mvp");

        PlayerSettings.SetScriptingBackend(
            NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.bundleVersionCode = 1;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.Android.useCustomKeystore = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[RmlUnity] Unity Build Automation Android pre-export complete.");
    }
}
