#include "../../Include/URUI/Core/StyleSheetContainer.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/PropertyDictionary.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "../../Include/URUI/Core/Utilities.h"
#include "ComputeProperty.h"
#include "StyleSheetParser.h"
#include "DocumentFontContext.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"

namespace URUI
{

    StyleSheetContainer::StyleSheetContainer()
    {
    }

    StyleSheetContainer::~StyleSheetContainer()
    {
    }

    bool StyleSheetContainer::LoadStyleSheetContainer(Stream* stream, int begin_line_number, ElementDocument* document,
                                                      Vector<Diagnostic>* diagnostics)
    {
        resource_error.clear();
        SharedPtr<PropertySpecification> specification =
            document ? document->GetStyleSpecificationStorage()
                     : StyleSheetSpecification::GetSharedPropertySpecification();
        StyleSheetParser parser(specification.get(), diagnostics,
                                [&]() -> PropertySpecification&
                                {
                                    if (document)
                                    {
                                        specification = document->EnsureMutableStyleSpecificationStorage();
                                    }
                                    else
                                    {
                                        specification = SharedPtr<PropertySpecification>(specification->Clone());
                                    }
                                    return *specification;
                                });
        bool result = parser.Parse(media_blocks, stream, begin_line_number,
                                   [&]
                                   {
                                       if (!document || !document->GetContext())
                                       {
                                           return true;
                                       }
                                       // Validate font dependencies while @property changes can still roll
                                       // back. Neither the active font context nor its stylesheet is changed.
                                       UpdateCompiledStyleSheet(document->GetContext());
                                       const auto* sheet = GetCompiledStyleSheet();
                                       DocumentFontContext candidate;
                                       candidate.SetResourceContext(document->GetResourceContext());
                                       if (sheet && !candidate.SetRules(sheet->GetFontFaceRules()))
                                       {
                                           resource_error =
                                               "Font face could not be loaded: " + candidate.GetFailedSource();
                                           return false;
                                       }
                                       return true;
                                   });
        property_specifications.push_back(std::move(specification));
        return result;
    }

    void StyleSheetContainer::SetOrigin(CssOrigin origin)
    {
        for (auto& block : media_blocks)
        {
            block.stylesheet->SetOrigin(origin);
        }
        compiled_style_sheet = nullptr;
        combined_compiled_style_sheet.reset();
        active_media_block_indices.clear();
    }

