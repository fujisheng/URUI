using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class RuntimeRetirementTests
    {
        [UnityTest]
        public IEnumerator ReleaseCallbackCanCollectAgainWithoutReleasingTheBatchTwice()
        {
            var releases = 0;
            GpuRetirement.Retire(() =>
            {
                ++releases;
                if (releases == 1)
                {
                    GpuRetirement.Collect();
                }
            });
            for (var i = 0; i < 60 && releases == 0; ++i)
            {
                yield return null;
                GpuRetirement.Collect();
            }
            Assert.That(releases, Is.EqualTo(1));
            GpuRetirement.Collect();
            Assert.That(releases, Is.EqualTo(1));
        }
    }
}
