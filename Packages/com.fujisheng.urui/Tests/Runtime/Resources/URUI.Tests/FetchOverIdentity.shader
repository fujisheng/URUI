Shader "Hidden/URUI.Tests/FetchOverIdentity"
{
 Properties { _MainTex("Source",2D)="black"{} _Screen("Screen",Float)=0 }
 CGINCLUDE
 #include "UnityCG.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/FramebufferBlend.cginc"
 #include "FetchOverReference.cginc"
 sampler2D_float _MainTex;float _Screen;
 void Fragment(v2f_img input,inout float4 target:SV_Target){target=UruiFetchOver(tex2D(_MainTex,input.uv),target,_Screen);}
 void Reference(v2f_img input,inout float4 target:SV_Target){target=UruiReferenceFetchOver(tex2D(_MainTex,input.uv),target,_Screen);}
 ENDCG
 SubShader
 {
  Cull Off ZWrite Off ZTest Always Blend One Zero
  Pass { CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex vert_img
   #pragma fragment Fragment
   ENDCG }
  Pass { CGPROGRAM
   #pragma target 3.5
   #pragma only_renderers framebufferfetch
   #pragma vertex vert_img
   #pragma fragment Reference
   ENDCG }
 }
}
