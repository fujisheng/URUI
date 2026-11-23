// Link the bridge into this executable so allocation failures cover the C
// boundary and the statically linked engine without a production test hook.
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <new>
#if defined(__APPLE__) || defined(__linux__)
#include <execinfo.h>
#endif

namespace
{
    struct alignas(std::max_align_t) Allocation
    {
        bool tracked;
        Allocation* previous;
        Allocation* next;
        void* stack[12];
        int stack_size;
        size_t bytes;
    };
    Allocation* live = nullptr;
    bool trace_allocations = false;
    void* failure_stack[16];
    int failure_stack_size = 0;
    bool track_allocations = false;
    size_t allocation_count = 0;
    size_t outstanding = 0;
    size_t fail_at = 0;
    bool persistent_failure = false;
}

void* operator new(size_t size)
{
    if (track_allocations)
    {
        ++allocation_count;
        if (fail_at && (allocation_count == fail_at || (persistent_failure && allocation_count > fail_at)))
        {
#if defined(__APPLE__) || defined(__linux__)
            if (trace_allocations && allocation_count == fail_at)
            {
                failure_stack_size = backtrace(failure_stack, 16);
            }
#endif
            throw std::bad_alloc();
        }
    }
    auto* allocation = static_cast<Allocation*>(std::malloc(sizeof(Allocation) + (size ? size : 1)));
    if (!allocation)
    {
        throw std::bad_alloc();
    }
    allocation->tracked = track_allocations;
    allocation->stack_size = 0;
    allocation->bytes = size;
    if (allocation->tracked)
    {
        ++outstanding;
        allocation->previous = nullptr;
        allocation->next = live;
        if (live)
        {
            live->previous = allocation;
        }
        live = allocation;
#if defined(__APPLE__) || defined(__linux__)
        if (trace_allocations)
        {
            allocation->stack_size = backtrace(allocation->stack, 12);
        }
#endif
    }
    return allocation + 1;
}
void operator delete(void* pointer) noexcept
{
    if (!pointer)
    {
        return;
    }
    auto* allocation = static_cast<Allocation*>(pointer) - 1;
    if (allocation->tracked)
    {
        --outstanding;
        if (allocation->previous)
        {
            allocation->previous->next = allocation->next;
        }
        else
        {
            live = allocation->next;
        }
        if (allocation->next)
        {
            allocation->next->previous = allocation->previous;
        }
    }
    std::free(allocation);
}
void* operator new[](size_t size)
{
    return ::operator new(size);
}
void operator delete[](void* pointer) noexcept
{
    ::operator delete(pointer);
}

#include "../bridge_runtime.h"
#include "test_document.h"
#include <algorithm>
#include <cassert>
#include <cstring>

using namespace URUI::Bridge;

class FailingRenderElement final : public URUI::Element
{
public:
    static bool fail;
    explicit FailingRenderElement(const URUI::String& tag) : URUI::Element(tag)
    {
    }
    void OnRender() override
    {
        if (fail)
        {
            throw std::bad_alloc();
        }
    }
};
bool FailingRenderElement::fail = true;

uint64_t RenderContentSignature(SurfaceContext& surface)
{
    uint64_t result = 1469598103934665603ull;
    auto bytes = [&](const void* data, size_t size)
    {
        for (size_t i = 0; i < size; ++i)
        {
            result = (result ^ static_cast<const unsigned char*>(data)[i]) * 1099511628211ull;
        }
    };
    const auto& render = surface.renderer;
    for (auto command : render.commands)
    {
        const auto& mesh = render.meshes.at(command.geometry);
        bytes(mesh.vertices.data(), mesh.vertices.size() * sizeof(URUIVertex));
        bytes(mesh.indices.data(), mesh.indices.size() * sizeof(int));
        if (command.texture)
        {
            const auto& texture = render.textures.at(command.texture);
            bytes(&texture.width, sizeof(texture.width));
            bytes(&texture.height, sizeof(texture.height));
            bytes(texture.pixels.data(), texture.pixels.size());
        }
        // Identity allocation order may differ after a retry.
        command.geometry = command.texture = 0;
        bytes(&command.tx, offsetof(URUICommand, clip_h) + sizeof(command.clip_h) - offsetof(URUICommand, tx));
    }
    for (auto command : render.layerCommands)
    {
        command.source = command.target = command.mask = 0;
        bytes(&command, sizeof(command));
    }
    return result;
}

