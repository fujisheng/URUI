using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Documents;
namespace URUI.Tests
{
    public sealed class BezierMotionTests
    {
        [UnityTest]
        public IEnumerator ActualNodesUseTypedProgressAndRestoreWithoutSignals()
        {
            var camera = new GameObject("Path camera", typeof(Camera));
            camera.tag = "MainCamera";
            var host = new GameObject("Generic path behavior");
            try
            {
                var document = HtmlFixtureAssets.Create(host);
                document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/BezierMotion"), "URUI/Tests/BezierMotion.html");
                var source = document.Q("source");
                var target = document.Q("target");
                var path = document.Q("path");
                var rotation = document.Q("rotation");
                document.SynchronizeLayout();
                using var motion = new BezierMotion(path, rotation, source, target);
                motion.Begin();
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                }
                Assert.That(Vector2.Distance(path.WorldBound.center, source.WorldBound.center), Is.LessThan(.01f));
                path.Style.SetNumber("--urui-motion-progress", .5f);
                rotation.Style.SetNumber("--urui-motion-rotation-progress", .5f);
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                }
                Assert.That(Vector2.Distance(path.WorldBound.center, (source.WorldBound.center + target.WorldBound.center) * .5f), Is.LessThan(.01f));
                var origin = rotation.LocalToWorld(Vector2.zero);
                var dx = rotation.LocalToWorld(Vector2.right) - origin;
                Assert.That(Mathf.Atan2(dx.y, dx.x) * Mathf.Rad2Deg, Is.EqualTo(-90).Within(.01f));
                path.Style.SetNumber("--urui-motion-progress", 1);
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                }
                Assert.That(Vector2.Distance(path.WorldBound.center, target.WorldBound.center), Is.LessThan(.01f));
                motion.Stop();
                document.SynchronizeLayout();
                Assert.That(Vector2.Distance(path.WorldBound.center, target.WorldBound.center), Is.LessThan(.01f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(camera);
            }
        }
    }
}
