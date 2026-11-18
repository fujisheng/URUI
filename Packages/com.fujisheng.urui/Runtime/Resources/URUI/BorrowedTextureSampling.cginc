#ifndef URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED
#define URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED

#include "FramebufferBlend.cginc"

Texture2D _MainTex;
SamplerState sampler_MainTex;

// 0: native top-row-first, encoded-premultiplied owned atlas uploaded as data.
// 1: borrowed Unity bottom-row-first, straight-alpha color texture.
// 2: the same source, with uncompressed sRGB byte codes.
float _UruiBorrowedTexture;
// Registered base level: width, height, reciprocal width, reciprocal height.
float4 _UruiSourceSize;
// The original source's sRGB interpretation, independent of project color space.
float _UruiSourceSrgb;
// Geometry-local addressing: bit0 repeats X, bit1 repeats Y. Source textures stay unchanged.
float _UruiTextureSampling;

int UruiTexelAddress(int value, int size, bool repeatAxis)
{
    if (!repeatAxis) return clamp(value, 0, size - 1);
    return ((value % size) + size) % size;
}

float4 UruiLoadTextureTexel(Texture2D inputTexture, float2 sourceSize, float borrowed, int x, int y, int sampling)
{
    int pixelX = UruiTexelAddress(x, (int)sourceSize.x, (sampling & 1) != 0);
    int pixelY = UruiTexelAddress(y, (int)sourceSize.y, (sampling & 2) != 0);
    // Load has no sampler: per-draw addressing plus mip 0 implements sampling
    // on Metal, DX11, GLES3 and WebGL2 without touching caller texture state.
    // The GPU decompresses compressed formats and performs source sRGB decode.
    float4 value = inputTexture.Load(int3(pixelX, pixelY, 0));
    if (borrowed < 0.5)
    {
        // Native RGBA is encoded premultiplied data uploaded without GPU sRGB decode.
        // Convert straight RGB to the render color space, then restore alpha before filtering.
        return UruiLinearizePremultipliedColor(value);
    }
    // Borrowed source textures perform their own sRGB decode on Load. Premultiply
    // the resulting straight color once; this also covers linear and HDR sources.
    #ifndef UNITY_COLORSPACE_GAMMA
    if (_UruiEncodedTarget > .5)
    {
        // The fast approximation drops dark sRGB values. Hardware sRGB reads
        // also have finite precision; recover discrete byte codes only where
        // the source format guarantees them, before alpha/filtering.
        value.rgb = UruiFetchEncodeSrgb(max(value.rgb, 0));
        if (borrowed > 1.5) value.rgb = round(value.rgb * 255) / 255;
    }
    #endif
    value.rgb *= value.a;
    return value;
}

float4 UruiSampleTexture(Texture2D inputTexture, SamplerState inputSampler, float2 sourceSize, float borrowed, float2 uv, int sampling)
{
#ifdef UNITY_COLORSPACE_GAMMA
    if (borrowed < 0.5 && sampling == 0)
        return inputTexture.Sample(inputSampler, uv);
#endif

    // Addressing belongs to this draw. Clamp keeps the existing image behavior;
    // CSS repeating images wrap each bilinear tap across the texture boundary.
    float u = (sampling & 1) != 0 ? frac(uv.x) : saturate(uv.x);
    float v = (sampling & 2) != 0 ? frac(uv.y) : saturate(uv.y);
    float pixelX = u * sourceSize.x - 0.5;
    float pixelY = (borrowed > 0.5 ? 1.0 - v : v) * sourceSize.y - 0.5;
    float baseX = floor(pixelX);
    float baseY = floor(pixelY);
    int x = (int)baseX;
    int y = (int)baseY;
    float4 row0 = lerp(UruiLoadTextureTexel(inputTexture, sourceSize, borrowed, x, y, sampling), UruiLoadTextureTexel(inputTexture, sourceSize, borrowed, x + 1, y, sampling), pixelX - baseX);
    float4 row1 = lerp(UruiLoadTextureTexel(inputTexture, sourceSize, borrowed, x, y + 1, sampling), UruiLoadTextureTexel(inputTexture, sourceSize, borrowed, x + 1, y + 1, sampling), pixelX - baseX);
    return lerp(row0, row1, pixelY - baseY);
}

float4 UruiSampleTexture(Texture2D inputTexture, SamplerState inputSampler, float2 sourceSize, float borrowed, float2 uv)
{
    return UruiSampleTexture(inputTexture, inputSampler, sourceSize, borrowed, uv, (int)_UruiTextureSampling);
}

float4 UruiSampleMainTexture(float2 uv)
{
    return UruiSampleTexture(_MainTex, sampler_MainTex, _UruiSourceSize.xy, _UruiBorrowedTexture, uv);
}

#endif
