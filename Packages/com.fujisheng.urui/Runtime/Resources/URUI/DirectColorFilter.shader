Shader "URUI/DirectColorFilter"
{
    Properties
    {
        _MainTex("Image", 2D) = "white" {}
        [HideInInspector] _UruiBorrowedTexture("Borrowed", Float) = 0
        [HideInInspector] _UruiSourceSize("Size", Vector) = (1,1,1,1)
        [HideInInspector] _UruiSourceSrgb("sRGB", Float) = 0
        [HideInInspector] _UruiTextureSampling("Sampling", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"
            #include "PremultipliedColor.cginc"
            #include "BorrowedTextureSampling.cginc"
            #include "CssColorOps.cginc"
            float _UruiClipEnabled;
            float4 _UruiClipRect;
            int _CssFilterCount;
            float4 _CssFilters[16];
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; float4 color:COLOR; };
            Output Vertex(Input input)
            {
                Output output; output.position = UnityObjectToClipPos(input.vertex); output.uv = input.uv;
                output.pixel = mul(unity_ObjectToWorld, input.vertex).xy; output.color = input.color; return output;
            }
            float4 Fragment(Output input):SV_Target
            {
                if (_UruiClipEnabled > .5)
                { clip(input.pixel - _UruiClipRect.xy); clip(_UruiClipRect.zw - input.pixel); }
                float4 color = UruiCssToEncoded(UruiSampleMainTexture(input.uv) * UruiLinearizePremultipliedColor(input.color));
                [loop] for (int index = 0; index < _CssFilterCount; index++)
                { color = UruiCssColorStep(color, (int)_CssFilters[index].x, _CssFilters[index].y); }
                return UruiCssFromEncoded(color);
            }
            ENDCG
        }
    }
}
