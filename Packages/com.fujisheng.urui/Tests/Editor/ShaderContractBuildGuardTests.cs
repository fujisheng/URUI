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
    }
}
