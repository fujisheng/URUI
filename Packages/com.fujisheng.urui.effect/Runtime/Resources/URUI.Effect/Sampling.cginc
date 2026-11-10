// 依据 UIEffect 5.8.4 的采样核与包装顺序改编；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_SAMPLING_INCLUDED
#define URUI_EFFECT_SAMPLING_INCLUDED
float UruiSamplingMask(float2 uv)
{ return step(_SourceUvBounds.x,uv.x)*step(uv.x,_SourceUvBounds.z)*step(_SourceUvBounds.y,uv.y)*step(uv.y,_SourceUvBounds.w); }
float4 UruiSampleClamped(float2 uv) { return SampleSource(uv) * UruiSamplingMask(uv); }
float2 UruiSamplingTexel() { return _SourceTexel.xy * _SamplingScale; }
float4 UruiSamplingBase(float2 uv, float intensity)
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
        float4 color = 0; float sum = 0; half2 delta = UruiSamplingTexel() * intensity * 2;
        [unroll] for (int x=0;x<size;x++) [unroll] for (int y=0;y<size;y++)
        {
            float2 sampleUv = uv + delta * float2(float(x)-size/2,float(y)-size/2);
            float weight = weights[x]*weights[y]*UruiSamplingMask(sampleUv);
            color += SampleSource(sampleUv)*weight; sum += weight;
        }
        return sum > 0 ? color / sum : 0;
    }
    #endif
    return UruiSampleClamped(uv);
}
float UruiSamplingEdgeValue(float2 uv)
{
    float4 color = UruiSampleClamped(uv);
    return _SamplingFilter == 6 ? Luminance(color.rgb)*color.a : color.a;
}
float UruiSamplingEdge(float2 uv)
{
    float2 d = UruiSamplingTexel()*_SamplingWidth;
    half v00=UruiSamplingEdgeValue(uv+float2(-d.x,-d.y)),v01=UruiSamplingEdgeValue(uv+float2(-d.x,0)),v02=UruiSamplingEdgeValue(uv+float2(-d.x,d.y));
    half v10=UruiSamplingEdgeValue(uv+float2(0,-d.y)),v12=UruiSamplingEdgeValue(uv+float2(0,d.y));
    half v20=UruiSamplingEdgeValue(uv+float2(d.x,-d.y)),v21=UruiSamplingEdgeValue(uv+float2(d.x,0)),v22=UruiSamplingEdgeValue(uv+float2(d.x,d.y));
    half horizontal=-v00-2*v01-v02+v20+2*v21+v22,vertical=-v00-2*v10-v20+v02+2*v12+v22;
    half sobel=sqrt(horizontal*horizontal+vertical*vertical)*_SamplingIntensity;
    return UruiInverseRange(.5,1,sobel);
}
#endif
