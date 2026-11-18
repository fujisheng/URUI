Shader "URUI.Effect/WaveImage" {
 Properties {_MainTex("Texture",2D)="white" {} [HideInInspector]_UruiParameters("Parameters",2D)="black" {} [HideInInspector]_UruiParameterSize("Parameter Size",Vector)=(1,1,1,1)}
 SubShader {Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 Pass {CGPROGRAM
 #pragma target 3.5
 #pragma vertex Vertex
 #pragma fragment Fragment
 #include "UnityCG.cginc"
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/PremultipliedColor.cginc"
 sampler2D _MainTex;
 #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/ImageParameters.cginc"
 struct Output{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 pixel:TEXCOORD1;nointerpolation float4 clipRect:TEXCOORD2;nointerpolation float4 state:TEXCOORD3;nointerpolation float4 tint:TEXCOORD4;};
 Output Vertex(UruiImageInput input){
  float4 metadata=UruiParameter(input.source.y,7);float fields=metadata.z+2;
  float4 motion=UruiParameterTexel(fields),shape=UruiParameterTexel(fields+1),axis=UruiParameterTexel(fields+2);
  float4 runtime=UruiParameterTexel(metadata.z+1);float time=runtime.z<0?0:UruiParameterTexel(runtime.z).x;
  float nx=input.vertex.x,ny=1-input.vertex.y,edge=axis.x;bool horizontal=axis.y<.5;bool verticalAxis=(edge>1.5&&edge<3.5)||(edge>3.5&&horizontal);
  float coordinate=verticalAxis?ny:nx,orthogonal=verticalAxis?nx:ny;
  float distance=edge<.5?nx:edge<1.5?1-nx:edge<2.5?1-ny:edge<3.5?ny:2*abs(coordinate-.5);
  float phase=(time+shape.w)*motion.z;
  float wave=sin(coordinate*motion.y+phase)+motion.w*sin(coordinate*motion.y*.5-phase*.7);
  wave+=shape.y*(orthogonal-.5)*(verticalAxis?runtime.x:runtime.y)*shape.z*sin(phase+coordinate*2);
  float offset=wave*motion.x*smoothstep(shape.x,1,distance);
  float4 local=float4(input.vertex.xy*runtime.xy+float2(horizontal?offset:0,horizontal?0:-offset),0,1);
  Output o;o.pixel=UruiParameterPosition(local,input.source.y);o.vertex=mul(UNITY_MATRIX_VP,o.pixel);o.uv=input.uv;
  o.clipRect=UruiParameter(input.source.y,4);o.state=float4(metadata.x,input.source.x,runtime.w,0);o.tint=UruiParameterTexel(fields+3);o.tint.rgb*=o.tint.a;return o;
 }
 float4 Fragment(Output input):SV_Target{
  UruiParameterClip(input.pixel,input.clipRect,input.state.x);float2 uv=input.uv;int flags=(int)input.state.z;if((flags&1)!=0)uv.y=1-uv.y;
  float4 color=UruiImageSample(uv,input.state.y);if((flags&2)==0)color.rgb*=color.a;
  return color*UruiLinearizePremultipliedColor(input.tint);
 }
 ENDCG}
 }
}
