using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using URUI.Animation;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Editor.Inspection
{
    /// <summary>
    /// 拥有自有 Preview Scene；不能与游戏 Surface 同时占用原生上下文。
    /// </summary>
    public sealed class EditorPreviewHost : IDisposable
    {
        sealed class PreviewClock : IUIClock
        {
            public double Now
            {
                get;
                set;
            }
            public float Delta
            {
                get;
                set;
            }
        }
        readonly PreviewClock clock = new PreviewClock();
        Scene scene;
        GameObject root;
        Camera camera;
        public Document Document
        {
            get;
            private set;
        }
        public Surface Surface
        {
            get;
            private set;
        }
        public RenderTexture Target
        {
            get;
            private set;
        }
        public long FrameId
        {
            get;
            private set;
        }
        public double Time => clock.Now;
        internal EditorPreviewHost(int width, int height)
        {
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                root = new GameObject("URUI Authoring Preview") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .08f, .09f, 1);
                camera.cullingMask = 0;
                camera.orthographic = true;
                Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "URUI Authoring"
                };
                Target.Create();
                camera.targetTexture = Target;
                Surface = root.AddComponent<Surface>();
                Surface.InitializeForEditor(camera);
                Surface.Viewport = new Vector2Int(width, height);
                Surface.UIClock = clock;
                Document = root.AddComponent<Document>();
                Document.InitializeForEditor();
                Document.Surface = Surface;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void Advance(float delta)
        {
            if (!float.IsFinite(delta) || delta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }
            clock.Delta = delta;
            clock.Now += delta;
        }
        public void Render()
        {
            if (!Document || !Document.IsLoaded)
            {
                throw new InvalidOperationException("Preview document is not loaded.");
            }
            RuntimeInspectionBridge.Refresh(Document);
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                // SRP 不执行相机的旧式 CommandBuffer；自有离屏预览通过同一 fence 路径直接提交。
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = Target;
                    GL.Clear(true, true, camera.backgroundColor);
                    if (!Document.Host.SubmitPreview(Target))
                    {
                        throw new InvalidOperationException("Preview rendering is not ready.");
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            else
            {
                camera.Render();
            }
            clock.Delta = 0;
            FrameId++;
        }
        public CapturedPixels Capture()
        {
            Render();
            var previous = RenderTexture.active;
            var texture = new Texture2D(Target.width, Target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                RenderTexture.active = Target;
                texture.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0);
                texture.Apply();
                return new CapturedPixels(texture, FrameId);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
        public void Dispose()
        {
            var hadContent = FrameId != 0 || (Document && Document.IsLoaded);
            try
            {
                if (Document && Document.IsLoaded)
                {
                    Document.Unload();
                }
            }
            finally
            {
                if (Surface)
                {
                    Surface.ReleaseForEditor();
                }
            }
            if (hadContent && Target && !URUI.Internal.Resources.ImagePinRetirement.TryCompleteImmediateRetirements(ConfirmSubmittedWork))
            {
                throw new InvalidOperationException("预览包含无法确认的 GPU 提交，保留资源以便重试释放。");
            }
            if (root)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            root = null;
            Document = null;
            Surface = null;
            if (Target)
            {
                Target.Release();
                UnityEngine.Object.DestroyImmediate(Target);
                Target = null;
            }
            if (scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            scene = default;
        }
        void ConfirmSubmittedWork()
        {
            // CPU fence 的可见状态可能要到下一次 Editor update 才更新；域重载前
            // 使用独立的一像素读回确认此前即时队列完成，不增加整图副本。
            if (!Target || !Target.IsCreated())
            {
                throw new InvalidOperationException("预览目标在 GPU 退休确认前已释放。");
            }
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var request = AsyncGPUReadback.Request(Target, 0, 0, 1, 0, 1, 0, 1);
                request.WaitForCompletion();
                if (request.hasError)
                {
                    throw new InvalidOperationException("预览 GPU 退休确认失败。");
                }
            }
            else
            {
                var previous = RenderTexture.active;
                var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = Target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    pixel.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(pixel);
                }
            }
        }
    }
    public sealed class CapturedPixels : IDisposable
    {
        public Texture2D Texture
        {
            get;
        }
        public long FrameId
        {
            get;
        }
        internal CapturedPixels(Texture2D texture, long frameId)
        {
            Texture = texture;
            FrameId = frameId;
        }
        public void Dispose()
        {
            if (Texture)
            {
                UnityEngine.Object.DestroyImmediate(Texture);
            }
        }
    }
}
