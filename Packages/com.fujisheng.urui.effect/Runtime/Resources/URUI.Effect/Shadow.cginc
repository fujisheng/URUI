// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_SHADOW_INCLUDED
#define URUI_EFFECT_SHADOW_INCLUDED
int _ShadowColorFilter, _ShadowColorGlow;
float4 _ShadowColor;
float _ShadowBlurIntensity;
float4 UruiShadow(float4 color)
{ return UruiColor(color,_ShadowColor,1,_ShadowColorFilter,_ShadowColorGlow,false); }
#endif
