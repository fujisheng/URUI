   sampler2D _MainTex, _PatternTex;
   float4 _ReferenceSize, _PatternSpeed, _Range, _BandColor, _AddColor;
   float _FilterMode, _ColorBlend, _Rotation, _Rate, _Speed, _Width, _Softness, _BandSourceAlpha, _AddColorIntensity, _AddColorGlow, _Saturation, _Brightness;
   float3 BlendColor(float3 source, float strength, float sourceWeight)
   {
    float3 bandSource=source*sourceWeight;
    float3 target = _ColorBlend < .5 ? bandSource * _BandColor.rgb
      : _ColorBlend < 1.5 ? _BandColor.rgb
      : _ColorBlend < 2.5 ? bandSource * (1 + _BandColor.rgb) : bandSource + _BandColor.rgb;
    return lerp(source, target, strength * _BandColor.a);
   }

float2 ComputeTextureFilterLocal(float2 uv, float2 referenceSize)
{
    float angle=radians(_Rotation), s=sin(angle), c=cos(angle);
    float2 centered=(uv-0.5)*referenceSize/max(referenceSize.x,referenceSize.y);
    return 0.5+mul(float2x2(c,-s,s,c),centered)/max(abs(c-s),abs(c+s));
}

float4 ApplyTextureFilterCore(float4 color, float2 local, float time, float rate, float saturation, float brightness, bool premultipliedOut)
{
    if (_FilterMode<1.5)
    {
     float2 puv=local*lerp(100,1,_Width)+float2(-rate*2,0)+time*_PatternSpeed.xy;
     float alpha=tex2D(_PatternTex,puv).a;
     float ramp=min(saturate((local.x-_Range.x)/max(0.001,_Range.y-_Range.x)),0.995);
     float mask=step(1-alpha,ramp);
     color.rgb=BlendColor(color.rgb,mask,1);
    }
    else
    {
     float alpha=tex2D(_PatternTex,local).a;
     float factor=alpha-rate*(1+_Width)+_Width;
     float soft=max(0.0001,_Width*_Softness);
     float band=saturate((_Width-factor)/soft)*saturate(factor*2/soft);
     color.rgb=BlendColor(color.rgb,band,lerp(1,color.a,saturate(_BandSourceAlpha)));
    }
    float glow=saturate(_AddColorGlow), intensity=saturate(_AddColorIntensity);
    color.rgb += _AddColor.rgb * lerp(color.a,1,glow) * intensity;
    // The document consumes straight alpha. Preserve emission when coverage drops;
    // a small positive coverage also represents fully additive light without NaN/Inf.
    float coverage=max(1.0/4096.0,1-glow*intensity);
    float origAlpha=color.a;
    if (!premultipliedOut)
    {
     color.rgb/=coverage;
    }
    color.a=origAlpha*coverage;
    color.rgb=lerp(dot(color.rgb,float3(.3,.59,.11)).xxx,color.rgb,saturate(saturation));
    color.rgb*=max(0,brightness);
    if (premultipliedOut)
    {
     // Dividing by coverage then multiplying by (origAlpha * coverage) cancels coverage algebraically.
     color.rgb*=origAlpha;
    }
    return color;
}

float4 ApplyTextureFilter(float4 color,float2 uv,float time,float2 referenceSize,float inputRate,float saturation,float brightness)
{
    float2 local=ComputeTextureFilterLocal(uv,referenceSize);
    float rate=frac(_Speed*time+inputRate*0.9999);
    return ApplyTextureFilterCore(color,local,time,rate,saturation,brightness,false);
}

float4 ApplyTextureFilter(float4 color,float2 uv,float time)
{return ApplyTextureFilter(color,uv,time,_ReferenceSize.xy,_Rate,_Saturation,_Brightness);}
