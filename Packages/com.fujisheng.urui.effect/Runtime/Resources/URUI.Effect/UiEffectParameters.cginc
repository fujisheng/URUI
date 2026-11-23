#ifndef URUI_EFFECT_PARAMETERS_INCLUDED
#define URUI_EFFECT_PARAMETERS_INCLUDED
// Fixed schema shared with UIEffectParameters.cs. Runtime rows precede CSS.
static float _UiFieldBase, _UiRuntimeBase, _UiTime, _UiTextureSlot;
static int _UiInputFlags;
static float4 _UiUvBounds, _UiTexel;
static float4 _UiRow0, _UiRow5, _UiRow7, _UiRow8, _UiRow28, _UiRow35, _UiRow36;

float UruiUiNumber(float offset){return UruiParameterTexel(_UiFieldBase+floor(offset/4))[(int)fmod(offset,4)];}
float4 UruiUiColor(float offset){return UruiParameterTexel(_UiFieldBase+offset/4);}
float4 UruiUiRuntime(float row){return UruiParameterTexel(_UiRuntimeBase+row);}

// Read a pair (value, autoFlag) stored at an even offset in a single parameter texel fetch.
float2 UruiUiNumberPair(float offset)
{
    float4 row = UruiParameterTexel(_UiFieldBase + floor(offset * 0.25));
    return fmod(offset, 4.0) < 1.5 ? row.xy : row.zw;
}

void UruiUiLoadVertexRows()
{
    _UiRow7 = UruiParameterTexel(_UiFieldBase + 7);
    _UiRow8 = UruiParameterTexel(_UiFieldBase + 8);
    _UiRow35 = UruiParameterTexel(_UiFieldBase + 35);
    _UiRow36 = UruiParameterTexel(_UiFieldBase + 36);
}

void UruiUiLoadFragmentRows(bool loadShadowRows)
{
    _UiRow0 = UruiParameterTexel(_UiFieldBase + 0);
    _UiRow5 = UruiParameterTexel(_UiFieldBase + 5);
    _UiRow7 = UruiParameterTexel(_UiFieldBase + 7);
    _UiRow8 = UruiParameterTexel(_UiFieldBase + 8);
    #if defined(URUI_COMPLEX_PIXEL)
    _UiRow28 = UruiParameterTexel(_UiFieldBase + 28);
    #else
    _UiRow28 = 0;
    #endif
    _UiRow36 = UruiParameterTexel(_UiFieldBase + 36);
    if (loadShadowRows)
    {
        _UiRow35 = UruiParameterTexel(_UiFieldBase + 35);
    }
    else
    {
        _UiRow35 = 0;
    }
}

