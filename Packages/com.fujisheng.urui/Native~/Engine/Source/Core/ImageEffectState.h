#pragma once
#include "DirectColorFilters.h"
#include "ElementStyle.h"
#include "../../Include/URUI/Core/CompiledFilterShader.h"
#include "../../Include/URUI/Core/RenderManager.h"

namespace URUI {

// 图片与CSS九宫格共用效果解析；样式未变时不创建字符串或编译新程序。
class ImageEffectState {
public:
    const CompiledShader& Resolve(Element* element)
    {
        const auto current = element->GetStyle()->GetComputedRevision();
        if (!initialized || current != revision || capture != element->effect_capture_required) {
            const auto* property = element->GetProperty("--urui-image-effect");
            String next = property ? property->Get<String>() : "none";
            if (next.empty() || next == "none") {
                const auto* generic = element->GetProperty(PropertyId::ElementEffect);
                if (generic && element->IsDirectEffectImage()) next = generic->Get<String>();
            }
            const auto filters = (next.empty() || next == "none") ? DirectColorFilters(element) : FiltersPtr{};
            if (filters) next = "urui:css-color:" + filters->value;
            if (next != name) {
                shader = {}; name = std::move(next);
                if (!name.empty() && name != "none") {
                    Dictionary parameters;
                    parameters["value"] = name;
                    parameters["element"] = static_cast<void*>(element);
                    shader = element->GetRenderManager()->CompileShader(filters ? "--urui-css-color" : "--urui-image-effect", parameters);
                }
            }
            revision = current;
            capture = element->effect_capture_required;
            initialized = true;
        }
        return shader;
    }
private:
    CompiledShader shader;
    String name;
    uint64_t revision = 0;
    bool initialized = false;
    bool capture = false;
};

}
