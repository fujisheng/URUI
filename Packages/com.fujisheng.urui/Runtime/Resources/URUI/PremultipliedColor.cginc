#ifndef URUI_PREMULTIPLIED_COLOR_INCLUDED
#define URUI_PREMULTIPLIED_COLOR_INCLUDED
float _UruiEncodedTarget;
float4 UruiLinearizePremultipliedColor(float4 color)
{
 #ifndef UNITY_COLORSPACE_GAMMA
 // URUI premultiplies sRGB vertex colors. Convert the straight color, then restore alpha.
 if (_UruiEncodedTarget < .5)
  color.rgb=color.a>0?GammaToLinearSpace(color.rgb/color.a)*color.a:0;
 #endif
 return color;
}
#endif
