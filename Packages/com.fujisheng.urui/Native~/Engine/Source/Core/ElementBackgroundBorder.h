#pragma once

#include "../../Include/URUI/Core/CallbackTexture.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/Types.h"

namespace URUI
{

    struct BoxShadowRenderable;

    class ElementBackgroundBorder
    {
    public:
        ElementBackgroundBorder();
        // 背景图像插在底色与内阴影/边框之间，遵循标准绘制顺序。
        void Render(Element* element, bool foreground, bool image_border = false);
        void DirtyBackground();
        void DirtyBorder();
        Geometry* GetClipGeometry(Element* element, BoxArea clip_area);

    private:
        enum class BackgroundType
        {
            Background,
            Border,
            OuterShadow,
            InnerShadow,
            ClipBorder,
            ClipPadding,
            ClipContent
        };
        struct Background
        {
            Geometry geometry;
            SharedPtr<BoxShadowRenderable> shadow;
        };
        Background* GetBackground(BackgroundType type);
        Background& GetOrCreateBackground(BackgroundType type);
        void GenerateGeometry(Element* element);
        bool background_dirty = true;
        bool border_dirty = true;
        StableMap<BackgroundType, Background> backgrounds;
    };

}
