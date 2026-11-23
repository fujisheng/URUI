#ifndef URUI_LAYER_COMPOSITE_PROGRAM_INCLUDED
#define URUI_LAYER_COMPOSITE_PROGRAM_INCLUDED
 #include "UnityCG.cginc"
 // 中间颜色目标包含HDR浮点数据；移动Shader也必须保留完整采样精度。
 sampler2D_float _UruiLayerSource, _UruiLayerMask;
 sampler2D_float _MainTex;
 float4 _MainTex_TexelSize, _UruiLayerReadRect;
 float _UruiLayerMaskEnabled, _UruiLayerClipEnabled, _UruiLayerReadCameraTarget;
 float4 _UruiLayerClip, _UruiLayerMaskRect;
 struct Varying { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; float2 maskUv:TEXCOORD2; };
 Varying Vertex(appdata_img input)
 {
  Varying output;
  output.position=UnityObjectToClipPos(input.vertex);
  output.uv=input.texcoord;
  float2 pixel=mul(unity_ObjectToWorld,input.vertex).xy;
  output.pixel=pixel;
  float2 muv=_UruiLayerMaskEnabled>.5?(pixel-_UruiLayerMaskRect.xy)/_UruiLayerMaskRect.zw:0;
  output.maskUv=float2(muv.x,1-muv.y);
  return output;
 }
 v2f_img VertexBackdrop(appdata_img input)
 {
  v2f_img output;
  output.pos=UnityObjectToClipPos(input.vertex);
  float2 uv=(_UruiLayerReadRect.xy+input.texcoord.xy*_UruiLayerReadRect.zw)*abs(_MainTex_TexelSize.xy);
  #if UNITY_UV_STARTS_AT_TOP
  if(_UruiLayerReadCameraTarget>.5||_MainTex_TexelSize.y<0)uv.y=1-uv.y;
  #endif
  output.uv=uv;
  return output;
 }
 float UruiLayerMaskAlpha(float2 uv)
 {
  // Explicit LOD 0 avoids Metal 2x2 pixel-quad derivative hazards after divergent bounds/clip exits.
  float4 mask=tex2Dlod(_UruiLayerMask,float4(uv,0,0));
  return _UruiLayerMaskEnabled>1.5?mask.r:mask.a;
 }
 float4 Composite(Varying input):SV_Target
 {
  if(_UruiLayerClipEnabled>.5)
  {
   clip(float4(input.pixel-_UruiLayerClip.xy,_UruiLayerClip.zw-input.pixel));
  }
  float4 color=tex2Dlod(_UruiLayerSource,float4(input.uv,0,0));
  if(_UruiLayerMaskEnabled>.5){
   float2 uv=input.maskUv;
   if(uv.x<0||uv.x>1||uv.y<0||uv.y>1)return 0;
   color*=UruiLayerMaskAlpha(uv);
  }
  return color;
 }
 float4 CopyBackdrop(v2f_img input):SV_Target
 {
  return tex2D(_MainTex,input.uv);
 }
 float4 CopySource(Varying input):SV_Target { return tex2D(_UruiLayerSource,input.uv); }
#endif
