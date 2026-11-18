Shader "URUI/LayerCompositeFetch"
{
 SubShader {Cull Off ZWrite Off ZTest Always Blend One Zero
  Pass {CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex Vertex
   #pragma fragment FetchComposite
   #include "LayerCompositeProgram.cginc"
   #include "FramebufferBlend.cginc"
   void FetchComposite(Varying input,inout float4 target:SV_Target)
   {target=UruiFetchOver(UruiFetchEncode(Composite(input),0),target,_UruiLayerReadRootColor);}
  ENDCG}
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
     float2 uv=(input.pixel-_UruiLayerMaskRect.xy)/_UruiLayerMaskRect.zw;uv.y=1-uv.y;
     float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
     // 保留原 Composite 的 float 乘法及目标量化；省去 scratch 写回和源纹理读取。
     target*=tex2D(_UruiLayerMask,uv).a*inside;
    }
   }
  ENDCG}
 }
}
