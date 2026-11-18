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
        public const string BuiltinHash = "457a41bfd9530616ab58124aab9e39c07b880030eb472ff8261cfc30b6d1b1b3";
        public const string SamplingPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc";
        public const string SamplingHash = "c1fd347bb710881dd4ce216291b77e6703e44fd685881766c48b942efa495cb1";
        public const string ColorPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc";
        public const string ColorHash = "c2b97f762b4b38cbfd5f1ed96c5c3f8c4db437bbdb6c561eb21fe6c7304c5726";
        public const string PalettePath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedPalette.cginc";
        public const string PaletteHash = "3c7e2ed11fab88f49d71331e274c381c7bbf419994f019acf1e4734b97ddfc29";
        public const string ParametersPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/RenderParameters.cginc";
        public const string ParametersHash = "25d6608e0294441f4e1a5a58f1c230836530ea55a2b6582528a2347c3b416244";
        public const string CssColorPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/CssColorOps.cginc";
        public const string CssColorHash = "32f6e6108f784522c6b96c86b3dee1d6b2ddea50891cfdce04dcbf0ac67e94bc";
        public const string MaskPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/DirectMask.cginc";
        public const string MaskHash = "30f3ada67d605858010051c7412b228c49b6a6bfbfa0ff85be9ff29303b1af73";
        public const string ProgramPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedProgram.cginc";
        public const string ProgramHash = "8af603e6de0a82587b3a7f690d55568bdb1b9bfee596de4533a47235890ccd6f";
        public const string FetchPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedFetch.shader";
        public const string FetchHash = "8441aafe4d24bb8fbcadae4906f50e527a6bd374fd2a1a4de26d7285b58dfe0a";
        public const string FramebufferPath = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/FramebufferBlend.cginc";
        public const string FramebufferHash = "93bbb5e65eb94580f4ab7d34bee0c3f81f1850d202025995bc59a3aa72052893";
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
            foreach(var source in new[]{(SamplingPath,SamplingHash),(ColorPath,ColorHash),(PalettePath,PaletteHash),(ParametersPath,ParametersHash),(CssColorPath,CssColorHash),(MaskPath,MaskHash),(ProgramPath,ProgramHash),(FetchPath,FetchHash),(FramebufferPath,FramebufferHash)})
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
                throw new System.ArgumentException("Only the audited framework shader is supported by contract V7");
            }

            verifiedShader = source;
            verifiedSourceHash = BuiltinHash;
            revision = 7;
        }
#endif
    }
}
