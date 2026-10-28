Shader "RmlUnity/Premultiplied"
{
 Properties { _MainTex ("Atlas", 2D) = "white" {} }
 SubShader
 {
  Tags { "Queue"="Overlay" "RenderType"="Transparent" }
  Cull Off ZWrite Off ZTest Always
  Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   float4 _RmlClipRect;
   float4 _RmlTranslation;
   float _RmlClipEnabled;
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; fixed4 color:COLOR; };
   v2f vert(appdata v)
   {
    v2f o;
    o.pos=UnityObjectToClipPos(v.vertex);
    o.uv=v.uv;
    o.color=v.color;
    o.pixel=v.vertex.xy+_RmlTranslation.xy;
    return o;
   }
   fixed4 frag(v2f i):SV_Target
   {
    if (_RmlClipEnabled > 0.5)
    {
     clip(i.pixel.x - _RmlClipRect.x);
     clip(i.pixel.y - _RmlClipRect.y);
     clip(_RmlClipRect.z - i.pixel.x);
     clip(_RmlClipRect.w - i.pixel.y);
    }
    // RmlUi supplies premultiplied-alpha vertices and generated atlas pixels.
    // Do not multiply RGB by alpha again.
    return tex2D(_MainTex,i.uv)*i.color;
   }
   ENDCG
  }
 }
}
