Shader "URUI.Validation/SceneBackdrop"
{
 SubShader
 {
  Tags { "RenderType"="Opaque" "Queue"="Background" }
  Pass
  {
   ZWrite Off ZTest Always Cull Off
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct Input { float4 vertex:POSITION; float4 color:COLOR; };
   struct Varying { float4 position:SV_POSITION; float4 color:COLOR; };
   Varying vert(Input input){Varying output;output.position=UnityObjectToClipPos(input.vertex);output.color=input.color;return output;}
   float4 frag(Varying input):SV_Target{return input.color;}
   ENDCG
  }
 }
}