    bool StyleSheetContainer::UpdateCompiledStyleSheet(const Context* context)
    {
        URUI_ZoneScoped;

        const float dp_ratio = context->GetDensityIndependentPixelRatio();
        const Vector2i vp_dimensions_i(context->GetDimensions());
        const Vector2f vp_dimensions(vp_dimensions_i);

        Vector<int> new_active_media_block_indices;

        const float font_size = DefaultComputedValues().font_size();

        for (int media_block_index = 0; media_block_index < (int)media_blocks.size(); media_block_index++)
        {
            const MediaBlock& media_block = media_blocks[media_block_index];
            bool all_match = true;
            bool expected_match_value = media_block.modifier == MediaQueryModifier::Not ? false : true;

            for (const auto& property : media_block.properties.GetProperties())
            {
                const MediaQueryId id = static_cast<MediaQueryId>(property.first);
                Vector2i ratio;

                switch (id)
                {
                case MediaQueryId::Width:
                    if (vp_dimensions.x !=
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MinWidth:
                    if (vp_dimensions.x <
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MaxWidth:
                    if (vp_dimensions.x >
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::Height:
                    if (vp_dimensions.y !=
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MinHeight:
                    if (vp_dimensions.y <
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MaxHeight:
                    if (vp_dimensions.y >
                        ComputeLength(property.second.GetNumericValue(), font_size, font_size, dp_ratio, vp_dimensions))
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::AspectRatio:
                    ratio = Vector2i(property.second.Get<Vector2f>());
                    if (vp_dimensions_i.x * ratio.y != vp_dimensions_i.y * ratio.x)
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MinAspectRatio:
                    ratio = Vector2i(property.second.Get<Vector2f>());
                    if (vp_dimensions_i.x * ratio.y < vp_dimensions_i.y * ratio.x)
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MaxAspectRatio:
                    ratio = Vector2i(property.second.Get<Vector2f>());
                    if (vp_dimensions_i.x * ratio.y > vp_dimensions_i.y * ratio.x)
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::Resolution:
                    if (dp_ratio != property.second.Get<float>())
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MinResolution:
                    if (dp_ratio < property.second.Get<float>())
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::MaxResolution:
                    if (dp_ratio > property.second.Get<float>())
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::Orientation:
                    // Landscape (x > y) = 0
                    // Portrait (x <= y) = 1
                    if ((vp_dimensions.x <= vp_dimensions.y) != property.second.Get<bool>())
                    {
                        all_match = false;
                    }
                    break;
                case MediaQueryId::Theme:
                    if (!context->IsThemeActive(property.second.Get<String>()))
                    {
                        all_match = false;
                    }
                    break;
                    // Invalid properties
                case MediaQueryId::Invalid:
                case MediaQueryId::NumDefinedIds:
                    break;
                }

                if (all_match != expected_match_value)
                {
                    break;
                }
            }

            if (all_match == expected_match_value)
            {
                new_active_media_block_indices.push_back(media_block_index);
            }
        }

        const bool style_sheet_changed =
            (new_active_media_block_indices != active_media_block_indices || !compiled_style_sheet);

        if (style_sheet_changed)
        {
            StyleSheet* first_sheet = nullptr;
            UniquePtr<StyleSheet> new_sheet;

            for (int index : new_active_media_block_indices)
            {
                MediaBlock& media_block = media_blocks[index];
                if (!first_sheet)
                {
                    first_sheet = media_block.stylesheet.get();
                }
                else if (!new_sheet)
                {
                    new_sheet = first_sheet->CombineStyleSheet(*media_block.stylesheet);
                }
                else
                {
                    new_sheet->MergeStyleSheet(*media_block.stylesheet);
                }
            }

            if (!first_sheet)
            {
                new_sheet.reset(new StyleSheet);
                first_sheet = new_sheet.get();
            }

            compiled_style_sheet = (new_sheet ? new_sheet.get() : first_sheet);
            combined_compiled_style_sheet = std::move(new_sheet);

            compiled_style_sheet->BuildNodeIndex();
        }

        active_media_block_indices = std::move(new_active_media_block_indices);

        return style_sheet_changed;
    }

    StyleSheet* StyleSheetContainer::GetCompiledStyleSheet()
    {
        return compiled_style_sheet;
    }

    SharedPtr<StyleSheetContainer> StyleSheetContainer::CloneMediaShell() const
    {
        SharedPtr<StyleSheetContainer> clone = MakeShared<StyleSheetContainer>();
        clone->property_specifications = property_specifications;
        clone->media_blocks.reserve(media_blocks.size());
        for (const MediaBlock& media_block : media_blocks)
        {
            clone->media_blocks.emplace_back(media_block.properties, media_block.stylesheet, media_block.modifier);
        }
        return clone;
    }

    SharedPtr<StyleSheetContainer>
    StyleSheetContainer::CombineStyleSheetContainer(const StyleSheetContainer& container) const
    {
        URUI_ZoneScoped;

        SharedPtr<StyleSheetContainer> new_sheet = MakeShared<StyleSheetContainer>();
        new_sheet->property_specifications = property_specifications;

        for (const MediaBlock& media_block : media_blocks)
        {
            new_sheet->media_blocks.emplace_back(media_block.properties, media_block.stylesheet, media_block.modifier);
        }

        new_sheet->MergeStyleSheetContainer(container);

        return new_sheet;
    }

    void StyleSheetContainer::MergeStyleSheetContainer(const StyleSheetContainer& other)
    {
        URUI_ZoneScoped;
        for (const auto& specification : other.property_specifications)
        {
            if (std::find(property_specifications.begin(), property_specifications.end(), specification) ==
                property_specifications.end())
            {
                property_specifications.push_back(specification);
            }
        }

        // Style sheet container must not be merged after it's been compiled. This will invalidate references to the compiled style sheet.
        URUI_ASSERT(!compiled_style_sheet);

        auto it_other_begin = other.media_blocks.begin();

#if 0
	// If the last block here has the same media requirements as the first block in other, we can safely merge them
	// while retaining correct specificity of all properties. This is essentially an optimization to avoid more
	// style sheet merging later on.
	if (!media_blocks.empty() && !other.media_blocks.empty())
	{
		MediaBlock& block_local = media_blocks.back();
		const MediaBlock& block_other = other.media_blocks.front();
		if (block_local.properties.GetProperties() == block_other.properties.GetProperties())
		{
			// Now we can safely merge the two style sheets.
			block_local.stylesheet = block_local.stylesheet->CombineStyleSheet(*block_other.stylesheet);

			// And we need to skip the first media block in the 'other' style sheet, since we merged it just now.
			++it_other_begin;
		}
	}
#endif

        // Add all the other blocks into ours.
        for (auto it = it_other_begin; it != other.media_blocks.end(); ++it)
        {
            const MediaBlock& block_other = *it;
            media_blocks.emplace_back(block_other.properties, block_other.stylesheet, block_other.modifier);
        }
    }

}
