using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Hosting;

namespace URUI.Internal.Rendering.Retention
{
    /// <summary>
    /// 记录复用依赖的 Unity 状态快照。逐项比较实际状态，避免哈希碰撞造成错误复用。
    /// 不认识的渲染路径或无法证明独占所有权的资源不参与复用。
    /// </summary>
    internal partial struct RecordingKey : IEquatable<RecordingKey>
    {
        public int width;
        public int height;
        public int screenWidth;
        public int screenHeight;
        public int orientation;
        public int fullscreen;
        public int colorSpace;
        public int camera;
        public int material;
        public int shader;
        public int contract;
        public int options;
        public int renderQueue;
        public int display;
        public int renderingPath;
        public float dpi;
        public float widthScale;
        public float heightScale;
        public Rect viewport;
        public Matrix4x4 view;
        public Matrix4x4 projection;
        public bool hdr;
        public bool msaa;
        public bool instancing;
        public bool doubleSided;
        public bool passEnabled;
        public MaterialGlobalIlluminationFlags illumination;

        static bool MatrixEqual(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        public bool Equals(RecordingKey x) => width == x.width
            && height == x.height
            && screenWidth == x.screenWidth
            && screenHeight == x.screenHeight
            && orientation == x.orientation
            && fullscreen == x.fullscreen
            && colorSpace == x.colorSpace
            && camera == x.camera
            && material == x.material
            && shader == x.shader
            && contract == x.contract
            && options == x.options
            && renderQueue == x.renderQueue
            && display == x.display
            && renderingPath == x.renderingPath
            && dpi == x.dpi
            && widthScale == x.widthScale
            && heightScale == x.heightScale
            && viewport.Equals(x.viewport)
            && MatrixEqual(view, x.view)
            && MatrixEqual(projection, x.projection)
            && hdr == x.hdr
            && msaa == x.msaa
            && instancing == x.instancing
            && doubleSided == x.doubleSided
            && passEnabled == x.passEnabled
            && illumination == x.illumination;

        static bool IsMaterialVariant(Material material)
        {
#if UNITY_EDITOR
            return material.isVariant;
#else
            // Host creates a fresh Material(shader); no material variants are loaded.
            return false;
#endif
        }

        public static bool TryCapture(RenderHost host, Material material, Camera camera, string passName, out RecordingKey key)
        {
#if URUI_DEBUG
            using var scope = CaptureMarker.Auto();
#endif
            return Capture(host, material, camera, passName, out key);
        }

        static bool Capture(RenderHost host, Material material, Camera camera, string passName, out RecordingKey key)
        {
            key = default;
            if (host.recordingOwnership != RecordingOwnership.HostExclusive
                || !material
                || !camera
                || !host.propertyContract
                || !host.propertyContract.Supports(material)
                || material.passCount != 1
                || IsMaterialVariant(material)
                || GraphicsSettings.currentRenderPipeline
                || camera.targetTexture
                || camera.stereoEnabled
                || camera.forceIntoRenderTexture)
            {
                return false;
            }

            var v = camera.worldToCameraMatrix;
            var p = camera.projectionMatrix;
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i]) || float.IsNaN(p[i]) || float.IsInfinity(p[i]))
                {
                    return false;
                }
            }

            int options = (host.batchDisjointCommands ? 1 : 0)
                | (host.batchEquivalentTransforms ? 2 : 0)
                | (host.batchOrderedOverlap ? 4 : 0)
                | (host.reusePropertyBlock ? 8 : 0)
                | (host.cullFullyClipped ? 16 : 0)
                | (host.reuseEquivalentPropertyState ? 64 : 0)
                | (host.useShaderPropertyIds ? 128 : 0)
                | (host.useFrameScratchCollections ? 256 : 0);
#if URUI_DEBUG
            options |= host.skipDrawForDiagnosis ? 32 : 0;
#endif
            key = new RecordingKey
            {
                width = host.UiWidth,
                height = host.UiHeight,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                orientation = (int)Screen.orientation,
                fullscreen = (int)Screen.fullScreenMode,
                colorSpace = (int)QualitySettings.activeColorSpace,
                camera = camera.GetInstanceID(),
                material = material.GetInstanceID(),
                shader = material.shader.GetInstanceID(),
                contract = host.propertyContract.GetInstanceID(),
                options = options,
                renderQueue = material.renderQueue,
                display = camera.targetDisplay,
                renderingPath = (int)camera.actualRenderingPath,
                dpi = Screen.dpi,
                widthScale = ScalableBufferManager.widthScaleFactor,
                heightScale = ScalableBufferManager.heightScaleFactor,
                viewport = camera.pixelRect,
                view = v,
                projection = p,
                hdr = camera.allowHDR,
                msaa = camera.allowMSAA,
                instancing = material.enableInstancing,
                doubleSided = material.doubleSidedGI,
                passEnabled = material.GetShaderPassEnabled(passName),
                illumination = material.globalIlluminationFlags
            };
            return true;
        }
    }
}
