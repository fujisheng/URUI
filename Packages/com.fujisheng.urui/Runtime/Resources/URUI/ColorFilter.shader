Shader "URUI/ColorFilter" {
 SubShader { Cull Off ZWrite Off ZTest Always Blend One Zero
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
#include "UnityCG.cginc"
#include "PremultipliedColor.cginc"
#include "CssColorOps.cginc"
 sampler2D _InputTex;
 struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
 Output Vertex(appdata_img input){Output o;o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o;}
 float4 SampleInput(float2 uv){return tex2D(_InputTex,uv)*step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);}

 float _Amount,_Mode;
 float4 Fragment(Output input):SV_Target {
  return UruiCssFromEncoded(UruiCssColorStep(UruiCssToEncoded(SampleInput(input.uv)), (int)_Mode, _Amount));
 }
 ENDCG }
 }
}
