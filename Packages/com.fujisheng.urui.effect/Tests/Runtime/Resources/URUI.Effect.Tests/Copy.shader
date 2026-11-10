Shader "Hidden/URUI.Effect.Tests/Copy"
{
 Properties { _MainTex("Texture",2D)="white" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Blend Off
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
   v2f vert(float4 position:POSITION,float2 uv:TEXCOORD0) { v2f o; o.position=UnityObjectToClipPos(position); o.uv=uv; return o; }
   fixed4 frag(v2f i):SV_Target { return tex2D(_MainTex,i.uv); }
   ENDCG
  }
 }
}
