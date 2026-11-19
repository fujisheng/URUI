#pragma once

#include "../../Include/URUI/Core/Types.h"
#include <URUI/Core/CascadePriority.h>

namespace URUI
{

    /**
    The document header struct contains the
    metadata gathered from the strict HTML document.
 */

    class DocumentHeader
    {
    public:
        /// Path and filename this document was loaded from
        String source;
        /// The title of the document
        String title;

        struct Resource
        {
            // Content path for inline resources, source path for external resources.
            String path;
            // Only set for inline resources.
            String content;
            bool is_inline = false;
            int line = 0;
            // Only set for inline resources.
            CssOrigin origin = CssOrigin::Author;
        };
        using ResourceList = Vector<Resource>;

        /// CSS definitions
        ResourceList css;
    };

}
