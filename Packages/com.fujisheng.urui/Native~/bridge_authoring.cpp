#include "bridge_runtime.h"
#include "Engine/Source/Core/CompiledDocument.h"
#include "Engine/Source/Core/CssImageGeometry.h"
#include "Engine/Source/Core/CssParser.h"
#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/HtmlParser.h"
#include <URUI/Core/StreamMemory.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <algorithm>
#include <charconv>
#include <cmath>
#include <cstring>
#include <functional>
#include <stdexcept>
#include <unordered_map>

namespace URUI::Bridge
{
    namespace
    {
        void AppendQuoted(URUI::String& json, const URUI::String& value)
        {
            static constexpr char hex[] = "0123456789abcdef";
            json.push_back('"');
            for (const unsigned char byte : value)
            {
                if (byte == '"' || byte == '\\')
                {
                    json.push_back('\\');
                    json.push_back(static_cast<char>(byte));
                }
                else if (byte < 32)
                {
                    json.append("\\u00", 4);
                    json.push_back(hex[byte >> 4]);
                    json.push_back(hex[byte & 15]);
                }
                else
                {
                    json.push_back(static_cast<char>(byte));
                }
            }
            json.push_back('"');
        }

        void AppendQuotedCStr(URUI::String& json, const char* value)
        {
            static constexpr char hex[] = "0123456789abcdef";
            json.push_back('"');
            if (value)
            {
                for (const unsigned char* p = reinterpret_cast<const unsigned char*>(value); *p; ++p)
                {
                    const unsigned char byte = *p;
                    if (byte == '"' || byte == '\\')
                    {
                        json.push_back('\\');
                        json.push_back(static_cast<char>(byte));
                    }
                    else if (byte < 32)
                    {
                        json.append("\\u00", 4);
                        json.push_back(hex[byte >> 4]);
                        json.push_back(hex[byte & 15]);
                    }
                    else
                    {
                        json.push_back(static_cast<char>(byte));
                    }
                }
            }
            json.push_back('"');
        }

        template <typename Int> void AppendInt(URUI::String& json, Int value)
        {
            char buffer[32];
            const auto result = std::to_chars(buffer, buffer + sizeof(buffer), value);
            if (result.ec == std::errc{})
            {
                json.append(buffer, static_cast<size_t>(result.ptr - buffer));
            }
            else
            {
                json += std::to_string(value);
            }
        }

        void AppendFloat(URUI::String& json, float value)
        {
            if (!std::isfinite(value))
            {
                json += "0";
                return;
            }
            char buffer[48];
            const int written = std::snprintf(buffer, sizeof(buffer), "%.6f", static_cast<double>(value));
            if (written > 0 && static_cast<size_t>(written) < sizeof(buffer))
            {
                json.append(buffer, static_cast<size_t>(written));
            }
            else
            {
                json += "0";
            }
        }

        void AppendLocation(URUI::String& json, const URUI::SourceSpan& span)
        {
            json += "\"line\":";
            AppendInt(json, span.line);
            json += ",\"column\":";
            AppendInt(json, span.column);
            json += ",\"byteOffset\":";
            AppendInt(json, span.byteOffset);
        }

        const URUI::String& AttributeValue(const URUI::HtmlNode& node, const char* name)
        {
            static const URUI::String empty;
            for (const auto& attribute : node.attributes)
            {
                if (attribute.name == name)
                {
                    return attribute.value;
                }
            }
            return empty;
        }

