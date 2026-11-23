#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include "../render_plan_protocol.h"
#include "style_properties.h"
#include <URUI/Core.h>
#include "../Engine/Source/Core/ElementEffects.h"
#include <cassert>
#include <cstring>
#include <iostream>
#include <limits>
#include <vector>

struct EffectSnapshot
{
    std::vector<URUIStyleValue> values;
    std::vector<char> names;
    EffectSnapshot(URUIDocumentHandle owner, uint64_t element)
    {
        uint64_t revision = 0;
        int count = 0;
        int bytes = 0;
        assert(urui_element_style_snapshot(g_test_surface, owner, element, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) == URUI_ERR_BUFFER_TOO_SMALL);
        values.resize(count);
        names.resize(bytes);
        assert(urui_element_style_snapshot(g_test_surface, owner, element, revision, values.data(), count, names.data(), bytes,
                                           &revision, &count, &bytes) == 1);
    }
    const URUIStyleValue& Get(const char* name) const
    {
        for (const auto& value : values)
        {
            if (!std::strcmp(names.data() + value.name_offset, name))
            {
                return value;
            }
        }
        std::cerr << "Missing effect parameter: " << name << '\n';
        std::abort();
    }
};
int ResolveEffectImage(URUISurfaceHandle, URUIDocumentHandle owner, const char*, uint64_t* id, uint64_t* source,
                       int* w, int* h)
{
    *source = owner;
    *id = 0x8000000000000000ULL | owner;
    *w = 64;
    *h = 64;
    return 1;
}
void ReleaseEffectImage(URUISurfaceHandle, uint64_t)
{
}

struct CountingEffectResources : URUI::ResourceContext
{
    mutable int queries = 0;
    URUI::EffectImageCondition condition{"--urui-image-density", "css-pixels"};
    CountingEffectResources() : ResourceContext(9001, 9001)
    {
    }
    URUI::FileInterface* GetFileInterface() const override
    {
        return URUI::GetFileInterface();
    }
    const URUI::EffectImageCondition* GetEffectImageCondition(const URUI::String& name) const override
    {
        ++queries;
        return name == "probe" ? &condition : nullptr;
    }
};

