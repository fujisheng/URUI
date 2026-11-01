using System;
using UnityEngine;

namespace URUI.Internal.Rendering.Shaders
{
    // 通过已审核 Shader 资产及源码指纹确认参数通道约定，避免仅按名称接受不兼容材质。
    internal sealed class ParameterShaderCapability : ScriptableObject
    {
        public const string SourcePath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ParameterTable.shader";
        public const string SourceHash = "ed3b104eb76f2cd4d1b74c0a6e5d2722b90af0e44ed3561f2eef4b46dd6d39b2";
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
            if (revision != 1 || audit != SourceHash || !parameter || !legacy || !m || m.shader != legacy || !old || !old.Supports(m))
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
            return revision == 1
                && audit == SourceHash
                && BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(ShaderSources.ResolveAssetPath(path)))).Replace("-", "").ToLowerInvariant() == SourceHash;
        }

        public void BindEditor(Shader p, Shader l)
        {
            parameter = p;
            legacy = l;
            audit = SourceHash;
            revision = 1;
            if (!AuditEditor())
            {
                throw new ArgumentException("Parameter shader capability requires audited framework sources");
            }
        }
#endif
    }
}
