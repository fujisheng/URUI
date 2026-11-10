#include "ElementBackgroundBorder.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "BoxShadowCache.h"
#include "CssImageGeometry.h"
#include "../../../graphic_tint.h"

namespace URUI {

ElementBackgroundBorder::ElementBackgroundBorder() {}

void ElementBackgroundBorder::Render(Element* element, bool foreground, bool image_border)
{
    if (background_dirty || border_dirty) {
        for (auto& background : backgrounds) background.second.geometry.Release();
        GenerateGeometry(element);
        background_dirty = border_dirty = false;
    }
    const Vector2f offset = element->GetAbsoluteOffset(BoxArea::Border);
    if (Background* shadow = GetBackground(foreground ? BackgroundType::InnerShadow : BackgroundType::OuterShadow))
        if (shadow->shadow) shadow->shadow->geometry.Render(offset, shadow->shadow->texture);
    if (Background* background = (foreground && image_border ? nullptr : GetBackground(foreground ? BackgroundType::Border : BackgroundType::Background)))
        background->geometry.Render(offset);
}

void ElementBackgroundBorder::DirtyBackground() { background_dirty = true; }
void ElementBackgroundBorder::DirtyBorder() { border_dirty = true; }

Geometry* ElementBackgroundBorder::GetClipGeometry(Element* element, BoxArea clip_area)
{
    BackgroundType type = {};
    switch (clip_area) {
    case BoxArea::Border: type = BackgroundType::ClipBorder; break;
    case BoxArea::Padding: type = BackgroundType::ClipPadding; break;
    case BoxArea::Content: type = BackgroundType::ClipContent; break;
    default: URUI_ERROR; return nullptr;
    }
    RenderManager* manager = element->GetRenderManager();
    Geometry& geometry = GetOrCreateBackground(type).geometry;
    if (manager && !geometry) {
        Mesh mesh = geometry.Release(Geometry::ReleaseMode::ClearMesh);
        MeshUtilities::GenerateBackground(mesh, element->GetRenderBox(clip_area), ColourbPremultiplied(255));
        geometry = manager->MakeGeometry(std::move(mesh));
    }
    return &geometry;
}

ElementBackgroundBorder::Background* ElementBackgroundBorder::GetBackground(BackgroundType type)
{
    const auto found = backgrounds.find(type);
    return found == backgrounds.end() ? nullptr : &found->second;
}
ElementBackgroundBorder::Background& ElementBackgroundBorder::GetOrCreateBackground(BackgroundType type) { return backgrounds[type]; }

void ElementBackgroundBorder::GenerateGeometry(Element* element)
{
    URUI_ZoneScoped;
    RenderManager* manager = element->GetRenderManager();
    if (!manager) return;
    const ComputedValues& computed = element->GetComputedValues();
    for (const auto& pass : {std::make_pair(BackgroundType::OuterShadow, false), std::make_pair(BackgroundType::InnerShadow, true)}) {
        if (computed.has_box_shadow()) GetOrCreateBackground(pass.first).shadow = BoxShadowCache::GetHandle(element, computed, pass.second);
        else backgrounds.erase(pass.first);
    }
    const Array<ColourbPremultiplied, 4> border_colors = {
        urui_graphic::Tint(*element,computed.border_top_color()).ToPremultiplied(), urui_graphic::Tint(*element,computed.border_right_color()).ToPremultiplied(),
        urui_graphic::Tint(*element,computed.border_bottom_color()).ToPremultiplied(), urui_graphic::Tint(*element,computed.border_left_color()).ToPremultiplied()};
    const size_t layer_count = GetCssImageLayerCount(element, PropertyId::BackgroundImage);
    const auto clip = GetCssImageArea(element, PropertyId::BackgroundClip, layer_count ? layer_count-1 : 0);
    Mesh background_mesh, border_mesh;
    for (int index = 0; index < element->GetNumBoxes(); ++index) {
        MeshUtilities::GenerateBackground(background_mesh, element->GetRenderBox(clip,index), urui_graphic::Tint(*element,computed.background_color()).ToPremultiplied());
        MeshUtilities::GenerateBackgroundBorder(border_mesh, element->GetRenderBox(BoxArea::Padding,index), ColourbPremultiplied(0,0), border_colors.data());
    }
    GetOrCreateBackground(BackgroundType::Background).geometry = manager->MakeGeometry(std::move(background_mesh));
    GetOrCreateBackground(BackgroundType::Border).geometry = manager->MakeGeometry(std::move(border_mesh));
}

} // namespace URUI
