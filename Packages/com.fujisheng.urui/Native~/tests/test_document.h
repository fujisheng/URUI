#pragma once
#include "urui.h"
#include <cassert>
#include <string>
#include <vector>

struct TestSurface
{
    URUISurfaceHandle handle = 0;

    TestSurface() = default;
    TestSurface(int width, int height)
    {
        assert(urui_surface_create(width, height, &handle) == URUI_OK && handle != 0);
    }
    ~TestSurface()
    {
        Destroy();
    }
    TestSurface(const TestSurface&) = delete;
    TestSurface& operator=(const TestSurface&) = delete;
    TestSurface(TestSurface&& other) noexcept : handle(other.handle)
    {
        other.handle = 0;
    }
    TestSurface& operator=(TestSurface&& other) noexcept
    {
        if (this != &other)
        {
            Destroy();
            handle = other.handle;
            other.handle = 0;
        }
        return *this;
    }
    void Create(int width, int height)
    {
        Destroy();
        assert(urui_surface_create(width, height, &handle) == URUI_OK && handle != 0);
    }
    void Destroy()
    {
        if (handle)
        {
            urui_surface_destroy(handle);
            handle = 0;
        }
    }
    operator URUISurfaceHandle() const noexcept
    {
        return handle;
    }
};

inline URUIDocumentHandle LoadTestDocument(URUISurfaceHandle surface, const char* html, const char* source)
{
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(surface, &owner) == URUI_OK && owner);
    assert(urui_document_set_presentation(surface, owner, 0, 0, 1, 0) == URUI_OK);
    assert(urui_document_load_html(surface, owner, html, source) == URUI_OK);
    return owner;
}

inline std::string TestLastError(URUISurfaceHandle surface)
{
    int required = 0;
    const int probe = surface ? urui_surface_last_error(surface, nullptr, 0, &required)
                              : urui_last_error_copy(nullptr, 0, &required);
    if (probe != URUI_OK && probe != URUI_ERR_BUFFER_TOO_SMALL)
    {
        return {};
    }
    if (required <= 1)
    {
        return {};
    }
    std::string out(static_cast<size_t>(required), '\0');
    const int status = surface ? urui_surface_last_error(surface, out.data(), required, &required)
                               : urui_last_error_copy(out.data(), required, &required);
    if (status != URUI_OK)
    {
        return {};
    }
    if (!out.empty() && out.back() == '\0')
    {
        out.pop_back();
    }
    return out;
}

inline uint64_t FindTestElement(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id)
{
    const uint64_t root = urui_document_root(surface, owner);
    if (!root || !id || !*id)
    {
        return 0;
    }
    const uint64_t tok = urui_element_find(surface, owner, root, id, nullptr);
    return tok == UINT64_MAX ? 0 : tok;
}

inline bool TestElementExists(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id)
{
    return FindTestElement(surface, owner, id) != 0;
}

inline int TestSetStyle(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id, const char* prop,
                        const char* value)
{
    const uint64_t tok = FindTestElement(surface, owner, id);
    if (!tok)
    {
        return URUI_ABSENT;
    }
    return urui_element_write(surface, owner, tok, 4, prop, value);
}

inline int TestSetAttribute(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id, const char* name,
                            const char* value)
{
    const uint64_t tok = FindTestElement(surface, owner, id);
    if (!tok)
    {
        return URUI_ABSENT;
    }
    return urui_element_write(surface, owner, tok, 2, name, value);
}

inline int TestGetAttribute(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id, const char* name,
                            char* out, int capacity)
{
    const uint64_t tok = FindTestElement(surface, owner, id);
    if (!tok)
    {
        return URUI_ABSENT;
    }
    int required = 0;
    const int status = urui_element_read(surface, owner, tok, 3, name, out, capacity, &required);
    if (status == URUI_OK)
    {
        return required > 0 ? required - 1 : 0;
    }
    return status;
}

inline int TestGetElementBox(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id, float* out)
{
    const uint64_t tok = FindTestElement(surface, owner, id);
    if (!tok || !out)
    {
        return URUI_ABSENT;
    }
    float box[16]{};
    const int status = urui_element_box(surface, owner, tok, box, 16);
    if (status != URUI_OK)
    {
        return status;
    }
    out[0] = box[0];
    out[1] = box[1];
    out[2] = box[2];
    out[3] = box[3];
    return URUI_OK;
}
