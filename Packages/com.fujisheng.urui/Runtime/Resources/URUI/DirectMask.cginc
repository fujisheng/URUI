#ifndef URUI_DIRECT_MASK_INCLUDED
#define URUI_DIRECT_MASK_INCLUDED
Texture2D<float4> _UruiMaskTexture;
SamplerState sampler_UruiMaskTexture;
float4 _UruiMaskSource;

// Alpha is always linear and never premultiplied: use 1-tap hardware SampleLevel.a whenever
// Bilinear+Clamp matches (including interior pixels of repeated masks), and scalar-alpha 4-tap Load otherwise.
float UruiSampleMaskAlpha(float2 uv, int sampling)
{
    float borrowed = _UruiMaskSource.z;
    if ((borrowed > 1.5 && borrowed < 2.5) || (borrowed > 3.5 && borrowed < 4.5))
    {
        return 1.0;
    }
    if (borrowed > 2.5)
    {
        bool flipV = borrowed > 3.5;
        if (sampling == 0)
        {
            return _UruiMaskTexture.SampleLevel(sampler_UruiMaskTexture, flipV ? float2(uv.x, 1.0 - uv.y) : uv, 0).a;
        }
        float u = (sampling & 1) != 0 ? frac(uv.x) : saturate(uv.x);
        float v = (sampling & 2) != 0 ? frac(uv.y) : saturate(uv.y);
        float2 halfTexel = 0.5 / _UruiMaskSource.xy;
        bool interiorX = (sampling & 1) == 0 || (u >= halfTexel.x && u <= 1.0 - halfTexel.x);
        bool interiorY = (sampling & 2) == 0 || (v >= halfTexel.y && v <= 1.0 - halfTexel.y);
        if (interiorX && interiorY)
        {
            return _UruiMaskTexture.SampleLevel(sampler_UruiMaskTexture, flipV ? float2(u, 1.0 - v) : float2(u, v), 0).a;
        }
    }
    int x0, x1, y0, y1;
    float fx, fy;
    UruiBilinearTexelCoords(_UruiMaskSource.xy, borrowed, uv, sampling, x0, x1, y0, y1, fx, fy);
    float a00 = _UruiMaskTexture.Load(int3(x0, y0, 0)).a;
    float a10 = _UruiMaskTexture.Load(int3(x1, y0, 0)).a;
    float a01 = _UruiMaskTexture.Load(int3(x0, y1, 0)).a;
    float a11 = _UruiMaskTexture.Load(int3(x1, y1, 0)).a;
    return lerp(lerp(a00, a10, fx), lerp(a01, a11, fx), fy);
}

float UruiDirectMaskAtWindow(float2 window, float offset)
{
    float4 local = UruiParameterTexel(offset) * window.x + UruiParameterTexel(offset + 1) * window.y + UruiParameterTexel(offset + 3);
    if (abs(local.w) < 1e-8) return 0;
    float2 coordinate = local.xy / local.w;
    float4 rectangle = UruiParameterTexel(offset + 4);
    float4 domain = UruiParameterTexel(offset + 7);
    float coverage = step(rectangle.x, coordinate.x) * step(coordinate.x, rectangle.z) * step(rectangle.y, coordinate.y) * step(coordinate.y, rectangle.w);
    coverage *= step(domain.x, window.x) * step(window.x, domain.z) * step(domain.y, window.y) * step(window.y, domain.w);
    if (coverage <= 0) return 0;
    float4 u = UruiParameterTexel(offset + 5), v = UruiParameterTexel(offset + 6);
    float2 uv = float2(dot(u.xyz, float3(coordinate, 1)), dot(v.xyz, float3(coordinate, 1)));
    int flags = (int)u.w;
    float alpha;
    if ((flags & 8) != 0)
    {
        float4 feather = UruiParameterTexel(offset + 2);
        float2 localPixel = coordinate * u.xy;
        float4 distance = float4(localPixel.x, localPixel.y, u.x - localPixel.x, u.y - localPixel.y);
        float4 ramp = lerp(saturate(distance / max(feather, 1e-20)), 1, step(feather, 0));
        alpha = ramp.x * ramp.y * ramp.z * ramp.w;
    }
    else
    {
        alpha = (flags & 4) != 0 ? 1 : UruiSampleMaskAlpha(uv, flags & 3);
    }
    return alpha * v.w * coverage;
}

float UruiDirectMask(float3 pixel, float offset)
{
    return UruiDirectMaskAtWindow(pixel.xy / pixel.z, offset);
}
#endif