class FailingShowElement final : public URUI::Element, public URUI::EventListener
{
    int updates = 0;
    bool attached = false;
    bool focus_attached = false;

public:
    static bool fail;
    static bool notification;
    static bool focus_notification;
    explicit FailingShowElement(const URUI::String& tag) : URUI::Element(tag)
    {
    }
    ~FailingShowElement() override
    {
        if (auto* document = GetOwnerDocument())
        {
            if (attached)
            {
                document->RemoveEventListener("show", this);
            }
            if (focus_attached)
            {
                document->RemoveEventListener("focus", this, true);
            }
        }
    }
    void OnUpdate() override
    {
        if (fail && ++updates == 2)
        {
            fail = false;
            throw std::bad_alloc();
        }
        if (notification && !attached)
        {
            GetOwnerDocument()->AddEventListener("show", this);
            attached = true;
        }
        if (focus_notification && !focus_attached)
        {
            GetOwnerDocument()->AddEventListener("focus", this, true);
            focus_attached = true;
        }
    }
    void ProcessEvent(URUI::Event& event) override
    {
        if (notification && event.GetType() == "show")
        {
            notification = false;
            throw std::bad_alloc();
        }
        if (focus_notification && event.GetType() == "focus")
        {
            focus_notification = false;
            throw std::bad_alloc();
        }
    }
};
bool FailingShowElement::fail = true;
bool FailingShowElement::notification = false;
bool FailingShowElement::focus_notification = false;

struct FailingUnloadListener final : URUI::EventListener
{
    void ProcessEvent(URUI::Event&) override
    {
        throw std::bad_alloc();
    }
};
struct RedirectFocusListener final : URUI::EventListener
{
    URUI::Element* target;
    bool redirected = false;
    explicit RedirectFocusListener(URUI::Element* target) : target(target)
    {
    }
    void ProcessEvent(URUI::Event&) override
    {
        if (!redirected)
        {
            redirected = true;
            target->Focus();
        }
    }
};
int last_load_result = 0;

void CheckCloseFailure()
{
    URUISurfaceHandle surface = 0;
    assert(urui_surface_create(64, 64, &surface) == URUI_OK);
    auto* ctx = FindSurfaceInternal(surface);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(surface, &owner) == URUI_OK);
    const auto html = "<!doctype html><html><head></head><body></body></html>";
    assert(urui_document_load_html(surface, owner, html, "/old.html") == URUI_OK);
    assert(urui_document_load_html(surface, owner, html, "/replacement.html") == URUI_OK);
    // Retire the old tree and its reserved ownership storage.
    urui_surface_update(surface);
    auto* previous = ctx->documentRegistry->Find(owner)->Element();
    const auto was_tracking = track_allocations;
    track_allocations = true;
    allocation_count = 0;
    fail_at = 1;
    persistent_failure = true;
    const auto result = urui_document_close(surface, owner);
    fail_at = 0;
    persistent_failure = false;
    track_allocations = was_tracking;
    assert(result == URUI_ERR_INTERNAL || result == URUI_OK);
    if (result == URUI_ERR_INTERNAL)
    {
        assert(ctx->documentRegistry->Find(owner) && ctx->documentRegistry->Find(owner)->Element() == previous);
        assert(urui_document_close(surface, owner) == URUI_OK);
    }
    assert(ctx->context->GetNumDocuments() == 0);
    assert(urui_surface_destroy(surface) == URUI_OK);
}

