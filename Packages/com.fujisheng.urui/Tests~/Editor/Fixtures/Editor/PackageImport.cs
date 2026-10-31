#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace URUI.Tests.Editor
{
    public static class PackageImport
    {
        const string Package = "Packages/com.fujisheng.urui";
        static void Need(bool condition, string reason) { if (!condition) throw new Exception(reason); }

        public static void Check()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Package + "/package.json");
            Need(info != null && info.name == "com.fujisheng.urui", "UPM resolution");
            var assemblies = CompilationPipeline.GetAssemblies();
            var runtime = assemblies.Single(value => value.name == "URUI.Runtime");
            Need(!runtime.assemblyReferences.Any(value => value.name == "Unity.ugui"), "runtime dependency closure");
            var definitions = Directory.GetFiles(info.resolvedPath, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith("~"))).ToArray();
            Need(definitions.Length == 2, "single runtime and editor assembly definitions");
            var shader = Resources.Load<Shader>("URUI/Premultiplied");
            Need(shader && shader.name == "URUI/Premultiplied", "shader identity");
            Need(AssetDatabase.LoadAssetAtPath<Shader>(Package + "/Runtime/Resources/URUI/ParameterTable.shader"), "parameter shader asset");
            var plugin = (PluginImporter)AssetImporter.GetAtPath(Package + "/Runtime/Plugins/macOS/libURUIBridge.dylib");
            Need(plugin != null && !plugin.GetCompatibleWithAnyPlatform() && plugin.GetCompatibleWithEditor() && plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), "Mac ARM64 importer");
            Need(plugin.GetEditorData("CPU") == "ARM64" && plugin.GetEditorData("OS") == "OSX", "Editor architecture");
            CheckPlugin("macOS/x86_64/libURUIBridge.dylib", BuildTarget.StandaloneOSX, "x86_64");
            CheckPlugin("Android/arm64-v8a/libURUIBridge.so", BuildTarget.Android, "ARM64");
            CheckPlugin("Android/x86_64/libURUIBridge.so", BuildTarget.Android, "x86_64");
            CheckPlugin("Windows/x86_64/URUIBridge.dll", BuildTarget.StandaloneWindows64, "x86_64");
            CheckPlugin("iOS/arm64/libURUIBridge.a", BuildTarget.iOS, "ARM64");
            CheckPlugin("WebGL/libURUIBridge.a", BuildTarget.WebGL, "AnyCPU");
            var ios = (PluginImporter)AssetImporter.GetAtPath(Package + "/Runtime/Plugins/iOS/arm64/libURUIBridge.a");
            Need(ios.GetPlatformData(BuildTarget.iOS, "SDK") == "DeviceSDK", "iOS device-only SDK scope");
            Directory.CreateDirectory("Evidence");
            File.WriteAllText("Evidence/import.json", "{\"passed\":true,\"singleRuntimeAssembly\":true,\"shaderResources\":true,\"macArm64Plugin\":true}");
            Debug.Log("URUI_IMPORT_PASS");
        }

        static void CheckPlugin(string path, BuildTarget target, string cpu)
        {
            var importer = (PluginImporter)AssetImporter.GetAtPath(Package + "/Runtime/Plugins/" + path);
            Need(importer != null && !importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithPlatform(target), "plugin target: " + path);
            Need(importer.GetPlatformData(target, "CPU") == cpu, "plugin architecture: " + path);
        }
    }
}
#endif
