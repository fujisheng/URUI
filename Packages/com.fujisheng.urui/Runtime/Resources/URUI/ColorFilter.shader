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

 float _Amount,_Mode;
 float4 Fragment(Output input):SV_Target {
  float4 color = tex2D(_InputTex, input.uv);
  int mode = (int)_Mode;
  if (mode == 4) { return color * saturate(_Amount); }
  float3 rgb;
  float a;
  UruiCssBeginStraight(color, rgb, a);
  UruiCssStepStraight(rgb, a, mode, _Amount);
  return UruiCssEndStraight(rgb, a);
 }
 ENDCG }
 }
}
