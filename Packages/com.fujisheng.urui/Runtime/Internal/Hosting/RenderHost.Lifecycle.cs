using System;
using System.IO;
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
        internal void InitializeForDocument()
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
            if (!document)
            {
                Debug.LogError("URUI: assign RML TextAsset.");
                enabled = false;
                return;
            }

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
            recordedPassName = material.GetPassName(0);
            if (reusePropertyBlock)
            {
                reusableProperties = new MaterialPropertyBlock();
            }

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
                if (NativeMethods.urui_bridge_abi_version() != 3)
                {
                    throw new Exception("URUIBridge ABI version 3 is required");
                }

                if (NativeMethods.urui_texture_protocol_version() != 1)
                    throw new Exception("URUIBridge texture protocol v1 is required; update all platform plugins");

                if (NativeMethods.urui_create(UiWidth, UiHeight, ResolveFontPath()) == 0)
                {
                    throw new Exception(NativeMethods.LastError());
                }

                active = true;
                ActiveHost = this;
#if URUI_DEBUG
                if (useInlineLookupQueue && !SetInlineLookupQueue(true))
                {
                    throw new Exception("This native build has no inline lookup queue support");
                }
#endif
                borrowedTextures.Register(imageAssets);

                var fontFile = Path.GetFileNameWithoutExtension(ResolveFontPath()).ToLowerInvariant();
                var family = fontFile.Contains("roboto") ? "Roboto" : fontFile.Contains("noto") ? "Noto Sans" : fontFile.Contains("dejavu") ? "DejaVu Sans" : "Arial";
                var rml = ControlStyleSheet.Apply(string.IsNullOrEmpty(InitialMarkup) ? document.text : InitialMarkup).Replace("__URUI_FONT__", family);
                if (NativeMethods.urui_load_document(rml) == 0)
                {
                    throw new Exception(NativeMethods.LastError());
                }
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("Build Native/URUIBridge first: " + ex.Message);
                AbortStartup();
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError("Update Native/URUIBridge to ABI3 with texture protocol v1: " + ex.Message);
                AbortStartup();
            }
            catch (Exception ex)
            {
                Debug.LogError("URUI: " + ex);
                AbortStartup();
            }
        }

        void AbortStartup()
        {
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
            enabled = false;
        }

        static string ResolveFont()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var path in new[] {
                "/system/fonts/Roboto-Regular.ttf",
                "/system/fonts/NotoSans-Regular.ttf",
                "/system/fonts/DroidSans.ttf"
            }

            )
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "/system/fonts/Roboto-Regular.ttf";
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
   return "C:/Windows/Fonts/arial.ttf";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return "/System/Library/Fonts/Supplemental/Arial.ttf";
#else
            return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
#endif
        }

        string ResolveFontPath() => string.IsNullOrEmpty(overrideFontPath) ? ResolveFont() : overrideFontPath;

        void OnDisable()
        {
            StopParameterHost();
            InvalidateRecording(true);
            if (active && nativeRetention)
            {
                ConfigureNativeRetention(false);
            }
        }

        void OnEnable()
        {
#if URUI_DEBUG
            ResetParameterRootMetrics();
#endif
            parameterRootFault = false;
            parameterFault = false;
            retainedRecording.Invalidate();
        }

        void OnDestroy()
        {
            ReleaseForDocument();
        }

        // Detach submitted commands before destroying host-owned GPU resources. The
        // document's TextAsset and source textures belong to its caller, not this host.
        internal void ReleaseForDocument()
        {
            if (resourcesReleased)
            {
                return;
            }

            resourcesReleased = true;
            enabled = false;
            StopParameterHost();
            InvalidateRecording(true);
            if (active && nativeRetention)
            {
                ConfigureNativeRetention(false);
            }

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
            foreach (var item in textures.Values)
            {
                if (item)
                {
                    Destroy(item);
                }
            }

            textures.Clear();
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
            if (ReferenceEquals(ActiveHost, this))
            {
                ActiveHost = null;
            }
        }
    }
}
