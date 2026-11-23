using UnityEngine;

namespace URUI.Internal.Rendering.Shaders
{
    /// <summary>
    /// 将属性复用许可绑定到已核对的 shader 资产及源码摘要，不能仅按 shader 名称授权。
    /// V7 的 pass0 读取原生批次顶点和完整精度参数页；变换、裁剪、UV、乘色及采样属于每个参数索引。
    /// 解析矩形遮罩复用8texel描述的投影列2保存羽化距离；只有明确的analytic标记启用该语义。
    /// 批次缓存初始布局位置，参数保留完整差量矩阵；pass1 仅消费已被原生证明完整的顶点、UV与颜色。
    /// </summary>
    internal sealed class ShaderPropertyContract : ScriptableObject
    {
        public const string BuiltinPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/Premultiplied.shader";
        public const string BuiltinHash = "e72e926aec9fe2b8c663adbec0d346abe375ee2936acf7ea920438370af34c36";
        public const string SamplingPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc";
        public const string SamplingHash = "6be8f2a2c386995fe04e9db3b252920dbd320f704122d43b015b7f34dd4a816a";
        public const string ColorPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc";
        public const string ColorHash = "3b4c1d6e177a437e5a048be72a9851228eddd495efb603822fbfae79430bd4fb";
        public const string PalettePath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedPalette.cginc";
        public const string PaletteHash = "34be457bba1aa52391a11ca82838cc8e1945a159ebd25c32f7664a0ffed88037";
        public const string ParametersPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/RenderParameters.cginc";
        public const string ParametersHash = "0df621c72f3d7a1144f9c9e66678772e3d7b8e7125b976e077187d8551ee2472";
        public const string CssColorPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/CssColorOps.cginc";
        public const string CssColorHash = "4820705e4b07286d6edad8eb44be8deb0fb4984ff99d306211c988540edcb85f";
        public const string MaskPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/DirectMask.cginc";
        public const string MaskHash = "3fb5d296c1f448ce1a79cea5f2b84334e40c5f5a349ea933ddc25e5606214c37";
        public const string ProgramPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedProgram.cginc";
        public const string ProgramHash = "2e80dbd97f66e23351aeed1b8ee3b18caa2fea54eca7f7572bc817908ab4577a";
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
            if (revision != 7 || verifiedSourceHash != BuiltinHash || !verifiedShader || !material || material.shader != verifiedShader)
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
            foreach (var source in new[] { (SamplingPath, SamplingHash), (ColorPath, ColorHash), (PalettePath, PaletteHash), (ParametersPath, ParametersHash), (CssColorPath, CssColorHash), (MaskPath, MaskHash), (ProgramPath, ProgramHash) })
            {
                string path = ShaderSources.ResolveAssetPath(source.Item1);
                if (!System.IO.File.Exists(path))
                {
                    return false;
                }
                using var hash = System.Security.Cryptography.SHA256.Create();
                if (System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() != source.Item2)
                {
                    return false;
                }
            }
            return true;
        }
#endif
    }
}
