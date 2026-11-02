using UnityEngine;

namespace URUI.Internal.Rendering.Shaders
{
    /// <summary>
    /// 将属性复用许可绑定到已核对的 shader 资产及源码摘要，不能仅按 shader 名称授权。
    /// V2 的 pass0 及其变体读取 MainTex、借用纹理状态、ClipEnabled、ClipRect，不读取 Translation。
    /// </summary>
    internal sealed class ShaderPropertyContract : ScriptableObject
    {
        public const string BuiltinPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/Premultiplied.shader";
        public const string BuiltinHash = "c9d6676fc82deb74bc77f08611d25a372b0fb00926e7e4c9cff31a8800cc67dc";
        public const string SamplingPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc";
        public const string SamplingHash = "44334ffe1217200b79359f1a43fc3966299deb2509aabe08d2ce44a6c6abeff5";
        [SerializeField]
        Shader verifiedShader;
        [SerializeField]
        string verifiedSourceHash;
        [SerializeField]
        int revision;

        public Shader VerifiedShader => verifiedShader;

        public string VerifiedSourceHash => verifiedSourceHash;

        public bool Supports(Material material)
        {
            if (revision != 2 || verifiedSourceHash != BuiltinHash || !verifiedShader || !material || material.shader != verifiedShader)
            {
                return false;
            }
#if UNITY_EDITOR
            // 资产导入会使结果失效。这里是复用正确性验证，正式 Editor 路径也必须执行。
            return EditorAuditMatches();
#else
            return true;
            // The scene build guard verified this immutable Player shader asset.
#endif
        }
#if UNITY_EDITOR
        static int editorAuditGeneration;
        [System.NonSerialized]
        int auditedGeneration = -1;
        [System.NonSerialized]
        Shader auditedShader;
        [System.NonSerialized]
        bool auditedResult;

        public static void InvalidateEditorAudit()
        {
            editorAuditGeneration++;
        }

        bool EditorAuditMatches()
        {
            if (auditedGeneration == editorAuditGeneration && auditedShader == verifiedShader)
            {
                return auditedResult;
            }

            auditedGeneration = editorAuditGeneration;
            auditedShader = verifiedShader;
            string path = UnityEditor.AssetDatabase.GetAssetPath(verifiedShader);
            if (path != BuiltinPath || !System.IO.File.Exists(ShaderSources.ResolveAssetPath(path)))
            {
                return auditedResult = false;
            }

            using var hash = System.Security.Cryptography.SHA256.Create();
            string actual = System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(ShaderSources.ResolveAssetPath(path)))).Replace("-", "").ToLowerInvariant();
            return auditedResult = actual == BuiltinHash && SamplingSourceMatches();
        }

        public static bool SamplingSourceMatches()
        {
            string path = ShaderSources.ResolveAssetPath(SamplingPath);
            if (!System.IO.File.Exists(path)) return false;
            using var hash = System.Security.Cryptography.SHA256.Create();
            return System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() == SamplingHash;
        }

        public void BindBuiltinForEditor(Shader source)
        {
            if (UnityEditor.AssetDatabase.GetAssetPath(source) != BuiltinPath)
            {
                throw new System.ArgumentException("Only the audited framework shader is supported by contract V2");
            }

            verifiedShader = source;
            verifiedSourceHash = BuiltinHash;
            revision = 2;
        }
#endif
    }
}
