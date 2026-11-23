#ifndef URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED
#define URUI_BORROWED_TEXTURE_SAMPLING_INCLUDED

// Explicit float4 return type prevents Unity's HLSLcc Metal/GLES backend from lowering
// shared DXBC temporary registers to half/ushort and corrupting 32-bit uint texel addresses.
Texture2D<float4> _MainTex;
SamplerState sampler_MainTex;

// 0: native top-row-first, sRGB-encoded premultiplied atlas uploaded as linear data.
// 1: borrowed Unity bottom-row-first texture requiring software 4-tap Load (Point/Repeat/HDR).
// 2: solid white sentinel (0 texture fetches, returns float4(1,1,1,1)).
// 3: native top-row-first, pre-linearized premultiplied RGBAHalf atlas (1-tap hardware SampleLevel).
// 4: borrowed Unity opaque (no alpha channel) Clamp+Bilinear texture (1-tap hardware SampleLevel, flipped V).
// 5: borrowed Unity UNorm alpha Clamp+Bilinear texture (1-tap hardware SampleLevel with exact a==1 / a==0 early-out).
float _UruiBorrowedTexture;
// Registered base level: width, height, reciprocal width, reciprocal height.
float4 _UruiSourceSize;

// Since u, v are already in [0, 1], floor(u * size - 0.5) is guaranteed in [-1, size - 1],
// so x0 and x1 only ever cross the boundary by 1 texel (zero 32-bit integer modulo divisions).
void UruiBilinearTexelCoords(float2 sourceSize, float borrowed, float2 uv, int sampling,
    out int x0, out int x1, out int y0, out int y1, out float fx, out float fy)
{
    bool repX = (sampling & 1) != 0;
    bool repY = (sampling & 2) != 0;
    float u = repX ? frac(uv.x) : saturate(uv.x);
    float v = repY ? frac(uv.y) : saturate(uv.y);
    bool flipV = (borrowed > 0.5 && borrowed < 1.5) || borrowed > 3.5;
    float pixelX = u * sourceSize.x - 0.5;
    float pixelY = (flipV ? 1.0 - v : v) * sourceSize.y - 0.5;
    float baseX = floor(pixelX);
    float baseY = floor(pixelY);
    int x = (int)baseX;
    int y = (int)baseY;
    int w = (int)sourceSize.x;
    int h = (int)sourceSize.y;
    x0 = x < 0 ? (repX ? w - 1 : 0) : min(x, w - 1);
    x1 = x + 1 >= w ? (repX ? 0 : w - 1) : max(x + 1, 0);
    y0 = y < 0 ? (repY ? h - 1 : 0) : min(y, h - 1);
    y1 = y + 1 >= h ? (repY ? 0 : h - 1) : max(y + 1, 0);
    fx = pixelX - baseX;
    fy = pixelY - baseY;
}

float4 UruiConvertLoadedTexel(float4 value, float borrowed)
{
    if (borrowed < 0.5)
    {
        return UruiLinearizePremultipliedColor(value);
    }
    if (borrowed > 2.5 && borrowed < 3.5)
    {
        return value;
    }
    value.rgb *= value.a;
    return value;
}

float4 UruiFilterLoadedQuad(float4 t00, float4 t10, float4 t01, float4 t11, float borrowed, float fx, float fy)
{
    t00 = UruiConvertLoadedTexel(t00, borrowed);
    t10 = UruiConvertLoadedTexel(t10, borrowed);
    t01 = UruiConvertLoadedTexel(t01, borrowed);
    t11 = UruiConvertLoadedTexel(t11, borrowed);
    return lerp(lerp(t00, t10, fx), lerp(t01, t11, fx), fy);
}

float4 UruiSampleTexture(Texture2D<float4> inputTexture, SamplerState inputSampler, float2 sourceSize, float borrowed, float2 uv, int sampling)
{
    if (borrowed > 1.5 && borrowed < 2.5)
    {
        return float4(1.0, 1.0, 1.0, 1.0);
    }
    if (sampling == 0)
    {
        // Fast hardware bilinear path: pre-linearized native atlases (kind=3) work in both Gamma and Linear.
        if (borrowed > 2.5 && borrowed < 3.5)
            return inputTexture.SampleLevel(inputSampler, uv, 0);
        // Fast hardware bilinear path: borrowed opaque Clamp+Bilinear textures (kind=4, alpha guaranteed 1).
        if (borrowed > 3.5 && borrowed < 4.5)
            return inputTexture.SampleLevel(inputSampler, float2(uv.x, 1.0 - uv.y), 0);
        // Exact 1-tap hardware path for borrowed UNorm RGBA Clamp+Bilinear textures (kind=5):
        // Because all 4 texels satisfy a_ij in [0, 1], c.a >= 1-1e-6 implies all a_ij == 1 (exact premul),
        // and c.a <= 0 implies all a_ij == 0 (exact zero). Only translucent edges fall through to 4-tap Load.
        if (borrowed > 4.5)
        {
            float4 c = inputTexture.SampleLevel(inputSampler, float2(uv.x, 1.0 - uv.y), 0);
            if (c.a >= 0.999999) return c;
            if (c.a <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);
        }
#ifdef UNITY_COLORSPACE_GAMMA
        if (borrowed < 0.5)
            return inputTexture.SampleLevel(inputSampler, uv, 0);
#endif
    }

    int x0, x1, y0, y1;
    float fx, fy;
    UruiBilinearTexelCoords(sourceSize, borrowed, uv, sampling, x0, x1, y0, y1, fx, fy);
    float4 t00 = inputTexture.Load(int3(x0, y0, 0));
    float4 t10 = inputTexture.Load(int3(x1, y0, 0));
    float4 t01 = inputTexture.Load(int3(x0, y1, 0));
    float4 t11 = inputTexture.Load(int3(x1, y1, 0));
    return UruiFilterLoadedQuad(t00, t10, t01, t11, borrowed, fx, fy);
}

float4 UruiSampleTexture(Texture2D<float4> inputTexture, SamplerState inputSampler, float2 sourceSize, float borrowed, float2 uv)
{
    return UruiSampleTexture(inputTexture, inputSampler, sourceSize, borrowed, uv, 0);
}

float4 UruiSampleMainTexture(float2 uv)
{
    return UruiSampleTexture(_MainTex, sampler_MainTex, _UruiSourceSize.xy, _UruiBorrowedTexture, uv, 0);
}

#endif
