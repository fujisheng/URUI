#include "lookup_probe.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/Factory.h>
namespace {
Rml::ElementPtr Node(const char* id){auto e=Rml::Factory::InstanceElement(nullptr,"div","div",{});e->SetId(id);return e;}
bool Same(Rml::Element* root,const char* id){rui_lookup::inline_enabled=false;auto* expected=root->GetElementById(id);rui_lookup::inline_enabled=true;return expected==root->GetElementById(id);}
}
extern "C" int rui_lookup_queue_selftest(){
 auto* ctx=Rml::GetContext("rmlunity");if(!ctx||ctx->GetNumDocuments()!=0)return -1;
 struct Restore {bool enabled=rui_lookup::inline_enabled;int mode=rui_lookup::profile_mode;RuiLookupProfile counters=rui_lookup::profile;~Restore(){rui_lookup::inline_enabled=enabled;rui_lookup::profile_mode=mode;rui_lookup::profile=counters;}} restore;
 int pass=0;auto root=Node("root");auto* nest=root->AppendChild(Node("nest"));auto* deep=nest->AppendChild(Node("dup"));auto* sibling=root->AppendChild(Node("dup"));
 if(Same(root.get(),"dup")&&root->GetElementById("dup")==sibling)pass|=1;
 sibling->SetId("renamed");bool dynamic=Same(root.get(),"dup")&&root->GetElementById("dup")==deep;
 sibling->RemoveAttribute("id");dynamic&=Same(root.get(),"renamed")&&root->GetElementById("renamed")==nullptr;
 auto moved=nest->RemoveChild(deep);root->InsertBefore(std::move(moved),nest);dynamic&=Same(root.get(),"dup")&&root->GetElementById("dup")==deep;
 auto replacement=Node("new");auto* inserted=replacement.get();auto old=root->ReplaceChild(std::move(replacement),deep);old.reset();dynamic&=Same(root.get(),"dup")&&Same(root.get(),"new")&&root->GetElementById("new")==inserted;
 auto non_dom=Node("internal");root->AppendChild(std::move(non_dom),false);dynamic&=Same(root.get(),"internal")&&root->GetElementById("internal")==nullptr;
 if(dynamic)pass|=2;
 if(Same(root.get(),"#self")&&Same(nest,"#parent")&&Same(nest,"#document")&&Same(root.get(),""))pass|=4;
 bool widths=true;for(int width:{255,256,257,1024}){auto tree=Node("wide");for(int i=0;i<width;i++){auto child=Node(i==width-1?"tail":"other");tree->AppendChild(std::move(child));}widths&=Same(tree.get(),"tail")&&Same(tree.get(),"missing")&&Same(tree.get(),"other");}
 auto overflow_root=Node("overflow-root");auto* fanout=overflow_root->AppendChild(Node("fanout"));auto* waiting_sibling=overflow_root->AppendChild(Node("winner"));
 for(int i=0;i<300;i++)fanout->AppendChild(Node(i==0?"winner":"other"));
 widths&=Same(overflow_root.get(),"winner")&&overflow_root->GetElementById("winner")==waiting_sibling;
 if(widths)pass|=8;
 auto chain=Node("chain");auto* tip=chain.get();for(int i=0;i<600;i++)tip=tip->AppendChild(Node(i==599?"last":"path"));if(Same(chain.get(),"last")&&Same(chain.get(),"missing"))pass|=16;
 rui_lookup::inline_enabled=true;rui_lookup::profile_mode=2;rui_lookup::profile={};for(int i=0;i<6;i++)chain->GetElementById("missing");if(rui_lookup::profile.queries==6&&rui_lookup::profile.nodes_visited==6*601)pass|=32;
 rui_lookup::profile={};root->GetElementById("new");if(rui_lookup::profile.inline_calls==1&&rui_lookup::profile.overflow_calls==0)pass|=64;
 auto* doc=ctx->CreateDocument();auto* a=doc->AppendChild(Node("a"));doc->AppendChild(Node("peer"));bool document=Same(a,"peer")&&a->GetElementById("peer")!=nullptr;auto observer=a->GetObserverPtr();doc->Close();ctx->Update();document&=!observer;
 if(document)pass|=128;
 return pass;
}
