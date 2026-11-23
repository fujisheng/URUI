#ifndef URUI_ELEMENT_PAINT_INCLUDED
#define URUI_ELEMENT_PAINT_INCLUDED
float4 _UruiClipRect;
float _UruiClipEnabled, _UruiPaintOpacity;
void UruiClipPaint(float2 pixel)
{
    if (_UruiClipEnabled > 0.5)
    {
        clip(float4(pixel - _UruiClipRect.xy, _UruiClipRect.zw - pixel));
    }
}
#endif
