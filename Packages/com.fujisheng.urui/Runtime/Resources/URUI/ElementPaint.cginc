#ifndef URUI_ELEMENT_PAINT_INCLUDED
#define URUI_ELEMENT_PAINT_INCLUDED
float4 _UruiClipRect;
float _UruiClipEnabled, _UruiPaintOpacity;
void UruiClipPaint(float2 pixel)
{
    if (_UruiClipEnabled > 0.5)
    {
        clip(pixel.x - _UruiClipRect.x);
        clip(pixel.y - _UruiClipRect.y);
        clip(_UruiClipRect.z - pixel.x);
        clip(_UruiClipRect.w - pixel.y);
    }
}
#endif