void CheckPublicationFailure()
{
    URUISurfaceHandle surface = 0;
    assert(urui_surface_create(64, 64, &surface) == URUI_OK);
    auto* ctx = FindSurfaceInternal(surface);
    URUI::ElementInstancerGeneric<FailingShowElement> instancer;
    URUI::Factory::RegisterElementInstancer("urui-show-failure", &instancer);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(surface, &owner) == URUI_OK);
    assert(
        urui_document_load_html(
            surface, owner,
            "<!doctype html><html><head></head><body style='margin:0'><div id='old' tabindex='0' style='width:32px;height:32px'></div></body></html>",
            "/old.html") == URUI_OK);
    assert(urui_document_set_presentation(surface, owner, 0, 0, 1, 0) == URUI_OK);
    const auto token = urui_element_ref_acquire(surface, owner, "old");
    assert(urui_element_focus(surface, owner, token, 1) == URUI_OK);
    urui_pointer_event(surface, 0, 0, 0, 5, 5, 0, 0, 0, 0, 0);
    const auto focused = ctx->context->GetFocusElement();
    const auto hovered = ctx->context->GetHoverElement();
    auto* previous = ctx->documentRegistry->Find(owner)->Element();
    const auto resources = ctx->documentRegistry->Find(owner)->resources;
    FailingShowElement::fail = true;
    assert(urui_document_load_html(
               surface, owner,
               "<!doctype html><html><head></head><body><urui-show-failure></urui-show-failure></body></html>",
               "/candidate.html") == URUI_ERR_INTERNAL);
    assert(ctx->documentRegistry->Find(owner)->Element() == previous);
    assert(ctx->documentRegistry->Find(owner)->resources == resources);
    assert(urui_element_ref_valid(surface, owner, token) == 1 && previous->IsPresented());
    assert(ctx->context->GetFocusElement() == focused && ctx->context->GetHoverElement() == hovered);
    assert(ctx->context->GetNumDocuments() == 1);
    FailingUnloadListener unload_listener;
    previous->AddEventListener("unload", &unload_listener);
    FailingShowElement::fail = false;
    FailingShowElement::notification = true;
    assert(urui_document_load_html(
               surface, owner,
               "<!doctype html><html><head></head><body><urui-show-failure></urui-show-failure></body></html>",
               "/committed.html") == URUI_OK);
    assert(ctx->documentRegistry->Find(owner)->Element() != previous);
    assert(ctx->documentRegistry->Find(owner)->Element()->IsPresented());
    assert(urui_element_ref_valid(surface, owner, token) == 0);
    assert(TestLastError(surface).find("notification failed after document commit") != std::string::npos);
    previous->RemoveEventListener("unload", &unload_listener);
    assert(urui_document_set_presentation(surface, owner, 0, 0, 1, 1) == URUI_OK);
    FailingShowElement::focus_notification = true;
    assert(
        urui_document_load_html(
            surface, owner,
            "<!doctype html><html><head></head><body><input id='input' type='text' autofocus><input id='other' type='text'><urui-show-failure></urui-show-failure></body></html>",
            "/modal.html") == URUI_OK);
    urui_surface_update(surface);
    assert(urui_surface_focused_document(surface) == owner && ctx->context->GetFocusElement()->GetId() == "input");
    assert(urui_text_input(surface, "Z") == URUI_OK);
    char value[16];
    int required = 0;
    assert(urui_control_value_read(surface, owner, urui_element_ref_acquire(surface, owner, "input"), value,
                                   sizeof(value), &required) == URUI_OK);
    assert(std::strcmp(value, "Z") == 0);
    auto* committed = ctx->documentRegistry->Find(owner)->Element();
    committed->Hide();
    const auto was_tracking = track_allocations;
    track_allocations = true;
    allocation_count = 0;
    fail_at = 1;
    persistent_failure = true;
    bool focus_failed = false;
    try
    {
        committed->Show(URUI::ModalFlag::Modal, URUI::FocusFlag::Auto, URUI::ScrollFlag::Auto, false);
    }
    catch (const std::bad_alloc&)
    {
        focus_failed = true;
    }
    fail_at = 0;
    persistent_failure = false;
    track_allocations = was_tracking;
    assert(focus_failed && committed->IsPresented());
    urui_surface_update(surface);
    assert(urui_surface_focused_document(surface) == owner && ctx->context->GetFocusElement()->GetId() == "input");
    auto* first = committed->GetElementById("input");
    auto* second = committed->GetElementById("other");
    assert(second->Focus());
    RedirectFocusListener redirect(second);
    first->AddEventListener("focus", &redirect);
    first->Focus();
    assert(ctx->context->GetFocusElement() == second && committed->GetFocusLeafNode() == second);
    std::fprintf(stderr, "redirect focus states: A=%d visible=%d B=%d visible=%d\n", first->IsPseudoClassSet("focus"),
                 first->IsPseudoClassSet("focus-visible"), second->IsPseudoClassSet("focus"),
                 second->IsPseudoClassSet("focus-visible"));
    assert(!first->IsPseudoClassSet("focus") && !first->IsPseudoClassSet("focus-visible") &&
           second->IsPseudoClassSet("focus"));
    committed->Show(URUI::ModalFlag::Keep, URUI::FocusFlag::Keep);
    assert(ctx->context->GetFocusElement() == second && committed->GetFocusLeafNode() == second);
    first->RemoveEventListener("focus", &redirect);
    assert(first->Focus());
    RedirectFocusListener restore(first);
    first->AddEventListener("blur", &restore);
    second->Focus();
    assert(ctx->context->GetFocusElement() == first && committed->GetFocusLeafNode() == first);
    assert(first->IsPseudoClassSet("focus") && !second->IsPseudoClassSet("focus"));
    first->RemoveEventListener("blur", &restore);
    assert(urui_surface_destroy(surface) == URUI_OK);
}

