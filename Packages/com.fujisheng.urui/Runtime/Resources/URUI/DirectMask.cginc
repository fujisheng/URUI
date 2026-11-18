#ifndef URUI_DIRECT_MASK_INCLUDED
#define URUI_DIRECT_MASK_INCLUDED
Texture2D _UruiMaskTexture;
SamplerState sampler_UruiMaskTexture;
float4 _UruiMaskSource;

float UruiDirectMask(float3 pixel, float offset)
{
    float2 window=pixel.xy/pixel.z;
    float4 local=UruiParameterTexel(offset)*window.x+UruiParameterTexel(offset+1)*window.y+UruiParameterTexel(offset+3);
    if(abs(local.w)<1e-8)return 0;
    float2 coordinate=local.xy/local.w;
    float4 rectangle=UruiParameterTexel(offset+4);
    float4 domain=UruiParameterTexel(offset+7);
    float coverage=step(rectangle.x,coordinate.x)*step(coordinate.x,rectangle.z)*step(rectangle.y,coordinate.y)*step(coordinate.y,rectangle.w);
    coverage*=step(domain.x,window.x)*step(window.x,domain.z)*step(domain.y,window.y)*step(window.y,domain.w);
    float4 u=UruiParameterTexel(offset+5),v=UruiParameterTexel(offset+6);
    float2 uv=float2(dot(u.xyz,float3(coordinate,1)),dot(v.xyz,float3(coordinate,1)));
    int flags=(int)u.w;
    float alpha;
    if((flags&8)!=0)
    {
        float4 feather=UruiParameterTexel(offset+2);float2 localPixel=coordinate*u.xy;
        float4 distance=float4(localPixel.x,localPixel.y,u.x-localPixel.x,u.y-localPixel.y);
        float4 ramp=lerp(saturate(distance/max(feather,1e-20)),1,step(feather,0));alpha=ramp.x*ramp.y*ramp.z*ramp.w;
    }
    else alpha=(flags&4)!=0?1:UruiSampleTexture(_UruiMaskTexture,sampler_UruiMaskTexture,_UruiMaskSource.xy,_UruiMaskSource.z,uv,flags&3).a;
    return alpha*v.w*coverage;
}
#endif
