Shader "URUI/LayerComposite"
{
 Properties
 {
  [HideInInspector] _MainTex ("Backdrop source", 2D) = "black" {}
  [HideInInspector] _UruiCompositeSrcBlend ("Effect source blend", Float) = 1
  [HideInInspector] _UruiCompositeDstBlend ("Effect destination blend", Float) = 10
 }
 CGINCLUDE
 #include "LayerCompositeProgram.cginc"
 ENDCG
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   Blend One OneMinusSrcAlpha
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Composite
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Composite
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment CompositeSrgb
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment CopyBackdrop
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment EncodeBackdrop
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment DecodeSource
   ENDCG
  }
  Pass
  {
   Blend [_UruiCompositeSrcBlend] [_UruiCompositeDstBlend]
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Composite
   ENDCG
  }
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment CopySource
   ENDCG
  }

 }
}
