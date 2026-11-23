#ifndef URUI_CSS_COLOR_OPS_INCLUDED
#define URUI_CSS_COLOR_OPS_INCLUDED
// CSS filter() 在 sRGB 的非预乘颜色上运算，每个步骤都截断；合并时不能只在末尾截断。
// 保持非预乘 sRGB 状态贯穿整条滤波链，将 N+2 次除法降至单次入口除法。
void UruiCssBeginStraight(float4 color, out float3 rgb, out float a)
{
    a = color.a;
    if (a > 1e-6)
    {
        rgb = color.rgb / a;
        #ifndef UNITY_COLORSPACE_GAMMA
        rgb = LinearToGammaSpace(rgb);
        #endif
    }
    else
    {
        rgb = 0;
    }
}

void UruiCssStepStraight(inout float3 rgb, inout float a, int mode, float amount)
{
    if (mode == 4)
    {
        a *= saturate(amount);
        return;
    }
    if (mode == 0)
    {
        float gray = dot(rgb, float3(.2126, .7152, .0722));
        rgb = lerp(rgb, gray.xxx, saturate(amount));
    }
    else if (mode == 1)
    {
        float gray = dot(rgb, float3(.2126, .7152, .0722));
        rgb = lerp(gray.xxx, rgb, max(0, amount));
    }
    else if (mode == 2)
    {
        rgb *= max(0, amount);
    }
    else
    {
        rgb = (rgb - .5) * max(0, amount) + .5;
    }
    rgb = saturate(rgb);
    a = saturate(a);
}

float4 UruiCssEndStraight(float3 rgb, float a)
{
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb = a > 1e-6 ? GammaToLinearSpace(rgb) : 0;
    #endif
    return float4(rgb * a, a);
}
#endif
