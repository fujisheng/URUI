using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class ParticleMaterialTests
    {
        [Test]
        public void ParticleDescriptorUsesTheActualMaterialBlendAndAlphaContract()
        {
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle"));
            try
            {
                material.SetFloat("_SrcBlend", (int)BlendMode.One);
                material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_PremultiplyRGB", 1);
                var method = typeof(ParticlePainter).GetMethod("CreateProgram", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var program = (URUI.Rendering.ExternalGeometryProgram)method.Invoke(null, new object[] { material });
                Assert.That(program.Descriptor.SourceBlend, Is.EqualTo(BlendMode.One));
                Assert.That(program.Descriptor.DestinationBlend, Is.EqualTo(BlendMode.OneMinusSrcAlpha));
                Assert.That(program.Descriptor.Alpha, Is.EqualTo(URUI.Rendering.RenderAlpha.Premultiplied));
                material.SetFloat("_SrcBlendAlpha", 1);
                material.SetFloat("_DstBlendAlpha", 10);
                material.SetFloat("_CullMode", 2);
                material.SetFloat("_ColorMask", 7);
                material.SetFloat("_PremultiplyRGB", .5f);
                program = (URUI.Rendering.ExternalGeometryProgram)method.Invoke(null, new object[] { material });
                Assert.That(program.Descriptor.SourceAlphaBlend, Is.EqualTo(BlendMode.One));
                Assert.That(program.Descriptor.DestinationAlphaBlend, Is.EqualTo(BlendMode.OneMinusSrcAlpha));
                Assert.That(program.Descriptor.Cull, Is.EqualTo(CullMode.Back));
                Assert.That((int)program.Descriptor.ColorMask, Is.EqualTo(7));
                Assert.That(program.Descriptor.Alpha, Is.EqualTo(URUI.Rendering.RenderAlpha.PartialPremultiplied));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
        // 与正式绘制相同的八个 float4 参数，单位矩阵、白色乘色、禁用裁剪。
        static Texture2D Parameters(Material material)
        {
            var texture = new Texture2D(256, 1, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var values = new Color[256];
            values[0] = new Color(1, 0, 0, 0);
            values[1] = new Color(0, 1, 0, 0);
            values[2] = new Color(0, 0, 1, 0);
            values[3] = new Color(0, 0, 0, 1);
            values[5] = Color.white;
            texture.SetPixels(values);
            texture.Apply();
            material.SetTexture("_UruiParameters", texture);
            return texture;
        }
        [UnityTest]
        public IEnumerator SeparateAlphaBlendingKeepsAnOpaqueBackdropOpaque()
        {
            var material = new Material(Shader.Find("URUI.Effect/Particle"));
            var mesh = new Mesh();
            var commands = new CommandBuffer();
            var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            target.Create();
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var parameters = Parameters(material);
            try
            {
                mesh.vertices = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.uv2 = new Vector2[4];
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                var tint = new Color(1, 0, 0, .5f);
                mesh.colors = new[] { tint, tint, tint, tint };
                material.mainTexture = Texture2D.whiteTexture;
                material.SetFloat("_SrcBlend", 5);
                material.SetFloat("_DstBlend", 10);
                material.SetFloat("_SrcBlendAlpha", 1);
                material.SetFloat("_DstBlendAlpha", 10);
                commands.SetRenderTarget(target);
                commands.SetViewport(new Rect(0, 0, 8, 8));
                commands.ClearRenderTarget(false, true, Color.blue);
                commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1));
                commands.DrawMesh(mesh, Matrix4x4.identity, material);
                Graphics.ExecuteCommandBuffer(commands);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(4, 4, 1, 1), 0, 0);
                    pixel.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                var actual = pixel.GetPixel(0, 0);
                Assert.That(actual.a, Is.EqualTo(1).Within(.01f));
                Assert.That(actual.r, Is.GreaterThan(.45f));
                Assert.That(actual.b, Is.GreaterThan(.45f));
            }
            finally
            {
                commands.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixel);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(parameters);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MaterialCanCullBackFacesWithoutChangingTheDefault()
        {
            var material = new Material(Shader.Find("URUI.Effect/Particle"));
            var mesh = new Mesh();
            var commands = new CommandBuffer();
            var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            target.Create();
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var parameters = Parameters(material);
            try
            {
                mesh.vertices = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.uv2 = new Vector2[4];
                mesh.colors = new[] { Color.red, Color.red, Color.red, Color.red };
                material.mainTexture = Texture2D.whiteTexture;
                foreach (var mode in new[] { 0, 2 })
                {
                    material.SetFloat("_CullMode", mode);
                    var visible = 0;
                    foreach (var reverse in new[] { false, true })
                    {
                        mesh.triangles = reverse ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
                        commands.Clear();
                        commands.SetRenderTarget(target);
                        commands.SetViewport(new Rect(0, 0, 8, 8));
                        commands.ClearRenderTarget(false, true, Color.black);
                        commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1));
                        commands.DrawMesh(mesh, Matrix4x4.identity, material);
                        Graphics.ExecuteCommandBuffer(commands);
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = target;
                            pixel.ReadPixels(new Rect(4, 4, 1, 1), 0, 0);
                            pixel.Apply();
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                        }
                        if (pixel.GetPixel(0, 0).r > .9f)
                        {
                            visible++;
                        }
                    }
                    Assert.That(visible, Is.EqualTo(mode == 0 ? 2 : 1), "Cull mode=" + mode);
                }
            }
            finally
            {
                commands.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixel);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(parameters);
            }
            yield return null;
        }
    }
}
