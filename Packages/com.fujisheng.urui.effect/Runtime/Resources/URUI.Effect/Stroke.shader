Shader "URUI.Effect/Stroke"
{
 Properties {
  [HideInInspector] _UruiPaintOpacity("Opacity",Float)=1
  [HideInInspector] _UruiClipEnabled("Clip",Float)=0
  [HideInInspector] _UruiClipRect("Clip Rect",Vector)=(0,0,0,0)
 }
 SubShader { Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
   struct input { float4 vertex:POSITION; float4 color:COLOR; };
   struct output { float4 vertex:SV_POSITION; float4 color:COLOR; float2 pixel:TEXCOORD0; };
   output vert(input v) { output o; o.vertex=UnityObjectToClipPos(v.vertex); o.pixel=mul(unity_ObjectToWorld,v.vertex).xy; o.color=v.color; return o; }
   float4 frag(output i):SV_Target { UruiClipPaint(i.pixel); i.color.a*=_UruiPaintOpacity; return i.color; }
   ENDCG
  }
 }
}
