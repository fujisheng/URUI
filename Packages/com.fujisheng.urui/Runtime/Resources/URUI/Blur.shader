Shader "URUI/Blur" {
 CGINCLUDE
#include "UnityCG.cginc"
 sampler2D _InputTex;
 struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
 Output Vertex(appdata_img input){Output o;o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o;}
 float4 SampleInput(float2 uv){return tex2D(_InputTex,uv)*step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);}

 float4 _Blur;
 float4 Gaussian(float2 uv,float2 direction) {
  if(_Blur.x<=.001)return SampleInput(uv);
  float4 sum=SampleInput(uv);float total=1;
  // 相邻整数像素的高斯权重配对，用双线性采样准确合并，足迹保持连续。
  int radius=(int)min(ceil(3*_Blur.x),max(1,_Blur.w));
  [loop]for(int i=1;i<=radius;i+=2){
   float a=i/_Blur.x,b=(i+1)/_Blur.x;
   float first=exp(-.5*a*a),second=i+1<=radius?exp(-.5*b*b):0;
   float weight=first+second,offset=i+second/weight;
   sum+=(SampleInput(uv+direction*offset)+SampleInput(uv-direction*offset))*weight;total+=2*weight;
  }
  return sum/total;
 }
 float4 Horizontal(Output input):SV_Target{return Gaussian(input.uv,float2(_Blur.y,0));}
 float4 Vertical(Output input):SV_Target{return Gaussian(input.uv,float2(0,_Blur.z));}
 ENDCG
 SubShader { Cull Off ZWrite Off ZTest Always Blend One Zero
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Horizontal
 ENDCG }
 Pass { CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Vertical
 ENDCG }
 }
}
