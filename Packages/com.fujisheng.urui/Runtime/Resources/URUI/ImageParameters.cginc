#ifndef URUI_IMAGE_PARAMETERS_INCLUDED
#define URUI_IMAGE_PARAMETERS_INCLUDED
#include "RenderParameters.cginc"
sampler2D _UruiTexture1,_UruiTexture2,_UruiTexture3,_UruiTexture4,_UruiTexture5,_UruiTexture6,_UruiTexture7;
float4 UruiImageSample(float2 uv,float slot)
{
    if(slot<.5)return tex2D(_MainTex,uv);
    if(slot<1.5)return tex2D(_UruiTexture1,uv);
    if(slot<2.5)return tex2D(_UruiTexture2,uv);
    if(slot<3.5)return tex2D(_UruiTexture3,uv);
    if(slot<4.5)return tex2D(_UruiTexture4,uv);
    if(slot<5.5)return tex2D(_UruiTexture5,uv);
    if(slot<6.5)return tex2D(_UruiTexture6,uv);
    return tex2D(_UruiTexture7,uv);
}
float4 UruiImageSampleLevel(float2 uv,float slot)
{
    if(slot<.5)return tex2Dlod(_MainTex,float4(uv,0,0));
    if(slot<1.5)return tex2Dlod(_UruiTexture1,float4(uv,0,0));
    if(slot<2.5)return tex2Dlod(_UruiTexture2,float4(uv,0,0));
    if(slot<3.5)return tex2Dlod(_UruiTexture3,float4(uv,0,0));
    if(slot<4.5)return tex2Dlod(_UruiTexture4,float4(uv,0,0));
    if(slot<5.5)return tex2Dlod(_UruiTexture5,float4(uv,0,0));
    if(slot<6.5)return tex2Dlod(_UruiTexture6,float4(uv,0,0));
    return tex2Dlod(_UruiTexture7,float4(uv,0,0));
}
struct UruiImageInput {float4 vertex:POSITION;float2 uv:TEXCOORD0;float2 source:TEXCOORD1;float4 color:COLOR;};
struct UruiImageOutput
{
    float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 pixel:TEXCOORD1;
    float2 effectUv:TEXCOORD2;float4 color:COLOR;
    nointerpolation float4 clipRect:TEXCOORD3;
    nointerpolation float4 state:TEXCOORD4;
    nointerpolation float4 runtime:TEXCOORD5;
};
UruiImageOutput UruiImageVertex(UruiImageInput input)
{
    UruiImageOutput output;
    int2 baseCoord=UruiParameterBase(input.source.y);
    float4 metadata=UruiParameterAt(baseCoord,7);
    output.pixel=UruiParameterAt(baseCoord,0)*input.vertex.x+UruiParameterAt(baseCoord,1)*input.vertex.y+UruiParameterAt(baseCoord,3)*input.vertex.w;
    if(input.vertex.z!=0)output.pixel+=UruiParameterAt(baseCoord,2)*input.vertex.z;
    output.vertex=mul(UNITY_MATRIX_VP,output.pixel);
    float4 transform=UruiParameterAt(baseCoord,6);
    output.uv=input.uv*transform.xy+transform.zw;
    output.color=input.color*UruiParameterAt(baseCoord,5);
    output.clipRect=(((int)metadata.x)&1)!=0?UruiParameterAt(baseCoord,4):0;
    output.state=float4(metadata.x,input.source.x,metadata.z,0);
    float4 bounds=UruiParameterTexel(metadata.z);
    output.effectUv=(input.vertex.xy-bounds.xy)*bounds.zw;
    output.runtime=UruiParameterTexel(metadata.z+1);
    output.runtime.z=output.runtime.z<0?0:UruiParameterTexel(output.runtime.z).x;
    return output;
}
#endif