struct PreparedCase
{
    URUISurfaceHandle surface = 0;
    URUIDocumentHandle owner = 0;
    uint64_t body = 0;
    uint64_t same = 0;
};

template <typename Invoke> void CheckOperation(const char* name, Invoke invoke)
{
    trace_allocations =
        std::getenv("URUI_ALLOC_TRACE") && (std::strstr(name, "render") || std::strcmp(name, "load") == 0);
    const char* html =
        "<!doctype html><html><head></head><body id='body' style='margin:0;width:64px;height:64px;background:#123456'><div id='same' data-long='abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz'>abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz</div></body></html>";
    auto prepare = [&]() -> PreparedCase
    {
        PreparedCase c{};
        assert(urui_surface_create(64, 64, &c.surface) == URUI_OK);
        assert(urui_document_create(c.surface, &c.owner) == URUI_OK);
        assert(urui_document_load_html(c.surface, c.owner, html, "ui/failure.html") == URUI_OK);
        assert(urui_document_set_presentation(c.surface, c.owner, 0, 0, 1, 0) == URUI_OK);
        urui_surface_update(c.surface);
        c.body = urui_document_body(c.surface, c.owner);
        c.same = FindTestElement(c.surface, c.owner, "same");
        assert(c.body && c.same);
        auto* ctx = FindSurfaceInternal(c.surface);
        if (std::strcmp(name, "click read") == 0)
        {
            auto* element = ctx->context->GetDocument(0)->GetElementById("same");
            element->SetId("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz");
            ctx->documentRegistry->Find(c.owner)->clicks.push_back(element->GetObserverPtr());
        }
        if (std::strcmp(name, "destroy") == 0)
        {
            assert(urui_element_write(c.surface, c.owner, c.same, 2, "tabindex", "0") == URUI_OK);
            assert(urui_element_focus(c.surface, c.owner, c.same, 1) == URUI_OK);
        }
        if (std::strcmp(name, "masked render") == 0)
        {
            assert(urui_element_write(c.surface, c.owner, c.body, 4, "mask-image",
                                      "linear-gradient(black, transparent)") == URUI_OK);
            assert(urui_element_write(c.surface, c.owner, c.body, 4, "opacity", ".5") == URUI_OK);
            urui_surface_update(c.surface);
        }
        if (std::strcmp(name, "load") == 0)
        {
            assert(urui_element_write(c.surface, c.owner, c.same, 2, "tabindex", "0") == URUI_OK);
            assert(urui_element_write(c.surface, c.owner, c.same, 4, "height", "32px") == URUI_OK);
            urui_surface_update(c.surface);
            assert(urui_element_focus(c.surface, c.owner, c.same, 1) == URUI_OK);
            assert(urui_pointer_event(c.surface, 0, 0, 0, 5, 5, 0, 0, 0, 0, 0) == URUI_OK);
            assert(urui_pointer_capture(c.surface, c.owner, 0, c.same) == URUI_OK);
        }
        return c;
    };
    auto c = prepare();
    allocation_count = 0;
    invoke(c);
    auto* ctx = FindSurfaceInternal(c.surface);
    const auto expected_content = (ctx && std::strstr(name, "render")) ? RenderContentSignature(*ctx) : 0;
    const auto positions = allocation_count;
    if (ctx)
    {
        FlushCandidateEvents(*ctx);
        assert(ctx->pendingDocuments.empty());
        urui_surface_destroy(c.surface);
    }
    const auto retained = outstanding;
    if (trace_allocations && std::strcmp(name, "load") == 0)
    {
        for (auto* allocation = live; allocation; allocation = allocation->next)
        {
            std::fprintf(stderr, "baseline retained: %zu bytes\n", allocation->bytes);
#if defined(__APPLE__) || defined(__linux__)
            backtrace_symbols_fd(allocation->stack, allocation->stack_size, 2);
#endif
        }
    }
    for (size_t position = 1; position <= positions; ++position)
    {
        c = prepare();
        ctx = FindSurfaceInternal(c.surface);
        auto* previous = ctx->documentRegistry->Find(c.owner)->Element();
        auto previous_resources = ctx->documentRegistry->Find(c.owner)->resources;
        const auto focused = ctx->context->GetFocusElement();
        const auto captured = std::strcmp(name, "load") == 0 ? urui_pointer_captured(c.surface, c.owner, 0) : 0;
        allocation_count = 0;
        fail_at = position;
        persistent_failure = true;
        if (std::strcmp(name, "load") == 0)
        {
            std::fprintf(stderr, "load allocation %zu/%zu\n", position, positions);
        }
        try
        {
            invoke(c);
        }
        catch (...)
        {
            std::fprintf(stderr, "%s: exception escaped allocation %zu/%zu\n", name, position, positions);
            assert(false);
        }
        fail_at = 0;
        persistent_failure = false;
        ctx = FindSurfaceInternal(c.surface);
        if (std::strcmp(name, "load") == 0)
        {
            assert(ctx);
            if (last_load_result < 0)
            {
                assert(ctx->documentRegistry->Find(c.owner)->Element() == previous);
                assert(ctx->documentRegistry->Find(c.owner)->resources == previous_resources);
                assert(ctx->context->GetFocusElement() == focused);
                assert(urui_pointer_captured(c.surface, c.owner, 0) == captured);
                assert(urui_element_ref_valid(c.surface, c.owner, captured) == 1);
            }
            else
            {
                assert(last_load_result == URUI_OK && ctx->documentRegistry->Find(c.owner)->Element() != previous);
            }
            assert(ctx->context->GetNumDocuments() == 1);
        }
        if (std::strcmp(name, "render") == 0 || std::strcmp(name, "masked render") == 0)
        {
            std::fprintf(stderr, "retry %s allocation %zu/%zu\n", name, position, positions);
#if defined(__APPLE__) || defined(__linux__)
            if (trace_allocations && failure_stack_size)
            {
                backtrace_symbols_fd(failure_stack, failure_stack_size, 2);
            }
#endif
            invoke(c);
            assert(ctx && RenderContentSignature(*ctx) == expected_content);
        }
        previous_resources.reset();
        if (ctx)
        {
            FlushCandidateEvents(*ctx);
            assert(ctx->pendingDocuments.empty());
            urui_surface_destroy(c.surface);
        }
        if (outstanding != retained)
        {
            std::fprintf(stderr, "%s: retained %zu allocations, expected %zu, position %zu\n", name, outstanding,
                         retained, position);
#if defined(__APPLE__) || defined(__linux__)
            for (auto* allocation = live; allocation; allocation = allocation->next)
            {
                std::fprintf(stderr, "live allocation: %zu bytes\n", allocation->bytes);
                if (allocation->stack_size)
                {
                    backtrace_symbols_fd(allocation->stack, allocation->stack_size, 2);
                }
            }
#endif
        }
        assert(outstanding == retained);
    }
    std::fprintf(stderr, "%s: %zu persistent allocation failures contained\n", name, positions);
}

