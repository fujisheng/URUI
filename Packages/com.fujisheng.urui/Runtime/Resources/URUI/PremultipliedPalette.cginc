#ifndef URUI_PREMULTIPLIED_PALETTE_INCLUDED
#define URUI_PREMULTIPLIED_PALETTE_INCLUDED

Texture2D<float4> _UruiTexture1;
SamplerState sampler_UruiTexture1;
float4 _UruiSource1;
Texture2D<float4> _UruiTexture2;
SamplerState sampler_UruiTexture2;
float4 _UruiSource2;
Texture2D<float4> _UruiTexture3;
SamplerState sampler_UruiTexture3;
float4 _UruiSource3;
Texture2D<float4> _UruiTexture4;
SamplerState sampler_UruiTexture4;
float4 _UruiSource4;
Texture2D<float4> _UruiTexture5;
SamplerState sampler_UruiTexture5;
float4 _UruiSource5;
Texture2D<float4> _UruiTexture6;
SamplerState sampler_UruiTexture6;
float4 _UruiSource6;
Texture2D<float4> _UruiTexture7;
SamplerState sampler_UruiTexture7;
float4 _UruiSource7;

float3 UruiPaletteSourceInfo(int slotIdx)
{
    if (slotIdx == 0) return float3(_UruiSourceSize.xy, _UruiBorrowedTexture);
    if (slotIdx == 1) return _UruiSource1.xyz;
    if (slotIdx == 2) return _UruiSource2.xyz;
    if (slotIdx == 3) return _UruiSource3.xyz;
    if (slotIdx == 4) return _UruiSource4.xyz;
    if (slotIdx == 5) return _UruiSource5.xyz;
    if (slotIdx == 6) return _UruiSource6.xyz;
    return _UruiSource7.xyz;
}

float4 UruiSampleSlotLevel(int slotIdx, float2 uv)
{
    if (slotIdx == 0) return _MainTex.SampleLevel(sampler_MainTex, uv, 0);
    if (slotIdx == 1) return _UruiTexture1.SampleLevel(sampler_UruiTexture1, uv, 0);
    if (slotIdx == 2) return _UruiTexture2.SampleLevel(sampler_UruiTexture2, uv, 0);
    if (slotIdx == 3) return _UruiTexture3.SampleLevel(sampler_UruiTexture3, uv, 0);
    if (slotIdx == 4) return _UruiTexture4.SampleLevel(sampler_UruiTexture4, uv, 0);
    if (slotIdx == 5) return _UruiTexture5.SampleLevel(sampler_UruiTexture5, uv, 0);
    if (slotIdx == 6) return _UruiTexture6.SampleLevel(sampler_UruiTexture6, uv, 0);
    return _UruiTexture7.SampleLevel(sampler_UruiTexture7, uv, 0);
}

