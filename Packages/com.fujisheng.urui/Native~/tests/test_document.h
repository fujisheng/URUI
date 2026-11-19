#pragma once
#include "urui.h"
#include <cassert>

inline URUIDocumentHandle LoadTestDocument(const char* html, const char* source)
{
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1 && owner);
    assert(urui_document_set_presentation(owner, 0, 0, 1, 0) == 1);
    assert(urui_document_load_html(owner, html, source) == 1);
    return owner;
}
