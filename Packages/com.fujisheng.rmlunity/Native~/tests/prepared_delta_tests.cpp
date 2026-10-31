#include "prepared_batch.h"
#include <cassert>
#include <iostream>
#include <map>
using namespace rui_prepared;
int main(){
 std::vector<RuiVertex> a={{0,0,0,0,255,0,0,128},{8,0,1,0,0,255,0,255},{0,8,0,1,0,0,255,255}};auto b=a;b[0].r=100;std::vector<int> ix={0,1,2};
 auto geometry=[&](uint64_t id){return id==1||id==2?View{a.data(),a.size(),ix.data(),ix.size()}:id==3?View{b.data(),b.size(),ix.data(),ix.size()}:View{};};
 RuiFrameStamp s{};s.epoch=1;s.commands=2;s.resources=3;s.flags=1;s.count=3;std::vector<RuiCommand> c(3);for(auto& x:c)x.geometry=1;c[1].geometry=2;uint8_t mask[]={1,1,1};Session session;assert(session.EnableDelta(1)==1);RuiPreparedPacket first{},second{};assert(session.Acquire(s,c,mask,3,geometry,first)==1);auto old=session.Get(first.lease)->batches[0];auto oldID=old->draw.batch;
 std::vector<RuiPreparedVertex> vb(old->vertices.size());std::vector<uint32_t> ib(old->indices.size());RuiPreparedDeltaInfo info{};
 assert(session.CopyDelta(first.lease,oldID,0,vb.data(),vb.size(),ib.data(),ib.size(),info)==1&&info.mode==0);
 c[1].geometry=3;s.commands++;assert(session.Acquire(s,c,mask,3,geometry,second)==1);auto changed=session.Get(second.lease)->batches[0];assert(session.CopyDelta(second.lease,changed->draw.batch,oldID,vb.data(),vb.size(),ib.data(),ib.size(),info)==1);assert(info.mode==1&&info.copied_bytes==108&&info.full_bytes==324&&info.base_batch==oldID);assert(std::memcmp(vb.data(),changed->vertices.data(),vb.size()*32)==0&&ib==changed->indices);assert(old->vertices[3].r==255);assert(session.CopyDelta(second.lease,changed->draw.batch,changed->draw.batch,vb.data(),vb.size(),ib.data(),ib.size(),info)==1&&info.mode==2&&info.copied_bytes==0);
 auto frozen=vb;assert(session.CopyDelta(second.lease,changed->draw.batch,oldID,vb.data(),0,ib.data(),ib.size(),info)==-1);assert(std::memcmp(frozen.data(),vb.data(),vb.size()*32)==0&&info.copied_bytes==0);
 assert(session.CopyDelta(second.lease,changed->draw.batch,oldID+9999,vb.data(),vb.size(),ib.data(),ib.size(),info)==1&&info.mode==0&&info.copied_bytes==324);
 assert(session.Release(first.lease)==1&&session.Release(second.lease)==1);assert(session.CopyDelta(first.lease,oldID,0,vb.data(),vb.size(),ib.data(),ib.size(),info)==-2);
 // Epoch and complete-state changes must not reuse a previous base.
 RuiPreparedPacket epoch{};s.epoch++;assert(session.Acquire(s,c,mask,3,geometry,epoch)==1);auto e=session.Get(epoch.lease)->batches[0];assert(e->delta_base==0);assert(session.Release(epoch.lease)==1);
 Session disabled;assert(disabled.Acquire(s,c,mask,3,geometry,first)==1);assert(disabled.EnableDelta(1)==-5);assert(disabled.Release(first.lease)==1);assert(disabled.EnableDelta(1)==1);assert(disabled.EnableDelta(7)==-1);
 // Excessive fragmentation is a complete-copy fallback, not partial state.
 Session fragmented;assert(fragmented.EnableDelta(1)==1);std::vector<RuiCommand> many(100);for(auto& v:many)v.geometry=1;std::vector<uint8_t> m(100,1);s.count=100;assert(fragmented.Acquire(s,many,m.data(),100,geometry,first)==1);for(size_t i=0;i<many.size();i+=2)many[i].geometry=3;assert(fragmented.Acquire(s,many,m.data(),100,geometry,second)==1);auto last=fragmented.Get(second.lease)->batches[0];assert(last->delta_base==0&&last->vertex_ranges.empty());assert(fragmented.Release(first.lease)==1&&fragmented.Release(second.lease)==1);
 // Source/layout replacement with different vertex count must reject the base.
 Session layout;assert(layout.EnableDelta(1)==1);s.count=3;c[1].geometry=2;assert(layout.Acquire(s,c,mask,3,geometry,first)==1);auto wider=b;wider.push_back(b[0]);auto shapes=[&](uint64_t id){return id==4?View{wider.data(),wider.size(),ix.data(),ix.size()}:geometry(id);};c[1].geometry=4;assert(layout.Acquire(s,c,mask,3,shapes,second)==1);last=layout.Get(second.lease)->batches[0];assert(last->delta_base==0);assert(layout.Close()==-5);std::vector<RuiPreparedVertex> all(last->vertices.size());std::vector<uint32_t> ids(last->indices.size());assert(layout.CopyDelta(second.lease,last->draw.batch,oldID,all.data(),all.size(),ids.data(),ids.size(),info)==1&&info.mode==0);assert(layout.Release(first.lease)==1&&layout.Release(second.lease)==1);
 std::cout<<"NATIVE_DELTA_IDENTITY_RANGE_ORACLE_PASS\n";
}
