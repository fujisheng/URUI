Shader "URUI/PremultipliedFetch"
{
 Properties
 {
  [HideInInspector] _UruiMaskTexture ("Direct Mask", 2D) = "white" {}
  [HideInInspector] _UruiParameters ("Draw Parameters", 2D) = "black" {}
  _MainTex ("Atlas", 2D) = "white" {}
  [HideInInspector] _UruiTexture1 ("Source 1", 2D) = "white" {}
  [HideInInspector] _UruiTexture2 ("Source 2", 2D) = "white" {}
  [HideInInspector] _UruiTexture3 ("Source 3", 2D) = "white" {}
  [HideInInspector] _UruiTexture4 ("Source 4", 2D) = "white" {}
  [HideInInspector] _UruiTexture5 ("Source 5", 2D) = "white" {}
  [HideInInspector] _UruiTexture6 ("Source 6", 2D) = "white" {}
  [HideInInspector] _UruiTexture7 ("Source 7", 2D) = "white" {}
  [HideInInspector] _UruiBorrowedTexture ("Borrowed Texture", Float) = 0
  [HideInInspector] _UruiSourceSize ("Source Size", Vector) = (1,1,1,1)
  [HideInInspector] _UruiSourceSrgb ("Source sRGB", Float) = 0
  [HideInInspector] _UruiTextureSampling ("Texture Sampling", Float) = 0
 }
 CGINCLUDE
 #include "PremultipliedProgram.cginc"
 #include "FramebufferBlend.cginc"
 float _UruiLayerReadRootColor;
 void fragFetch(v2f value,inout float4 target:SV_Target)
 {float4 source=UruiFragment(value);target=UruiFetchOver(source,target,_UruiLayerReadRootColor);}
 void fragCompleteFetch(completeOutput value,inout float4 target:SV_Target)
 {float4 source=UruiFragmentComplete(value);target=UruiFetchOver(source,target,_UruiLayerReadRootColor);}
 ENDCG
 SubShader
 {
  Tags {"Queue"="Overlay" "RenderType"="Transparent"}
  Cull Off ZWrite Off ZTest Always Blend One Zero
  Pass {CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex vert
   #pragma fragment fragFetch
   ENDCG}
  Pass {CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex vertComplete
   #pragma fragment fragCompleteFetch
   ENDCG}
 }
}
