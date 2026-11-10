#ifndef URUI_PREMULTIPLIED_PALETTE_INCLUDED
#define URUI_PREMULTIPLIED_PALETTE_INCLUDED

Texture2D _UruiTexture1;
SamplerState sampler_UruiTexture1;
float4 _UruiSource1;
Texture2D _UruiTexture2;
SamplerState sampler_UruiTexture2;
float4 _UruiSource2;
Texture2D _UruiTexture3;
SamplerState sampler_UruiTexture3;
float4 _UruiSource3;
Texture2D _UruiTexture4;
SamplerState sampler_UruiTexture4;
float4 _UruiSource4;
Texture2D _UruiTexture5;
SamplerState sampler_UruiTexture5;
float4 _UruiSource5;
Texture2D _UruiTexture6;
SamplerState sampler_UruiTexture6;
float4 _UruiSource6;
Texture2D _UruiTexture7;
SamplerState sampler_UruiTexture7;
float4 _UruiSource7;

float4 UruiSamplePalette(float2 uv, float slot)
{
    if (slot < 0.5) return UruiSampleMainTexture(uv);
    if (slot < 1.5) return UruiSampleTexture(_UruiTexture1, sampler_UruiTexture1, _UruiSource1.xy, _UruiSource1.z, uv);
    if (slot < 2.5) return UruiSampleTexture(_UruiTexture2, sampler_UruiTexture2, _UruiSource2.xy, _UruiSource2.z, uv);
    if (slot < 3.5) return UruiSampleTexture(_UruiTexture3, sampler_UruiTexture3, _UruiSource3.xy, _UruiSource3.z, uv);
    if (slot < 4.5) return UruiSampleTexture(_UruiTexture4, sampler_UruiTexture4, _UruiSource4.xy, _UruiSource4.z, uv);
    if (slot < 5.5) return UruiSampleTexture(_UruiTexture5, sampler_UruiTexture5, _UruiSource5.xy, _UruiSource5.z, uv);
    if (slot < 6.5) return UruiSampleTexture(_UruiTexture6, sampler_UruiTexture6, _UruiSource6.xy, _UruiSource6.z, uv);
    return UruiSampleTexture(_UruiTexture7, sampler_UruiTexture7, _UruiSource7.xy, _UruiSource7.z, uv);
}

#endif
