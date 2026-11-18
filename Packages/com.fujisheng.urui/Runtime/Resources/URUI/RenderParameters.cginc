#ifndef URUI_RENDER_PARAMETERS_INCLUDED
#define URUI_RENDER_PARAMETERS_INCLUDED

// Point-sampled linear RGBAFloat data. Eight texels per base draw parameter.
Texture2D<float4> _UruiParameters;
float4 _UruiParameterSize;

float4 UruiParameterTexel(float texel)
{
    uint address = (uint)texel;
    return _UruiParameters.Load(int3(address & 255u, address >> 8, 0));
}
float4 UruiParameter(float index, float row)
{
    return UruiParameterTexel(floor(index + .5) * 8 + row);
}

float4 UruiParameterPosition(float4 position, float index)
{
    // Native matrices are column-major, including the complete projective w row.
    return UruiParameter(index, 0) * position.x + UruiParameter(index, 1) * position.y +
        UruiParameter(index, 2) * position.z + UruiParameter(index, 3) * position.w;
}

// 普通几何的 z 恒为零；bit9 表示第三列 XY 保存首次布局原点。
float4 UruiOrdinaryParameterPosition(float4 position, float index, float flags)
{
    float4 x=UruiParameter(index,0),y=UruiParameter(index,1);
    float4 z=UruiParameter(index,2),w=UruiParameter(index,3);
    if(((int)flags&512)!=0)position.xy-=z.xy;
    return x*position.x+y*position.y+z*position.z+w*position.w;
}

void UruiParameterClip(float4 pixel, float4 rectangle, float flags)
{
    if (((int)floor(flags + .5) & 1) == 0) return;
    float2 coordinate = pixel.xy / pixel.w;
    clip(float4(coordinate - rectangle.xy, rectangle.zw - coordinate));
}

#endif
