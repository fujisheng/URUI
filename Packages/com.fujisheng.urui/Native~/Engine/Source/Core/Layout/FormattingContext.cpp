#include "FormattingContext.h"
#include "../../../Include/URUI/Core/ComputedValues.h"
#include "../../../Include/URUI/Core/Element.h"
#include "../../../Include/URUI/Core/ElementText.h"
#include "../../../Include/URUI/Core/Core.h"
#include "../../../Include/URUI/Core/FontEngineInterface.h"
#include "../../../Include/URUI/Core/FontMetrics.h"
#include "../../../Include/URUI/Core/Profiling.h"
#include "BlockFormattingContext.h"
#include "FlexFormattingContext.h"
#include "LayoutBox.h"
#include "ReplacedFormattingContext.h"
#include "TableFormattingContext.h"
#include "LayoutDetails.h"
#include <cfloat>

namespace URUI {

namespace {
class TextLayoutBox final : public ReplacedBox {
public:
    explicit TextLayoutBox(Element* element) : ReplacedBox(element) {}
    float baseline = 0;
    float natural_width = 0;
    bool has_line = false;
    bool GetBaselineOfLastLine(float& result) const override { result = baseline; return has_line; }
    float GetShrinkToFitWidth(bool) const override { return natural_width; }
    void CloseText(Vector2f overflow) { Close(); SetVisibleOverflowSize(overflow); }
};

UniquePtr<LayoutBox> FormatText(ContainerBox* parent, ElementText* text, const Box* initial)
{
    // A flex text run owns an anonymous layout box, without introducing a DOM wrapper or cloning the text.
    auto result = MakeUnique<TextLayoutBox>(text);
    auto& box = result->GetBox();
    if (initial) box = *initial;
    else LayoutDetails::BuildBox(box, LayoutDetails::GetContainingBlock(parent, text->GetPosition()).size, text);
    text->ClearLines();
    auto size = box.GetSize();
    const float maximum_width = size.x < 0 ? FLT_MAX : Math::Max(size.x, 0.f);
    float natural_width = 0, height = 0;
    if (text->GetFontFaceHandle()) {
        const auto& metrics = GetFontEngineInterface()->GetFontMetrics(text->GetFontFaceHandle());
        const float line_height = text->GetComputedValues().line_height().value;
        const float first_baseline = metrics.ascent + (line_height - metrics.ascent - metrics.descent) * .5f;
        int begin = 0;
        while (begin < int(text->GetText().size())) {
            String line; int consumed = 0; float width = 0;
            const bool last = text->GenerateLine(line, consumed, width, begin, maximum_width, 0, true, false, false);
            if (!consumed) break;
            text->AddLine({0, height + first_baseline}, std::move(line));
            result->baseline = height + first_baseline; result->has_line = true;
            natural_width = Math::Max(natural_width, width); height += line_height; begin += consumed;
            if (last) break;
        }
    }
    if (size.x < 0) size.x = natural_width;
    if (size.y < 0) size.y = height;
    result->natural_width = natural_width;
    box.SetContent(size); result->CloseText(Math::Max(size, Vector2f(natural_width, height)));
    return result;
}
}

UniquePtr<LayoutBox> FormattingContext::FormatIndependent(ContainerBox* parent_container, Element* element, const Box* override_initial_box,
	FormattingContextType backup_context)
{
	URUI_ZoneScopedC(0xAFAFAF);
	using namespace Style;
	if (auto* text = urui_dynamic_cast<ElementText*>(element)) return FormatText(parent_container, text, override_initial_box);

	if (element->IsReplaced())
		return ReplacedFormattingContext::Format(parent_container, element, override_initial_box);

	FormattingContextType type = backup_context;

	auto& computed = element->GetComputedValues();
	const Display display = computed.display();
	if (display == Display::Flex || display == Display::InlineFlex)
	{
		type = FormattingContextType::Flex;
	}
	else if (display == Display::Table || display == Display::InlineTable)
	{
		type = FormattingContextType::Table;
	}
	else if (display == Display::InlineBlock || display == Display::FlowRoot || display == Display::TableCell || computed.float_() != Float::None ||
		computed.position() == Position::Absolute || computed.position() == Position::Fixed || computed.overflow_x() != Overflow::Visible ||
		computed.overflow_y() != Overflow::Visible || !element->GetParentNode() || element->GetParentNode()->GetDisplay() == Display::Flex)
	{
		type = FormattingContextType::Block;
	}

	switch (type)
	{
	case FormattingContextType::Block: return BlockFormattingContext::Format(parent_container, element, override_initial_box);
	case FormattingContextType::Table: return TableFormattingContext::Format(parent_container, element, override_initial_box);
	case FormattingContextType::Flex: return FlexFormattingContext::Format(parent_container, element, override_initial_box);
	case FormattingContextType::None: break;
	}

	return nullptr;
}

} // namespace URUI
