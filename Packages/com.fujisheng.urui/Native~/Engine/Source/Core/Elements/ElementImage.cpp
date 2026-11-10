#include <URUI/Core/ResourceContext.h>
#include "ElementImage.h"
#include "../DirectColorFilters.h"
#include "../ElementStyle.h"
#include "../../../Include/URUI/Core/ComputedValues.h"
#include "../../../Include/URUI/Core/ElementDocument.h"
#include "../../../Include/URUI/Core/ElementUtilities.h"
#include "../../../Include/URUI/Core/MeshUtilities.h"
#include "../../../Include/URUI/Core/PropertyIdSet.h"
#include "../../../Include/URUI/Core/StyleSheet.h"
#include "../../../Include/URUI/Core/Texture.h"
#include "../../../Include/URUI/Core/URL.h"
#include "../TextureDatabase.h"
#include "../../../../image_geometry.h"
#include "../../../../graphic_tint.h"

namespace URUI {

URUI_RTTI_Define(ElementImage)

ElementImage::ElementImage(const String& tag) : Element(tag), dimensions(-1, -1), rect_source(RectSource::None)
{
	dimensions_scale = 1.0f;
	geometry_dirty = false;
	texture_dirty = true;
}

ElementImage::~ElementImage() {}

bool ElementImage::GetIntrinsicDimensions(Vector2f& _dimensions, float& _ratio)
{
	EnsureSourceLoaded();

	if (rect_source == RectSource::None)
	{
		dimensions = Vector2f(texture.GetDimensions());
	}
	else
	{
		dimensions = rect.Size();
	}

	dimensions *= dimensions_scale;
	if (dimensions.y > 0)
	{
		_ratio = dimensions.x / dimensions.y;

		// Scale intrinsic size based on attributes. CSS properties can in turn override these attributes, see LayoutDetails.
		auto requested_width = GetAttribute<float>("width", -1);
		auto requested_height = GetAttribute<float>("height", -1);
		if (requested_height > 0 && requested_width > 0)
		{
			// If both a height and width are set update the ratio to match
			_ratio = requested_width / requested_height;
			dimensions.x = requested_width;
			dimensions.y = requested_height;
		}
		else if (requested_height > 0)
		{
			dimensions.x = requested_height * _ratio;
			dimensions.y = requested_height;
		}
		else if (requested_width > 0)
		{
			dimensions.x = requested_width;
			dimensions.y = requested_width / _ratio;
		}
	}

	// Return the calculated dimensions. If this changes the size of the element, it will result in
	// a call to 'onresize' below which will regenerate the geometry.
	_dimensions = dimensions;

	return true;
}
void ElementImage::EnsureSourceLoaded()
{
	if (texture_dirty)
		LoadTexture();
}

void ElementImage::OnRender()
{
	// Regenerate the geometry if required (this will be set if 'rect' changes but does not result in a resize).
	if (geometry_dirty)
		GenerateGeometry();

	geometry.Render(GetAbsoluteOffset(BoxArea::Border), texture, image_effect.Resolve(this));
}

void ElementImage::OnAttributeChange(const ElementAttributes& changed_attributes)
{
	// Call through to the base element's OnAttributeChange().
	Element::OnAttributeChange(changed_attributes);

	bool dirty_layout = false;

	// Check for a changed 'src' attribute. If this changes, the old texture handle is released,
	// forcing a reload when the layout is regenerated.
	if (changed_attributes.find("src") != changed_attributes.end() || changed_attributes.find("data-urui-sprite") != changed_attributes.end())
	{
		texture_dirty = true;
		dirty_layout = true;
	}

	// Check for a changed 'width' attribute. If this changes, a layout is forced which will
	// recalculate the dimensions.
	if (changed_attributes.find("width") != changed_attributes.end() || changed_attributes.find("height") != changed_attributes.end())
	{
		dirty_layout = true;
	}

	// Check for a change to the 'rect' attribute. If this changes, the coordinates are
	// recomputed and a layout forced. If a sprite is set to source, then that will override any attribute.
	if (changed_attributes.find("data-urui-rect") != changed_attributes.end())
	{
		UpdateRect();

		// Rectangle has changed; this will most likely result in a size change, so we need to force a layout.
		dirty_layout = true;
	}

	if (dirty_layout)
		DirtyLayout();
}

void ElementImage::OnPropertyChange(const PropertyIdSet& changed_properties)
{
	Element::OnPropertyChange(changed_properties);
	const auto* density = StyleSheetSpecification::GetProperty("--urui-image-density");
	if (density && changed_properties.Contains(density->GetId())) {
		texture_dirty = true;
		DirtyLayout();
	}

	if (changed_properties.Contains(PropertyId::ImageColor) || changed_properties.Contains(PropertyId::Opacity) || urui_image::Changed(changed_properties))
		geometry_dirty = true;
}

void ElementImage::OnChildAdd(Element* child)
{
	// Load the texture once we have attached to the document so that it can immediately be found during the call to `URUI::GetTextureSourceList`. The
	// texture won't actually be loaded from the backend before it is shown. However, only do this if we have an active context so that the dp-ratio
	// can be retrieved. If there is no context now the texture loading will be deferred until the next layout update.
	if (child == this && texture_dirty && GetContext())
		LoadTexture();
}

void ElementImage::OnResize()
{
	geometry_dirty = true;
}

void ElementImage::OnDpRatioChange()
{
	texture_dirty = true;
	DirtyLayout();
}

void ElementImage::OnStyleSheetChange()
{
	if (HasAttribute("data-urui-sprite"))
	{
		texture_dirty = true;
		DirtyLayout();
	}
}

void ElementImage::GenerateGeometry()
{
	// Release the old geometry before specifying the new vertices.
	Mesh mesh = geometry.Release(Geometry::ReleaseMode::ClearMesh);

	// Generate the texture coordinates.
	Vector2f texcoords[2];
	if (rect_source != RectSource::None)
	{
		Vector2f texture_dimensions = Vector2f(Math::Max(texture.GetDimensions(), Vector2i(1)));
		texcoords[0] = rect.TopLeft() / texture_dimensions;
		texcoords[1] = rect.BottomRight() / texture_dimensions;
	}
	else
	{
		texcoords[0] = Vector2f(0, 0);
		texcoords[1] = Vector2f(1, 1);
	}

	const ComputedValues& computed = GetComputedValues();
	// 交互反馈独立乘色，保留作者颜色及透明区域；不作用于兄弟装饰。
    const ColourbPremultiplied quad_colour = urui_graphic::Tint(*this, computed.image_color()).ToPremultiplied();
	const RenderBox render_box = GetRenderBox(BoxArea::Content);

	const Vector2f source_size = rect_source == RectSource::None ? Vector2f(texture.GetDimensions()) : rect.Size();
	urui_image::Generate(*this, mesh, render_box.GetFillOffset(), render_box.GetFillSize(), texcoords[0], texcoords[1], source_size, dimensions_scale, quad_colour);

	if (RenderManager* render_manager = GetRenderManager())
		geometry = render_manager->MakeGeometry(std::move(mesh));

	geometry_dirty = false;
}

bool ElementImage::LoadTexture()
{
	texture_dirty = false;
	geometry_dirty = true;
	dimensions_scale = 1.0f;

	RenderManager* render_manager = GetRenderManager();
	if (!render_manager)
	{
		texture = {};
		return false;
	}

	const float dp_ratio = ElementUtilities::GetDensityIndependentPixelRatio(this);

	// Check for a sprite first, this takes precedence.
	const String sprite_name = GetAttribute<String>("data-urui-sprite", "");
	if (!sprite_name.empty())
	{
		// Load sprite.
		bool valid_sprite = false;

		if (ElementDocument* document = GetOwnerDocument())
		{
			if (const auto& resources = document->GetResourceContext())
			{
				if (const Sprite* sprite = resources->GetSprite(sprite_name))
				{
					rect = sprite->rectangle;
					rect_source = RectSource::Sprite;
					texture = sprite->sprite_sheet->texture_source.GetTexture(*render_manager, GetOwnerDocument() ? GetOwnerDocument()->GetResourceContext() : nullptr);
					dimensions_scale = sprite->sprite_sheet->display_scale * dp_ratio;
					valid_sprite = true;
				}
			}
		}

		if (!valid_sprite)
		{
			texture = {};
			rect_source = RectSource::None;
			UpdateRect();
			Log::Message(Log::LT_WARNING, "Could not find sprite '%s' specified in img element %s", sprite_name.c_str(), GetAddress().c_str());
			return false;
		}
	}
	else
	{
		// Load image from source URL.
		const String source_name = GetAttribute<String>("src", "");
		if (source_name.empty())
		{
			texture = {};
			rect_source = RectSource::None;
			return false;
		}

		URL source_url;

		if (ElementDocument* document = GetOwnerDocument())
			source_url.SetURL(document->GetSourceURL());

		texture = render_manager->LoadTexture(source_name, source_url.GetURL(), GetOwnerDocument() ? GetOwnerDocument()->GetResourceContext() : nullptr);

		dimensions_scale = urui_image::Number(*this, "--urui-image-density", 0) == 1 ? dp_ratio : 1.f;
	}

	return true;
}

void ElementImage::UpdateRect()
{
	if (rect_source != RectSource::Sprite)
	{
		bool valid_rect = false;

		String rect_string = GetAttribute<String>("data-urui-rect", "");
		if (!rect_string.empty())
		{
			StringList coords_list;
			StringUtilities::ExpandString(coords_list, rect_string, ' ');

			if (coords_list.size() != 4)
			{
				Log::Message(Log::LT_WARNING, "Element '%s' has an invalid 'rect' attribute; rect requires 4 space-separated values, found %zu.",
					GetAddress().c_str(), coords_list.size());
			}
			else
			{
				const Vector2f position = {FromString(coords_list[0], 0.f), FromString(coords_list[1], 0.f)};
				const Vector2f size = {FromString(coords_list[2], 0.f), FromString(coords_list[3], 0.f)};
				rect = Rectanglef::FromPositionSize(position, size);

				// We have new, valid coordinates; force the geometry to be regenerated.
				valid_rect = true;
				geometry_dirty = true;
				rect_source = RectSource::Attribute;
			}
		}

		if (!valid_rect)
		{
			rect = {};
			rect_source = RectSource::None;
		}
	}
}

} // namespace URUI
