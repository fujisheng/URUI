#include "test_document.h"
#include "urui.h"
#include "style_properties.h"
#include <URUI/Core.h>
#include "../Engine/Source/Core/ElementEffects.h"
#include <cassert>
#include <cstring>
#include <iostream>
#include <limits>
#include <vector>

struct EffectSnapshot {
    std::vector<UruiStyleValue> values;
    std::vector<char> names;
    EffectSnapshot(UruiDocumentHandle owner, uint64_t element) {
        uint64_t revision = 0; int count = 0, bytes = 0;
        assert(urui_element_style_snapshot(owner, element, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) == -3);
        values.resize(count); names.resize(bytes);
        assert(urui_element_style_snapshot(owner, element, revision, values.data(), count, names.data(), bytes, &revision, &count, &bytes) == 1);
    }
    const UruiStyleValue& Get(const char* name) const {
        for (const auto& value : values) if (!std::strcmp(names.data() + value.name_offset, name)) return value;
        std::cerr << "Missing effect parameter: " << name << '\n'; std::abort();
    }
};
int ResolveEffectImage(UruiDocumentHandle owner, const char*, uint64_t* id, uint64_t* source, int* w, int* h) {
    *source = owner; *id = 0x8000000000000000ULL | owner; *w = 64; *h = 64; return 1;
}
void ReleaseEffectImage(uint64_t) {}

struct CountingEffectResources : URUI::ResourceContext {
    mutable int queries = 0;
    URUI::EffectImageCondition condition{"--urui-image-density", "css-pixels"};
    CountingEffectResources() : ResourceContext(9001, 9001) {}
    URUI::FileInterface* GetFileInterface() const override { return URUI::GetFileInterface(); }
    const URUI::EffectImageCondition* GetEffectImageCondition(const URUI::String& name) const override {
        ++queries; return name == "probe" ? &condition : nullptr;
    }
};

