#ifndef URUI_CSS_COLOR_OPS_INCLUDED
#define URUI_CSS_COLOR_OPS_INCLUDED
// CSS filter() 在 sRGB 的非预乘颜色上运算，每个步骤都截断；合并时不能只在末尾截断。
float4 UruiCssToEncoded(float4 color)
{
    #ifndef UNITY_COLORSPACE_GAMMA
    if (_UruiEncodedTarget < .5) { color.rgb = color.a > 1e-6 ? LinearToGammaSpace(color.rgb / color.a) * color.a : 0; }
    #endif
    return color;
}
float4 UruiCssFromEncoded(float4 color)
{
    #ifndef UNITY_COLORSPACE_GAMMA
    if (_UruiEncodedTarget < .5) { color.rgb = color.a > 1e-6 ? GammaToLinearSpace(color.rgb / color.a) * color.a : 0; }
    #endif
    return color;
}
float4 UruiCssColorStep(float4 color, int mode, float amount)
{
    if (mode == 4) { return color * saturate(amount); }
    float3 rgb = color.a > 1e-6 ? color.rgb / color.a : 0;
    float gray = dot(rgb, float3(.2126, .7152, .0722));
    if (mode == 0) { rgb = lerp(rgb, gray.xxx, saturate(amount)); }
    else if (mode == 1) { rgb = lerp(gray.xxx, rgb, max(0, amount)); }
    else if (mode == 2) { rgb *= max(0, amount); }
    else { rgb = (rgb - .5) * max(0, amount) + .5; }
    return float4(saturate(rgb) * saturate(color.a), saturate(color.a));
}
#endif
