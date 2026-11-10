using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using UnityResources = UnityEngine.Resources;

namespace URUI.Internal.Hosting
{
    // 初始化与关闭原生 context、相机命令缓冲及自有资源；失败与禁用走同一套释放顺序。
    internal sealed partial class RenderHost
    {
        internal void InitializeForSurface()
        {
            Start();
        }

        // Document initializes this component synchronously. Unity may call Start again,
        // so startupAttempted also prevents a second native context from being created.
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

            targetCamera = Camera.main;
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
            targetCamera.AddCommandBuffer(CameraEvent.AfterEverything, buffer);
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            try
            {
                if (NativeMethods.urui_bridge_abi_version() != 8)
                {
                    throw new Exception("URUIBridge ABI version 8 is required");
                }
                if (NativeMethods.urui_effect_api_version() != 7)
                    throw new NotSupportedException("URUI 效果协议 v7 必需，请重建当前目标插件。");

                if (NativeMethods.urui_texture_protocol_version() != 1)
                    throw new Exception("URUIBridge texture protocol v1 is required; update all platform plugins");
                if (NativeMethods.urui_command_protocol_version() != 2)
                    throw new Exception("URUIBridge command protocol v2 is required; update all platform plugins");

                if (ActiveHost && ActiveHost != this) throw new InvalidOperationException("当前原生桥接仅支持一个活动 Surface。请先关闭另一个 Surface。");
                if (NativeMethods.urui_create(UiWidth, UiHeight) == 0)
                {
                    throw new Exception(NativeMethods.LastError());
                }

                active = true;
                ActiveHost = this;
                if (NativeMethods.urui_set_direct_color_filters(shader.name == "URUI/Premultiplied" ? 1 : 0) != 1)
                    throw new InvalidOperationException("无法设置 URUI 标准图片滤镜能力。");
                DefaultFonts.Install();
                StartImageResolver();
                SynchronizeUiClock(true);
                if (NativeMethods.urui_set_event_callback(URUI.Documents.Document.NativeEventCallback) != 1)
                    throw new InvalidOperationException("URUI Surface 事件初始化失败。");
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("Build Native/URUIBridge first: " + ex.Message);
                AbortStartup();
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError("Update Native/URUIBridge to ABI8 with texture protocol v1: " + ex.Message);
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
            if (targetCamera && targetCamera.isActiveAndEnabled) return true;
            var replacement = Camera.main;
            if (!replacement || !replacement.isActiveAndEnabled) return false;
            // Surface 可比场景相机存活更久；重录沿用现有 GPU 退役保护。
            if (targetCamera && buffer != null)
                targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, buffer);
            InvalidateRecording(true);
            submissionTarget = null;
            submissionViewport = default;
            targetCamera = replacement;
            if (buffer != null) targetCamera.AddCommandBuffer(CameraEvent.AfterEverything, buffer);
            return true;
        }

        void AbortStartup()
        {
            ReleaseLayerRendering();
            if (targetCamera && buffer != null)
            {
                targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, buffer);
            }

            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            if (material)
            {
                Destroy(material);
                material = null;
            }

            if (active)
            {
                NativeMethods.urui_destroy();
                active = false;
            }

            if (ReferenceEquals(ActiveHost, this))
            {
                ActiveHost = null;
            }

            borrowedTextures.Clear();
            RetireLegacyImages();
            enabled = false;
        }

        void OnDisable()
        {
            UnsubscribeEffectSubmissions();
            if (resourcesReleased || OwnerSurface?.Defer(OnDisable) == true) return;
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
            if (OwnerSurface?.Defer(ReleaseForSurface) == true) return;
            resourcesReleased = true;
            ReleaseSubmissionPool();
            if (this) enabled = false;
            InvalidateRecording(true);

            ClearFrameScratch();
            ClearBatches();
            if (targetCamera && buffer != null)
            {
                targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, buffer);
            }

            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            foreach (var item in meshes.Values)
            {
                if (item.mesh)
                {
                    Destroy(item.mesh);
                }
            }

            meshes.Clear();
            idleGeometry.Clear(); pooledGeometryBytes = 0;
            foreach (var item in textures.Values)
            {
                if (item)
                {
                    Destroy(item);
                }
            }

            textures.Clear();
            ReleaseLayerRendering();
            if (material)
            {
                Destroy(material);
            }

            if (active)
            {
                NativeMethods.urui_destroy();
                active = false;
            }

            borrowedTextures.Clear();
            RetireLegacyImages();
            if (ReferenceEquals(ActiveHost, this))
            {
                ActiveHost = null;
            }
        }
    }
}
