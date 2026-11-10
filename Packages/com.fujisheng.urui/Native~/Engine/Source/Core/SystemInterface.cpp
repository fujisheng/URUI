#include "../../Include/URUI/Core/SystemInterface.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/StringUtilities.h"
#include "../../Include/URUI/Core/URL.h"
#include "LogDefault.h"
#include <chrono>

namespace URUI {

static String& GlobalClipBoardText()
{
	static String clipboard_text;
	return clipboard_text;
}

SystemInterface::SystemInterface() {}

SystemInterface::~SystemInterface() {}

double SystemInterface::GetElapsedTime()
{
	static const auto start = std::chrono::steady_clock::now();
	const auto current = std::chrono::steady_clock::now();
	std::chrono::duration<double> diff = current - start;
	return diff.count();
}

bool SystemInterface::LogMessage(Log::Type type, const String& message)
{
	return LogDefault::LogMessage(type, message);
}

void SystemInterface::SetMouseCursor(const String& /*cursor_name*/) {}

void SystemInterface::SetClipboardText(const String& text)
{
	// The default implementation will only copy and paste within the application
	GlobalClipBoardText() = text;
}

void SystemInterface::GetClipboardText(String& text)
{
	text = GlobalClipBoardText();
}

int SystemInterface::TranslateString(String& translated, const String& input)
{
	translated = input;
	return 0;
}

namespace {
struct ResourceUrlParts {
    String scheme, authority, path, query, fragment;
    bool has_authority = false, has_query = false, has_fragment = false;
};
ResourceUrlParts SplitResourceUrl(String value)
{
    ResourceUrlParts result;
    value = StringUtilities::Replace(value, '\\', '/');
    const auto hash = value.find('#');
    if (hash != String::npos) { result.has_fragment = true; result.fragment = value.substr(hash + 1); value.resize(hash); }
    const auto query = value.find('?');
    if (query != String::npos) { result.has_query = true; result.query = value.substr(query + 1); value.resize(query); }
    const auto colon = value.find(':'), slash = value.find('/');
    if (colon != String::npos && (slash == String::npos || colon < slash)) {
        result.scheme = value.substr(0, colon);
        for (auto& ch : result.scheme) if (ch >= 'A' && ch <= 'Z') ch += 'a' - 'A';
        value.erase(0, colon + 1);
        // Platform file paths enter the same hierarchical file URL domain.
        if (result.scheme.size() == 1 && !value.empty() && value[0] == '/') {
            value = "///" + result.scheme + ':' + value; result.scheme = "file";
        }
    }
    if (value.compare(0, 2, "//") == 0) {
        result.has_authority = true;
        const auto end = value.find('/', 2);
        result.authority = value.substr(2, end == String::npos ? String::npos : end - 2);
        // Resource hosts are ASCII; path spelling and case remain significant.
        for (auto& ch : result.authority) if (ch >= 'A' && ch <= 'Z') ch += 'a' - 'A';
        value = end == String::npos ? "/" : value.substr(end);
    }
    result.path = std::move(value); return result;
}
String EncodeResourceUrl(const String& value, bool path)
{
    const char* hex = "0123456789ABCDEF";
    String result;
    for (const unsigned char ch : value) {
        if (ch <= 0x20 || ch >= 0x7f || ch == '"' || ch == '<' || ch == '>' || (path && (ch == '`' || ch == '{' || ch == '}'))) {
            result += '%'; result += hex[ch >> 4]; result += hex[ch & 15];
        } else result += char(ch);
    }
    return result;
}
String NormalizeResourcePath(const String& value)
{
    const bool absolute = !value.empty() && value[0] == '/';
    Vector<String> segments;
    size_t begin = absolute ? 1 : 0;
    while (begin <= value.size()) {
        const auto end = value.find('/', begin);
        const auto part = value.substr(begin, end == String::npos ? String::npos : end - begin);
        String dot;
        for (size_t i = 0; i < part.size(); ++i) {
            if (i + 2 < part.size() && part[i] == '%' && part[i+1] == '2' && (part[i+2] == 'e' || part[i+2] == 'E')) { dot += '.'; i += 2; }
            else dot += part[i];
        }
        if (dot == "..") { if (!segments.empty()) segments.pop_back(); }
        else if (dot != ".") segments.push_back(part);
        if (end == String::npos) {
            if ((dot == "." || dot == "..") && !segments.empty()) segments.push_back("");
            break;
        }
        begin = end + 1;
    }
    String result = absolute ? "/" : "";
    for (size_t i = 0; i < segments.size(); ++i) { if (i) result += '/'; result += segments[i]; }
    return EncodeResourceUrl(result, true);
}
}

void SystemInterface::JoinPath(String& translated_path, const String& document_path, const String& path)
{
    // One URL resolver serves HTML, CSS, font sources and host registration.
    // Hierarchical resource URLs follow the path/reference rules in the URL
    // Standard; this does not add network loading or opaque data URL support.
    const auto base = SplitResourceUrl(document_path);
    auto target = SplitResourceUrl(path);
    if (target.scheme.empty()) {
        target.scheme = base.scheme;
        if (!target.has_authority) {
            target.has_authority = base.has_authority; target.authority = base.authority;
            if (target.path.empty()) {
                target.path = base.path;
                if (!target.has_query) { target.has_query = base.has_query; target.query = base.query; }
            } else if (target.path[0] != '/') {
                const auto end = base.path.rfind('/');
                target.path = (end == String::npos ? (base.has_authority ? "/" : "") : base.path.substr(0, end + 1)) + target.path;
            }
        }
    }
    translated_path = target.scheme.empty() ? "" : target.scheme + ':';
    if (target.has_authority) translated_path += "//" + target.authority;
    translated_path += NormalizeResourcePath(target.path);
    if (target.has_query) translated_path += '?' + EncodeResourceUrl(target.query, false);
    if (target.has_fragment) translated_path += '#' + EncodeResourceUrl(target.fragment, false);
}

void SystemInterface::ActivateKeyboard(URUI::Vector2f /*caret_position*/, float /*line_height*/) {}

void SystemInterface::DeactivateKeyboard() {}

} // namespace URUI
