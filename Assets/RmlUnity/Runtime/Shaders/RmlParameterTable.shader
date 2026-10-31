Shader "RmlUnity/Experimental/ParameterTable" {
 Properties { _MainTex("Atlas",2D)="white"{} }
 SubShader { Tags {"Queue"="Overlay" "RenderType"="Transparent"} Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma target 4.5
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct RmlTransform {float4 row0;float4 row1;};
 StructuredBuffer<RmlTransform> _RmlTransforms;
 sampler2D _MainTex;float4 _RmlClipRect;float _RmlClipEnabled;
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float2 slot:TEXCOORD1;};
 struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float2 pixel:TEXCOORD1;fixed4 color:COLOR;};
 v2f vert(appdata v){RmlTransform t=_RmlTransforms[(uint)v.slot.x];float2 pixel=float2(dot(t.row0.xy,v.vertex.xy)+t.row0.z,dot(t.row1.xy,v.vertex.xy)+t.row1.z);v2f o;o.pos=mul(UNITY_MATRIX_VP,float4(pixel,0,1));o.pixel=pixel;o.uv=v.uv;o.color=v.color;return o;}
 fixed4 frag(v2f i):SV_Target {if(_RmlClipEnabled>.5){clip(i.pixel.x-_RmlClipRect.x);clip(i.pixel.y-_RmlClipRect.y);clip(_RmlClipRect.z-i.pixel.x);clip(_RmlClipRect.w-i.pixel.y);}return tex2D(_MainTex,i.uv)*i.color;}
 ENDCG }
 }
}
