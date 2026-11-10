#include "test_document.h"
#include "urui.h"
#include "style_properties.h"
#include <cassert>
#include <chrono>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iostream>
#include <iterator>
#include <thread>
#include <vector>
struct Snapshot {
    uint64_t revision = 0;
    std::vector<UruiStyleValue> values;
    std::vector<char> names;
    explicit Snapshot(UruiDocumentHandle owner, uint64_t token) {
        int count = 0, bytes = 0;
        assert(urui_element_style_snapshot(owner, token, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) == -3);
        values.resize(count); names.resize(bytes);
        assert(urui_element_style_snapshot(owner, token, revision, values.data(), count, names.data(), bytes, &revision, &count, &bytes) == 1);
    }
    const UruiStyleValue& Get(const char* name) const {
        for (const auto& value : values) if (!std::strcmp(names.data()+value.name_offset, name)) return value;
        std::cerr << "Missing typed style: " << name << std::endl; std::abort();
    }
};
int main(int argc, char** argv) {
    assert(argc == 2);
    std::ifstream input(argv[1]); const std::string markup((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    assert(!markup.empty()); assert(urui_create(400, 200) == 1); const auto owner = LoadTestDocument(markup.c_str(), argv[1]); urui_update();
    const auto root = urui_document_root(owner); const auto child = urui_element_find(owner, root, "child", nullptr);
    const auto animated = urui_element_find(owner, root, "animated", nullptr); assert(child && animated);
    Snapshot initial(owner, child);
    assert((initial.Get("--test-edge").kind>>8)==2);
    assert((initial.Get("--optional").kind&255)==8);
    assert(!std::strcmp(initial.names.data() + initial.Get("--optional").text_offset, "auto"));
    assert(urui_style_api_version()==4);assert((initial.Get("--test-edge").kind&255)==8);assert(!std::strcmp(initial.names.data()+initial.Get("--test-edge").text_offset,"top"));
    assert((initial.Get("--fx-progress").kind&255)==1 && initial.Get("--fx-progress").x == 0);
    assert(std::abs(initial.Get("--inherited").x-.6f) < 1e-6f);
    assert(initial.Get("opacity").x == 0);
    assert((initial.Get("width").kind&255)==2 && initial.Get("width").x == 120);
    assert((initial.Get("--padding").kind&255)==2 && initial.Get("--padding").x == 4);
    assert((initial.Get("--zero").kind&255)==3 && initial.Get("--zero").x == 0);
    assert((initial.Get("--tint").kind&255)==4 && std::abs(initial.Get("--tint").x-18.f/255) < 1e-6f);
    Snapshot unchanged(owner, child); assert(unchanged.revision == initial.revision);
    const auto bare = urui_element_find(owner, root, "bare", nullptr); Snapshot bare_initial(owner, bare); assert(bare_initial.Get("--fx-progress").x == 0);
    assert((bare_initial.Get("font-size").kind&255)==2 && bare_initial.Get("font-size").x == 24);
    assert((bare_initial.Get("line-height").kind&255)==2 && std::abs(bare_initial.Get("line-height").x-28.8f) < .001f);
    assert(urui_document_add_stylesheet(owner, "@property --fx-progress {syntax:\"<number>\";inherits:false;initial-value:0.4;}","ui/inline.css",0) == 1); urui_update();
    Snapshot reloaded(owner, child); assert(reloaded.Get("--fx-progress").x == .4f && reloaded.Get("opacity").x == .4f);
    assert(Snapshot(owner, bare).Get("--fx-progress").x == .4f);
    assert(urui_document_add_stylesheet(owner, "@property --fx-progress {syntax:\"<number>\";inherits:false;initial-value:0;}","ui/inline.css",0) == 1); urui_update();

    std::this_thread::sleep_for(std::chrono::milliseconds(50)); urui_update();
    Snapshot midpoint(owner, animated); assert(midpoint.Get("--fx-progress").x > 0 && midpoint.Get("--fx-progress").x < 1);
    float final_progress = 0;
    for (int frame = 0; frame < 100 && final_progress < 1; frame++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(20)); urui_update();
        final_progress = Snapshot(owner, animated).Get("--fx-progress").x;
    }
    assert(final_progress == 1);
    const float precise = .123456789f;
    assert(urui_element_number(owner, child, "--fx-progress", precise, 0) == 1); urui_update();
    Snapshot written(owner, child); assert(written.Get("--fx-progress").x == precise);
    assert(written.Get("opacity").x == precise);
    assert(urui_element_number(owner, child, "--fx-progress", precise, 0) == 1); urui_update();
    assert(Snapshot(owner, child).revision == written.revision);
    assert(urui_element_number(owner, child, "--fx-progress", 0, 0) == 1); urui_update();
    assert(urui_element_number(owner, child, "--offset", 4.125f, 3) == 1);
    assert(urui_document_add_stylesheet(owner, "#child{height:var(--offset);margin:var(--offset);}","ui/inline.css",0) == 1); urui_update();
    assert(std::abs(Snapshot(owner, child).Get("height").x-4.125f) < .001f);
    assert(urui_document_add_stylesheet(owner, "@property --fx-progress {syntax:\"<number>\";inherits:true;initial-value:0.5;} #child{width:80px;}","ui/inline.css",0) == -2);
    urui_update(); Snapshot kept(owner, child); assert(kept.Get("width").x == 120 && kept.Get("--fx-progress").x == 0);
    assert(urui_document_add_stylesheet(owner, "@property --fx-progress {syntax:\"<number>\";inherits:false;initial-value:1e100;}","ui/inline.css",0) == -2);
    assert(urui_document_add_stylesheet(owner, "@property --new {syntax:\"<number>\";inherits:false;initial-value:0;} @property --fx-progress {syntax:\"<number>\";inherits:true;initial-value:0;}","ui/inline.css",0) == -2);
    assert(urui_document_add_stylesheet(owner, "@property --new {syntax:\"<number>\";inherits:true;initial-value:0.4;} #child{height:50px;}","ui/inline.css",0) == 1);
    urui_update(); Snapshot added(owner, child); assert(std::abs(added.Get("--new").x-.4f) < 1e-6f && added.Get("height").x == 50);
    char diagnostic[4096]; int required = 0;
    assert(urui_validate_stylesheet("busy.css", "div{opacity:.4;}", diagnostic, 4096, &required) == 0);
    assert(urui_document_root(owner) && Snapshot(owner, child).Get("width").x == 120);
    assert(urui_document_add_stylesheet(owner, "@property --optional {syntax:'<number> | auto';inherits:false;initial-value:.25;} #child{--optional:-.25;}", "ui/optional.css", 0) == 1);
    urui_update(); assert((Snapshot(owner, child).Get("--optional").kind & 255) == 1 && Snapshot(owner, child).Get("--optional").x == -.25f);
    assert(urui_document_close(owner) == 1); urui_update();
    uint64_t revision; int count, bytes;
    assert(urui_element_style_snapshot(owner, child, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) == -1);
    urui_destroy();
    assert(urui_validate_stylesheet("valid.css", "div{--theme:blue;opacity:.4;}", diagnostic,4096,&required) == 0);
    assert(urui_validate_stylesheet("broken.css", "div{unregistered-thing:4px;}", diagnostic,4096,&required) > 0);
    assert(std::strstr(diagnostic, "broken.css"));
    assert(std::strstr(diagnostic, "\"diagnostics\":[{") && std::strstr(diagnostic, "\"byteOffset\":"));
    assert(urui_validate_stylesheet("invalid-schema.css", "@property --x {syntax:\"<angle>\";inherits:false;initial-value:4deg;}",diagnostic,4096,&required) > 0);
    std::cout << "registered-number, inheritance, var, typed snapshots, animation, atomic import and author diagnostics passed" << std::endl;
}