int main() {
    static_assert(sizeof(UruiStyleValue) == 32, "style v3 layout");
    static_assert(sizeof(UruiFilter) == 48, "filter v3 layout");
    assert(urui_bridge_abi_version() == 11);
    assert(urui_style_api_version() == 4 && urui_effect_api_version() == 7 && urui_filter_api_version() == 3);
    assert(urui_create(400, 200) == 1);
    assert(urui_set_image_resolver(ResolveEffectImage, ReleaseEffectImage) == 1);
    const auto owner = LoadTestDocument(
        "<!doctype html><html><head><style>"
        "@property --pattern {syntax:'<image>';inherits:false;initial-value:none;}"
        "@property --tone-probe {syntax:'none | grayscale | sepia';inherits:false;initial-value:none;}"
        "body{margin:0;}#leaf{width:32px;height:32px;--urui-effect:probe;}"
        "#group{width:80px;height:40px;background-color:red;--urui-effect:probe;}"
        "</style></head><body id='body'><img id='leaf' src='white.png'><div id='group'><span id='child'></span></div></body></html>",
        "/views/effects.html");
    assert(urui_document_add_stylesheet(owner, "#leaf{--pattern:url('../images/noise.png');--tone-probe:sepia;}", "/styles/effect.css", 0) == 1);
    urui_update(); urui_render();
    const auto root = urui_document_root(owner);
    const auto leaf = urui_element_find(owner, root, "leaf", nullptr);
    const auto child = urui_element_find(owner, root, "child", nullptr);
    const auto group = urui_element_find(owner, root, "group", nullptr);
    EffectSnapshot snapshot(owner, leaf);
    const auto& image = snapshot.Get("--pattern");
    const auto& keyword = snapshot.Get("--tone-probe");
    assert((keyword.kind&255) == 8 && !std::strcmp(snapshot.names.data() + keyword.text_offset, "sepia"));
    assert((image.kind & 16) != 0);
    assert(!std::strcmp(snapshot.names.data() + image.text_offset, "../images/noise.png"));
    assert(!std::strcmp(snapshot.names.data() + image.source_offset, "/styles/effect.css"));
    EffectSnapshot child_snapshot(owner, child);
    const auto& activation = child_snapshot.Get("--urui-effect");
    assert(!std::strcmp(child_snapshot.names.data() + activation.text_offset, "none"));
    std::vector<UruiPaintCommand> paints(urui_get_paint_commands(nullptr, 0));
    assert(urui_get_paint_commands(paints.data(), paints.size()) == int(paints.size()));
    bool original_image = false;
    for (const auto& paint : paints) original_image |= paint.element == leaf && paint.reserved == 1;
    assert(original_image);
    std::vector<UruiLayerCommand> layers(urui_get_layer_commands(nullptr, 0));
    assert(!layers.empty());
    assert(urui_get_layer_commands(layers.data(), layers.size()) == int(layers.size()));
    bool subtree_effect = false;
    for (const auto& layer : layers) if (layer.kind == 6 && layer.blend == 0) {
        UruiFilter filter{}; char name[128]{};
        assert(urui_filter_info(layer.mask, &filter, name, sizeof(name)) > 0);
        if (!std::strcmp(name, "probe")) {
            assert(filter.element == group); subtree_effect = true;
            assert(urui_element_effect_expansion(layer.mask, -1, 0, 0, 0) == -1);
            assert(urui_element_effect_expansion(layer.mask, 8, 4, 8, 4) == 1);
            std::vector<UruiLayerRegion> regions(urui_get_layer_regions(nullptr, 0));
            assert(urui_get_layer_regions(regions.data(), regions.size()) == int(regions.size()));
            bool expanded = false;
            for (const auto& region : regions) if (region.layer == layer.source) {
                assert(region.width == 96 && region.height == 48); expanded = true;
            }
            assert(expanded);
            std::vector<UruiEffectInputRegion> inputs(urui_get_effect_input_regions(nullptr,0));
            assert(urui_get_effect_input_regions(inputs.data(),inputs.size())==int(inputs.size()));
            bool source_domain=false;
            for(const auto& input:inputs)if(input.filter==layer.mask) {assert(input.layer==layer.source&&input.width==80&&input.height==40);source_domain=true;}
            assert(source_domain);
            const float shifted[16]={1,0,0,0,0,1,0,0,0,0,1,0,100,0,0,1};
            assert(urui_element_effect_transforms(layer.mask,shifted,1)==1);
            float invalid[16]{};invalid[0]=std::numeric_limits<float>::quiet_NaN();
            assert(urui_element_effect_transforms(layer.mask,invalid,1)==-1);
            assert(urui_element_effect_transforms(layer.mask,shifted,65)==-1);
            assert(urui_get_layer_regions(regions.data(),regions.size())==int(regions.size()));
            bool transformed=false;
            for(const auto& region:regions)if(region.layer==layer.source){assert(region.width==196&&region.height==48);transformed=true;}
            assert(transformed);
            assert(urui_element_effect_transforms(layer.mask,nullptr,0)==1);
        }
    }
    assert(subtree_effect);
    float layout[40]{}; uint64_t coordinate_root=0;
    assert(urui_effect_layout(owner,leaf,layout,39,&coordinate_root)==-1);
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1);
    assert(coordinate_root==leaf && layout[2]==32 && layout[3]==32);
    assert(urui_set_attribute(owner,"leaf","data-urui-effect-root","group")==1);
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1);
    assert(coordinate_root==group && layout[22]==80 && layout[23]==40);
    assert(urui_set_style(owner,"group","width","160px")==1);urui_update();
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1 && layout[22]==160);
    assert(urui_remove_element(owner,"group")==1);urui_update();
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1 && coordinate_root==leaf);
    assert(urui_append_element(owner,"body","div","group")==1);
    assert(urui_set_style(owner,"group","width","48px")==1);urui_update();
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1 && coordinate_root!=leaf && coordinate_root!=group && layout[22]==48);
    assert(urui_set_style(owner,"leaf","transform","perspective(300px) rotateY(30deg)")==1);urui_update();
    assert(urui_effect_layout(owner,leaf,layout,40,&coordinate_root)==1);
    float projected[2]{}; assert(urui_element_point(owner,leaf,1,12,8,projected)==1);
    const float w=layout[7]*12+layout[11]*8+layout[19];
    assert(std::abs((layout[4]*12+layout[8]*8+layout[16])/w-projected[0])<.001f);
    assert(std::abs((layout[5]*12+layout[9]*8+layout[17])/w-projected[1])<.001f);
    UruiDocumentHandle conditional=0;
    assert(urui_document_create(&conditional)==1);
    assert(urui_document_effect_image_condition(conditional,"probe","--blend-type","alpha")==1);
    assert(urui_document_load_html(conditional,"<!doctype html><html><head><style>@property --blend-type{syntax:'alpha | custom';inherits:false;initial-value:alpha;}img{width:32px;height:32px;--urui-effect:probe;}</style></head><body><img id='conditional' src='white.png'></body></html>","/conditional.html")==1);
    assert(urui_document_set_presentation(conditional,0,1,1,0)==1);
    assert(urui_effect_layout(conditional,leaf,layout,40,&coordinate_root)==-2);
    assert(urui_document_effect_image_condition(conditional,"probe","--other","alpha")<0);
    urui_update();urui_render();
    const auto conditional_root=urui_document_root(conditional);
    const auto conditional_image=urui_element_find(conditional,conditional_root,"conditional",nullptr);
    auto HasDirectImage=[&]() {
        std::vector<UruiPaintCommand> current(urui_get_paint_commands(nullptr,0));
        assert(urui_get_paint_commands(current.data(),current.size())==int(current.size()));
        for(const auto& paint:current)if(paint.element==conditional_image&&paint.reserved==1)return true;
        return false;
    };
    assert(HasDirectImage());
    assert(urui_set_style(conditional,"conditional","--blend-type","custom")==1);urui_update();urui_render();
    assert(!HasDirectImage());
    assert(urui_set_style(conditional,"conditional","--blend-type","alpha")==1);urui_update();urui_render();
    assert(HasDirectImage());
    assert(urui_element_effect_state(conditional,conditional_image,1,0)==1);urui_render();
    assert(!HasDirectImage());
    assert(urui_element_effect_state(conditional,conditional_image,2,0)==-1);
    assert(urui_element_effect_state(owner,conditional_image,0,0)==-2);
    assert(urui_element_effect_state(conditional,conditional_image,0,0)==1);urui_render();
    assert(HasDirectImage());
    assert(urui_document_close(conditional)==1);
    assert(urui_element_effect_state(conditional,conditional_image,0,0)<0);
    assert(urui_document_close(owner) == 1);
    // Repeated paints of an unchanged image must not resolve its effect policy again.
    auto* cached_document = URUI::GetContext("urui")->CreateDocument();
    auto resources = URUI::MakeShared<CountingEffectResources>();
    cached_document->SetResourceContext(resources);
    auto image_node=cached_document->CreateElement("img");
    image_node->SetProperty("width","32px");image_node->SetProperty("height","32px");image_node->SetProperty("--urui-effect","probe");
    cached_document->AppendChild(std::move(image_node));
    cached_document->Show();urui_update();urui_render();
    auto* cached_image=cached_document->GetChild(0);assert(cached_image);
    assert(cached_image->GetProperty(URUI::PropertyId::ElementEffect)->Get<URUI::String>()=="probe");
    { URUI::ElementEffects cached_effects(cached_image);cached_effects.InstanceEffects();
    const auto queries = resources->queries;
    assert(queries > 0);
    for (int i=0;i<5;++i) cached_effects.InstanceEffects();
    assert(resources->queries == queries); }
    cached_document->Close();urui_destroy();
    std::cout << "element effects: typed images, non-inheriting activation, leaf geometry and subtree input passed\n";
}
