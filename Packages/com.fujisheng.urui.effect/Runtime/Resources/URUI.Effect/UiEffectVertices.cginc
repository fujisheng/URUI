#ifndef URUI_EFFECT_VERTICES_INCLUDED
#define URUI_EFFECT_VERTICES_INCLUDED
sampler2D _UruiTriangleFacts;
float4 UruiUiFact(float texel){return tex2Dlod(_UruiTriangleFacts,float4((float2(fmod(texel,256),floor(texel/256))+.5)/256,0,0));}
float4 UruiUiMultiply(float first,float4 v){return UruiUiRuntime(first)*v.x+UruiUiRuntime(first+1)*v.y+UruiUiRuntime(first+2)*v.z+UruiUiRuntime(first+3)*v.w;}
struct UiVertex {float2 position,uv;float4 color;};
float4 UruiUiFade(float4 color,float alpha)
{
    float4 bytes=floor(saturate(color)*255+.5);float next=floor(saturate(alpha)*bytes.a);
    return bytes.a==0?0:float4(floor(bytes.rgb*next/max(1,bytes.a)),next)/255;
}
UiVertex UruiUiSourceVertex(float2 position,float2 uv,float4 color,float metadata,float parameter)
{
    UiVertex vertex;float4 bounds=UruiUiRuntime(0);float2 size=float2(bounds.z>0?1/bounds.z:0,bounds.w>0?1/bounds.w:0);
    float4 expansion=UruiUiExpansion();float2 distance=UruiUiDistance();
    bool geometry=any(expansion!=0)||(_Flip&3)!=0||(_ShadowMode!=0&&any(distance!=0));
    if((_UiInputFlags&4)!=0)
    {
        float4 capture=UruiUiRuntime(2),domain=UruiUiRuntime(3);float2 low=capture.xy,high=low+capture.zw;
        if(geometry)
        {
            low=capture.xy+float2(domain.x,1-domain.w)*capture.zw;
            high=capture.xy+float2(domain.z,1-domain.y)*capture.zw;
            float2 scale=(high-low)*.5/max(float2(.00001,.00001),high-low-.002);
            low-=expansion.xy*scale;high+=expansion.zw*scale;
        }
        float2 world=lerp(low,high,position);float4 local=UruiUiMultiply(7,float4(world,0,1));vertex.position=local.w!=0?local.xy/local.w:float2(0,0);
        vertex.uv=float2((world.x-capture.x)/capture.z,1-(world.y-capture.y)/capture.w);vertex.color=1;
    }
    else
    {
        vertex.position=bounds.xy+position*size;
        float4 originalUv=UruiUiRuntime(15),uvTransform=UruiParameter(parameter,6);
        vertex.uv=lerp(originalUv.xy,originalUv.zw,uv)*uvTransform.xy+uvTransform.zw;
        float4 center=UruiUiFact(metadata);float2 triangleSize=UruiUiFact(metadata+1).xy*size-.002;
        float2 centerPoint=bounds.xy+center.xy*size;
        float2 centerUv=lerp(originalUv.xy,originalUv.zw,center.zw)*uvTransform.xy+uvTransform.zw;
        float x=vertex.position.x<bounds.x+.001?expansion.x:vertex.position.x>bounds.x+size.x-.001?expansion.z:0;
        float y=vertex.position.y<bounds.y+.001?expansion.y:vertex.position.y>bounds.y+size.y-.001?expansion.w:0;
        float2 scale=1+float2(x,y)/max(abs(triangleSize),float2(.00001,.00001));
        vertex.position=centerPoint+(vertex.position-centerPoint)*scale;vertex.uv=centerUv+(vertex.uv-centerUv)*scale;
        vertex.color=floor(saturate(color*UruiParameter(parameter,5))*255+.5)/255;
    }
    float4 content=UruiUiRuntime(5);float2 center=content.xy+content.zw*.5;
    if((_Flip&1)!=0)vertex.position.x=2*center.x-vertex.position.x;
    if((_Flip&2)!=0)vertex.position.y=2*center.y-vertex.position.y;
    return vertex;
}
UiVertex UruiUiInterpolate(UiVertex a,UiVertex b,float t)
{
    UiVertex v;v.position=lerp(a.position,b.position,t);v.uv=lerp(a.uv,b.uv,t);v.color=floor(saturate(lerp(a.color,b.color,t))*255+.5)/255;return v;
}
UiVertex UruiUiMirror(float metadata,float outputCorner,float parameter)
{
    UiVertex original[3];float4 content=UruiUiRuntime(5),root=UruiUiRuntime(6);float2 distance=UruiUiDistance();
    float height=root.w,halfSum=(content.w+height)*.5,rate=saturate(distance.x);
    float threshold=content.y+halfSum-height*rate;
    [unroll] for(int i=0;i<3;i++)
    {
        float4 source=UruiUiFact(metadata+2+i);
        original[i]=UruiUiSourceVertex(source.xy,source.zw,UruiUiFact(metadata+5+i),metadata,parameter);
        float normalized=(halfSum-(original[i].position.y-content.y))/max(.001,height);
        float fade=rate==0?0:saturate((rate-normalized)/rate);
        original[i].color=UruiUiFade(UruiUiFade(original[i].color,_ShadowFade),fade);
    }
    UiVertex polygon[4];int count=0;
    [unroll] for(int edge=0;edge<3;edge++)
    {
        UiVertex a=original[edge],b=original[(edge+1)%3];bool inside=a.position.y>=threshold,nextInside=b.position.y>=threshold;
        if(inside){polygon[count]=a;count++;}
        if(inside!=nextInside){polygon[count]=UruiUiInterpolate(a,b,(threshold-a.position.y)/(b.position.y-a.position.y));count++;}
    }
    int corner=(int)outputCorner;UiVertex result;
    // The second static triangle collapses when clipping yields only three corners.
    result.position=0;result.uv=0;result.color=0;
    if(count>=3&&any(distance!=0))
    {
        if(corner>=count)corner=count-1;result=polygon[corner];
        result.position.y=2*content.y+halfSum*(1+_ShadowMirrorScale)-distance.y-_ShadowMirrorScale*result.position.y;
    }
    return result;
}
float2 UruiUiShadowDirection(int mode,int direction)
{
    if(mode==1)return 1;
    if(mode==2)return direction==0?float2(1,1):direction==1?float2(1,0):float2(0,1);
    if(direction==0)return 1;if(direction==1)return -1;if(direction==2)return float2(1,-1);if(direction==3)return float2(-1,1);
    if(direction==4)return float2(1,0);if(direction==5)return float2(0,1);if(direction==6)return float2(-1,0);return float2(0,-1);
}
struct UiOutput
{
    float4 position:SV_POSITION;float2 uv:TEXCOORD0;float4 pixel:TEXCOORD1;float4 root:TEXCOORD2;float4 color:COLOR;
    nointerpolation float4 state:TEXCOORD3;nointerpolation float4 clipRect:TEXCOORD4;nointerpolation float4 runtime:TEXCOORD5;
};
UiOutput Vertex(UruiImageInput input)
{
    UiOutput output;float4 baseState=UruiParameter(input.source.y,7);_UiRuntimeBase=baseState.z;_UiFieldBase=_UiRuntimeBase+16;
    float4 runtime=UruiUiRuntime(1);_UiInputFlags=(int)runtime.w;
    int code=(int)floor(input.vertex.z/65536);float metadata=input.vertex.z-code*65536;int copy=code/3;
    UiVertex vertex;
    if(code>=128)vertex=UruiUiMirror(metadata,code-128,input.source.y);
    else
    {
        vertex=UruiUiSourceVertex(input.vertex.xy,input.uv,input.color,metadata,input.source.y);
        if(copy>0)
        {
            float2 distance=UruiUiDistance();int count=_ShadowMode==1?1:_ShadowMode==2?3:_ShadowMode==3?4:8;
            int iteration=(copy-1)/count,direction=(copy-1)%count;float harmonic=0;
            [unroll] for(int i=1;i<=5;i++)if(i<=iteration+1)harmonic+=1.0/i;
            float2 offset=distance*harmonic*UruiUiShadowDirection(_ShadowMode,direction);vertex.position+=float2(offset.x,-offset.y);
            vertex.color=UruiUiFade(vertex.color,_ShadowFade*pow(.75,iteration));
            if(all(distance==0)){vertex.position=0;vertex.color=0;}
        }
    }
    output.pixel=UruiParameterPosition(float4(vertex.position,0,1),input.source.y);output.position=mul(UNITY_MATRIX_VP,output.pixel);
    #if defined(URUI_COMPLEX_PIXEL)
    output.root=UruiUiMultiply(11,output.pixel);
    #else
    output.root=0;
    #endif
    output.uv=vertex.uv;output.color=UruiLinearizePremultipliedColor(vertex.color);
    output.state=float4(baseState.x,input.source.x,_UiRuntimeBase,code>=128?2:copy>0?1:0);output.clipRect=UruiParameter(input.source.y,4);
    runtime.z=runtime.z<0?0:UruiParameterTexel(runtime.z).x;output.runtime=runtime;return output;
}
float2 UruiUiCoordinate(float2 root,float rotation)
{
    float4 bounds=UruiUiRuntime(6);float2 scale=float2(1/max(.001,bounds.z),-1/max(.001,bounds.w));
    if(_TransitionKeepAspectRatio!=0){scale.x=min(scale.x,-scale.y);scale.y=-scale.x;}
    if((_Flip&4)!=0){if((_Flip&1)!=0)scale.x=-scale.x;if((_Flip&2)!=0)scale.y=-scale.y;}
    float angle=radians(rotation),c=cos(angle),s=sin(angle),multiplier=max(abs(c-s),abs(c+s));
    float2 local=(root-(bounds.xy+bounds.zw*.5))*scale;return .5+float2(c*local.x-s*local.y,s*local.x+c*local.y)/multiplier;
}
#endif
