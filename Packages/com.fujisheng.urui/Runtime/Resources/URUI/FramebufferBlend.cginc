#ifndef URUI_FRAMEBUFFER_BLEND_INCLUDED
#define URUI_FRAMEBUFFER_BLEND_INCLUDED
// 与原有离屏编码路径保持同一套 sRGB 传递函数及预乘约定。
float3 UruiFetchEncodeSrgb(float3 value)
{return lerp(1.055*pow(max(value,0),1.0/2.4)-.055,value*12.92,step(value,.0031308));}
float3 UruiFetchDecodeSrgb(float3 value)
{return lerp(pow(max((value+.055)/1.055,0),2.4),value/12.92,step(value,.04045));}
float4 UruiFetchEncode(float4 color,float screen)
{
 color.rgb=screen>.5?UruiFetchEncodeSrgb(color.rgb):(color.a>1e-6?UruiFetchEncodeSrgb(color.rgb/color.a)*color.a:0);
 return color;
}
float4 UruiFetchOver(float4 encodedSource,float4 linearTarget,float screen)
{
 // 不透明源完全覆盖背景；先返回，避免对没有贡献的目标颜色做转换。
 if(encodedSource.a==1)return float4(UruiFetchDecodeSrgb(encodedSource.rgb),1);
 // 透明黑是source-over恒等元；往返颜色转换会改写低alpha/HDR背景。
 // alpha为零但RGB非零的加法输入仍须继续合成。
 if(all(encodedSource==0))return linearTarget;
 float4 combined=encodedSource+UruiFetchEncode(linearTarget,screen)*(1-encodedSource.a);
 combined.rgb=screen>.5?UruiFetchDecodeSrgb(combined.rgb):(combined.a>1e-6?UruiFetchDecodeSrgb(combined.rgb/combined.a)*combined.a:0);
 return combined;
}
#endif
