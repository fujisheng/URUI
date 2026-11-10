Shader "URUI.Effect/UiEffect"
{
    Properties
    {
        _MainTex("Input", 2D) = "white" {}
        _TransitionTex("Transition Pattern", 2D) = "white" {}
        _DetailTex("Detail Pattern", 2D) = "white" {}
        _GradationTex("Gradient", 2D) = "white" {}
        _SrcBlend("Source Blend", Float) = 1
        _DstBlend("Destination Blend", Float) = 10
        _ToneFilter("Tone", Int) = 0
        _ToneIntensity("Tone Intensity", Float) = 1
        _ColorFilter("Color", Int) = 0
        // 源插件是原始数值 uniform；Vector 避免 ShaderLab Color 在 Linear 项目中自动转换。
        _ColorValue("Color Value", Vector) = (1,1,1,1)
        _ColorIntensity("Color Intensity", Float) = 1
        _ColorGlow("Color Glow", Int) = 0
        _TargetMode("Target Mode", Int) = 0
        _TargetColor("Target Color", Vector) = (1,1,1,1)
        _TargetRange("Target Range", Float) = .1
        _TargetSoftness("Target Softness", Float) = .5
        _InputBottomLeft("Input Origin", Float) = 0
        _InputPremultiplied("Input Alpha", Float) = 0
        _InputCaptured("Captured Input", Float) = 0
        _UseVertexColor("Vertex Color", Float) = 0
        _SourceUvBounds("Input UV", Vector) = (0,0,1,1)
        _SourceTexel("Input Texel", Vector) = (1,1,1,1)
        _SamplingFilter("Sampling", Int) = 0
        _SamplingIntensity("Sampling Intensity", Float) = .5
        _SamplingWidth("Sampling Width", Float) = 1
        _SamplingScale("Sampling Scale", Float) = 1
        [HideInInspector] _UruiPaintOpacity("Opacity", Float) = 1
        [HideInInspector] _UruiClipEnabled("Clip", Float) = 0
        [HideInInspector] _UruiClipRect("Clip Rect", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_local _ URUI_BLUR_FAST URUI_BLUR_MEDIUM URUI_BLUR_DETAIL
            #include "UnityCG.cginc"
            #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
            #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
            #include "ColorOps.cginc"
            sampler2D _MainTex;
            int _ToneFilter, _ColorFilter, _ColorGlow, _TargetMode;
            float _ToneIntensity, _ColorIntensity, _TargetRange, _TargetSoftness;
            float4 _ColorValue, _TargetColor;
            float _InputBottomLeft, _InputPremultiplied, _InputCaptured, _UseVertexColor, _UseEffectGeometry;
            float4 _SourceUvBounds, _SourceTexel;
            int _SamplingFilter;
            float _SamplingIntensity, _SamplingWidth, _SamplingScale;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 effect:TEXCOORD1; float4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float4 pixel:TEXCOORD1; float2 effect:TEXCOORD2; float4 color:COLOR; };
            Output Vertex(Input input)
            {
                Output output; output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv; output.pixel = mul(unity_ObjectToWorld, input.vertex);
                output.effect = _UseEffectGeometry > .5 ? input.effect : 0;
                output.color = _UseVertexColor > .5 ? UruiLinearizePremultipliedColor(input.color) : 1;
                return output;
            }
            float4 SampleSource(float2 uv)
            {
                float2 center = (_SourceUvBounds.xy+_SourceUvBounds.zw)*.5;
                bool fullTexture=all(_SourceUvBounds.xy==0)&&all(_SourceUvBounds.zw==1);
                if (!fullTexture)
                { uv = clamp(uv,min(_SourceUvBounds.xy+_SourceTexel.xy*.5,center),max(_SourceUvBounds.zw-_SourceTexel.xy*.5,center)); }
                if (_InputBottomLeft > .5) { uv.y = 1 - uv.y; }
                // 原图保留源插件的隐式采样；捕获图统一 mip0，不能因 padding 改变采样方式。
                float4 color = fullTexture && _InputCaptured < .5 ? tex2D(_MainTex,uv) : tex2Dlod(_MainTex, float4(uv,0,0));
                if (_InputPremultiplied < .5) { color.rgb *= color.a; }
                else if (_InputCaptured < .5) { color = UruiLinearizePremultipliedColor(color); }
                return color;
            }
            #include "Sampling.cginc"
            #include "Transition.cginc"
            #include "Detail.cginc"
            #include "Edge.cginc"
            #include "Gradation.cginc"
            #include "Shadow.cginc"
            float4x4 _EffectRootMatrix, _EffectGradMatrix, _EffectMirrorRootMatrix, _EffectMirrorGradMatrix;
            float2 EffectCoordinate(float4x4 coordinateTransform,float2 pixel)
            {
                float4 coordinate=mul(coordinateTransform,float4(pixel,0,1));
                return abs(coordinate.w)>1e-6 ? coordinate.xy/coordinate.w : 0;
            }
            float4 UruiEffectInternal(float2 uv, float2 local, float2 gradation, float shadow)
            {
                half alpha = UruiTransitionAlpha(local);
                float edgeFactor = UruiEdgeFactor(uv);
                uv += UruiTransitionMove(alpha);
                float4 color = UruiSamplingBase(uv, shadow > .5 ? _ShadowBlurIntensity : _SamplingIntensity);
                color = UruiGradation(color, gradation);
                color = UruiTone(color, _ToneFilter, _ToneIntensity);
                color = UruiTransition(color, alpha, local, edgeFactor);
                color = UruiDetail(color, local);
                if (shadow > .5) return UruiShadow(color);
                color = UruiColor(color, _ColorValue, _ColorIntensity, _ColorFilter, _ColorGlow, false);
                return UruiEdge(color, edgeFactor, local);
            }
            float4 Fragment(Output input):SV_Target
            {
                float2 pixel=abs(input.pixel.w)>1e-6 ? input.pixel.xy/input.pixel.w : 0;
                UruiClipPaint(pixel);
                float2 uv = input.uv;
                float2 local = saturate(EffectCoordinate(_EffectRootMatrix,pixel));
                float2 gradation = EffectCoordinate(_EffectGradMatrix,pixel);
                if (input.effect.y > .5)
                {
                    local = saturate(EffectCoordinate(_EffectMirrorRootMatrix,pixel));
                    gradation = saturate(EffectCoordinate(_EffectMirrorGradMatrix,pixel));
                }
                float4 original = UruiSampleClamped(uv);
                float rate = UruiTargetRate(original.rgb, _TargetMode, _TargetColor.rgb, _TargetRange, _TargetSoftness);
                #if defined(URUI_BLUR_FAST) || defined(URUI_BLUR_MEDIUM) || defined(URUI_BLUR_DETAIL)
                // 程序 key 保证 blur 变体只用于三档 blur；排除不可达的 RGBShift 三次展开核。
                float4 color=lerp(original,UruiEffectInternal(uv,local,gradation,input.effect.x),rate);
                #else
                if (_SamplingFilter == 4)
                {
                    // 捕获的 padding 只属于存储，不得平移或缩放像素化网格。
                    float2 domain = _InputCaptured > .5 ? max(_SourceUvBounds.zw-_SourceUvBounds.xy,float2(1e-6,1e-6)) : float2(1,1);
                    float2 origin = _InputCaptured > .5 ? _SourceUvBounds.xy : float2(0,0);
                    uv = (uv-origin)/domain;
                    half2 pixels = max(2,(1-lerp(.5,.95,_SamplingIntensity))/(UruiSamplingTexel()/domain));
                    // 非整数网格的 round 与 Y 翻转不能交换；按源纹理坐标量化后再返回 URUI UV。
                    if (_InputBottomLeft > .5) { uv.y=1-uv.y; }
                    uv=round(uv*pixels)/pixels;
                    if (_InputBottomLeft > .5) { uv.y=1-uv.y; }
                    uv = origin+uv*domain;
                }
                float4 color;
                if (_SamplingFilter == 5)
                {
                    half2 offset=half2(_SamplingIntensity*UruiSamplingTexel().x*20,0);
                    half2 r=UruiEffectInternal(uv+offset,local,gradation,input.effect.x).ra,g=UruiEffectInternal(uv,local,gradation,input.effect.x).ga,b=UruiEffectInternal(uv-offset,local,gradation,input.effect.x).ba;
                    color=float4(r.x*r.y,g.x*g.y,b.x*b.y,(r.y+g.y+b.y)/3);
                }
                else if (_SamplingFilter == 6 || _SamplingFilter == 7) { color=UruiEffectInternal(uv,local,gradation,input.effect.x)*UruiSamplingEdge(uv); }
                else { color=lerp(original,UruiEffectInternal(uv,local,gradation,input.effect.x),rate); }
                #endif
                return color * input.color * _UruiPaintOpacity;
            }
            ENDCG
        }
    }
}
