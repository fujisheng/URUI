using System;
using UnityEngine;

namespace URUI.Internal.Rendering.Shaders
{
    // 通过已审核 Shader 资产及源码指纹确认参数通道约定，避免仅按名称接受不兼容材质。
    internal sealed class ParameterShaderCapability : ScriptableObject
    {
        public const string SourcePath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ParameterTable.shader";
        public const string SourceHash = "21b78c79daa5693c467ce26ab3111e0fb3fcf19f02148cad5fd4bef2fcdd2049";
        [SerializeField]
        Shader parameter;
        [SerializeField]
        Shader legacy;
        [SerializeField]
        string audit;
        [SerializeField]
        int revision;

        public Shader Parameter => parameter;

        public bool Supports(Material m, ShaderPropertyContract old)
        {
            if (revision != 2 || audit != SourceHash || !parameter || !legacy || !m || m.shader != legacy || !old || !old.Supports(m))
            {
                return false;
            }
#if UNITY_EDITOR
            return AuditEditor();
#else
            return true;
#endif
        }
#if UNITY_EDITOR
        public bool AuditEditor()
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(parameter);
            if (path != SourcePath
                || !System.IO.File.Exists(ShaderSources.ResolveAssetPath(path))
                || UnityEditor.AssetDatabase.GetAssetPath(legacy) != ShaderPropertyContract.BuiltinPath)
            {
                return false;
            }

            using var sha = System.Security.Cryptography.SHA256.Create();
            return revision == 2
                && audit == SourceHash
                && ShaderPropertyContract.SamplingSourceMatches()
                && BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(ShaderSources.ResolveAssetPath(path)))).Replace("-", "").ToLowerInvariant() == SourceHash;
        }

        public void BindEditor(Shader p, Shader l)
        {
            parameter = p;
            legacy = l;
            audit = SourceHash;
            revision = 2;
            if (!AuditEditor())
            {
                throw new ArgumentException("Parameter shader capability requires audited framework sources");
            }
        }
#endif
    }
}
