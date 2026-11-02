#ifndef URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED
#define URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED

Texture2D _MainTex;
SamplerState sampler_MainTex;

// 0: native top-row-first, already-premultiplied owned atlas (original path).
// 1: borrowed Unity bottom-row-first, straight-alpha color Texture2D.
float _UruiBorrowedTexture;
// Registered base level: width, height, reciprocal width, reciprocal height.
float4 _UruiSourceSize;
// The original source's sRGB interpretation, independent of project color space.
float _UruiSourceSrgb;

float UruiSrgbToLinear(float value)
{
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
}

float UruiLinearToSrgb(float value)
{
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

float4 UruiLoadBorrowedTexel(int x, int y)
{
    int pixelX = clamp(x, 0, (int)_UruiSourceSize.x - 1);
    int pixelY = clamp(y, 0, (int)_UruiSourceSize.y - 1);
    // Load has no sampler: integer clamping plus mip 0 implements point/clamp
    // on Metal, DX11, GLES3 and WebGL2 without touching caller texture state.
    // The GPU decompresses compressed formats and performs source sRGB decode.
    float4 value = _MainTex.Load(int3(pixelX, pixelY, 0));
    if (_UruiSourceSrgb > 0.5)
    {
#ifndef UNITY_COLORSPACE_GAMMA
        // Native historically premultiplied encoded UNorm8 bytes, then uploaded
        // an sRGB atlas. Recover those encoded values before premultiplication.
        value.r = UruiLinearToSrgb(value.r);
        value.g = UruiLinearToSrgb(value.g);
        value.b = UruiLinearToSrgb(value.b);
#endif
        // Match native (channelByte * alphaByte + 127) / 255, including its
        // quantization, before filtering. Transparent colors contribute zero.
        value.r = floor(saturate(value.r) * saturate(value.a) * 255.0 + 0.5) / 255.0;
        value.g = floor(saturate(value.g) * saturate(value.a) * 255.0 + 0.5) / 255.0;
        value.b = floor(saturate(value.b) * saturate(value.a) * 255.0 + 0.5) / 255.0;
#ifndef UNITY_COLORSPACE_GAMMA
        value.r = UruiSrgbToLinear(value.r);
        value.g = UruiSrgbToLinear(value.g);
        value.b = UruiSrgbToLinear(value.b);
#endif
    }
    else
    {
        // Linear sources remain linear (including float/HDR values). The old
        // pixel-copy path incorrectly relabelled these values as sRGB bytes.
        value.r *= value.a;
        value.g *= value.a;
        value.b *= value.a;
    }
    return value;
}

float4 UruiSampleMainTexture(float2 uv)
{
    if (_UruiBorrowedTexture < 0.5)
    {
        // Same implicit LOD and original texture-bound sampler as tex2D.
        // Generated/font atlas RGB is already premultiplied; leave it intact.
        return _MainTex.Sample(sampler_MainTex, uv);
    }

    // Historical UI images use bilinear/clamp/base mip, regardless of the
    // borrowed source's Point/Repeat/anisotropic/mipmap configuration.
    float pixelX = saturate(uv.x) * _UruiSourceSize.x - 0.5;
    float pixelY = (1.0 - saturate(uv.y)) * _UruiSourceSize.y - 0.5;
    float baseX = floor(pixelX);
    float baseY = floor(pixelY);
    int x = (int)baseX;
    int y = (int)baseY;
    float4 row0 = lerp(UruiLoadBorrowedTexel(x, y), UruiLoadBorrowedTexel(x + 1, y), pixelX - baseX);
    float4 row1 = lerp(UruiLoadBorrowedTexel(x, y + 1), UruiLoadBorrowedTexel(x + 1, y + 1), pixelX - baseX);
    return lerp(row0, row1, pixelY - baseY);
}

#endif
