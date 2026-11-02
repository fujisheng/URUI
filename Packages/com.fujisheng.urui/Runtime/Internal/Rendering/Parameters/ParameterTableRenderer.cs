using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Resources;
using URUI.Internal.Rendering.Meshes;

namespace URUI.Internal.Rendering.Parameters
{
    // 参数表渲染器私有持有计划与资源；调用者负责独占提交，Dispose 前必须完成 GPU 退役。
    internal sealed partial class ParameterTableRenderer
    {
        readonly Dictionary<(ulong epoch, ulong id), SharedMesh> sharedMeshes = new Dictionary<(ulong, ulong), SharedMesh>();
        readonly Dictionary<(ulong epoch, ulong id), SharedTexture> sharedTextures = new Dictionary<(ulong, ulong), SharedTexture>();
        readonly BorrowedTextureRegistry borrowedTextures;
        readonly bool lazySourceMeshes;
        readonly bool shareResources;
        long texturePayload;
        long sourcePayload;
        long combinedPayload;
        internal const long MaxSharedPayloadBytes = 64L * 1024 * 1024;

        internal long SharedPayloadBytes => texturePayload + sourcePayload + combinedPayload + nativeGpuPayload + sourceCpuPayload + batchIdentityPayload;

        const long MaxPlanGeometryBytes = 16L * 1024 * 1024;
        readonly Shader expectedLegacy;
        readonly Shader expectedParameter;
        readonly Material legacy;
        readonly Material parameterMaterial;
        readonly int maxPackets;
        readonly List<Packet> packets = new List<Packet>();
        readonly List<Plan> plans = new List<Plan>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly BatchScratch scratch;
        Plan current;
        ulong serial;
        bool disposed;

        internal string LastFailure
        {
            get;
            private set;
        }

        internal bool Supported => legacy
            && parameterMaterial
            && legacy.shader == expectedLegacy
            && parameterMaterial.shader == expectedParameter
            && parameterMaterial.shader.isSupported
            && SystemInfo.supportsGraphicsFence
            && SystemInfo.graphicsShaderLevel >= 45
            && SystemInfo.supports32bitsIndexBuffer;

        internal ParameterTableRenderer(
            Shader legacyShader,
            Shader parameterShader,
            int maxPackets = 8,
            bool shareResources = false,
            bool reuseBatches = false,
            bool reuseBatchScratch = false,
            bool lazySourceMeshes = false,
            bool nativePrepared = false,
            bool nativeIncremental = false,
            bool nativePinned = false,
            BorrowedTextureRegistry borrowedTextures = null)
        {
            this.borrowedTextures = borrowedTextures;
            this.nativePinned = nativePinned;
            this.nativeIncremental = nativeIncremental;
            this.nativePrepared = shareResources && nativePrepared && NativePrepared.Available;
            this.lazySourceMeshes = shareResources && reuseBatches && lazySourceMeshes;
            scratch = shareResources && reuseBatches && reuseBatchScratch ? new BatchScratch() : null;
            this.reuseBatches = shareResources && reuseBatches;
            this.shareResources = shareResources;
            if (maxPackets < 1 || maxPackets > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPackets));
            }

            this.maxPackets = maxPackets;
            expectedLegacy = legacyShader;
            expectedParameter = parameterShader;
            legacy = new Material(legacyShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            parameterMaterial = new Material(parameterShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
