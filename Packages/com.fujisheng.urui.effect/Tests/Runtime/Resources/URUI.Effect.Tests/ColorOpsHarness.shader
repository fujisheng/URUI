Shader "Hidden/URUI.Effect.Tests/ColorOpsHarness"
{
    Properties { _MainTex("Input",2D)="white"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"
            #include "Packages/com.fujisheng.urui.effect/Runtime/Resources/URUI.Effect/ColorOps.cginc"
            sampler2D _MainTex;
            float4 _Factor;
            float _Intensity;
            int _Mode;
            float4 Fragment(v2f_img input):SV_Target
            { return UruiColor(tex2D(_MainTex,input.uv),_Factor,_Intensity,_Mode,0,true); }
            ENDCG
        }
    }
}
