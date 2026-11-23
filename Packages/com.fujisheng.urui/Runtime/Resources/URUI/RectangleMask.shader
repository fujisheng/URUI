Shader "URUI/RectangleMask"
{
 Properties
 {
  [HideInInspector] _UruiParameters("Parameters",2D)="black"{}
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
   struct Output {float4 position:SV_POSITION;float2 local:TEXCOORD0;float4 pixel:TEXCOORD1;nointerpolation float4 clipRect:TEXCOORD2;nointerpolation float4 invFeather:TEXCOORD3;nointerpolation float3 state:TEXCOORD4;};
   Output Vertex(Input input)
   {
    Output output;
    int2 baseCoord = UruiParameterBase(input.source.y);
    float4 pixel = UruiParameterAt(baseCoord, 0) * input.vertex.x + UruiParameterAt(baseCoord, 1) * input.vertex.y + UruiParameterAt(baseCoord, 3) * input.vertex.w;
    if (input.vertex.z != 0) pixel += UruiParameterAt(baseCoord, 2) * input.vertex.z;
    output.pixel = pixel;
    output.position = mul(UNITY_MATRIX_VP, pixel);
    float2 size = UruiParameterAt(baseCoord, 6).xy;
    output.local = input.vertex.xy * size;
    float4 feather = UruiParameterAt(baseCoord, 5);
    output.invFeather = feather > 0 ? rcp(feather) : 0;
    float flags = UruiParameterAt(baseCoord, 7).x;
    output.clipRect = (((int)flags) & 1) != 0 ? UruiParameterAt(baseCoord, 4) : 0;
    output.state = float3(size, flags);
    return output;
   }
   float4 Fragment(Output input):SV_Target
   {
    UruiParameterClip(input.pixel, input.clipRect, input.state.z);
    float4 distance = float4(input.local.x, input.local.y, input.state.x - input.local.x, input.state.y - input.local.y);
    float4 ramp = saturate(distance * input.invFeather + step(input.invFeather, 0));
    float alpha = ramp.x * ramp.y * ramp.z * ramp.w;
    return alpha;
   }
   ENDCG
  }
 }
}
