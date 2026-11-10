#include "TextLayoutProbe.h"
#include <URUI/Core.h>
#include <URUI/Core/FontMetrics.h>
#include <cstring>
#include <vector>

extern "C" int urui_validation_text_lines(const char* source_url, const char* id, UruiValidationTextLine* output, int capacity)
try {
    if (!source_url || !*source_url || !id || !*id || capacity < 0 || (!output && capacity)) return -1;
    auto* context = URUI::GetContext("urui");
    if (!context) return -2;
    URUI::ElementDocument* document = nullptr;
    for (int i = 0; i < context->GetNumDocuments(); ++i) {
        auto* candidate = context->GetDocument(i);
        if (candidate->GetSourceURL() != source_url) continue;
        if (document) return -2; // URL 歧义不能静默选择另一个页面。
        document = candidate;
    }
    auto* target = document ? document->GetElementById(id) : nullptr;
    if (!target) return -2;
    std::vector<UruiValidationTextLine> lines;
    std::vector<URUI::Element*> pending{target};
    while (!pending.empty()) {
        auto* element = pending.back(); pending.pop_back();
        if (auto* text = urui_dynamic_cast<URUI::ElementText*>(element); text && !text->GetLines().empty()) {
            if (!text->GetFontFaceHandle()) return -3;
            const auto& metrics = URUI::GetFontEngineInterface()->GetFontMetrics(text->GetFontFaceHandle());
            const auto line_height = text->GetComputedValues().line_height().value;
            const auto offset = text->GetAbsoluteOffset();
            for (const auto& line : text->GetLines()) {
                if (line.width <= 0 || line.text.empty()) continue;
                const auto baseline = offset + line.position;
                const auto leading = (line_height - metrics.ascent - metrics.descent) * .5f;
                lines.push_back({baseline.x, baseline.y - metrics.ascent - leading, float(line.width), line_height, baseline.y});
            }
        }
        for (int i = element->GetNumChildren() - 1; i >= 0; --i) pending.push_back(element->GetChild(i));
        if (lines.size() > 65536 || pending.size() > 65536) return -4;
    }
    if (capacity >= int(lines.size()) && !lines.empty()) std::memcpy(output, lines.data(), lines.size() * sizeof(lines[0]));
    return int(lines.size());
} catch (...) { return -8; }
