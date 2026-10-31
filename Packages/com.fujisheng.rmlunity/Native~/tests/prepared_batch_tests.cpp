#include "prepared_batch.h"
#include <cassert>
#include <iostream>
#include <map>
#include <cstdlib>
#include <new>
static size_t allocations=0;
void* operator new(size_t n){++allocations;if(auto p=std::malloc(n))return p;throw std::bad_alloc();}
void operator delete(void* p) noexcept{std::free(p);}
using namespace rui_prepared;
int main(){
 std::vector<RuiVertex> v={{0,0,0,0,255,0,0,128},{8,0,1,0,0,255,0,255},{0,8,0,1,0,0,255,255}};std::vector<int> ix={0,1,2};
 auto geometry=[&](uint64_t id){return id==1||id==2?View{v.data(),v.size(),ix.data(),ix.size()}:View{};};
 RuiFrameStamp s{};s.epoch=1;s.commands=2;s.resources=3;s.flags=1;s.count=4;
 std::vector<RuiCommand> c(4);for(auto& d:c){d.geometry=1;d.texture=7;}c[1].geometry=2;c[2].clip_enabled=1;c[2].clip_w=10;c[2].clip_h=10;
 uint8_t mask[]={1,1,1,0};Session session;RuiPreparedPacket p{};assert(session.Acquire(s,c,mask,4,geometry,p)==1);assert(p.draw_count==3&&p.built_batches==3);
 auto plan=session.Get(p.lease);assert(plan&&plan->batches.size()==3);auto first=plan->batches[0];assert(first->draw.count==2&&first->vertices.size()==6&&first->indices[3]==3);assert(first->vertices[3].slot==1&&first->vertices[0].a==128);assert(plan->batches[2]->draw.kind==1);
 size_t before=allocations;RuiPreparedPacket same{};assert(session.Acquire(s,c,mask,4,geometry,same)==1);assert(same.plan==p.plan&&same.built_batches==0&&same.reused_batches==3);assert(allocations==before);
 assert(session.Release(p.lease)==1);assert(session.Release(p.lease)<0);assert(session.Release(same.lease)==1);
 // Source bytes and vectors may disappear while a CPU lease remains valid.
 RuiPreparedPacket held{};assert(session.Acquire(s,c,mask,4,geometry,held)==1);auto frozen=session.Get(held.lease);auto pixel=frozen->batches[0]->vertices[0].a;v.clear();ix.clear();assert(frozen->batches[0]->vertices[0].a==pixel);
 assert(session.Close()==-5);assert(session.Acquire(s,c,mask,4,geometry,p)<0);assert(session.Get(held.lease));assert(session.Release(held.lease)==1);assert(!session.Pending());
 v={{0,0,0,0,255,0,0,128},{8,0,1,0,0,255,0,255},{0,8,0,1,0,0,255,255}};ix={0,1,2};
 Session bounded(100);assert(bounded.Acquire(s,c,mask,4,geometry,p)==-4&&bounded.Retained()==0);
 Session dynamic;RuiPreparedPacket a{},b{},x{};assert(dynamic.Acquire(s,c,mask,4,geometry,a)==1);auto old=dynamic.Get(a.lease);auto batch=old->batches[0]->draw.batch;
 std::swap(c[0].geometry,c[1].geometry);s.commands++;assert(dynamic.Acquire(s,c,mask,4,geometry,b)==1);assert(dynamic.Get(b.lease)->batches[0]->draw.batch!=batch);assert(dynamic.Get(b.lease)->batches[1]==old->batches[1]);
 // Failed transaction must not publish or retain temporary replacement bytes.
 auto bytes=dynamic.Retained();c[0].geometry=99;assert(dynamic.Acquire(s,c,mask,4,geometry,x)==-2&&dynamic.Retained()==bytes&&x.lease==0);c[0].geometry=2;
 ix[0]=-1;s.epoch++;assert(dynamic.Acquire(s,c,mask,4,geometry,x)==-1);ix[0]=0;
 assert(dynamic.Acquire(s,c,mask,4,geometry,x)==1);assert(dynamic.Get(x.lease)->batches[0]->draw.batch!=batch);RuiPreparedPacket fourth{};assert(dynamic.Acquire(s,c,mask,4,geometry,fourth)==1);RuiPreparedPacket fifth{};assert(dynamic.Acquire(s,c,mask,4,geometry,fifth)==-5);
 int foreign=0;std::thread t([&]{foreign=dynamic.Release(a.lease);});t.join();assert(foreign==-3);
 for(auto token:{a.lease,b.lease,x.lease,fourth.lease})assert(dynamic.Release(token)==1);
 assert(dynamic.Release(session.id)==-2);mask[0]=2;assert(dynamic.Acquire(s,c,mask,4,geometry,p)==-1);mask[0]=1;
 assert(sizeof(RuiVertex)==20&&sizeof(RuiCommand)==48&&sizeof(RuiProfile)==104);
 std::cout<<"native prepared ordered/packed/cache PASS\n";
}
