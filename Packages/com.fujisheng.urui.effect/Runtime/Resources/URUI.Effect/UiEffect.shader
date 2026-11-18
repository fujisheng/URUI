Shader "URUI.Effect/UiEffect"
{
    Properties
    {
        _MainTex("Input",2D)="white" {}
        _TransitionTex("Transition Pattern",2D)="white" {}
        _DetailTex("Detail Pattern",2D)="white" {}
        _GradationTex("Gradient",2D)="white" {}
        [HideInInspector] _UruiTriangleFacts("Triangle Facts",2D)="black" {}
        [HideInInspector] _UruiParameters("Parameters",2D)="black" {}
        [HideInInspector] _UruiParameterSize("Parameter Size",Vector)=(1,1,1,1)
        _SrcBlend("Source Blend",Float)=1
        _DstBlend("Destination Blend",Float)=10
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
            #pragma multi_compile_local _ URUI_COMPLEX_PIXEL
            #include "UnityCG.cginc"
            #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
            sampler2D _MainTex;
            #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ImageParameters.cginc"
            #include "ColorOps.cginc"
            #include "UiEffectParameters.cginc"
            #include "UiEffectVertices.cginc"
            float4 SampleSource(float2 uv)
            {
                float2 center = (_SourceUvBounds.xy+_SourceUvBounds.zw)*.5;
                bool fullTexture=all(_SourceUvBounds.xy==0)&&all(_SourceUvBounds.zw==1);
                if (!fullTexture)
                { uv = clamp(uv,min(_SourceUvBounds.xy+_SourceTexel.xy*.5,center),max(_SourceUvBounds.zw-_SourceTexel.xy*.5,center)); }
                if (_InputBottomLeft > .5) { uv.y = 1 - uv.y; }
                // 原图保留源插件的隐式采样；捕获图统一 mip0，不能因 padding 改变采样方式。
                float4 color = fullTexture && _InputCaptured < .5 ? UruiImageSample(uv,_UiTextureSlot) : UruiImageSampleLevel(uv,_UiTextureSlot);
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
            float4 UruiEffectInternal(float2 uv, float2 local, float2 gradation, float shadow)
            {
                #if defined(URUI_COMPLEX_PIXEL)
                // 必须在调用前判断模式；函数参数会先读取整组颜色/范围字段。
                int transitionMode=_TransitionFilter,edgeMode=_EdgeMode;
                half alpha=1;if(transitionMode!=0)alpha=UruiTransitionAlpha(local);
                float edgeFactor=0;if(edgeMode!=0)edgeFactor=UruiEdgeFactor(uv);
                if(transitionMode==6||transitionMode==7)uv+=UruiTransitionMove(alpha);
                #endif
                float4 color=UruiSamplingBase(uv,shadow>.5?_ShadowBlurIntensity:_SamplingIntensity);
                #if defined(URUI_COMPLEX_PIXEL)
                if(_GradationMode!=0)color=UruiGradation(color,gradation);
                #endif
                if(_ToneFilter!=0)color=UruiTone(color,_ToneFilter,_ToneIntensity);
                #if defined(URUI_COMPLEX_PIXEL)
                if(transitionMode!=0)color=UruiTransition(color,alpha,local,edgeFactor);
                if(_DetailFilter!=0)color=UruiDetail(color,local);
                #endif
                if(shadow>.5)return UruiShadow(color);
                if(_ColorFilter!=0)color=UruiColor(color,_ColorValue,_ColorIntensity,_ColorFilter,_ColorGlow,false);
                #if defined(URUI_COMPLEX_PIXEL)
                if(edgeMode!=0)color=UruiEdge(color,edgeFactor,local);
                #endif
                return color;
            }
            float4 Fragment(UiOutput input):SV_Target
            {
                _UiRuntimeBase=input.state.z;_UiFieldBase=_UiRuntimeBase+16;_UiTime=input.runtime.z;_UiTextureSlot=input.state.y;_UiInputFlags=(int)input.runtime.w;
                _UiUvBounds=UruiUiRuntime(3);_UiTexel=UruiUiRuntime(4);
                UruiParameterClip(input.pixel,input.clipRect,input.state.x);
                float2 uv=input.uv;float2 local=0,gradation=0;
                #if defined(URUI_COMPLEX_PIXEL)
                bool mirror=input.state.w>1.5;
                // 坐标仅供已启用的效果使用；普通调色不承担逐像素旋转和尺寸读取。
                bool usesLocal=_TransitionFilter!=0||_DetailFilter!=0||_EdgeMode==2;
                if(usesLocal||_GradationMode!=0)
                {
                    float2 root=abs(input.root.w)>1e-6?input.root.xy/input.root.w:0;
                    if(mirror&&_ShadowMirrorScale>0){float4 rect=UruiUiRuntime(6);root.y=(2*rect.y+rect.w*(1+_ShadowMirrorScale)-UruiUiDistance().y-root.y)/_ShadowMirrorScale;}
                    if(usesLocal)local=saturate(UruiUiCoordinate(root,_TransitionRotation));
                    if(_GradationMode!=0)
                    {
                        float rotation=_GradationMode==8?135:_GradationMode==7?45:_GradationMode==3?90:_GradationMode==4?-90:_GradationMode==9||_GradationMode==10?_GradationRotation:0;
                        gradation=UruiUiCoordinate(root,rotation);if(mirror)gradation=saturate(gradation);
                    }
                }
                #endif
                float shadow=input.state.w>.5&&input.state.w<1.5?1:0;
                float4 original = UruiSampleClamped(uv);
                float rate=1;
                if(_TargetMode!=0&&_TargetRange<1)rate=UruiTargetRate(original.rgb,_TargetMode,_TargetColor.rgb,_TargetRange,_TargetSoftness);
                #if defined(URUI_BLUR_FAST) || defined(URUI_BLUR_MEDIUM) || defined(URUI_BLUR_DETAIL)
                // 程序 key 保证 blur 变体只用于三档 blur；排除不可达的 RGBShift 三次展开核。
                float4 color=lerp(original,UruiEffectInternal(uv,local,gradation,shadow),rate);
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
                    half2 r=UruiEffectInternal(uv+offset,local,gradation,shadow).ra,g=UruiEffectInternal(uv,local,gradation,shadow).ga,b=UruiEffectInternal(uv-offset,local,gradation,shadow).ba;
                    color=float4(r.x*r.y,g.x*g.y,b.x*b.y,(r.y+g.y+b.y)/3);
                }
                else if (_SamplingFilter == 6 || _SamplingFilter == 7) { color=UruiEffectInternal(uv,local,gradation,shadow)*UruiSamplingEdge(uv); }
                else { color=lerp(original,UruiEffectInternal(uv,local,gradation,shadow),rate); }
                #endif
                return color * input.color;
            }
            ENDCG
        }
    }
}
