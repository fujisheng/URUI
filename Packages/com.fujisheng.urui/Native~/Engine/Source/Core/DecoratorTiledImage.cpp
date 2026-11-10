#include "DecoratorTiledImage.h"
#include "CssImageGeometry.h"
#include "ImageEffectState.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/RenderManager.h"

namespace URUI {

namespace {
struct ImageData {
    Geometry geometry;
    ImageEffectState effect;
};
}

DecoratorTiledImage::DecoratorTiledImage() {}

DecoratorTiledImage::~DecoratorTiledImage() {}

bool DecoratorTiledImage::Initialise(const Tile& _tile, Texture _texture)
{
	tile = _tile;
	tile.texture_index = AddTexture(_texture);
	return (tile.texture_index >= 0);
}

DecoratorDataHandle DecoratorTiledImage::GenerateElementData(Element* element, BoxArea paint_area) const
{
    if (css_image_property != PropertyId::Invalid) {
        Mesh mesh = css_image_property == PropertyId::BorderImageSource ? GenerateCssBorderImageMesh(element, Vector2f(GetTexture().GetDimensions())) :
            GenerateCssImageMesh(element, css_image_property, css_image_layer, Vector2f(GetTexture().GetDimensions()));
        return reinterpret_cast<DecoratorDataHandle>(new ImageData{element->GetRenderManager()->MakeGeometry(std::move(mesh)), {}});
    }
	// Calculate the tile's dimensions for this element.
	tile.CalculateDimensions(GetTexture());

	const ComputedValues& computed = element->GetComputedValues();

	const RenderBox render_box = element->GetRenderBox(paint_area);
	const Vector2f offset = render_box.GetFillOffset();
	const Vector2f size = render_box.GetFillSize();

	// Generate the geometry for the tile.
	Mesh mesh;
	if (tile.fit_mode == REPEAT) mesh.texture_sampling = TextureSampling::Repeat;
	else if (tile.fit_mode == REPEAT_X) mesh.texture_sampling = TextureSampling::RepeatX;
	else if (tile.fit_mode == REPEAT_Y) mesh.texture_sampling = TextureSampling::RepeatY;
	tile.GenerateGeometry(mesh, computed, offset, size, tile.GetNaturalDimensions(element));

	ImageData* data = new ImageData{element->GetRenderManager()->MakeGeometry(std::move(mesh)), {}};

	return reinterpret_cast<DecoratorDataHandle>(data);
}

void DecoratorTiledImage::ReleaseElementData(DecoratorDataHandle element_data) const
{
	delete reinterpret_cast<ImageData*>(element_data);
}

void DecoratorTiledImage::RenderElement(Element* element, DecoratorDataHandle element_data) const
{
	ImageData* data = reinterpret_cast<ImageData*>(element_data);
    if (css_image_property == PropertyId::BorderImageSource)
        data->geometry.Render(element->GetAbsoluteOffset(BoxArea::Border), GetTexture(), data->effect.Resolve(element));
    else
        data->geometry.Render(element->GetAbsoluteOffset(BoxArea::Border), GetTexture());
}

DecoratorTiledImageInstancer::DecoratorTiledImageInstancer(bool standard_image) : DecoratorTiledInstancer(1), standard_image(standard_image)
{
	RegisterTileProperty("image", true);
	RegisterShorthand("decorator", "image", ShorthandType::RecursiveRepeat);
}

DecoratorTiledImageInstancer::~DecoratorTiledImageInstancer() {}

SharedPtr<Decorator> DecoratorTiledImageInstancer::InstanceDecorator(const String& /*name*/, const PropertyDictionary& properties,
	const DecoratorInstancerInterface& instancer_interface)
{
	DecoratorTiled::Tile tile;
	Texture texture;

	if (!GetTileProperties(&tile, &texture, 1, properties, instancer_interface, standard_image))
		return nullptr;

	auto decorator = MakeShared<DecoratorTiledImage>();

	if (!decorator->Initialise(tile, texture))
		return nullptr;

	return decorator;
}

} // namespace URUI
