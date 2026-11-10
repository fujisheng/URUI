#include "LayoutEngine.h"
#include "../../../Include/URUI/Core/Element.h"
#include "../../../Include/URUI/Core/ElementDocument.h"
#include "../../../Include/URUI/Core/Log.h"
#include "../../../Include/URUI/Core/Profiling.h"
#include "ContainerBox.h"
#include "FormattingContext.h"

namespace URUI {

void LayoutEngine::FormatElement(Element* element, Vector2f containing_block)
{
	URUI_ASSERT(element && containing_block.x >= 0 && containing_block.y >= 0);

	RootBox root(containing_block, element->GetOwnerDocument() == element ? element->GetParentNode() : nullptr);

	auto layout_box = FormattingContext::FormatIndependent(&root, element, nullptr, FormattingContextType::Block);
	if (!layout_box)
	{
		Log::Message(Log::LT_ERROR, "Error while formatting element: %s", element->GetAddress().c_str());
	}
	// A static HTML document leaves viewport-positioned descendants on the root
	// box. They still require formatting after normal document flow is complete.
	root.FinishLayout();

	{
		URUI_ZoneScopedN("ClampScrollOffsetRecursive");
		// The size of the scrollable area might have changed, so clamp the scroll offset to avoid scrolling outside the
		// scrollable area. During layouting, we might be changing the scrollable overflow area of the element several
		// times, such as after enabling scrollbars. For this reason, we don't clamp the scroll offset during layouting,
		// as that could inadvertently clamp it to a temporary size. Now that we know the final layout, including the
		// size of each element's scrollable area, we can finally clamp the scroll offset.
		element->ClampScrollOffsetRecursive();
	}
}

} // namespace URUI
