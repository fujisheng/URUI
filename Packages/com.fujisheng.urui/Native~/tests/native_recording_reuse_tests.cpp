#include "test_document.h"
#include "../render_plan_protocol.h"
#include <URUI/Core.h>
#include <URUI/Core/Decorator.h>
#include <URUI/Core/ElementText.h>
#include <URUI/Core/FontEngineInterface.h>
#include <filesystem>
#include <algorithm>
#include <array>
#include <cassert>
#include <chrono>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

struct Frame
{
    UruiPlanInputPacket input{};
    UruiRenderPlanPacket output{};
    Frame()
    {
        assert(urui_plan_begin(&input)==1);
        UruiPlanBindings bindings{};
        assert(urui_plan_compile(input.lease,&bindings,&output)==1);
    }
    ~Frame(){assert(urui_plan_release(output.lease)==1&&urui_plan_release(input.lease)==1);}
};

static double StableRender(int count)
{
    assert(urui_create(128,128)==1);
    std::string html="<!doctype html><html><head><style>body{margin:0;}div{position:absolute;width:4px;height:4px;background:red;}</style></head><body>";
    for(int n=0;n<count;n++)html+="<div></div>";
    html+="</body></html>";
    LoadTestDocument(html.c_str(),"/stable-native-recording.html");
    for(int n=0;n<8;n++)
    {
        auto* context=URUI::GetContext("urui");
        const auto before=context->GetNativeRenderRevision();urui_update();
        const auto updated=context->GetNativeRenderRevision();urui_render();
        if(n<3)std::cout<<"warm="<<n<<" allowed="<<context->AllowsNativeRecordingReuse()<<" factoryOpaque="<<URUI::Factory::HasOpaqueRenderingInstancers()<<" revision="<<before<<"->"<<updated<<"->"<<context->GetNativeRenderRevision()<<std::endl;
    }
    {Frame frame;assert(frame.input.command_count==uint32_t(count));}
    std::array<double,5> times{};
    for(auto& time:times)
    {
        double elapsed=0;
        for(int n=0;n<16;n++)
        {
            // 每批仍执行Update；批量计时避免单次复用已短于时钟分辨率导致比例失真。
            urui_update();
            const auto start=std::chrono::steady_clock::now();
            for(int draw=0;draw<64;draw++)urui_render();
            elapsed+=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count();
        }
        time=elapsed/(16*64);
    }
    std::sort(times.begin(),times.end());urui_destroy();return times[2];
}

static int Image(UruiDocumentHandle owner,const char* uri,uint64_t* resource,uint64_t* source,int* width,int* height)
{
    *resource=(0x8000000000000000ULL|owner)+(std::strstr(uri,"two")?100:0);*source=owner;*width=*height=64;return 1;
}
static void Release(uint64_t){}

static void UniformMutations()
{
    assert(urui_create(128,128)==1);assert(urui_set_image_resolver(Image,Release)==1);
    auto owner=LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}#solid{width:8px;height:8px;background:red;}</style></head><body><div id='solid'></div></body></html>","/record-mutations.html");
    urui_update();
    {Frame first;assert(first.output.parameter_count&&first.output.parameters[0].tint[0]==1);}
    assert(urui_set_style(owner,"solid","background-color","blue")==1);urui_update();
    {Frame next;assert(next.output.parameters[0].tint[2]==1&&next.output.parameters[0].tint[0]==0);}
    assert(urui_set_style(owner,"solid","opacity","0.5")==1);urui_update();
    {Frame next;assert(next.input.layer_count||next.output.parameters[0].tint[3]<.75f);}
    assert(urui_document_load_html(owner,"<!doctype html><html><head><style>body{margin:0;}img{width:8px;height:8px;}</style></head><body><img id='image' src='one.png' data-urui-rect='0 0 8 8'></body></html>","/record-image.html")==1);
    urui_update();uint64_t texture=0;
    {Frame first;assert(first.input.command_count==1);texture=first.input.commands[0].texture;}
    assert(urui_set_style(owner,"image","--urui-graphic-tint","rgba(128,64,32,.5)")==1);urui_update();
    {Frame next;assert(next.output.parameters[0].tint[3]>.49f&&next.output.parameters[0].tint[3]<.51f);}
    assert(urui_set_attribute(owner,"image","data-urui-rect","8 0 8 8")==1);urui_update();
    {Frame next;assert(next.output.parameters[0].uv[2]==.125f);}
    assert(urui_set_attribute(owner,"image","src","two.png")==1);urui_update();
    {Frame next;assert(next.input.commands[0].texture!=texture);}
    urui_destroy();
}

