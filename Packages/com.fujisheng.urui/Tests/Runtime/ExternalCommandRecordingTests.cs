using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Rendering;
using URUI.Rendering;

namespace URUI.Tests
{
    public sealed class ExternalCommandRecordingTests
    {
        static Texture2D ColorTexture(Color value)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            texture.SetPixel(0, 0, value); texture.Apply(); return texture;
        }
        static Color Read(RenderTexture target, int x, int y)
        {
            var previous = RenderTexture.active; var image = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(x, y, 1, 1), 0, 0); image.Apply(); return image.GetPixel(0, 0); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        [Test]
        public void ParentTargetIsResolvedOnlyWhenRecordingActualParentCommands()
        {
            using var recording=new ExternalCommandRecording();
            var calls=0;
            System.Func<RenderTargetIdentifier> resolve=()=>{calls++;return BuiltinRenderTextureType.CameraTarget;};
            recording.Begin(BuiltinRenderTextureType.CameraTarget,new Rect(0,0,16,16),Matrix4x4.identity,resolve);
            Assert.That(calls,Is.Zero,"空回调不应租用父目标。");
            recording.BeginSurface();Assert.That(calls,Is.Zero,"仅离屏写入不需要父目标。");
            recording.EndSurface();recording.BeginDraw();Assert.That(calls,Is.EqualTo(1));
            recording.BeginDraw();Assert.That(calls,Is.EqualTo(1));
            recording.BeginSurface();recording.EndSurface();recording.BeginDraw();Assert.That(calls,Is.EqualTo(2));
            recording.Begin(BuiltinRenderTextureType.CameraTarget,new Rect(0,0,16,16),Matrix4x4.identity);
            recording.BeginDraw();Assert.That(calls,Is.EqualTo(2),"新录制不能沿用旧解析器。");
        }

        [Test]
        public void PreparedCommandsFreezePropertiesAndRestoreAfterOffscreenWork()
        {
            using var recording = new ExternalCommandRecording();
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var intermediate = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var red = ColorTexture(Color.red); var green = ColorTexture(Color.green); var blue = ColorTexture(Color.blue);
            var mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var properties = new MaterialPropertyBlock(); properties.SetFloat("_Mode", 2); properties.SetFloat("_ParameterIndex", 0);
            var state = new CommandBuffer();
            try
            {
                target.Create(); intermediate.Create();
                recording.Begin(target, new Rect(0, 0, 16, 16), Matrix4x4.Ortho(0, 16, 16, 0, -1, 1));
                var pool = new PaintContextPool();
                var context = pool.Open(null, null, Matrix4x4.identity, Vector4.zero, false, recording: recording);
                properties.SetTexture("_Parameters", red);
                context.DrawMesh(mesh, material, Matrix4x4.Scale(new Vector3(8, 8, 1)), properties);
                properties.SetTexture("_Parameters", blue);
                context.RecordSurface(intermediate, mesh, material, Matrix4x4.Ortho(0, 1, 1, 0, -1, 1), properties, 0);
                properties.SetTexture("_Parameters", green);
                context.DrawMesh(mesh, material, Matrix4x4.TRS(new Vector3(8, 0), Quaternion.identity, new Vector3(8, 8, 1)), properties);
                context.Close();
                Assert.That(recording.Count, Is.EqualTo(3)); Assert.That(recording[2].RestoreParent, Is.True);
                state.SetRenderTarget(target); state.ClearRenderTarget(false, true, Color.black);
                state.SetViewport(new Rect(0, 0, 16, 16)); state.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 16, 16, 0, -1, 1));
                Graphics.ExecuteCommandBuffer(state);
                for (var i = 0; i < recording.Count; i++)
                {
                    var segment = recording[i];
                    Graphics.ExecuteCommandBuffer(segment.Buffer);
                }
                Assert.That(Read(target, 4, 12).r, Is.GreaterThan(.99f));
                Assert.That(Read(target, 4, 12).g, Is.LessThan(.01f));
                Assert.That(Read(target, 12, 12).g, Is.GreaterThan(.99f));
                Assert.That(Read(intermediate, 4, 4).b, Is.GreaterThan(.99f));
                recording.Begin(target, new Rect(0, 0, 16, 16), Matrix4x4.identity); Assert.That(recording.HasCommands, Is.False);
            }
            finally
            {
                state.Release(); target.Release(); intermediate.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(intermediate);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); Object.DestroyImmediate(red); Object.DestroyImmediate(green); Object.DestroyImmediate(blue);
            }
        }
    }
}
