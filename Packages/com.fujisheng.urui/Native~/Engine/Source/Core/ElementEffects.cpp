#include "ElementEffects.h"
#include "DirectColorFilters.h"
#include "ElementEffectImagePolicy.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Decorator.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/Filter.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "ElementStyle.h"
#include "../../Include/URUI/Core/RenderManager.h"

namespace URUI {

ElementEffects::ElementEffects(Element* _element) : element(_element) {}

ElementEffects::~ElementEffects()
{
	ReleaseEffects();
}

bool ElementEffects::IsDirectImage()
{
    const auto revision = element->GetStyle()->GetComputedRevision();
    const bool capture = element->effect_capture_required;
    // The document resource schema is fixed before publication. Only computed
    // style and the host's capture requirement can change this policy afterward.
    if (!direct_image_initialized || revision != direct_image_revision || capture != direct_image_capture) {
        const bool next = DirectElementEffectImage(element);
        if (next != direct_image) effects_dirty = true;
        direct_image = next;
        direct_image_revision = revision;
        direct_image_capture = capture;
        direct_image_initialized = true;
    }
    return direct_image;
}

void ElementEffects::InstanceEffects() try
{
    IsDirectImage();
	if (!effects_dirty)
		return;

	effects_dirty = false;
	effects_data_dirty = true;

	URUI_ZoneScopedC(0xB22222);
	ReleaseEffects();

	RenderManager* render_manager = element->GetRenderManager();
	if (!render_manager)
	{
		URUI_ERRORMSG("Decorators are being instanced before a render manager is available. Is this element attached to the document?");
		return;
	}

	const ComputedValues& computed = element->GetComputedValues();
    if (computed.opacity() < 1.f)
        opacity_filter = render_manager->CompileFilter("opacity", Dictionary{{"value", Variant(computed.opacity())}},
            element->GetOwnerDocument() ? element->GetOwnerDocument()->GetResourceContext() : nullptr);

    const auto* effect_property = element->GetProperty(PropertyId::ElementEffect);
    const auto effect_name = effect_property ? effect_property->Get<String>() : String();
    if (!effect_name.empty() && effect_name != "none" && !direct_image) {
        element_effect = render_manager->CompileFilter("--urui-effect",
            Dictionary{{"value", Variant(effect_name)}, {"element", Variant(static_cast<void*>(element))}},
            element->GetOwnerDocument() ? element->GetOwnerDocument()->GetResourceContext() : nullptr);
    }

	if (computed.has_decorator() || computed.has_mask_image() || element->GetProperty(PropertyId::BackgroundImage))
	{
		const StyleSheet* style_sheet = element->GetStyleSheet();
		if (!style_sheet)
			return;

		for (const auto id : {PropertyId::Decorator, PropertyId::BackgroundImage, PropertyId::BorderImageSource, PropertyId::MaskImage, PropertyId::UruiMaskImage})
		{
			const Property* property = element->GetStyle()->GetLocalPropertyWithResolvedVariables(id);
			if (!property || property->unit != Unit::DECORATOR)
				continue;

			DecoratorsPtr decorators_ptr = property->Get<DecoratorsPtr>();
			if (!decorators_ptr)
				continue;

			PropertySource document_source("", 0, "");
			const PropertySource* source = property->source.get();

			if (!source)
			{
				if (ElementDocument* document = element->GetOwnerDocument())
				{
					document_source.path = document->GetSourceURL();
					source = &document_source;
				}
			}

			const DecoratorPtrList& decorator_list = style_sheet->InstanceDecorators(*render_manager, *decorators_ptr, source, element->GetOwnerDocument() ? element->GetOwnerDocument()->GetResourceContext() : nullptr);
			URUI_ASSERT(decorator_list.empty() || decorator_list.size() == decorators_ptr->list.size());

			DecoratorEntryList& decorators_target = (id == PropertyId::Decorator ? decorators : id == PropertyId::BackgroundImage ? background_images : id == PropertyId::BorderImageSource ? border_images : mask_images);
			decorators_target.reserve(decorators_ptr->list.size());

			for (size_t i = 0; i < decorator_list.size() && i < decorators_ptr->list.size(); i++)
			{
				const SharedPtr<const Decorator>& decorator = decorator_list[i];
				if (decorator)
				{
					DecoratorEntry entry;
					entry.decorator_data = 0;
					entry.decorator = decorator;
					entry.paint_area = decorators_ptr->list[i].paint_area;
					if (entry.paint_area == BoxArea::Auto)
						entry.paint_area = (id == PropertyId::Decorator ? BoxArea::Padding : BoxArea::Border);

					URUI_ASSERT(entry.paint_area >= BoxArea::Border && entry.paint_area <= BoxArea::Content);
					decorators_target.push_back(std::move(entry));
				}
			}
		}
	}

	if (computed.has_filter() || computed.has_backdrop_filter())
	{
		for (const auto id : {PropertyId::Filter, PropertyId::BackdropFilter})
		{
            if (id == PropertyId::Filter && DirectColorFilters(element)) continue;
			const Property* property = element->GetStyle()->GetLocalPropertyWithResolvedVariables(id);
			if (!property || property->unit != Unit::FILTER)
				continue;

			FiltersPtr filters_ptr = property->Get<FiltersPtr>();
			if (!filters_ptr)
				continue;

			FilterEntryList& list = (id == PropertyId::Filter ? filters : backdrop_filters);
			list.reserve(filters_ptr->list.size());

			for (const FilterDeclaration& declaration : filters_ptr->list)
			{
				SharedPtr<const Filter> filter = declaration.instancer->InstanceFilter(declaration.type, declaration.properties);
				if (filter)
				{
					list.push_back({std::move(filter), CompiledFilter{}});
				}
				else
				{
					const auto& source = property->source;
					Log::Message(Log::LT_WARNING, "Filter '%s' in '%s' could not be instanced, declared at %s:%d", declaration.type.c_str(),
						filters_ptr->value.c_str(), source ? source->path.c_str() : "", source ? source->line_number : -1);
				}
			}
		}
	}
}

catch (...)
{
	// Instancing may have left a partial list. Rebuild it on the next frame.
	effects_dirty = true;
	throw;
}

void ElementEffects::ReloadEffectsData() try
{
	if (effects_data_dirty)
	{
		effects_data_dirty = false;

		bool decorator_data_failed = false;
		for (DecoratorEntryList* list : {&decorators, &background_images, &border_images, &mask_images})
		{
			for (DecoratorEntry& decorator : *list)
			{
				const DecoratorDataHandle old_data = decorator.decorator_data;

				decorator.decorator_data = decorator.decorator->GenerateElementData(element, decorator.paint_area);
				if (!decorator.decorator_data)
					decorator_data_failed = true;

				// Release old element data after generating new data, so that the decorator can reuse any cache.
				if (old_data)
					decorator.decorator->ReleaseElementData(old_data);
			}
		}

		if (decorator_data_failed)
			Log::Message(Log::LT_WARNING, "Could not generate decorator element data: %s", element->GetAddress().c_str());

		bool filter_compile_failed = false;
		for (FilterEntryList* list : {&filters, &backdrop_filters})
		{
			for (FilterEntry& filter : *list)
			{
				filter.compiled = filter.filter->CompileFilter(element);
				if (!filter.compiled)
					filter_compile_failed = true;
			}
		}

		if (filter_compile_failed)
			Log::Message(Log::LT_WARNING, "Could not compile filter on element: %s", element->GetAddress().c_str());
	}
}

catch (...)
{
	// Keep already owned data, but do not publish a failed generation as clean.
	effects_data_dirty = true;
	throw;
}

void ElementEffects::ReleaseEffects()
{
	for (DecoratorEntryList* list : {&decorators, &background_images, &border_images, &mask_images})
	{
		for (DecoratorEntry& decorator : *list)
		{
			if (decorator.decorator_data)
				decorator.decorator->ReleaseElementData(decorator.decorator_data);
		}
		list->clear();
	}

	filters.clear();
	backdrop_filters.clear();
	opacity_filter = {};
	element_effect = {};
}

void ElementEffects::RenderEffects(RenderStage render_stage)
{
	InstanceEffects();
	ReloadEffectsData();

    if (render_stage == RenderStage::Background || render_stage == RenderStage::Border) {
        auto& images = render_stage == RenderStage::Background ? background_images : border_images;
        for (int i = int(images.size()) - 1; i >= 0; --i) {
            auto& image = images[i];
            if (image.decorator_data) image.decorator->RenderElement(element, image.decorator_data);
        }
        return;
    }
	if (!decorators.empty())
	{
		if (render_stage == RenderStage::Decoration)
		{
			// Render the decorators attached to this element in its current state.
			// Render from back to front for correct render order.
			for (int i = (int)decorators.size() - 1; i >= 0; i--)
			{
				DecoratorEntry& decorator = decorators[i];
				if (decorator.decorator_data)
					decorator.decorator->RenderElement(element, decorator.decorator_data);
			}
		}
	}

	if (filters.empty() && backdrop_filters.empty() && mask_images.empty() && !opacity_filter && !element_effect)
		return;

	RenderManager* render_manager = element->GetRenderManager();
	if (!render_manager)
		return;

	Rectanglei initial_scissor_region = render_manager->GetScissorRegion();

	auto ApplyClippingRegion = [this, &render_manager](PropertyId filter_id) {
		URUI_ASSERT(filter_id == PropertyId::Filter || filter_id == PropertyId::BackdropFilter);

		const bool force_clip_to_self_border_box = (filter_id == PropertyId::BackdropFilter);
		ElementUtilities::SetClippingRegion(element, force_clip_to_self_border_box);

        // URUI composites the recorded subtree ink region, including transformed
        // descendants and filter halos. Clipping to this element's own border
        // would discard overflowing children before that region can be used.
        // Preserve ancestor clipping here; the renderer bounds the actual quad.
        if (filter_id == PropertyId::Filter)
            return;

		// Find the region being affected by the active filters and apply it as a scissor.
		Rectanglef filter_region = Rectanglef::MakeInvalid();
		ElementUtilities::GetBoundingBox(filter_region, element, force_clip_to_self_border_box ? BoxArea::Border : BoxArea::Auto);

		// The filter property may draw outside our normal clipping region due to ink overflow.
		if (filter_id == PropertyId::Filter)
		{
			for (const auto& filter : filters)
				filter.filter->ExtendInkOverflow(element, filter_region);
		}

		Math::ExpandToPixelGrid(filter_region);

		Rectanglei scissor_region = Rectanglei(filter_region).IntersectIfValid(render_manager->GetScissorRegion());
		render_manager->SetScissorRegion(scissor_region);
	};
	auto ApplyScissorRegionForBackdrop = [this, &render_manager]() {
		// Set the scissor region for backdrop drawing, which covers the element's border box plus any area we may need
		// to read from, such as any blur radius.
		Rectanglef filter_region = Rectanglef::MakeInvalid();
		ElementUtilities::GetBoundingBox(filter_region, element, BoxArea::Border);
		for (const auto& filter : backdrop_filters)
			filter.filter->ExtendInkOverflow(element, filter_region);
		Math::ExpandToPixelGrid(filter_region);
		render_manager->SetScissorRegion(Rectanglei(filter_region));
	};

	if (render_stage == RenderStage::Enter)
	{
		const auto* scope = element->GetProperty(PropertyId::BackdropScope);
        const bool surface_scope = scope && scope->Get<int>() == 1;
        const LayerHandle backdrop_source_layer = render_manager->GetTopLayer();

		if (!filters.empty() || !mask_images.empty() || opacity_filter || element_effect)
		{
			render_manager->PushLayer();
		}

		if (element->IsVisible() && !backdrop_filters.empty())
		{
			const LayerHandle backdrop_destination_layer = render_manager->GetTopLayer();

			// @performance We strictly only need this temporary buffer when having to read from outside the element
			// boundaries, which currently only applies to blur and drop-shadow. Alternatively, we could avoid this
			// completely if we introduced a render interface API concept of different input and output clipping. That
			// is, we set a large input scissor to cover all input data, which can be used e.g. during blurring, and use
			// our small border-area-only clipping region for the composite layers output.
			ApplyScissorRegionForBackdrop();
			render_manager->PushLayer();
			const LayerHandle backdrop_temp_layer = render_manager->GetTopLayer();

			FilterHandleList filter_handles;
			for (auto& filter : backdrop_filters)
				filter.compiled.AddHandleTo(filter_handles);

			// Render the backdrop filters in the extended scissor region including any ink overflow.
			if (surface_scope) render_manager->CompositeSurfaceBackdrop(backdrop_temp_layer);
            else render_manager->CompositeLayers(backdrop_source_layer, backdrop_temp_layer, BlendMode::Replace, {});
            render_manager->CompositeLayers(backdrop_temp_layer, backdrop_temp_layer, BlendMode::Replace, filter_handles);

			// Then composite the filter output to our destination while applying our clipping region, including any border-radius.
			ApplyClippingRegion(PropertyId::BackdropFilter);
			render_manager->CompositeLayers(backdrop_temp_layer, backdrop_destination_layer, BlendMode::Blend, {});
			render_manager->PopLayer();
			render_manager->SetScissorRegion(initial_scissor_region);
		}
	}
	else if (render_stage == RenderStage::Exit)
	{
		if (!filters.empty() || !mask_images.empty() || opacity_filter || element_effect)
		{
			ApplyClippingRegion(PropertyId::Filter);

			CompiledFilter mask_image_filter;
			FilterHandleList filter_handles;
			filter_handles.reserve(filters.size() + (mask_images.empty() ? 0 : 1));
			element_effect.AddHandleTo(filter_handles);

			for (auto& filter : filters)
				filter.compiled.AddHandleTo(filter_handles);

			if (!mask_images.empty())
			{
				render_manager->PushLayer();

				for (int i = (int)mask_images.size() - 1; i >= 0; i--)
				{
					DecoratorEntry& mask_image = mask_images[i];
					if (mask_image.decorator_data)
						mask_image.decorator->RenderElement(element, mask_image.decorator_data);
				}
				mask_image_filter = render_manager->SaveLayerAsMaskImage();
				mask_image_filter.AddHandleTo(filter_handles);
				render_manager->PopLayer();
			}

            opacity_filter.AddHandleTo(filter_handles);
			render_manager->CompositeLayers(render_manager->GetTopLayer(), render_manager->GetNextLayer(), BlendMode::Blend, filter_handles);
			render_manager->PopLayer();
			render_manager->SetScissorRegion(initial_scissor_region);
		}
	}
}

void ElementEffects::DirtyEffects()
{
	effects_dirty = true;
}

void ElementEffects::DirtyEffectsData()
{
	effects_data_dirty = true;
}

} // namespace URUI
