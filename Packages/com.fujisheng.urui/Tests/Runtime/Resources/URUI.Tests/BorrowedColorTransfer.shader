Shader "Hidden/URUI.Tests/BorrowedColorTransfer" {
Properties {_MainTex("Source",2D)="white"{}}
SubShader {Cull Off ZWrite Off ZTest Always Blend Off
Pass { CGPROGRAM
#pragma target 3.5
#pragma vertex vert_img
#pragma fragment Fragment
#include "UnityCG.cginc"
#include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
#include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc"
float4 Fragment(v2f_img value):SV_Target{return UruiSampleMainTexture(value.uv);}
ENDCG }
}}