static int opaque_renders=0;
class OpaqueElement final:public URUI::Element
{
public:
    explicit OpaqueElement(const URUI::String& tag):Element(tag){}
protected:
    void OnRender() override{opaque_renders++;}
};
static URUI::ElementInstancerGeneric<OpaqueElement> opaque_instancer;
static void DocumentStacking()
{
    assert(urui_create(128,128)==1);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div></div></body></html>","/order-red.html");
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:blue;}</style></head><body><div></div></body></html>","/order-blue.html");
    urui_update();
    for(int n=0;n<4;n++){Frame frame;assert(frame.input.command_count==2);}
    auto* context=URUI::GetContext("urui");auto* red=context->GetDocument(0);
    {Frame frame;assert(frame.output.parameters[0].tint[0]==1);}
    context->PullDocumentToFront(red);urui_update();
    {Frame frame;assert(frame.output.parameters[0].tint[2]==1&&frame.output.parameters[1].tint[0]==1);}
    context->PushDocumentToBack(red);urui_update();
    {Frame frame;assert(frame.output.parameters[0].tint[0]==1);}
    urui_destroy();
}
static void OpaqueCallbacks()
{
    assert(urui_create(128,128)==1);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div></div></body></html>","/opaque-recording.html");
    urui_update();urui_render();urui_render();
    URUI::Factory::RegisterElementInstancer("opaque-counter",&opaque_instancer);
    auto* document=URUI::GetContext("urui")->GetDocument(0);
    auto element=URUI::Factory::InstanceElement(document,"opaque-counter","opaque-counter",{});
    assert(element);element->SetProperty("width","8px");element->SetProperty("height","8px");
    document->AppendChild(std::move(element));urui_update();
    const auto before=opaque_renders;
    for(int n=0;n<5;n++)urui_render();
    assert(opaque_renders==before+5);
    urui_destroy();
}

static void SpatialChangesAndOwnedInputs()
{
    assert(urui_create(128,128)==1);
    auto owner=LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}#clip{width:16px;height:16px;overflow:hidden;}#wide{position:relative;left:12px;width:32px;height:8px;background:red;}</style></head><body><div id='clip'><div id='wide'></div></div></body></html>","/record-spatial.html");
    urui_update();for(int n=0;n<4;n++){Frame frame;}
    {
        Frame held;assert(held.input.command_count==1);
        const auto old_command=held.input.commands[0];const auto old_parameter=held.output.parameters[0];
        assert(old_parameter.matrix[12]==12&&old_parameter.clip[2]==16);
        URUI::GetContext("urui")->GetDocument(0)->GetElementById("clip")->SetScrollLeft(6);urui_update();
        {Frame next;assert(next.output.parameters[0].matrix[12]==6);}
        assert(urui_set_style(owner,"wide","transform","translateX(8px)")==1);urui_update();
        {Frame next;assert(next.output.parameters[0].matrix[12]==14);}
        assert(urui_set_style(owner,"clip","width","22px")==1);urui_update();
        {Frame next;assert(next.output.parameters[0].clip[2]==22);}
        assert(std::memcmp(&old_command,held.input.commands,sizeof(old_command))==0);
        assert(std::memcmp(&old_parameter,held.output.parameters,sizeof(old_parameter))==0);
    }
    assert(urui_remove_element(owner,"wide")==1);urui_update();
    {Frame next;assert(next.input.command_count==0);}
    urui_destroy();
}

static void ViewportAndTimer()
{
    assert(urui_create(128,128)==1);
    // 时钟在创建动画前注入；中途切换时基会保留宿主时钟上的原动画起点。
    assert(urui_set_ui_time(0)==1);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:50vw;height:8px;background:red;animation:fade 1s linear infinite;}@keyframes fade{from{background-color:red;}to{background-color:blue;}}</style></head><body><div></div></body></html>","/record-time.html");
    urui_update();for(int n=0;n<4;n++){Frame frame;}
    float red=0;
    {Frame first;red=first.output.parameters[0].tint[0];}
    assert(urui_set_ui_time(.5)==1);urui_update();
    {Frame next;assert(next.output.parameters[0].tint[0]<red&&next.output.parameters[0].tint[2]>0);}
    urui_resize(256,128);urui_update();
    {Frame next;UruiGeometryView geometry{};assert(urui_plan_geometry(next.output.lease,next.output.operations[0].geometry,&geometry)==1);float right=0;for(uint32_t n=0;n<geometry.vertex_count;n++)right=std::max(right,geometry.vertices[n].x);assert(right==128);}
    urui_destroy();
}

