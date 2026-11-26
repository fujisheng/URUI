Shader "URUI/Samples/Tideglass/Optical Glass"
{
    Properties
    {
        _MainTex ("Backdrop", 2D) = "white" {}
        [HideInInspector] _UruiPaintOpacity ("Paint opacity", Float) = 1
        [HideInInspector] _UruiClipEnabled ("Scissor enabled", Float) = 0
        [HideInInspector] _UruiClipRect ("Scissor", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ElementPaint.cginc"
            sampler2D _MainTex;
            float4x4 _DocumentToGlass;
            float4 _GlassRect, _SampleStep;
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 pixel : TEXCOORD1; };
            Varyings vert(appdata_img v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.pixel = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UruiClipPaint(i.pixel);
                float4 local = mul(_DocumentToGlass, float4(i.pixel, 0, 1));
                float2 p = (local.xy / local.w - _GlassRect.xy) / max(_GlassRect.zw, 1);
                float2 edge = min(p, 1-p) * _GlassRect.zw;
                float rim = min(edge.x, edge.y);
                clip(rim);
                float envelope = smoothstep(0, 12, rim);
                float2 normal = float2(sin(p.y*12.4 + p.x*3.7) + .30*sin(p.y*24.5 - p.x*5.2),
                                       cos(p.x*10.2 + p.y*6.1));
                float2 offset = normal * float2(8, 4.5) * _SampleStep.xy * envelope;
                float2 uv = saturate(i.uv + offset);
                half4 colour = tex2D(_MainTex, uv);
                float2 dispersion = normal * _SampleStep.xy * .75 * envelope;
                colour.r = tex2D(_MainTex, saturate(uv + dispersion)).r;
                colour.b = tex2D(_MainTex, saturate(uv - dispersion)).b;
                colour.rgb += half3(.025,.058,.060) * (1-smoothstep(0, 2.5, rim)) * colour.a;
                return colour * _UruiPaintOpacity;
            }
            ENDCG
        }
    }
}
