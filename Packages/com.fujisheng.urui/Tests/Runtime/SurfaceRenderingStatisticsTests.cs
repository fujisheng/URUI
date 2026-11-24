using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Tests
{
    public sealed class SurfaceRenderingStatisticsTests
    {
        [UnityTest]
        public IEnumerator GetRenderingStatisticsReflectsLoadedSurfaceDrawsAndBytes()
        {
            var cameraObject = new GameObject("StatsTestCamera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var root = new GameObject("StatsTestSurface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                surface.Viewport = new Vector2Int(512, 512);

                var initial = surface.GetRenderingStatistics();
                Assert.That(initial.Ready, Is.False);
                Assert.That(initial.LoadedDocuments, Is.Zero);
                Assert.That(initial.RecordedGeometryDraws, Is.Zero);

                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml(
                    "<!doctype html><html><head><style>body{margin:0;background:#f6f1e7;} .card{width:240px;height:120px;background:#ef5a36;border-radius:16px;}</style></head><body><div class='card'></div></body></html>",
                    "/stats-test.html");

                yield return null;
                yield return null;

                var active = surface.GetRenderingStatistics();
                Assert.That(active.Ready, Is.True);
                Assert.That(active.LoadedDocuments, Is.EqualTo(1));
                Assert.That(active.RecordedGeometryDraws, Is.GreaterThanOrEqualTo(1));
                Assert.That(active.NativePlanCommandCount, Is.GreaterThan(0u));
                Assert.That(active.NativePlanFrameBytes, Is.GreaterThan(0ul));
                Assert.That(active.GeometryUploadBytes, Is.GreaterThan(0L));

                document.Unload();
                yield return null;

                var unloaded = surface.GetRenderingStatistics();
                Assert.That(unloaded.LoadedDocuments, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
