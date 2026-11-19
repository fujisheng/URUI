#include "HtmlParser.h"
#include <gumbo.h>
#include <error.h>
#include <algorithm>
#include <cctype>
#include <csetjmp>
#include <cstddef>
#include <cstdlib>
#include <limits>
#include <new>
#include <string_view>

namespace URUI
{
    namespace
    {
#if defined(URUI_HTML_PARSER_TESTING)
        thread_local size_t outstandingAllocations = 0;
#endif
        // Gumbo assumes allocation succeeds. Track its C allocations so an allocator
        // failure can leave the C call with longjmp and reclaim even a partial parse.
        // No C++ object lives in the jumped-over C frames.
        struct ParserMemory
        {
            struct alignas(std::max_align_t) Block
            {
                Block* previous;
                Block* next;
                size_t size;
            };
            Block* first = nullptr;
            size_t count = 0;
            size_t bytes = 0;
            size_t peak = 0;
            size_t limit = std::numeric_limits<size_t>::max();
            std::jmp_buf failure;
            static void* Allocate(void* userdata, size_t size)
            {
                auto& memory = *static_cast<ParserMemory*>(userdata);
                if (memory.count >= memory.limit || size > std::numeric_limits<size_t>::max() - sizeof(Block))
                {
                    std::longjmp(memory.failure, 1);
                }
                auto* block = static_cast<Block*>(std::malloc(sizeof(Block) + size));
                if (!block)
                {
                    std::longjmp(memory.failure, 1);
                }
                block->previous = nullptr;
                block->next = memory.first;
                block->size = size;
                if (memory.first)
                {
                    memory.first->previous = block;
                }
                memory.first = block;
                ++memory.count;
                memory.bytes += size + sizeof(Block);
                memory.peak = std::max(memory.peak, memory.bytes);
#if defined(URUI_HTML_PARSER_TESTING)
                ++outstandingAllocations;
#endif
                return block + 1;
            }
            static void Free(void* userdata, void* pointer)
            {
                if (!pointer)
                {
                    return;
                }
                auto& memory = *static_cast<ParserMemory*>(userdata);
                auto* block = static_cast<Block*>(pointer) - 1;
                if (block->previous)
                {
                    block->previous->next = block->next;
                }
                else
                {
                    memory.first = block->next;
                }
                if (block->next)
                {
                    block->next->previous = block->previous;
                }
                memory.bytes -= block->size + sizeof(Block);
                std::free(block);
#if defined(URUI_HTML_PARSER_TESTING)
                --outstandingAllocations;
#endif
            }
            ~ParserMemory()
            {
                // The arena also avoids Gumbo's recursive tree destruction on hostile
                // deeply nested input. All Gumbo storage uses these callbacks.
                while (first)
                {
                    Free(this, first + 1);
                }
            }
        };

