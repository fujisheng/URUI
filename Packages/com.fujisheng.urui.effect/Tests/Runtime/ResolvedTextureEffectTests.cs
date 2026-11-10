using NUnit.Framework;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed partial class ResolvedTextureEffectTests
    {
        [Test]
        public void GenericMaterialEffectAcceptsAResourceKeyResolver()
        {Assert.That(typeof(TextureColorEffect).GetMethod("DefineResolved"),Is.Not.Null,"通用单图程序不应为每个项目材质注册一份定义");}
    }
}
