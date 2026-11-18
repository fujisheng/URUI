// UIEffect 5.8.4 派生算法；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_EDGE_INCLUDED
#define URUI_EFFECT_EDGE_INCLUDED
float UruiEdgeFactor(float2 uv)
{
    if (_EdgeMode==0) return 0;
    float2 d=UruiSamplingTexel()*lerp(1,20,_EdgeWidth);
    float minimum=1;
    const float2 directions[12]={float2(1,0),float2(.866025,.5),float2(.5,.866025),float2(0,1),float2(-.5,.866025),float2(-.866025,.5),
        float2(-1,0),float2(-.866025,-.5),float2(-.5,-.866025),float2(0,-1),float2(.5,-.866025),float2(.866025,-.5)};
    [unroll] for(int i=0;i<12;i++) { minimum=min(minimum,UruiSampleClamped(uv+d*directions[i]).a); }
    return 1-UruiInverseRange(.15,.3,minimum);
}
half4 UruiEdge(half4 color,float edgeFactor,float2 local)
{
    if (_EdgeMode==0) return color;
    half4 edgeColor=UruiColor(color,_EdgeColor,1,_EdgeColorFilter,_EdgeColorGlow,false);
    float shiny=1;
    if (_EdgeMode==2)
    {
        float angle=atan2(local.y-.5,local.x-.5)/UNITY_PI;
        shiny=frac(_EdgeShinyRate+_EffectTime*_EdgeShinyAutoPlaySpeed+angle)<_EdgeShinyWidth;
    }
    return lerp(color,edgeColor,edgeFactor*shiny);
}
#endif
