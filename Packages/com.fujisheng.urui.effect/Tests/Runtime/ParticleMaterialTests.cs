using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class ParticleMaterialTests
    {
        [UnityTest]
        public IEnumerator SeparateAlphaBlendingKeepsAnOpaqueBackdropOpaque()
        {
            var material=new Material(Shader.Find("URUI.Effect/Particle"));var mesh=new Mesh();var commands=new CommandBuffer();
            var target=new RenderTexture(8,8,0,RenderTextureFormat.ARGB32);target.Create();var pixel=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            try
            {
                mesh.vertices=new[]{new Vector3(-1,-1),new Vector3(1,-1),new Vector3(1,1),new Vector3(-1,1)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};
                var tint=new Color(1,0,0,.5f);mesh.colors=new[]{tint,tint,tint,tint};material.mainTexture=Texture2D.whiteTexture;
                material.SetFloat("_SrcBlend",5);material.SetFloat("_DstBlend",10);material.SetFloat("_SrcBlendAlpha",1);material.SetFloat("_DstBlendAlpha",10);
                commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,8,8));commands.ClearRenderTarget(false,true,Color.blue);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(-1,1,-1,1,-1,1));commands.DrawMesh(mesh,Matrix4x4.identity,material);Graphics.ExecuteCommandBuffer(commands);
                var previous=RenderTexture.active;
                try{RenderTexture.active=target;pixel.ReadPixels(new Rect(4,4,1,1),0,0);pixel.Apply();}finally{RenderTexture.active=previous;}
                var actual=pixel.GetPixel(0,0);Assert.That(actual.a,Is.EqualTo(1).Within(.01f));
                Assert.That(actual.r,Is.GreaterThan(.45f));Assert.That(actual.b,Is.GreaterThan(.45f));
            }
            finally{commands.Dispose();target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixel);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
            yield return null;
        }

        [UnityTest]
        public IEnumerator MaterialCanCullBackFacesWithoutChangingTheDefault()
        {
            var material=new Material(Shader.Find("URUI.Effect/Particle"));
            var mesh=new Mesh();var commands=new CommandBuffer();
            var target=new RenderTexture(8,8,0,RenderTextureFormat.ARGB32);target.Create();
            var pixel=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            try
            {
                mesh.vertices=new[]{new Vector3(-1,-1),new Vector3(1,-1),new Vector3(1,1),new Vector3(-1,1)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.colors=new[]{Color.red,Color.red,Color.red,Color.red};
                material.mainTexture=Texture2D.whiteTexture;material.SetFloat("_UruiPaintOpacity",1);material.SetFloat("_UruiClipEnabled",0);
                foreach(var mode in new[]{0,2})
                {
                    material.SetFloat("_CullMode",mode);var visible=0;
                    foreach(var reverse in new[]{false,true})
                    {
                        mesh.triangles=reverse?new[]{0,2,1,0,3,2}:new[]{0,1,2,0,2,3};
                        commands.Clear();commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,8,8));commands.ClearRenderTarget(false,true,Color.black);
                        commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(-1,1,-1,1,-1,1));
                        commands.DrawMesh(mesh,Matrix4x4.identity,material);Graphics.ExecuteCommandBuffer(commands);
                        var previous=RenderTexture.active;
                        try{RenderTexture.active=target;pixel.ReadPixels(new Rect(4,4,1,1),0,0);pixel.Apply();}
                        finally{RenderTexture.active=previous;}
                        if(pixel.GetPixel(0,0).r>.9f){visible++;}
                    }
                    Assert.That(visible,Is.EqualTo(mode==0?2:1),"Cull mode="+mode);
                }
            }
            finally{commands.Dispose();target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixel);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
            yield return null;
        }
    }
}