int main()
{
    static_assert(sizeof(URUIStyleValue) == 32, "style v3 layout");
    static_assert(sizeof(URUIFilter) == 48, "filter v3 layout");
    assert(urui_bridge_abi_version() == 13);
    assert(urui_style_api_version() == 5 && urui_effect_api_version() == 8 && urui_filter_api_version() == 4);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, ResolveEffectImage, ReleaseEffectImage) == 1);
    const auto owner = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>"
        "@property --pattern {syntax:'<image>';inherits:false;initial-value:none;}"
        "@property --tone-probe {syntax:'none | grayscale | sepia';inherits:false;initial-value:none;}"
        "body{margin:0;}#leaf{width:32px;height:32px;--urui-effect:probe;}"
        "#group{width:80px;height:40px;background-color:red;--urui-effect:probe;}"
        "</style></head><body id='body'><img id='leaf' src='white.png'><div id='group'><span id='child'></span></div></body></html>",
        "/views/effects.html");
    assert(urui_document_add_stylesheet(g_test_surface, owner, "#leaf{--pattern:url('../images/noise.png');--tone-probe:sepia;}",
                                        "/styles/effect.css", 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    const auto root = urui_document_root(g_test_surface, owner);
    const auto leaf = urui_element_find(g_test_surface, owner, root, "leaf", nullptr);
    const auto child = urui_element_find(g_test_surface, owner, root, "child", nullptr);
    const auto group = urui_element_find(g_test_surface, owner, root, "group", nullptr);
    EffectSnapshot snapshot(owner, leaf);
    const auto& image = snapshot.Get("--pattern");
    const auto& keyword = snapshot.Get("--tone-probe");
    assert((keyword.kind & 255) == 8 && !std::strcmp(snapshot.names.data() + keyword.text_offset, "sepia"));
    assert((image.kind & 16) != 0);
    assert(!std::strcmp(snapshot.names.data() + image.text_offset, "../images/noise.png"));
    assert(!std::strcmp(snapshot.names.data() + image.source_offset, "/styles/effect.css"));
    EffectSnapshot child_snapshot(owner, child);
    const auto& activation = child_snapshot.Get("--urui-effect");
    assert(!std::strcmp(child_snapshot.names.data() + activation.text_offset, "none"));
    std::vector<URUIPaintCommand> paints(urui_get_paint_commands(g_test_surface, nullptr, 0));
    assert(urui_get_paint_commands(g_test_surface, paints.data(), paints.size()) == int(paints.size()));
    bool original_image = false;
    for (const auto& paint : paints)
    {
        original_image |= paint.element == leaf && paint.reserved == 1;
    }
    assert(original_image);
    std::vector<URUILayerCommand> layers(urui_get_layer_commands(g_test_surface, nullptr, 0));
    assert(!layers.empty());
    assert(urui_get_layer_commands(g_test_surface, layers.data(), layers.size()) == int(layers.size()));
    bool subtree_effect = false;
    for (const auto& layer : layers)
    {
        if (layer.kind == URUI_LAYER_APPLY_MASK && layer.blend == 0)
        {
            URUIFilter filter{};
            char name[128]{};
            int required = 0;
            assert(urui_filter_info(g_test_surface, layer.mask, &filter, name, sizeof(name), &required) == URUI_OK &&
                   required > 0);
            if (!std::strcmp(name, "probe"))
            {
                assert(filter.element == group);
                subtree_effect = true;
                // Validate subtree expansion and effect transforms through RenderPlan v9 (Phase 1 -> 2 -> 3).
                URUIPlanInputPacket planInput{};
                assert(urui_plan_begin(g_test_surface, &planInput) == URUI_OK);
                URUIPlanPreparePacket phase1{};
                assert(urui_plan_resolve(g_test_surface, planInput.lease, nullptr, 0, nullptr, &phase1) == URUI_OK);
                // Phase 1 resolves image effects first.
                std::vector<URUIPlanFeedback> imgFbs;
                for (uint32_t i = 0; i < phase1.count; ++i)
                {
                    assert(phase1.requests[i].kind == URUI_PLAN_PREPARE_IMAGE);
                    URUIPlanFeedback imgFb{};
                    imgFb.request = phase1.requests[i].request;
                    imgFb.kind = URUI_PLAN_PREPARE_IMAGE;
                    imgFb.resource = 3001 + i;
                    imgFb.pixel_width = 32;
                    imgFb.pixel_height = 32;
                    imgFb.ink[2] = 32;
                    imgFb.ink[3] = 32;
                    imgFb.uv[2] = 1;
                    imgFb.uv[3] = 1;
                    imgFbs.push_back(imgFb);
                }
                URUIPlanPreparePacket phase2{};
                assert(urui_plan_resolve(g_test_surface, planInput.lease, imgFbs.data(), uint32_t(imgFbs.size()),
                                         nullptr, &phase2) == URUI_OK);
                uint64_t layoutRequest = 0;
                for (uint32_t i = 0; i < phase2.count; ++i)
                {
                    if (phase2.requests[i].kind == URUI_PLAN_ELEMENT_LAYOUT && phase2.requests[i].element == group)
                    {
                        layoutRequest = phase2.requests[i].request;
                    }
                }
                assert(layoutRequest != 0);

                // Negative expansion must be rejected.
                URUIPlanFeedback badExp{};
                badExp.request = layoutRequest;
                badExp.kind = URUI_PLAN_ELEMENT_LAYOUT;
                badExp.expansion[0] = -1;
                URUIPlanPreparePacket ignored{};
                assert(urui_plan_resolve(g_test_surface, planInput.lease, &badExp, 1, nullptr, &ignored) ==
                       URUI_ERR_INVALID_ARG);

                // NaN or >64 transforms must be rejected.
                float invalid[16]{};
                invalid[0] = std::numeric_limits<float>::quiet_NaN();
                URUIPlanFeedback badXform{};
                badXform.request = layoutRequest;
                badXform.kind = URUI_PLAN_ELEMENT_LAYOUT;
                badXform.transforms = invalid;
                badXform.transform_count = 1;
                assert(urui_plan_resolve(g_test_surface, planInput.lease, &badXform, 1, nullptr, &ignored) ==
                       URUI_ERR_INVALID_ARG);
                const float shifted[16] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 100, 0, 0, 1};
                badXform.transforms = shifted;
                badXform.transform_count = 65;
                assert(urui_plan_resolve(g_test_surface, planInput.lease, &badXform, 1, nullptr, &ignored) ==
                       URUI_ERR_INVALID_ARG);

                // Valid expansion + transform widens the Phase 3 capture domain to 196x48 while preserving 80x40 source_ink.
                URUIPlanFeedback good{};
                good.request = layoutRequest;
                good.kind = URUI_PLAN_ELEMENT_LAYOUT;
                good.expansion[0] = 8;
                good.expansion[1] = 4;
                good.expansion[2] = 8;
                good.expansion[3] = 4;
                good.transforms = shifted;
                good.transform_count = 1;
                URUIPlanPreparePacket phase3{};
                assert(urui_plan_resolve(g_test_surface, planInput.lease, &good, 1, nullptr, &phase3) == URUI_OK);
                assert(phase3.count == 1 && phase3.requests[0].kind == URUI_PLAN_ACQUIRE_CAPTURE);
                assert(phase3.requests[0].source_ink[2] == 80 && phase3.requests[0].source_ink[3] == 40);
                assert(phase3.requests[0].capture[2] == 196 && phase3.requests[0].capture[3] == 48);
                assert(urui_plan_release(g_test_surface, planInput.lease) == URUI_OK);
            }
        }
    }
    assert(subtree_effect);
    float layout[40]{};
    uint64_t coordinate_root = 0;
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 39, &coordinate_root) == -1);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1);
    assert(coordinate_root == leaf && layout[2] == 32 && layout[3] == 32);
    assert(TestSetAttribute(g_test_surface, owner, "leaf", "data-urui-effect-root", "group") == 1);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1);
    assert(coordinate_root == group && layout[22] == 80 && layout[23] == 40);
    assert(TestSetStyle(g_test_surface, owner, "group", "width", "160px") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1 && layout[22] == 160);
    assert(urui_element_destroy(g_test_surface, owner, group) == 1);
    urui_surface_update(g_test_surface);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1 && coordinate_root == leaf);
    const auto newGroup = urui_element_create(g_test_surface, owner, "div");
    assert(newGroup && urui_element_write(g_test_surface, owner, newGroup, 2, "id", "group") == 1);
    assert(urui_element_insert(g_test_surface, owner, urui_document_body(g_test_surface, owner), newGroup, -1) == 1);
    assert(TestSetStyle(g_test_surface, owner, "group", "width", "48px") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1 && coordinate_root != leaf &&
           coordinate_root != group && layout[22] == 48);
    assert(TestSetStyle(g_test_surface, owner, "leaf", "transform", "perspective(300px) rotateY(30deg)") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_effect_layout(g_test_surface, owner, leaf, layout, 40, &coordinate_root) == 1);
    float projected[2]{};
    assert(urui_element_point(g_test_surface, owner, leaf, 1, 12, 8, projected) == 1);
    const float w = layout[7] * 12 + layout[11] * 8 + layout[19];
    assert(std::abs((layout[4] * 12 + layout[8] * 8 + layout[16]) / w - projected[0]) < .001f);
    assert(std::abs((layout[5] * 12 + layout[9] * 8 + layout[17]) / w - projected[1]) < .001f);
    URUIDocumentHandle conditional = 0;
    assert(urui_document_create(g_test_surface, &conditional) == 1);
    assert(urui_document_effect_image_condition(g_test_surface, conditional, "probe", "--blend-type", "alpha") == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            conditional,
            "<!doctype html><html><head><style>@property --blend-type{syntax:'alpha | custom';inherits:false;initial-value:alpha;}img{width:32px;height:32px;--urui-effect:probe;}</style></head><body><img id='conditional' src='white.png'></body></html>",
            "/conditional.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, conditional, 0, 1, 1, 0) == 1);
    assert(urui_effect_layout(g_test_surface, conditional, leaf, layout, 40, &coordinate_root) == -2);
    assert(urui_document_effect_image_condition(g_test_surface, conditional, "probe", "--other", "alpha") < 0);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    const auto conditional_root = urui_document_root(g_test_surface, conditional);
    const auto conditional_image = urui_element_find(g_test_surface, conditional, conditional_root, "conditional", nullptr);
    auto HasDirectImage = [&]()
    {
        std::vector<URUIPaintCommand> current(urui_get_paint_commands(g_test_surface, nullptr, 0));
        assert(urui_get_paint_commands(g_test_surface, current.data(), current.size()) == int(current.size()));
        for (const auto& paint : current)
        {
            if (paint.element == conditional_image && paint.reserved == 1)
            {
                return true;
            }
        }
        return false;
    };
    assert(HasDirectImage());
    assert(TestSetStyle(g_test_surface, conditional, "conditional", "--blend-type", "custom") == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(!HasDirectImage());
    assert(TestSetStyle(g_test_surface, conditional, "conditional", "--blend-type", "alpha") == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(HasDirectImage());
    assert(urui_element_effect_state(g_test_surface, conditional, conditional_image, 1, 0) == 1);
    urui_surface_render(g_test_surface);
    assert(!HasDirectImage());
    assert(urui_element_effect_state(g_test_surface, conditional, conditional_image, 2, 0) == -1);
    assert(urui_element_effect_state(g_test_surface, owner, conditional_image, 0, 0) == -2);
    assert(urui_element_effect_state(g_test_surface, conditional, conditional_image, 0, 0) == 1);
    urui_surface_render(g_test_surface);
    assert(HasDirectImage());
    assert(urui_document_close(g_test_surface, conditional) == 1);
    assert(urui_element_effect_state(g_test_surface, conditional, conditional_image, 0, 0) < 0);
    assert(urui_document_close(g_test_surface, owner) == 1);
    // Repeated paints of an unchanged image must not resolve its effect policy again.
    auto* cached_document = URUI::GetContext(0)->CreateDocument();
    auto resources = URUI::MakeShared<CountingEffectResources>();
    cached_document->SetResourceContext(resources);
    auto image_node = cached_document->CreateElement("img");
    image_node->SetProperty("width", "32px");
    image_node->SetProperty("height", "32px");
    image_node->SetProperty("--urui-effect", "probe");
    cached_document->AppendChild(std::move(image_node));
    cached_document->Show();
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    auto* cached_image = cached_document->GetChild(0);
    assert(cached_image);
    assert(cached_image->GetProperty(URUI::PropertyId::ElementEffect)->Get<URUI::String>() == "probe");
    {
        URUI::ElementEffects cached_effects(cached_image);
        cached_effects.InstanceEffects();
        const auto queries = resources->queries;
        assert(queries > 0);
        for (int i = 0; i < 5; ++i)
        {
            cached_effects.InstanceEffects();
        }
        assert(resources->queries == queries);
    }
    cached_document->Close();
    urui_surface_destroy(g_test_surface);
    std::cout << "element effects: typed images, non-inheriting activation, leaf geometry and subtree input passed\n";
}
