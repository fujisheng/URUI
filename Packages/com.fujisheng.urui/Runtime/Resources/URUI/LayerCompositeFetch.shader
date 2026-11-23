Shader "URUI/LayerCompositeFetch"
{
 SubShader {Cull Off ZWrite Off ZTest Always Blend One Zero
  Pass {CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex Vertex
   #pragma fragment FetchMask
   #include "LayerCompositeProgram.cginc"
   void FetchMask(Varying input,inout float4 target:SV_Target)
   {
    if(_UruiLayerMaskEnabled>.5)
    {
     float2 uv=input.maskUv;
     if(uv.x<0||uv.x>1||uv.y<0||uv.y>1){target=0;return;}
     // 保留原 Composite 的 float 乘法及目标量化；省去 scratch 写回和源纹理读取。
     target*=UruiLayerMaskAlpha(uv);
    }
   }
  ENDCG}

 }
}
