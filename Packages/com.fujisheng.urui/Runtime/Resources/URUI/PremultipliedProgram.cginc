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
    float4 metadata=UruiParameter(v.source.y,7);
    float4 world=UruiOrdinaryParameterPosition(v.vertex,v.source.y,metadata.x);
    o.pos=mul(UNITY_MATRIX_VP,world);
    float4 uv=UruiParameter(v.source.y,6);
    o.uv=v.uv*uv.xy+uv.zw;
    o.slot=v.source.x;
    o.color=v.color*UruiParameter(v.source.y,5);
    o.pixel=float3(world.xy,world.w);
    o.clipRect=UruiParameter(v.source.y,4);
    o.flags=metadata.x;o.program=metadata.yzw;
    return o;
   }
   float4 UruiFragment(v2f i)
   {
    UruiParameterClip(float4(i.pixel.xy,0,i.pixel.z),i.clipRect,i.flags);
    float maskAlpha=1;
    if(((int)i.flags&256)!=0){maskAlpha=UruiDirectMask(i.pixel,i.program.z);if(maskAlpha<=0)return 0;}
    float4 color=UruiLinearizePremultipliedColor(i.color);
    color=UruiSamplePalette(i.uv,i.slot,((int)i.flags>>1)&3)*color;
    if((int)i.program.x==1)
    {
     color=UruiCssToEncoded(color);
     int filterCount=(int)UruiParameterTexel(i.program.y).x;
     [loop] for(int filterIndex=0;filterIndex<filterCount;filterIndex++)
     {
      float4 value=UruiParameterTexel(i.program.y+1+filterIndex);
      color=UruiCssColorStep(color,(int)value.x,value.y);
     }
     color=UruiCssFromEncoded(color);
    }
    return color*maskAlpha;
   }

   #include "UnityCG.cginc"
   #include "PremultipliedColor.cginc"
   #include "BorrowedTextureSampling.cginc"
   #include "PremultipliedPalette.cginc"
   struct completeInput {float4 vertex:POSITION;float2 uv:TEXCOORD0;float2 source:TEXCOORD1;float4 color:COLOR;};
   struct completeOutput {float4 position:SV_POSITION;float2 uv:TEXCOORD0;nointerpolation float slot:TEXCOORD1;float4 color:COLOR;};
   completeOutput vertComplete(completeInput value)
   {
    completeOutput result;result.position=mul(UNITY_MATRIX_VP,value.vertex);result.uv=value.uv;result.slot=value.source.x;result.color=value.color;return result;
   }
   float4 UruiFragmentComplete(completeOutput value)
   {return UruiSamplePalette(value.uv,value.slot,0)*UruiLinearizePremultipliedColor(value.color);}

#endif
