using UnityEngine;

namespace URUI.Internal.Rendering.Shaders
{
    /// <summary>
    /// 将属性复用许可绑定到已核对的 shader 资产及源码摘要，不能仅按 shader 名称授权。
    /// V5 的 pass0 读取首纹理、借用状态、裁剪与采样，不读取 Translation；额外纹理槽在每次批次提交时完整写入。
    /// </summary>
    internal sealed class ShaderPropertyContract : ScriptableObject
    {
        public const string BuiltinPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/Premultiplied.shader";
        public const string BuiltinHash = "d1e1a118b75ff7e69da80bc721a090cdedf0d23f82a022440a1a08209c05c867";
        public const string SamplingPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc";
        public const string SamplingHash = "62211c4c8ce3f27b8251066940b75e126ada1d72c2f48a7784b508eeff213871";
        public const string ColorPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc";
        public const string ColorHash = "c2b97f762b4b38cbfd5f1ed96c5c3f8c4db437bbdb6c561eb21fe6c7304c5726";
        public const string PalettePath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedPalette.cginc";
        public const string PaletteHash = "c26a5382f92109cec828a1bace1929367a3ae4306c5984deee7fbbfcd50d5be0";
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
            if (revision != 5 || verifiedSourceHash != BuiltinHash || !verifiedShader || !material || material.shader != verifiedShader)
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
            // Sampling reads the geometry-local addressing flag; command-state equality includes all flag bits.
            // Audit the whole shared source closure before allowing recorded parameter reuse.
            foreach(var source in new[]{(SamplingPath,SamplingHash),(ColorPath,ColorHash),(PalettePath,PaletteHash)})
            {
                string path=ShaderSources.ResolveAssetPath(source.Item1);
                if(!System.IO.File.Exists(path)){return false;}
                using var hash=System.Security.Cryptography.SHA256.Create();
                if(System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()!=source.Item2){return false;}
            }
            return true;
        }

        public void BindBuiltinForEditor(Shader source)
        {
            if (UnityEditor.AssetDatabase.GetAssetPath(source) != BuiltinPath)
            {
                throw new System.ArgumentException("Only the audited framework shader is supported by contract V5");
            }

            verifiedShader = source;
            verifiedSourceHash = BuiltinHash;
            revision = 5;
        }
#endif
    }
}
