Shader "URUI.Effect/WaveImage" {
 Properties {_MainTex("Texture",2D)="white" {} [HideInInspector]_UruiPaintOpacity("Opacity",Float)=1 [HideInInspector]_UruiClipEnabled("Clip",Float)=0 [HideInInspector]_UruiClipRect("Clip Rect",Vector)=(0,0,0,0)}
 SubShader {Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 Pass {CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
 #include "UnityCG.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
 sampler2D _MainTex;float4 _WaveSize,_WaveMotion,_WaveShape,_WaveAxis,_ImageColor;float _InputBottomLeft,_InputPremultiplied;
 struct Output{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float2 pixel:TEXCOORD1;};
 Output Vertex(appdata_img input){
  float nx=input.vertex.x,ny=1-input.vertex.y;float edge=_WaveAxis.x;bool horizontal=_WaveAxis.y<.5;bool verticalAxis=(edge>1.5&&edge<3.5)||(edge>3.5&&horizontal);
  float coordinate=verticalAxis?ny:nx,orthogonal=verticalAxis?nx:ny;
  float distance=edge<.5?nx:edge<1.5?1-nx:edge<2.5?1-ny:edge<3.5?ny:2*abs(coordinate-.5);
  float phase=(_WaveAxis.z+_WaveShape.w)*_WaveMotion.z;
  float wave=sin(coordinate*_WaveMotion.y+phase)+_WaveMotion.w*sin(coordinate*_WaveMotion.y*.5-phase*.7);
  wave+=_WaveShape.y*(orthogonal-.5)*(verticalAxis?_WaveSize.x:_WaveSize.y)*_WaveShape.z*sin(phase+coordinate*2);
  float offset=wave*_WaveMotion.x*smoothstep(_WaveShape.x,1,distance);
  float4 position=float4(input.vertex.xy*_WaveSize.xy+_WaveSize.zw+float2(horizontal?offset:0,horizontal?0:-offset),0,1);
  Output o;o.vertex=UnityObjectToClipPos(position);o.pixel=mul(unity_ObjectToWorld,position).xy;o.uv=input.texcoord;return o;
 }
 float4 Fragment(Output input):SV_Target{UruiClipPaint(input.pixel);float2 uv=input.uv;if(_InputBottomLeft>.5)uv.y=1-uv.y;float4 color=tex2D(_MainTex,uv);if(_InputPremultiplied<.5)color.rgb*=color.a;return color*UruiLinearizePremultipliedColor(_ImageColor)*_UruiPaintOpacity;}
 ENDCG}
 }
}
