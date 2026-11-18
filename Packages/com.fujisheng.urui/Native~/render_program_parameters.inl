// Private implementation of registered CSS IDs -> fixed float4 data. Included
// after the style snapshot implementation; no Unity or Effect algorithms here.
namespace urui_styles {
struct ProgramKeyword {URUI::String text;float value=0;int native=0;};
struct ProgramResource {URUI::String text,source;};
struct ProgramField {
    URUI::String name;
    URUI::PropertyId property=URUI::PropertyId::Invalid;
    uint32_t offset=0,kind=0;
    int32_t origin=-1;
    std::vector<ProgramKeyword> keywords;
    std::vector<URUI::String> resource_keywords;
    std::vector<ProgramResource> images;
    URUI::Property last_image;URUI::String last_image_source;bool image_cached=false;
    uint32_t resource_id=0;
    bool raw=false,cached=false,optional=false;
    URUI::String cached_text;
    URUI::Property cached_property;
    uint32_t cached_kind=0;
};
struct ProgramLayout {
    uint64_t owner=0,root=0,schema_revision=0;
    URUI::String name;
    uint32_t rows=0;
    const URUI::PropertySpecification* specification=nullptr;
    std::vector<ProgramField> fields;
};
static_assert(sizeof(UruiProgramField)==(sizeof(void*)==8?32:24),"program field ABI");
static_assert(sizeof(UruiProgramKeyword)==(sizeof(void*)==8?16:12),"program keyword ABI");
static std::unordered_map<uint64_t,ProgramLayout> program_layouts;
static uint64_t next_program_layout=0;
static void ClearPrograms(){program_layouts.clear();}
static int ProgramThread() {
    if(!resolve)return -2;
    return owner_thread==std::this_thread::get_id()?1:-3;
}
static bool ProgramEqual(const ProgramLayout& layout,const UruiProgramField* fields,uint32_t count,uint32_t rows) {
    if(layout.rows!=rows||layout.fields.size()!=count)return false;
    for(uint32_t n=0;n<count;n++) {
        const auto& a=layout.fields[n];const auto& b=fields[n];
        if(!b.property||a.name!=b.property||a.offset!=b.offset||(a.kind|(a.optional?256u:0u))!=b.kind||a.origin!=b.origin_offset||a.keywords.size()!=b.keyword_count||(!b.keywords&&b.keyword_count))return false;
        for(uint32_t k=0;k<b.keyword_count;k++)if(!b.keywords[k].text||a.keywords[k].text!=b.keywords[k].text||a.keywords[k].value!=b.keywords[k].value||b.keywords[k].reserved)return false;
    }
    return true;
}
static bool BindProgram(ProgramLayout& layout,URUI::Element* element) {
    const auto& specification=element->GetPropertySpecification();
    if(layout.specification==&specification&&layout.schema_revision==specification.GetRegisteredPropertyRevision())return true;
    layout.specification=nullptr;
    for(auto& field:layout.fields) {
        field.property=specification.GetPropertyId(field.name);
        const auto* definition=specification.GetProperty(field.property);
        field.raw=!definition;field.cached=false;
        if(!definition) {
            if(field.name.rfind("--",0)!=0)return false;
            for(size_t a=0;a<field.keywords.size();a++)for(size_t b=0;b<a;b++)
                if(field.keywords[a].text==field.keywords[b].text&&field.keywords[a].value!=field.keywords[b].value)return false;
            continue;
        }
        for(auto& keyword:field.keywords) {
            URUI::Property parsed;
            if(!definition->ParseValue(parsed,keyword.text)||parsed.unit!=URUI::Unit::KEYWORD)return false;
            keyword.native=parsed.Get<int>();
        }
        for(size_t a=0;a<field.keywords.size();a++)for(size_t b=0;b<a;b++)
            if(field.keywords[a].native==field.keywords[b].native&&field.keywords[a].value!=field.keywords[b].value)return false;
    }
    layout.specification=&specification;layout.schema_revision=specification.GetRegisteredPropertyRevision();return true;
}
static int ProgramOrigin(URUI::Element* element,URUI::PropertyId id) {
    const auto* definition=element->GetPropertySpecification().GetProperty(id);
    if(!definition)return 0;
    for(auto* current=element;current;current=definition->IsInherited()?current->GetParentNode():nullptr)
        if(const auto* property=current->GetLocalProperty(id)) {
            switch(property->priority.originImportance) {
                case URUI::CssOriginImportance::UserAgentNormal:return 1;
                case URUI::CssOriginImportance::AuthorNormal:return 2;
                case URUI::CssOriginImportance::Animation:return 3;
                case URUI::CssOriginImportance::AuthorImportant:
                case URUI::CssOriginImportance::UserAgentImportant:return 4;
                case URUI::CssOriginImportance::Transition:return 5;
            }
        }
    return 0;
}
static void InternProgramKeyword(ProgramField& field,const URUI::String& text) {
    auto found=std::find(field.resource_keywords.begin(),field.resource_keywords.end(),text);
    if(found==field.resource_keywords.end()) {
        if(field.resource_keywords.size()==65536)throw std::bad_alloc();
        field.resource_keywords.push_back(text);field.resource_id=uint32_t(field.resource_keywords.size());
    } else field.resource_id=uint32_t(found-field.resource_keywords.begin())+1;
}
static bool ReadProgramImage(URUI::Element* element,ProgramField& field,const URUI::Property& property,float* out) {
    if(property.unit!=URUI::Unit::DECORATOR)return false;
    const auto declarations=property.Get<URUI::DecoratorsPtr>();
    if(declarations&&declarations->layer_count!=1)return false;
    if(!declarations||declarations->list.empty()){field.image_cached=false;field.resource_id=0;out[0]=0;return true;}
    const auto& type=declarations->list.front().type;
    if(type!="urui-css-image"&&type.find("gradient")==URUI::String::npos)return false;
    static const URUI::String empty;
    const auto* document=element->GetOwnerDocument();
    const auto& source=property.source?property.source->path:document?document->GetSourceURL():empty;
    if(field.image_cached&&field.last_image==property&&field.last_image_source==source){out[0]=float(field.resource_id);return true;}
    const auto text=property.ToString();
    size_t index=0;
    for(;index<field.images.size();index++)if(field.images[index].text==text&&field.images[index].source==source)break;
    if(index==field.images.size()) {
        if(index==65536)throw std::bad_alloc();
        field.images.push_back({text,source});
    }
    field.resource_id=uint32_t(index)+1;field.last_image=property;field.last_image_source=source;field.image_cached=true;
    out[0]=float(field.resource_id);return true;
}
static bool ReadRawProgramField(URUI::Element* element,ProgramField& field,const URUI::Property& property,float* out) {
    if(property.unit!=URUI::Unit::STRING)return false;
    const auto& text=property.value.GetReference<URUI::String>();
    if(!field.cached||field.cached_text!=text) {
        URUI::Property parsed;
        auto* number=URUI::StyleSheetSpecification::GetParser("number");
        auto* length=URUI::StyleSheetSpecification::GetParser("length");
        auto* color=URUI::StyleSheetSpecification::GetParser("color");
        uint32_t kind=8;
        if(number&&number->ParseValue(parsed,text,{}))kind=parsed.Get<float>()==0?3:1;
        else if(length&&length->ParseValue(parsed,text,{})&&(parsed.unit==URUI::Unit::PX||parsed.unit==URUI::Unit::DP))kind=2;
        else if(color&&color->ParseValue(parsed,text,{}))kind=4;
        else {
            auto* image=URUI::StyleSheetSpecification::GetParser("css_image");
            if((text!="none"||field.kind==16)&&image&&image->ParseValue(parsed,text,{{"effect",1}}))kind=16;
        }
        if(kind==8&&field.kind==8&&field.keywords.empty())InternProgramKeyword(field,text);
        field.cached_kind=kind;field.cached_property=std::move(parsed);field.cached_text=text;field.cached=true;
    }
    if(field.kind==16) {
        // The parsed image belongs to the declaration's stylesheet, including
        // when only the source URL changed while its relative text stayed equal.
        field.cached_property.source=property.source;
        return ReadProgramImage(element,field,field.cached_property,out);
    }
    if(field.kind==32) {
        if(field.cached_kind&1){out[0]=field.cached_property.Get<float>();out[1]=0;return std::isfinite(out[0]);}
        if(field.cached_kind!=8)return false;
        for(const auto& keyword:field.keywords)if(keyword.text==text){out[0]=keyword.value;out[1]=1;return true;}
        return false;
    }
    if(!(field.cached_kind&field.kind))return false;
    if(field.kind==1)out[0]=field.cached_property.Get<float>();
    else if(field.kind==2)out[0]=field.cached_property.unit==URUI::Unit::NUMBER?0:element->ResolveLength(field.cached_property.GetNumericValue());
    else if(field.kind==4) {
        const auto color=field.cached_property.Get<URUI::Colourb>();
        out[0]=color.red/255.f;out[1]=color.green/255.f;out[2]=color.blue/255.f;out[3]=color.alpha/255.f;
    } else if(field.keywords.empty())out[0]=float(field.resource_id);
    else {
        bool found=false;
        for(const auto& keyword:field.keywords)if(keyword.text==text){out[0]=keyword.value;found=true;break;}
        if(!found)return false;
    }
    for(uint32_t n=0;n<(field.kind==4?4u:1u);n++)if(!std::isfinite(out[n]))return false;
    return true;
}
static bool ReadProgramField(URUI::Element* element,ProgramField& field,float* out) {
    const auto* property=field.raw?element->GetProperty(field.name):element->GetProperty(field.property);if(!property)return field.optional;
    if(field.raw&&property->unit==URUI::Unit::STRING)return ReadRawProgramField(element,field,*property,out);
    if(field.kind==16)return ReadProgramImage(element,field,*property,out);
    if(field.kind==32) {
        if(property->unit==URUI::Unit::NUMBER){out[0]=property->Get<float>();out[1]=0;return std::isfinite(out[0]);}
        if(property->unit!=URUI::Unit::KEYWORD||field.raw)return false;
        for(const auto& keyword:field.keywords)if(keyword.native==property->Get<int>()){out[0]=keyword.value;out[1]=1;return true;}
        return false;
    }
    // 未注册自定义属性也可以由数值 API 写入已解析的 NUMBER/PX/COLOUR。
    // 没有注册枚举定义时不能猜测 KEYWORD 的整数含义。
    if(field.raw&&field.kind==8)return false;
    if(field.kind==1) {
        if(field.property==URUI::PropertyId::Opacity)out[0]=element->GetComputedValues().opacity();
        else {if(property->unit!=URUI::Unit::NUMBER)return false;out[0]=property->Get<float>();}
    } else if(field.kind==2) {
        if(field.property==URUI::PropertyId::Width||field.property==URUI::PropertyId::Height) {
            const auto size=element->GetBox().GetSize(URUI::BoxArea::Border);out[0]=field.property==URUI::PropertyId::Width?size.x:size.y;
        } else if(field.property==URUI::PropertyId::FontSize)out[0]=element->GetComputedValues().font_size();
        else if(field.property==URUI::PropertyId::LineHeight)out[0]=element->GetComputedValues().line_height().value;
        else if(URUI::Any(property->unit&URUI::Unit::LENGTH))out[0]=element->ResolveLength(property->GetNumericValue());
        else if(property->unit==URUI::Unit::NUMBER&&property->Get<float>()==0)out[0]=0;
        else if(property->unit==URUI::Unit::PERCENT&&property->definition&&property->definition->GetRelativeTarget()!=URUI::RelativeTarget::None)
            out[0]=element->ResolveNumericValue(property->GetNumericValue(),Base(element,property->definition));
        else return false;
    } else if(field.kind==4) {
        if(property->unit!=URUI::Unit::COLOUR)return false;
        const auto color=property->Get<URUI::Colourb>();out[0]=color.red/255.f;out[1]=color.green/255.f;out[2]=color.blue/255.f;out[3]=color.alpha/255.f;
    } else {
        if(property->unit!=URUI::Unit::KEYWORD)return false;
        const auto native=property->Get<int>();
        if(field.keywords.empty()) {
            if(!field.cached||field.cached_property!=*property) {
                InternProgramKeyword(field,property->ToString());field.cached_property=*property;field.cached=true;
            }
            out[0]=float(field.resource_id);
        }
        else {
            bool found=false;
            for(const auto& keyword:field.keywords)if(keyword.native==native){out[0]=keyword.value;found=true;break;}
            if(!found)return false;
        }
    }
    for(uint32_t n=0;n<(field.kind==4?4u:1u);n++)if(!std::isfinite(out[n]))return false;
    return true;
}
// The renderer can call this directly into its owned input/parameter buffers.
static bool FillProgram(ProgramLayout& layout,URUI::Element* element,float* values) {
    std::fill_n(values,size_t(layout.rows)*4,0.f);
    if(!BindProgram(layout,element))return false;
    for(auto& field:layout.fields) {
        if(!ReadProgramField(element,field,values+field.offset)){std::fill_n(values,size_t(layout.rows)*4,0.f);return false;}
        if(field.origin>=0)values[field.origin]=float(field.raw?Origin(element,field.name):ProgramOrigin(element,field.property));
    }
    return true;
}
}
extern "C" {
int urui_program_parameter_api_version(){return 1;}
int urui_program_layout_register(UruiDocumentHandle owner,uint64_t exemplar,const char* name,const UruiProgramField* fields,uint32_t count,uint32_t rows,uint64_t* output) try {
    if(!output)return -1;*output=0;
    const auto thread=urui_styles::ProgramThread();if(thread!=1)return thread;
    if(!name||!*name||!rows||rows>512||count>2048||(!fields&&count))return -1;
    auto* element=urui_styles::resolve(owner,exemplar);if(!element)return -2;
    auto* document=element->GetOwnerDocument();
    if(!document||(element!=document&&element->GetParentNode()!=document))return -1;
    for(auto& item:urui_styles::program_layouts)if(item.second.owner==owner&&item.second.name==name) {
        if(!urui_styles::ProgramEqual(item.second,fields,count,rows))return -1;
        item.second.root=exemplar;item.second.specification=nullptr;
        if(!urui_styles::BindProgram(item.second,element))return -1;
        *output=item.first;return 1;
    }
    std::array<uint8_t,2048> used{};urui_styles::ProgramLayout layout;layout.owner=owner;layout.root=exemplar;layout.name=name;layout.rows=rows;layout.fields.reserve(count);
    for(uint32_t n=0;n<count;n++) {
        const auto& source=fields[n];const auto kind=source.kind&~256u;
        if(!source.property||!*source.property||(kind!=1&&kind!=2&&kind!=4&&kind!=8&&kind!=16&&kind!=32)||source.keyword_count>128||(!source.keywords&&source.keyword_count)||(source.keyword_count&&kind!=8&&kind!=32))return -1;
        const auto width=kind==4?4u:kind==32?2u:1u;
        if(source.offset>rows*4||width>rows*4-source.offset||source.origin_offset< -1||source.origin_offset>=int32_t(rows*4))return -1;
        for(uint32_t k=0;k<width;k++)if(used[source.offset+k]++)return -1;
        if(source.origin_offset>=0&&used[uint32_t(source.origin_offset)]++)return -1;
        urui_styles::ProgramField field;field.name=source.property;field.offset=source.offset;field.kind=kind;field.optional=(source.kind&256)!=0;field.origin=source.origin_offset;field.keywords.reserve(source.keyword_count);
        for(uint32_t k=0;k<source.keyword_count;k++) {
            const auto& keyword=source.keywords[k];if(!keyword.text||!std::isfinite(keyword.value)||keyword.reserved)return -1;
            field.keywords.push_back({keyword.text,keyword.value,0});
        }
        layout.fields.push_back(std::move(field));
    }
    if(!urui_styles::BindProgram(layout,element))return -1;
    if(urui_styles::next_program_layout==UINT64_MAX)return -8;
    const auto identity=++urui_styles::next_program_layout;
    urui_styles::program_layouts.emplace(identity,std::move(layout));*output=identity;return 1;
} catch(const std::bad_alloc&){return -4;}catch(...){return -8;}
int urui_program_layout_values(UruiDocumentHandle owner,uint64_t token,uint64_t program,float* values,uint32_t rows) try {
    const auto thread=urui_styles::ProgramThread();if(thread!=1)return thread;
    auto found=urui_styles::program_layouts.find(program);if(found==urui_styles::program_layouts.end()||found->second.owner!=owner)return -2;
    if(!values||rows<found->second.rows)return -3;
    auto* element=urui_styles::resolve(owner,token);if(!element)return -2;
    auto* root=urui_styles::resolve(owner,found->second.root);
    if(!root||root->GetOwnerDocument()!=element->GetOwnerDocument())return -2;
    return urui_styles::FillProgram(found->second,element,values)?1:-1;
} catch(const std::bad_alloc&){return -4;}catch(...){return -8;}
int urui_program_layout_release(UruiDocumentHandle owner,uint64_t program) try {
    const auto thread=urui_styles::ProgramThread();if(thread!=1)return thread;
    auto found=urui_styles::program_layouts.find(program);if(found==urui_styles::program_layouts.end()||found->second.owner!=owner)return -2;
    urui_styles::program_layouts.erase(found);return 1;
} catch(...){return -8;}
}