        void AppendBorderImage(URUI::String& json, const URUI::String& property, const URUI::String& value)
        {
            if (property != "border-image" && property != "border-image-slice")
            {
                return;
            }
            URUI::String slice = value;
            if (property == "border-image")
            {
                URUI::PropertyDictionary parsed;
                if (!URUI::ParseCssBorderImageShorthand(URUI::StyleSheetSpecification::GetPropertySpecification(),
                                                        parsed, value))
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
            json += ",\"borderImage\":{\"fill\":";
            json += fill ? "true" : "false";
            json += ",\"slices\":[";
            for (int i = 0; i < 4; ++i)
            {
                if (i)
                {
                    json.push_back(',');
                }
                AppendFloat(json, slices[i].number);
            }
            json += "],\"percentages\":[";
            for (int i = 0; i < 4; ++i)
            {
                if (i)
                {
                    json.push_back(',');
                }
                json += slices[i].unit == URUI::Unit::PERCENT ? "true" : "false";
            }
            json += "]}";
        }

        void AppendCssUrls(URUI::String& json, const URUI::Vector<URUI::CssToken>& tokens)
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
                    json.push_back(',');
                }
                first = false;
                AppendQuoted(json, url);
            }
        }

        void AppendCssRules(URUI::String& json, const URUI::Vector<URUI::CssRule>& rules)
        {
            bool first = true;
            for (const auto& rule : rules)
            {
                if (!first)
                {
                    json.push_back(',');
                }
                first = false;
                json += "{\"atRule\":";
                AppendQuoted(json, rule.atRule);
                json += ",\"prelude\":";
                AppendQuoted(json, rule.prelude);
                json.push_back(',');
                AppendLocation(json, rule.span);
                json += ",\"declarations\":[";
                bool firstDeclaration = true;
                for (const auto& declaration : rule.declarations)
                {
                    if (!firstDeclaration)
                    {
                        json.push_back(',');
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
                    json += "{\"property\":";
                    AppendQuoted(json, declaration.propertyName);
                    json += ",\"value\":";
                    AppendQuoted(json, declaration.tokenValue);
                    json += ",\"identifier\":";
                    AppendQuoted(json, identifier);
                    json += ",\"important\":";
                    json += declaration.important ? "true" : "false";
                    json.push_back(',');
                    AppendLocation(json, declaration.span);
                    json += ",\"urls\":[";
                    AppendCssUrls(json, tokens);
                    json.push_back(']');
                    AppendBorderImage(json, declaration.propertyName, declaration.tokenValue);
                    json.push_back('}');
                }
                json += "],\"children\":[";
                AppendCssRules(json, rule.children);
                json += "]}";
            }
        }

        void AppendHtmlNodes(URUI::String& json, const std::vector<URUI::HtmlNode>& nodes, bool inert, bool& first)
        {
            for (const auto& node : nodes)
            {
                if (node.kind != URUI::HtmlNodeKind::Element && node.kind != URUI::HtmlNodeKind::Template)
                {
                    continue;
                }
                if (!first)
                {
                    json.push_back(',');
                }
                first = false;
                json += "{\"tag\":";
                AppendQuoted(json, node.tag);
                json += ",\"id\":";
                AppendQuoted(json, AttributeValue(node, "id"));
                json += ",\"classes\":";
                AppendQuoted(json, AttributeValue(node, "class"));
                json += ",\"source\":";
                AppendQuoted(json, AttributeValue(node, "src"));
                json += ",\"href\":";
                AppendQuoted(json, AttributeValue(node, "href"));
                json += ",\"rel\":";
                AppendQuoted(json, AttributeValue(node, "rel"));
                json += ",\"line\":";
                AppendInt(json, node.start.line);
                json += ",\"inert\":";
                json += inert ? "true" : "false";
                json += ",\"inlineStyle\":";
                AppendQuoted(json, AttributeValue(node, "style"));
                json += ",\"styleText\":";
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
                AppendQuoted(json, style_text);
                json += ",\"attributes\":{";
                bool firstAttribute = true;
                for (const auto& attribute : node.attributes)
                {
                    if (!firstAttribute)
                    {
                        json.push_back(',');
                    }
                    firstAttribute = false;
                    AppendQuoted(json, attribute.name);
                    json.push_back(':');
                    AppendQuoted(json, attribute.value);
                }
                json += "}}";
                AppendHtmlNodes(json, node.children, inert || node.kind == URUI::HtmlNodeKind::Template, first);
            }
        }

        int FormatDiagnosticsError(const URUI::Vector<URUI::Diagnostic>& diagnostics, SurfaceContext* surface = nullptr)
        {
            if (!diagnostics.empty())
            {
                const auto& error = diagnostics.front();
                const std::string formatted = error.sourceUrl + ":" + std::to_string(error.span.line) + ":" +
                                              std::to_string(error.span.column) + " [" + error.code + "] " +
                                              error.message;
                if (surface)
                {
                    surface->SetError(formatted);
                }
                else
                {
                    EngineRuntime::SetThreadError(formatted);
                }
                if (error.code == "HTML_MEMORY")
                {
                    return URUI_ERR_INTERNAL;
                }
            }
            return URUI_ERR_VALIDATION;
        }

        class CompileFiles final : public URUI::FileInterface
        {
            struct Cursor
            {
                const uint8_t* data;
                size_t size;
                size_t position = 0;
            };
            std::unordered_map<URUI::String, Cursor> sources;
            std::unordered_map<Cursor*, std::unique_ptr<Cursor>> opened;

        public:
            CompileFiles(const URUICompileResource* resources, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    const auto& source = resources[i];
                    if (!source.uri || !*source.uri || source.length < 0 || (source.length && !source.bytes))
                    {
                        throw std::invalid_argument("Invalid compile dependency.");
                    }
                    const auto uri = CanonicalResourceUrl(source.uri);
                    if (!sources.emplace(uri, Cursor{source.bytes, static_cast<size_t>(source.length)}).second)
                    {
                        throw std::invalid_argument("Duplicate compile dependency URI.");
                    }
                }
            }
            URUI::FileHandle Open(const URUI::String& path) override
            {
                const auto found = sources.find(CanonicalResourceUrl(path.c_str()));
                if (found == sources.end())
                {
                    return 0;
                }
                auto cursor = std::make_unique<Cursor>(found->second);
                auto* key = cursor.get();
                opened.emplace(key, std::move(cursor));
                return reinterpret_cast<URUI::FileHandle>(key);
            }
            void Close(URUI::FileHandle handle) override
            {
                opened.erase(reinterpret_cast<Cursor*>(handle));
            }
            size_t Read(void* target, size_t size, URUI::FileHandle handle) override
            {
                auto& cursor = *opened.at(reinterpret_cast<Cursor*>(handle));
                const auto count = std::min(size, cursor.size - cursor.position);
                if (count)
                {
                    std::memcpy(target, cursor.data + cursor.position, count);
                }
                cursor.position += count;
                return count;
            }
            bool Seek(URUI::FileHandle handle, long offset, int origin) override
            {
                auto& cursor = *opened.at(reinterpret_cast<Cursor*>(handle));
                if (origin != SEEK_SET && origin != SEEK_CUR && origin != SEEK_END)
                {
                    return false;
                }
                const auto base = origin == SEEK_SET ? int64_t{0}
                                                     : int64_t(origin == SEEK_CUR ? cursor.position : cursor.size);
                if (offset < -base || offset > int64_t(cursor.size) - base)
                {
                    return false;
                }
                cursor.position = static_cast<size_t>(base + offset);
                return true;
            }
            size_t Tell(URUI::FileHandle handle) override
            {
                return opened.at(reinterpret_cast<Cursor*>(handle))->position;
            }
            size_t Length(URUI::FileHandle handle) override
            {
                return opened.at(reinterpret_cast<Cursor*>(handle))->size;
            }
        };
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_authoring_protocol_version(void)
    {
        return 2;
    }

    int urui_validate_stylesheet(const char* uri, const char* source, char* output, int capacity, int* required)
    try
    {
        if (!uri || !*uri || !source || !required || capacity < 0 || (capacity && !output))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        if (auto* active = EngineRuntime::ActiveSurface(); active && active->callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        EngineRuntime::IsolatedScope isolated;
        if (!isolated.ok)
        {
            return URUI_ERR_INTERNAL;
        }
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
        URUI::String json;
        json.reserve(512 + std::strlen(source) * 2);
        json += "{\"diagnostics\":[";
        bool first = true;
        for (const auto& diagnostic : diagnostics)
        {
            if (!first)
            {
                json.push_back(',');
            }
            first = false;
            json += "{\"code\":";
            AppendQuoted(json, diagnostic.code);
            json += ",\"message\":";
            AppendQuoted(json, diagnostic.message);
            json += ",\"uri\":";
            AppendQuoted(json, diagnostic.sourceUrl);
            json += ",\"severity\":";
            AppendQuotedCStr(json, diagnostic.severity == URUI::DiagnosticSeverity::Error ? "error" : "warning");
            json.push_back(',');
            AppendLocation(json, diagnostic.span);
            json.push_back('}');
        }
        json += "],\"rules\":[";
        AppendCssRules(json, parsed.rules);
        json += "]}";
        if (json.size() >= static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(json.size()) + 1;
        if (capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(output, json.c_str(), static_cast<size_t>(*required));
        return static_cast<int>(diagnostics.size());
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_validate_stylesheet failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_validate_html(const char* source, const char* source_url, int fragment, char* output, int capacity,
                           int* required)
    try
    {
        if (!source || !source_url || !*source_url || !required || capacity < 0 || (!output && capacity) ||
            (fragment != 0 && fragment != 1))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        auto parsed = fragment ? URUI::ParseHtmlFragment(source, "body", source_url)
                               : URUI::ParseHtmlDocument(source, source_url);
        URUI::String json;
        json.reserve(512 + std::strlen(source) * 2);
        json += "{\"diagnostics\":[";
        bool first = true;
        for (const auto& diagnostic : parsed.diagnostics)
        {
            if (!first)
            {
                json.push_back(',');
            }
            first = false;
            json += "{\"code\":";
            AppendQuoted(json, diagnostic.code);
            json += ",\"message\":";
            AppendQuoted(json, diagnostic.message);
            json += ",\"uri\":";
            AppendQuoted(json, diagnostic.sourceUrl);
            json += ",\"severity\":";
            AppendQuotedCStr(json, diagnostic.severity == URUI::DiagnosticSeverity::Error ? "error" : "warning");
            json.push_back(',');
            AppendLocation(json, diagnostic.span);
            json.push_back('}');
        }
        json += "],\"nodes\":[";
        first = true;
        if (parsed.document)
        {
            AppendHtmlNodes(json, parsed.document->nodes, false, first);
        }
        json += "]}";
        if (json.size() >= static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(json.size()) + 1;
        if (capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(output, json.c_str(), static_cast<size_t>(*required));
        return static_cast<int>(parsed.diagnostics.size());
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_validate_html failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_compile_document(const char* html, const char* source_url, int fragment,
                              const URUICompileResource* resources, int resource_count, uint8_t* output, int capacity,
                              int* required)
    try
    {
        if (!html || !source_url || !*source_url || (fragment != 0 && fragment != 1) || !required || capacity < 0 ||
            (capacity && !output) || resource_count < 0 || resource_count > 100000 || (resource_count && !resources))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        if (auto* active = EngineRuntime::ActiveSurface(); active && active->callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        EngineRuntime::IsolatedScope isolated;
        if (!isolated.ok)
        {
            return URUI_ERR_INTERNAL;
        }
        CompileFiles files(resources, resource_count);
        URUI::Vector<URUI::byte> bytes;
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::CompileDocument(html, source_url, &files, bytes, diagnostics, fragment != 0))
        {
            return FormatDiagnosticsError(diagnostics);
        }
        if (bytes.size() > static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(bytes.size());
        if (capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(output, bytes.data(), bytes.size());
        return URUI_OK;
    }
    catch (const std::invalid_argument& failure)
    {
        EngineRuntime::SetThreadError(failure.what());
        return URUI_ERR_INVALID_ARG;
    }
    catch (const std::exception& failure)
    {
        EngineRuntime::SetThreadError(failure.what());
        return URUI_ERR_INTERNAL;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_compile_document failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_compile_template(const uint8_t* parent, int parent_length, const char* html, const char* source_url,
                              uint8_t* output, int capacity, int* required)
    try
    {
        if (!parent || parent_length <= 0 || !html || !source_url || !*source_url || capacity < 0 ||
            (capacity && !output) || !required)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        if (auto* active = EngineRuntime::ActiveSurface(); active && active->callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        EngineRuntime::IsolatedScope isolated;
        if (!isolated.ok)
        {
            return URUI_ERR_INTERNAL;
        }
        URUI::Vector<URUI::byte> bytes;
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::CompileTemplate(html, source_url, parent, static_cast<size_t>(parent_length), bytes, diagnostics))
        {
            return FormatDiagnosticsError(diagnostics);
        }
        if (bytes.size() > static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(bytes.size());
        if (capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(output, bytes.data(), bytes.size());
        return URUI_OK;
    }
    catch (const std::exception& failure)
    {
        EngineRuntime::SetThreadError(failure.what());
        return URUI_ERR_INTERNAL;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_compile_template failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_compiled_document_metadata(const uint8_t* bytes, int length, char* output, int capacity, int* required)
    try
    {
        if (!bytes || length <= 0 || capacity < 0 || (capacity && !output) || !required)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        if (auto* active = EngineRuntime::ActiveSurface(); active && active->callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        EngineRuntime::IsolatedScope isolated;
        if (!isolated.ok)
        {
            return URUI_ERR_INTERNAL;
        }
        URUI::CompiledDocument compiled;
        URUI::String error;
        if (!URUI::ReadCompiledDocument(bytes, static_cast<size_t>(length), compiled, error))
        {
            EngineRuntime::SetThreadError(error);
            return URUI_ERR_VALIDATION;
        }
        uint32_t count = 0;
        for (const auto& node : compiled.html.nodes)
        {
            count = std::max(count, node.subtreeEnd);
        }
        URUI::String json;
        json.reserve(256 + static_cast<size_t>(count) * 96);
        json += "{\"identity\":\"";
        AppendInt(json, compiled.identity);
        json += "\",\"parentIdentity\":\"";
        AppendInt(json, compiled.parentIdentity);
        json += "\",\"nodeCount\":";
        AppendInt(json, count);
        json += ",\"nodes\":[";
        bool first = true;
        std::function<void(const std::vector<URUI::HtmlNode>&, int)> append = [&](const auto& nodes, int parent)
        {
            for (const auto& node : nodes)
            {
                if (node.kind == URUI::HtmlNodeKind::Element || node.kind == URUI::HtmlNodeKind::Template)
                {
                    if (!first)
                    {
                        json.push_back(',');
                    }
                    first = false;
                    const URUI::String* name = nullptr;
                    const URUI::String* marker = nullptr;
                    const URUI::String* input_type = nullptr;
                    for (const auto& attribute : node.attributes)
                    {
                        if (attribute.name == "id")
                        {
                            name = &attribute.value;
                        }
                        else if (attribute.name == "data-urui-type")
                        {
                            marker = &attribute.value;
                        }
                        else if (attribute.name == "type")
                        {
                            input_type = &attribute.value;
                        }
                    }
                    static const URUI::String empty;
                    json += "{\"id\":";
                    AppendInt(json, node.localId);
                    json += ",\"end\":";
                    AppendInt(json, node.subtreeEnd);
                    json += ",\"parent\":";
                    AppendInt(json, parent);
                    json += ",\"tag\":";
                    AppendQuoted(json, node.tag);
                    json += ",\"name\":";
                    AppendQuoted(json, name ? *name : empty);
                    json += ",\"marker\":";
                    AppendQuoted(json, marker ? *marker : empty);
                    json += ",\"inputType\":";
                    AppendQuoted(json, input_type ? *input_type : empty);
                    json += ",\"classes\":[";
                    bool classFirst = true;
                    for (const auto& item : node.compiledClasses)
                    {
                        if (!classFirst)
                        {
                            json.push_back(',');
                        }
                        classFirst = false;
                        AppendQuoted(json, item);
                    }
                    json += "]}";
                }
                append(node.children, static_cast<int>(node.localId));
            }
        };
        append(compiled.html.nodes, -1);
        json += "]}";
        if (json.size() >= static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(json.size()) + 1;
        if (capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(output, json.c_str(), static_cast<size_t>(*required));
        return URUI_OK;
    }
    catch (const std::exception& failure)
    {
        EngineRuntime::SetThreadError(failure.what());
        return URUI_ERR_INTERNAL;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_compiled_document_metadata failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_compiled_info(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int kind,
                                   const char* key, URUICompiledNodeInfo* info)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!info || kind < 0 || kind > 2 || (kind && (!key || !*key)))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = surface->elementReferences.Resolve(token, state->Element());
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!element->GetCompiledBlueprint())
        {
            return URUI_ABSENT;
        }
        *info = {element->GetCompiledBlueprint(),
                 element->GetCompiledQueryRevision(),
                 element->GetCompiledNodeId(),
                 element->GetCompiledSubtreeEnd(),
                 static_cast<int>(element->GetCompiledQueryFlags()),
                 0};
        if ((info->flags & 8) && kind)
        {
            info->root_match = kind == 1 ? (element->GetId() == key) : element->IsClassSet(key);
        }
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_element_compiled_info failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_compiled_tokens(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                     uint64_t identity, uint64_t revision, uint64_t* output, int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* root = surface->elementReferences.Resolve(token, state->Element());
        if (!root)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!identity || root->GetCompiledBlueprint() != identity || root->GetCompiledQueryRevision() != revision ||
            !(root->GetCompiledQueryFlags() & 1))
        {
            return URUI_ABSENT;
        }
        const uint32_t first = root->GetCompiledNodeId() == UINT32_MAX ? 0 : root->GetCompiledNodeId();
        const uint32_t end = root->GetCompiledSubtreeEnd();
        if (end < first || end - first > 1000000 || capacity < 0 || (capacity && !output))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (static_cast<uint32_t>(capacity) < end - first)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        if (end > first)
        {
            std::fill(output, output + (end - first), uint64_t{0});
        }
        std::vector<URUI::Element*> pending{root};
        uint32_t previous = 0;
        bool have_previous = false;
        while (!pending.empty())
        {
            auto* element = pending.back();
            pending.pop_back();
            const auto id = element->GetCompiledNodeId();
            if (element == root && (root->GetCompiledQueryFlags() & 8))
            {
            }
            else
            {
                if (element->GetCompiledBlueprint() != identity || id < first || id >= end ||
                    (have_previous && id <= previous))
                {
                    return URUI_ABSENT;
                }
                previous = id;
                have_previous = true;
                const auto reference = surface->elementReferences.Track(element, state->Element());
                if (!reference)
                {
                    return URUI_ERR_BUDGET;
                }
                output[id - first] = reference;
            }
            for (int i = element->GetNumChildren() - 1; i >= 0; --i)
            {
                pending.push_back(element->GetChild(i));
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_element_compiled_tokens failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_begin_compiled_template(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                             const uint8_t* bytes, int length)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (state->fragment)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (!token || !bytes || length <= 0)
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto recordedOwner = surface->documentRegistry->OwnerOf(token);
        if (recordedOwner && recordedOwner != owner)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = surface->elementReferences.Resolve(token, state->Element());
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        URUI::String error;
        const auto compiled = state->templates.Read(bytes, static_cast<size_t>(length),
                                                    state->Element()->GetStyleSpecification(), error);
        if (!compiled)
        {
            surface->SetError(error);
            return URUI_ERR_VALIDATION;
        }
        if (compiled->html.kind != URUI::HtmlDocumentKind::Fragment || !compiled->parentIdentity ||
            compiled->parentIdentity != state->Element()->GetCompiledIdentity())
        {
            surface->SetError("Compiled template does not belong to this document generation.");
            return URUI_ERR_INVALID_ARG;
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto change = URUI::InnerHtmlReplacement::Begin(*element, compiled->html, diagnostics);
        if (!change)
        {
            if (!diagnostics.empty())
            {
                FormatDiagnosticsError(diagnostics, surface);
            }
            return URUI_ERR_INTERNAL;
        }
        uint32_t end = 0;
        for (const auto& node : compiled->html.nodes)
        {
            end = std::max(end, node.subtreeEnd);
        }
        change->SetCompiledRoot(compiled->identity, end);
        state->fragment = std::move(change);
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_element_begin_compiled_template failed");
        return URUI_ERR_INTERNAL;
    }
}
