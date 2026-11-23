#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include <cassert>
#include <chrono>
#include <thread>
#include <fstream>
#include <iterator>
#include <map>
#include <string>
#include <iostream>
std::map<uint64_t, std::string> created;
std::map<uint64_t, std::string> ended;
int Callback(const URUIElementEvent* event)
{
    if (event->type == 17 || event->type == 19)
    {
        char name[128];
        int required = 0;
        uint64_t id;
        int iterations;
        assert(urui_animation_current_event(g_test_surface, event->document, name, 128, &required, &id, &iterations) ==
               URUI_OK);
        assert(id && iterations == 1);
        (event->type == 19 ? created : ended)[id] = name;
    }
    return 0;
}
int main(int argc, char** argv)
{
    assert(argc == 2);
    std::ifstream input(argv[1]);
    std::string markup((std::istreambuf_iterator<char>(input)), {});
    assert(urui_animation_api_version() == 2);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    const auto owner = LoadTestDocument(g_test_surface, markup.c_str(), argv[1]);
    assert(urui_set_event_callback(g_test_surface, Callback) == 1);
    assert(urui_document_listen(g_test_surface, owner) == 1);
    urui_surface_update(g_test_surface);
    auto epoch = urui_animation_next_identity();
    assert(urui_element_class(g_test_surface, owner, urui_document_root(g_test_surface, owner), "running", 1) == 1);
    for (int frame = 0; frame < 60 && ended.size() < 2; frame++)
    {
        urui_surface_update(g_test_surface);
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    assert(created.size() == 2 && ended.size() == 2);
    for (auto entry : created)
    {
        assert(entry.first >= epoch && ended.at(entry.first) == entry.second);
    }
    assert(urui_animation_next_identity() > epoch);
    assert(urui_document_close(g_test_surface, owner) == 1);
    urui_surface_destroy(g_test_surface);
    created.clear();
    ended.clear();
    {
        assert(urui_surface_create(400, 200, &g_test_surface) == 1);
        assert(urui_ui_clock_api_version() == 2);
        assert(urui_surface_set_ui_time(g_test_surface, 0) == 1);
        const auto owner = LoadTestDocument(g_test_surface, markup.c_str(), argv[1]);
        assert(urui_set_event_callback(g_test_surface, Callback) == 1);
        assert(urui_document_listen(g_test_surface, owner) == 1);
        urui_surface_update(g_test_surface);
        assert(urui_element_class(g_test_surface, owner, urui_document_root(g_test_surface, owner), "running", 1) == 1);
        for (int frame = 0; frame < 8; frame++)
        {
            urui_surface_update(g_test_surface);
        }
        assert(created.size() == 2 && ended.empty());
        for (int frame = 1; frame <= 30; frame++)
        {
            assert(urui_surface_set_ui_time(g_test_surface, frame * .02) == 1);
            urui_surface_update(g_test_surface);
        }
        assert(ended.size() == 2);
        urui_surface_destroy(g_test_surface);
        std::cout << "named track identities and document-owned event packets passed\n";
    }
}
