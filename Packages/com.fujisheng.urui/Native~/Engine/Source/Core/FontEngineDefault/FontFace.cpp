#include "FontFace.h"
#include "../../../Include/URUI/Core/Log.h"
#include "FontFaceHandleDefault.h"
#include "FreeTypeInterface.h"
#include <cmath>
#include <limits>

namespace URUI
{

    FontFace::FontFace(FontFaceHandleFreetype _face, Style::FontStyle _style, Style::FontWeight _weight)
    {
        style = _style;
        weight = _weight;
        face = _face;
    }

    FontFace::~FontFace()
    {
        if (face)
        {
            FreeType::ReleaseFace(face);
        }
    }

    Style::FontStyle FontFace::GetStyle() const
    {
        return style;
    }

    Style::FontWeight FontFace::GetWeight() const
    {
        return weight;
    }

    FontFaceHandleDefault* FontFace::GetHandle(float size, bool load_default_glyphs)
    {
        const double fixed_size = std::round(double(size) * 64.0);
        if (!std::isfinite(fixed_size) || fixed_size < 0 || fixed_size > std::numeric_limits<int>::max())
        {
            return nullptr;
        }
        const int key = int(fixed_size);
        size = float(key) / 64.f;
        auto it = handles.find(key);
        if (it != handles.end())
        {
            return it->second.get();
        }

        // See if this face has been released.
        if (!face)
        {
            Log::Message(Log::LT_WARNING, "Font face has been released, unable to generate new handle.");
            return nullptr;
        }

        // Construct and initialise the new handle.
        auto handle = MakeUnique<FontFaceHandleDefault>();
        if (!handle->Initialize(face, size, load_default_glyphs))
        {
            handles[key] = nullptr;
            return nullptr;
        }

        FontFaceHandleDefault* result = handle.get();

        // Save the new handle to the font face
        handles[key] = std::move(handle);

        return result;
    }

    void FontFace::ReleaseFontResources()
    {
        HandleMap().swap(handles);
    }

}
