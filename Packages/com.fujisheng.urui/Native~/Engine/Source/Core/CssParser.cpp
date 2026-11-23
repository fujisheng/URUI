#include "CssParser.h"
#include <algorithm>

namespace URUI
{
    namespace
    {
        bool Is(const CssToken& token, const char* value)
        {
            return token.kind == CssTokenKind::Delimiter && token.value == value;
        }
        void Trim(const Vector<CssToken>& tokens, size_t& begin, size_t& end)
        {
            while (begin < end && tokens[begin].kind == CssTokenKind::Whitespace)
            {
                ++begin;
            }
            while (end > begin && tokens[end - 1].kind == CssTokenKind::Whitespace)
            {
                --end;
            }
        }
        String Join(const Vector<CssToken>& tokens, size_t begin, size_t end, bool decodeIdents = false)
        {
            String value;
            for (size_t i = begin; i < end; ++i)
            {
                // Removed comments must not turn separate names/numbers into one token,
                // or an ident followed by '(' into a function token.
                auto namePart = [](char c)
                {
                    return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-' ||
                           c == '_' || c == '.' || uint8_t(c) >= 128;
                };
                if (i > begin &&
                    tokens[i].span.byteOffset > tokens[i - 1].span.byteOffset + tokens[i - 1].span.length &&
                    ((namePart(tokens[i - 1].raw.back()) && namePart(tokens[i].raw.front())) ||
                     (tokens[i - 1].kind == CssTokenKind::Ident && Is(tokens[i], "("))))
                {
                    value += "/**/";
                }
                if (decodeIdents && tokens[i].kind == CssTokenKind::Ident)
                {
                    value += SerializeCssIdentifier(tokens[i].value);
                }
                else if (decodeIdents && tokens[i].kind == CssTokenKind::Dimension)
                {
                    value += tokens[i].value;
                }
                else
                {
                    value += tokens[i].raw;
                }
            }
            return value;
        }
        void Warn(Vector<Diagnostic>& diagnostics, const String& url, const CssToken& token, const String& code,
                  const String& message)
        {
            diagnostics.push_back({DiagnosticSeverity::Warning, code, message, url, token.span});
        }
        Vector<CssDeclaration> Declarations(const Vector<CssToken>& tokens, size_t begin, size_t end, const String& url,
                                            Vector<Diagnostic>& diagnostics)
        {
            Vector<CssDeclaration> declarations;
            uint64_t sourceOrder = 0;
            while (begin < end)
            {
                if (tokens[begin].kind == CssTokenKind::Whitespace || Is(tokens[begin], ";"))
                {
                    ++begin;
                    continue;
                }
                const size_t start = begin;
                Vector<char> brackets;
                bool bad = false;
                for (; begin < end; ++begin)
                {
                    const auto& token = tokens[begin];
                    if (brackets.empty() && Is(token, ";"))
                    {
                        break;
                    }
                    if (token.kind == CssTokenKind::BadString)
                    {
                        bad = true;
                    }
                    if (token.kind != CssTokenKind::Delimiter)
                    {
                        continue;
                    }
                    const char c = token.value[0];
                    if (c == '(' || c == '[' || c == '{')
                    {
                        brackets.push_back(c);
                    }
                    else if (c == ')' || c == ']' || c == '}')
                    {
                        const char expected = c == ')' ? '(' : c == ']' ? '[' : '{';
                        if (brackets.empty() || brackets.back() != expected)
                        {
                            bad = true;
                        }
                        else
                        {
                            brackets.pop_back();
                        }
                    }
                }
                size_t stop = begin;
                if (begin < end)
                {
                    ++begin;
                }
                size_t name = start;
                Trim(tokens, name, stop);
                size_t colon = name + 1;
                while (colon < stop && tokens[colon].kind == CssTokenKind::Whitespace)
                {
                    ++colon;
                }
                if (bad || !brackets.empty() || name >= stop || tokens[name].kind != CssTokenKind::Ident ||
                    colon >= stop || !Is(tokens[colon], ":"))
                {
                    Warn(diagnostics, url, tokens[start], "CSS_DECLARATION", "Invalid CSS declaration was ignored.");
                    continue;
                }
                size_t value = colon + 1;
                Trim(tokens, value, stop);
                bool important = false;
                if (stop > value && tokens[stop - 1].kind == CssTokenKind::Ident &&
                    AsciiLowerCss(tokens[stop - 1].value) == "important")
                {
                    size_t bang = stop - 1;
                    while (bang > value && tokens[bang - 1].kind == CssTokenKind::Whitespace)
                    {
                        --bang;
                    }
                    if (bang > value && Is(tokens[bang - 1], "!"))
                    {
                        important = true;
                        stop = bang - 1;
                        Trim(tokens, value, stop);
                    }
                }
                String property = tokens[name].value;
                if (property.compare(0, 2, "--") != 0)
                {
                    property = AsciiLowerCss(std::move(property));
                }
                SourceSpan span = tokens[name].span;
                const size_t last = begin > start && Is(tokens[begin - 1], ";") ? begin - 1 : begin;
                if (last > start)
                {
                    span.length = tokens[last - 1].span.byteOffset + tokens[last - 1].span.length - span.byteOffset;
                }
                declarations.push_back(
                    {std::move(property), Join(tokens, value, stop, true), important, sourceOrder++, span});
            }
            return declarations;
        }
        Vector<CssRule> Rules(const String& source, const Vector<CssToken>& tokens, size_t begin, size_t end,
                              const String& url, Vector<Diagnostic>& diagnostics, unsigned depth)
        {
            Vector<CssRule> rules;
            if (depth > 128)
            {
                if (begin < end)
                {
                    Warn(diagnostics, url, tokens[begin], "CSS_DEPTH", "CSS rule nesting exceeds the supported limit.");
                }
                return rules;
            }
            while (begin < end)
            {
                if (tokens[begin].kind == CssTokenKind::Whitespace || Is(tokens[begin], ";"))
                {
                    ++begin;
                    continue;
                }
                const size_t start = begin;
                CssRule rule;
                rule.span = tokens[start].span;
                if (Is(tokens[begin], "@"))
                {
                    ++begin;
                    if (begin == end || tokens[begin].kind != CssTokenKind::Ident)
                    {
                        Warn(diagnostics, url, tokens[start], "CSS_AT_RULE", "Invalid CSS at-rule was ignored.");
                        continue;
                    }
                    rule.atRule = AsciiLowerCss(tokens[begin++].value);
                }
                const size_t prelude = begin;
                Vector<char> brackets;
                while (begin < end)
                {
                    if (brackets.empty() && (Is(tokens[begin], "{") || Is(tokens[begin], ";")))
                    {
                        break;
                    }
                    if (Is(tokens[begin], "(") || Is(tokens[begin], "["))
                    {
                        brackets.push_back(tokens[begin].value[0]);
                    }
                    else if ((Is(tokens[begin], ")") || Is(tokens[begin], "]")) && !brackets.empty())
                    {
                        brackets.pop_back();
                    }
                    ++begin;
                }
                size_t preludeBegin = prelude;
                size_t preludeEnd = begin;
                Trim(tokens, preludeBegin, preludeEnd);
                rule.prelude = Join(tokens, preludeBegin, preludeEnd);
                if (begin == end)
                {
                    Warn(diagnostics, url, tokens[start], "CSS_RULE", "CSS rule without a block was ignored.");
                    break;
                }
                if (Is(tokens[begin], ";"))
                {
                    ++begin;
                    if (!rule.atRule.empty())
                    {
                        rules.push_back(std::move(rule));
                    }
                    else
                    {
                        Warn(diagnostics, url, tokens[start], "CSS_RULE", "Qualified CSS rule requires a block.");
                    }
                    continue;
                }
                const size_t open = begin++;
                const size_t blockStart = begin;
                unsigned level = 1;
                for (; begin < end; ++begin)
                {
                    if (Is(tokens[begin], "{"))
                    {
                        ++level;
                    }
                    else if (Is(tokens[begin], "}") && --level == 0)
                    {
                        break;
                    }
                }
                const size_t close = begin;
                const size_t blockEnd = close < end ? tokens[close].span.byteOffset : source.size();
                if (rule.atRule == "media" || rule.atRule == "keyframes" || rule.atRule == "supports")
                {
                    rule.children = Rules(source, tokens, blockStart, close, url, diagnostics, depth + 1);
                }
                else
                {
                    rule.declarations = Declarations(tokens, blockStart, close, url, diagnostics);
                }
                rule.span.length = uint32_t(blockEnd - rule.span.byteOffset + (close < end ? 1 : 0));
                if (begin < end)
                {
                    ++begin;
                }
                rules.push_back(std::move(rule));
            }
            return rules;
        }
    }
    Vector<CssDeclaration> ParseCssDeclarations(const String& css, const String& sourceUrl,
                                                Vector<Diagnostic>& diagnostics)
    {
        const auto tokens = TokenizeCss(css, sourceUrl, diagnostics);
        return Declarations(tokens, 0, tokens.size(), sourceUrl, diagnostics);
    }
    CssParseResult ParseCss(const String& css, const String& sourceUrl)
    {
        CssParseResult result;
        // 样式表是 UTF-8 字节输入；移除签名但保留其后的原始字节偏移。
        // 声明/变量中的 U+FEFF 仍保留其正常的标识符语义。
        const size_t start = css.compare(0, 3, "\xef\xbb\xbf") == 0 ? 3 : 0;
        const auto tokens = TokenizeCss(css, sourceUrl, result.diagnostics, start);
        result.rules = Rules(css, tokens, 0, tokens.size(), sourceUrl, result.diagnostics, 0);
        return result;
    }
}
