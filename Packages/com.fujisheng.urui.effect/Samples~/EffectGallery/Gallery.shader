Shader "URUI.Effect/Examples/Tint" {
 Properties {_MainTex("Source",2D)="white"{} _Progress("Progress",Float)=0 _UruiPaintOpacity("Opacity",Float)=1 _UruiClipEnabled("Clip",Float)=0 _UruiClipRect("Clip Rect",Vector)=(0,0,0,0)}
 SubShader {Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
 sampler2D _MainTex;float _Progress;
 struct Input {float4 position:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
 struct Output {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float2 pixel:TEXCOORD1;float4 color:COLOR;};
 Output vert(Input input){Output o;o.position=UnityObjectToClipPos(input.position);o.pixel=mul(unity_ObjectToWorld,input.position).xy;o.uv=input.uv;o.color=input.color;return o;}
 float4 frag(Output input):SV_Target {UruiClipPaint(input.pixel);float4 c=tex2D(_MainTex,input.uv);c.rgb*=lerp(float3(1,.2,.2),float3(.2,.5,1),_Progress);c.rgb*=c.a;return c*UruiLinearizePremultipliedColor(input.color)*_UruiPaintOpacity;}
 ENDCG}}}
