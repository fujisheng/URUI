Shader "URUI.Effect/TextureFilter"
{
 Properties {
  _MainTex("Source",2D)="white" {}
  _PatternTex("Pattern",2D)="white" {}
  [Enum(Pattern,1,Sheen,2)] _FilterMode("Filter",Float)=1
  [Enum(Multiply,0,Replace,1,MultiplyAdd,2,Add,3)] _ColorBlend("Color Blend",Float)=0
  _ReferenceSize("Reference Size",Vector)=(100,100,0,0)
  _Rotation("Rotation",Float)=0
  _Rate("Rate",Float)=0
  _Speed("Speed",Float)=0
  _PatternSpeed("Pattern Velocity",Vector)=(0,0,0,0)
  _Width("Width",Float)=0.25
  _Softness("Softness",Float)=0.5
  _Range("Range",Vector)=(0,1,0,0)
  // Raw blend coefficients avoid an implicit ShaderLab Color gamma conversion.
  _BandColor("Blend Coefficients",Vector)=(0.5,0.5,0.5,1)
  _BandSourceAlpha("Source Opacity In Band",Range(0,1))=0
  _AddColor("Post Additive Color",Vector)=(0,0,0,0)
  _AddColorIntensity("Post Additive Intensity",Range(0,1))=0
  _AddColorGlow("Post Additive Glow",Range(0,1))=0
  _Saturation("Saturation",Range(0,1))=1
  _Brightness("Brightness",Range(0,3))=1
 }
 SubShader { Cull Off ZWrite Off ZTest Always Blend Off
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "TextureFilter.cginc"
   struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
   v2f vert(float4 position:POSITION,float2 uv:TEXCOORD0) { v2f o; o.position=UnityObjectToClipPos(position); o.uv=uv; return o; }
   float _EffectTime;
   half4 frag(v2f i):SV_Target { return ApplyTextureFilter(tex2D(_MainTex,i.uv),i.uv,_EffectTime); }
   ENDCG
  }
 }
}
