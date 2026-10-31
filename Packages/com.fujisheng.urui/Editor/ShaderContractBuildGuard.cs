using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using URUI.Internal;

namespace URUI.Editor
{
    /// <summary>
    /// 构建前验证 shader 源码与依赖契约一致，阻止使用未重新审计的渲染资源。
    /// </summary>
    /// <remarks>该检查保证构建正确性，始终启用，不属于可关闭的诊断采样。</remarks>
    internal sealed class ShaderContractBuildGuard : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        /// <summary>
        /// 检查契约绑定的 shader 路径、当前源码哈希与记录值是否一致。
        /// </summary>
        /// <param name="contract">可选契约；未提供时不启用该契约的优化路径。</param>
        public static void Validate(ShaderPropertyContract contract)
        {
            if (!contract)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(contract.VerifiedShader);
            using var sha = SHA256.Create();
            string hash = File.Exists(ShaderSources.ResolveAssetPath(path)) ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ShaderSources.ResolveAssetPath(path)))).Replace("-", "").ToLowerInvariant() : "";
            if (path != ShaderPropertyContract.BuiltinPath
                || hash != ShaderPropertyContract.BuiltinHash
                || contract.VerifiedSourceHash != hash)
            {
                throw new BuildFailedException("Rml shader dependency contract changed: re-audit or remove the contract; unknown shaders require the legacy property path.");
            }
        }

        /// <summary>
        /// 检查场景中全部宿主，包括当前未启用的对象。
        /// </summary>
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var host in root.GetComponentsInChildren<RenderHost>(true))
                {
                    Validate(host.propertyContract);
                    if (host.parameterCapability && !host.parameterCapability.AuditEditor())
                    {
                        throw new BuildFailedException("Parameter shader capability requires re-audit");
                    }
                }
            }
        }

        /// <summary>
        /// 获取或创建内置 shader 的契约资产，并在返回前验证其有效性。
        /// </summary>
        /// <returns>位于 Assets/URUIGenerated、由项目持有的契约资产。</returns>
        public static ShaderPropertyContract Prepare()
        {
            System.IO.Directory.CreateDirectory("Assets/URUIGenerated");
            AssetDatabase.Refresh();
            const string path = "Assets/URUIGenerated/BuiltinShaderPropertyContract.asset";
            var contract = AssetDatabase.LoadAssetAtPath<ShaderPropertyContract>(path);
            if (!contract)
            {
                contract = UnityEngine.ScriptableObject.CreateInstance<ShaderPropertyContract>();
                contract.BindBuiltinForEditor(AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(ShaderPropertyContract.BuiltinPath));
                AssetDatabase.CreateAsset(contract, path);
                AssetDatabase.SaveAssets();
            }

            Validate(contract);
            return contract;
        }
    }
}
