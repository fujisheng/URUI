#ifndef URUI_LAYER_COMPOSITE_PROGRAM_INCLUDED
#define URUI_LAYER_COMPOSITE_PROGRAM_INCLUDED
 #include "UnityCG.cginc"
 // 中间颜色目标包含HDR浮点数据；移动Shader也必须保留完整采样精度。
 sampler2D_float _UruiLayerSource, _UruiLayerMask, _UruiLayerBackdrop;
 sampler2D_float _MainTex;
 float4 _MainTex_TexelSize, _UruiLayerReadRect, _UruiLayerBackdropTransform;
 float _UruiLayerMaskEnabled, _UruiLayerClipEnabled, _UruiLayerReadCameraTarget, _UruiLayerReadRootColor;
 float4 _UruiLayerClip, _UruiLayerMaskRect;
 struct Varying { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; };
 Varying Vertex(appdata_img input)
 {
  Varying output;
  output.position=UnityObjectToClipPos(input.vertex);output.uv=input.texcoord;
  output.pixel=mul(unity_ObjectToWorld,input.vertex).xy;
  return output;
 }
 float4 Composite(Varying input):SV_Target
 {
  if(_UruiLayerClipEnabled>.5)
  {
   clip(input.pixel.x-_UruiLayerClip.x);clip(input.pixel.y-_UruiLayerClip.y);
   clip(_UruiLayerClip.z-input.pixel.x);clip(_UruiLayerClip.w-input.pixel.y);
  }
  float4 color=tex2D(_UruiLayerSource,input.uv);
  if(_UruiLayerMaskEnabled>.5){
   float2 uv=(input.pixel-_UruiLayerMaskRect.xy)/_UruiLayerMaskRect.zw;uv.y=1-uv.y;
   float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
   color*=tex2D(_UruiLayerMask,uv).a*inside;
  }
  return color;
 }
 float3 EncodeSrgb(float3 value)
 {
  return lerp(1.055*pow(max(value,0),1.0/2.4)-.055,value*12.92,step(value,.0031308));
 }
 float3 DecodeSrgb(float3 value)
 {
  return lerp(pow(max((value+.055)/1.055,0),2.4),value/12.92,step(value,.04045));
 }
 float4 CompositeSrgb(Varying input):SV_Target
 {
  float2 backdropUv=input.pixel*_UruiLayerBackdropTransform.xy+_UruiLayerBackdropTransform.zw;
  float4 source=Composite(input), backdrop=tex2D(_UruiLayerBackdrop,backdropUv);
  float alpha=source.a+backdrop.a*(1-source.a);
  float3 sourceRgb=source.a>1e-6?EncodeSrgb(source.rgb/source.a):0;
  // The screen framebuffer carries scene RGB independently of its alpha.
  // CSS offscreen groups retain their premultiplied representation.
  if(_UruiLayerReadRootColor>.5)
   return float4(DecodeSrgb(sourceRgb*source.a+EncodeSrgb(backdrop.rgb)*(1-source.a)),alpha);
  float3 backdropRgb=backdrop.a>1e-6?EncodeSrgb(backdrop.rgb/backdrop.a):0;
  float3 combined=sourceRgb*source.a+backdropRgb*backdrop.a*(1-source.a);
  return float4(alpha>1e-6?DecodeSrgb(combined/alpha)*alpha:0,alpha);
 }
 float4 CopyBackdrop(v2f_img input):SV_Target
 {
  float2 uv=(_UruiLayerReadRect.xy+input.uv*_UruiLayerReadRect.zw)*abs(_MainTex_TexelSize.xy);
  #if UNITY_UV_STARTS_AT_TOP
  if(_UruiLayerReadCameraTarget>.5||_MainTex_TexelSize.y<0)uv.y=1-uv.y;
  #endif
  return tex2D(_MainTex,uv);
 }
 float4 EncodeBackdrop(v2f_img input):SV_Target
 {
  float4 color=CopyBackdrop(input);
  if(_UruiLayerReadRootColor>.5)color.rgb=EncodeSrgb(color.rgb);
  else color.rgb=color.a>1e-6?EncodeSrgb(color.rgb/color.a)*color.a:0;
  return color;
 }
 float4 CopySource(Varying input):SV_Target { return tex2D(_UruiLayerSource,input.uv); }
 float4 DecodeSource(Varying input):SV_Target
 {
  float4 color=tex2D(_UruiLayerSource,input.uv);
  if(_UruiLayerReadRootColor>.5)color.rgb=DecodeSrgb(color.rgb);
  else color.rgb=color.a>1e-6?DecodeSrgb(color.rgb/color.a)*color.a:0;
  return color;
 }

#endif
