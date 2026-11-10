// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_GRADATION_INCLUDED
#define URUI_EFFECT_GRADATION_INCLUDED
sampler2D _GradationTex;
float4 _GradationTex_ST, _GradationColor1, _GradationColor2, _GradationColor3, _GradationColor4;
int _GradationMode, _GradationColorFilter;
float _GradationIntensity;
half4 UruiGradation(half4 color,float2 coordinate)
{
    if(_GradationMode==0)return color;
    float2 uv=coordinate*_GradationTex_ST.x+_GradationTex_ST.z;
    half4 factor;
    if(_GradationMode==2 || _GradationMode==4 || _GradationMode==10) factor=tex2D(_GradationTex,uv);
    else if(_GradationMode==5 || _GradationMode==6)
    {
        float t=saturate(length((coordinate-float2(.5,.5))*2*_GradationTex_ST.x)+_GradationTex_ST.z);
        factor=lerp(_GradationColor1,_GradationColor2,t);
    }
    else if(_GradationMode==11) factor=lerp(lerp(_GradationColor3,_GradationColor4,saturate(uv.x)),lerp(_GradationColor1,_GradationColor2,saturate(uv.x)),saturate(uv.y));
    else factor=lerp(_GradationColor1,_GradationColor2,saturate(uv.x));
    return UruiColor(color,factor,_GradationIntensity,_GradationColorFilter,0,true);
}
#endif
