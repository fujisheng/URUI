// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_TRANSITION_INCLUDED
#define URUI_EFFECT_TRANSITION_INCLUDED
sampler2D _TransitionTex;
float UruiTransitionRate() { return frac(_TransitionAutoPlaySpeed * _EffectTime + _TransitionRate * .9999); }
float UruiTransitionAlpha(float2 local)
{
    if (_TransitionFilter == 0) return 1;
    float2 uv;
    if (_TransitionFilter == 8)
    {
        half scale = lerp(100,1,_TransitionWidth);
        half2 time = half2(-UruiTransitionRate()*2,0);
        uv=local*_TransitionTex_ST.xy*scale+_TransitionTex_ST.zw+time;
    }
    else { uv=local*_TransitionTex_ST.xy+_TransitionTex_ST.zw; }
    float alpha=tex2D(_TransitionTex,uv+_EffectTime*_TransitionTex_Speed).a;
    return _TransitionReverse ? 1-alpha : alpha;
}
float2 UruiTransitionMove(float alpha)
{
    if (_TransitionFilter != 6 && _TransitionFilter != 7) return 0;
    float factor=alpha-UruiTransitionRate()*(1+_TransitionWidth*1.5)+_TransitionWidth;
    float band=max(0,_TransitionWidth-factor);
    // URUI 源 UV 向下；原纹理 UV 向上，Melt/Burn 位移反向映射。
    float direction=_InputBottomLeft>.5 ? -1 : 1;
    return float2(0,(_TransitionFilter==6 ? 1 : -1)*direction*band*band*(_SourceUvBounds.w-_SourceUvBounds.y)/max(.01,_TransitionWidth));
}
half4 UruiTransition(half4 color, half alpha, float2 local, float edgeFactor)
{
    float rate=UruiTransitionRate();
    if (_TransitionFilter==1) { color*=saturate(alpha+1-rate*2); }
    else if (_TransitionFilter==2) { color*=step(.001,alpha-rate); }
    else if (_TransitionFilter==8)
    {
        half4 pattern=UruiColor(half4(color.rgb,1),half4(_TransitionColor.rgb*color.a,1),_TransitionColor.a,_TransitionColorFilter,_TransitionColorGlow,false);
        float isPattern=min(UruiInverseRange(_TransitionRange.x,_TransitionRange.y,local.x),.995)<(_TransitionPatternReverse ? alpha : 1-alpha);
        isPattern=_TransitionPatternReverse ? isPattern : 1-isPattern;
        float area=_PatternArea==0 ? 1 : _PatternArea==1 ? 1-edgeFactor : edgeFactor;
        color.rgb=lerp(color.rgb,pattern.rgb,area*isPattern);
    }
    else if (_TransitionFilter>=3 && _TransitionFilter<=7)
    {
        float factor=alpha-rate*(1+_TransitionWidth)+_TransitionWidth;
        float softness=max(.0001,_TransitionWidth*_TransitionSoftness);
        half band=saturate((_TransitionWidth-factor)/softness);
        half soft=saturate(factor*2/softness);
        half4 bandColor=UruiColor(half4(color.rgb,1),half4(_TransitionColor.rgb,1),_TransitionColor.a,_TransitionColorFilter,_TransitionColorGlow,false);
        bandColor*=color.a;
        if (_TransitionFilter==6) return lerp(color,bandColor,band);
        if (_TransitionFilter==7)
        {
            color=lerp(color,bandColor,band*1.25);
            color.a*=1-UruiInverseRange(.85,1,band*1.25);
            color.rgb*=(1-UruiInverseRange(.85,1,band*1.3))*color.a;
            return color;
        }
        color=lerp(color,bandColor,band*soft);
        if (_TransitionFilter==3) color*=soft;
        else if (_TransitionFilter==5) color*=band*soft;
    }
    return color;
}
#endif
