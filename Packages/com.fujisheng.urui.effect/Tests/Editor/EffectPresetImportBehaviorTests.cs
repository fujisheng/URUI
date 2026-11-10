using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using URUI.Effect.Editor.Import;
namespace URUI.Effect.Editor.Tests
{
    public sealed partial class EffectPresetImportTests
    {
        const string Folder="Assets/URUI Effect Import Tests";
        static string TargetName(string name)=>name.Substring(2).Replace("TransitionTex","TransitionTexture").Replace("DetailTex","DetailTexture").Replace("TransitionRate","TransitionProgress").Replace("EdgeShinyRate","EdgeShinyProgress");
        [Test]
        public void ImportPreservesEveryFieldAndGradientKey()
        {
            var source=ScriptableObject.CreateInstance<ImportSourceFixture>();var texture=new Texture2D(2,2);
            try
            {
                foreach(var field in typeof(ImportSourceFixture).GetFields(BindingFlags.Public|BindingFlags.Instance))
                {
                    var type=field.FieldType;
                    if(type==typeof(float))field.SetValue(source,.37f);
                    else if(type==typeof(int))field.SetValue(source,3);
                    else if(type==typeof(bool))field.SetValue(source,true);
                    else if(type==typeof(Color))field.SetValue(source,new Color(2,-.1f,.4f,.7f));
                    else if(type==typeof(Vector2))field.SetValue(source,new Vector2(.3f,-.2f));
                    else if(type.IsEnum){var values=Enum.GetValues(type);field.SetValue(source,values.GetValue(values.Length-1));}
                    else if(type==typeof(ImportRange))field.SetValue(source,new ImportRange(.2f,.8f));
                    else if(type==typeof(Texture))field.SetValue(source,texture);
                }
                source.m_GradationGradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,.3f),new GradientColorKey(Color.green,1)},new[]{new GradientAlphaKey(.2f,0),new GradientAlphaKey(.7f,.6f),new GradientAlphaKey(1,1)});
                source.m_GradationGradient.mode=GradientMode.Fixed;source.m_GradationGradient.colorSpace=ColorSpace.Linear;
                var imported=UIEffectPresetImporter.Read(source);var actual=imported.CreateParameters();
                var count=0;
                foreach(var field in typeof(ImportSourceFixture).GetFields(BindingFlags.Public|BindingFlags.Instance))
                {
                    var target=typeof(EffectParameters).GetField(TargetName(field.Name));Assert.That(target,Is.Not.Null,field.Name);var expected=field.GetValue(source);var value=target.GetValue(actual);
                    if(expected is Gradient gradient)
                    {var got=(Gradient)value;Assert.That(got,Is.Not.SameAs(gradient));Assert.That(got.mode,Is.EqualTo(gradient.mode));Assert.That(got.colorSpace,Is.EqualTo(gradient.colorSpace));Assert.That(got.colorKeys,Is.EqualTo(gradient.colorKeys));Assert.That(got.alphaKeys,Is.EqualTo(gradient.alphaKeys));}
                    else if(expected is ImportRange range){var got=(EffectRange)value;Assert.That(got.Min,Is.EqualTo(range.m_Min));Assert.That(got.Max,Is.EqualTo(range.m_Max));}
                    else Assert.That(value,Is.EqualTo(expected),field.Name);
                    count++;
                }
                Assert.That(count,Is.EqualTo(71));actual.ToneIntensity=.9f;actual.GradationGradient.mode=GradientMode.Blend;
                Assert.That(imported.CreateParameters().ToneIntensity,Is.EqualTo(.37f));Assert.That(source.m_GradationGradient.mode,Is.EqualTo(GradientMode.Fixed));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test]
        public void ImportAppendAndResourceReferences()
        {
            var source=ScriptableObject.CreateInstance<ImportSourceFixture>();var original=ScriptableObject.CreateInstance<EffectPreset>();var texture=new Texture2D(2,2);EffectPreset appended=null;
            try
            {
                original.SetParameters(new EffectParameters{ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f,Flip=Flip.Horizontal});
                source.m_ColorFilter=ColorFilter.Multiply;source.m_TransitionTex=texture;source.m_Flip=Flip.Vertical;
                appended=UIEffectPresetImporter.CreatePreset(UIEffectPresetImporter.Read(source),original);var p=appended.CreateParameters();
                Assert.That(p.ToneFilter,Is.EqualTo(ToneFilter.Sepia));Assert.That(p.ToneIntensity,Is.EqualTo(.4f));Assert.That(p.ColorFilter,Is.EqualTo(ColorFilter.Multiply));Assert.That(p.Flip,Is.EqualTo(Flip.Horizontal|Flip.Vertical));
                Assert.That(original.CreateParameters().ColorFilter,Is.EqualTo(ColorFilter.None));
                // 追加只复制启用的组；被关闭的 Transition 纹理仍保存在完整导入结果中。
                Assert.That(UIEffectPresetImporter.Read(source).CreateParameters().TransitionTexture,Is.SameAs(texture));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(texture);if(appended)UnityEngine.Object.DestroyImmediate(appended);}
        }
        [Test]
        public void UnknownFieldReportsFailure()
        {
            var source=ScriptableObject.CreateInstance<UnknownImportSource>();
            try{Assert.That(Assert.Throws<InvalidOperationException>(()=>UIEffectPresetImporter.Read(source)).Message,Does.Contain("m_FutureMode"));}
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
        [Test]
        public void ComponentRootMustHaveAnExplicitDestination()
        {
            var source=ScriptableObject.CreateInstance<ComponentImportSource>();var root=new GameObject("Root",typeof(RectTransform));source.m_CustomRoot=root.GetComponent<RectTransform>();source.m_SamplingScale=2.5f;source.m_AllowToModifyMeshShape=false;
            try
            {
                Assert.Throws<InvalidOperationException>(()=>UIEffectPresetImporter.Read(source));
                var result=UIEffectPresetImporter.Read(source,new EffectImportContext{ResolveRoot=value=>ReferenceEquals(value,source.m_CustomRoot)?"effect-root":null});
                Assert.That(result.RootElementId,Is.EqualTo("effect-root"));Assert.That(result.CreateParameters().SamplingScale,Is.EqualTo(2.5f));Assert.That(result.CreateParameters().AllowToModifyMeshShape,Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test]
        public void ImportedPresetLoadsWithoutCoffeeAndDoesNotOverwriteAssets()
        {
            Assert.That(Array.Exists(AppDomain.CurrentDomain.GetAssemblies(),assembly=>assembly.GetName().Name=="Coffee.UIEffect"),Is.False,"独立项目必须真正不安装 Coffee");
            var source=ScriptableObject.CreateInstance<ImportSourceFixture>();var texture=new Texture2D(2,2);
            try
            {
                Directory.CreateDirectory(Folder);AssetDatabase.Refresh();AssetDatabase.CreateAsset(texture,Folder+"/input.asset");source.m_TransitionTex=texture;source.m_ToneFilter=ToneFilter.Sepia;
                var first=UIEffectPresetImporter.Save(UIEffectPresetImporter.Read(source),Folder+"/Imported.asset");var firstPath=AssetDatabase.GetAssetPath(first);
                var second=UIEffectPresetImporter.Save(UIEffectPresetImporter.Read(source),Folder+"/Imported.asset");Assert.That(AssetDatabase.GetAssetPath(second),Is.Not.EqualTo(firstPath));
                var loaded=AssetDatabase.LoadAssetAtPath<EffectPreset>(firstPath);Assert.That(loaded.CreateParameters().ToneFilter,Is.EqualTo(ToneFilter.Sepia));Assert.That(AssetDatabase.GetAssetPath(loaded.CreateParameters().TransitionTexture),Is.EqualTo(Folder+"/input.asset"));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);AssetDatabase.DeleteAsset(Folder);}
        }
        [Test]
        public void CssExportUsesInvariantNumbersAndRejectsUnmappedHdr()
        {
            var source=ScriptableObject.CreateInstance<ImportSourceFixture>();var texture=new Texture2D(2,2);var oldCulture=System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                source.m_ColorFilter=ColorFilter.HsvModifier;source.m_Color=new Color(-.2f,.3f,.4f,.5f);source.m_TransitionTex=texture;
                var p=UIEffectPresetImporter.Read(source).CreateParameters();
                Assert.Throws<InvalidOperationException>(()=>EffectPresetCssWriter.Write(p,".effect"));
                System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("fr-FR");
                var css=EffectPresetCssWriter.Write(p,".effect",new EffectImportContext{TextureUrl=_=>"/textures/pattern.png"});
                Assert.That(css,Does.Contain("--urui-fx-color-hue-shift: -0.2;").And.Contain("--urui-fx-color-alpha: 0.5;").And.Contain("url(\"/textures/pattern.png\")"));
                p.ColorFilter=ColorFilter.Multiply;p.Color=new Color(2,1,1,1);Assert.Throws<NotSupportedException>(()=>EffectPresetCssWriter.Write(p,".effect"));
            }
            finally{System.Globalization.CultureInfo.CurrentCulture=oldCulture;UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test]
        public void SaveKeepsRootBindingsAndPreservesExistingSidecars()
        {
            var source=ScriptableObject.CreateInstance<ComponentImportSource>();var root=new GameObject("Root",typeof(RectTransform));source.m_CustomRoot=root.GetComponent<RectTransform>();
            try
            {
                Directory.CreateDirectory(Folder);AssetDatabase.Refresh();var protectedPath=Folder+"/Root.bindings.json";File.WriteAllText(protectedPath,"protected");AssetDatabase.ImportAsset(protectedPath);
                var data=UIEffectPresetImporter.Read(source,new EffectImportContext{ResolveRoot=_=>"effect-root"});
                var preset=UIEffectPresetImporter.Save(data,Folder+"/Root.asset");var path=AssetDatabase.GetAssetPath(preset);
                Assert.That(File.ReadAllText(protectedPath),Is.EqualTo("protected"));var binding=File.ReadAllText(Path.ChangeExtension(path,"bindings.json"));
                Assert.That(binding,Does.Contain("data-urui-effect-root").And.Contain("effect-root"));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(root);AssetDatabase.DeleteAsset(Folder);}
        }
    }
}
