using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace URUI.Effect.Tests
{
    public sealed class EffectParameterTests
    {
        [Test]
        public void PresetSnapshotsCopyGradientDataAndKeepSourceDefaults()
        {
            var type = typeof(EffectModule).Assembly.GetType("URUI.Effect.EffectPreset");
            Assert.That(type, Is.Not.Null, "预设必须提供不持有 DOM 的参数快照");
            var preset = ScriptableObject.CreateInstance(type);
            try
            {
                var snapshot = type.GetMethod("CreateParameters");
                var first = snapshot.Invoke(preset, null);
                var second = snapshot.Invoke(preset, null);
                var gradientField = first.GetType().GetField("GradationGradient");
                var firstGradient = (Gradient)gradientField.GetValue(first);
                var secondGradient = (Gradient)gradientField.GetValue(second);
                firstGradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) },
                    new[] { new GradientAlphaKey(.25f, 0), new GradientAlphaKey(.75f, 1) });
                firstGradient.mode = GradientMode.Fixed;
                Assert.That(secondGradient, Is.Not.SameAs(firstGradient));
                Assert.That(secondGradient.Evaluate(0), Is.EqualTo(Color.white));
                var third = snapshot.Invoke(preset, null);
                Assert.That(((Gradient)gradientField.GetValue(third)).Evaluate(0), Is.EqualTo(Color.white));
                Assert.That(first.GetType().GetField("SamplingScale").GetValue(first), Is.EqualTo(1f));
                Assert.That(first.GetType().GetField("AllowToModifyMeshShape").GetValue(first), Is.EqualTo(true));
                foreach (var field in first.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.That(typeof(URUI.Elements.Element).IsAssignableFrom(field.FieldType), Is.False, field.Name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }
    }
}
