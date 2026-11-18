#include "urui.h"
#include "../graphic_tint.h"
#include "../Engine/Source/Core/ElementStyle.h"
#include <URUI/Core.h>
#include "../Engine/Source/Core/CssImageGeometry.h"
#include <cassert>
#include <cmath>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>

static double now = 0;
static void Tick() { now += .2; assert(urui_set_ui_time(now) == 1); urui_update(); now += .2; assert(urui_set_ui_time(now) == 1); urui_update(); }
static void Move(int phase, int buttons = 0, float x = 40, float y = 40) { assert(urui_pointer_event(0,0,phase,x,y,0,buttons,0,0,0)==1); Tick(); }
static URUI::Colourb Background(URUI::Element* e) { return e->GetComputedValues().background_color(); }
static void Equal(URUI::Colourb got, URUI::Colourb want) {
    if (got != want) std::cerr << "RGBA actual " << int(got.red) << ',' << int(got.green) << ',' << int(got.blue) << ',' << int(got.alpha)
        << " expected " << int(want.red) << ',' << int(want.green) << ',' << int(want.blue) << ',' << int(want.alpha) << '\n';
    assert(got == want);
}
static void Vertices(uint64_t texture, int r, int g, int b, int a, float extent = -1) {
    UruiPlanInputPacket input{};assert(urui_plan_begin(&input)==1);bool found=false;
    struct ReleaseInput {uint64_t lease;~ReleaseInput(){assert(urui_plan_release(lease)==1);}} release{input.lease};
    for (uint32_t index=0;index<input.command_count;index++) {
        const auto& command=input.commands[index];if(command.texture!=texture)continue;
        int vc=0,ic=0;assert(urui_get_geometry_info(command.geometry,&vc,&ic)==1);
        std::vector<UruiVertex> v(vc);std::vector<int> ids(ic);assert(urui_copy_geometry(command.geometry,v.data(),vc,ids.data(),ic)==1);
        // The color oracle applies immutable draw uniforms to base vertices,
        // exactly as the production vertex shader will. Expected RGBA stays unchanged.
        const auto& uniform=input.uniforms[index];
        for(auto& vertex:v) {
            vertex.r=uint8_t(std::lround(vertex.r*uniform.tint[0]));vertex.g=uint8_t(std::lround(vertex.g*uniform.tint[1]));
            vertex.b=uint8_t(std::lround(vertex.b*uniform.tint[2]));vertex.a=uint8_t(std::lround(vertex.a*uniform.tint[3]));
            vertex.u=vertex.u*uniform.uv[0]+uniform.uv[2];vertex.v=vertex.v*uniform.uv[1]+uniform.uv[3];
        }
        if (extent >= 0) {
            float low=1e9f,high=-1e9f;for(const auto& x:v){low=std::min(low,x.x);high=std::max(high,x.x);}
            if(std::abs(high-low-extent)>.01f)continue;
        }
        for(const auto& x:v) {
            if (std::abs(int(x.r)-r)>1 || std::abs(int(x.g)-g)>1 || std::abs(int(x.b)-b)>1 || std::abs(int(x.a)-a)>1)
                std::cerr << "vertex RGBA " << int(x.r) << ',' << int(x.g) << ',' << int(x.b) << ',' << int(x.a) << " expected " << r << ',' << g << ',' << b << ',' << a << '\n';
            assert(std::abs(int(x.r)-r)<=1 && std::abs(int(x.g)-g)<=1 && std::abs(int(x.b)-b)<=1 && std::abs(int(x.a)-a)<=1);found=true;
        }
    }
    assert(found);
}
int main(int argc,char** argv) {
    assert(argc==4);std::ifstream cssFile(argv[1]),mapFile(argv[2]);
    const std::string css((std::istreambuf_iterator<char>(cssFile)),{}),map((std::istreambuf_iterator<char>(mapFile)),{}),mode=argv[3];
    assert(!css.empty()&&!map.empty());assert(urui_create(600,480)==1);assert(urui_set_ui_time(0)==1);
    UruiDocumentHandle owner=0;assert(urui_document_create(&owner)==1);assert(urui_document_set_presentation(owner,0,0,1,0)==1);
    if(mode=="transition-idle") {
        const std::string html="<!doctype html><html><head><style>#value{opacity:1;transition:opacity .1s linear;}#value.go{opacity:.5;}</style></head><body><div id='value'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/transition-idle.html")==1);urui_update();
        auto* element=URUI::GetContext("urui")->GetDocument(0)->GetElementById("value");element->SetClass("go",true);urui_update();
        const auto revision=element->GetStyle()->GetComputedRevision();urui_update();
        assert(element->GetStyle()->GetComputedRevision()==revision);assert(element->GetComputedValues().opacity()==1);
        assert(urui_set_ui_time(.05)==1);urui_update();assert(std::abs(element->GetComputedValues().opacity()-.75f)<.001f);
        urui_destroy();return 0;
    }
    if(mode=="animation-idempotent") {
        const std::string html="<!doctype html><html><head><style>@property --phase{syntax:'<number>';inherits:false;initial-value:0;}@property --tint{syntax:'<color>';inherits:false;initial-value:white;}@keyframes hold{from{--phase:.5;--tint:red;}to{--phase:.5;--tint:red;}}#value{animation:hold 1s linear;}</style></head><body><div id='value'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/animation-idempotent.html")==1);Tick();
        auto* element=URUI::GetContext("urui")->GetDocument(0)->GetElementById("value");const auto revision=element->GetStyle()->GetComputedRevision();
        now+=.05;assert(urui_set_ui_time(now)==1);urui_update();assert(element->GetStyle()->GetComputedRevision()==revision);
        assert(std::abs(element->GetProperty("--phase")->Get<float>()-.5f)<.001f);Equal(element->GetProperty("--tint")->Get<URUI::Colourb>(),{255,0,0,255});
        assert(urui_set_ui_time(1.3)==1);urui_update();assert(element->GetStyle()->GetComputedRevision()>revision);assert(element->GetProperty("--phase")->Get<float>()==0);
        urui_destroy();return 0;
    }
    if(mode=="property-sets") {
        URUI::PropertyIdSet a,b;const int ids[]={1,63,64,127,128,255,256,511};
        assert(a.Empty()&&a.begin()==a.end());for(int id:ids)a.Insert(static_cast<URUI::PropertyId>(id));
        assert(a.Size()==8);std::vector<int> actual;for(auto id:a)actual.push_back(int(id));assert(actual==std::vector<int>(ids,ids+8));
        for(int id:{1,64,128,256})b.Insert(static_cast<URUI::PropertyId>(id));
        auto intersection=a&b;assert(intersection.Size()==4);assert((a|b).Size()==8);
        for(auto it=a.begin();it!=a.end();)if(int(*it)%2)it=a.Erase(it);else ++it;
        assert(a.Size()==3&&a.Contains(static_cast<URUI::PropertyId>(64))&&a.Contains(static_cast<URUI::PropertyId>(128))&&a.Contains(static_cast<URUI::PropertyId>(256)));
        a.Clear();assert(a.Empty()&&a.Size()==0&&a.begin()==a.end());
        bool threw=false;try{a.Contains(URUI::PropertyId::MaxNumIds);}catch(const std::out_of_range&){threw=true;}assert(threw);
        urui_destroy();return 0;
    }
    if(mode=="registered-capacity") {
        std::string declarations=css;
        for(int i=0;i<192;i++) declarations+="@property --author"+std::to_string(i)+"{syntax:'<length>';inherits:false;initial-value:1px;}";
        const std::string html="<!doctype html><html><head><style>"+declarations+"#capacity{--author191:17px;width:var(--author191);}</style></head><body><div id='capacity'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/capacity.html")==1);Tick();
        auto* element=URUI::GetContext("urui")->GetDocument(0)->GetElementById("capacity");
        size_t highest=0;std::string highest_name;
        for(int i=0;i<192;i++){const std::string name="--author"+std::to_string(i);const size_t id=size_t(element->GetPropertySpecification().GetPropertyId(name));if(id>highest){highest=id;highest_name=name;}}
        assert(highest>255);element->SetProperty(highest_name,"17px");element->SetProperty("width","var("+highest_name+")");Tick();assert(element->GetBox().GetSize().x==17);
        std::string overflow="<!doctype html><html><head><style>";
        for(int i=0;i<600;i++) overflow+="@property --overflow"+std::to_string(i)+"{syntax:'<number>';inherits:false;initial-value:0;}";
        overflow+="</style></head><body></body></html>";
        assert(urui_document_load_html(owner,overflow.c_str(),"/overflow.html")==-8);Tick();
        assert(URUI::GetContext("urui")->GetDocument(0)->GetElementById("capacity")==element);assert(element->GetBox().GetSize().x==17);
        urui_destroy();return 0;
    }
    if(mode=="supports") {
        const char* html="<!doctype html><html><head><style>#supported{width:25px;} @supports (display:block) {#supported{width:40px;}} @supports (color:color-mix(in srgb,white,black)) {#supported{width:80px;}} @supports not (display:block) {#supported{width:90px;}}</style></head><body><div id='supported'></div></body></html>";
        assert(urui_document_load_html(owner,html,"/supports.html")==1);Tick();
        auto* element=URUI::GetContext("urui")->GetDocument(0)->GetElementById("supported");
        std::cerr<<"supports width="<<element->GetBox().GetSize().x<<'\n';assert(element->GetBox().GetSize().x==40);
        urui_destroy();return 0;
    }
    if(mode=="slider-feedback") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><urui-slider><input id='range' type='range'><div id='thumb' class='urui-slider-thumb'></div></urui-slider></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/slider-feedback.html")==1);Tick();
        auto* document=URUI::GetContext("urui")->GetDocument(0);auto* field=document->GetElementById("range");auto* thumb=document->GetElementById("thumb");
        Equal(urui_graphic::Tint(*thumb,Background(thumb)),{255,255,255,255});
        field->SetPseudoClass("hover",true);Tick();Equal(urui_graphic::Tint(*thumb,Background(thumb)),{245,245,245,255});
        field->SetPseudoClass("active",true);Tick();Equal(urui_graphic::Tint(*thumb,Background(thumb)),{200,200,200,255});
        field->SetAttribute("disabled",true);Tick();Equal(urui_graphic::Tint(*thumb,Background(thumb)),{200,200,200,128});
        urui_destroy();return 0;
    }
    if(mode=="solid-graphic") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><div style='width:20px;height:20px;background-color:#80402080;--urui-graphic-tint:#c8c8c880;'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/graphic-solid.html")==1);Tick();Vertices(0,25,13,6,64,20);
        urui_destroy();return 0;
    }
    if(mode=="mask-graphic") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><div id='graphic' style='width:20px;height:20px;background-image:linear-gradient(white,white);mask-image:linear-gradient(white,white);--urui-graphic-tint:#c8c8c880;'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/graphic-mask.html")==1);Tick();auto* element=URUI::GetContext("urui")->GetDocument(0)->GetElementById("graphic");
        auto mask=URUI::GenerateCssImageMesh(element,URUI::PropertyId::MaskImage,0,{20,20},false,-1);assert(!mask.vertices.empty());
        for(const auto& v:mask.vertices) assert(v.colour.red==255&&v.colour.alpha==255);
        auto background=URUI::GenerateCssImageMesh(element,URUI::PropertyId::BackgroundImage,0,{20,20},false,-1);assert(!background.vertices.empty());
        for(const auto& v:background.vertices) {std::cerr<<"background RGBA="<<int(v.colour.red)<<','<<int(v.colour.alpha)<<'\n';assert(std::abs(int(v.colour.red)-100)<=1&&v.colour.alpha==128);}
        urui_destroy();return 0;
    }
    if(mode=="vertical-slider"||mode=="horizontal-slider") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><input id='range' type='range' data-urui-orientation='"+(mode=="vertical-slider"?"vertical":"horizontal")+"' value='50'></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/range.html")==1);Tick();
        auto* field=URUI::GetContext("urui")->GetDocument(0)->GetElementById("range");URUI::Element* track=nullptr;URUI::Element* thumb=nullptr;
        for(int n=0;n<field->GetNumChildren(true);n++) {
            auto* child=field->GetChild(n);if(child->GetTagName()=="urui-internal-range-track")track=child;if(child->GetTagName()=="urui-internal-range-thumb")thumb=child;
        }
        assert(track&&thumb);std::cerr<<"vertical track="<<track->GetBox().GetSize().x<<" thumb="<<thumb->GetBox().GetSize(URUI::BoxArea::Border).x<<'\n';
        int axis=mode=="vertical-slider"?0:1;
        assert(std::abs(track->GetBox().GetSize()[axis]-10)<.01f);assert(std::abs(thumb->GetBox().GetSize(URUI::BoxArea::Border)[axis]-20)<.01f);
        const float track_center=track->GetAbsoluteOffset(URUI::BoxArea::Border)[axis]+track->GetBox().GetSize()[axis]*.5f;
        const float thumb_center=thumb->GetAbsoluteOffset(URUI::BoxArea::Border)[axis]+thumb->GetBox().GetSize(URUI::BoxArea::Border)[axis]*.5f;
        std::cerr<<"track center="<<track_center<<" thumb center="<<thumb_center<<'\n';assert(std::abs(track_center-thumb_center)<.01f);
        urui_destroy();return 0;
    }
    if(mode=="animation-start") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><div id='popup' class='urui-select-options'></div></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/animation-start.html")==1);
        auto* popup=URUI::GetContext("urui")->GetDocument(0)->GetElementById("popup");popup->SetClass("urui-select-opening",true);
        urui_update();urui_update();std::cerr<<"initial opacity="<<popup->GetComputedValues().opacity()<<'\n';assert(popup->GetComputedValues().opacity()==0);
        assert(urui_set_ui_time(.075)==1);urui_update();urui_update();assert(std::abs(popup->GetComputedValues().opacity()-.5f)<.001f);
        urui_destroy();return 0;
    }
    if(mode=="caret" || mode=="selection" || mode=="native-skins") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><input id='field' type='text' value='Text' style='color:#406080;width:100px;height:30px;'><select id='select'><option>A</option></select></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/input.html")==1);Tick();
        auto* document=URUI::GetContext("urui")->GetDocument(0);auto* input=document->GetElementById("field");
        if(mode=="caret") {std::cerr<<input->GetProperty("--urui-caret-blink-rate")->Get<float>()<<'\n';assert(std::abs(input->GetProperty("--urui-caret-blink-rate")->Get<float>()-.85f)<.0001f);}
        else if(mode=="selection") Equal(input->GetChild(1)->GetComputedValues().color(),{64,96,128,255});
        else {
            auto* select=document->GetElementById("select");URUI::Element* arrow=nullptr;
            for(int n=0;n<select->GetNumChildren(true);++n) if(select->GetChild(n)->GetTagName()=="urui-internal-select-arrow") arrow=select->GetChild(n);
            assert(arrow);Equal(arrow->GetComputedValues().border_top_color(),{50,50,50,255});
        }
        urui_destroy();return 0;
    }
    if(mode=="text") {
        const std::string html="<!doctype html><html><head><style>"+css+"</style></head><body><input id='field' type='text' placeholder='Hint' style='color:#406080;width:100px;height:30px;'></body></html>";
        assert(urui_document_load_html(owner,html.c_str(),"/input.html")==1);Tick();
        auto* input=URUI::GetContext("urui")->GetDocument(0)->GetElementById("field");
        Equal(input->GetComputedValues().color(),{64,96,128,255});
        assert(input->GetNumChildren(true)>=2);
        Equal(input->GetChild(0)->GetComputedValues().color(),{64,96,128,128});
        Equal(input->GetChild(1)->GetComputedValues().color(),{64,96,128,255});
        assert(std::abs(input->GetProperty("--urui-caret-blink-rate")->Get<float>()-.85f)<.0001f);
        assert(urui_set_attribute(owner,"field","value","Text")==1);Tick();
        Equal(input->GetChild(0)->GetComputedValues().color(),{64,96,128,255});
        urui_destroy();return 0;
    }
    std::string classes=mode=="button"?"":"map-control "+mode;
    const std::string style="position:fixed;left:20px;top:20px;margin:0;width:100px;height:80px;";
    std::string children;
    uint64_t graphic=0,decoration=0;
    if(mode=="image"||mode=="border-graphic") {
        const uint8_t pixels[]={255,255,255,255}, other[]={0,255,0,255};
        assert(urui_document_register_rgba_texture(owner,"/target.png",1,1,pixels,4,&graphic)==1);
        assert(urui_document_register_rgba_texture(owner,"/decoration.png",1,1,other,4,&decoration)==1);
        children=(mode=="image"?"<img data-urui-graphic src='/target.png' style='position:absolute;width:20px;height:20px;--urui-image-color:#80402080;'>":"<span data-urui-graphic style='position:absolute;width:20px;height:20px;--urui-image-color:#80402080;border-image:url(/target.png) 0 fill / 0 / 0 stretch;'></span>")+std::string("<img src='/decoration.png' style='position:absolute;left:30px;width:20px;height:20px;'>");
    }
    std::string html="<!doctype html><html><head><style>"+css+(mode=="button"||mode=="image"||mode=="border-graphic"?"":map)+"</style></head><body><main class='map-canvas'><button id='probe' class='"+classes+"' "+((mode=="image"||mode=="border-graphic")?"data-urui-skin='image' ":"")+"style='"+style+"'>"+children+"</button></main></body></html>";
    assert(urui_document_load_html(owner,html.c_str(),"/controls-test.html")==1);Tick();
    auto* probe=URUI::GetContext("urui")->GetDocument(0)->GetElementById("probe");assert(probe);
    Move(1,0,500,400);
    if(mode=="image"||mode=="border-graphic") Vertices(graphic,64,32,16,128);else Equal(Background(probe),mode=="button"?URUI::Colourb(255):URUI::Colourb(0,0));
    Move(1);
    if(mode=="image"||mode=="border-graphic") {Vertices(graphic,62,31,15,128);Vertices(decoration,255,255,255,255);}
    else Equal(Background(probe),mode=="button"?URUI::Colourb(245,245,245):URUI::Colourb(0,0));
    Move(0,1);
    if(mode=="image"||mode=="border-graphic") {Vertices(graphic,50,25,13,128);Vertices(decoration,255,255,255,255);}
    else Equal(Background(probe),mode=="button"?URUI::Colourb(200,200,200):URUI::Colourb(0,0));
    Move(2);assert(urui_set_attribute(owner,"probe","disabled","")==1);Tick();
    if(mode=="image"||mode=="border-graphic") {Vertices(graphic,25,13,6,64);Vertices(decoration,255,255,255,255);}
    else Equal(Background(probe),mode=="button"?URUI::Colourb(200,200,200,128):URUI::Colourb(0,0));
    assert(urui_document_close(owner)==1);urui_update();urui_destroy();std::cout<<mode<<" states passed\n";
}
