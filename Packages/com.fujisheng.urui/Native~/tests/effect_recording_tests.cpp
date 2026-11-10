#include "test_document.h"
#include "urui.h"
#include <cassert>
#include <fstream>
#include <iterator>
#include <iostream>
#include <vector>
#include <cstring>
int Resolve(UruiDocumentHandle,const char*,uint64_t* id,uint64_t* source,int* w,int* h) { *source=1; *id=0x8000000000000001ULL;*w=64;*h=64;return 1; }
void Release(uint64_t) {}
int main(int argc,char** argv) {
 assert(argc==2); std::ifstream input(argv[1]); std::string markup((std::istreambuf_iterator<char>(input)),{});
 assert(urui_create(600,400)==1);assert(urui_set_image_resolver(Resolve,Release)==1); const auto owner = LoadTestDocument(markup.c_str(), argv[1]);
 urui_update(); urui_render();
 std::vector<UruiPaintCommand> effects(2); assert(urui_get_paint_commands(nullptr,0)==2);
 assert(urui_get_paint_commands(effects.data(),2)==2);
 assert(effects[0].element!=effects[1].element && effects[0].shader!=effects[1].shader);
 for (auto effect:effects) { assert(effect.reserved==1); char name[40]; assert(urui_paint_shader_name(effect.shader,name,40)>0); assert(!strcmp(name,"probe")); }
 assert(urui_effect_api_version()==7);
 // 原图局部尺寸允许超过纹理上限，但最终未缩放的图层仍须拒绝分配。
 assert(urui_effect_ink_bounds(effects[0].command_index,0,0,10437.25f,5218.63f)==1);
 { const int count=urui_get_layer_regions(nullptr,0); std::vector<UruiLayerRegion> large(count); assert(urui_get_layer_regions(large.data(),count)==-3); }
 assert(urui_effect_ink_bounds(effects[0].command_index,-25,-8,350,200)==1);
 int regions_count=urui_get_layer_regions(nullptr,0);assert(regions_count>0);std::vector<UruiLayerRegion> regions(regions_count);assert(urui_get_layer_regions(regions.data(),regions_count)==regions_count);assert(regions[0].x<=-25&&regions[0].width>=350);
 UruiFrameStamp stamp{}; assert(urui_render_plan_stamp(&stamp)==1); assert(stamp.flags==2); // Never replay incomplete effect data.
 std::vector<UruiCommand> commands(urui_command_count()); urui_get_commands(commands.data(),commands.size());
 std::vector<uint64_t> geometry; for (auto effect:effects) geometry.push_back(commands[effect.command_index].geometry);
 auto root=urui_document_root(owner); auto a=urui_element_find(owner, root,"a",nullptr);
 assert(urui_element_class(owner, a,"plain",1)==1); urui_update(); urui_render(); assert(urui_get_paint_commands(nullptr,0)==1);
 commands.resize(urui_command_count()); urui_get_commands(commands.data(),commands.size());
 bool retained=false; for(auto command:commands) retained |= command.geometry==geometry[0]; assert(retained); // Same image mesh, no overlay.
 assert(urui_element_class(owner, a,"plain",0)==1); assert(urui_element_number(owner, a,"--probe-progress",.6f,0)==1);
 urui_update();urui_render();assert(urui_get_paint_commands(nullptr,0)==2);
 assert(urui_element_class(owner, a,"hidden",1)==1);urui_update();urui_render();assert(urui_get_paint_commands(nullptr,0)==1);
 assert(urui_document_close(owner) == 1);urui_destroy(); std::cout<<"image effects: independent identities, original geometry, visibility and explicit cache rejection passed\n";
}
