#include "test_document.h"
#include "urui.h"
#include <cassert>
#include <chrono>
#include <thread>
#include <fstream>
#include <iterator>
#include <map>
#include <string>
#include <iostream>
std::map<uint64_t,std::string> created,ended;
int Callback(const UruiElementEvent* event){
 if(event->type==17||event->type==19){char name[128];uint64_t id;int iterations;assert(urui_animation_current_event(event->document,name,128,&id,&iterations)>0);assert(id&&iterations==1);(event->type==19?created:ended)[id]=name;}return 0;
}
int main(int argc,char** argv){
 assert(argc==2);std::ifstream input(argv[1]);std::string markup((std::istreambuf_iterator<char>(input)),{});
 assert(urui_animation_api_version()==1);assert(urui_create(400,200)==1);const auto owner=LoadTestDocument(markup.c_str(),argv[1]);
 assert(urui_set_event_callback(Callback)==1);assert(urui_document_listen(owner)==1);urui_update();auto epoch=urui_animation_next_identity();
 assert(urui_element_class(owner,urui_document_root(owner),"running",1)==1);
 for(int frame=0;frame<60&&ended.size()<2;frame++){urui_update();std::this_thread::sleep_for(std::chrono::milliseconds(10));}
 assert(created.size()==2&&ended.size()==2);for(auto entry:created){assert(entry.first>=epoch&&ended.at(entry.first)==entry.second);}
 assert(urui_animation_next_identity()>epoch);assert(urui_document_close(owner)==1);urui_destroy();
 created.clear();ended.clear();{assert(urui_create(400,200)==1);assert(urui_ui_clock_api_version()==1);assert(urui_set_ui_time(0)==1);const auto owner=LoadTestDocument(markup.c_str(),argv[1]);assert(urui_set_event_callback(Callback)==1);assert(urui_document_listen(owner)==1);urui_update();assert(urui_element_class(owner,urui_document_root(owner),"running",1)==1);
 for(int frame=0;frame<8;frame++)urui_update();assert(created.size()==2&&ended.empty());
 for(int frame=1;frame<=30;frame++){assert(urui_set_ui_time(frame*.02)==1);urui_update();}assert(ended.size()==2);urui_destroy();std::cout<<"named track identities and document-owned event packets passed\n";}
}
