#include "DecoratorShader.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "../../Include/URUI/Core/RenderManager.h"

namespace URUI
{

    Pool<ShaderElementData>& GetShaderElementDataPool()
    {
        static Pool<ShaderElementData> gradient_element_data_pool(20, true);
        return gradient_element_data_pool;
    }

    DecoratorShader::DecoratorShader()
    {
    }

    DecoratorShader::~DecoratorShader()
    {
    }

    bool DecoratorShader::Initialise(String&& in_value)
    {
        value = std::move(in_value);
        return true;
    }

    DecoratorDataHandle DecoratorShader::GenerateElementData(Element* element, BoxArea paint_area) const
    {
        RenderManager* render_manager = element->GetRenderManager();
        if (!render_manager)
        {
            return INVALID_DECORATORDATAHANDLE;
        }

        const RenderBox render_box = element->GetRenderBox(paint_area);
        const Vector2f dimensions = render_box.GetFillSize();
        // Keep the owning node available to external renderers without encoding identity in authored markup.
        CompiledShader shader =
            render_manager->CompileShader("shader", Dictionary{{"value", Variant(value)},
                                                               {"dimensions", Variant(dimensions)},
                                                               {"element", Variant(static_cast<void*>(element))}});
        if (!shader)
        {
            return INVALID_DECORATORDATAHANDLE;
        }

        Mesh mesh;

        const byte alpha = byte(1.f * 255.f);
        MeshUtilities::GenerateBackground(mesh, render_box, ColourbPremultiplied(alpha, alpha));

        const Vector2f offset = render_box.GetFillOffset();
        const float inv_w = dimensions.x > 0.f ? 1.f / dimensions.x : 0.f;
        const float inv_h = dimensions.y > 0.f ? 1.f / dimensions.y : 0.f;
        for (Vertex& vertex : mesh.vertices)
        {
            const Vector2f delta = vertex.position - offset;
            vertex.tex_coord = Vector2f(delta.x * inv_w, delta.y * inv_h);
        }

        ShaderElementData* element_data = GetShaderElementDataPool().AllocateAndConstruct(
            render_manager->MakeGeometry(std::move(mesh)), std::move(shader));

        return reinterpret_cast<DecoratorDataHandle>(element_data);
    }

    void DecoratorShader::ReleaseElementData(DecoratorDataHandle handle) const
    {
        ShaderElementData* element_data = reinterpret_cast<ShaderElementData*>(handle);
        GetShaderElementDataPool().DestroyAndDeallocate(element_data);
    }

    void DecoratorShader::RenderElement(Element* element, DecoratorDataHandle handle) const
    {
        ShaderElementData* element_data = reinterpret_cast<ShaderElementData*>(handle);
        element_data->geometry.Render(element->GetAbsoluteOffset(BoxArea::Border), {}, element_data->shader);
    }

    DecoratorShaderInstancer::DecoratorShaderInstancer()
    {
        ids.value = RegisterProperty("value", String()).AddParser("string").GetId();
        RegisterShorthand("decorator", "value", ShorthandType::FallThrough);
    }

    DecoratorShaderInstancer::~DecoratorShaderInstancer()
    {
    }

    SharedPtr<Decorator> DecoratorShaderInstancer::InstanceDecorator(const String& /*name*/,
                                                                     const PropertyDictionary& properties_,
                                                                     const DecoratorInstancerInterface& /*interface_*/)
    {
        const Property* p_value = properties_.GetProperty(ids.value);
        if (!p_value)
        {
            return nullptr;
        }

        String value = p_value->Get<String>();

        auto decorator = MakeShared<DecoratorShader>();
        if (decorator->Initialise(std::move(value)))
        {
            return decorator;
        }

        return nullptr;
    }

}
