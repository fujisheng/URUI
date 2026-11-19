#include "Engine/Source/Core/CssImageGeometry.h"

namespace URUI::Authoring
{
    void BorderImage(URUI::String& json, const URUI::String& property, const URUI::String& value)
    {
        if (property != "border-image" && property != "border-image-slice")
        {
            return;
        }
        URUI::String slice = value;
        if (property == "border-image")
        {
            URUI::PropertyDictionary parsed;
            if (!URUI::ParseCssBorderImageShorthand(URUI::StyleSheetSpecification::GetPropertySpecification(), parsed,
                                                    value))
            {
                return;
            }
            const auto* part = parsed.GetProperty(URUI::PropertyId::BorderImageSlice);
            if (!part)
            {
                return;
            }
            slice = part->Get<URUI::String>();
        }
        URUI::Array<URUI::NumericValue, 4> slices;
        bool fill = false;
        if (!URUI::ParseCssBorderImageSlice(slice, slices, fill))
        {
            return;
        }
        json += ",\"borderImage\":{\"fill\":" + URUI::String(fill ? "true" : "false") + ",\"slices\":[";
        for (int i = 0; i < 4; ++i)
        {
            if (i)
            {
                json += ',';
            }
            json += std::to_string(slices[i].number);
        }
        json += "],\"percentages\":[";
        for (int i = 0; i < 4; ++i)
        {
            if (i)
            {
                json += ',';
            }
            json += slices[i].unit == URUI::Unit::PERCENT ? "true" : "false";
        }
        json += "]}";
    }
    URUI::String Quote(const URUI::String& value)
    {
        URUI::String result = "\"";
        const char* hex = "0123456789abcdef";
        for (const unsigned char byte : value)
        {
            if (byte == '"' || byte == '\\')
            {
                result += '\\';
                result += char(byte);
            }
            else if (byte < 32)
            {
                result += "\\u00";
                result += hex[byte >> 4];
                result += hex[byte & 15];
            }
            else
            {
                result += char(byte);
            }
        }
        return result + '"';
    }
    URUI::String Attribute(const URUI::HtmlNode& node, const char* name)
    {
        for (const auto& attribute : node.attributes)
        {
            if (attribute.name == name)
            {
                return attribute.value;
            }
        }
        return {};
    }
    URUI::String Location(const URUI::SourceSpan& span)
    {
        return "\"line\":" + std::to_string(span.line) + ",\"column\":" + std::to_string(span.column) +
               ",\"byteOffset\":" + std::to_string(span.byteOffset);
    }
    void CssUrls(URUI::String& json, const URUI::Vector<URUI::CssToken>& tokens)
    {
        bool first = true;
        for (size_t i = 0; i < tokens.size(); ++i)
        {
            URUI::String url;
            if (tokens[i].kind == URUI::CssTokenKind::Url)
            {
                if (!URUI::DecodeCssUrl(tokens[i], url))
                {
                    continue;
                }
            }
            else if (tokens[i].kind == URUI::CssTokenKind::Ident && URUI::AsciiLowerCss(tokens[i].value) == "url" &&
                     i + 3 < tokens.size() && tokens[i + 1].raw == "(" &&
                     tokens[i + 2].kind == URUI::CssTokenKind::String && tokens[i + 3].raw == ")")
            {
                url = tokens[i + 2].value;
                i += 3;
            }
            else
            {
                continue;
            }
            if (!first)
            {
                json += ',';
            }
            first = false;
            json += Quote(url);
        }
    }
    void CssRules(URUI::String& json, const URUI::Vector<URUI::CssRule>& rules)
    {
        bool first = true;
        for (const auto& rule : rules)
        {
            if (!first)
            {
                json += ',';
            }
            first = false;
            json += "{\"atRule\":" + Quote(rule.atRule) + ",\"prelude\":" + Quote(rule.prelude) + ',' +
                    Location(rule.span) + ",\"declarations\":[";
            bool firstDeclaration = true;
            for (const auto& declaration : rule.declarations)
            {
                if (!firstDeclaration)
                {
                    json += ',';
                }
                firstDeclaration = false;
                URUI::Vector<URUI::Diagnostic> ignored;
                auto tokens = URUI::TokenizeCss(declaration.tokenValue, "urui:authoring-value", ignored);
                tokens.erase(std::remove_if(tokens.begin(), tokens.end(),
                                            [](const URUI::CssToken& token)
                                            { return token.kind == URUI::CssTokenKind::Whitespace; }),
                             tokens.end());
                const URUI::String identifier = tokens.size() == 1 && tokens[0].kind == URUI::CssTokenKind::Ident
                                                    ? tokens[0].value
                                                    : URUI::String();
                json += "{\"property\":" + Quote(declaration.propertyName) +
                        ",\"value\":" + Quote(declaration.tokenValue) + ",\"identifier\":" + Quote(identifier) +
                        ",\"important\":" + (declaration.important ? "true" : "false") + ',' +
                        Location(declaration.span) + ",\"urls\":[";
                CssUrls(json, tokens);
                json += "]";
                BorderImage(json, declaration.propertyName, declaration.tokenValue);
                json += "}";
            }
            json += "],\"children\":[";
            CssRules(json, rule.children);
            json += "]}";
        }
    }
    void Nodes(URUI::String& json, const std::vector<URUI::HtmlNode>& nodes, bool inert, bool& first)
    {
        for (const auto& node : nodes)
        {
            if (node.kind != URUI::HtmlNodeKind::Element && node.kind != URUI::HtmlNodeKind::Template)
            {
                continue;
            }
            if (!first)
            {
                json += ',';
            }
            first = false;
            json += "{\"tag\":" + Quote(node.tag) + ",\"id\":" + Quote(Attribute(node, "id")) +
                    ",\"classes\":" + Quote(Attribute(node, "class")) + ",\"source\":" + Quote(Attribute(node, "src")) +
                    ",\"href\":" + Quote(Attribute(node, "href")) + ",\"rel\":" + Quote(Attribute(node, "rel")) +
                    ",\"line\":" + std::to_string(node.start.line) + ",\"inert\":" + (inert ? "true" : "false") +
                    ",\"inlineStyle\":" + Quote(Attribute(node, "style")) + ",\"styleText\":";
            URUI::String style_text;
            if (node.tag == "style")
            {
                for (const auto& child : node.children)
                {
                    if (child.kind == URUI::HtmlNodeKind::Text)
                    {
                        style_text += child.text;
                    }
                }
            }
            json += Quote(style_text) + ",\"attributes\":{";
            bool firstAttribute = true;
            for (const auto& attribute : node.attributes)
            {
                if (!firstAttribute)
                {
                    json += ',';
                }
                firstAttribute = false;
                json += Quote(attribute.name) + ':' + Quote(attribute.value);
            }
            json += "}}";
            Nodes(json, node.children, inert || node.kind == URUI::HtmlNodeKind::Template, first);
        }
    }
}
extern "C"
{
    int urui_authoring_protocol_version(void)
    {
        return 1;
    }
    int urui_validate_stylesheet(const char* uri, const char* source, char* output, int capacity, int* required)
    try
    {
        if (!uri || !*uri || !source || !required || capacity < 0 || (capacity && !output))
        {
            return -1;
        }
        if (started && contextThread != std::this_thread::get_id())
        {
            return -5;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        // 复用已初始化的解析器与注册表；每次编译仅修改独立的属性表副本。
        // 尚无 Surface 时只在本调用内初始化后端，绝不替换活动文档。
        const bool temporary = !started;
        if (temporary && !urui_create(1, 1))
        {
            return -2;
        }
        struct Shutdown
        {
            bool temporary;
            ~Shutdown()
            {
                if (temporary)
                {
                    urui_destroy();
                }
            }
        } shutdown{temporary};
        URUI::Vector<URUI::Diagnostic> diagnostics;
        {
            URUI::StreamMemory stream(reinterpret_cast<const URUI::byte*>(source), std::strlen(source));
            stream.SetSourceURL(URUI::URL(uri));
            URUI::StyleSheetContainer sheet;
            if (!sheet.LoadStyleSheetContainer(&stream, 1, nullptr, &diagnostics) && diagnostics.empty())
            {
                diagnostics.push_back(
                    {URUI::DiagnosticSeverity::Error, "CSS_PARSE", "Stylesheet could not be compiled.", uri, {}});
            }
        }
        const auto parsed = URUI::ParseCss(source, uri);
        URUI::String json = "{\"diagnostics\":[";
        bool first = true;
        for (const auto& diagnostic : diagnostics)
        {
            if (!first)
            {
                json += ',';
            }
            first = false;
            json +=
                "{\"code\":" + URUI::Authoring::Quote(diagnostic.code) +
                ",\"message\":" + URUI::Authoring::Quote(diagnostic.message) +
                ",\"uri\":" + URUI::Authoring::Quote(diagnostic.sourceUrl) + ",\"severity\":" +
                URUI::Authoring::Quote(diagnostic.severity == URUI::DiagnosticSeverity::Error ? "error" : "warning") +
                ',' + URUI::Authoring::Location(diagnostic.span) + '}';
        }
        json += "],\"rules\":[";
        URUI::Authoring::CssRules(json, parsed.rules);
        json += "]}";
        if (json.size() >= INT32_MAX)
        {
            return -4;
        }
        *required = int(json.size()) + 1;
        if (capacity < *required)
        {
            return -3;
        }
        std::memcpy(output, json.c_str(), size_t(*required));
        return int(diagnostics.size());
    }
    catch (...)
    {
        return -8;
    }
    int urui_validate_html(const char* source, const char* source_url, int fragment, char* output, int capacity,
                           int* required)
    try
    {
        if (!source || !source_url || !*source_url || !required || capacity < 0 || (!output && capacity) ||
            (fragment != 0 && fragment != 1))
        {
            return -1;
        }
        // 与运行时共用同一个纯解析入口，不创建或切换任何活动 Context。
        auto parsed = fragment ? URUI::ParseHtmlFragment(source, "body", source_url)
                               : URUI::ParseHtmlDocument(source, source_url);
        URUI::String json = "{\"diagnostics\":[";
        bool first = true;
        for (const auto& diagnostic : parsed.diagnostics)
        {
            if (!first)
            {
                json += ',';
            }
            first = false;
            json +=
                "{\"code\":" + URUI::Authoring::Quote(diagnostic.code) +
                ",\"message\":" + URUI::Authoring::Quote(diagnostic.message) +
                ",\"uri\":" + URUI::Authoring::Quote(diagnostic.sourceUrl) + ",\"severity\":" +
                URUI::Authoring::Quote(diagnostic.severity == URUI::DiagnosticSeverity::Error ? "error" : "warning") +
                ",\"line\":" + std::to_string(diagnostic.span.line) +
                ",\"column\":" + std::to_string(diagnostic.span.column) +
                ",\"byteOffset\":" + std::to_string(diagnostic.span.byteOffset) + '}';
        }
        json += "],\"nodes\":[";
        first = true;
        if (parsed.document)
        {
            URUI::Authoring::Nodes(json, parsed.document->nodes, false, first);
        }
        json += "]}";
        if (json.size() >= INT32_MAX)
        {
            return -4;
        }
        *required = int(json.size()) + 1;
        if (capacity < *required)
        {
            return -3;
        }
        std::memcpy(output, json.c_str(), size_t(*required));
        return int(parsed.diagnostics.size());
    }
    catch (...)
    {
        return -8;
    }
}
