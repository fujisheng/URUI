Shader "URUI.Effect/FeatherMask"
{
 Properties
 {
  [HideInInspector] _UruiPaintOpacity("Opacity",Float)=1
  [HideInInspector] _UruiClipEnabled("Clip Enabled",Float)=0
  [HideInInspector] _UruiClipRect("Clip Rect",Vector)=(0,0,0,0)
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
   float4 _Size,_Feather;
   struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
   struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; };
   Output Vert(Input input)
   {
    Output output;output.position=UnityObjectToClipPos(input.vertex);output.uv=input.uv;
    output.pixel=mul(unity_ObjectToWorld,input.vertex).xy;return output;
   }
   float Ramp(float distance,float feather) { return feather>0?saturate(distance/feather):1; }
   fixed4 Frag(Output input):SV_Target
   {
    UruiClipPaint(input.pixel);
    float2 local=input.uv*_Size.xy;
    float alpha=Ramp(local.x,_Feather.x)*Ramp(local.y,_Feather.y)*Ramp(_Size.x-local.x,_Feather.z)*Ramp(_Size.y-local.y,_Feather.w)*_UruiPaintOpacity;
    return fixed4(alpha,alpha,alpha,alpha);
   }
   ENDCG
  }
 }
}
