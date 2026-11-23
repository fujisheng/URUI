// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_GRADATION_INCLUDED
#define URUI_EFFECT_GRADATION_INCLUDED
sampler2D _GradationTex;
half4 UruiGradation(half4 color,float2 coordinate)
{
    int mode=_GradationMode;
    if(mode==0)return color;
    float4 row29=UruiParameterTexel(_UiFieldBase+29);
    int colorFilter=(int)floor(row29.x+.5);
    float intensity=saturate(row29.y);
    float inverse=1/clamp(row29.w,.01,10);
    float gradOffset=clamp(row29.z,-1,1);
    bool gradient=mode==2||mode==4||mode==10;
    bool radial=mode==5||mode==6;
    float offset=gradient?-.5*(inverse+1)-gradOffset:radial?gradOffset:gradOffset*(inverse+1)/2-inverse*.5+.5;
    float2 uv=coordinate*inverse+offset;
    half4 factor;
    if(gradient) factor=tex2D(_GradationTex,uv);
    else if(radial)
    {
        float t=saturate(length((coordinate-float2(.5,.5))*2*inverse)+offset);
        factor=lerp(_GradationColor1,_GradationColor2,t);
    }
    else if(mode==11) factor=lerp(lerp(_GradationColor3,_GradationColor4,saturate(uv.x)),lerp(_GradationColor1,_GradationColor2,saturate(uv.x)),saturate(uv.y));
    else factor=lerp(_GradationColor1,_GradationColor2,saturate(uv.x));
    return UruiColor(color,factor,intensity,colorFilter,0,true);
}
#endif
