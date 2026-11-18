#ifndef URUI_EFFECT_PARAMETERS_INCLUDED
#define URUI_EFFECT_PARAMETERS_INCLUDED
// Fixed schema shared with UiEffectParameters.cs. Runtime rows precede CSS.
static float _UiFieldBase, _UiRuntimeBase, _UiTime, _UiTextureSlot;
static int _UiInputFlags;
static float4 _UiUvBounds, _UiTexel;
float UruiUiNumber(float offset){return UruiParameterTexel(_UiFieldBase+floor(offset/4))[(int)fmod(offset,4)];}
float4 UruiUiColor(float offset){return UruiParameterTexel(_UiFieldBase+offset/4);}
float4 UruiUiRuntime(float row){return UruiParameterTexel(_UiRuntimeBase+row);}
#define _EffectTime _UiTime
#define _SourceUvBounds _UiUvBounds
#define _SourceTexel _UiTexel
#define _InputBottomLeft ((_UiInputFlags&1)!=0?1:0)
#define _InputPremultiplied ((_UiInputFlags&2)!=0?1:0)
#define _InputCaptured ((_UiInputFlags&4)!=0?1:0)
#define _ToneFilter (((int)floor(UruiUiNumber(0)+.5)))
#define _ToneIntensity (saturate(UruiUiNumber(1)))
#define _ColorFilter (((int)floor(UruiUiNumber(2)+.5)))
#define _ColorIntensity (saturate(UruiUiNumber(3)))
#define _ColorGlow (((int)floor(UruiUiNumber(8)+.5)))
#define _TargetMode (((int)floor(UruiUiNumber(22)+.5)))
#define _TargetColor (UruiUiColor(24))
#define _TargetRange (saturate(UruiUiNumber(28)))
#define _TargetSoftness (saturate(UruiUiNumber(29)))
#define _SamplingFilter (((int)floor(UruiUiNumber(30)+.5)))
#define _SamplingIntensity (saturate(UruiUiNumber(31)))
#define _SamplingWidth (clamp(UruiUiNumber(32),.5,10))
#define _SamplingScale (clamp(UruiUiNumber(33),.01,100))
#define _AllowToModifyMeshShape (((int)floor(UruiUiNumber(34)+.5)))
#define _TransitionFilter (((int)floor(UruiUiNumber(35)+.5)))
#define _TransitionRate (saturate(UruiUiNumber(36)))
#define _TransitionWidth (saturate(UruiUiNumber(37)))
#define _TransitionSoftness (saturate(UruiUiNumber(38)))
#define _TransitionRotation (clamp(UruiUiNumber(39),0,360))
#define _TransitionRangeMin (saturate(UruiUiNumber(40)))
#define _TransitionRangeMax (saturate(UruiUiNumber(41)))
#define _TransitionAutoPlaySpeed (clamp(UruiUiNumber(42),-5,5))
#define _DetailIntensity (saturate(UruiUiNumber(43)))
#define _DetailThresholdMin (saturate(UruiUiNumber(44)))
#define _DetailThresholdMax (saturate(UruiUiNumber(45)))
#define _EdgeWidth (saturate(UruiUiNumber(46)))
#define _EdgeShinyRate (saturate(UruiUiNumber(47)))
#define _EdgeShinyWidth (saturate(UruiUiNumber(48)))
#define _EdgeShinyAutoPlaySpeed (clamp(UruiUiNumber(49),-5,5))
#define _TransitionReverse (((int)floor(UruiUiNumber(50)+.5)))
#define _TransitionKeepAspectRatio (((int)floor(UruiUiNumber(51)+.5)))
#define _TransitionPatternReverse (((int)floor(UruiUiNumber(52)+.5)))
#define _TransitionColorGlow (((int)floor(UruiUiNumber(53)+.5)))
#define _EdgeColorGlow (((int)floor(UruiUiNumber(54)+.5)))
#define _TransitionTextureScaleX (UruiUiNumber(58))
#define _TransitionTextureScaleY (UruiUiNumber(59))
#define _TransitionTextureOffsetX (UruiUiNumber(60))
#define _TransitionTextureOffsetY (UruiUiNumber(61))
#define _TransitionTextureSpeedX (UruiUiNumber(62))
#define _TransitionTextureSpeedY (UruiUiNumber(63))
#define _DetailTextureScaleX (UruiUiNumber(66))
#define _DetailTextureScaleY (UruiUiNumber(67))
#define _DetailTextureOffsetX (UruiUiNumber(68))
#define _DetailTextureOffsetY (UruiUiNumber(69))
#define _DetailTextureSpeedX (UruiUiNumber(70))
#define _DetailTextureSpeedY (UruiUiNumber(71))
#define _TransitionColorFilter (((int)floor(UruiUiNumber(72)+.5)))
#define _EdgeColorFilter (((int)floor(UruiUiNumber(86)+.5)))
#define _DetailColor (UruiUiColor(104))
#define _DetailFilter (((int)floor(UruiUiNumber(112)+.5)))
#define _EdgeMode (((int)floor(UruiUiNumber(113)+.5)))
#define _PatternArea (((int)floor(UruiUiNumber(114)+.5)))
#define _GradationMode (((int)floor(UruiUiNumber(115)+.5)))
#define _GradationColorFilter (((int)floor(UruiUiNumber(116)+.5)))
#define _GradationIntensity (saturate(UruiUiNumber(117)))
#define _GradationOffset (clamp(UruiUiNumber(118),-1,1))
#define _GradationScale (clamp(UruiUiNumber(119),.01,10))
#define _GradationRotation (clamp(UruiUiNumber(120),0,360))
#define _GradationColor1 (UruiUiColor(124))
#define _GradationColor2 (UruiUiColor(128))
#define _GradationColor3 (UruiUiColor(132))
#define _GradationColor4 (UruiUiColor(136))
#define _ShadowMode (((int)floor(UruiUiNumber(140)+.5)))
#define _ShadowDistanceX (clamp(UruiUiNumber(141),-600,600))
#define _ShadowDistanceY (clamp(UruiUiNumber(142),-600,600))
#define _ShadowIteration (((int)floor(clamp(UruiUiNumber(143),1,5)+.5)))
#define _ShadowFade (saturate(UruiUiNumber(144)))
#define _ShadowMirrorScale (clamp(UruiUiNumber(145),0,2))
#define _ShadowBlurIntensity (saturate(UruiUiNumber(146)))
#define _Flip ((int)round(clamp(UruiUiNumber(147),0,15)))
#define _ShadowColorFilter (((int)floor(UruiUiNumber(148)+.5)))
#define _ShadowColorGlow (((int)floor(UruiUiNumber(156)+.5)))
#define _BlendType (UruiUiNumber(170))
#define _SrcBlendMode (UruiUiNumber(171))
#define _DstBlendMode (UruiUiNumber(172))
float4 UruiUiAdjustedColor(float baseOffset,int mode,float hue,float saturation,float value,float contrast,float brightness,float alpha)
{
    float4 color=UruiUiColor(baseOffset);
    if(mode==7){if(UruiUiNumber(hue+1)==0)color.r=clamp(UruiUiNumber(hue),-.5,.5);if(UruiUiNumber(saturation+1)==0)color.g=clamp(UruiUiNumber(saturation),-1,1);if(UruiUiNumber(value+1)==0)color.b=clamp(UruiUiNumber(value),-1,1);}
    else if(mode==8){if(UruiUiNumber(contrast+1)==0)color.r=clamp(UruiUiNumber(contrast),-1,1);if(UruiUiNumber(brightness+1)==0)color.g=clamp(UruiUiNumber(brightness),-1,1);}
    if(UruiUiNumber(alpha+1)==0)color.a=UruiUiNumber(alpha);return color;
}
#define _ColorValue (UruiUiAdjustedColor(4,_ColorFilter,10,12,14,16,18,20))
#define _TransitionColor (UruiUiAdjustedColor(100,_TransitionColorFilter,74,76,78,80,82,84))
#define _EdgeColor (UruiUiAdjustedColor(108,_EdgeColorFilter,88,90,92,94,96,98))
#define _ShadowColor (UruiUiAdjustedColor(152,_ShadowColorFilter,158,160,162,164,166,168))
#define _TransitionTex_ST float4(_TransitionTextureScaleX,_TransitionTextureScaleY,_TransitionTextureOffsetX,_TransitionTextureOffsetY)
#define _TransitionTex_Speed float2(_TransitionTextureSpeedX,_TransitionTextureSpeedY)
#define _DetailTex_ST float4(_DetailTextureScaleX,_DetailTextureScaleY,_DetailTextureOffsetX,_DetailTextureOffsetY)
#define _DetailTex_Speed float2(_DetailTextureSpeedX,_DetailTextureSpeedY)
#define _TransitionRange float2(_TransitionRangeMin,_TransitionRangeMax)
#define _DetailThreshold float2(_DetailThresholdMin,_DetailThresholdMax)
float4 UruiUiGradationSt(){float inverse=1/_GradationScale;bool gradient=_GradationMode==2||_GradationMode==4||_GradationMode==10;bool radial=_GradationMode==5||_GradationMode==6;float offset=gradient?-.5*(inverse+1)-_GradationOffset:radial?_GradationOffset:_GradationOffset*(inverse+1)/2-inverse*.5+.5;return float4(inverse,1,offset,0);}
#define _GradationTex_ST UruiUiGradationSt()
float2 UruiUiDistance(){float2 d=float2(_ShadowDistanceX,_ShadowDistanceY);if((_Flip&8)!=0){if((_Flip&1)!=0)d.x=-d.x;if((_Flip&2)!=0)d.y=-d.y;}return d;}
float4 UruiUiExpansion(){if(_AllowToModifyMeshShape==0)return 0;float4 e=_SamplingFilter==1?10:_SamplingFilter==2?15:_SamplingFilter==3?20:_SamplingFilter==5?float4(40,0,40,0):float4(0,0,0,0);if(_TransitionFilter==6)e.w+=40;else if(_TransitionFilter==7)e.y+=40;if((_Flip&4)==0){if((_Flip&1)!=0)e.xz=e.zx;if((_Flip&2)!=0)e.yw=e.wy;}return e;}
#endif
