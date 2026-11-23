// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_DETAIL_INCLUDED
#define URUI_EFFECT_DETAIL_INCLUDED
sampler2D _DetailTex;
half4 UruiDetail(half4 color,float2 local)
{
    int filter=_DetailFilter;
    if (filter==0) return color;
    half4 original=color;
    float4 row16=UruiParameterTexel(_UiFieldBase+16);
    float4 row17=UruiParameterTexel(_UiFieldBase+17);
    float2 uv=local*row16.zw+row17.xy+_EffectTime*row17.zw;
    half4 detail=tex2D(_DetailTex,uv);
    if (filter==1)
    {
        float2 thresh=saturate(UruiParameterTexel(_UiFieldBase+11).xy);
        color.a*=UruiInverseRange(thresh.x,thresh.y,detail.a);
        return color;
    }
    detail*=_DetailColor;
    if (filter==2) color.rgb*=detail.rgb;
    else if (filter==3) color.rgb+=detail.rgb*color.a;
    else if (filter==6) color.rgb-=detail.rgb*color.a;
    else if (filter==4) color.rgb=detail.rgb*color.a;
    else if (filter==5) color.rgb*=1+detail.rgb;
    return lerp(original,color,_DetailIntensity*detail.a);
}
#endif
