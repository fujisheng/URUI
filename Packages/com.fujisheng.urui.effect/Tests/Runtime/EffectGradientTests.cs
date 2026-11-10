using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectGradientTests
    {
        internal static Gradient ReferenceGradient()
        {
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.green,.4f),new GradientColorKey(Color.blue,1)},
                new[]{new GradientAlphaKey(.3f,0),new GradientAlphaKey(1,1)});return gradient;
        }
        [UnityTest]
        public IEnumerator AllGradationsAndFixedKeys()
        {
            var type=typeof(EffectModule).Assembly.GetType("URUI.Effect.Rendering.EffectGradientCache");
            Assert.That(type,Is.Not.Null,"任意色键和 Fixed 模式需要按完整内容共享 LUT");
            var acquire=type.GetMethod("Acquire",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
            Assert.That(acquire,Is.Not.Null);
            var gradient=ReferenceGradient();
            Texture2D Texture(object lease) => (Texture2D)lease.GetType().GetProperty("Texture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(lease);
            using(var a=(IDisposable)acquire.Invoke(null,new object[]{gradient}))
            using(var b=(IDisposable)acquire.Invoke(null,new object[]{ReferenceGradient()}))
            {
                Assert.That(Texture(b),Is.SameAs(Texture(a)));Assert.That(Texture(a).width,Is.EqualTo(256));
                foreach(var mode in (GradientMode[])Enum.GetValues(typeof(GradientMode)))
                {
                    gradient.mode=mode;
                    using var lease=(IDisposable)acquire.Invoke(null,new object[]{gradient});
                    var texture=Texture(lease);
                    Assert.That(texture.filterMode,Is.EqualTo(mode==GradientMode.Blend ? FilterMode.Bilinear : FilterMode.Point));
                    var pixels=texture.GetPixels32();
                    for(var i=0;i<256;i++)Assert.That(pixels[i],Is.EqualTo((Color32)gradient.Evaluate(i/255f)),mode+"/"+i);
                    if(mode!=GradientMode.Blend)Assert.That(texture,Is.Not.SameAs(Texture(a)));
                }
                gradient.mode=GradientMode.Blend;gradient.colorSpace=gradient.colorSpace==ColorSpace.Linear ? ColorSpace.Gamma : ColorSpace.Linear;
                using var changed=(IDisposable)acquire.Invoke(null,new object[]{gradient});
                Assert.That(Texture(changed),Is.Not.SameAs(Texture(a)),"色彩契约必须进入共享键");
            }
            using var fixture=new EffectRenderFixture();
            var report=new EffectTransitionTests.CaptureSet {colorSpace=QualitySettings.activeColorSpace.ToString()};
            const string colors="--urui-fx-gradation-intensity:.5;--urui-fx-gradation-color1:#e61a33;--urui-fx-gradation-color2:#1acc4db3;--urui-fx-gradation-color3:#334de6;--urui-fx-gradation-color4:#e6b33366;--urui-fx-gradation-gradient:url('gradient-lut.png');";
            foreach(var mode in (GradationMode[])Enum.GetValues(typeof(GradationMode)))
            {
                var id="GradationMode."+mode+".050";
                fixture.Load(colors+"--urui-fx-gradation-mode:"+EffectRenderFixture.Kebab(mode.ToString())+";");fixture.Compare(id);report.cases.Add(id);
            }
            foreach(var mode in (GradationColorFilter[])Enum.GetValues(typeof(GradationColorFilter)))
            {
                var id="GradationColorFilter."+mode+".050";
                fixture.Load(colors+"--urui-fx-gradation-mode:horizontal-gradient;--urui-fx-gradation-color-filter:"+EffectRenderFixture.Kebab(mode.ToString())+";");
                fixture.Compare(id);report.cases.Add(id);
            }
            File.WriteAllText(Path.Combine(fixture.Output,"gradation-captures.json"),JsonUtility.ToJson(report,true));yield return null;
        }
    }
}
