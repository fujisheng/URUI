#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityResources = UnityEngine.Resources;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 检查包解析、程序集依赖、shader 资源和各平台插件导入设置。
    /// </summary>
    public static class PackageImport
    {
        const string Package = "Packages/com.fujisheng.urui";

        [Serializable] sealed class SampleImportReport
        {
            public bool passed;
            public string packageVersion;
            public SampleImportEntry[] samples;
        }
        [Serializable] sealed class SampleImportEntry
        {
            public string name, sourcePath, importPath;
            public bool importedNow, previouslyImported;
            public int fileCount;
        }

        /// <summary>在尚未复制运行时夹具的独立工程中使用真实 UPM 导入全部声明样例。</summary>
        public static void ImportSamples()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Package + "/package.json");
                Need(info != null, "UPM resolution before sample import");
                var samples = UnityEditor.PackageManager.UI.Sample.FindByPackage(info.name, info.version).ToArray();
                Need(samples.Length != 0, "package declares samples");
                var entries = samples.Select(sample =>
                {
                    var previouslyImported = sample.isImported;
                    Need(sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports),
                        "Sample.Import: " + sample.displayName);
                    Need(Directory.Exists(sample.importPath), "sample import directory: " + sample.displayName);
                    var files = Directory.GetFiles(sample.resolvedPath, "*", SearchOption.AllDirectories)
                        .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal)).ToArray();
                    foreach (var source in files)
                    {
                        var target = Path.Combine(sample.importPath, Path.GetRelativePath(sample.resolvedPath, source));
                        Need(File.Exists(target) && File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)),
                            "sample source bytes: " + target);
                    }
                    return new SampleImportEntry { name = sample.displayName, sourcePath = sample.resolvedPath,
                        importPath = sample.importPath, importedNow = true, previouslyImported = previouslyImported, fileCount = files.Length };
                }).ToArray();
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/sample-import.json", JsonUtility.ToJson(new SampleImportReport {
                    passed = true, packageVersion = info.version, samples = entries }, true));
                Debug.Log("URUI_SAMPLE_IMPORT_PASS " + entries.Length);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        static void Need(bool condition, string reason)
        {
            if (!condition)
            {
                throw new Exception(reason);
            }
        }

        /// <summary>
        /// 读取实际导入结果并写入报告；不将导入配置检查视为对应平台的运行验证。
        /// </summary>
        public static void Check()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Package + "/package.json");
            Need(info != null && info.name == "com.fujisheng.urui", "UPM resolution");
            var assemblies = CompilationPipeline.GetAssemblies();
            var runtime = assemblies.Single(value => value.name == "URUI.Runtime");
            Need(!runtime.assemblyReferences.Any(value => value.name == "Unity.ugui"), "runtime dependency closure");
            var definitions = Directory.GetFiles(info.resolvedPath, "*.asmdef", SearchOption.AllDirectories).Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith("~") || part == "Tests")).ToArray();
            Need(definitions.Length == 2, "single runtime and editor assembly definitions");
            var shader = UnityResources.Load<Shader>("URUI/Premultiplied");
            Need(shader && shader.name == "URUI/Premultiplied", "shader identity");
            Need(AssetDatabase.LoadAssetAtPath<Shader>(Package + "/Runtime/Resources/URUI/ParameterTable.shader"), "parameter shader asset");
            var plugin = (PluginImporter)AssetImporter.GetAtPath(Package + "/Runtime/Plugins/macOS/libURUIBridge.dylib");
            Need(plugin != null
                && !plugin.GetCompatibleWithAnyPlatform()
                && plugin.GetCompatibleWithEditor()
                && plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), "Mac ARM64 importer");
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
