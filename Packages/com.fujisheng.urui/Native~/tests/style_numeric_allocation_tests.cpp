// Link bridge sources directly into this executable so the independent
// operator-new probe covers style_properties.cpp and render_program_parameters.cpp.
#include "bridge_runtime.h"
#include "Engine/Source/Core/ElementStyle.h"
#include "test_document.h"
#include <URUI/Core/PropertyParser.h>
#include <URUI/Core/StyleSheetSpecification.h>
#include <algorithm>
#include <array>
#include <cassert>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <thread>
#include <vector>

static thread_local bool watch_heap = false;
static thread_local uint64_t allocations = 0;
static thread_local uint64_t frees = 0;
void* operator new(std::size_t bytes)
{
    void* value = std::malloc(bytes ? bytes : 1);
    if (!value)
    {
        throw std::bad_alloc();
    }
    if (watch_heap)
    {
        allocations++;
    }
    return value;
}
void operator delete(void* value) noexcept
{
    if (value && watch_heap)
    {
        frees++;
    }
    std::free(value);
}
void operator delete(void* value, std::size_t) noexcept
{
    ::operator delete(value);
}
void* operator new[](std::size_t bytes)
{
    return ::operator new(bytes);
}
void operator delete[](void* value) noexcept
{
    ::operator delete(value);
}
void operator delete[](void* value, std::size_t) noexcept
{
    ::operator delete(value);
}

