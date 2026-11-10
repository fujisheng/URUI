Shader "URUI/DropShadow" {
 SubShader { Cull Off ZWrite Off ZTest Always Blend One Zero
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
#include "UnityCG.cginc"
 sampler2D _InputTex;
 struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
 Output Vertex(appdata_img input){Output o;o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o;}
 float4 SampleInput(float2 uv){return tex2D(_InputTex,uv)*step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);}

 sampler2D _Shadow;float4 _Offset,_ShadowColor;
 float4 Fragment(Output input):SV_Target {
  float4 original=SampleInput(input.uv);float2 uv=input.uv-_Offset.xy;
  float alpha=tex2D(_Shadow,uv).a*step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1)*_ShadowColor.a;
  return original+float4(_ShadowColor.rgb*alpha,alpha)*(1-original.a);
 }
 ENDCG }
 }
}
