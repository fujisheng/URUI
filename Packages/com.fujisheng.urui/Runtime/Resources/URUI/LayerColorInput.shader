Shader "URUI/LayerColorInput"
{
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One Zero
  CGINCLUDE
  #include "LayerCompositeProgram.cginc"
  UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(0);
  float4 EncodeInput(Varying input):SV_Target
  {
   float4 color=UNITY_READ_FRAMEBUFFER_INPUT(0,input.position.xy);
   color.rgb=_UruiLayerReadRootColor>.5?EncodeSrgb(color.rgb):(color.a>1e-6?EncodeSrgb(color.rgb/color.a)*color.a:0);
   return color;
  }
  float4 DecodeInput(Varying input):SV_Target
  {
   float4 color=UNITY_READ_FRAMEBUFFER_INPUT(0,input.position.xy);
   color.rgb=_UruiLayerReadRootColor>.5?DecodeSrgb(color.rgb):(color.a>1e-6?DecodeSrgb(color.rgb/color.a)*color.a:0);
   return color;
  }
  ENDCG
  Pass
  {
   CGPROGRAM
   #pragma target 4.5
   #pragma only_renderers metal vulkan
   #pragma vertex Vertex
   #pragma fragment EncodeInput
   ENDCG
  }
  Pass
  {
   CGPROGRAM
   #pragma target 4.5
   #pragma only_renderers metal vulkan
   #pragma vertex Vertex
   #pragma fragment DecodeInput
   ENDCG
  }
 }
}
