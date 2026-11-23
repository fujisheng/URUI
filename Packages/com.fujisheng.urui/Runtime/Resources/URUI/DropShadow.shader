Shader "URUI/DropShadow" {
 SubShader { Cull Off ZWrite Off ZTest Always Blend One Zero
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
#include "UnityCG.cginc"
 sampler2D _InputTex;
 sampler2D _Shadow;
 float4 _Offset,_ShadowColor;
 struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0;float2 shadowUv:TEXCOORD1; };
 Output Vertex(appdata_img input){
  Output o;
  o.position=UnityObjectToClipPos(input.vertex);
  o.uv=input.texcoord;
  o.shadowUv=input.texcoord-_Offset.xy;
  return o;
 }
 float4 Fragment(Output input):SV_Target {
  float4 original=tex2Dlod(_InputTex,float4(input.uv,0,0));
  if (original.a >= 1.0) return original;
  float2 uv=input.shadowUv;
  if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return original;
  float alpha=tex2Dlod(_Shadow,float4(uv,0,0)).a*_ShadowColor.a;
  return original+float4(_ShadowColor.rgb*alpha,alpha)*(1-original.a);
 }
 ENDCG }
 }
}
