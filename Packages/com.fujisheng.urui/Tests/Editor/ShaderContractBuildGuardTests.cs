using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace URUI.Tests.Editor
{
    public sealed class ShaderContractBuildGuardTests
    {
        [Test]
        public void PackagedShaderClosurePassesTheRealSceneBuildGuard()
        {
            var type = Type.GetType("URUI.Editor.Validation.ShaderContractBuildGuard, URUI.Editor", true);
            var guard = (IProcessSceneWithReport)Activator.CreateInstance(type, true);
            Assert.DoesNotThrow(() => guard.OnProcessScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), null));
        }
        [Test]
        public void ACorruptedSerializedShaderContractIsRejected()
        {
            var source = UnityEngine.Resources.Load<ScriptableObject>("URUI/BuiltinShaderPropertyContract");
            Assert.That(source, Is.Not.Null);
            var contract = UnityEngine.Object.Instantiate(source);
            try
            {
                var serialized = new SerializedObject(contract);
                serialized.FindProperty("verifiedSourceHash").stringValue = "invalid";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var type = Type.GetType("URUI.Editor.Validation.ShaderContractBuildGuard, URUI.Editor", true);
                var validate = type.GetMethod("Validate");
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => validate.Invoke(null, new object[] { contract }));
                Assert.That(error.InnerException, Is.TypeOf<BuildFailedException>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(contract);
            }
        }

        [Test]
        public void PremultipliedShaderIncludesDeclareExplicitFloat4TextureTypes()
        {
            var regex = new System.Text.RegularExpressions.Regex(@"\bTexture2D\s+(?!<)");
            var paths = new[]
            {
                "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc",
                "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedPalette.cginc",
                "Packages/com.fujisheng.urui/Runtime/Resources/URUI/DirectMask.cginc"
            };
            foreach (var assetPath in paths)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                Assert.That(package, Is.Not.Null, assetPath);
                var prefix = "Packages/" + package.name + "/";
                var fullPath = System.IO.Path.Combine(package.resolvedPath, assetPath.Substring(prefix.Length));
                Assert.That(System.IO.File.Exists(fullPath), Is.True, fullPath);
                var source = System.IO.File.ReadAllText(fullPath);
                Assert.That(regex.IsMatch(source), Is.False,
                    assetPath + " 必须使用 Texture2D<float4> 而非裸 Texture2D，避免 Unity HLSLcc 将共享寄存器降级为 half/ushort 破坏 32 位参数寻址。");
            }
        }
    }
}
