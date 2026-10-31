#pragma once
#include "urui.h"
#include <cstring>
namespace urui_core {
inline uint32_t Bits(float v){uint32_t b;std::memcpy(&b,&v,sizeof(b));return b;}
inline bool Ordinary(float v){const auto b=Bits(v),a=b&0x7fffffffu;return b==0||(a>=0x00800000u&&a<0x7f800000u);}
// This deliberately narrow condition is a numeric feature test, not a scene
// contract. Other matrices and unusual floats retain the managed oracle.
inline void Prepare(const UruiCommand& c,const float* m,UruiCoreKey& key,UruiCoreValue& value,float* world){
 key={c.geometry,c.texture,c.clip_enabled,c.clip_x,c.clip_y,c.clip_w,c.clip_h,0};
 value={};value.data[0]=c.tx;value.data[1]=c.ty;std::memcpy(world,m,64);
 for(int i=0;i<16;i++)if(i!=12&&i!=13&&Bits(m[i])!=((i==0||i==5||i==10||i==15)?0x3f800000u:0u))return;
 if(!Ordinary(c.tx)||!Ordinary(c.ty)||!Ordinary(m[12])||!Ordinary(m[13]))return;
 const float x=m[12]+c.tx,y=m[13]+c.ty;if(!Ordinary(x)||!Ordinary(y))return;
 world[12]=x;world[13]=y;value={};value.data[0]=1;value.data[2]=x;value.data[5]=1;value.data[6]=y;key.mode=1;
}
static_assert(sizeof(UruiCoreKey)==40&&sizeof(UruiCoreValue)==32&&sizeof(UruiCoreSummary)==16,"core frame ABI");
}
