Shader "URUI/RectangleMask"
{
 Properties
 {
  [HideInInspector] _UruiParameters("Parameters",2D)="black"{}
  [HideInInspector] _UruiParameterSize("Parameter Size",Vector)=(1,1,1,1)
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "UnityCG.cginc"
   #include "RenderParameters.cginc"
   struct Input {float4 vertex:POSITION;float2 source:TEXCOORD1;};
   struct Output {float4 position:SV_POSITION;float2 local:TEXCOORD0;float4 pixel:TEXCOORD1;nointerpolation float4 clipRect:TEXCOORD2;nointerpolation float4 feather:TEXCOORD3;nointerpolation float3 state:TEXCOORD4;};
   Output Vertex(Input input)
   {
    Output output;output.pixel=UruiParameterPosition(input.vertex,input.source.y);output.position=mul(UNITY_MATRIX_VP,output.pixel);
    float2 size=UruiParameter(input.source.y,6).xy;output.local=input.vertex.xy*size;output.feather=UruiParameter(input.source.y,5);
    output.clipRect=UruiParameter(input.source.y,4);output.state=float3(size,UruiParameter(input.source.y,7).x);return output;
   }
   float4 Fragment(Output input):SV_Target
   {
    UruiParameterClip(input.pixel,input.clipRect,input.state.z);
    float4 distance=float4(input.local.x,input.local.y,input.state.x-input.local.x,input.state.y-input.local.y);
    float4 ramp=lerp(saturate(distance/max(input.feather,1e-20)),1,step(input.feather,0));float alpha=ramp.x*ramp.y*ramp.z*ramp.w;return alpha;
   }
   ENDCG
  }
 }
}
