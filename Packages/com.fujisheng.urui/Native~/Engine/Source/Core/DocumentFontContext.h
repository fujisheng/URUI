#pragma once

#include "../../Include/URUI/Core/StyleTypes.h"
#include "../../Include/URUI/Core/Types.h"

namespace URUI
{
    struct FontFaceRule;
    class ResourceContext;

    // CSS family selection belongs to a document. Only immutable source instances
    // are interned in the engine font cache; its keys never contain CSS family names.
    class DocumentFontContext
    {
    public:
        static void InitializeSourceCache();
        static void ShutdownSourceCache();
        static bool LoadDefaultFace(int family, Span<const byte> bytes, Style::FontStyle style,
                                    Style::FontWeight weight, int face_index);
        void SetResourceContext(SharedPtr<ResourceContext> resources)
        {
            resource_context = std::move(resources);
        }
        bool SetRules(const Vector<FontFaceRule>& rules);
        const String& GetFailedSource() const
        {
            return failed_source;
        }
        uint64_t GetRevision() const
        {
            return revision;
        }
        bool LoadFace(const String& path, const String& family, Style::FontStyle style, Style::FontWeight weight,
                      int face_index);
        FontFaceHandle ResolveFace(const String& families, Style::FontStyle style, Style::FontWeight weight,
                                   float size) const;

    private:
        struct Face
        {
            String family;
            String source;
            Style::FontStyle style;
            Style::FontWeight weight;
            bool operator==(const Face& other) const
            {
                return family == other.family && source == other.source && style == other.style &&
                       weight == other.weight;
            }
        };
        Vector<Face> faces;
        uint64_t revision = 0;
        SharedPtr<ResourceContext> resource_context;
        String failed_source;
    };

}
