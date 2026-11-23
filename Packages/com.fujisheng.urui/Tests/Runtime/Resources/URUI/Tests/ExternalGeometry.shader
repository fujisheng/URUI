Shader "URUI.Tests/ExternalGeometry"
{
 Properties { _FactFloat("Fact Float",Float)=0 _FactInt("Fact Integer",Integer)=0 _FactColor("Fact Color",Color)=(0,0,0,0)  _TestTexture("Test Texture",2D)="white"{} _TestTint("Test Tint",Vector)=(1,1,1,1) [HideInInspector] _UruiParameters("Parameters",2D)="black"{} }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/RenderParameters.cginc"
   struct input {float4 vertex:POSITION;float4 color:COLOR;float2 parameter:TEXCOORD1;};
   struct output {float4 position:SV_POSITION;float4 world:TEXCOORD0;float4 clipRect:TEXCOORD1;nointerpolation float flags:TEXCOORD2;float4 color:COLOR;};
   output vert(input value)
   {
    output result;result.world=UruiParameterPosition(value.vertex,value.parameter.y);result.position=mul(UNITY_MATRIX_VP,result.world);
    result.clipRect=UruiParameter(value.parameter.y,4);result.color=value.color*UruiParameter(value.parameter.y,5);result.flags=UruiParameter(value.parameter.y,7).x;return result;
   }
   float4 _TestTint;
   float4 frag(output value):SV_Target {UruiParameterClip(value.world,value.clipRect,value.flags);return float4(value.color.rgb*value.color.a,value.color.a)*_TestTint;}
   ENDCG
  }
 }
}
