#pragma once
#include "HtmlDocument.h"

namespace URUI
{

    HtmlParseResult ParseHtmlDocument(const String& html, const String& sourceUrl);
    HtmlParseResult ParseHtmlFragment(const String& html, const String& contextTag, const String& sourceUrl);

#if defined(URUI_HTML_PARSER_TESTING)
    namespace HtmlParserTesting
    {
        HtmlParseResult ParseWithAllocationLimit(const String& html, const String& sourceUrl, size_t allocationLimit);
        size_t OutstandingAllocations();
    }
#endif

}
