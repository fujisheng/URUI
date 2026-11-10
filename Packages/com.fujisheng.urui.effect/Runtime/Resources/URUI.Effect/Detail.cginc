// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_DETAIL_INCLUDED
#define URUI_EFFECT_DETAIL_INCLUDED
sampler2D _DetailTex;
float4 _DetailTex_ST, _DetailColor;
float2 _DetailTex_Speed, _DetailThreshold;
float _DetailIntensity;
int _DetailFilter;
half4 UruiDetail(half4 color,float2 local)
{
    if (_DetailFilter==0) return color;
    half4 original=color;
    float2 uv=local*_DetailTex_ST.xy+_DetailTex_ST.zw+_EffectTime*_DetailTex_Speed;
    half4 detail=tex2D(_DetailTex,uv);
    if (_DetailFilter==1) { color.a*=UruiInverseRange(_DetailThreshold.x,_DetailThreshold.y,detail.a); return color; }
    detail*=_DetailColor;
    if (_DetailFilter==2) color.rgb*=detail.rgb;
    else if (_DetailFilter==3) color.rgb+=detail.rgb*color.a;
    else if (_DetailFilter==6) color.rgb-=detail.rgb*color.a;
    else if (_DetailFilter==4) color.rgb=detail.rgb*color.a;
    else if (_DetailFilter==5) color.rgb*=1+detail.rgb;
    return lerp(original,color,_DetailIntensity*detail.a);
}
#endif
