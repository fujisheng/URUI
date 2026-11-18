#pragma once

#include "StyleSheetTypes.h"
#include "CascadePriority.h"
#include "Traits.h"

namespace URUI {

class Stream;
class StyleSheet;
class PropertySpecification;
struct Diagnostic;

/**
    StyleSheetContainer contains a list of media blocks and creates a combined style sheet when getting
    properties of the current context regarding the available media features.
 */

class URUI_CORE_API StyleSheetContainer : public NonCopyMoveable {
public:
	StyleSheetContainer();
	virtual ~StyleSheetContainer();

	/// Loads a style from a CSS definition.
	bool LoadStyleSheetContainer(Stream* stream, int begin_line_number = 1, ElementDocument* document = nullptr,
		Vector<Diagnostic>* diagnostics = nullptr);

	/// Compiles a single style sheet by combining all contained style sheets whose media queries match the current state of the context.
	/// @param[in] context The current context used for evaluating media query parameters against.
	/// @returns True when the compiled style sheet was changed, otherwise false.
	/// @warning This operation invalidates all references to the previously compiled style sheet.
	bool UpdateCompiledStyleSheet(const Context* context);

	/// Returns the previously compiled style sheet.
	StyleSheet* GetCompiledStyleSheet();

	/// Combines this style sheet container with another one, producing a new sheet container.
	SharedPtr<StyleSheetContainer> CombineStyleSheetContainer(const StyleSheetContainer& container) const;

	/// Merge another style sheet container into this.
	void MergeStyleSheetContainer(const StyleSheetContainer& container);

	void SetOrigin(CssOrigin origin);
	const String& GetResourceError() const { return resource_error; }

private:
	friend class CompiledArchive;
	String resource_error;
	Vector<SharedPtr<PropertySpecification>> property_specifications;
	MediaBlockList media_blocks;

	StyleSheet* compiled_style_sheet = nullptr;
	UniquePtr<StyleSheet> combined_compiled_style_sheet;
	Vector<int> active_media_block_indices;
};

} // namespace URUI