void UruiLoadSlotQuad(int slotIdx, int x0, int x1, int y0, int y1,
    out float4 t00, out float4 t10, out float4 t01, out float4 t11)
{
    if (slotIdx == 0)
    {
        t00 = _MainTex.Load(int3(x0, y0, 0)); t10 = _MainTex.Load(int3(x1, y0, 0));
        t01 = _MainTex.Load(int3(x0, y1, 0)); t11 = _MainTex.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 1)
    {
        t00 = _UruiTexture1.Load(int3(x0, y0, 0)); t10 = _UruiTexture1.Load(int3(x1, y0, 0));
        t01 = _UruiTexture1.Load(int3(x0, y1, 0)); t11 = _UruiTexture1.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 2)
    {
        t00 = _UruiTexture2.Load(int3(x0, y0, 0)); t10 = _UruiTexture2.Load(int3(x1, y0, 0));
        t01 = _UruiTexture2.Load(int3(x0, y1, 0)); t11 = _UruiTexture2.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 3)
    {
        t00 = _UruiTexture3.Load(int3(x0, y0, 0)); t10 = _UruiTexture3.Load(int3(x1, y0, 0));
        t01 = _UruiTexture3.Load(int3(x0, y1, 0)); t11 = _UruiTexture3.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 4)
    {
        t00 = _UruiTexture4.Load(int3(x0, y0, 0)); t10 = _UruiTexture4.Load(int3(x1, y0, 0));
        t01 = _UruiTexture4.Load(int3(x0, y1, 0)); t11 = _UruiTexture4.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 5)
    {
        t00 = _UruiTexture5.Load(int3(x0, y0, 0)); t10 = _UruiTexture5.Load(int3(x1, y0, 0));
        t01 = _UruiTexture5.Load(int3(x0, y1, 0)); t11 = _UruiTexture5.Load(int3(x1, y1, 0));
    }
    else if (slotIdx == 6)
    {
        t00 = _UruiTexture6.Load(int3(x0, y0, 0)); t10 = _UruiTexture6.Load(int3(x1, y0, 0));
        t01 = _UruiTexture6.Load(int3(x0, y1, 0)); t11 = _UruiTexture6.Load(int3(x1, y1, 0));
    }
    else
    {
        t00 = _UruiTexture7.Load(int3(x0, y0, 0)); t10 = _UruiTexture7.Load(int3(x1, y0, 0));
        t01 = _UruiTexture7.Load(int3(x0, y1, 0)); t11 = _UruiTexture7.Load(int3(x1, y1, 0));
    }
}

// Shared hoisted palette sampler: all coordinate math, fast-path checks, premul/linearize,
// and bilinear lerp live ONCE outside the 8-way texture switch (~75% smaller shader & lower VGPRs).
float4 UruiSamplePalette(float2 uv, float slot, int sampling)
{
    int slotIdx = (int)(slot + 0.5);
    float3 src = UruiPaletteSourceInfo(slotIdx);
    float borrowed = src.z;
    if (borrowed > 1.5 && borrowed < 2.5)
    {
        return float4(1.0, 1.0, 1.0, 1.0);
    }
    if (sampling == 0)
    {
        if (borrowed > 2.5 && borrowed < 3.5)
        {
            return UruiSampleSlotLevel(slotIdx, uv);
        }
        if (borrowed > 3.5 && borrowed < 4.5)
        {
            return UruiSampleSlotLevel(slotIdx, float2(uv.x, 1.0 - uv.y));
        }
        if (borrowed > 4.5)
        {
            float4 c = UruiSampleSlotLevel(slotIdx, float2(uv.x, 1.0 - uv.y));
            if (c.a >= 0.999999) return c;
            if (c.a <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);
        }
#ifdef UNITY_COLORSPACE_GAMMA
        if (borrowed < 0.5)
        {
            return UruiSampleSlotLevel(slotIdx, uv);
        }
#endif
    }
    int x0, x1, y0, y1;
    float fx, fy;
    UruiBilinearTexelCoords(src.xy, borrowed, uv, sampling, x0, x1, y0, y1, fx, fy);
    float4 t00, t10, t01, t11;
    UruiLoadSlotQuad(slotIdx, x0, x1, y0, y1, t00, t10, t01, t11);
    return UruiFilterLoadedQuad(t00, t10, t01, t11, borrowed, fx, fy);
}

// Hardware-only palette sampler for CompleteFast pass (all slots proven kind 2/3/4; zero Load/lerp).
float4 UruiSamplePaletteFast(float2 uv, float slot)
{
    int slotIdx = (int)(slot + 0.5);
    float borrowed = UruiPaletteSourceInfo(slotIdx).z;
    if (borrowed > 1.5 && borrowed < 2.5)
    {
        return float4(1.0, 1.0, 1.0, 1.0);
    }
    float2 sampleUv = borrowed > 3.5 ? float2(uv.x, 1.0 - uv.y) : uv;
    return UruiSampleSlotLevel(slotIdx, sampleUv);
}

float4 UruiSamplePalette(float2 uv, float slot)
{
    return UruiSamplePalette(uv, slot, 0);
}

#endif
