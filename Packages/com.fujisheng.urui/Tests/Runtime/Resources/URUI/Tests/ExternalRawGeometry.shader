Shader "URUI.Tests/ExternalRawGeometry"
{
 Properties { _TestTint("Test Tint",Vector)=(1,1,1,1) [HideInInspector] _UruiPaintOpacity("Opacity",Float)=1 [HideInInspector] _UruiClipEnabled("Clip",Float)=0 [HideInInspector] _UruiClipRect("Clip Rect",Vector)=(0,0,0,0) }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
   struct appdata {float4 vertex:POSITION;float4 signal:TEXCOORD2;};
   struct v2f {float4 position:SV_POSITION;float2 pixel:TEXCOORD0;float4 signal:TEXCOORD1;};
   v2f vert(appdata value){v2f result;result.position=UnityObjectToClipPos(value.vertex);result.pixel=mul(unity_ObjectToWorld,value.vertex).xy;result.signal=value.signal;return result;}
   float4 _TestTint;
   float4 frag(v2f value):SV_Target{UruiClipPaint(value.pixel);return float4(value.signal.rgb,1)*_UruiPaintOpacity*_TestTint;}
   ENDCG
  }
 }
}