int main()
{
    TestSurface surface(128, 128);
    const char* property = "--generic-animated-number-longer-than-small-string";
    const char* html =
        "<!doctype html><html><head><style>"
        "@property --generic-animated-number-longer-than-small-string{syntax:'<number>';inherits:false;initial-value:0;}"
        "@property --program-color{syntax:'<color>';inherits:false;initial-value:#804020;}"
        "@property --derived-color{syntax:'<color>';inherits:false;initial-value:#804020;}"
        "@property --program-mode{syntax:'low | high';inherits:false;initial-value:low;}"
        "#value{width:16px;height:8px;--program-mode:high;}"
        "</style></head><body><div id='value'></div></body></html>";
    const URUIDocumentHandle owner = LoadTestDocument(surface, html, "/numeric-allocations.html");
    urui_surface_update(surface);
    const uint64_t docRoot = urui_document_root(surface, owner);
    const uint64_t valueToken = FindTestElement(surface, owner, "value");
    assert(docRoot && valueToken);
    auto* context = URUI::GetContext(0);
    auto* document = context->GetDocument(0);
    auto* element = document->GetElementById("value");
    assert(element);

    std::vector<URUIStyleValue> values(256);
    std::vector<char> names(16384);
    uint64_t revision = 0;
    int count = 0;
    int text_bytes = 0;
    auto readSnapshot = [&](uint64_t expected = 0)
    {
        int status = urui_element_style_snapshot(surface, owner, valueToken, expected, values.data(),
                                                 static_cast<int>(values.size()), names.data(),
                                                 static_cast<int>(names.size()), &revision, &count, &text_bytes);
        if (status == URUI_ERR_BUFFER_TOO_SMALL)
        {
            values.resize(static_cast<size_t>(count) + 32);
            names.resize(static_cast<size_t>(text_bytes) + 1024);
            status = urui_element_style_snapshot(surface, owner, valueToken, expected, values.data(),
                                                 static_cast<int>(values.size()), names.data(),
                                                 static_cast<int>(names.size()), &revision, &count, &text_bytes);
        }
        return status;
    };
    auto findValue = [&](const char* propName) -> const URUIStyleValue*
    {
        for (int i = 0; i < count; ++i)
        {
            if (std::strcmp(names.data() + values[i].name_offset, propName) == 0)
            {
                return &values[i];
            }
        }
        return nullptr;
    };

    assert(readSnapshot() == URUI_OK);
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty(property, i % 2 ? ".25" : ".75"));
        context->Update();
        const auto prevRevision = revision;
        watch_heap = i >= 300;
        const int st = urui_element_style_snapshot(surface, owner, valueToken, 0, values.data(),
                                                   static_cast<int>(values.size()), names.data(),
                                                   static_cast<int>(names.size()), &revision, &count, &text_bytes);
        watch_heap = false;
        assert(st == URUI_OK && revision != prevRevision);
        const auto* found = findValue(property);
        assert(found && found->x == (i % 2 ? .25f : .75f));
    }
    std::cout << "numeric snapshot allocation/free=" << allocations << "/" << frees << std::endl;
    assert(allocations == 0 && frees == 0);

    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty("--program-color", i % 2 ? "#804020" : "#204080"));
        context->Update();
        watch_heap = i >= 300;
        const int st = urui_element_style_snapshot(surface, owner, valueToken, 0, values.data(),
                                                   static_cast<int>(values.size()), names.data(),
                                                   static_cast<int>(names.size()), &revision, &count, &text_bytes);
        watch_heap = false;
        assert(st == URUI_OK);
        const auto* found = findValue("--program-color");
        assert(found && found->x == (i % 2 ? 128.f : 32.f) / 255.f);
    }
    std::cout << "color snapshot allocation/free=" << allocations << "/" << frees << std::endl;
    assert(allocations == 0 && frees == 0);

    // Prepared effects retain the computed version even when a live style read occurs after a DOM change.
    URUIFrozenStyleHandle frozen{};
    assert(urui_style_snapshot_freeze(surface, owner, valueToken, nullptr, &frozen) == URUI_OK);
    auto old = frozen;
    assert(element->SetProperty(property, ".5"));
    context->Update();
    assert(readSnapshot() == URUI_OK);
    assert(urui_style_snapshot_copy(surface, owner, &frozen, 0, nullptr, 0, nullptr, 0, &revision, &count,
                                    &text_bytes) == URUI_ERR_BUFFER_TOO_SMALL);
    auto frozen_number = [&]()
    {
        assert(urui_style_snapshot_copy(surface, owner, &frozen, 0, values.data(), static_cast<int>(values.size()),
                                        names.data(), static_cast<int>(names.size()), &revision, &count,
                                        &text_bytes) == URUI_OK);
        const auto* f = findValue(property);
        assert(f);
        return f->x;
    };
    assert(frozen_number() == .25f);
    assert(urui_style_snapshot_freeze(surface, owner, valueToken, &frozen, &frozen) == URUI_OK &&
           frozen_number() == .5f);
    assert(urui_style_snapshot_copy(surface, owner, &old, 0, nullptr, 0, nullptr, 0, &revision, &count, &text_bytes) ==
           URUI_ERR_NOT_READY);
    assert(urui_style_snapshot_release(surface, owner + 99, &frozen) == URUI_ERR_NOT_READY);
    int wrong_thread = 0;
    std::thread other([&] { wrong_thread = urui_style_snapshot_release(surface, owner, &frozen); });
    other.join();
    assert(wrong_thread == URUI_ERR_WRONG_THREAD && frozen_number() == .5f);

    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty(property, i % 2 ? ".25" : ".75"));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_element_style_snapshot(surface, owner, valueToken, 0, values.data(),
                                           static_cast<int>(values.size()), names.data(),
                                           static_cast<int>(names.size()), &revision, &count, &text_bytes) == URUI_OK);
        assert(urui_style_snapshot_freeze(surface, owner, valueToken, &frozen, &frozen) == URUI_OK);
        assert(frozen_number() == (i % 2 ? .25f : .75f));
        watch_heap = false;
    }
    assert(allocations == 0 && frees == 0);

    auto frozen_color = [&]()
    {
        assert(urui_style_snapshot_copy(surface, owner, &frozen, 0, values.data(), static_cast<int>(values.size()),
                                        names.data(), static_cast<int>(names.size()), &revision, &count,
                                        &text_bytes) == URUI_OK);
        const auto* f = findValue("--program-color");
        assert(f);
        return f->x;
    };
    const auto before_color = frozen_color();
    assert(element->SetProperty("--program-color", "#204080"));
    context->Update();
    assert(readSnapshot() == URUI_OK);
    assert(frozen_color() == before_color);
    assert(urui_style_snapshot_freeze(surface, owner, valueToken, &frozen, &frozen) == URUI_OK &&
           frozen_color() == 32.f / 255.f);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty("--program-color", i % 2 ? "#804020" : "#204080"));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_element_style_snapshot(surface, owner, valueToken, 0, values.data(),
                                           static_cast<int>(values.size()), names.data(),
                                           static_cast<int>(names.size()), &revision, &count, &text_bytes) == URUI_OK);
        assert(urui_style_snapshot_freeze(surface, owner, valueToken, &frozen, &frozen) == URUI_OK);
        assert(frozen_color() == (i % 2 ? 128.f : 32.f) / 255.f);
        watch_heap = false;
    }
    assert(allocations == 0 && frees == 0);
    assert(urui_style_snapshot_release(surface, owner, &frozen) == URUI_OK &&
           urui_style_snapshot_release(surface, owner, &frozen) == URUI_ERR_NOT_READY);

    assert(element->SetProperty("--derived-color", "var(--program-color)"));
    context->Update();
    assert(element->SetProperty("--program-color", "#102030"));
    context->Update();
    assert(readSnapshot() == URUI_OK);
    const auto* dependent = findValue("--derived-color");
    assert(dependent && dependent->x == 16.f / 255.f && dependent->y == 32.f / 255.f && dependent->z == 48.f / 255.f);
    assert(element->SetProperty("--program-color", "#804020"));
    context->Update();
    assert(readSnapshot() == URUI_OK);

    // Unregistered raw custom properties.
    assert(element->SetProperty("--unregistered-program-keyword-with-a-long-name",
                                "author-keyword-longer-than-the-native-small-string-storage"));
    assert(element->SetProperty("--raw-length", "4px"));
    assert(element->SetProperty("--raw-resource-key", "a"));
    context->Update();
    assert(readSnapshot() == URUI_OK);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty(property, i % 2 ? ".25" : ".75"));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_element_style_snapshot(surface, owner, valueToken, 0, values.data(),
                                           static_cast<int>(values.size()), names.data(),
                                           static_cast<int>(names.size()), &revision, &count, &text_bytes) == URUI_OK);
        watch_heap = false;
    }
    std::cout << "registered delta with constant raw fields allocation/free=" << allocations << "/" << frees
              << std::endl;
    assert(allocations == 0 && frees == 0);

    // Program layout registration & zero-allocation reads.
    const URUIProgramField optional_field{"--optional-feather-length", 0, 258, -1, 0, nullptr};
    uint64_t optional_program = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "optional-zero-length", &optional_field, 1, 1,
                                        &optional_program) == URUI_OK);
    float optional_values[4]{9, 9, 9, 9};
    assert(urui_program_layout_values(surface, owner, valueToken, optional_program, optional_values, 1) == URUI_OK &&
           optional_values[0] == 0);
    assert(element->SetProperty("--optional-feather-length", "red"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, optional_program, optional_values, 1) ==
               URUI_ERR_INVALID_ARG &&
           optional_values[0] == 0);
    assert(element->SetProperty("--optional-feather-length", "12px"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, optional_program, optional_values, 1) == URUI_OK &&
           optional_values[0] == 12);
    element->RemoveProperty("--optional-feather-length");
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, optional_program, optional_values, 1) == URUI_OK &&
           optional_values[0] == 0);
    element->GetStyle()->SetCustomProperty("--optional-feather-length", URUI::Property(13.f, URUI::Unit::PX));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, optional_program, optional_values, 1) == URUI_OK &&
           optional_values[0] == 13);
    assert(urui_program_layout_release(surface, owner, optional_program) == URUI_OK);

    element->GetStyle()->SetCustomProperty("--typed-program-number", URUI::Property(.25f, URUI::Unit::NUMBER));
    context->Update();
    const URUIProgramField typed_field{"--typed-program-number", 0, 1, -1, 0, nullptr};
    uint64_t typed_program = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "typed-raw-number", &typed_field, 1, 1,
                                        &typed_program) == URUI_OK);
    assert(urui_program_layout_values(surface, owner, valueToken, typed_program, optional_values, 1) == URUI_OK &&
           optional_values[0] == .25f);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        element->GetStyle()->SetCustomProperty("--typed-program-number",
                                               URUI::Property(i % 2 ? .25f : .75f, URUI::Unit::NUMBER));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_program_layout_values(surface, owner, valueToken, typed_program, optional_values, 1) == URUI_OK);
        watch_heap = false;
    }
    assert(allocations == 0 && frees == 0);
    assert(urui_program_layout_release(surface, owner, typed_program) == URUI_OK);

    const URUIProgramField identities[] = {{"--optional-image-resource", 0, 16 | 256, -1, 0, nullptr},
                                           {"--optional-key-resource", 1, 8 | 256, -1, 0, nullptr}};
    uint64_t identities_program = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "optional-resource-identities", identities, 2, 1,
                                        &identities_program) == URUI_OK);
    float identity_values[4]{};
    assert(urui_program_layout_values(surface, owner, valueToken, identities_program, identity_values, 1) == URUI_OK &&
           identity_values[0] == 0 && identity_values[1] == 0);
    URUI::Property image_property{URUI::String("url('relative.png')"), URUI::Unit::STRING};
    image_property.source = URUI::MakeShared<URUI::PropertySource>("/first/source.css", 1, "#value");
    element->GetStyle()->SetCustomProperty("--optional-image-resource", image_property);
    assert(element->SetProperty("--optional-key-resource", "present"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, identities_program, identity_values, 1) == URUI_OK &&
           identity_values[0] != 0 && identity_values[1] != 0);
    const auto first_source = identity_values[0];
    image_property.source = URUI::MakeShared<URUI::PropertySource>("/second/source.css", 1, "#value");
    element->GetStyle()->SetCustomProperty("--optional-image-resource", image_property);
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, identities_program, identity_values, 1) == URUI_OK &&
           identity_values[0] != first_source);
    element->RemoveProperty("--optional-image-resource");
    element->RemoveProperty("--optional-key-resource");
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, identities_program, identity_values, 1) == URUI_OK &&
           identity_values[0] == 0 && identity_values[1] == 0);
    assert(urui_program_layout_release(surface, owner, identities_program) == URUI_OK);

    const URUIProgramKeyword automatic[] = {{"auto", 7, 0}};
    const URUIProgramField mixed_fields[] = {{"--numeric-or-auto", 0, 32, -1, 1, automatic},
                                             {"--image-resource", 2, 16, 3, 0, nullptr}};
    uint64_t mixed_program = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "numeric-and-image-fields", mixed_fields, 2, 1,
                                        &mixed_program) == URUI_OK);
    assert(element->SetProperty("--numeric-or-auto", "auto"));
    assert(element->SetProperty("--image-resource", "url('first.png')"));
    context->Update();
    float mixed_values[4]{};
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[0] == 7 && mixed_values[1] == 1);
    const float first_image = mixed_values[2];
    assert(mixed_values[3] >= 2);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        element->GetStyle()->SetCustomProperty("--numeric-or-auto",
                                               URUI::Property(i % 2 ? .25f : .75f, URUI::Unit::NUMBER));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK);
        watch_heap = false;
        assert(mixed_values[0] == (i % 2 ? .25f : .75f) && mixed_values[1] == 0 && mixed_values[2] == first_image);
    }
    assert(allocations == 0 && frees == 0);
    assert(element->SetProperty("--image-resource", "url('second.png')"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] != first_image);
    assert(element->SetProperty("--image-resource", "url('first.png')"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] == first_image);
    assert(element->SetProperty("--image-resource", "none"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] != first_image);
    assert(element->SetProperty("--image-resource", "url('first.png')"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] == first_image);
    URUI::Property typed_image;
    assert(URUI::StyleSheetSpecification::GetParser("css_image")
               ->ParseValue(typed_image, "url('first.png')", {{"effect", 1}}));
    element->GetStyle()->SetCustomProperty("--image-resource", typed_image);
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] == first_image);
    element->GetStyle()->SetCustomProperty("--image-resource",
                                           URUI::Property(URUI::DecoratorsPtr{}, URUI::Unit::DECORATOR));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] == 0);
    element->GetStyle()->SetCustomProperty("--image-resource", typed_image);
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) == URUI_OK &&
           mixed_values[2] == first_image);
    assert(element->SetProperty("--numeric-or-auto", "bad-keyword"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, mixed_program, mixed_values, 1) ==
           URUI_ERR_INVALID_ARG);
    assert(urui_program_layout_release(surface, owner, mixed_program) == URUI_OK);

    const URUIProgramKeyword modes[] = {{"low", 3, 0}, {"high", 7, 0}};
    const URUIProgramKeyword raw_modes[] = {{"author-keyword-longer-than-the-native-small-string-storage", 11, 0}};
    const URUIProgramField fields[] = {{property, 0, 1, 3, 0, nullptr},
                                       {"width", 1, 2, -1, 0, nullptr},
                                       {"--program-color", 4, 4, -1, 0, nullptr},
                                       {"--program-mode", 2, 8, 8, 2, modes},
                                       {"--unregistered-program-keyword-with-a-long-name", 9, 8, -1, 1, raw_modes},
                                       {"--raw-length", 10, 2, -1, 0, nullptr},
                                       {"--raw-resource-key", 12, 8, -1, 0, nullptr},
                                       {"--program-mode", 13, 8, -1, 0, nullptr}};
    uint64_t program = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "generic-fields", fields, 8, 4, &program) == URUI_OK &&
           program);
    std::array<float, 16> rows{};
    assert(urui_program_layout_values(surface, owner, valueToken, program, rows.data(), 4) == URUI_OK);
    assert(rows[0] == .25f && rows[1] == 16 && rows[2] == 7 && rows[3] == 2 && rows[8] == 2);
    assert(std::abs(rows[4] - 128.f / 255) < .00001f && rows[7] == 1 && rows[9] == 11 && rows[10] == 4 &&
           rows[11] == 0);
    const auto registered_key = rows[13];
    const auto raw_key = rows[12];
    assert(raw_key != 0);
    assert(element->SetProperty("--raw-resource-key", "b"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, program, rows.data(), 4) == URUI_OK &&
           rows[12] != raw_key && rows[13] == registered_key);
    assert(element->SetProperty("--raw-resource-key", "a"));
    context->Update();
    assert(urui_program_layout_values(surface, owner, valueToken, program, rows.data(), 4) == URUI_OK &&
           rows[12] == raw_key);
    uint64_t duplicate = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "generic-fields", fields, 8, 4, &duplicate) ==
               URUI_OK &&
           duplicate == program);
    URUIProgramField invalid[2] = {fields[0], fields[1]};
    invalid[1].offset = 0;
    assert(urui_program_layout_register(surface, owner, docRoot, "bad-overlap", invalid, 2, 3, &duplicate) ==
               URUI_ERR_INVALID_ARG &&
           duplicate == 0);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        assert(element->SetProperty(property, i % 2 ? ".25" : ".75"));
        context->Update();
        watch_heap = i >= 300;
        assert(urui_program_layout_values(surface, owner, valueToken, program, rows.data(), 4) == URUI_OK);
        watch_heap = false;
        assert(rows[0] == (i % 2 ? .25f : .75f));
    }
    assert(allocations == 0 && frees == 0);
    assert(urui_program_layout_release(surface, owner + 99, program) == URUI_ERR_NOT_READY &&
           urui_program_layout_release(surface, owner, program) == URUI_OK);
    assert(urui_program_layout_values(surface, owner, valueToken, program, rows.data(), 4) == URUI_ERR_NOT_READY);
    assert(urui_document_close(surface, owner) == URUI_OK);

    // Opacity clamped snapshot zero-allocation test.
    const URUIDocumentHandle opOwner = LoadTestDocument(
        surface, "<!doctype html><html><head></head><body><div id='opacity'></div></body></html>",
        "/opacity-snapshot.html");
    urui_surface_update(surface);
    const uint64_t opToken = FindTestElement(surface, opOwner, "opacity");
    auto* opElement = context->GetDocument(0)->GetElementById("opacity");
    assert(opToken && opElement);
    assert(urui_element_style_snapshot(surface, opOwner, opToken, 0, values.data(), static_cast<int>(values.size()),
                                       names.data(), static_cast<int>(names.size()), &revision, &count,
                                       &text_bytes) == URUI_OK);
    allocations = frees = 0;
    for (int i = 0; i < 332; i++)
    {
        const char* input[] = {"1.5", "50%", "-.5", ".25"};
        const float expected[] = {1.f, .5f, 0.f, .25f};
        assert(opElement->SetProperty("opacity", input[i % 4]));
        context->Update();
        watch_heap = i >= 300;
        const int st = urui_element_style_snapshot(surface, opOwner, opToken, 0, values.data(),
                                                   static_cast<int>(values.size()), names.data(),
                                                   static_cast<int>(names.size()), &revision, &count, &text_bytes);
        watch_heap = false;
        assert(st == URUI_OK);
        const auto* f = findValue("opacity");
        assert(f && f->x == expected[i % 4]);
    }
    std::cout << "opacity snapshot allocations=" << allocations << " frees=" << frees << std::endl;
    assert(allocations == 0 && frees == 0);
}