#define _EffectTime _UiTime
#define _SourceUvBounds _UiUvBounds
#define _SourceTexel _UiTexel
#define _InputBottomLeft ((_UiInputFlags&1)!=0?1:0)
#define _InputPremultiplied ((_UiInputFlags&2)!=0?1:0)
#define _InputCaptured ((_UiInputFlags&4)!=0?1:0)
#define _ToneFilter (((int)floor(_UiRow0.x+.5)))
#define _ToneIntensity (saturate(_UiRow0.y))
#define _ColorFilter (((int)floor(_UiRow0.z+.5)))
#define _ColorIntensity (saturate(_UiRow0.w))
#define _ColorGlow (((int)floor(UruiUiNumber(8)+.5)))
#define _TargetMode (((int)floor(_UiRow5.z+.5)))
#define _TargetColor (UruiUiColor(24))
#define _TargetRange (saturate(_UiRow7.x))
#define _TargetSoftness (saturate(_UiRow7.y))
#define _SamplingFilter (((int)floor(_UiRow7.z+.5)))
#define _SamplingIntensity (saturate(_UiRow7.w))
#define _SamplingWidth (clamp(_UiRow8.x,.5,10))
#define _SamplingScale (clamp(_UiRow8.y,.01,100))
#define _AllowToModifyMeshShape (((int)floor(_UiRow8.z+.5)))
#define _TransitionFilter (((int)floor(_UiRow8.w+.5)))
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
#define _DetailFilter (((int)floor(_UiRow28.x+.5)))
#define _EdgeMode (((int)floor(_UiRow28.y+.5)))
#define _PatternArea (((int)floor(_UiRow28.z+.5)))
#define _GradationMode (((int)floor(_UiRow28.w+.5)))
#define _GradationColorFilter (((int)floor(UruiUiNumber(116)+.5)))
#define _GradationIntensity (saturate(UruiUiNumber(117)))
#define _GradationOffset (clamp(UruiUiNumber(118),-1,1))
#define _GradationScale (clamp(UruiUiNumber(119),.01,10))
#define _GradationRotation (clamp(UruiUiNumber(120),0,360))
#define _GradationColor1 (UruiUiColor(124))
#define _GradationColor2 (UruiUiColor(128))
#define _GradationColor3 (UruiUiColor(132))
#define _GradationColor4 (UruiUiColor(136))
#define _ShadowMode (((int)floor(_UiRow35.x+.5)))
#define _ShadowDistanceX (clamp(_UiRow35.y,-600,600))
#define _ShadowDistanceY (clamp(_UiRow35.z,-600,600))
#define _ShadowIteration (((int)floor(clamp(_UiRow35.w,1,5)+.5)))
#define _ShadowFade (saturate(_UiRow36.x))
#define _ShadowMirrorScale (clamp(_UiRow36.y,0,2))
#define _ShadowBlurIntensity (saturate(_UiRow36.z))
#define _Flip ((int)round(clamp(_UiRow36.w,0,15)))
#define _ShadowColorFilter (((int)floor(UruiUiNumber(148)+.5)))
#define _ShadowColorGlow (((int)floor(UruiUiNumber(156)+.5)))
#define _BlendType (UruiUiNumber(170))
#define _SrcBlendMode (UruiUiNumber(171))
#define _DstBlendMode (UruiUiNumber(172))
float4 UruiUiAdjustedColor(float baseOffset,int mode,float hue,float saturation,float value,float contrast,float brightness,float alpha)
{
    float4 color=UruiUiColor(baseOffset);
    if(mode==7)
    {
        float2 hp=UruiUiNumberPair(hue); if(hp.y==0)color.r=clamp(hp.x,-.5,.5);
        float2 sp=UruiUiNumberPair(saturation); if(sp.y==0)color.g=clamp(sp.x,-1,1);
        float2 vp=UruiUiNumberPair(value); if(vp.y==0)color.b=clamp(vp.x,-1,1);
    }
    else if(mode==8)
    {
        float2 cp=UruiUiNumberPair(contrast); if(cp.y==0)color.r=clamp(cp.x,-1,1);
        float2 bp=UruiUiNumberPair(brightness); if(bp.y==0)color.g=clamp(bp.x,-1,1);
    }
    float2 ap=UruiUiNumberPair(alpha); if(ap.y==0)color.a=ap.x;
    return color;
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
float4 UruiUiGradationSt(){float4 row29=UruiParameterTexel(_UiFieldBase+29);float inverse=1/clamp(row29.w,.01,10);float gradOffset=clamp(row29.z,-1,1);int mode=_GradationMode;bool gradient=mode==2||mode==4||mode==10;bool radial=mode==5||mode==6;float offset=gradient?-.5*(inverse+1)-gradOffset:radial?gradOffset:gradOffset*(inverse+1)/2-inverse*.5+.5;return float4(inverse,1,offset,0);}
#define _GradationTex_ST UruiUiGradationSt()
float2 UruiUiDistance(){float2 d=float2(_ShadowDistanceX,_ShadowDistanceY);int flip=_Flip;if((flip&8)!=0){if((flip&1)!=0)d.x=-d.x;if((flip&2)!=0)d.y=-d.y;}return d;}
float4 UruiUiExpansion(){if(_AllowToModifyMeshShape==0)return 0;int sf=_SamplingFilter;float4 e=sf==1?10:sf==2?15:sf==3?20:sf==5?float4(40,0,40,0):float4(0,0,0,0);int tf=_TransitionFilter;if(tf==6)e.w+=40;else if(tf==7)e.y+=40;int flip=_Flip;if((flip&4)==0){if((flip&1)!=0)e.xz=e.zx;if((flip&2)!=0)e.yw=e.wy;}return e;}
#endif
