using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using UnityResources = UnityEngine.Resources;

namespace URUI.Internal.Hosting
{
    // 初始化与关闭原生 Surface、相机命令缓冲及自有资源；失败与禁用走同一套异常安全释放顺序。
    internal sealed partial class RenderHost
    {
        internal void InitializeForSurface()
        {
            Start();
        }

        // Surface initializes this component synchronously. Unity may call Start again,
        // so startupAttempted also prevents a second native surface from being created.
        void Start()
        {
            if (startupAttempted || resourcesReleased)
            {
                return;
            }

            startupAttempted = true;
            if (!shader)
            {
                shader = UnityResources.Load<Shader>("URUI/Premultiplied");
            }

            if (!shader)
            {
                shader = Shader.Find("URUI/Premultiplied");
            }

            if (!shader)
            {
                Debug.LogError("URUI: shader missing.");
                enabled = false;
                return;
            }

            targetCamera = OwnerSurface && OwnerSurface.PreviewCamera ? OwnerSurface.PreviewCamera : Camera.main;
            if (!targetCamera)
            {
                Debug.LogError("URUI: camera missing.");
                enabled = false;
                return;
            }

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            propertyContract = UnityResources.Load<URUI.Internal.Rendering.Shaders.ShaderPropertyContract>("URUI/BuiltinShaderPropertyContract");
            reusableProperties = new MaterialPropertyBlock();

            white = Texture2D.whiteTexture;
            buffer = new CommandBuffer
            {
                name = "URUI UI"
            };
            lastWidth = UIWidth;
            lastHeight = UIHeight;
            try
            {
                if (NativeMethods.urui_bridge_abi_version() != NativeMethods.ExpectedBridgeAbiVersion)
                {
                    throw new Exception($"URUIBridge ABI version {NativeMethods.ExpectedBridgeAbiVersion} is required");
                }
                if (NativeMethods.urui_effect_api_version() != NativeMethods.ExpectedEffectApiVersion)
                {
                    throw new NotSupportedException($"URUI 效果协议 v{NativeMethods.ExpectedEffectApiVersion} 必需，请重建当前目标插件。");
                }
                if (NativeMethods.urui_filter_api_version() != NativeMethods.ExpectedFilterApiVersion)
                {
                    throw new NotSupportedException($"URUI 滤镜协议 v{NativeMethods.ExpectedFilterApiVersion} 必需，请重建当前目标插件。");
                }
                if (NativeMethods.urui_texture_protocol_version() != NativeMethods.ExpectedTextureProtocolVersion)
                {
                    throw new Exception($"URUIBridge texture protocol v{NativeMethods.ExpectedTextureProtocolVersion} is required; update all platform plugins");
                }
                if (NativeMethods.urui_command_protocol_version() != NativeMethods.ExpectedCommandProtocolVersion)
                {
                    throw new Exception($"URUIBridge command protocol v{NativeMethods.ExpectedCommandProtocolVersion} is required; update all platform plugins");
                }

                if (NativeMethods.urui_surface_create(lastWidth, lastHeight, out var handle) != NativeStatus.Ok || handle == 0)
                {
                    throw new Exception(NativeMethods.LastError(0));
                }

                SurfaceHandle = handle;
                active = true;
                activeHosts[handle] = this;
                if (NativeMethods.urui_surface_set_direct_color_filters(SurfaceHandle, shader.name == "URUI/Premultiplied" ? 1 : 0) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("无法设置 URUI 标准图片滤镜能力。");
                }
                DefaultFonts.Install(SurfaceHandle);
                StartImageResolver();
                SynchronizeUIClock(true);
                if (NativeMethods.urui_set_event_callback(SurfaceHandle, URUI.Documents.Document.NativeEventCallback) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("URUI Surface 事件初始化失败。");
                }
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("Build Native/URUIBridge first: " + ex.Message);
                AbortStartup();
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError($"Update Native/URUIBridge to ABI{NativeMethods.ExpectedBridgeAbiVersion}: " + ex.Message);
                AbortStartup();
            }
            catch (Exception ex)
            {
                Debug.LogError("URUI: " + ex);
                AbortStartup();
            }
        }

