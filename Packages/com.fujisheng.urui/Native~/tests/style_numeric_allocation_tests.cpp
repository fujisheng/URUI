// Compile the snapshot implementation in this executable so the independent
// operator-new probe covers its helpers, rather than trusting planner counters.
#include "../style_properties.cpp"
#include <cassert>
#include <cstdlib>
#include <iostream>
static bool watch_heap=false;
static uint64_t allocations=0,frees=0;
void* operator new(std::size_t bytes){void* value=std::malloc(bytes?bytes:1);if(!value)throw std::bad_alloc();if(watch_heap)allocations++;return value;}
void operator delete(void* value)noexcept{if(value&&watch_heap)frees++;std::free(value);}
void operator delete(void* value,std::size_t)noexcept{::operator delete(value);}
void* operator new[](std::size_t bytes){return ::operator new(bytes);}
void operator delete[](void* value)noexcept{::operator delete(value);}
void operator delete[](void* value,std::size_t)noexcept{::operator delete(value);}
static URUI::Element* element;
static URUI::Element* document_root;
static URUI::Element* Resolve(uint64_t owner,uint64_t token){return owner!=1?nullptr:token==1?element:token==2?document_root:nullptr;}
int main()
{
    assert(urui_create(128,128)==1);auto* context=URUI::GetContext("urui");
    const char* property="--generic-animated-number-longer-than-small-string";
    auto* document=context->LoadDocumentFromMemory("<!doctype html><html><head><style>@property --generic-animated-number-longer-than-small-string{syntax:'<number>';inherits:false;initial-value:0;}@property --program-color{syntax:'<color>';inherits:false;initial-value:#804020;}@property --derived-color{syntax:'<color>';inherits:false;initial-value:#804020;}@property --program-mode{syntax:'low | high';inherits:false;initial-value:low;}#value{width:16px;height:8px;--program-mode:high;}</style></head><body><div id='value'></div></body></html>","/numeric-allocations.html");
    assert(document);document_root=document;document->Show();context->Update();element=document->GetElementById("value");assert(element);
    urui_styles::Begin(Resolve);
    auto* snapshot=urui_styles::Get(1,1);assert(snapshot&&!snapshot->has_unregistered_custom);
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty(property,i%2?".25":".75"));context->Update();
        const auto revision=snapshot->revision;
        watch_heap=i>=300;snapshot=urui_styles::Get(1,1);watch_heap=false;
        assert(snapshot->revision!=revision);
        auto found=std::find_if(snapshot->values.begin(),snapshot->values.end(),[&](const auto& value){return value.name==property;});
        assert(found!=snapshot->values.end()&&found->x==(i%2?.25f:.75f));
    }
    std::cout<<"numeric snapshot allocation/free="<<allocations<<"/"<<frees<<std::endl;
    assert(allocations==0&&frees==0);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty("--program-color",i%2?"#804020":"#204080"));context->Update();
        watch_heap=i>=300;snapshot=urui_styles::Get(1,1);watch_heap=false;
        auto found=std::find_if(snapshot->values.begin(),snapshot->values.end(),[](const auto& value){return value.name=="--program-color";});
        assert(found!=snapshot->values.end()&&found->x==(i%2?128.f:32.f)/255.f);
    }
    std::cout<<"color snapshot allocation/free="<<allocations<<"/"<<frees<<std::endl;
    assert(allocations==0&&frees==0);
    // Prepared effects retain the computed version even when a live style read
    // occurs after a DOM change. Replacing a lease invalidates its prior handle.
    UruiFrozenStyleHandle frozen{};
    assert(urui_style_snapshot_freeze(1,1,nullptr,&frozen)==1);
    auto old=frozen;
    assert(element->SetProperty(property,".5"));context->Update();
    snapshot=urui_styles::Get(1,1);
    uint64_t revision=0;int count=0,text_bytes=0;
    assert(urui_style_snapshot_copy(1,&frozen,0,nullptr,0,nullptr,0,&revision,&count,&text_bytes)==-3);
    std::vector<UruiStyleValue> values(count);std::vector<char> names(text_bytes);
    auto frozen_number=[&]()
    {
        assert(urui_style_snapshot_copy(1,&frozen,0,values.data(),int(values.size()),names.data(),int(names.size()),&revision,&count,&text_bytes)==1);
        for(const auto& value:values)if(std::strcmp(names.data()+value.name_offset,property)==0)return value.x;
        std::abort();
    };
    assert(frozen_number()==.25f); // last prepared value before .5 mutation
    assert(urui_style_snapshot_freeze(1,1,&frozen,&frozen)==1&&frozen_number()==.5f);
    assert(urui_style_snapshot_copy(1,&old,0,nullptr,0,nullptr,0,&revision,&count,&text_bytes)==-2);
    assert(urui_style_snapshot_release(2,&frozen)==-2);
    int wrong_thread=0;std::thread other([&]{wrong_thread=urui_style_snapshot_release(1,&frozen);});other.join();
    assert(wrong_thread==-3&&frozen_number()==.5f);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty(property,i%2?".25":".75"));context->Update();
        watch_heap=i>=300;
        (void)urui_styles::Get(1,1);
        assert(urui_style_snapshot_freeze(1,1,&frozen,&frozen)==1);
        assert(frozen_number()==(i%2?.25f:.75f));watch_heap=false;
    }
    assert(allocations==0&&frees==0);
    auto frozen_color=[&]()
    {
        assert(urui_style_snapshot_copy(1,&frozen,0,values.data(),int(values.size()),names.data(),int(names.size()),&revision,&count,&text_bytes)==1);
        for(const auto& value:values)if(std::strcmp(names.data()+value.name_offset,"--program-color")==0)return value.x;
        std::abort();
    };
    const auto before_color=frozen_color();
    assert(element->SetProperty("--program-color","#204080"));context->Update();snapshot=urui_styles::Get(1,1);
    assert(frozen_color()==before_color); // live color deltas cannot mutate a retained version
    assert(urui_style_snapshot_freeze(1,1,&frozen,&frozen)==1&&frozen_color()==32.f/255);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty("--program-color",i%2?"#804020":"#204080"));context->Update();watch_heap=i>=300;
        (void)urui_styles::Get(1,1);assert(urui_style_snapshot_freeze(1,1,&frozen,&frozen)==1);
        assert(frozen_color()==(i%2?128.f:32.f)/255);watch_heap=false;
    }
    assert(allocations==0&&frees==0);
    assert(urui_style_snapshot_release(1,&frozen)==1&&urui_style_snapshot_release(1,&frozen)==-2);
    assert(element->SetProperty("--derived-color","var(--program-color)"));context->Update();
    assert(element->SetProperty("--program-color","#102030"));context->Update();snapshot=urui_styles::Get(1,1);
    auto dependent=std::find_if(snapshot->values.begin(),snapshot->values.end(),[](const auto& value){return value.name=="--derived-color";});
    assert(dependent!=snapshot->values.end()&&dependent->x==16.f/255&&dependent->y==32.f/255&&dependent->z==48.f/255);
    assert(element->SetProperty("--program-color","#804020"));context->Update();(void)urui_styles::Get(1,1);
    // Fixed GPU field layout resolves property names/keywords once, then reads
    // only native IDs and writes a contiguous float4 table.
    assert(element->SetProperty("--unregistered-program-keyword-with-a-long-name","author-keyword-longer-than-the-native-small-string-storage"));
    assert(element->SetProperty("--raw-length","4px"));context->Update();
    assert(element->SetProperty("--raw-resource-key","a"));context->Update();
    snapshot=urui_styles::Get(1,1);assert(snapshot->has_unregistered_custom);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty(property,i%2?".25":".75"));context->Update();watch_heap=i>=300;
        snapshot=urui_styles::Get(1,1);watch_heap=false;
    }
    std::cout<<"registered delta with constant raw fields allocation/free="<<allocations<<"/"<<frees<<std::endl;
    assert(allocations==0&&frees==0);
    // The delta is valid only if all variable dependencies are covered. Compare
    // with an independent forced full read after an unregistered var() dependency.
    assert(element->SetProperty("--raw-dependent","var(--generic-animated-number-longer-than-small-string)"));context->Update();(void)urui_styles::Get(1,1);
    for(int i=0;i<8;i++)
    {
        assert(element->SetProperty(property,i%2?".25":".75"));context->Update();
        auto* candidate=urui_styles::Get(1,1);const auto actual=candidate->values;
        candidate->stamp=UINT64_MAX;const auto* full=urui_styles::Get(1,1);assert(actual==full->values);
    }
    const UruiProgramField optional_field{"--optional-feather-length",0,258,-1,0,nullptr};uint64_t optional_program=0;
    assert(urui_program_layout_register(1,2,"optional-zero-length",&optional_field,1,1,&optional_program)==1);
    float optional_values[4]{9,9,9,9};assert(urui_program_layout_values(1,1,optional_program,optional_values,1)==1&&optional_values[0]==0);
    assert(element->SetProperty("--optional-feather-length","red"));context->Update();
    assert(urui_program_layout_values(1,1,optional_program,optional_values,1)==-1&&optional_values[0]==0);
    assert(element->SetProperty("--optional-feather-length","12px"));context->Update();
    assert(urui_program_layout_values(1,1,optional_program,optional_values,1)==1&&optional_values[0]==12);
    element->RemoveProperty("--optional-feather-length");context->Update();
    assert(urui_program_layout_values(1,1,optional_program,optional_values,1)==1&&optional_values[0]==0);
    element->GetStyle()->SetCustomProperty("--optional-feather-length",URUI::Property(13.f,URUI::Unit::PX));context->Update();
    assert(urui_program_layout_values(1,1,optional_program,optional_values,1)==1&&optional_values[0]==13);
    assert(urui_program_layout_release(1,optional_program)==1);
    element->GetStyle()->SetCustomProperty("--typed-program-number",URUI::Property(.25f,URUI::Unit::NUMBER));context->Update();
    const UruiProgramField typed_field{"--typed-program-number",0,1,-1,0,nullptr};uint64_t typed_program=0;
    assert(urui_program_layout_register(1,2,"typed-raw-number",&typed_field,1,1,&typed_program)==1);
    assert(urui_program_layout_values(1,1,typed_program,optional_values,1)==1&&optional_values[0]==.25f);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        element->GetStyle()->SetCustomProperty("--typed-program-number",URUI::Property(i%2?.25f:.75f,URUI::Unit::NUMBER));context->Update();watch_heap=i>=300;
        assert(urui_program_layout_values(1,1,typed_program,optional_values,1)==1);watch_heap=false;
    }
    assert(allocations==0&&frees==0);assert(urui_program_layout_release(1,typed_program)==1);
    // Missing opaque inputs cannot alias the first real resource. Relative
    // image identities also include the stylesheet that supplied the URL.
    const UruiProgramField identities[]={
        {"--optional-image-resource",0,16|256,-1,0,nullptr},
        {"--optional-key-resource",1,8|256,-1,0,nullptr}};
    uint64_t identities_program=0;
    assert(urui_program_layout_register(1,2,"optional-resource-identities",identities,2,1,&identities_program)==1);
    float identity_values[4]{};
    assert(urui_program_layout_values(1,1,identities_program,identity_values,1)==1&&identity_values[0]==0&&identity_values[1]==0);
    URUI::Property image_property{URUI::String("url('relative.png')"),URUI::Unit::STRING};
    image_property.source=URUI::MakeShared<URUI::PropertySource>("/first/source.css",1,"#value");
    element->GetStyle()->SetCustomProperty("--optional-image-resource",image_property);
    assert(element->SetProperty("--optional-key-resource","present"));context->Update();
    assert(urui_program_layout_values(1,1,identities_program,identity_values,1)==1&&identity_values[0]!=0&&identity_values[1]!=0);
    const auto first_source=identity_values[0];
    image_property.source=URUI::MakeShared<URUI::PropertySource>("/second/source.css",1,"#value");
    element->GetStyle()->SetCustomProperty("--optional-image-resource",image_property);context->Update();
    assert(urui_program_layout_values(1,1,identities_program,identity_values,1)==1&&identity_values[0]!=first_source);
    element->RemoveProperty("--optional-image-resource");element->RemoveProperty("--optional-key-resource");context->Update();
    assert(urui_program_layout_values(1,1,identities_program,identity_values,1)==1&&identity_values[0]==0&&identity_values[1]==0);
    assert(urui_program_layout_release(1,identities_program)==1);
    // Number-or-keyword fields preserve the distinction without a NaN sentinel.
    const UruiProgramKeyword automatic[]={{"auto",7,0}};
    const UruiProgramField mixed_fields[]={
        {"--numeric-or-auto",0,32,-1,1,automatic},
        {"--image-resource",2,16,3,0,nullptr}};
    uint64_t mixed_program=0;
    assert(urui_program_layout_register(1,2,"numeric-and-image-fields",mixed_fields,2,1,&mixed_program)==1);
    assert(element->SetProperty("--numeric-or-auto","auto"));
    assert(element->SetProperty("--image-resource","url('first.png')"));context->Update();
    float mixed_values[4]{};
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[0]==7&&mixed_values[1]==1);
    const float first_image=mixed_values[2];assert(mixed_values[3]>=2);
    allocations=frees=0;
    for(int i=0;i<332;i++) {
        element->GetStyle()->SetCustomProperty("--numeric-or-auto",URUI::Property(i%2?.25f:.75f,URUI::Unit::NUMBER));context->Update();watch_heap=i>=300;
        assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1);watch_heap=false;
        assert(mixed_values[0]==(i%2?.25f:.75f)&&mixed_values[1]==0&&mixed_values[2]==first_image);
    }
    assert(allocations==0&&frees==0);
    assert(element->SetProperty("--image-resource","url('second.png')"));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]!=first_image);
    assert(element->SetProperty("--image-resource","url('first.png')"));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]==first_image);
    assert(element->SetProperty("--image-resource","none"));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]!=first_image);
    assert(element->SetProperty("--image-resource","url('first.png')"));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]==first_image);
    URUI::Property typed_image;
    assert(URUI::StyleSheetSpecification::GetParser("css_image")->ParseValue(typed_image,"url('first.png')",{{"effect",1}}));
    element->GetStyle()->SetCustomProperty("--image-resource",typed_image);context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]==first_image);
    element->GetStyle()->SetCustomProperty("--image-resource",URUI::Property(URUI::DecoratorsPtr{},URUI::Unit::DECORATOR));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]==0);
    element->GetStyle()->SetCustomProperty("--image-resource",typed_image);context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==1&&mixed_values[2]==first_image);
    assert(element->SetProperty("--numeric-or-auto","bad-keyword"));context->Update();
    assert(urui_program_layout_values(1,1,mixed_program,mixed_values,1)==-1);
    assert(urui_program_layout_release(1,mixed_program)==1);
    const UruiProgramKeyword modes[]={{"low",3,0},{"high",7,0}};
    const UruiProgramKeyword raw_modes[]={{"author-keyword-longer-than-the-native-small-string-storage",11,0}};
    const UruiProgramField fields[]={
        {property,0,1,3,0,nullptr},{"width",1,2,-1,0,nullptr},
        {"--program-color",4,4,-1,0,nullptr},{"--program-mode",2,8,8,2,modes},
        {"--unregistered-program-keyword-with-a-long-name",9,8,-1,1,raw_modes},{"--raw-length",10,2,-1,0,nullptr},
        {"--raw-resource-key",12,8,-1,0,nullptr},{"--program-mode",13,8,-1,0,nullptr}};
    uint64_t program=0;
    assert(urui_program_layout_register(1,2,"generic-fields",fields,8,4,&program)==1&&program);
    std::array<float,16> rows{};
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==1);
    assert(rows[0]==.25f&&rows[1]==16&&rows[2]==7&&rows[3]==2&&rows[8]==2);
    assert(std::abs(rows[4]-128.f/255)<.00001f&&rows[7]==1&&rows[9]==11&&rows[10]==4&&rows[11]==0);
    const auto registered_key=rows[13],raw_key=rows[12];assert(raw_key!=0);
    assert(element->SetProperty("--raw-resource-key","b"));context->Update();
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==1&&rows[12]!=raw_key&&rows[13]==registered_key);
    assert(element->SetProperty("--raw-resource-key","a"));context->Update();
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==1&&rows[12]==raw_key);
    uint64_t duplicate=0;
    assert(urui_program_layout_register(1,2,"generic-fields",fields,8,4,&duplicate)==1&&duplicate==program);
    UruiProgramField invalid[2]={fields[0],fields[1]};invalid[1].offset=0;
    assert(urui_program_layout_register(1,2,"bad-overlap",invalid,2,3,&duplicate)==-1&&duplicate==0);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        assert(element->SetProperty(property,i%2?".25":".75"));context->Update();
        watch_heap=i>=300;
        assert(urui_program_layout_values(1,1,program,rows.data(),4)==1);watch_heap=false;
        assert(rows[0]==(i%2?.25f:.75f));
    }
    assert(allocations==0&&frees==0);
    document_root=nullptr;
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==-2);
    document_root=document;
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==1);
    assert(urui_program_layout_release(2,program)==-2&&urui_program_layout_release(1,program)==1);
    assert(urui_program_layout_values(1,1,program,rows.data(),4)==-2);
    urui_styles::End(); document->Close(); context->Update();
    document = context->LoadDocumentFromMemory("<!doctype html><html><head></head><body><div id='opacity'></div></body></html>", "/opacity-snapshot.html");
    assert(document); document->Show(); context->Update(); element=document->GetElementById("opacity"); document_root=document;
    urui_styles::Begin(Resolve); (void)urui_styles::Get(1,1);
    allocations=frees=0;
    for(int i=0;i<332;i++)
    {
        const char* input[]={"1.5","50%","-.5",".25"};const float expected[]={1.f,.5f,0.f,.25f};
        assert(element->SetProperty("opacity",input[i%4]));context->Update();
        watch_heap=i>=300;const auto* current=urui_styles::Get(1,1);watch_heap=false;
        const auto value=std::find_if(current->values.begin(),current->values.end(),[](const urui_styles::Value& entry){return entry.name=="opacity";});
        assert(value!=current->values.end()&&value->x==expected[i%4]);
    }
    std::cout<<"opacity snapshot allocations="<<allocations<<" frees="<<frees<<std::endl;
    assert(allocations==0&&frees==0);
    urui_styles::End();urui_destroy();
}
