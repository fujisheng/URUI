#include "CssTokenizer.h"
#include <algorithm>
#include <cstdio>

namespace URUI
{
    namespace
    {
        bool Space(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }
        int Hex(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }
            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }
            return -1;
        }
        bool NameStart(unsigned char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c >= 128;
        }
        bool Name(unsigned char c)
        {
            return NameStart(c) || c == '-' || (c >= '0' && c <= '9');
        }
        bool Digit(char c)
        {
            return c >= '0' && c <= '9';
        }
        bool StartsNumber(const String& input, size_t i)
        {
            if (input[i] == '+' || input[i] == '-')
            {
                ++i;
            }
            return i < input.size() &&
                   (Digit(input[i]) || (input[i] == '.' && i + 1 < input.size() && Digit(input[i + 1])));
        }
        size_t ConsumeNumber(const String& input, size_t i)
        {
            if (input[i] == '+' || input[i] == '-')
            {
                ++i;
            }
            while (i < input.size() && Digit(input[i]))
            {
                ++i;
            }
            if (i + 1 < input.size() && input[i] == '.' && Digit(input[i + 1]))
            {
                ++i;
                while (i < input.size() && Digit(input[i]))
                {
                    ++i;
                }
            }
            if (i < input.size() && (input[i] == 'e' || input[i] == 'E'))
            {
                size_t exponent = i + 1;
                if (exponent < input.size() && (input[exponent] == '+' || input[exponent] == '-'))
                {
                    ++exponent;
                }
                if (exponent < input.size() && Digit(input[exponent]))
                {
                    i = exponent + 1;
                    while (i < input.size() && Digit(input[i]))
                    {
                        ++i;
                    }
                }
            }
            return i;
        }
        void Utf8(String& text, uint32_t cp)
        {
            if (cp == 0 || cp > 0x10ffff || (cp >= 0xd800 && cp <= 0xdfff))
            {
                cp = 0xfffd;
            }
            if (cp < 0x80)
            {
                text += char(cp);
            }
            else if (cp < 0x800)
            {
                text += char(0xc0 | (cp >> 6));
                text += char(0x80 | (cp & 63));
            }
            else if (cp < 0x10000)
            {
                text += char(0xe0 | (cp >> 12));
                text += char(0x80 | ((cp >> 6) & 63));
                text += char(0x80 | (cp & 63));
            }
            else
            {
                text += char(0xf0 | (cp >> 18));
                text += char(0x80 | ((cp >> 12) & 63));
                text += char(0x80 | ((cp >> 6) & 63));
                text += char(0x80 | (cp & 63));
            }
        }
        size_t Escape(const String& input, size_t position, String& decoded)
        {
            ++position;
            if (position >= input.size())
            {
                Utf8(decoded, 0xfffd);
                return position;
            }
            if (input[position] == '\n' || input[position] == '\f' || input[position] == '\r')
            {
                if (input[position] == '\r' && position + 1 < input.size() && input[position + 1] == '\n')
                {
                    ++position;
                }
                return position + 1;
            }
            if (Hex(input[position]) < 0)
            {
                decoded += input[position];
                return position + 1;
            }
            uint32_t cp = 0;
            for (unsigned count = 0; position < input.size() && count < 6 && Hex(input[position]) >= 0;
                 ++count, ++position)
            {
                cp = (cp << 4) | unsigned(Hex(input[position]));
            }
            Utf8(decoded, cp);
            if (position < input.size() && Space(input[position]))
            {
                if (input[position] == '\r' && position + 1 < input.size() && input[position + 1] == '\n')
                {
                    ++position;
                }
                ++position;
            }
            return position;
        }
    }
    String AsciiLowerCss(String value)
    {
        for (char& c : value)
        {
            if (c >= 'A' && c <= 'Z')
            {
                c = char(c + ('a' - 'A'));
            }
        }
        return value;
    }
    String SerializeCssIdentifier(const String& value)
    {
        String result;
        for (size_t i = 0; i < value.size(); ++i)
        {
            const unsigned char c = uint8_t(value[i]);
            if (c < 32 || c == 127 || (Digit(char(c)) && (i == 0 || (i == 1 && value[0] == '-'))))
            {
                char escaped[12];
                std::snprintf(escaped, sizeof(escaped), "\\%x ", unsigned(c));
                result += escaped;
            }
            else
            {
                if (!Name(c) || (value.size() == 1 && c == '-'))
                {
                    result += '\\';
                }
                result += char(c);
            }
        }
        return result;
    }
    String DecodeCssIdentifier(const String& raw)
    {
        String decoded;
        for (size_t i = 0; i < raw.size();)
        {
            if (raw[i] == '\\')
            {
                i = Escape(raw, i, decoded);
            }
            else
            {
                decoded += raw[i++];
            }
        }
        return decoded;
    }
    bool ParseCssFontFamilies(const String& value, Vector<CssFontFamily>& families)
    {
        Vector<Diagnostic> diagnostics;
        const auto tokens = TokenizeCss(value, "urui:font-family", diagnostics);
        if (!diagnostics.empty())
        {
            return false;
        }
        Vector<CssFontFamily> parsed;
        String name;
        int parts = 0;
        bool quoted = false;
        auto Save = [&]()
        {
            if (parts == 0)
            {
                return false;
            }
            const auto lower = AsciiLowerCss(name);
            int generic = -1;
            if (!quoted && parts == 1)
            {
                if (lower == "serif")
                {
                    generic = 0;
                }
                else if (lower == "sans-serif" || lower == "system-ui")
                {
                    generic = 1;
                }
                else if (lower == "monospace")
                {
                    generic = 2;
                }
                else if (lower == "cursive" || lower == "fantasy")
                {
                    generic = 3;
                }
            }
            parsed.push_back({lower, generic});
            name.clear();
            parts = 0;
            quoted = false;
            return true;
        };
        for (const auto& token : tokens)
        {
            if (token.kind == CssTokenKind::Whitespace)
            {
                continue;
            }
            if (token.kind == CssTokenKind::Delimiter && token.value == ",")
            {
                if (!Save())
                {
                    return false;
                }
                continue;
            }
            if (token.kind == CssTokenKind::String)
            {
                if (parts != 0)
                {
                    return false;
                }
                quoted = true;
                name = token.value;
                ++parts;
            }
            else if (token.kind == CssTokenKind::Ident && !quoted)
            {
                const auto lower = AsciiLowerCss(token.value);
                if (lower == "inherit" || lower == "initial" || lower == "unset" || lower == "revert" ||
                    lower == "revert-layer" || lower == "default")
                {
                    return false;
                }
                if (parts != 0)
                {
                    name += ' ';
                }
                name += token.value;
                ++parts;
            }
            else
            {
                return false;
            }
        }
        if (!Save())
        {
            return false;
        }
        families = std::move(parsed);
        return true;
    }

    bool DecodeCssUrl(const CssToken& token, String& url)
    {
        if (token.kind != CssTokenKind::Url)
        {
            return false;
        }
        const auto open = token.value.find('(');
        if (open == String::npos || token.value.empty() || token.value.back() != ')')
        {
            return false;
        }
        auto content = token.value.substr(open + 1, token.value.size() - open - 2);
        size_t begin = 0;
        while (begin < content.size() && Space(content[begin]))
        {
            ++begin;
        }
        content = content.substr(begin);
        size_t end = content.size();
        for (size_t i = 0; i < content.size();)
        {
            const auto c = uint8_t(content[i]);
            if (c == '\\')
            {
                if (i + 1 == content.size() || content[i + 1] == '\n' || content[i + 1] == '\r' ||
                    content[i + 1] == '\f')
                {
                    return false;
                }
                String ignored;
                i = Escape(content, i, ignored);
            }
            else
            {
                if (Space(char(c)))
                {
                    end = i;
                    for (; i < content.size(); ++i)
                    {
                        if (!Space(content[i]))
                        {
                            return false;
                        }
                    }
                    break;
                }
                if (c < 0x20 || c == 0x7f || c == '\'' || c == '"' || c == '(' || c == ')')
                {
                    return false;
                }
                ++i;
            }
        }
        url = DecodeCssIdentifier(content.substr(0, end));
        return true;
    }

    Vector<CssToken> TokenizeCss(const String& css, const String& sourceUrl, Vector<Diagnostic>& diagnostics,
                                 size_t initial_offset)
    {
        Vector<CssToken> tokens;
        uint32_t line = 1;
        uint32_t column = 1;
        for (size_t i = initial_offset; i < css.size();)
        {
            const size_t start = i;
            const SourceSpan begin{uint32_t(i), 0, line, column};
            auto kind = CssTokenKind::Delimiter;
            String decoded;
            if (css[i] == '/' && i + 1 < css.size() && css[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < css.size() && !(css[i] == '*' && css[i + 1] == '/'))
                {
                    ++i;
                }
                if (i + 1 < css.size())
                {
                    i += 2;
                }
                else
                {
                    i = css.size();
                    diagnostics.push_back(
                        {DiagnosticSeverity::Warning, "CSS_COMMENT", "Unterminated CSS comment.", sourceUrl, begin});
                }
                // A comment is removed, not converted to whitespace or merged into an ident.
                kind = CssTokenKind::Whitespace;
            }
            else if (Space(css[i]))
            {
                kind = CssTokenKind::Whitespace;
                while (i < css.size() && Space(css[i]))
                {
                    ++i;
                }
                decoded = " ";
            }
            else if (css[i] == '\'' || css[i] == '"')
            {
                kind = CssTokenKind::String;
                const char quote = css[i++];
                bool closed = false;
                while (i < css.size())
                {
                    if (css[i] == quote)
                    {
                        ++i;
                        closed = true;
                        break;
                    }
                    if (css[i] == '\n' || css[i] == '\r' || css[i] == '\f')
                    {
                        break;
                    }
                    if (css[i] == '\\')
                    {
                        i = Escape(css, i, decoded);
                    }
                    else
                    {
                        decoded += css[i++];
                    }
                }
                if (!closed)
                {
                    kind = CssTokenKind::BadString;
                    diagnostics.push_back(
                        {DiagnosticSeverity::Warning, "CSS_STRING", "Invalid CSS string.", sourceUrl, begin});
                }
            }
            else if (StartsNumber(css, i))
            {
                kind = CssTokenKind::Number;
                i = ConsumeNumber(css, i);
                decoded = css.substr(start, i - start);
                if (i < css.size() && (NameStart(uint8_t(css[i])) || css[i] == '-' || css[i] == '\\'))
                {
                    kind = CssTokenKind::Dimension;
                    while (i < css.size())
                    {
                        if (css[i] == '\\')
                        {
                            i = Escape(css, i, decoded);
                        }
                        else if (Name(uint8_t(css[i])))
                        {
                            decoded += css[i++];
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (i < css.size() && css[i] == '%')
                {
                    kind = CssTokenKind::Percentage;
                    decoded += css[i++];
                }
            }
            else if (NameStart(uint8_t(css[i])) || css[i] == '\\' ||
                     (css[i] == '-' && i + 1 < css.size() &&
                      (NameStart(uint8_t(css[i + 1])) || css[i + 1] == '-' || css[i + 1] == '\\')))
            {
                kind = CssTokenKind::Ident;
                while (i < css.size())
                {
                    if (css[i] == '\\')
                    {
                        i = Escape(css, i, decoded);
                    }
                    else if (Name(uint8_t(css[i])))
                    {
                        decoded += css[i++];
                    }
                    else
                    {
                        break;
                    }
                }
                if (AsciiLowerCss(decoded) == "url" && i < css.size() && css[i] == '(')
                {
                    size_t next = i + 1;
                    while (next < css.size() && Space(css[next]))
                    {
                        ++next;
                    }
                    if (next < css.size() && css[next] != '\'' && css[next] != '"')
                    {
                        kind = CssTokenKind::Url;
                        ++i;
                        while (i < css.size())
                        {
                            if (css[i] == '\\')
                            {
                                String ignored;
                                i = Escape(css, i, ignored);
                            }
                            else if (css[i++] == ')')
                            {
                                break;
                            }
                        }
                        decoded = css.substr(start, i - start);
                    }
                }
            }
            else
            {
                decoded += css[i++];
            }
            SourceSpan span = begin;
            span.length = uint32_t(i - start);
            if (!(css[start] == '/' && start + 1 < css.size() && css[start + 1] == '*'))
            {
                tokens.push_back({kind, decoded, css.substr(start, i - start), span});
            }
            for (size_t j = start; j < i; ++j)
            {
                if (css[j] == '\r' || css[j] == '\f' || css[j] == '\n')
                {
                    if (css[j] == '\n' && j > start && css[j - 1] == '\r')
                    {
                        continue;
                    }
                    ++line;
                    column = 1;
                }
                else if ((uint8_t(css[j]) & 0xc0) != 0x80)
                {
                    ++column;
                }
            }
        }
        return tokens;
    }
    bool SplitCssNumericValue(const String& value, String& number, String& unit)
    {
        if (value.empty() || !StartsNumber(value, 0))
        {
            return false;
        }
        size_t i = ConsumeNumber(value, 0);
        number = value.substr(0, i);
        unit.clear();
        if (i == value.size())
        {
            return true;
        }
        if (value[i] == '%' && i + 1 == value.size())
        {
            unit = "%";
            return true;
        }
        if (!NameStart(uint8_t(value[i])) && value[i] != '-' && value[i] != '\\')
        {
            return false;
        }
        while (i < value.size())
        {
            if (value[i] == '\\')
            {
                i = Escape(value, i, unit);
            }
            else if (Name(uint8_t(value[i])))
            {
                unit += value[i++];
            }
            else
            {
                return false;
            }
        }
        unit = AsciiLowerCss(std::move(unit));
        return true;
    }
    bool ScanCssVariables(const String& value, Vector<CssVariableReference>& references)
    {
        Vector<Diagnostic> diagnostics;
        const auto tokens = TokenizeCss(value, "urui:variable", diagnostics);
        for (size_t i = 0; i + 1 < tokens.size(); ++i)
        {
            if (tokens[i].kind != CssTokenKind::Ident || AsciiLowerCss(tokens[i].value) != "var" ||
                tokens[i + 1].kind != CssTokenKind::Delimiter || tokens[i + 1].value != "(" ||
                tokens[i].span.byteOffset + tokens[i].span.length != tokens[i + 1].span.byteOffset)
            {
                continue;
            }
            // A hash/at-keyword token followed by a block is not a var() function.
            if (i > 0 && tokens[i - 1].kind == CssTokenKind::Delimiter &&
                (tokens[i - 1].value == "#" || tokens[i - 1].value == "@") &&
                tokens[i - 1].span.byteOffset + tokens[i - 1].span.length == tokens[i].span.byteOffset)
            {
                continue;
            }
            size_t name = i + 2;
            while (name < tokens.size() && tokens[name].kind == CssTokenKind::Whitespace)
            {
                ++name;
            }
            if (name == tokens.size() || tokens[name].kind != CssTokenKind::Ident || tokens[name].value.size() <= 2 ||
                tokens[name].value.compare(0, 2, "--") != 0)
            {
                return false;
            }
            size_t next = name + 1;
            while (next < tokens.size() && tokens[next].kind == CssTokenKind::Whitespace)
            {
                ++next;
            }
            if (next == tokens.size() || tokens[next].kind != CssTokenKind::Delimiter ||
                (tokens[next].value != "," && tokens[next].value != ")"))
            {
                return false;
            }
            const bool fallback = tokens[next].value == ",";
            unsigned depth = 1;
            size_t close = i + 2;
            for (; close < tokens.size(); ++close)
            {
                if (tokens[close].kind != CssTokenKind::Delimiter)
                {
                    continue;
                }
                if (tokens[close].value == "(")
                {
                    ++depth;
                }
                else if (tokens[close].value == ")" && --depth == 0)
                {
                    break;
                }
            }
            if (close == tokens.size())
            {
                return false;
            }
            references.push_back({tokens[name].value, tokens[i].span.byteOffset,
                                  tokens[close].span.byteOffset + tokens[close].span.length,
                                  fallback ? tokens[next].span.byteOffset + tokens[next].span.length : 0, fallback});
        }
        return true;
    }
}
