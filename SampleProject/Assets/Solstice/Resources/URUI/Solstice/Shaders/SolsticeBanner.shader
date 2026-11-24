Shader "Solstice/AstralBanner"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintA ("Deep Astral Tint", Color) = (0.08, 0.14, 0.28, 1.0)
        _TintB ("Solar Gold Accent", Color) = (0.94, 0.77, 0.42, 1.0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintA;
            fixed4 _TintB;
            float _EffectTime;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv);
                float t = _EffectTime;
                // Diagonal astral sweep + subtle warm gold caustic rim
                float diag = i.uv.x * 1.35 + i.uv.y * 0.75 - frac(t * 0.22) * 2.4;
                float sweep = exp(-diag * diag * 28.0);
                float wave = 0.5 + 0.5 * sin(i.uv.x * 14.0 - t * 2.1 + i.uv.y * 6.0);

                fixed3 col = lerp(baseCol.rgb, baseCol.rgb * 0.88 + _TintA.rgb * 0.35, 0.22);
                col += _TintB.rgb * (sweep * 0.34 + wave * 0.045);

                // Vignette edge darken for legible foreground HUD typography
                float2 centered = i.uv * 2.0 - 1.0;
                float vig = saturate(1.0 - dot(centered, centered) * 0.28);
                col *= lerp(0.72, 1.04, vig);

                return fixed4(saturate(col), baseCol.a);
            }
            ENDCG
        }
    }
}
