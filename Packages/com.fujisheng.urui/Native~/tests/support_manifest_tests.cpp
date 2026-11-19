#include "urui.h"
#include <URUI/Core/StyleSheetSpecification.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/PropertyIdSet.h>
#include <URUI/Core/Property.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <string>

// 枚举真正启动后的注册表，覆盖运行时注册与循环注册的属性。
static void Quote(std::ostream& out, const std::string& value)
{
    out << '"';
    for (unsigned char c : value)
    {
        if (c == '"' || c == '\\')
        {
            out << '\\' << c;
        }
        else if (c < 32)
        {
            out << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<unsigned>(c) << std::dec;
        }
        else
        {
            out << c;
        }
    }
    out << '"';
}

int main(int argc, char** argv)
{
    if (argc != 2 || urui_create(1280, 720) != 1)
    {
        return 2;
    }
    // Engine-owned semantics use explicit URUI property names, without old aliases.
    for (const char* name : {"caret-blink-rate",
                             "caret-width",
                             "fill-image",
                             "font-effect",
                             "image-color",
                             "image-effect",
                             "image-fill-amount",
                             "image-fill-center",
                             "image-fill-clockwise",
                             "image-fill-mode",
                             "image-fill-origin",
                             "image-slice-bottom",
                             "image-slice-left",
                             "image-slice-right",
                             "image-slice-top",
                             "nav-down",
                             "nav-left",
                             "nav-right",
                             "nav-up",
                             "perspective-origin-x",
                             "perspective-origin-y",
                             "scrollbar-margin",
                             "transform-origin-x",
                             "transform-origin-y",
                             "transform-origin-z",
                             "clip",
                             "drag",
                             "tab-index",
                             "focus",
                             "decorator"})
    {
        if (URUI::StyleSheetSpecification::GetProperty(name) ||
            !URUI::StyleSheetSpecification::GetProperty(std::string("--urui-") + name))
        {
            std::cerr << "Unmigrated private property: " << name << std::endl;
            urui_destroy();
            return 5;
        }
    }
    std::ofstream output(argv[1]);
    if (!output)
    {
        urui_destroy();
        return 3;
    }
    output << "{\"abi\":" << urui_bridge_abi_version() << ",\"properties\":[";
    bool first = true;
    for (auto id : URUI::StyleSheetSpecification::GetRegisteredProperties())
    {
        const auto* definition = URUI::StyleSheetSpecification::GetProperty(id);
        if (!first)
        {
            output << ',';
        }
        first = false;
        output << "{\"name\":";
        Quote(output, URUI::StyleSheetSpecification::GetPropertyName(id));
        output << ",\"initial\":";
        Quote(output, definition->GetDefaultValue()->ToString());
        output << ",\"inherited\":" << (definition->IsInherited() ? "true" : "false")
               << ",\"relativeTarget\":" << static_cast<int>(definition->GetRelativeTarget()) << '}';
    }
    output << "],\"shorthands\":[";
    first = true;
    for (int i = 1; i < static_cast<int>(URUI::ShorthandId::NumDefinedIds); ++i)
    {
        const auto id = static_cast<URUI::ShorthandId>(i);
        if (!URUI::StyleSheetSpecification::GetShorthand(id))
        {
            continue;
        }
        if (!first)
        {
            output << ',';
        }
        first = false;
        Quote(output, URUI::StyleSheetSpecification::GetShorthandName(id));
    }
    output << "]}\n";
    urui_destroy();
    return output.good() ? 0 : 4;
}
