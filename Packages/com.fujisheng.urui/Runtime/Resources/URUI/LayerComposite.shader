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
   #pragma vertex VertexBackdrop
   #pragma fragment CopyBackdrop
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
  Pass
  {
   Blend One Zero
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment ClearLayerRegion
   float4 ClearLayerRegion():SV_Target { return 0; }
   ENDCG
  }
  // Fast unclipped, unmasked alpha-over composite (zero dynamic branches / clip instructions).
  Pass
  {
   Blend One OneMinusSrcAlpha
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment CopySource
   ENDCG
  }
 }
}
