// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_TRANSITION_INCLUDED
#define URUI_EFFECT_TRANSITION_INCLUDED
sampler2D _TransitionTex;
float UruiTransitionRate() { return frac(_TransitionAutoPlaySpeed * _EffectTime + _TransitionRate * .9999); }
float UruiTransitionAlpha(float2 local)
{
    int filter=_TransitionFilter;
    if (filter == 0) return 1;
    float4 row14=UruiParameterTexel(_UiFieldBase+14);
    float4 row15=UruiParameterTexel(_UiFieldBase+15);
    float2 texScale=row14.zw;
    float2 texOffset=row15.xy;
    float2 texSpeed=row15.zw;
    float2 uv;
    if (filter == 8)
    {
        float4 row9=UruiParameterTexel(_UiFieldBase+9);
        float autoPlay=clamp(UruiParameterTexel(_UiFieldBase+10).z,-5,5);
        float rate=frac(autoPlay*_EffectTime+saturate(row9.x)*.9999);
        half scale = lerp(100,1,saturate(row9.y));
        half2 time = half2(-rate*2,0);
        uv=local*texScale*scale+texOffset+time;
    }
    else { uv=local*texScale+texOffset; }
    float alpha=tex2D(_TransitionTex,uv+_EffectTime*texSpeed).a;
    return _TransitionReverse ? 1-alpha : alpha;
}
float2 UruiTransitionMove(float alpha)
{
    int filter=_TransitionFilter;
    if (filter != 6 && filter != 7) return 0;
    float4 row9=UruiParameterTexel(_UiFieldBase+9);
    float autoPlay=clamp(UruiParameterTexel(_UiFieldBase+10).z,-5,5);
    float rate=frac(autoPlay*_EffectTime+saturate(row9.x)*.9999);
    float width=saturate(row9.y);
    float factor=alpha-rate*(1+width*1.5)+width;
    float band=max(0,width-factor);
    // URUI 源 UV 向下；原纹理 UV 向上，Melt/Burn 位移反向映射。
    float direction=_InputBottomLeft>.5 ? -1 : 1;
    return float2(0,(filter==6 ? 1 : -1)*direction*band*band*(_SourceUvBounds.w-_SourceUvBounds.y)/max(.01,width));
}
half4 UruiTransition(half4 color, half alpha, float2 local, float edgeFactor)
{
    int filter=_TransitionFilter;
    float4 row9=UruiParameterTexel(_UiFieldBase+9);
    float4 row10=UruiParameterTexel(_UiFieldBase+10);
    float rate=frac(clamp(row10.z,-5,5)*_EffectTime+saturate(row9.x)*.9999);
    if (filter==1) { color*=saturate(alpha+1-rate*2); }
    else if (filter==2) { color*=step(.001,alpha-rate); }
    else if (filter==8)
    {
        float4 row13=UruiParameterTexel(_UiFieldBase+13);
        int patternReverse=(int)floor(row13.x+.5);
        int colorGlow=(int)floor(row13.y+.5);
        int colorFilter=_TransitionColorFilter;
        float4 tc=UruiUiAdjustedColor(100,colorFilter,74,76,78,80,82,84);
        half4 pattern=UruiColor(half4(color.rgb,1),half4(tc.rgb*color.a,1),tc.a,colorFilter,colorGlow,false);
        float isPattern=min(UruiInverseRange(saturate(row10.x),saturate(row10.y),local.x),.995)<(patternReverse ? alpha : 1-alpha);
        isPattern=patternReverse ? isPattern : 1-isPattern;
        int pArea=_PatternArea;
        float area=pArea==0 ? 1 : pArea==1 ? 1-edgeFactor : edgeFactor;
        color.rgb=lerp(color.rgb,pattern.rgb,area*isPattern);
    }
    else if (filter>=3 && filter<=7)
    {
        float width=saturate(row9.y);
        float softnessVal=saturate(row9.z);
        float factor=alpha-rate*(1+width)+width;
        float softness=max(.0001,width*softnessVal);
        half band=saturate((width-factor)/softness);
        half soft=saturate(factor*2/softness);
        int colorFilter=_TransitionColorFilter;
        float4 tc=UruiUiAdjustedColor(100,colorFilter,74,76,78,80,82,84);
        half4 bandColor=UruiColor(half4(color.rgb,1),half4(tc.rgb,1),tc.a,colorFilter,_TransitionColorGlow,false);
        bandColor*=color.a;
        if (filter==6) return lerp(color,bandColor,band);
        if (filter==7)
        {
            color=lerp(color,bandColor,band*1.25);
            color.a*=1-UruiInverseRange(.85,1,band*1.25);
            color.rgb*=(1-UruiInverseRange(.85,1,band*1.3))*color.a;
            return color;
        }
        color=lerp(color,bandColor,band*soft);
        if (filter==3) color*=soft;
        else if (filter==5) color*=band*soft;
    }
    return color;
}
#endif
