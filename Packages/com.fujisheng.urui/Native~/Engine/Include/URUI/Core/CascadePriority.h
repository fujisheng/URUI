#pragma once
#include <cstdint>
#include <tuple>

namespace URUI
{
    enum class CssOrigin
    {
        UserAgent,
        Author
    };
    // Combined origin/importance rank, ordered from weakest to strongest.
    enum class CssOriginImportance : uint8_t
    {
        UserAgentNormal,
        AuthorNormal,
        Animation,
        AuthorImportant,
        UserAgentImportant,
        Transition
    };
    struct Specificity
    {
        uint32_t ids = 0;
        uint32_t classes = 0;
        uint32_t types = 0;
        Specificity& operator+=(const Specificity& other)
        {
            ids += other.ids;
            classes += other.classes;
            types += other.types;
            return *this;
        }
        friend bool operator<(const Specificity& a, const Specificity& b)
        {
            return std::tie(a.ids, a.classes, a.types) < std::tie(b.ids, b.classes, b.types);
        }
        friend bool operator==(const Specificity& a, const Specificity& b)
        {
            return std::tie(a.ids, a.classes, a.types) == std::tie(b.ids, b.classes, b.types);
        }
    };
    struct CascadePriority
    {
        CssOriginImportance originImportance = CssOriginImportance::AuthorNormal;
        bool presentationalHint = false;
        bool inlineStyle = false;
        Specificity specificity;
        uint64_t sourceOrder = 0;
        bool IsImportant() const
        {
            return originImportance == CssOriginImportance::AuthorImportant ||
                   originImportance == CssOriginImportance::UserAgentImportant;
        }
        void SetOrigin(CssOrigin origin)
        {
            originImportance =
                origin == CssOrigin::UserAgent
                    ? (IsImportant() ? CssOriginImportance::UserAgentImportant : CssOriginImportance::UserAgentNormal)
                    : (IsImportant() ? CssOriginImportance::AuthorImportant : CssOriginImportance::AuthorNormal);
        }
        friend bool operator<(const CascadePriority& a, const CascadePriority& b)
        {
            return std::make_tuple(a.originImportance, !a.presentationalHint, a.inlineStyle, a.specificity,
                                   a.sourceOrder) < std::make_tuple(b.originImportance, !b.presentationalHint,
                                                                    b.inlineStyle, b.specificity, b.sourceOrder);
        }
        friend bool operator==(const CascadePriority& a, const CascadePriority& b)
        {
            return std::tie(a.originImportance, a.presentationalHint, a.inlineStyle, a.specificity, a.sourceOrder) ==
                   std::tie(b.originImportance, b.presentationalHint, b.inlineStyle, b.specificity, b.sourceOrder);
        }
    };
}
