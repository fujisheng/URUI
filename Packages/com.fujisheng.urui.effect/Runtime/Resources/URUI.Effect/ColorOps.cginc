// 依据 UIEffect 5.8.4 的颜色算法改编；MIT 许可见 ThirdParty/UIEffect/LICENSE.md。
#ifndef URUI_EFFECT_COLOR_OPS_INCLUDED
#define URUI_EFFECT_COLOR_OPS_INCLUDED

float3 UruiRgbToHsv(float3 c)
{
    const float4 K = float4(0, -1.0 / 3.0, 2.0 / 3.0, -1);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float denominator = q.x + 1e-4;
    // 浮点 HDR 输入也允许负数；避免源公式在恰好 -epsilon 时除零。
    denominator = abs(denominator) < 1e-6 ? (denominator < 0 ? -1e-6 : 1e-6) : denominator;
    return float3(abs(q.z + (q.w - q.y) / (6 * d + 1e-4)), d / denominator, q.x);
}
float3 UruiHsvToRgb(float3 c)
{
    const float4 K = float4(1, 2.0 / 3.0, 1.0 / 3.0, 3);
    float3 p = abs(frac(c.xxx + K.xyz) * 6 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}
float UruiInverseRange(float from, float to, float value)
{ return saturate(max(0, value - from) / max(.001, to - from)); }

float UruiTargetRate(float3 color, int mode, float3 targetColor, float range, float softness)
{
    if (mode == 0 || range >= 1) { return 1; }
    if (range <= 0) { return 0; }
    float difference = 0;
    if (mode == 1)
    {
        difference = abs(UruiRgbToHsv(targetColor).x - UruiRgbToHsv(color).x);
        difference = min(difference, 1 - difference);
    }
    else { difference = abs(Luminance(targetColor) - Luminance(color)); }
    return 1 - UruiInverseRange(range * (1 - softness), range, difference);
}

float4 UruiTone(float4 color, int mode, float intensity)
{
    float luminance = Luminance(color.rgb);
    if (mode == 1) { color.rgb = lerp(color.rgb, luminance.xxx, intensity); }
    else if (mode == 2) { color.rgb = lerp(color.rgb, luminance * float3(1.07, .74, .43), intensity); }
    else if (mode == 3) { color.rgb = lerp(color.rgb, (1 - color.rgb) * color.a, intensity); }
    else if (mode == 4)
    {
        float r0 = step(luminance, .25), r1 = step(luminance, .5), r2 = step(luminance, .75);
        float3 retro = float3(.06, .22, .06) * r0 + float3(.19, .38, .19) * (1 - r0) * r1
            + float3(.54, .67, .06) * (1 - r1) * r2 + float3(.60, .74, .06) * (1 - r2);
        color.rgb = lerp(color.rgb, retro * color.a, intensity);
    }
    else if (mode == 5)
    {
        float division = round(lerp(48, 4, intensity) / 2) * 2;
        color.rgb = UruiHsvToRgb((floor(UruiRgbToHsv(color.rgb) * division) + .5) / division) * color.a;
    }
    return color;
}

float4 UruiColor(float4 original, float4 factor, float intensity, int mode, float glow, bool gradation)
{
    float4 color = original;
    float weight = gradation ? factor.a : 1;
    if (mode == 1) { color.rgb *= factor.rgb; if (gradation) { color *= factor.a; } }
    else if (mode == 2) { color.rgb += factor.rgb * color.a * weight; }
    else if (mode == 3) { color.rgb -= factor.rgb * color.a * weight; }
    else if (mode == 4) { color.rgb = factor.rgb * color.a; if (gradation) { color *= factor.a; } }
    else if (mode == 5) { color.rgb = (1 + Luminance(color.rgb)) * factor.rgb * weight / 2 * color.a; }
    else if (mode == 6) { color.rgb *= 1 + factor.rgb * weight; }
    else if (mode == 7)
    {
        color.rgb = UruiHsvToRgb(UruiRgbToHsv(color.rgb) + factor.rgb) * color.a * factor.a;
        color.a = original.a * factor.a;
    }
    else if (mode == 8)
    {
        color.rgb = ((color.rgb - .5) * (factor.r + 1) + .5 + factor.g * 1.5) * color.a * factor.a;
        color.a *= factor.a;
    }
    if (mode > 0) { color = lerp(original, color, intensity); if (!gradation) { color.a *= 1 - glow * intensity; } }
    return color;
}
#endif
