using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Editor.Validation
{
    /// <summary>
    /// 构建前验证 shader 源码与依赖契约一致，阻止使用未重新审计的渲染资源。
    /// </summary>
    /// <remarks>
    /// 该检查保证构建正确性，始终启用，不属于可关闭的诊断采样。
    /// </remarks>
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
                || !ShaderPropertyContract.SamplingSourceMatches()
                || hash != ShaderPropertyContract.BuiltinHash
                || contract.VerifiedSourceHash != hash)
            {
                throw new BuildFailedException("URUI builtin shader dependency contract changed: re-audit the packaged shader source.");
            }
        }

        /// <summary>
        /// 检查场景中全部宿主，包括当前未启用的对象。
        /// </summary>
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
            {
                return;
            }
            var contract = UnityEngine.Resources.Load<ShaderPropertyContract>("URUI/BuiltinShaderPropertyContract");
            if (!contract)
            {
                throw new BuildFailedException("URUI builtin shader contract resource is missing.");
            }
            Validate(contract);
        }

    }
}
