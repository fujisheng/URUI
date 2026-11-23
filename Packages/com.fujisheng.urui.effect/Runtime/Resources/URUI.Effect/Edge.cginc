// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_EDGE_INCLUDED
#define URUI_EFFECT_EDGE_INCLUDED
float UruiEdgeFactor(float2 uv)
{
    if (_EdgeMode==0) return 0;
    float2 d=UruiSamplingTexel()*lerp(1,20,_EdgeWidth);
    float4 bounds=_SourceUvBounds;
    float2 center=(bounds.xy+bounds.zw)*0.5;
    bool fullTexture=all(bounds.xy==0)&&all(bounds.zw==1);
    float2 clampMin=min(bounds.xy+_SourceTexel.xy*0.5,center);
    float2 clampMax=max(bounds.zw-_SourceTexel.xy*0.5,center);
    bool flipY=_InputBottomLeft>0.5;
    bool implicitLod=fullTexture&&_InputCaptured<0.5;
    bool slotZero=_UiTextureSlot<0.5;
    float minimum=1;
    const float2 directions[12]={float2(1,0),float2(.866025,.5),float2(.5,.866025),float2(0,1),float2(-.5,.866025),float2(-.866025,.5),
        float2(-1,0),float2(-.866025,-.5),float2(-.5,-.866025),float2(0,-1),float2(.5,-.866025),float2(.866025,-.5)};
    [unroll] for(int i=0;i<12;i++)
    {
        minimum=min(minimum,UruiSampleAlphaClampedFast(uv+d*directions[i],bounds,fullTexture,clampMin,clampMax,flipY,implicitLod,slotZero));
    }
    return 1-UruiInverseRange(.15,.3,minimum);
}
half4 UruiEdge(half4 color,float edgeFactor,float2 local)
{
    int mode=_EdgeMode;
    if (mode==0) return color;
    half4 edgeColor=UruiColor(color,_EdgeColor,1,_EdgeColorFilter,_EdgeColorGlow,false);
    float shiny=1;
    if (mode==2)
    {
        float angle=atan2(local.y-.5,local.x-.5)/UNITY_PI;
        float4 row11=UruiParameterTexel(_UiFieldBase+11);
        float4 row12=UruiParameterTexel(_UiFieldBase+12);
        shiny=frac(saturate(row11.w)+_EffectTime*clamp(row12.y,-5,5)+angle)<saturate(row12.x);
    }
    return lerp(color,edgeColor,edgeFactor*shiny);
}
#endif
