#ifndef URUI_PREMULTIPLIED_PROGRAM_INCLUDED
#define URUI_PREMULTIPLIED_PROGRAM_INCLUDED
   #include "UnityCG.cginc"
   #include "PremultipliedColor.cginc"
   #include "BorrowedTextureSampling.cginc"
   #include "PremultipliedPalette.cginc"
   #include "RenderParameters.cginc"
   #include "CssColorOps.cginc"
   #include "DirectMask.cginc"
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 source:TEXCOORD1; float4 color:COLOR; };
   struct v2f
   {
    float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 pixel:TEXCOORD1;
    nointerpolation float slot:TEXCOORD2; nointerpolation float4 clipRect:TEXCOORD3;
    nointerpolation float flags:TEXCOORD4; nointerpolation float3 program:TEXCOORD5; float4 color:COLOR;
   };
   v2f vert(appdata v)
   {
    v2f o;
    int2 baseCoord = UruiParameterBase(v.source.y);
    float4 metadata = UruiParameterAt(baseCoord, 7);
    int flags = (int)metadata.x;
    float4 world = UruiOrdinaryParameterPositionAt(v.vertex, baseCoord, flags);
    o.pos = mul(UNITY_MATRIX_VP, world);
    float4 uv = UruiParameterAt(baseCoord, 6);
    o.uv = v.uv * uv.xy + uv.zw;
    o.slot = v.source.x;
    float4 color = v.color * UruiParameterAt(baseCoord, 5);
    // Bit 10 proves uniform per-primitive vertex color, allowing exact per-vertex sRGB->Linear hoist.
    if ((flags & 1024) != 0)
    {
     color = UruiLinearizePremultipliedColor(color);
    }
    o.color = color;
    o.pixel = float3(world.xy, world.w);
    o.clipRect = (flags & 1) != 0 ? UruiParameterAt(baseCoord, 4) : 0;
    o.flags = metadata.x;
    o.program = metadata.yzw;
    return o;
   }
   float4 UruiFragment(v2f i)
   {
    int flags = (int)i.flags;
    float maskAlpha = 1;
    if ((flags & 257) != 0)
    {
     float2 window = i.pixel.xy / i.pixel.z;
     if ((flags & 1) != 0)
     {
      UruiParameterClipWindow(window, i.clipRect);
     }
     if ((flags & 256) != 0)
     {
      maskAlpha = UruiDirectMaskAtWindow(window, i.program.z);
      if (maskAlpha <= 0) return 0;
     }
    }
    float4 color = (flags & 1024) != 0 ? i.color : UruiLinearizePremultipliedColor(i.color);
    color = UruiSamplePalette(i.uv, i.slot, (flags >> 1) & 3) * color;
    if ((int)i.program.x == 1)
    {
     float3 rgb;
     float a;
     UruiCssBeginStraight(color, rgb, a);
     int filterCount = (int)UruiParameterTexel(i.program.y).x;
     [loop] for (int filterIndex = 0; filterIndex < filterCount; filterIndex++)
     {
      float4 value = UruiParameterTexel(i.program.y + 1 + filterIndex);
      UruiCssStepStraight(rgb, a, (int)value.x, value.y);
     }
     color = UruiCssEndStraight(rgb, a);
    }
    return color * maskAlpha;
   }
   // Lean parametric pass without DirectMask or CSS color filter branches/registers (used when batch proves neither).
   float4 UruiFragmentParametricFast(v2f i)
   {
    int flags = (int)i.flags;
    if ((flags & 1) != 0)
    {
     UruiParameterClipWindow(i.pixel.xy / i.pixel.z, i.clipRect);
    }
    float4 color = (flags & 1024) != 0 ? i.color : UruiLinearizePremultipliedColor(i.color);
    return UruiSamplePalette(i.uv, i.slot, (flags >> 1) & 3) * color;
   }

   struct completeInput { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 source:TEXCOORD1; float4 color:COLOR; };
   struct completeOutput { float4 position:SV_POSITION; float2 uv:TEXCOORD0; nointerpolation float slot:TEXCOORD1; float4 color:COLOR; };
   completeOutput vertComplete(completeInput value)
   {
    completeOutput result;
    // UnityObjectToClipPos supports both Matrix4x4.identity and shared batch affine transforms.
    result.position = UnityObjectToClipPos(value.vertex);
    result.uv = value.uv;
    result.slot = value.source.x;
    result.color = value.color;
    return result;
   }
   float4 UruiFragmentComplete(completeOutput value)
   {
    return UruiSamplePalette(value.uv, value.slot, 0) * UruiLinearizePremultipliedColor(value.color);
   }
   // Hardware-fast-only Complete pass (all slots proven kind 2/3/4: zero Load, zero software filter, minimal VGPRs).
   float4 UruiFragmentCompleteFast(completeOutput value)
   {
    return UruiSamplePaletteFast(value.uv, value.slot) * UruiLinearizePremultipliedColor(value.color);
   }

#endif
