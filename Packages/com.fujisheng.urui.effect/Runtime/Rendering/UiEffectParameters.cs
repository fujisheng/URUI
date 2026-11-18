using System;
using UnityEngine;
using URUI.Rendering;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    // Fixed CPU/GPU schema. Component offsets match UiEffectParameters.cginc.
    internal static class UiEffectParameters
    {
        internal const int RowCount=44;
        internal const int ToneFilter=0;
        internal const int ToneIntensity=1;
        internal const int ColorFilter=2;
        internal const int ColorIntensity=3;
        internal const int Color=4;
        internal const int ColorGlow=8;
        internal const int ColorHueShift=10;
        internal const int ColorSaturationShift=12;
        internal const int ColorValueShift=14;
        internal const int ColorContrastShift=16;
        internal const int ColorBrightnessShift=18;
        internal const int ColorAlpha=20;
        internal const int TargetMode=22;
        internal const int TargetColor=24;
        internal const int TargetRange=28;
        internal const int TargetSoftness=29;
        internal const int SamplingFilter=30;
        internal const int SamplingIntensity=31;
        internal const int SamplingWidth=32;
        internal const int SamplingScale=33;
        internal const int AllowToModifyMeshShape=34;
        internal const int TransitionFilter=35;
        internal const int TransitionProgress=36;
        internal const int TransitionWidth=37;
        internal const int TransitionSoftness=38;
        internal const int TransitionRotation=39;
        internal const int TransitionRangeMin=40;
        internal const int TransitionRangeMax=41;
        internal const int TransitionAutoPlaySpeed=42;
        internal const int DetailIntensity=43;
        internal const int DetailThresholdMin=44;
        internal const int DetailThresholdMax=45;
        internal const int EdgeWidth=46;
        internal const int EdgeShinyProgress=47;
        internal const int EdgeShinyWidth=48;
        internal const int EdgeShinyAutoPlaySpeed=49;
        internal const int TransitionReverse=50;
        internal const int TransitionKeepAspectRatio=51;
        internal const int TransitionPatternReverse=52;
        internal const int TransitionColorGlow=53;
        internal const int EdgeColorGlow=54;
        internal const int TransitionTexture=56;
        internal const int TransitionTextureScaleX=58;
        internal const int TransitionTextureScaleY=59;
        internal const int TransitionTextureOffsetX=60;
        internal const int TransitionTextureOffsetY=61;
        internal const int TransitionTextureSpeedX=62;
        internal const int TransitionTextureSpeedY=63;
        internal const int DetailTexture=64;
        internal const int DetailTextureScaleX=66;
        internal const int DetailTextureScaleY=67;
        internal const int DetailTextureOffsetX=68;
        internal const int DetailTextureOffsetY=69;
        internal const int DetailTextureSpeedX=70;
        internal const int DetailTextureSpeedY=71;
        internal const int TransitionColorFilter=72;
        internal const int TransitionColorHueShift=74;
        internal const int TransitionColorSaturationShift=76;
        internal const int TransitionColorValueShift=78;
        internal const int TransitionColorContrastShift=80;
        internal const int TransitionColorBrightnessShift=82;
        internal const int TransitionColorAlpha=84;
        internal const int EdgeColorFilter=86;
        internal const int EdgeColorHueShift=88;
        internal const int EdgeColorSaturationShift=90;
        internal const int EdgeColorValueShift=92;
        internal const int EdgeColorContrastShift=94;
        internal const int EdgeColorBrightnessShift=96;
        internal const int EdgeColorAlpha=98;
        internal const int TransitionColor=100;
        internal const int DetailColor=104;
        internal const int EdgeColor=108;
        internal const int DetailFilter=112;
        internal const int EdgeMode=113;
        internal const int PatternArea=114;
        internal const int GradationMode=115;
        internal const int GradationColorFilter=116;
        internal const int GradationIntensity=117;
        internal const int GradationOffset=118;
        internal const int GradationScale=119;
        internal const int GradationRotation=120;
        internal const int GradationGradient=122;
        internal const int GradationColor1=124;
        internal const int GradationColor2=128;
        internal const int GradationColor3=132;
        internal const int GradationColor4=136;
        internal const int ShadowMode=140;
        internal const int ShadowDistanceX=141;
        internal const int ShadowDistanceY=142;
        internal const int ShadowIteration=143;
        internal const int ShadowFade=144;
        internal const int ShadowMirrorScale=145;
        internal const int ShadowBlurIntensity=146;
        internal const int Flip=147;
        internal const int ShadowColorFilter=148;
        internal const int ShadowColor=152;
        internal const int ShadowColorGlow=156;
        internal const int ShadowColorHueShift=158;
        internal const int ShadowColorSaturationShift=160;
        internal const int ShadowColorValueShift=162;
        internal const int ShadowColorContrastShift=164;
        internal const int ShadowColorBrightnessShift=166;
        internal const int ShadowColorAlpha=168;
        internal const int BlendType=170;
        internal const int SrcBlendMode=171;
        internal const int DstBlendMode=172;
        internal static readonly RenderParameterLayout Layout=new(RowCount,new RenderParameterField[]{
            new(new StylePropertyDefinition("--urui-fx-tone-filter",StyleValueType.Keyword),ToneFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("grayscale",1),new("sepia",2),new("negative",3),new("retro",4),new("posterize",5)}),
            new(new StylePropertyDefinition("--urui-fx-tone-intensity",StyleValueType.Number),ToneIntensity),
            new(new StylePropertyDefinition("--urui-fx-color-filter",StyleValueType.Keyword),ColorFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("multiply",1),new("additive",2),new("subtractive",3),new("replace",4),new("multiply-luminance",5),new("multiply-additive",6),new("hsv-modifier",7),new("contrast",8)}),
            new(new StylePropertyDefinition("--urui-fx-color-intensity",StyleValueType.Number),ColorIntensity),
            new(new StylePropertyDefinition("--urui-fx-color",StyleValueType.Color),Color),
            new(new StylePropertyDefinition("--urui-fx-color-glow",StyleValueType.Keyword),ColorGlow,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-color-hue-shift",StyleValueType.Number),ColorHueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-color-saturation-shift",StyleValueType.Number),ColorSaturationShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-color-value-shift",StyleValueType.Number),ColorValueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-color-contrast-shift",StyleValueType.Number),ColorContrastShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-color-brightness-shift",StyleValueType.Number),ColorBrightnessShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-color-alpha",StyleValueType.Number),ColorAlpha,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-target-mode",StyleValueType.Keyword),TargetMode,keywords:new RenderKeywordMapping[]{new("none",0),new("hue",1),new("luminance",2)}),
            new(new StylePropertyDefinition("--urui-fx-target-color",StyleValueType.Color),TargetColor),
            new(new StylePropertyDefinition("--urui-fx-target-range",StyleValueType.Number),TargetRange),
            new(new StylePropertyDefinition("--urui-fx-target-softness",StyleValueType.Number),TargetSoftness),
            new(new StylePropertyDefinition("--urui-fx-sampling-filter",StyleValueType.Keyword),SamplingFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("blur-fast",1),new("blur-medium",2),new("blur-detail",3),new("pixelation",4),new("rgb-shift",5),new("edge-luminance",6),new("edge-alpha",7)}),
            new(new StylePropertyDefinition("--urui-fx-sampling-intensity",StyleValueType.Number),SamplingIntensity),
            new(new StylePropertyDefinition("--urui-fx-sampling-width",StyleValueType.Number),SamplingWidth),
            new(new StylePropertyDefinition("--urui-fx-sampling-scale",StyleValueType.Number),SamplingScale),
            new(new StylePropertyDefinition("--urui-fx-allow-to-modify-mesh-shape",StyleValueType.Keyword),AllowToModifyMeshShape,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-transition-filter",StyleValueType.Keyword),TransitionFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("fade",1),new("cutoff",2),new("dissolve",3),new("shiny",4),new("mask",5),new("melt",6),new("burn",7),new("pattern",8)}),
            new(new StylePropertyDefinition("--urui-fx-transition-progress",StyleValueType.Number),TransitionProgress),
            new(new StylePropertyDefinition("--urui-fx-transition-width",StyleValueType.Number),TransitionWidth),
            new(new StylePropertyDefinition("--urui-fx-transition-softness",StyleValueType.Number),TransitionSoftness),
            new(new StylePropertyDefinition("--urui-fx-transition-rotation",StyleValueType.Number),TransitionRotation),
            new(new StylePropertyDefinition("--urui-fx-transition-range-min",StyleValueType.Number),TransitionRangeMin),
            new(new StylePropertyDefinition("--urui-fx-transition-range-max",StyleValueType.Number),TransitionRangeMax),
            new(new StylePropertyDefinition("--urui-fx-transition-auto-play-speed",StyleValueType.Number),TransitionAutoPlaySpeed),
            new(new StylePropertyDefinition("--urui-fx-detail-intensity",StyleValueType.Number),DetailIntensity),
            new(new StylePropertyDefinition("--urui-fx-detail-threshold-min",StyleValueType.Number),DetailThresholdMin),
            new(new StylePropertyDefinition("--urui-fx-detail-threshold-max",StyleValueType.Number),DetailThresholdMax),
            new(new StylePropertyDefinition("--urui-fx-edge-width",StyleValueType.Number),EdgeWidth),
            new(new StylePropertyDefinition("--urui-fx-edge-shiny-progress",StyleValueType.Number),EdgeShinyProgress),
            new(new StylePropertyDefinition("--urui-fx-edge-shiny-width",StyleValueType.Number),EdgeShinyWidth),
            new(new StylePropertyDefinition("--urui-fx-edge-shiny-auto-play-speed",StyleValueType.Number),EdgeShinyAutoPlaySpeed),
            new(new StylePropertyDefinition("--urui-fx-transition-reverse",StyleValueType.Keyword),TransitionReverse,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-transition-keep-aspect-ratio",StyleValueType.Keyword),TransitionKeepAspectRatio,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-transition-pattern-reverse",StyleValueType.Keyword),TransitionPatternReverse,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-glow",StyleValueType.Keyword),TransitionColorGlow,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-glow",StyleValueType.Keyword),EdgeColorGlow,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-transition-texture",StyleValueType.Image),TransitionTexture,originOffset:57),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-scale-x",StyleValueType.Number),TransitionTextureScaleX),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-scale-y",StyleValueType.Number),TransitionTextureScaleY),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-offset-x",StyleValueType.Number),TransitionTextureOffsetX),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-offset-y",StyleValueType.Number),TransitionTextureOffsetY),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-speed-x",StyleValueType.Number),TransitionTextureSpeedX),
            new(new StylePropertyDefinition("--urui-fx-transition-texture-speed-y",StyleValueType.Number),TransitionTextureSpeedY),
            new(new StylePropertyDefinition("--urui-fx-detail-texture",StyleValueType.Image),DetailTexture,originOffset:65),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-scale-x",StyleValueType.Number),DetailTextureScaleX),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-scale-y",StyleValueType.Number),DetailTextureScaleY),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-offset-x",StyleValueType.Number),DetailTextureOffsetX),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-offset-y",StyleValueType.Number),DetailTextureOffsetY),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-speed-x",StyleValueType.Number),DetailTextureSpeedX),
            new(new StylePropertyDefinition("--urui-fx-detail-texture-speed-y",StyleValueType.Number),DetailTextureSpeedY),
            new(new StylePropertyDefinition("--urui-fx-transition-color-filter",StyleValueType.Keyword),TransitionColorFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("multiply",1),new("additive",2),new("subtractive",3),new("replace",4),new("multiply-luminance",5),new("multiply-additive",6),new("hsv-modifier",7),new("contrast",8)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-hue-shift",StyleValueType.Number),TransitionColorHueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-saturation-shift",StyleValueType.Number),TransitionColorSaturationShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-value-shift",StyleValueType.Number),TransitionColorValueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-contrast-shift",StyleValueType.Number),TransitionColorContrastShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-brightness-shift",StyleValueType.Number),TransitionColorBrightnessShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color-alpha",StyleValueType.Number),TransitionColorAlpha,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-filter",StyleValueType.Keyword),EdgeColorFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("multiply",1),new("additive",2),new("subtractive",3),new("replace",4),new("multiply-luminance",5),new("multiply-additive",6),new("hsv-modifier",7),new("contrast",8)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-hue-shift",StyleValueType.Number),EdgeColorHueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-saturation-shift",StyleValueType.Number),EdgeColorSaturationShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-value-shift",StyleValueType.Number),EdgeColorValueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-contrast-shift",StyleValueType.Number),EdgeColorContrastShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-brightness-shift",StyleValueType.Number),EdgeColorBrightnessShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-edge-color-alpha",StyleValueType.Number),EdgeColorAlpha,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-transition-color",StyleValueType.Color),TransitionColor),
            new(new StylePropertyDefinition("--urui-fx-detail-color",StyleValueType.Color),DetailColor),
            new(new StylePropertyDefinition("--urui-fx-edge-color",StyleValueType.Color),EdgeColor),
            new(new StylePropertyDefinition("--urui-fx-detail-filter",StyleValueType.Keyword),DetailFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("masking",1),new("multiply",2),new("additive",3),new("subtractive",6),new("replace",4),new("multiply-additive",5)}),
            new(new StylePropertyDefinition("--urui-fx-edge-mode",StyleValueType.Keyword),EdgeMode,keywords:new RenderKeywordMapping[]{new("none",0),new("plain",1),new("shiny",2)}),
            new(new StylePropertyDefinition("--urui-fx-pattern-area",StyleValueType.Keyword),PatternArea,keywords:new RenderKeywordMapping[]{new("all",0),new("inner",1),new("edge",2)}),
            new(new StylePropertyDefinition("--urui-fx-gradation-mode",StyleValueType.Keyword),GradationMode,keywords:new RenderKeywordMapping[]{new("none",0),new("horizontal",1),new("horizontal-gradient",2),new("vertical",3),new("vertical-gradient",4),new("radial-fast",5),new("radial-detail",6),new("diagonal-to-right-bottom",7),new("diagonal-to-left-bottom",8),new("angle",9),new("angle-gradient",10),new("diagonal",11)}),
            new(new StylePropertyDefinition("--urui-fx-gradation-color-filter",StyleValueType.Keyword),GradationColorFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("multiply",1),new("additive",2),new("subtractive",3),new("replace",4),new("multiply-luminance",5),new("multiply-additive",6)}),
            new(new StylePropertyDefinition("--urui-fx-gradation-intensity",StyleValueType.Number),GradationIntensity),
            new(new StylePropertyDefinition("--urui-fx-gradation-offset",StyleValueType.Number),GradationOffset),
            new(new StylePropertyDefinition("--urui-fx-gradation-scale",StyleValueType.Number),GradationScale),
            new(new StylePropertyDefinition("--urui-fx-gradation-rotation",StyleValueType.Number),GradationRotation),
            new(new StylePropertyDefinition("--urui-fx-gradation-gradient",StyleValueType.Image),GradationGradient,originOffset:123),
            new(new StylePropertyDefinition("--urui-fx-gradation-color1",StyleValueType.Color),GradationColor1),
            new(new StylePropertyDefinition("--urui-fx-gradation-color2",StyleValueType.Color),GradationColor2),
            new(new StylePropertyDefinition("--urui-fx-gradation-color3",StyleValueType.Color),GradationColor3),
            new(new StylePropertyDefinition("--urui-fx-gradation-color4",StyleValueType.Color),GradationColor4),
            new(new StylePropertyDefinition("--urui-fx-shadow-mode",StyleValueType.Keyword),ShadowMode,keywords:new RenderKeywordMapping[]{new("none",0),new("shadow",1),new("shadow3",2),new("outline",3),new("outline8",4),new("mirror",5)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-distance-x",StyleValueType.Number),ShadowDistanceX),
            new(new StylePropertyDefinition("--urui-fx-shadow-distance-y",StyleValueType.Number),ShadowDistanceY),
            new(new StylePropertyDefinition("--urui-fx-shadow-iteration",StyleValueType.Number),ShadowIteration),
            new(new StylePropertyDefinition("--urui-fx-shadow-fade",StyleValueType.Number),ShadowFade),
            new(new StylePropertyDefinition("--urui-fx-shadow-mirror-scale",StyleValueType.Number),ShadowMirrorScale),
            new(new StylePropertyDefinition("--urui-fx-shadow-blur-intensity",StyleValueType.Number),ShadowBlurIntensity),
            new(new StylePropertyDefinition("--urui-fx-flip",StyleValueType.Number),Flip),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-filter",StyleValueType.Keyword),ShadowColorFilter,keywords:new RenderKeywordMapping[]{new("none",0),new("multiply",1),new("additive",2),new("subtractive",3),new("replace",4),new("multiply-luminance",5),new("multiply-additive",6),new("hsv-modifier",7),new("contrast",8)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color",StyleValueType.Color),ShadowColor),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-glow",StyleValueType.Keyword),ShadowColorGlow,keywords:new RenderKeywordMapping[]{new("false",0),new("true",1)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-hue-shift",StyleValueType.Number),ShadowColorHueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-saturation-shift",StyleValueType.Number),ShadowColorSaturationShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-value-shift",StyleValueType.Number),ShadowColorValueShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-contrast-shift",StyleValueType.Number),ShadowColorContrastShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-brightness-shift",StyleValueType.Number),ShadowColorBrightnessShift,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-shadow-color-alpha",StyleValueType.Number),ShadowColorAlpha,keywords:new RenderKeywordMapping[]{new("auto",0)}),
            new(new StylePropertyDefinition("--urui-fx-blend-type",StyleValueType.Keyword),BlendType,keywords:new RenderKeywordMapping[]{new("custom",0),new("alpha-blend",1),new("multiply",2),new("additive",3),new("soft-additive",4),new("multiply-additive",5)}),
            new(new StylePropertyDefinition("--urui-fx-src-blend-mode",StyleValueType.Keyword),SrcBlendMode,keywords:new RenderKeywordMapping[]{new("zero",0),new("one",1),new("dst-color",2),new("src-color",3),new("one-minus-dst-color",4),new("src-alpha",5),new("one-minus-src-color",6),new("dst-alpha",7),new("one-minus-dst-alpha",8),new("src-alpha-saturate",9),new("one-minus-src-alpha",10)}),
            new(new StylePropertyDefinition("--urui-fx-dst-blend-mode",StyleValueType.Keyword),DstBlendMode,keywords:new RenderKeywordMapping[]{new("zero",0),new("one",1),new("dst-color",2),new("src-color",3),new("one-minus-dst-color",4),new("src-alpha",5),new("one-minus-src-color",6),new("dst-alpha",7),new("one-minus-dst-alpha",8),new("src-alpha-saturate",9),new("one-minus-src-alpha",10)}),
        });
        internal static float Number(ReadOnlySpan<Vector4> values,int offset)=>values[offset/4][offset%4];
        internal static void Set(Span<Vector4> values,int offset,float number){var row=values[offset/4];row[offset%4]=number;values[offset/4]=row;}
        internal static void ApplyOverrides(Span<Vector4> values,EffectStyleView style)
        {
            for(var fieldIndex=0;fieldIndex<Layout.Fields.Count;fieldIndex++)
            {
                var field=Layout.Fields[fieldIndex];
                if(!style.TryOverride(field.Property.Name,out var value))continue;
                var offset=field.ComponentOffset;
                if(value.TryGetNumber(out var number))
                {Set(values,offset,number);if(field.Keywords.Count!=0)Set(values,offset+1,0);}
                else if(value.TryGetColor(out var color))
                {for(var i=0;i<4;i++)Set(values,offset+i,color[i]);}
                else if(value.TryGetKeyword(out var keyword))
                {
                    var found=false;
                    for(var i=0;i<field.Keywords.Count;i++)if(field.Keywords[i].Keyword==keyword){Set(values,offset,field.Keywords[i].Value);found=true;break;}
                    if(!found)throw new InvalidOperationException("控制器关键字缺少固定映射："+field.Property.Name);
                }
                else throw new InvalidOperationException("控制器覆盖类型无效。");
            }
        }
        internal static void Write(Span<Vector4> values,EffectParameters p)
        {
            values.Clear();
            Set(values,ToneFilter,(float)p.ToneFilter);
            Set(values,ToneIntensity,p.ToneIntensity);
            Set(values,ColorFilter,(float)p.ColorFilter);
            Set(values,ColorIntensity,p.ColorIntensity);
            values[Color/4]=p.Color;
            Set(values,ColorGlow,(p.ColorGlow?1:0));
            Set(values,ColorHueShift+1,1);
            Set(values,ColorSaturationShift+1,1);
            Set(values,ColorValueShift+1,1);
            Set(values,ColorContrastShift+1,1);
            Set(values,ColorBrightnessShift+1,1);
            Set(values,ColorAlpha+1,1);
            Set(values,TargetMode,(float)p.TargetMode);
            values[TargetColor/4]=p.TargetColor;
            Set(values,TargetRange,p.TargetRange);
            Set(values,TargetSoftness,p.TargetSoftness);
            Set(values,SamplingFilter,(float)p.SamplingFilter);
            Set(values,SamplingIntensity,p.SamplingIntensity);
            Set(values,SamplingWidth,p.SamplingWidth);
            Set(values,SamplingScale,p.SamplingScale);
            Set(values,AllowToModifyMeshShape,(p.AllowToModifyMeshShape?1:0));
            Set(values,TransitionFilter,(float)p.TransitionFilter);
            Set(values,TransitionProgress,p.TransitionProgress);
            Set(values,TransitionWidth,p.TransitionWidth);
            Set(values,TransitionSoftness,p.TransitionSoftness);
            Set(values,TransitionRotation,p.TransitionRotation);
            Set(values,TransitionRangeMin,p.TransitionRange.Min);
            Set(values,TransitionRangeMax,p.TransitionRange.Max);
            Set(values,TransitionAutoPlaySpeed,p.TransitionAutoPlaySpeed);
            Set(values,DetailIntensity,p.DetailIntensity);
            Set(values,DetailThresholdMin,p.DetailThreshold.Min);
            Set(values,DetailThresholdMax,p.DetailThreshold.Max);
            Set(values,EdgeWidth,p.EdgeWidth);
            Set(values,EdgeShinyProgress,p.EdgeShinyProgress);
            Set(values,EdgeShinyWidth,p.EdgeShinyWidth);
            Set(values,EdgeShinyAutoPlaySpeed,p.EdgeShinyAutoPlaySpeed);
            Set(values,TransitionReverse,(p.TransitionReverse?1:0));
            Set(values,TransitionKeepAspectRatio,(p.TransitionKeepAspectRatio?1:0));
            Set(values,TransitionPatternReverse,(p.TransitionPatternReverse?1:0));
            Set(values,TransitionColorGlow,(p.TransitionColorGlow?1:0));
            Set(values,EdgeColorGlow,(p.EdgeColorGlow?1:0));
            Set(values,TransitionTextureScaleX,p.TransitionTextureScale.x);
            Set(values,TransitionTextureScaleY,p.TransitionTextureScale.y);
            Set(values,TransitionTextureOffsetX,p.TransitionTextureOffset.x);
            Set(values,TransitionTextureOffsetY,p.TransitionTextureOffset.y);
            Set(values,TransitionTextureSpeedX,p.TransitionTextureSpeed.x);
            Set(values,TransitionTextureSpeedY,p.TransitionTextureSpeed.y);
            Set(values,DetailTextureScaleX,p.DetailTextureScale.x);
            Set(values,DetailTextureScaleY,p.DetailTextureScale.y);
            Set(values,DetailTextureOffsetX,p.DetailTextureOffset.x);
            Set(values,DetailTextureOffsetY,p.DetailTextureOffset.y);
            Set(values,DetailTextureSpeedX,p.DetailTextureSpeed.x);
            Set(values,DetailTextureSpeedY,p.DetailTextureSpeed.y);
            Set(values,TransitionColorFilter,(float)p.TransitionColorFilter);
            Set(values,TransitionColorHueShift+1,1);
            Set(values,TransitionColorSaturationShift+1,1);
            Set(values,TransitionColorValueShift+1,1);
            Set(values,TransitionColorContrastShift+1,1);
            Set(values,TransitionColorBrightnessShift+1,1);
            Set(values,TransitionColorAlpha+1,1);
            Set(values,EdgeColorFilter,(float)p.EdgeColorFilter);
            Set(values,EdgeColorHueShift+1,1);
            Set(values,EdgeColorSaturationShift+1,1);
            Set(values,EdgeColorValueShift+1,1);
            Set(values,EdgeColorContrastShift+1,1);
            Set(values,EdgeColorBrightnessShift+1,1);
            Set(values,EdgeColorAlpha+1,1);
            values[TransitionColor/4]=p.TransitionColor;
            values[DetailColor/4]=p.DetailColor;
            values[EdgeColor/4]=p.EdgeColor;
            Set(values,DetailFilter,(float)p.DetailFilter);
            Set(values,EdgeMode,(float)p.EdgeMode);
            Set(values,PatternArea,(float)p.PatternArea);
            Set(values,GradationMode,(float)p.GradationMode);
            Set(values,GradationColorFilter,(float)p.GradationColorFilter);
            Set(values,GradationIntensity,p.GradationIntensity);
            Set(values,GradationOffset,p.GradationOffset);
            Set(values,GradationScale,p.GradationScale);
            Set(values,GradationRotation,p.GradationRotation);
            values[GradationColor1/4]=p.GradationColor1;
            values[GradationColor2/4]=p.GradationColor2;
            values[GradationColor3/4]=p.GradationColor3;
            values[GradationColor4/4]=p.GradationColor4;
            Set(values,ShadowMode,(float)p.ShadowMode);
            Set(values,ShadowDistanceX,p.ShadowDistance.x);
            Set(values,ShadowDistanceY,p.ShadowDistance.y);
            Set(values,ShadowIteration,p.ShadowIteration);
            Set(values,ShadowFade,p.ShadowFade);
            Set(values,ShadowMirrorScale,p.ShadowMirrorScale);
            Set(values,ShadowBlurIntensity,p.ShadowBlurIntensity);
            Set(values,Flip,(float)p.Flip);
            Set(values,ShadowColorFilter,(float)p.ShadowColorFilter);
            values[ShadowColor/4]=p.ShadowColor;
            Set(values,ShadowColorGlow,(p.ShadowColorGlow?1:0));
            Set(values,ShadowColorHueShift+1,1);
            Set(values,ShadowColorSaturationShift+1,1);
            Set(values,ShadowColorValueShift+1,1);
            Set(values,ShadowColorContrastShift+1,1);
            Set(values,ShadowColorBrightnessShift+1,1);
            Set(values,ShadowColorAlpha+1,1);
            Set(values,BlendType,(float)p.BlendType);
            Set(values,SrcBlendMode,(float)p.SrcBlendMode);
            Set(values,DstBlendMode,(float)p.DstBlendMode);
        }
    }
}
