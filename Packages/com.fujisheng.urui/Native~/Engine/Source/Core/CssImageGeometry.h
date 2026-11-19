#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"
#include "../../Include/URUI/Core/Mesh.h"

namespace URUI
{

    // 标准图像几何与游戏装饰器分开：CSS 源像素、定位区、绘制区及每层列表独立计算。
    class PropertyParserImageGeometry final : public PropertyParser
    {
    public:
        bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;
    };

    Mesh GenerateCssBorderImageMesh(Element* element, Vector2f source_size);
    bool ParseCssBorderImageSlice(const String& value, Array<NumericValue, 4>& slices, bool& fill);
    bool ParseCssBorderImageShorthand(const PropertySpecification& specification, PropertyDictionary& dictionary,
                                      const String& value);
    bool ParseCssBackgroundShorthand(const PropertySpecification& specification, PropertyDictionary& dictionary,
                                     const String& value);
    bool SplitCssImageComponents(const String& value, Vector<StringList>& layers);
    bool ParseCssImagePosition(const StringList& parts, Array<NumericValue, 2>& position);
    BoxArea GetCssImageArea(Element* element, PropertyId property, size_t layer);
    size_t GetCssImageLayerCount(Element* element, PropertyId image_property);
    struct CssImageLayout
    {
        Vector2f size;
        Vector2f origin;
        BoxArea clip = BoxArea::Border;
        TextureSampling sampling = TextureSampling::Clamp;
    };
    CssImageLayout ResolveCssImageLayout(Element* element, PropertyId image_property, size_t layer,
                                         Vector2f natural_size, bool intrinsic_ratio, int box);
    Mesh GenerateCssImageMesh(Element* element, PropertyId image_property, size_t layer, Vector2f natural_size,
                              bool intrinsic_ratio = true, int box_index = -1);

}
