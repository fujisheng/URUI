#ifndef URUI_PREMULTIPLIED_COLOR_INCLUDED
#define URUI_PREMULTIPLIED_COLOR_INCLUDED
float4 UruiLinearizePremultipliedColor(float4 color)
{
 #ifndef UNITY_COLORSPACE_GAMMA
 // URUI premultiplies sRGB vertex colors. GammaToLinearSpace(c/a)*a cancels one multiply by a.
 if (color.a > 0)
 {
  float3 s = color.rgb / color.a;
  color.rgb = color.rgb * (s * (s * 0.305306011 + 0.682171111) + 0.012522878);
 }
 else
 {
  color.rgb = 0;
 }
 #endif
 return color;
}
#endif
