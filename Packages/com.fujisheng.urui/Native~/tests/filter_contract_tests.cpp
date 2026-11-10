#include "test_document.h"
#include "urui.h"
#include <cassert>
#include <fstream>
#include <iterator>
#include <vector>
#include <cstring>
#include <iostream>
int main(int argc,char** argv){
 assert(argc==2);std::ifstream f(argv[1]);std::string markup((std::istreambuf_iterator<char>(f)),{});
 assert(urui_create(600,400)==1);const auto owner = LoadTestDocument(markup.c_str(), argv[1]);urui_update();urui_render();
 int count=urui_get_layer_commands(nullptr,0);assert(count>0);std::vector<UruiLayerCommand> commands(count);assert(urui_get_layer_commands(commands.data(),count)==count);
 const char* expected[]={"brightness","contrast","blur","drop-shadow"};int index=0;
 for(auto c:commands)if(c.kind==6){assert(index<4&&c.blend==0);UruiFilter d;char name[64];assert(urui_filter_info(c.mask,&d,name,64)>0);assert(!strcmp(name,expected[index]));if(index<2)assert(d.value==.5f);if(index==2)assert(d.sigma==2);if(index==3)assert(d.offset_x==4&&d.offset_y==3&&d.sigma==1&&d.red==1&&d.alpha==1);index++;}
 assert(index==4);count=urui_get_layer_regions(nullptr,0);assert(count>0);std::vector<UruiLayerRegion> regions(count);assert(urui_get_layer_regions(regions.data(),count)==count);
 assert(regions[0].width>140&&regions[0].height>70);assert(regions[0].x<10&&regions[0].y<30);
 assert(urui_document_close(owner) == 1);urui_destroy();std::cout<<"ordered native filters, original parsed values and transformed overflow/halo regions passed\n";
}