static int decorator_renders=0;
class OpaqueDecorator final:public URUI::Decorator
{
public:
    URUI::DecoratorDataHandle GenerateElementData(URUI::Element*,URUI::BoxArea)const override{return 1;}
    void ReleaseElementData(URUI::DecoratorDataHandle)const override{}
    void RenderElement(URUI::Element*,URUI::DecoratorDataHandle)const override{decorator_renders++;}
};
class OpaqueDecoratorInstancer final:public URUI::DecoratorInstancer
{
public:
    URUI::SharedPtr<URUI::Decorator> InstanceDecorator(const URUI::String&,const URUI::PropertyDictionary&,const URUI::DecoratorInstancerInterface&)override
    {return URUI::MakeShared<OpaqueDecorator>();}
};
static OpaqueDecoratorInstancer opaque_decorator;
static void OpaqueDecorators()
{
    assert(urui_create(128,128)==1);
    URUI::Factory::RegisterDecoratorInstancer("opaque-native",&opaque_decorator);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;--urui-decorator:opaque-native();}</style></head><body><div></div></body></html>","/opaque-decorator.html");
    urui_update();const auto before=decorator_renders;
    for(int n=0;n<5;n++)urui_render();
    assert(decorator_renders==before+5);
    urui_destroy();
}


static void LoadReviewFont()
{
    const auto path=(std::filesystem::path(__FILE__).parent_path()/"fixtures/fonts/LatoLatin-Regular.ttf").string();
    assert(URUI::LoadFontFace(path,"Recording",URUI::Style::FontStyle::Normal,URUI::Style::FontWeight::Normal));
}
static void ManualTextLines()
{
    assert(urui_create(128,128)==1);LoadReviewFont();
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;font:16px Recording;}</style></head><body><span id='text'>first</span></body></html>","/record-lines.html");
    urui_update();auto* text=urui_dynamic_cast<URUI::ElementText*>(URUI::GetContext("urui")->GetDocument(0)->GetElementById("text")->GetChild(0));assert(text);
    for(int n=0;n<4;n++){Frame frame;assert(frame.input.command_count==1);}
    text->SuppressAutoLayout();text->ClearLines();urui_update();
    {Frame frame;assert(frame.input.command_count==0);}
    text->AddLine({24,40},"second");urui_update();
    {Frame frame;assert(frame.input.command_count==1);}
    text->ClearLines();text->AddLine({48,40},"second");urui_update();
    {Frame frame;UruiGeometryView geometry{};assert(urui_plan_geometry(frame.output.lease,frame.output.operations[0].geometry,&geometry)==1);assert(geometry.vertices[0].x>=48);}
    urui_destroy();
}
class VersionedFont final:public URUI::FontEngineInterface
{
public:
    explicit VersionedFont(URUI::FontEngineInterface* inner):inner(inner){}
    URUI::FontEngineInterface* inner;int version=0,generated=0;
    URUI::FontFaceHandle GetFontFaceHandle(const URUI::String& family,URUI::Style::FontStyle style,URUI::Style::FontWeight weight,float size)override{return inner->GetFontFaceHandle(family,style,weight,size);}
    URUI::FontFaceHandle GetFontFaceHandleChain(URUI::Span<const URUI::FontFaceHandle> handles)override{return inner->GetFontFaceHandleChain(handles);}
    URUI::FontEffectsHandle PrepareFontEffects(URUI::FontFaceHandle handle,const URUI::FontEffectList& effects)override{return inner->PrepareFontEffects(handle,effects);}
    const URUI::FontMetrics& GetFontMetrics(URUI::FontFaceHandle handle)override{return inner->GetFontMetrics(handle);}
    float GetStringWidth(URUI::FontFaceHandle handle,URUI::StringView text,const URUI::TextShapingContext& shaping,URUI::Character prior)override{return inner->GetStringWidth(handle,text,shaping,prior);}
    float GenerateString(URUI::RenderManager& manager,URUI::FontFaceHandle face,URUI::FontEffectsHandle effects,URUI::StringView text,URUI::Vector2f position,URUI::ColourbPremultiplied color,float opacity,const URUI::TextShapingContext& shaping,URUI::TexturedMeshList& meshes)override
    {generated++;position.x+=version*8;return inner->GenerateString(manager,face,effects,text,position,color,opacity,shaping,meshes);}
    int GetVersion(URUI::FontFaceHandle handle)override{return inner->GetVersion(handle)+version;}
};
static void FontVersionContract()
{
    assert(urui_create(128,128)==1);LoadReviewFont();auto* original=URUI::GetFontEngineInterface();VersionedFont font(original);URUI::SetFontEngineInterface(&font);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;font:16px Recording;}</style></head><body>version</body></html>","/record-font.html");
    urui_update();for(int n=0;n<4;n++){Frame frame;assert(frame.input.command_count==1);}
    const int before=font.generated;assert(before>0);font.version++;urui_update();
    {Frame frame;assert(font.generated>before);UruiGeometryView geometry{};assert(urui_plan_geometry(frame.output.lease,frame.output.operations[0].geometry,&geometry)==1);assert(geometry.vertices[0].x>=8);}
    URUI::SetFontEngineInterface(original);urui_destroy();
}
static int hidden_updates=0,hidden_renders=0;
class HiddenOpaqueElement final:public URUI::Element
{
public:
    explicit HiddenOpaqueElement(const URUI::String& tag):Element(tag){}
protected:
    void OnUpdate() override {Element::OnUpdate();hidden_updates++;}
    void OnRender() override {hidden_renders++;}
};
static URUI::ElementInstancerGeneric<HiddenOpaqueElement> hidden_instancer;
static void HiddenOpaqueDocument()
{
    assert(urui_create(128,128)==1);
    URUI::Factory::RegisterElementInstancer("urui-opaque-hidden",&hidden_instancer);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div></div></body></html>","/visible-recording.html");
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}urui-opaque-hidden{display:block;width:8px;height:8px;background:blue;}</style></head><body><input type='checkbox'><urui-opaque-hidden id='probe'></urui-opaque-hidden></body></html>","/unpresented-recording.html");
    auto* context=URUI::GetContext("urui");auto* hidden=context->GetDocument(1);hidden->Hide();urui_update();
    assert(context->AllowsNativeRecordingReuse());
    const int updates=hidden_updates,renders=hidden_renders;
    for(int n=0;n<4;n++){urui_update();Frame frame;assert(frame.input.command_count==1);}
    assert(hidden_updates>updates&&hidden_renders==renders);
    {
        Frame held;const auto original=held.input.commands[0];
        hidden->Show();urui_update();assert(!context->AllowsNativeRecordingReuse());
        const int before=hidden_renders;
        for(int n=0;n<4;n++){Frame frame;}
        assert(hidden_renders==before+4);
        assert(std::memcmp(&original,held.input.commands,sizeof(original))==0);
    }
    // CSS visibility允许子节点覆盖，不能用它替代文档的IsPresented契约。
    assert(hidden->SetProperty("visibility","hidden"));
    assert(hidden->GetElementById("probe")->SetProperty("visibility","visible"));urui_update();
    assert(!context->AllowsNativeRecordingReuse());
    {const int before=hidden_renders;Frame frame;assert(hidden_renders==before+1);}
    hidden->Hide();urui_update();assert(context->AllowsNativeRecordingReuse());
    {const int before=hidden_renders;Frame frame;assert(frame.input.command_count==1&&hidden_renders==before);}
    urui_destroy();
}
static void RawContextRendering(bool same_context=true)
{
    assert(urui_create(128,128)==1);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div></div></body></html>","/record-raw.html");
    urui_update();for(int n=0;n<4;n++){Frame frame;assert(frame.input.command_count==1);}
    auto* context=URUI::GetContext("urui");if(same_context)context->Render();
    {Frame frame;assert(frame.input.command_count==1);}
    auto* other=URUI::CreateContext("other-recording",{128,128});assert(other);
    auto* document=other->LoadDocumentFromMemory("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:blue;}</style></head><body><div></div></body></html>","/record-other.html");assert(document);document->Show();other->Update();other->Render();
    for(int n=0;n<4;n++){Frame frame;assert(frame.input.command_count==1);}
    other->Render();
    {Frame frame;assert(frame.input.command_count==1&&frame.output.parameters[0].tint[0]==1);}
    assert(URUI::RemoveContext("other-recording"));urui_destroy();
}

int main(int argc,char** argv)
{
    if(argc==2)
    {
        const std::string name=argv[1];
        if(name=="review-text"){ManualTextLines();return 0;}
        if(name=="review-font"){FontVersionContract();return 0;}
        if(name=="review-raw"){RawContextRendering();return 0;}
        if(name=="review-cross-context"){RawContextRendering(false);return 0;}
        if(name=="review-hidden"){HiddenOpaqueDocument();return 0;}
    }
    if(argc==1)
    {
        const auto small=StableRender(500),large=StableRender(5000);
        std::cout<<"stable native Render us: 500="<<small<<" 5000="<<large<<" ratio="<<large/small<<std::endl;
        assert(large/small<2);
    }
    UniformMutations();DocumentStacking();SpatialChangesAndOwnedInputs();ViewportAndTimer();OpaqueCallbacks();OpaqueDecorators();ManualTextLines();FontVersionContract();RawContextRendering();HiddenOpaqueDocument();
    std::cout<<"native recording mutation and opaque callback contracts passed\n";
}
