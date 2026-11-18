// Frozen nonzero-source reference; transparent-source identity is checked against stored pixels.
#ifndef URUI_TEST_FETCH_REFERENCE_INCLUDED
#define URUI_TEST_FETCH_REFERENCE_INCLUDED
// 与原有离屏编码路径保持同一套 sRGB 传递函数及预乘约定。
float3 UruiReferenceFetchEncodeSrgb(float3 value)
{return lerp(1.055*pow(max(value,0),1.0/2.4)-.055,value*12.92,step(value,.0031308));}
float3 UruiReferenceFetchDecodeSrgb(float3 value)
{return lerp(pow(max((value+.055)/1.055,0),2.4),value/12.92,step(value,.04045));}
float4 UruiReferenceFetchEncode(float4 color,float screen)
{
 color.rgb=screen>.5?UruiReferenceFetchEncodeSrgb(color.rgb):(color.a>1e-6?UruiReferenceFetchEncodeSrgb(color.rgb/color.a)*color.a:0);
 return color;
}
float4 UruiReferenceFetchOver(float4 encodedSource,float4 linearTarget,float screen)
{
 // 不透明源完全覆盖背景；先返回，避免对没有贡献的目标颜色做转换。
 if(encodedSource.a==1)return float4(UruiReferenceFetchDecodeSrgb(encodedSource.rgb),1);
 float4 combined=encodedSource+UruiReferenceFetchEncode(linearTarget,screen)*(1-encodedSource.a);
 combined.rgb=screen>.5?UruiReferenceFetchDecodeSrgb(combined.rgb):(combined.a>1e-6?UruiReferenceFetchDecodeSrgb(combined.rgb/combined.a)*combined.a:0);
 return combined;
}
#endif
