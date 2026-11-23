Shader "URUI.Effect/TextureImage" {
 Properties {
  [HideInInspector] _UruiParameters("Parameters",2D)="black" {}
  [HideInInspector] _CssFields("CSS Fields",Vector)=(0,0,0,0)
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
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ImageParameters.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
 float4 _CssFields;
 struct TextureImageOutput
 {
  float4 vertex:SV_POSITION;
  float2 uv:TEXCOORD0;
  float4 pixel:TEXCOORD1;
  float2 local:TEXCOORD2;
  float4 color:COLOR;
  nointerpolation float4 clipRect:TEXCOORD3;
  nointerpolation float4 state:TEXCOORD4;
  nointerpolation float4 filterParams:TEXCOORD5;
 };
 TextureImageOutput Vertex(UruiImageInput input)
 {
  UruiImageOutput base = UruiImageVertex(input);
  TextureImageOutput o;
  o.vertex = base.vertex;
  int flags = (int)base.runtime.w;
  float2 uv = base.uv;
  float2 effectUv = base.effectUv;
  if ((flags & 1) != 0)
  {
   uv.y = 1 - uv.y;
   effectUv.y = 1 - effectUv.y;
  }
  o.uv = uv;
  o.pixel = base.pixel;
  o.local = ComputeTextureFilterLocal(effectUv, base.runtime.xy);
  o.color = base.color;
  o.clipRect = base.clipRect;
  o.state = float4(base.state.x, base.state.y, base.runtime.z, base.runtime.w);
  float4 fields = UruiParameterTexel(base.state.z + 2);
  float inputRate = _CssFields.x > .5 && (_CssFields.w < .5 || fields.w > 1) ? fields.x : _Rate;
  float rate = frac(_Speed * base.runtime.z + inputRate * 0.9999);
  float saturation = _Saturation * (_CssFields.y > .5 ? fields.y : 1);
  float brightness = _Brightness * (_CssFields.z > .5 ? fields.z : 1);
  o.filterParams = float4(rate, saturation, brightness, 0);
  return o;
 }
 float4 Fragment(TextureImageOutput input):SV_Target {
  UruiParameterClip(input.pixel,input.clipRect,input.state.x);
  int flags=(int)input.state.w;
  float4 source=UruiImageSample(input.uv,input.state.y);
  if((flags&2)!=0)source.rgb=source.a>1e-6?source.rgb/source.a:0;
  float4 color=ApplyTextureFilterCore(source,input.local,input.state.z,input.filterParams.x,input.filterParams.y,input.filterParams.z,true);
  return color*UruiLinearizePremultipliedColor(input.color);
 }
 ENDCG }
 }
}
