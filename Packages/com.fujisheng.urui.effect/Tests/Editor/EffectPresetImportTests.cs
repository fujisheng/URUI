using NUnit.Framework;
namespace URUI.Effect.Editor.Tests
{
    public sealed partial class EffectPresetImportTests
    {
        [Test]
        public void PresetImporterHasAnEditorOnlyReadEntry()
        {
            var type = typeof(EffectPresetEditor).Assembly.GetType("URUI.Effect.Editor.Import.UIEffectPresetImporter");
            Assert.That(type, Is.Not.Null, "需要严格读取锁定字段集的通用导入器");
            Assert.That(type.GetMethod("Read"), Is.Not.Null);
        }
    }
}
