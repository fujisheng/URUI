#pragma once
#include "render_plan_protocol.h"
#include <array>
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>

namespace urui_plan
{
    struct QuadMapping
    {
        std::array<float,4> rectangle{},u{},v{};
        float alpha=1;
    };
    // 只证明无内部重叠的完整矩形；文字、圆角扇形和任意扩展网格保持隔离。
    inline bool DirectQuad(const UruiVertex* vertices,size_t vertex_count,const int* indices,size_t index_count,QuadMapping& out,bool uniform_alpha)
    {
        if(!vertices||!indices||vertex_count!=4||index_count!=6)return false;
        float x0=vertices[0].x,x1=x0,y0=vertices[0].y,y1=y0;
        for(size_t n=0;n<4;n++)
        {
            const auto& v=vertices[n];if(!std::isfinite(v.x)||!std::isfinite(v.y)||!std::isfinite(v.u)||!std::isfinite(v.v))return false;
            if(uniform_alpha&&v.a!=vertices[0].a)return false;
            x0=std::min(x0,v.x);x1=std::max(x1,v.x);y0=std::min(y0,v.y);y1=std::max(y1,v.y);
        }
        if(!(x1>x0&&y1>y0))return false;
        int corners[4]{-1,-1,-1,-1},codes[4]{};
        for(int n=0;n<4;n++)
        {
            const auto& v=vertices[n];if((v.x!=x0&&v.x!=x1)||(v.y!=y0&&v.y!=y1))return false;
            const int code=(v.x==x1?1:0)|(v.y==y1?2:0);if(corners[code]>=0)return false;corners[code]=n;codes[n]=code;
        }
        int seen[4]{};
        for(size_t n=0;n<6;n++){if(indices[n]<0||indices[n]>=4)return false;seen[indices[n]]++;}
        int shared[2]{},count=0;
        for(int n=0;n<4;n++){if(seen[n]==2){if(count==2)return false;shared[count++]=n;}else if(seen[n]!=1)return false;}
        if(count!=2||(codes[shared[0]]^codes[shared[1]])!=3)return false;
        for(int n=0;n<6;n+=3)if(indices[n]==indices[n+1]||indices[n]==indices[n+2]||indices[n+1]==indices[n+2])return false;
        const auto& origin=vertices[corners[0]];const auto& x=vertices[corners[1]];const auto& y=vertices[corners[2]];const auto& diagonal=vertices[corners[3]];
        const double ux=(double(x.u)-origin.u)/(double(x1)-x0),uy=(double(y.u)-origin.u)/(double(y1)-y0);
        const double vx=(double(x.v)-origin.v)/(double(x1)-x0),vy=(double(y.v)-origin.v)/(double(y1)-y0);
        const auto affine=[](double expected,double actual){return std::abs(expected-actual)<=1e-6*std::max({1.,std::abs(expected),std::abs(actual)});};
        if(!affine(double(x.u)+y.u-origin.u,diagonal.u)||!affine(double(x.v)+y.v-origin.v,diagonal.v))return false;
        out.rectangle={x0,y0,x1,y1};out.u={float(ux),float(uy),float(origin.u-ux*x0-uy*y0),0};
        out.v={float(vx),float(vy),float(origin.v-vx*x0-vy*y0),0};out.alpha=origin.a/255.f;
        for(float value:out.u)if(!std::isfinite(value))return false;
        for(float value:out.v)if(!std::isfinite(value))return false;
        return true;
    }
    inline bool DirectMaskRasterGrid(const float* matrix,const QuadMapping& mapping)
    {
        if(matrix[1]!=0||matrix[4]!=0||matrix[3]!=0||matrix[7]!=0||matrix[2]!=0||matrix[6]!=0||matrix[14]!=0||matrix[15]<=0)return false;
        const double coordinates[4]={(double(matrix[0])*mapping.rectangle[0]+matrix[12])/matrix[15],
            (double(matrix[0])*mapping.rectangle[2]+matrix[12])/matrix[15],
            (double(matrix[5])*mapping.rectangle[1]+matrix[13])/matrix[15],
            (double(matrix[5])*mapping.rectangle[3]+matrix[13])/matrix[15]};
        // 栅格边恰过像素中心时受硬件 top-left 规则控制；保留原隔离路径，
        // 不用 Shader 的双闭区间判定猜测边界覆盖。旋转/3D 同样尚无该证明。
        for(double value:coordinates)if(!std::isfinite(value)||std::abs(value-std::floor(value)-.5)<1e-5)return false;
        return true;
    }
    inline bool InverseMaskProjection(const float* matrix,std::array<std::array<float,4>,4>& out)
    {
        const double a=matrix[0],b=matrix[4],c=matrix[12],d=matrix[1],e=matrix[5],f=matrix[13],g=matrix[3],h=matrix[7],i=matrix[15];
        const double determinant=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);
        if(!std::isfinite(determinant)||std::abs(determinant)<1e-12)return false;
        const double inverse[9]={(e*i-f*h)/determinant,(c*h-b*i)/determinant,(b*f-c*e)/determinant,
            (f*g-d*i)/determinant,(a*i-c*g)/determinant,(c*d-a*f)/determinant,
            (d*h-e*g)/determinant,(b*g-a*h)/determinant,(a*e-b*d)/determinant};
        for(double value:inverse)if(!std::isfinite(value)||std::abs(value)>std::numeric_limits<float>::max())return false;
        out={std::array<float,4>{float(inverse[0]),float(inverse[3]),0,float(inverse[6])},
            std::array<float,4>{float(inverse[1]),float(inverse[4]),0,float(inverse[7])},
            std::array<float,4>{0,0,1,0},std::array<float,4>{float(inverse[2]),float(inverse[5]),0,float(inverse[8])}};
        return true;
    }
    // 保留子树源层，只将零羽化矩形的二值像素覆盖并入最终合成裁剪。
    // 半像素硬件边界、透视与旋转继续使用原遮罩，避免猜测 top-left 覆盖规则。
    inline bool BinaryRectangleClip(const UruiPlanParameter& parameter,const UruiLayerCommand& composite,std::array<float,4>& out)
    {
        for(float feather:parameter.tint)if(feather!=0)return false;
        QuadMapping unit{};unit.rectangle={0,0,1,1};
        if(!DirectMaskRasterGrid(parameter.matrix,unit))return false;
        const auto* m=parameter.matrix;
        const double x0=double(m[12])/m[15],x1=(double(m[0])+m[12])/m[15];
        const double y0=double(m[13])/m[15],y1=(double(m[5])+m[13])/m[15];
        if(x0==x1||y0==y1)return false;
        const double values[4]={std::min(x0,x1),std::min(y0,y1),std::max(x0,x1),std::max(y0,y1)};
        for(int n=0;n<4;n++)
        {
            // 输出必须是精确可表示的整数；极端坐标保留原路径。
            if(std::abs(values[n])>8'000'000)return false;
            const double tolerance=std::max(1e-5,32*double(std::numeric_limits<float>::epsilon())*std::max(1.,std::abs(values[n])));
            if(std::abs(values[n]-std::floor(values[n])-.5)<=tolerance)return false;
            out[n]=float(std::floor(values[n]+.5));
        }
        if(uint32_t(parameter.metadata[0])&1)
        {
            for(float value:parameter.clip)if(!std::isfinite(value)||value!=std::floor(value))return false;
            out[0]=std::max(out[0],parameter.clip[0]);out[1]=std::max(out[1],parameter.clip[1]);
            out[2]=std::min(out[2],parameter.clip[2]);out[3]=std::min(out[3],parameter.clip[3]);
        }
        if(composite.flags&1)
        {
            out[0]=float(std::max(double(out[0]),double(composite.clip_x)));out[1]=float(std::max(double(out[1]),double(composite.clip_y)));
            out[2]=float(std::min(double(out[2]),double(composite.clip_x)+composite.clip_w));
            out[3]=float(std::min(double(out[3]),double(composite.clip_y)+composite.clip_h));
        }
        for(float value:out)if(!std::isfinite(value)||std::abs(value)>8'000'000)return false;
        out[2]=std::max(out[0],out[2]);out[3]=std::max(out[1],out[3]);
        return true;
    }
    struct DirectMask
    {
        uint32_t command=0,mask_command=0;
        uint64_t texture=0;
        std::array<std::array<float,4>,8> values{};
        UruiLayerCommand composite{};
    };
}
