using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Tests
{
    /// <summary>透明黑是source-over恒等元；不能因颜色往返而破坏低alpha、HDR或加法颜色。</summary>
    public sealed class TransparentBlendTests
    {
        [TestCase(RenderTextureFormat.ARGBHalf,0,true)]
        [TestCase(RenderTextureFormat.ARGBHalf,1,true)]
        [TestCase(RenderTextureFormat.ARGBFloat,0,true)]
        [TestCase(RenderTextureFormat.ARGBFloat,1,true)]
        [TestCase(RenderTextureFormat.ARGBHalf,0,false)]
        [TestCase(RenderTextureFormat.ARGBHalf,1,false)]
        [TestCase(RenderTextureFormat.ARGBFloat,0,false)]
        [TestCase(RenderTextureFormat.ARGBFloat,1,false)]
        public void SourceOverPreservesStoredPixels(RenderTextureFormat format,int screen,bool transparent)
        {
            var shader=UnityEngine.Resources.Load<Shader>("URUI.Tests/FetchOverIdentity");
            if(!shader||!shader.isSupported)Assert.Ignore("实际后端不支持framebuffer fetch。");
            const int width=257,height=131;
            var source=new Texture2D(13,5,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};var sourceColors=new Color[65];
            if(!transparent)for(var n=0;n<sourceColors.Length;n++)sourceColors[n]=n%4==0?new Color(.125f,.25f,.375f,.5f):n%4==1?new Color(2,.5f,.25f,1):n%4==2?new Color(.125f,.25f,.375f,0):new Color(1e-8f,2e-8f,3e-8f,1e-7f);
            source.SetPixels(sourceColors);source.Apply();
            var input=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var values=new Color[width*height];uint random=0x41975432u;
            float Next(){random^=random<<13;random^=random>>17;random^=random<<5;return(random&0xffffff)/16777215f;}
            for(var n=0;n<values.Length;n++)
            {
                var alpha=n%7==0?0:n%7==1?17f/16777216:n%7==2?.5f:n%7==3?1:n%7==4?1f/4096:n%7==5?32:Next();
                // alpha为0的非零RGB也必须保留，不能顺便清除已存在的加法光。
                values[n]=new Color((Next()-.125f)*8,(Next()-.125f)*4,Next()*16,alpha);
            }
            values[0]=new Color(13f/16777216,13f/16777216,13f/16777216,17f/16777216);
            input.SetPixels(values);input.Apply();
            var expected=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear);
            var actual=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear);
            var material=new Material(shader);material.SetFloat("_Screen",screen);
            var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up},uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,2,0,2,3}};
            var previous=RenderTexture.active;
            try
            {
                Assert.That(expected.Create()&&actual.Create(),Is.True);Graphics.Blit(input,expected);Graphics.CopyTexture(expected,actual);
                var initial=Read(expected,format);
                using(var commands=new CommandBuffer())
                {
                    material.SetTexture("_MainTex",source);commands.SetViewport(new Rect(0,0,width,height));
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,1,1,0,-1,1));
                    if(!transparent){commands.SetRenderTarget(expected);commands.DrawMesh(mesh,Matrix4x4.identity,material,0,1);}
                    commands.SetRenderTarget(actual);commands.DrawMesh(mesh,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(commands);
                }
                var before=Read(expected,format);var after=Read(actual,format);var changed=0;var nonzero=0;var processed=0;
                for(var n=0;n<before.Length;n++){if(before[n]!=after[n])changed++;if(before[n]!=0)nonzero++;if(before[n]!=initial[n])processed++;}
                Assert.That(nonzero,Is.GreaterThan(1000),"必须真实保存背景，空输出不能成为恒等证据。");
                if(!transparent)Assert.That(processed,Is.GreaterThan(1000),"非零源必须实际参与绘制，不能用不绘制通过。");
                Assert.That(changed,Is.Zero,transparent?"透明黑source-over必须逐字节保留背景，包括低alpha和HDR值。":"非零源必须保留原来的合成结果，包括非零RGB/零alpha的加法输入。");
            }
            finally
            {
                RenderTexture.active=previous;expected.Release();actual.Release();
                foreach(var resource in new UnityEngine.Object[]{source,input,expected,actual,material,mesh})UnityEngine.Object.DestroyImmediate(resource);
            }
        }
        static byte[] Read(RenderTexture target,RenderTextureFormat format)
        {
            RenderTexture.active=target;var image=new Texture2D(target.width,target.height,format==RenderTextureFormat.ARGBHalf?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat,false,true);
            try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image.GetRawTextureData();}
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
