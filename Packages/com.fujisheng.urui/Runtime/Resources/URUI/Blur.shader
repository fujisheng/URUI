Shader "URUI/Blur" {
 CGINCLUDE
#include "UnityCG.cginc"
 sampler2D _InputTex;
 struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
 Output Vertex(appdata_img input){Output o;o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o;}
 float4 SampleH(float2 uv){return (uv.x>=0&&uv.x<=1)?tex2Dlod(_InputTex,float4(uv,0,0)):0;}
 float4 SampleV(float2 uv){return (uv.y>=0&&uv.y<=1)?tex2Dlod(_InputTex,float4(uv,0,0)):0;}
 float SampleHAlpha(float2 uv){return (uv.x>=0&&uv.x<=1)?tex2Dlod(_InputTex,float4(uv,0,0)).a:0;}
 float SampleVAlpha(float2 uv){return (uv.y>=0&&uv.y<=1)?tex2Dlod(_InputTex,float4(uv,0,0)).a:0;}

 // _Blur: x=precomputed tap count (<=64), y=1/width, z=1/height, w=normalized center weight.
 float4 _Blur;
 float4 _BlurTaps[32];
 float4 Horizontal(Output input):SV_Target {
  float2 uv=input.uv;
  int tapCount=(int)(_Blur.x+.5);
  if(tapCount<=0)return tex2Dlod(_InputTex,float4(uv,0,0));
  float4 sum=tex2Dlod(_InputTex,float4(uv,0,0))*_Blur.w;
  float stepX=_Blur.y;
  int fullPairs=tapCount>>1;
  [loop]for(int k=0;k<fullPairs;k++){
   float4 packed=_BlurTaps[k];
   float dx0=stepX*packed.x,dx1=stepX*packed.z;
   sum+=(SampleH(float2(uv.x+dx0,uv.y))+SampleH(float2(uv.x-dx0,uv.y)))*packed.y;
   sum+=(SampleH(float2(uv.x+dx1,uv.y))+SampleH(float2(uv.x-dx1,uv.y)))*packed.w;
  }
  if((tapCount&1)!=0){
   float4 packed=_BlurTaps[fullPairs];
   float dx0=stepX*packed.x;
   sum+=(SampleH(float2(uv.x+dx0,uv.y))+SampleH(float2(uv.x-dx0,uv.y)))*packed.y;
  }
  return sum;
 }
 float4 Vertical(Output input):SV_Target {
  float2 uv=input.uv;
  int tapCount=(int)(_Blur.x+.5);
  if(tapCount<=0)return tex2Dlod(_InputTex,float4(uv,0,0));
  float4 sum=tex2Dlod(_InputTex,float4(uv,0,0))*_Blur.w;
  float stepY=_Blur.z;
  int fullPairs=tapCount>>1;
  [loop]for(int k=0;k<fullPairs;k++){
   float4 packed=_BlurTaps[k];
   float dy0=stepY*packed.x,dy1=stepY*packed.z;
   sum+=(SampleV(float2(uv.x,uv.y+dy0))+SampleV(float2(uv.x,uv.y-dy0)))*packed.y;
   sum+=(SampleV(float2(uv.x,uv.y+dy1))+SampleV(float2(uv.x,uv.y-dy1)))*packed.w;
  }
  if((tapCount&1)!=0){
   float4 packed=_BlurTaps[fullPairs];
   float dy0=stepY*packed.x;
   sum+=(SampleV(float2(uv.x,uv.y+dy0))+SampleV(float2(uv.x,uv.y-dy0)))*packed.y;
  }
  return sum;
 }
 // Scalar-alpha passes for drop-shadow (avoids 4-channel ALU when only alpha is consumed).
 float4 HorizontalAlpha(Output input):SV_Target {
  float2 uv=input.uv;
  int tapCount=(int)(_Blur.x+.5);
  if(tapCount<=0)return float4(0,0,0,tex2Dlod(_InputTex,float4(uv,0,0)).a);
  float sum=tex2Dlod(_InputTex,float4(uv,0,0)).a*_Blur.w;
  float stepX=_Blur.y;
  int fullPairs=tapCount>>1;
  [loop]for(int k=0;k<fullPairs;k++){
   float4 packed=_BlurTaps[k];
   float dx0=stepX*packed.x,dx1=stepX*packed.z;
   sum+=(SampleHAlpha(float2(uv.x+dx0,uv.y))+SampleHAlpha(float2(uv.x-dx0,uv.y)))*packed.y;
   sum+=(SampleHAlpha(float2(uv.x+dx1,uv.y))+SampleHAlpha(float2(uv.x-dx1,uv.y)))*packed.w;
  }
  if((tapCount&1)!=0){
   float4 packed=_BlurTaps[fullPairs];
   float dx0=stepX*packed.x;
   sum+=(SampleHAlpha(float2(uv.x+dx0,uv.y))+SampleHAlpha(float2(uv.x-dx0,uv.y)))*packed.y;
  }
  return float4(0,0,0,sum);
 }
 float4 VerticalAlpha(Output input):SV_Target {
  float2 uv=input.uv;
  int tapCount=(int)(_Blur.x+.5);
  if(tapCount<=0)return float4(0,0,0,tex2Dlod(_InputTex,float4(uv,0,0)).a);
  float sum=tex2Dlod(_InputTex,float4(uv,0,0)).a*_Blur.w;
  float stepY=_Blur.z;
  int fullPairs=tapCount>>1;
  [loop]for(int k=0;k<fullPairs;k++){
   float4 packed=_BlurTaps[k];
   float dy0=stepY*packed.x,dy1=stepY*packed.z;
   sum+=(SampleVAlpha(float2(uv.x,uv.y+dy0))+SampleVAlpha(float2(uv.x,uv.y-dy0)))*packed.y;
   sum+=(SampleVAlpha(float2(uv.x,uv.y+dy1))+SampleVAlpha(float2(uv.x,uv.y-dy1)))*packed.w;
  }
  if((tapCount&1)!=0){
   float4 packed=_BlurTaps[fullPairs];
   float dy0=stepY*packed.x;
   sum+=(SampleVAlpha(float2(uv.x,uv.y+dy0))+SampleVAlpha(float2(uv.x,uv.y-dy0)))*packed.y;
  }
  return float4(0,0,0,sum);
 }
 ENDCG
 SubShader { Cull Off ZWrite Off ZTest Always Blend One Zero
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Horizontal
 ENDCG }
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Vertical
 ENDCG }
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment HorizontalAlpha
 ENDCG }
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment VerticalAlpha
 ENDCG }
 }
}
