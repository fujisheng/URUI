Shader "URUI/Premultiplied"
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
 SubShader
 {
  Tags { "Queue"="Overlay" "RenderType"="Transparent" }
  Cull Off ZWrite Off ZTest Always
  Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "PremultipliedProgram.cginc"
   float4 frag(v2f value):SV_Target {return UruiFragment(value);}
   ENDCG
  }
  // Native proof: compiled layout vertices, UV and colors already contain every per-draw value.
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vertComplete
   #pragma fragment fragComplete
   #include "PremultipliedProgram.cginc"
   float4 fragComplete(completeOutput value):SV_Target {return UruiFragmentComplete(value);}
   ENDCG
  }
 }
}
