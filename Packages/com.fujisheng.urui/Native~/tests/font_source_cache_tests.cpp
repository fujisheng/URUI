#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <URUI/Core/ResourceContext.h>
#include "../Engine/Source/Core/DocumentFontContext.h"
#include <cassert>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>

using Data = URUI::SharedPtr<const URUI::Vector<URUI::byte>>;
class MemoryFonts final : public URUI::FileInterface
{
public:
    Data data;
    size_t position = 0;
    size_t read_bytes = 0;
    URUI::FileHandle Open(const URUI::String&) override
    {
        position = 0;
        return 1;
    }
    void Close(URUI::FileHandle) override
    {
    }
    size_t Read(void* output, size_t size, URUI::FileHandle) override
    {
        const auto count = std::min(size, data->size() - position);
        std::memcpy(output, data->data() + position, count);
        position += count;
        read_bytes += count;
        return count;
    }
    bool Seek(URUI::FileHandle, long offset, int origin) override
    {
        const auto next = (origin == SEEK_SET ? 0 : origin == SEEK_CUR ? long(position) : long(data->size())) + offset;
        if (next < 0 || size_t(next) > data->size())
        {
            return false;
        }
        position = size_t(next);
        return true;
    }
    size_t Tell(URUI::FileHandle) override
    {
        return position;
    }
    size_t Length(URUI::FileHandle) override
    {
        return data->size();
    }
    Data GetImmutableData(URUI::FileHandle) override
    {
        return data;
    }
};
class FontResources final : public URUI::ResourceContext
{
public:
    mutable MemoryFonts files;
    FontResources() : ResourceContext(123, 456)
    {
    }
    URUI::FileInterface* GetFileInterface() const override
    {
        return &files;
    }
};
Data Read(const char* name)
{
    const auto path = std::filesystem::path(__FILE__).parent_path() / "fixtures/fonts" / name;
    std::ifstream file(path, std::ios::binary);
    assert(file);
    return std::make_shared<const URUI::Vector<URUI::byte>>(std::istreambuf_iterator<char>(file),
                                                            std::istreambuf_iterator<char>());
}
int main()
{
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    {
        auto resources = URUI::MakeShared<FontResources>();
        resources->files.data = Read("LatoLatin-Regular.ttf");
        URUI::DocumentFontContext first;
        URUI::DocumentFontContext second;
        URUI::DocumentFontContext replacement;
        first.SetResourceContext(resources);
        second.SetResourceContext(resources);
        replacement.SetResourceContext(resources);
        const auto normal = URUI::Style::FontStyle::Normal;
        const auto weight = URUI::Style::FontWeight::Normal;
        assert(first.LoadFace("same.ttf", "example", normal, weight, 0));
        assert(second.LoadFace("same.ttf", "example", normal, weight, 0));
        const auto one = first.ResolveFace("example", normal, weight, 20);
        assert(one && second.ResolveFace("example", normal, weight, 20) == one);
        assert(resources->files.read_bytes == 0 &&
               "Immutable memory fonts must not be copied through FileInterface::Read");
        resources->files.data = Read("RobotoMono-Regular.ttf");
        assert(replacement.LoadFace("same.ttf", "example", normal, weight, 0));
        const auto two = replacement.ResolveFace("example", normal, weight, 20);
        const URUI::String language = "en";
        const URUI::TextShapingContext shaping{language};
        auto* engine = URUI::GetFontEngineInterface();
        assert(engine->GetStringWidth(one, "iiiiWWWW", shaping) != engine->GetStringWidth(two, "iiiiWWWW", shaping));
        assert(first.ResolveFace("example", normal, weight, 20) == one);
        assert(resources->files.read_bytes == 0);
    }
    urui_surface_destroy(g_test_surface);
    std::cout << "Immutable font reuse and changed-content isolation passed\n";
}
