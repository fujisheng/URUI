using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using URUI.Documents;
using URUI.Resources;

namespace URUI.Editor.Compilation
{
    /// <summary>
    /// 正式构建必须使用当前编译资产，禁止在 Player 中静默回退到源码解析。
    /// </summary>
    sealed class CompiledAssetBuildGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => -3000;
        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:CompiledDocumentAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<CompiledDocumentAsset>(AssetDatabase.GUIDToAssetPath(guid));
                try
                {
                    CompiledDocumentCompiler.ValidateCurrent(asset);
                }
                catch (System.Exception error)
                {
                    throw new BuildFailedException(error.Message);
                }
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
            {
                return;
            }
            ValidateScene(scene);
        }

        internal static void ValidateScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var document in root.GetComponentsInChildren<Document>(true))
                {
                    var source = document.SerializedSource;
                    var compiled = document.SerializedCompiledSource;
                    if (source && !compiled)
                    {
                        throw new BuildFailedException("URUI 场景文档缺少编译资产：" + document.name + "。请先编译 HTML 并配置 CompiledSource。");
                    }
                    if (compiled)
                    {
                        CompiledDocumentCompiler.ValidateCurrent(compiled);
                    }
                }
            }
        }
    }
}
