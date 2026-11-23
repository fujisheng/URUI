Shader "URUI.Effect/Particle"
{
 Properties
 {
  _MainTex("Texture",2D)="white" {}
  _AlphaTex("Alpha",2D)="white" {}
  _Color("Tint",Color)=(1,1,1,1)
  _AlphaChannel("Alpha Channel",Vector)=(0,0,0,1)
  _UseSeparateAlpha("Separate Alpha",Float)=0
  _UseRadialMask("Radial Mask",Float)=0
  _RadialSoftness("Radial Softness",Range(0.01,1))=0.35
  _Intensity("Intensity",Float)=1
  _PremultiplyRGB("Premultiply RGB",Float)=0
  _ColorMask("Color Mask",Float)=15
  [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend",Float)=5
  [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend",Float)=1
  [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendAlpha("Source Alpha Blend",Float)=5
  [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendAlpha("Destination Alpha Blend",Float)=1
  [Enum(UnityEngine.Rendering.CullMode)] _CullMode("Face Culling",Float)=0
  [HideInInspector] _UruiParameters("Parameters",2D)="black" {}
 }
 SubShader
 {
  Tags { "Queue"="Overlay" "RenderType"="Transparent" }
  ColorMask [_ColorMask]
  Cull [_CullMode] ZWrite Off ZTest Always Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/RenderParameters.cginc"
   sampler2D _MainTex, _AlphaTex;
   float4 _MainTex_ST, _AlphaTex_ST, _AlphaChannel;
   fixed4 _Color;
   float _UseSeparateAlpha, _UseRadialMask, _RadialSoftness, _Intensity, _PremultiplyRGB;
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 parameter:TEXCOORD1; fixed4 color:COLOR; };
   struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 alphaUv:TEXCOORD1; float4 pixel:TEXCOORD2; nointerpolation float4 clipRect:TEXCOORD3; nointerpolation float flags:TEXCOORD4; half4 color:COLOR; };
   v2f vert(appdata v)
   {
    // 粒子模拟可含深度；输出属于二维节点，不把模拟 Z 带进 UI 相机的裁剪空间。
    int2 baseCoord = UruiParameterBase(v.parameter.y);
    v2f o;
    o.pixel = UruiParameterAt(baseCoord, 0) * v.vertex.x + UruiParameterAt(baseCoord, 1) * v.vertex.y + UruiParameterAt(baseCoord, 3) * v.vertex.w;
    o.position = mul(UNITY_MATRIX_VP, o.pixel);
    float flags = UruiParameterAt(baseCoord, 7).x;
    o.clipRect = (((int)flags) & 1) != 0 ? UruiParameterAt(baseCoord, 4) : 0;
    o.flags = flags;
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.alphaUv = TRANSFORM_TEX(v.uv, _AlphaTex);
    half4 c = (half4)(v.color * _Color * UruiParameterAt(baseCoord, 5));
    c.rgb *= (half)_Intensity;
    o.color = c;
    return o;
   }
   half4 frag(v2f i):SV_Target
   {
    UruiParameterClip(i.pixel,i.clipRect,i.flags);
    fixed4 sample=tex2D(_MainTex,i.uv);
    fixed4 alphaSample=sample;
    if (_UseSeparateAlpha > 0)
    {
     alphaSample=lerp(sample,tex2D(_AlphaTex,i.alphaUv),saturate(_UseSeparateAlpha));
    }
    half alpha=saturate(dot(alphaSample,saturate(_AlphaChannel)));
    if (_UseRadialMask > 0)
    {
     half radial=smoothstep(0,max(0.01,_RadialSoftness),1-saturate(length(i.uv*2-1)));
     alpha*=lerp(1,radial,saturate(_UseRadialMask));
    }
    alpha*=i.color.a;
    half3 rgb=sample.rgb*i.color.rgb;
    rgb*=lerp(1,alpha,saturate(_PremultiplyRGB));
    return half4(rgb,alpha);
   }
   ENDCG
  }
 }
}
