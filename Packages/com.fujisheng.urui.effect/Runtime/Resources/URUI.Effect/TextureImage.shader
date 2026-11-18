Shader "URUI.Effect/TextureImage" {
 Properties {
  [HideInInspector] _UruiParameters("Parameters",2D)="black" {}
  [HideInInspector] _UruiParameterSize("Parameter Size",Vector)=(1,1,1,1)
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
 UruiImageOutput Vertex(UruiImageInput input){return UruiImageVertex(input);}
 float4 Fragment(UruiImageOutput input):SV_Target {
  UruiParameterClip(input.pixel,input.clipRect,input.state.x);
  int flags=(int)input.runtime.w;float2 uv=input.uv;if((flags&1)!=0)uv.y=1-uv.y;
  float4 source=UruiImageSample(uv,input.state.y);if((flags&2)!=0)source.rgb=source.a>1e-6?source.rgb/source.a:0;
  float2 effectUv=input.effectUv;if((flags&1)!=0)effectUv.y=1-effectUv.y;
  float4 fields=UruiParameterTexel(input.state.z+2);
  float rate=_CssFields.x>.5&&(_CssFields.w<.5||fields.w>1)?fields.x:_Rate;
  float saturation=_Saturation*(_CssFields.y>.5?fields.y:1);
  float brightness=_Brightness*(_CssFields.z>.5?fields.z:1);
  float4 color=ApplyTextureFilter(source,effectUv,input.runtime.z,input.runtime.xy,rate,saturation,brightness);color.rgb*=color.a;
  return color*UruiLinearizePremultipliedColor(input.color);
 }
 ENDCG }
 }
}