        bool RestoreSceneCamera()
        {
            if (OwnerSurface && OwnerSurface.ManualDrive)
            {
                return targetCamera;
            }
            if (targetCamera && targetCamera.isActiveAndEnabled)
            {
                return true;
            }
            var replacement = Camera.main;
            if (!replacement || !replacement.isActiveAndEnabled)
            {
                return false;
            }
            // Surface 可比场景相机存活更久；重录沿用现有 GPU 退役保护。
            DetachRecordedBuffers();
            InvalidateRecording(true);
            submissionTarget = null;
            submissionViewport = default;
            targetCamera = replacement;
            return true;
        }

        static void SafeCleanup(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        void DestroyNativeSurface()
        {
            var handle = SurfaceHandle;
            active = false;
            SurfaceHandle = 0;
            if (handle != 0)
            {
                try
                {
                    SafeCleanup(() => NativeMethods.urui_surface_destroy(handle));
                }
                finally
                {
                    if (activeHosts.TryGetValue(handle, out var registered) && ReferenceEquals(registered, this))
                    {
                        activeHosts.Remove(handle);
                    }
                }
            }
        }

        void AbortStartup()
        {
            try
            {
                SafeCleanup(() => ReleaseLayerRendering(tearingDown: true));
                SafeCleanup(ReleaseNativePlanRendering);
                if (buffer != null)
                {
                    var current = buffer;
                    buffer = null;
                    SafeCleanup(current.Release);
                }
                if (material)
                {
                    var current = material;
                    material = null;
                    SafeCleanup(() => DestroyRenderObject(current));
                }
            }
            finally
            {
                DestroyNativeSurface();
                SafeCleanup(borrowedTextures.Clear);
                SafeCleanup(RetireLegacyImages);
                if (this)
                {
                    enabled = false;
                }
            }
        }

        void OnDisable() => DisableHost(deferredDisable: false);

        void DisableHost(bool deferredDisable)
        {
            UnsubscribeEffectSubmissions();
            if (resourcesReleased || OwnerSurface?.Defer(() => DisableHost(deferredDisable: true)) == true)
            {
                return;
            }
            if (deferredDisable && this && isActiveAndEnabled)
            {
                return;
            }
            InvalidateRecording(true);
        }

        void OnEnable()
        {
            SubscribeEffectSubmissions();
            InvalidateRecording(false);
        }

        void OnDestroy()
        {
            ReleaseForSurface();
        }

        // Detach submitted commands before destroying host-owned GPU resources. The
        // document's TextAsset and source textures belong to its caller, not this host.
        internal void ReleaseForSurface()
        {
            if (resourcesReleased)
            {
                return;
            }

            // DestroyImmediate 可先销毁 Unity 对象；闭包仍须保留并在原生栈退出后清理字段。
            if (OwnerSurface?.Defer(ReleaseForSurface) == true)
            {
                return;
            }
            resourcesReleased = true;
            try
            {
                SafeCleanup(ReleaseSubmissionPool);
                if (this)
                {
                    enabled = false;
                }
                SafeCleanup(() => InvalidateRecording(true));

                frameUsedGeometry.Clear();
                frameStaleGeometry.Clear();
                SafeCleanup(ReleaseNativePlanRendering);
                if (buffer != null)
                {
                    var current = buffer;
                    buffer = null;
                    SafeCleanup(current.Release);
                }

                foreach (var item in meshes.Values)
                {
                    if (item.mesh)
                    {
                        var mesh = item.mesh;
                        SafeCleanup(() => DestroyRenderObject(mesh));
                    }
                }
                meshes.Clear();
                idleGeometry.Clear();
                pooledGeometryBytes = 0;

                foreach (var item in textures.Values)
                {
                    if (item.Texture)
                    {
                        var texture = item.Texture;
                        SafeCleanup(() => DestroyRenderObject(texture));
                    }
                }
                textures.Clear();

                SafeCleanup(() => ReleaseLayerRendering(tearingDown: true));
                if (material)
                {
                    var current = material;
                    material = null;
                    SafeCleanup(() => DestroyRenderObject(current));
                }
            }
            finally
            {
                DestroyNativeSurface();
                SafeCleanup(borrowedTextures.Clear);
                SafeCleanup(RetireLegacyImages);
            }
        }
    }
}
