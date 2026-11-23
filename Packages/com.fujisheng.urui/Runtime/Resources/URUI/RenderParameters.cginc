#ifndef URUI_RENDER_PARAMETERS_INCLUDED
#define URUI_RENDER_PARAMETERS_INCLUDED

// Point-sampled linear RGBAFloat data. Eight texels per base draw parameter (256 = 32 * 8, never splits a row).
Texture2D<float4> _UruiParameters;
// Batch base parameter offset so vertices only encode batch-local offsets (decoupled from global command index).
float _UruiBaseParameter;

float4 UruiParameterTexel(float texel)
{
    uint address = (uint)texel;
    return _UruiParameters.Load(int3(address & 255u, address >> 8, 0));
}

int2 UruiParameterBase(float index)
{
    uint baseAddr = (uint)(floor(index + .5) + _UruiBaseParameter) * 8u;
    return int2((int)(baseAddr & 255u), (int)(baseAddr >> 8));
}

float4 UruiParameterAt(int2 baseCoord, int row)
{
    return _UruiParameters.Load(int3(baseCoord.x + row, baseCoord.y, 0));
}

float4 UruiParameter(float index, float row)
{
    return UruiParameterTexel((floor(index + .5) + _UruiBaseParameter) * 8 + row);
}

float4 UruiParameterPosition(float4 position, float index)
{
    int2 baseCoord = UruiParameterBase(index);
    // Native matrices are column-major, including the complete projective w row.
    float4 result = UruiParameterAt(baseCoord, 0) * position.x + UruiParameterAt(baseCoord, 1) * position.y +
        UruiParameterAt(baseCoord, 3) * position.w;
    if (position.z != 0)
    {
        result += UruiParameterAt(baseCoord, 2) * position.z;
    }
    return result;
}

// 普通几何的 z 恒为零；bit9 表示第三列 XY 保存首次布局原点。
float4 UruiOrdinaryParameterPositionAt(float4 position, int2 baseCoord, int flags)
{
    float4 x = UruiParameterAt(baseCoord, 0), y = UruiParameterAt(baseCoord, 1);
    float4 w = UruiParameterAt(baseCoord, 3);
    if ((flags & 512) != 0)
    {
        float4 z = UruiParameterAt(baseCoord, 2);
        position.xy -= z.xy;
        return x * position.x + y * position.y + z * position.z + w * position.w;
    }
    if (position.z != 0)
    {
        return x * position.x + y * position.y + UruiParameterAt(baseCoord, 2) * position.z + w * position.w;
    }
    return x * position.x + y * position.y + w * position.w;
}

float4 UruiOrdinaryParameterPosition(float4 position, float index, float flags)
{
    return UruiOrdinaryParameterPositionAt(position, UruiParameterBase(index), (int)flags);
}

void UruiParameterClipWindow(float2 coordinate, float4 rectangle)
{
    clip(float4(coordinate - rectangle.xy, rectangle.zw - coordinate));
}

void UruiParameterClip(float4 pixel, float4 rectangle, float flags)
{
    if (((int)floor(flags + .5) & 1) == 0) return;
    UruiParameterClipWindow(pixel.xy / pixel.w, rectangle);
}

#endif
