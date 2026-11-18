using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace URUI.Tests
{
    public sealed class RenderParameterContractTests
    {
        [TestCase(16777216f)]
        [TestCase(-16777216f)]
        public void OrdinaryOriginDecodingPreservesLocalAndProjectivePrecision(float origin)
        {
            var parameters=new Texture2D(256,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            var values=new NativeArray<Vector4>(256,Allocator.Temp);
            try
            {
                values[0]=new Vector4(1.25f,0,.25f,.125f);values[1]=new Vector4(0,3,-.5f,.25f);
                values[2]=new Vector4(origin,-65536,1,0);values[3]=new Vector4(.125f,-.25f,.5f,1);values[7]=new Vector4(512,0,0,0);
                parameters.SetPixelData(values,0);parameters.Apply(false);material.SetTexture("_UruiParameters",parameters);
                material.SetVector("_Probe",new Vector4(origin+8,-65535.5f,0,1));material.SetFloat("_Mode",3);
                target.Create();Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                read.ReadPixels(new Rect(0,0,1,1),0,0);read.Apply(false);var actual=read.GetPixel(0,0);
                Assert.That(actual.r,Is.EqualTo(10.125f).Within(.001f));Assert.That(actual.g,Is.EqualTo(1.25f).Within(.001f));
                Assert.That(actual.b,Is.EqualTo(2.25f).Within(.001f));Assert.That(actual.a,Is.EqualTo(2.125f).Within(.001f));
            }
            finally
            {
                values.Dispose();
                RenderTexture.active=previous;Object.DestroyImmediate(parameters);Object.DestroyImmediate(material);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void FixedFieldsRepresentImageIdentityAndNumberOrKeywordWithoutAmbiguity()
        {
            var automatic=new URUI.Rendering.RenderKeywordMapping("auto",7);
            var number=new URUI.Rendering.RenderParameterField(new URUI.Styles.StylePropertyDefinition("--number",URUI.Styles.StyleValueType.Number),0,keywords:new[]{automatic});
            var image=new URUI.Rendering.RenderParameterField(new URUI.Styles.StylePropertyDefinition("--image",URUI.Styles.StyleValueType.Image),2,3);
            Assert.DoesNotThrow(()=>new URUI.Rendering.RenderParameterLayout(1,new[]{number,image}));
            var overlap=new URUI.Rendering.RenderParameterField(new URUI.Styles.StylePropertyDefinition("--overlap",URUI.Styles.StyleValueType.Number),1);
            Assert.Throws<System.ArgumentException>(()=>new URUI.Rendering.RenderParameterLayout(1,new[]{number,overlap}));
        }
        [TestCase(65535, false)]
        [TestCase(32768, false)]
        [TestCase(4095, false)]
        [TestCase(65535, true)]
        public void FloatParametersKeepIndicesCoordinatesAndHdrWithEightImageSamplers(int index, bool matrix)
        {
            Assert.That(SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat), Is.True);
            var parameters = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var color = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var mask = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            var values = new NativeArray<Vector4>(65536, Allocator.Temp);
            try
            {
                values[0] = new Vector4(2, 0, 0, 65536);
                values[1] = new Vector4(0, 3, 0, -4096);
                values[2] = new Vector4(0, 0, 1, .125f);
                values[index] = new Vector4(65536.125f, -4096.125f, .001953125f, 1);
                parameters.SetPixelData(values, 0);
                parameters.Apply(false);
                color.SetPixel(0, 0, new Color(1, .5f, .25f, 1));
                color.Apply(false);
                mask.SetPixel(0, 0, new Color(.5f, .5f, .5f, .5f));
                mask.Apply(false);
                material.SetTexture("_Parameters", parameters);
                material.SetTexture("_Mask", mask);
                for (var slot = 0; slot < 8; slot++) { material.SetTexture("_Color" + slot, color); }
                material.SetFloat("_ParameterIndex", index);
                material.SetFloat("_Mode", matrix ? 1 : 0);
                target.Create();
                Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                read.Apply(false);
                var actual = read.GetPixel(0, 0);
                var expected = matrix ? new Color(.5f, .25f, .125f, .5f) : new Color(1, -.125f, .015625f, .5f);
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(.001f));
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(.001f));
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(.001f));
                Assert.That(actual.a, Is.EqualTo(expected.a).Within(.001f));
            }
            finally
            {
                values.Dispose();
                RenderTexture.active = previous;
                Object.DestroyImmediate(parameters); Object.DestroyImmediate(material); Object.DestroyImmediate(color); Object.DestroyImmediate(mask);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(read);
            }
        }
    }
}
