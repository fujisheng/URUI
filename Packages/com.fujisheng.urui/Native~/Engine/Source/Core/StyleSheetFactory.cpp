#include "CssTokenizer.h"
#include <charconv>
#include "StyleSheetFactory.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/StyleSheetContainer.h"
#include "StreamFile.h"
#include "StyleSheetNode.h"
#include "StyleSheetParser.h"
#include "StyleSheetSelector.h"

namespace URUI
{

    static UniquePtr<StyleSheetFactory> instance;

    StyleSheetFactory::StyleSheetFactory()
        : selectors{
              {"nth-child", StructuralSelectorType::Nth_Child},
              {"nth-last-child", StructuralSelectorType::Nth_Last_Child},
              {"nth-of-type", StructuralSelectorType::Nth_Of_Type},
              {"nth-last-of-type", StructuralSelectorType::Nth_Last_Of_Type},
              {"first-child", StructuralSelectorType::First_Child},
              {"last-child", StructuralSelectorType::Last_Child},
              {"first-of-type", StructuralSelectorType::First_Of_Type},
              {"last-of-type", StructuralSelectorType::Last_Of_Type},
              {"only-child", StructuralSelectorType::Only_Child},
              {"only-of-type", StructuralSelectorType::Only_Of_Type},
              {"empty", StructuralSelectorType::Empty},
              {"not", StructuralSelectorType::Not},
              {"scope", StructuralSelectorType::Scope},
          }
    {
    }

    StyleSheetFactory::~StyleSheetFactory()
    {
    }

    bool StyleSheetFactory::Initialise()
    {
        URUI_ASSERT(instance == nullptr);
        instance = UniquePtr<StyleSheetFactory>(new StyleSheetFactory);
        return true;
    }

    void StyleSheetFactory::Shutdown()
    {
        instance.reset();
    }

    const StyleSheetContainer* StyleSheetFactory::GetStyleSheetContainer(const String& sheet_name)
    {
        // Look up the sheet definition in the cache
        auto it = instance->stylesheets.find(sheet_name);
        if (it != instance->stylesheets.end())
        {
            return it->second.get();
        }

        // Don't currently have the sheet, attempt to load it
        UniquePtr<const StyleSheetContainer> sheet = instance->LoadStyleSheetContainer(sheet_name);
        if (!sheet)
        {
            return nullptr;
        }

        const StyleSheetContainer* result = sheet.get();

        // Add it to the cache.
        instance->stylesheets[sheet_name] = std::move(sheet);

        return result;
    }

    void StyleSheetFactory::ClearStyleSheetCache()
    {
        instance->stylesheets.clear();
    }

    StructuralSelector StyleSheetFactory::GetSelector(const String& name)
    {
        SelectorMap::const_iterator it;
        const size_t parameter_start = name.find('(');

        if (parameter_start == String::npos)
        {
            it = instance->selectors.find(AsciiLowerCss(name));
        }
        else
        {
            it = instance->selectors.find(AsciiLowerCss(name.substr(0, parameter_start)));
        }

        if (it == instance->selectors.end())
        {
            return StructuralSelector(StructuralSelectorType::Invalid, 0, 0);
        }

        const StructuralSelectorType selector_type = it->second;

        bool requires_parameter = false;
        switch (selector_type)
        {
        case StructuralSelectorType::Nth_Child:
        case StructuralSelectorType::Nth_Last_Child:
        case StructuralSelectorType::Nth_Of_Type:
        case StructuralSelectorType::Nth_Last_Of_Type:
        case StructuralSelectorType::Not:
            requires_parameter = true;
            break;
        default:
            break;
        }

        const size_t parameter_end = name.rfind(')');
        const bool has_parameter =
            (parameter_start != String::npos && parameter_end != String::npos && parameter_start < parameter_end);

        if (requires_parameter != has_parameter || (has_parameter && parameter_end + 1 != name.size()))
        {
            Log::Message(Log::LT_WARNING, "Invalid selector ':%s' encountered, expected %s parameters", name.c_str(),
                         requires_parameter ? "parenthesized" : "no");
            return StructuralSelector(StructuralSelectorType::Invalid, 0, 0);
        }

        // Parse the 'a' and 'b' values.
        int a = 1;
        int b = 0;

        if (has_parameter)
        {
            String parameters = StringUtilities::StripWhitespace(
                StringView(name, parameter_start + 1, parameter_end - (parameter_start + 1)));

            if (selector_type == StructuralSelectorType::Not)
            {
                auto list = MakeShared<SelectorTree>();
                list->root = MakeUnique<StyleSheetNode>();
                list->leafs = StyleSheetParser::ConstructNodes(*list->root, parameters);
                if (list->leafs.empty())
                {
                    return StructuralSelector(StructuralSelectorType::Invalid, 0, 0);
                }

                Specificity specificity;
                for (const StyleSheetNode* node : list->leafs)
                {
                    if (specificity < node->GetSpecificity())
                    {
                        specificity = node->GetSpecificity();
                    }
                }

                return StructuralSelector(selector_type, std::move(list), specificity);
            }

            parameters = AsciiLowerCss(std::move(parameters));
            auto integer = [](String text, int& value)
            {
                if (!text.empty() && text.front() == '+')
                {
                    text.erase(0, 1);
                }
                if (text.empty())
                {
                    return false;
                }
                const auto result = std::from_chars(text.data(), text.data() + text.size(), value);
                return result.ec == std::errc{} && result.ptr == text.data() + text.size();
            };
            auto invalid = [] { return StructuralSelector(StructuralSelectorType::Invalid, 0, 0); };
            if (parameters == "even")
            {
                a = 2;
                b = 0;
            }
            else if (parameters == "odd")
            {
                a = 2;
                b = 1;
            }
            else
            {
                const auto n = parameters.find('n');
                if (n == String::npos)
                {
                    a = 0;
                    if (!integer(parameters, b))
                    {
                        return invalid();
                    }
                }
                else
                {
                    if (parameters.find('n', n + 1) != String::npos)
                    {
                        return invalid();
                    }
                    const auto coefficient = parameters.substr(0, n);
                    if (coefficient.empty() || coefficient == "+")
                    {
                        a = 1;
                    }
                    else if (coefficient == "-")
                    {
                        a = -1;
                    }
                    else if (!integer(coefficient, a))
                    {
                        return invalid();
                    }
                    const auto suffix = StringUtilities::StripWhitespace(parameters.substr(n + 1));
                    if (suffix.empty())
                    {
                        b = 0;
                    }
                    else
                    {
                        if (suffix[0] != '+' && suffix[0] != '-')
                        {
                            return invalid();
                        }
                        const auto digits = StringUtilities::StripWhitespace(suffix.substr(1));
                        if (digits.empty() || digits.find_first_not_of("0123456789") != String::npos ||
                            !integer(String(1, suffix[0]) + digits, b))
                        {
                            return invalid();
                        }
                    }
                }
            }
        }

        return StructuralSelector(selector_type, a, b);
    }

    UniquePtr<const StyleSheetContainer> StyleSheetFactory::LoadStyleSheetContainer(const String& sheet)
    {
        UniquePtr<StyleSheetContainer> new_style_sheet;

        // Open stream, construct new sheet and pass the stream into the sheet
        auto stream = MakeUnique<StreamFile>();
        if (stream->Open(sheet))
        {
            new_style_sheet = MakeUnique<StyleSheetContainer>();
            if (!new_style_sheet->LoadStyleSheetContainer(stream.get()))
            {
                new_style_sheet.reset();
            }
        }

        return new_style_sheet;
    }

}
