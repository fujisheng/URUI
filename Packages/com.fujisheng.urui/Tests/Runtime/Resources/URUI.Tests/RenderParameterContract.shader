Shader "Hidden/URUI.Tests/RenderParameterContract"
{
 Properties
 {
  _Parameters("Parameters",2D)="black"{}
  _UruiParameters("Draw parameters",2D)="black"{}
  _Probe("Probe position",Vector)=(0,0,0,1)
  _Mask("Mask",2D)="white"{}
  _Color0("Color0",2D)="white"{} _Color1("Color1",2D)="white"{}
  _Color2("Color2",2D)="white"{} _Color3("Color3",2D)="white"{}
  _Color4("Color4",2D)="white"{} _Color5("Color5",2D)="white"{}
  _Color6("Color6",2D)="white"{} _Color7("Color7",2D)="white"{}
  _ParameterIndex("Index",Float)=65535
  _Mode("Mode",Float)=0
  _UruiPaintOpacity("Opacity",Float)=1
  _UruiClipEnabled("Clip",Float)=0
  _UruiClipRect("ClipRect",Vector)=(0,0,0,0)
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Packages/com.fujisheng.urui/Runtime/Resources/URUI/RenderParameters.cginc"
   sampler2D _Parameters,_Mask,_Color0,_Color1,_Color2,_Color3,_Color4,_Color5,_Color6,_Color7;
   float _ParameterIndex,_Mode;float4 _Probe;
   float4 _Parameters_TexelSize;
   struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 transformed:TEXCOORD1; };
   float4 Parameter(float index)
   {
    return tex2Dlod(_Parameters,float4((fmod(index,256)+.5)*_Parameters_TexelSize.x,(floor(index/256)+.5)*_Parameters_TexelSize.y,0,0));
   }
   v2f vert(appdata_img v)
   {
    v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord;
    float4 p=float4(.125,.25,0,1);
    float3 value=float3(dot(Parameter(0),p),dot(Parameter(1),p),dot(Parameter(2),p));
    o.transformed=float4((value.x-65536)*4,(value.y+4096)/.75,value.z*8,1);
    if(_Mode>2.5)o.transformed=UruiOrdinaryParameterPosition(_Probe,0,UruiParameter(0,7).x);
    return o;
   }
   float4 frag(v2f i):SV_Target
   {
    if(_Mode>2.5)return i.transformed;
    float4 a=Parameter(_ParameterIndex);
    if(_Mode>1.5)return a;
    float3 value=_Mode>.5?i.transformed.xyz:float3((a.x-65536)*16,(a.y+4096)*4,a.z*64);
    float4 palette=(tex2D(_Color0,i.uv)+tex2D(_Color1,i.uv)+tex2D(_Color2,i.uv)+tex2D(_Color3,i.uv)+tex2D(_Color4,i.uv)+tex2D(_Color5,i.uv)+tex2D(_Color6,i.uv)+tex2D(_Color7,i.uv))/8;
    float mask=tex2D(_Mask,i.uv).r;
    return float4(value*palette.rgb*mask,mask);
   }
   ENDCG
  }
 }
}
