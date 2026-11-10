Shader "URUI.Effect/TextureImage" {
 Properties {
  [HideInInspector] _UruiPaintOpacity("Opacity",Float)=1
  [HideInInspector] _UruiClipEnabled("Clip",Float)=0
  [HideInInspector] _UruiClipRect("Clip Rect",Vector)=(0,0,0,0)
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

 SubShader { Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
 #include "UnityCG.cginc"
 #include "TextureFilter.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
 float _EffectTime,_InputBottomLeft,_InputPremultiplied;
 float4 _EffectBounds;
 struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
 struct Output {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float2 pixel:TEXCOORD1;float2 effectUv:TEXCOORD2;float4 color:COLOR;};
 Output Vertex(Input input){Output o;o.vertex=UnityObjectToClipPos(input.vertex);o.pixel=mul(unity_ObjectToWorld,input.vertex).xy;o.uv=input.uv;o.effectUv=(input.vertex.xy-_EffectBounds.xy)*_EffectBounds.zw;o.color=input.color;return o;}
 float4 Fragment(Output input):SV_Target {
  UruiClipPaint(input.pixel);float2 uv=input.uv;if(_InputBottomLeft>.5)uv.y=1-uv.y;
  float4 source=tex2D(_MainTex,uv);if(_InputPremultiplied>.5)source.rgb=source.a>1e-6?source.rgb/source.a:0;
  float2 effectUv=input.effectUv;if(_InputBottomLeft>.5)effectUv.y=1-effectUv.y;
  float4 color=ApplyTextureFilter(source,effectUv,_EffectTime);color.rgb*=color.a;
  return color*UruiLinearizePremultipliedColor(input.color)*_UruiPaintOpacity;
 }
 ENDCG }
 }
}
