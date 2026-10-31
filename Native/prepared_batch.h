#pragma once
#include "rmlunity.h"
#include <vector>
#include <array>
#include <memory>
#include <atomic>
#include <thread>
#include <cstring>
#include <cstddef>
#include <limits>
#include <stdexcept>
namespace rui_prepared {
static_assert(sizeof(RuiPreparedDeltaInfo)==48,"incremental info ABI");
static_assert(sizeof(RuiPreparedVertex)==32&&offsetof(RuiPreparedVertex,r)==12&&offsetof(RuiPreparedVertex,u)==16&&offsetof(RuiPreparedVertex,slot)==24,"vertex ABI");
static_assert(sizeof(RuiPreparedDraw)==56&&sizeof(RuiPreparedPacket)==88,"packet ABI");
inline uint64_t Token(){static std::atomic<uint64_t> next{0};auto v=next.load();while(v!=UINT64_MAX){if(next.compare_exchange_weak(v,v+1))return v+1;}throw std::overflow_error("prepared identity exhausted");}
struct View {const RuiVertex* vertices=nullptr;size_t nv=0;const int* indices=nullptr;size_t ni=0;};
struct Budget {size_t used=0,limit=64u*1024*1024;};
struct Segment {uint32_t vertices,indices;};
struct Range {uint32_t offset,count;};
struct Batch {
 uint64_t delta_base=0;std::vector<Segment> segments;std::vector<Range> vertex_ranges,index_ranges;
 RuiPreparedDraw draw{};uint64_t epoch=0;std::vector<uint64_t> geometry;std::vector<RuiPreparedVertex> vertices;std::vector<uint32_t> indices;
 std::shared_ptr<Budget> budget;size_t bytes=0;
 ~Batch(){if(budget)budget->used-=bytes;}
};
struct Plan {uint64_t id=0,epoch=0,resources=0;std::vector<std::shared_ptr<Batch>> batches;};
class Session {
 struct Lease {uint64_t token=0;std::shared_ptr<const Plan> plan;};
 std::thread::id owner=std::this_thread::get_id();std::array<Lease,4> leases{};std::shared_ptr<Budget> budget=std::make_shared<Budget>();std::shared_ptr<Plan> current;bool closing=false,delta_enabled=false;
 static bool State(const RuiPreparedDraw& a,const RuiCommand& b){return a.texture==b.texture&&a.clip_enabled==b.clip_enabled&&a.clip_x==b.clip_x&&a.clip_y==b.clip_y&&a.clip_w==b.clip_w&&a.clip_h==b.clip_h;}
 static bool Same(const Batch& b,uint64_t epoch,const std::vector<RuiCommand>& c,size_t first,size_t end,bool affine){if(b.epoch!=epoch||b.draw.slot!=first||b.draw.count!=end-first||b.draw.kind!=(affine?0u:1u)||!State(b.draw,c[first]))return false;for(size_t i=first;i<end;i++)if(b.geometry[i-first]!=c[i].geometry)return false;return true;}
 static void AddRange(std::vector<Range>& ranges,uint32_t offset,uint32_t count){if(!ranges.empty()&&ranges.back().offset+ranges.back().count==offset)ranges.back().count+=count;else ranges.push_back({offset,count});}
 void Delta(Batch& b,const Batch* old){
  if(!delta_enabled||!old||b.epoch!=old->epoch||b.draw.slot!=old->draw.slot||b.draw.count!=old->draw.count||b.draw.kind!=old->draw.kind||b.draw.texture!=old->draw.texture||b.draw.clip_enabled!=old->draw.clip_enabled||b.draw.clip_x!=old->draw.clip_x||b.draw.clip_y!=old->draw.clip_y||b.draw.clip_w!=old->draw.clip_w||b.draw.clip_h!=old->draw.clip_h||b.segments.size()!=old->segments.size())return;
  for(size_t i=0;i<b.segments.size();i++)if(b.segments[i].vertices!=old->segments[i].vertices||b.segments[i].indices!=old->segments[i].indices)return;
  uint32_t v=0,k=0;size_t dirty=0;
  for(size_t i=0;i<b.segments.size();i++){auto part=b.segments[i];if(b.geometry[i]!=old->geometry[i]){AddRange(b.vertex_ranges,v,part.vertices);AddRange(b.index_ranges,k,part.indices);dirty+=size_t(part.vertices)*32+size_t(part.indices)*4;}v+=part.vertices;k+=part.indices;}
  if(dirty>0&&dirty<b.bytes&&b.vertex_ranges.size()+b.index_ranges.size()<=64)b.delta_base=old->draw.batch;
  else{b.vertex_ranges.clear();b.index_ranges.clear();}
 }
public:
 const uint64_t id=Token();
 explicit Session(size_t maxBytes=64u*1024*1024){budget->limit=maxBytes;}
 int EnableDelta(int value){if(!Thread())return -3;if(closing||Pending())return -5;if(value!=0&&value!=1)return -1;if(delta_enabled!=(value!=0)){current.reset();delta_enabled=value!=0;}return 1;}
 int CopyDelta(uint64_t lease,uint64_t id,uint64_t base,RuiPreparedVertex* vertices,int vc,uint32_t* indices,int ic,RuiPreparedDeltaInfo& info)const{
  info={};if(!Thread())return -3;auto plan=Get(lease);if(!plan)return -2;
  for(const auto& b:plan->batches)if(b->draw.batch==id){
   if(vc<0||ic<0||size_t(vc)<b->vertices.size()||size_t(ic)<b->indices.size()||!vertices||!indices)return -1;
   info.base_batch=b->delta_base;info.full_bytes=b->bytes;info.delta_bytes=b->bytes;info.vertex_ranges=uint32_t(b->vertex_ranges.size());info.index_ranges=uint32_t(b->index_ranges.size());
   if(b->delta_base){info.delta_bytes=0;for(auto r:b->vertex_ranges)info.delta_bytes+=size_t(r.count)*32;for(auto r:b->index_ranges)info.delta_bytes+=size_t(r.count)*4;}
   if(base==b->draw.batch){info.base_batch=base;info.delta_bytes=0;info.vertex_ranges=info.index_ranges=0;info.mode=2;info.copied_bytes=0;}
   else if(base&&base==b->delta_base){for(auto r:b->vertex_ranges)std::memcpy(vertices+r.offset,b->vertices.data()+r.offset,size_t(r.count)*32);for(auto r:b->index_ranges)std::memcpy(indices+r.offset,b->indices.data()+r.offset,size_t(r.count)*4);info.mode=1;info.copied_bytes=info.delta_bytes;}
   else{std::memcpy(vertices,b->vertices.data(),b->vertices.size()*32);std::memcpy(indices,b->indices.data(),b->indices.size()*4);info.copied_bytes=info.full_bytes;}
   return 1;
  }return -2;
 }
 bool Thread()const{return owner==std::this_thread::get_id();}
 bool Pending()const{for(const auto& l:leases)if(l.token)return true;return false;}
 int Close(){if(!Thread())return -3;closing=true;current.reset();return Pending()?-5:1;}
 bool Closing()const{return closing;}
 std::shared_ptr<const Plan> Get(uint64_t lease)const{if(!Thread())return {};for(const auto& l:leases)if(l.token==lease&&lease)return l.plan;return {};}
 int Release(uint64_t lease){if(!Thread())return -3;for(auto& l:leases)if(l.token==lease&&lease){l.plan.reset();l.token=0;return 1;}return -2;}
 size_t Retained()const{return budget->used;}
 template<class Resolve> int Acquire(const RuiFrameStamp& s,const std::vector<RuiCommand>& c,const uint8_t* mask,int count,Resolve resolve,RuiPreparedPacket& out){
  out={};if(!Thread())return -3;if(closing)return -5;
  if(s.flags!=1||s.epoch==0||count<0||count>65536||size_t(count)!=c.size()||s.count!=uint32_t(count)||(count&&!mask))return -1;
  Lease* free=nullptr;for(auto& l:leases)if(!l.token){free=&l;break;}if(!free)return -5;
  for(int i=0;i<count;i++)if(mask[i]>1)return -1;
  if(current&&current->epoch==s.epoch){
   size_t ordinal=0,bi=0;bool same=true;
   while(ordinal<c.size()){
    if(!c[ordinal].geometry){ordinal++;continue;}bool affine=mask[ordinal]!=0;size_t end=ordinal+1;
    if(bi>=current->batches.size()){same=false;break;}
    const auto& old=*current->batches[bi];while(affine&&end<c.size()&&c[end].geometry&&mask[end]&&State(old.draw,c[end]))end++;
    if(!Same(old,s.epoch,c,ordinal,end,affine)){same=false;break;}ordinal=end;bi++;
   }
   if(same&&bi==current->batches.size()){
    auto lease=Token();free->plan=current;free->token=lease;out.lease=lease;out.session=id;out.epoch=s.epoch;out.plan=current->id;out.resources=s.resources;out.retained_bytes=budget->used;out.draw_count=uint32_t(bi);out.commands=uint32_t(count);out.reused_batches=uint32_t(bi);out.stride=32;out.version=1;return 1;
   }
  }
  // Exact ordered identities, never hash-only. Geometry is immutable under its
  // globally unique handle; a new document epoch always invalidates the plan.
  std::vector<std::shared_ptr<Batch>> next;next.reserve(current?current->batches.size():size_t(count));size_t total=0;uint32_t built=0,reused=0,vcopy=0,icopy=0;uint64_t builtBytes=0;
  for(size_t begin=0;begin<c.size();){if(!c[begin].geometry){begin++;continue;}bool affine=mask[begin]!=0;size_t end=begin+1;
   RuiPreparedDraw d{};d.texture=c[begin].texture;d.slot=uint32_t(begin);d.kind=affine?0:1;d.clip_enabled=c[begin].clip_enabled;d.clip_x=c[begin].clip_x;d.clip_y=c[begin].clip_y;d.clip_w=c[begin].clip_w;d.clip_h=c[begin].clip_h;
   while(affine&&end<c.size()&&c[end].geometry&&mask[end]&&State(d,c[end]))end++;
   d.count=uint32_t(end-begin);std::shared_ptr<Batch> b;
   if(current&&next.size()<current->batches.size()){auto old=current->batches[next.size()];if(Same(*old,s.epoch,c,begin,end,affine))b=old;}
   if(b){reused++;total+=b->bytes;}
   else {
    size_t nv=0,ni=0;for(size_t i=begin;i<end;i++){auto v=resolve(c[i].geometry);if(!v.vertices||!v.indices||v.nv==0||v.ni==0)return -2;if(v.nv>1048576||v.ni>3145728||nv+v.nv>1048576||ni+v.ni>3145728)return -4;nv+=v.nv;ni+=v.ni;for(size_t k=0;k<v.ni;k++)if(v.indices[k]<0||size_t(v.indices[k])>=v.nv)return -1;}
    size_t bytes=nv*32+ni*4;if(total+bytes>16u*1024*1024||bytes>budget->limit-budget->used)return -4;
    b=std::make_shared<Batch>();b->draw=d;b->epoch=s.epoch;b->geometry.reserve(end-begin);if(delta_enabled)b->segments.reserve(end-begin);b->vertices.reserve(nv);b->indices.reserve(ni);
    for(size_t i=begin;i<end;i++){auto v=resolve(c[i].geometry);uint32_t offset=uint32_t(b->vertices.size());b->geometry.push_back(c[i].geometry);if(delta_enabled)b->segments.push_back({uint32_t(v.nv),uint32_t(v.ni)});for(size_t n=0;n<v.nv;n++){auto a=v.vertices[n];b->vertices.push_back({a.x,a.y,0,a.r,a.g,a.b,a.a,a.u,a.v,float(i),0});}for(size_t n=0;n<v.ni;n++)b->indices.push_back(offset+uint32_t(v.indices[n]));}
    b->draw.batch=Token();b->draw.vertex_count=uint32_t(nv);b->draw.index_count=uint32_t(ni);b->bytes=bytes;b->budget=budget;budget->used+=bytes;total+=bytes;builtBytes+=bytes;vcopy+=uint32_t(nv);icopy+=uint32_t(ni);built++;if(delta_enabled)Delta(*b,current&&next.size()<current->batches.size()?current->batches[next.size()].get():nullptr);
   }
   if(total>16u*1024*1024)return -4;next.push_back(std::move(b));begin=end;
  }
  bool same=current&&current->epoch==s.epoch&&next.size()==current->batches.size();if(same)for(size_t i=0;i<next.size();i++)if(next[i]!=current->batches[i]){same=false;break;}
  auto plan=current;if(!same){plan=std::make_shared<Plan>();plan->id=Token();plan->epoch=s.epoch;plan->batches=std::move(next);}
  uint64_t lease=Token();free->plan=plan;free->token=lease;current=plan;
  out.lease=lease;out.session=id;out.epoch=s.epoch;out.plan=plan->id;out.resources=s.resources;out.retained_bytes=budget->used;out.built_bytes=builtBytes;out.draw_count=uint32_t(plan->batches.size());out.commands=uint32_t(count);out.built_batches=built;out.reused_batches=reused;out.native_vertices_copied=vcopy;out.native_indices_copied=icopy;out.stride=32;out.version=1;return 1;
 }
};
}