int main(int argc, char** argv)
{
    if (argc > 1 && std::strcmp(argv[1], "publication") == 0)
    {
        CheckPublicationFailure();
        return 0;
    }
    if (argc > 1 && std::strcmp(argv[1], "close") == 0)
    {
        CheckCloseFailure();
        return 0;
    }
    // Warm process-lifetime constants before checking per-Surface ownership.
    {
        URUISurfaceHandle warm = 0;
        assert(urui_surface_create(64, 64, &warm) == URUI_OK);
        URUIDocumentHandle warm_owner = 0;
        assert(urui_document_create(warm, &warm_owner) == URUI_OK);
        assert(urui_document_close(warm, warm_owner) == URUI_OK);
        assert(urui_surface_destroy(warm) == URUI_OK);
    }
    EngineRuntime::SetThreadError(std::string(256, ' '));
    EngineRuntime::SetThreadError("");
    track_allocations = true;
    allocation_count = 0;
    URUISurfaceHandle probe = 0;
    assert(urui_surface_create(64, 64, &probe) == URUI_OK);
    const size_t positions = allocation_count;
    assert(urui_surface_destroy(probe) == URUI_OK);
    const size_t retained = outstanding;
    const size_t first = argc > 1 ? std::strtoull(argv[1], nullptr, 10) : 1;
    const size_t last = argc > 2 ? std::strtoull(argv[2], nullptr, 10) : positions;
    for (int mode = 0; mode < 2; ++mode)
    {
        for (size_t position = first; position <= last; ++position)
        {
            persistent_failure = mode != 0;
            std::fprintf(stderr, "initialization allocation %zu/%zu, persistent=%d\n", position, positions, mode);
            allocation_count = 0;
            fail_at = position;
            URUISurfaceHandle failedSurface = 0;
            int result = -99;
            try
            {
                result = urui_surface_create(64, 64, &failedSurface);
            }
            catch (...)
            {
                std::fprintf(stderr, "exception escaped urui_surface_create\n");
                assert(false);
            }
            fail_at = 0;
            persistent_failure = false;
            assert(result < 0 && failedSurface == 0);
            assert(ActiveSurfaceCountInternal() == 0);
            assert(!URUI::GetRenderInterface() && !URUI::GetFileInterface());
            if (outstanding != retained)
            {
                std::fprintf(stderr, "outstanding allocations: %zu, expected %zu\n", outstanding, retained);
            }
            assert(outstanding == retained);
            // A failed initialization must not poison the next Surface generation.
            URUISurfaceHandle nextSurface = 0;
            assert(urui_surface_create(64, 64, &nextSurface) == URUI_OK && nextSurface != 0);
            URUIDocumentHandle owner = 0;
            assert(urui_document_create(nextSurface, &owner) == URUI_OK && owner != 0);
            assert(urui_document_close(nextSurface, owner) == URUI_OK);
            assert(urui_surface_destroy(nextSurface) == URUI_OK);
        }
    }
    std::printf("initialization failure recovery: %zu allocation positions, single and persistent failures\n",
                last - first + 1);
    CheckOperation("attribute",
                   [](const PreparedCase& c)
                   {
                       urui_element_write(c.surface, c.owner, c.same, 2, "data-new",
                                          "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz");
                   });
    CheckOperation("style",
                   [](const PreparedCase& c)
                   { urui_element_write(c.surface, c.owner, c.same, 4, "width", "32px"); });
    CheckOperation("attribute read",
                   [](const PreparedCase& c)
                   {
                       char output[256];
                       int required = 0;
                       urui_element_read(c.surface, c.owner, c.same, 3, "data-long", output, sizeof(output), &required);
                   });
    CheckOperation("text read",
                   [](const PreparedCase& c)
                   {
                       char output[256];
                       int required = 0;
                       urui_element_read(c.surface, c.owner, c.same, 2, nullptr, output, sizeof(output), &required);
                   });
    CheckOperation("element reference",
                   [](const PreparedCase& c) { urui_element_ref_acquire(c.surface, c.owner, "same"); });
    CheckOperation("append",
                   [](const PreparedCase& c)
                   {
                       const auto child = urui_element_create(c.surface, c.owner, "div");
                       if (child)
                       {
                           urui_element_insert(c.surface, c.owner, c.body, child, -1);
                       }
                   });
    CheckOperation("click read",
                   [](const PreparedCase& c)
                   {
                       uint64_t target = 0;
                       urui_element_pop_click(c.surface, c.owner, &target);
                   });
    CheckOperation("destroy", [](const PreparedCase& c) { urui_surface_destroy(c.surface); });
    CheckOperation("render",
                   [](const PreparedCase& c)
                   {
                       urui_surface_render(c.surface);
                       if (!fail_at)
                       {
                           assert(urui_command_count(c.surface) > 0);
                       }
                   });
    CheckOperation("masked render",
                   [](const PreparedCase& c)
                   {
                       urui_surface_render(c.surface);
                       if (!fail_at)
                       {
                           assert(urui_command_count(c.surface) > 0 &&
                                  urui_get_layer_commands(c.surface, nullptr, 0) > 0);
                           auto* ctx = FindSurfaceInternal(c.surface);
                           assert(ctx && std::any_of(ctx->renderer.layerCommands.begin(),
                                                     ctx->renderer.layerCommands.end(),
                                                     [](const auto& command) { return command.kind == 3; }));
                       }
                   });
    CheckOperation(
        "load",
        [](const PreparedCase& c)
        {
            last_load_result = urui_document_load_html(
                c.surface, c.owner,
                "<!doctype html><html><head><style>body{margin:0;background:blue}</style></head><body><div id='replacement'>Replacement</div></body></html>",
                "/replacement.html");
        });
    URUISurfaceHandle surface = 0;
    assert(urui_surface_create(64, 64, &surface) == URUI_OK);
    auto* ctx = FindSurfaceInternal(surface);
    URUI::ElementInstancerGeneric<FailingRenderElement> instancer;
    URUI::Factory::RegisterElementInstancer("urui-render-failure", &instancer);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(surface, &owner) == URUI_OK);
    assert(
        urui_document_load_html(
            surface, owner,
            "<!doctype html><html><head></head><body style='background:red'><div style='opacity:.5'><urui-render-failure style='display:block;width:32px;height:32px'></urui-render-failure></div></body></html>",
            "ui/render-failure.html") == URUI_OK);
    assert(urui_document_set_presentation(surface, owner, 0, 0, 1, 0) == URUI_OK);
    urui_surface_update(surface);
    assert(urui_surface_render(surface) == URUI_ERR_INTERNAL);
    assert(TestLastError(surface).find("urui_surface_render failed") != std::string::npos);
    assert(urui_command_count(surface) == 0 && urui_get_layer_commands(surface, nullptr, 0) < 0);
    assert(ctx->context->GetRenderManager().GetTopLayer() == 0);
    FailingRenderElement::fail = false;
    urui_surface_update(surface);
    assert(urui_surface_render(surface) == URUI_OK);
    assert(urui_command_count(surface) > 0 && urui_get_layer_commands(surface, nullptr, 0) >= 0);
    assert(urui_surface_destroy(surface) == URUI_OK);
    CheckPublicationFailure();
    CheckCloseFailure();
}
