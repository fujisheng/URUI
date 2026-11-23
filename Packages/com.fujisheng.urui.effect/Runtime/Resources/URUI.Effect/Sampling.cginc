// 依据 UIEffect 5.8.4 的采样核与包装顺序改编；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_SAMPLING_INCLUDED
#define URUI_EFFECT_SAMPLING_INCLUDED
float UruiSamplingMask(float2 uv)
{ return step(_SourceUvBounds.x,uv.x)*step(uv.x,_SourceUvBounds.z)*step(_SourceUvBounds.y,uv.y)*step(uv.y,_SourceUvBounds.w); }
float4 UruiSampleClamped(float2 uv) { return SampleSource(uv) * UruiSamplingMask(uv); }
float2 UruiSamplingTexel() { return _SourceTexel.xy * _SamplingScale; }
float UruiSampleAlphaClampedFast(float2 uv, float4 bounds, bool fullTexture, float2 clampMin, float2 clampMax, bool flipY, bool implicitLod, bool slotZero)
{
    if (!implicitLod)
    {
        if (uv.x < bounds.x || uv.x > bounds.z || uv.y < bounds.y || uv.y > bounds.w) return 0;
        float2 suv = fullTexture ? uv : clamp(uv, clampMin, clampMax);
        if (flipY) suv.y = 1.0 - suv.y;
        return slotZero ? tex2Dlod(_MainTex, float4(suv, 0, 0)).a : UruiImageSampleLevel(suv, _UiTextureSlot).a;
    }
    // Keep implicit-derivative texture fetches outside divergent branches so 2x2 pixel quad gradients remain uniform.
    float mask = (uv.x >= bounds.x && uv.x <= bounds.z && uv.y >= bounds.y && uv.y <= bounds.w) ? 1.0 : 0.0;
    float2 suv = fullTexture ? uv : clamp(uv, clampMin, clampMax);
    if (flipY) suv.y = 1.0 - suv.y;
    float a = slotZero ? tex2D(_MainTex, suv).a : UruiImageSample(suv, _UiTextureSlot).a;
    return a * mask;
}
float4 UruiSamplingBase(float2 uv, float intensity, bool hasOriginal, float4 original)
{
    #if defined(URUI_BLUR_FAST) || defined(URUI_BLUR_MEDIUM) || defined(URUI_BLUR_DETAIL)
    if (intensity > 0)
    {
        #if defined(URUI_BLUR_FAST)
        const int size = 5;
        const float weights[5] = {.2486,.7046,1,.7046,.2486};
        #elif defined(URUI_BLUR_MEDIUM)
        const int size = 9;
        const float weights[9] = {.0438,.1719,.4566,.8204,1,.8204,.4566,.1719,.0438};
        #else
        const int size = 13;
        const float weights[13] = {.0438,.1138,.2486,.4566,.7046,.9141,1,.9141,.7046,.4566,.2486,.1138,.0438};
        #endif
        float4 bounds = _SourceUvBounds;
        float2 center = (bounds.xy + bounds.zw) * 0.5;
        bool fullTexture = all(bounds.xy == 0) && all(bounds.zw == 1);
        float2 clampMin = min(bounds.xy + _SourceTexel.xy * 0.5, center);
        float2 clampMax = max(bounds.zw - _SourceTexel.xy * 0.5, center);
        bool flipY = _InputBottomLeft > 0.5;
        bool implicitLod = fullTexture && _InputCaptured < 0.5;
        bool premultiply = _InputPremultiplied < 0.5;
        bool linearize = !premultiply && _InputCaptured < 0.5;
        bool slotZero = _UiTextureSlot < 0.5;
        float4 color = 0; float sum = 0; float2 delta = UruiSamplingTexel() * (intensity * 2.0);
        const float halfSize = float(size / 2);
        if (!implicitLod)
        {
            [loop] for (int x=0;x<size;x++)
            {
                float u = uv.x + delta.x * (float(x) - halfSize);
                if (u < bounds.x || u > bounds.z) continue;
                float wx = weights[x];
                float cu = fullTexture ? u : clamp(u, clampMin.x, clampMax.x);
                [loop] for (int y=0;y<size;y++)
                {
                    float v = uv.y + delta.y * (float(y) - halfSize);
                    if (v < bounds.y || v > bounds.w) continue;
                    float weight = wx * weights[y];
                    float cv = fullTexture ? v : clamp(v, clampMin.y, clampMax.y);
                    float2 sampleUv = float2(cu, flipY ? 1.0 - cv : cv);
                    float4 tap = slotZero ? tex2Dlod(_MainTex, float4(sampleUv, 0, 0)) : UruiImageSampleLevel(sampleUv, _UiTextureSlot);
                    if (premultiply) { tap.rgb *= tap.a; }
                    else if (linearize) { tap = UruiLinearizePremultipliedColor(tap); }
                    color += tap * weight; sum += weight;
                }
            }
        }
        else
        {
            [loop] for (int x=0;x<size;x++)
            {
                float u = uv.x + delta.x * (float(x) - halfSize);
                float wx = weights[x] * ((u >= bounds.x && u <= bounds.z) ? 1.0 : 0.0);
                [loop] for (int y=0;y<size;y++)
                {
                    float v = uv.y + delta.y * (float(y) - halfSize);
                    float weight = wx * weights[y] * ((v >= bounds.y && v <= bounds.w) ? 1.0 : 0.0);
                    float2 sampleUv = float2(u, flipY ? 1.0 - v : v);
                    float4 tap = slotZero ? tex2D(_MainTex, sampleUv) : UruiImageSample(sampleUv, _UiTextureSlot);
                    if (premultiply) { tap.rgb *= tap.a; }
                    else if (linearize) { tap = UruiLinearizePremultipliedColor(tap); }
                    color += tap * weight; sum += weight;
                }
            }
        }
        return sum > 0 ? color / sum : 0;
    }
    #endif
    return hasOriginal ? original : UruiSampleClamped(uv);
}
float UruiSamplingEdgeValue(float2 uv)
{
    float4 color = UruiSampleClamped(uv);
    return _SamplingFilter == 6 ? Luminance(color.rgb)*color.a : color.a;
}
float UruiSamplingEdge(float2 uv)
{
    float2 d = UruiSamplingTexel()*_SamplingWidth;
    half v00, v01, v02, v10, v12, v20, v21, v22;
    if (_SamplingFilter == 7)
    {
        float4 bounds = _SourceUvBounds;
        float2 center = (bounds.xy + bounds.zw) * 0.5;
        bool fullTexture = all(bounds.xy == 0) && all(bounds.zw == 1);
        float2 clampMin = min(bounds.xy + _SourceTexel.xy * 0.5, center);
        float2 clampMax = max(bounds.zw - _SourceTexel.xy * 0.5, center);
        bool flipY = _InputBottomLeft > 0.5;
        bool implicitLod = fullTexture && _InputCaptured < 0.5;
        bool slotZero = _UiTextureSlot < 0.5;
        v00 = UruiSampleAlphaClampedFast(uv + float2(-d.x, -d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v01 = UruiSampleAlphaClampedFast(uv + float2(-d.x, 0), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v02 = UruiSampleAlphaClampedFast(uv + float2(-d.x, d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v10 = UruiSampleAlphaClampedFast(uv + float2(0, -d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v12 = UruiSampleAlphaClampedFast(uv + float2(0, d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v20 = UruiSampleAlphaClampedFast(uv + float2(d.x, -d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v21 = UruiSampleAlphaClampedFast(uv + float2(d.x, 0), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
        v22 = UruiSampleAlphaClampedFast(uv + float2(d.x, d.y), bounds, fullTexture, clampMin, clampMax, flipY, implicitLod, slotZero);
    }
    else
    {
        v00=UruiSamplingEdgeValue(uv+float2(-d.x,-d.y)); v01=UruiSamplingEdgeValue(uv+float2(-d.x,0)); v02=UruiSamplingEdgeValue(uv+float2(-d.x,d.y));
        v10=UruiSamplingEdgeValue(uv+float2(0,-d.y)); v12=UruiSamplingEdgeValue(uv+float2(0,d.y));
        v20=UruiSamplingEdgeValue(uv+float2(d.x,-d.y)); v21=UruiSamplingEdgeValue(uv+float2(d.x,0)); v22=UruiSamplingEdgeValue(uv+float2(d.x,d.y));
    }
    half horizontal=-v00-2*v01-v02+v20+2*v21+v22,vertical=-v00-2*v10-v20+v02+2*v12+v22;
    half sobel=sqrt(horizontal*horizontal+vertical*vertical)*_SamplingIntensity;
    return UruiInverseRange(.5,1,sobel);
}
#endif
