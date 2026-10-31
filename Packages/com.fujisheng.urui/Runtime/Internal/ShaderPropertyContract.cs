using UnityEngine;

namespace URUI.Internal
{
    /// <summary>
    /// 将属性复用许可绑定到已核对的 shader 资产及源码摘要，不能仅按 shader 名称授权。
    /// V1 的 pass 0 及其变体只读取 MainTex、ClipEnabled、ClipRect，不读取 Translation。
    /// </summary>
    internal sealed class ShaderPropertyContract : ScriptableObject
    {
        public const string BuiltinPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/Premultiplied.shader";
        public const string BuiltinHash = "6a3f982954e70f47d9dca65904f6352cf2b08e87722af5a504b8e1b3173ba0f6";
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
            if (revision != 1 || verifiedSourceHash != BuiltinHash || !verifiedShader || !material || material.shader != verifiedShader)
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
            return auditedResult = actual == BuiltinHash;
        }

        public void BindBuiltinForEditor(Shader source)
        {
            if (UnityEditor.AssetDatabase.GetAssetPath(source) != BuiltinPath)
            {
                throw new System.ArgumentException("Only the audited framework shader is supported by contract V1");
            }

            verifiedShader = source;
            verifiedSourceHash = BuiltinHash;
            revision = 1;
        }
#endif
    }
}