        String Lower(String value)
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
        bool OneOf(const String& value, std::string_view list)
        {
            const std::string_view name(value.data(), value.size());
            for (size_t start = 0; start < list.size();)
            {
                const auto end = list.find(' ', start);
                if (list.substr(start, end == list.npos ? list.size() - start : end - start) == name)
                {
                    return true;
                }
                if (end == list.npos)
                {
                    break;
                }
                start = end + 1;
            }
            return false;
        }
        bool IsVoid(const String& tag)
        {
            return OneOf(tag, "area base br col embed hr img input link meta param source track wbr");
        }
        bool Supported(const String& tag)
        {
            if (tag.size() > 5 && tag.compare(0, 5, "urui-") == 0)
            {
                return true;
            }
            return OneOf(
                tag,
                "html head body title meta link style template div span p a abbr b strong i em u s small sub sup "
                "br wbr hr pre code kbd samp var blockquote q cite address section article header footer main nav aside "
                "h1 h2 h3 h4 h5 h6 ul ol li dl dt dd img figure figcaption form input button select option optgroup textarea label "
                "progress table caption colgroup col thead tbody tfoot tr th td");
        }
        SourceSpan Span(GumboSourcePosition position, size_t length = 0)
        {
            return {position.offset, uint32_t(std::min(length, size_t(UINT32_MAX))), std::max(1u, position.line),
                    std::max(1u, position.column)};
        }
        void Error(HtmlParseResult& result, const String& url, const String& code, const String& message,
                   SourceSpan span = {})
        {
            if (result.diagnostics.size() < 64)
            {
                result.diagnostics.push_back({DiagnosticSeverity::Error, code, message, url, span});
            }
        }
        String Tag(const GumboElement& element)
        {
            auto tag = element.original_tag;
            gumbo_tag_from_original_text(&tag);
            return tag.length ? Lower(String(tag.data, tag.length)) : String(gumbo_normalized_tagname(element.tag));
        }
        bool CopyNode(const GumboNode& source, HtmlNode& target, HtmlParseResult& result, const String& url,
                      size_t depth)
        {
            if (depth > 512)
            {
                Error(result, url, "HTML_DEPTH", "HTML nesting exceeds 512 elements.");
                return false;
            }
            if (source.type == GUMBO_NODE_COMMENT || source.type == GUMBO_NODE_TEXT ||
                source.type == GUMBO_NODE_WHITESPACE)
            {
                target.kind = source.type == GUMBO_NODE_COMMENT ? HtmlNodeKind::Comment : HtmlNodeKind::Text;
                target.text = source.v.text.text;
                target.start = Span(source.v.text.start_pos, source.v.text.original_text.length);
                return true;
            }
            if (source.type != GUMBO_NODE_ELEMENT && source.type != GUMBO_NODE_TEMPLATE)
            {
                Error(result, url, "HTML_NODE", "This HTML node type is outside the URUI subset.");
                return false;
            }
            const auto& element = source.v.element;
            target.kind = source.type == GUMBO_NODE_TEMPLATE ? HtmlNodeKind::Template : HtmlNodeKind::Element;
            target.tag = Tag(element);
            target.start = Span(element.start_pos, element.original_tag.length);
            target.end = Span(element.end_pos, element.original_end_tag.length);
            if (element.tag_namespace != GUMBO_NAMESPACE_HTML || !Supported(target.tag))
            {
                Error(result, url, "HTML_ELEMENT", "Element <" + target.tag + "> is outside the URUI subset.",
                      target.start);
            }
            const auto allowedFlags = IsVoid(target.tag) ? GUMBO_INSERTION_IMPLICIT_END_TAG : GUMBO_INSERTION_NORMAL;
            if ((source.parse_flags & ~allowedFlags) != 0 || element.original_tag.length == 0)
            {
                Error(result, url, "HTML_REPAIR",
                      "HTML requires an implied, moved or repaired element <" + target.tag + ">.", target.start);
            }
            if (!IsVoid(target.tag) && element.original_end_tag.length == 0)
            {
                Error(result, url, "HTML_END_TAG", "Element <" + target.tag + "> requires an explicit closing tag.",
                      target.start);
            }
            for (unsigned i = 0; i < element.attributes.length; ++i)
            {
                const auto& attribute = *static_cast<const GumboAttribute*>(element.attributes.data[i]);
                if (attribute.attr_namespace != GUMBO_ATTR_NAMESPACE_NONE)
                {
                    Error(result, url, "HTML_ATTRIBUTE", "Namespaced attributes are outside the URUI subset.",
                          Span(attribute.name_start));
                }
                target.attributes.push_back(
                    {attribute.name, attribute.value, Span(attribute.name_start, attribute.original_name.length)});
            }
            if (target.tag == "style" || target.tag == "link")
            {
                for (const auto& attribute : target.attributes)
                {
                    if (attribute.name == "type" && !attribute.value.empty() && Lower(attribute.value) != "text/css")
                    {
                        Error(result, url, "HTML_CSS_TYPE",
                              "Stylesheets require the standard text/css MIME type or no type attribute.",
                              attribute.span);
                    }
                }
            }
            target.children.reserve(element.children.length);
            for (unsigned i = 0; i < element.children.length; ++i)
            {
                HtmlNode child;
                if (!CopyNode(*static_cast<const GumboNode*>(element.children.data[i]), child, result, url, depth + 1))
                {
                    return false;
                }
                target.children.push_back(std::move(child));
            }
            return true;
        }
        HtmlParseResult Parse(const String& input, const String& url, const String* context, size_t allocationLimit)
        {
            HtmlParseResult result;
            if (url.empty() || url.find('\0') != String::npos)
            {
                Error(result, url, "HTML_SOURCE", "A nonempty source URL is required.");
                return result;
            }
            if (input.size() > UINT32_MAX || input.find('\0') != String::npos)
            {
                Error(result, url, "HTML_INPUT", "HTML must be NUL-free UTF-8 with a representable source span.");
                return result;
            }
            try
            {
                // The state itself is not modified by setjmp: only its heap allocation
                // is changed by the C callbacks, so its cleanup remains well-defined.
                const auto memory = std::make_unique<ParserMemory>();
                memory->limit = allocationLimit;
                auto options = kGumboDefaultOptions;
                options.allocator = ParserMemory::Allocate;
                options.deallocator = ParserMemory::Free;
                options.userdata = memory.get();
                options.max_errors = 64;
                if (context)
                {
                    const String tag = Lower(*context);
                    if (!Supported(tag) || IsVoid(tag))
                    {
                        Error(result, url, "HTML_CONTEXT",
                              "Fragment context must be a supported non-void HTML element.");
                        return result;
                    }
                    options.fragment_context = gumbo_tag_enum(tag.c_str());
                }
                if (setjmp(memory->failure))
                {
                    result.stats = {memory->count, memory->peak};
                    Error(result, url, "HTML_MEMORY", "HTML parser allocation failed.");
                    return result;
                }
                const auto* output = gumbo_parse_with_options(&options, input.data(), input.size());
                result.stats = {memory->count, memory->peak};
                for (unsigned i = 0; i < output->errors.length; ++i)
                {
                    const auto& error = *static_cast<const GumboError*>(output->errors.data[i]);
                    Error(result, url, "HTML_SYNTAX",
                          "Invalid HTML syntax (parser error " + std::to_string(int(error.type)) + ").",
                          Span(error.position, 1));
                }
                if (!context)
                {
                    const auto& document = output->document->v.document;
                    if (!document.has_doctype || Lower(document.name) != "html" || *document.public_identifier ||
                        *document.system_identifier || document.doc_type_quirks_mode != GUMBO_DOCTYPE_NO_QUIRKS)
                    {
                        Error(result, url, "HTML_DOCTYPE", "A standard <!doctype html> declaration is required.");
                    }
                }
                auto document = std::make_unique<HtmlDocument>();
                document->kind = context ? HtmlDocumentKind::Fragment : HtmlDocumentKind::Document;
                document->sourceUrl = url;
                // Fragment parsing creates an internal html wrapper; it is not author
                // markup and is the only implied node exempt from strict validation.
                const auto& children =
                    context ? output->root->v.element.children : output->document->v.document.children;
                document->nodes.reserve(children.length);
                for (unsigned i = 0; i < children.length; ++i)
                {
                    HtmlNode node;
                    if (!CopyNode(*static_cast<const GumboNode*>(children.data[i]), node, result, url, 0))
                    {
                        break;
                    }
                    document->nodes.push_back(std::move(node));
                }
                if (result.diagnostics.empty())
                {
                    result.document = std::move(document);
                }
            }
            catch (const std::bad_alloc&)
            {
                result.document.reset();
                Error(result, url, "HTML_MEMORY", "HTML DTO allocation failed.");
            }
            return result;
        }
    }

    HtmlParseResult ParseHtmlDocument(const String& html, const String& sourceUrl)
    {
        return Parse(html, sourceUrl, nullptr, std::numeric_limits<size_t>::max());
    }
    HtmlParseResult ParseHtmlFragment(const String& html, const String& contextTag, const String& sourceUrl)
    {
        return Parse(html, sourceUrl, &contextTag, std::numeric_limits<size_t>::max());
    }
#if defined(URUI_HTML_PARSER_TESTING)
    HtmlParseResult HtmlParserTesting::ParseWithAllocationLimit(const String& html, const String& sourceUrl,
                                                                size_t allocationLimit)
    {
        return Parse(html, sourceUrl, nullptr, allocationLimit);
    }
    size_t HtmlParserTesting::OutstandingAllocations()
    {
        return outstandingAllocations;
    }
#endif
}
