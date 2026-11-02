Shader "URUI/Premultiplied"
{
 Properties
 {
  _MainTex ("Atlas", 2D) = "white" {}
  [HideInInspector] _UruiBorrowedTexture ("Borrowed Texture", Float) = 0
  [HideInInspector] _UruiSourceSize ("Source Size", Vector) = (1,1,1,1)
  [HideInInspector] _UruiSourceSrgb ("Source sRGB", Float) = 0
 }
 SubShader
 {
  Tags { "Queue"="Overlay" "RenderType"="Transparent" }
  Cull Off ZWrite Off ZTest Always
  Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "BorrowedTextureSampling.cginc"
   float4 _UruiClipRect;
   float4 _UruiTranslation;
   float _UruiClipEnabled;
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; fixed4 color:COLOR; };
   v2f vert(appdata v)
   {
    v2f o;
    o.pos=UnityObjectToClipPos(v.vertex);
    o.uv=v.uv;
    o.color=v.color;
    o.pixel=mul(unity_ObjectToWorld,v.vertex).xy;
    return o;
   }
   fixed4 frag(v2f i):SV_Target
   {
    if (_UruiClipEnabled > 0.5)
    {
     clip(i.pixel.x - _UruiClipRect.x);
     clip(i.pixel.y - _UruiClipRect.y);
     clip(_UruiClipRect.z - i.pixel.x);
     clip(_UruiClipRect.w - i.pixel.y);
    }
    // Both texture branches return premultiplied pixels; RmlUi vertex colors
    // are already premultiplied too.
    return UruiSampleMainTexture(i.uv)*i.color;
   }
   ENDCG
  }
 }
}
