#include "../render_plan_compiler.h"
#include <array>
#include <cassert>
#include <iostream>

using namespace urui_plan;
static const UruiVertex small[]={{0,0,0,0,255,255,255,128},{8,0,1,0,255,255,255,128},{8,8,1,1,255,255,255,128},{0,8,0,1,255,255,255,128}};
static const UruiVertex full[]={{0,0,0,0,255,255,255,128},{256,0,1,0,255,255,255,128},{256,256,1,1,255,255,255,128},{0,256,0,1,255,255,255,128}};
static const int indices[]={0,1,2,0,2,3};
static GeometrySource Resolve(uint64_t id){return {id==2?full:small,4,indices,6};}

static void ColorGroupBounds(bool known_root,bool unbounded,uint32_t excluded=0,bool nested=false)
{
    std::array<UruiCommand,10> commands{};
    std::array<std::array<float,16>,10> matrices{};
    std::array<UruiPlanTextureBinding,10> textures{};
    for(int n=0;n<10;n++)
    {
        matrices[n]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
        commands[n].geometry=n==9?2:1;commands[n].texture=n==9?99:10+n;
        commands[n].tx=n==9?0:10+n*17;commands[n].ty=n==9?0:20;
        textures[n]={commands[n].texture,1,n==9?1u:0u,1};
    }
    if(unbounded){commands[0].tx=0;matrices[0][3]=-.25f;}
    std::array<UruiLayerCommand,3> layers{};
    layers[0].kind=1;layers[0].target=1;
    layers[1].kind=4;layers[1].source=1;layers[1].command_index=10;
    layers[2].kind=2;layers[2].source=1;layers[2].command_index=10;
    Session session;session.Viewport(256,256);
    UruiPlanInputPacket input{};UruiRenderPlanPacket packet{};
    assert(session.Begin(7,commands.data(),matrices.data(),commands.size(),nullptr,0,nested?layers.data():nullptr,nested?layers.size():0,input)==1);
    UruiPlanBindings bindings{};bindings.textures=textures.data();bindings.texture_count=textures.size();
    bindings.frame_flags=1|2|8|16|32|256|(known_root?512:0);
    bindings.frame_flags&=~excluded;
    assert(session.Compile(input.lease,bindings,Resolve,packet)==1);
    int starts=0,css=0;
    for(uint32_t n=0;n<packet.operation_count;n++)
    {
        const auto& operation=packet.operations[n];
        if(operation.kind==URUI_PLAN_GEOMETRY&&operation.textures[0]!=99)css++;
        if(!(operation.reason&URUI_PLAN_RASTER_BEGIN))continue;
        starts++;
        const bool bounded=(operation.reason&0x00800000u)!=0;
        const bool expected=known_root&&!unbounded&&!nested&&!(excluded&(1|16));
        if(bounded!=expected)std::cerr<<"known="<<known_root<<" unbounded="<<unbounded<<" nested="<<nested<<" excluded="<<excluded<<" reason="<<operation.reason<<"\n";
        assert(bounded==expected);
        if(bounded)
        {
            // 九张CSS纹理超过单批八纹理上限，首批的保守范围必须包含后一批。
            assert(operation.bounds[0]==10&&operation.bounds[1]==20&&operation.bounds[2]==154&&operation.bounds[3]==28);
        }
    }
    assert(starts==1&&css==2);
}
int main()
{
    ColorGroupBounds(true,false);
    ColorGroupBounds(false,false);
    ColorGroupBounds(true,true);
    ColorGroupBounds(true,false,1);
    ColorGroupBounds(true,false,16);
    ColorGroupBounds(true,false,0,true);
    std::cout<<"bounded CSS groups and unknown/unbounded roots passed\n";
}
