#pragma once

#include "../../Include/URUI/Core/Types.h"
#include <URUI/Core/CascadePriority.h>

namespace URUI {

/**
    The document header struct contains the
    metadata gathered from the strict HTML document.
 */

class DocumentHeader {
public:
	/// Path and filename this document was loaded from
	String source;
	/// The title of the document
	String title;

	struct Resource {
		String path;    // Content path for inline resources, source path for external resources.
		String content; // Only set for inline resources.
		bool is_inline = false;
		int line = 0;
		CssOrigin origin = CssOrigin::Author;   // Only set for inline resources.
	};
	using ResourceList = Vector<Resource>;

	/// CSS definitions
	ResourceList css;

};

} // namespace URUI
