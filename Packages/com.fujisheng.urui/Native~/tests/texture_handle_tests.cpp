// Exercise allocator boundaries without allocating billions of textures. Include
// the bridge in this standalone test so no test hooks enter the production ABI.
#include "../bridge.cpp"
#include <cassert>
#include <iostream>

int main()
{
    uint64_t next32 = UINT32_MAX;
    assert(AllocateTextureId<uint32_t>(next32) == UINT32_MAX);
    assert(next32 == uint64_t{UINT32_MAX} + 1);
    assert(AllocateTextureId<uint32_t>(next32) == 0);
    assert(next32 == uint64_t{UINT32_MAX} + 1);

    uint64_t next64 = (uint64_t{1} << 63) - 1;
    assert(AllocateTextureId<uint64_t>(next64) == (uint64_t{1} << 63) - 1);
    assert(AllocateTextureId<uint64_t>(next64) == 0);
    uint64_t zero = 0;
    assert(AllocateTextureId<uint32_t>(zero) == 0);

    const std::array<URUI::byte, 4> pixels{{1, 2, 3, 4}};
    URUI::Span<const URUI::byte> bytes(pixels.data(), pixels.size());
    Renderer local;
    local.nextTexture = borrowedTextureBit;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.textures.empty());
    local.nextTexture = 1;
    assert(local.GenerateTexture(bytes, {1, 1}) == 1);
    local.nextTexture = 1;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.textures.size() == 1 && local.textures.at(1).pixels[0] == 1);
    local.borrowedTextureHandles.emplace(2, borrowedTextureBit + 99);
    local.nextTexture = 2;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.borrowedTextureHandles.at(2) == borrowedTextureBit + 99);

    assert(urui_create(32, 32) == 1);
    URUIDocumentHandle owner = 0;
    URUIDocumentHandle other_owner = 0;
    assert(urui_document_create(&owner) == 1);
    uint64_t owned = 0;
    assert(urui_document_register_rgba_texture(owner, "/good", 1, 1, pixels.data(), 4, &owned) == 1);
    auto resources = documentRegistry->Find(owner)->pendingResources;
    assert(resources && resources->FindImage("/good") == owned);
    render->nextTexture = borrowedTextureBit;
    assert(urui_document_register_external_texture(owner, "/good", 1, 1, borrowedTextureBit + 1) < 0);
    assert(resources->FindImage("/good") == owned && urui_get_texture_kind(owned) == 1);
    // 已有相同像素可共享，分配器耗尽不应破坏现有身份。
    uint64_t shared = 0;
    assert(urui_document_register_rgba_texture(owner, "/good", 1, 1, pixels.data(), 4, &shared) == 1 &&
           shared == owned);
    const uint8_t different_pixels[] = {255, 0, 0, 255};
    uint64_t sentinel = 17;
    assert(urui_document_register_rgba_texture(owner, "/good", 1, 1, different_pixels, 4, &sentinel) < 0);
    assert(sentinel == 17 && resources->FindImage("/good") == owned);
    render->nextTexture = 2;
    assert(urui_document_register_external_texture(owner, "/borrowed", 1, 1, borrowedTextureBit + 2) == 1);
    const auto borrowed_handle = render->textures.at(borrowedTextureBit + 2).renderHandle;
    assert(borrowed_handle == 2 && render->LogicalTextureId(borrowed_handle) == borrowedTextureBit + 2);
    render->ReleaseTexture(borrowed_handle);
    assert(urui_get_texture_kind(borrowedTextureBit + 2) == 2);
    URUI::Vector2i dimensions;
    // Image resolution always requires a document; a Surface has no image namespace.
    int unowned_resolutions = 0;
    static int* resolution_count;
    resolution_count = &unowned_resolutions;
    local.imageResolver = [](uint64_t, const char*, uint64_t*, uint64_t*, int*, int*)
    {
        ++*resolution_count;
        return 0;
    };
    dimensions = {17, 19};
    assert(local.LoadTexture(dimensions, "/unowned") == 0);
    assert(dimensions.x == 0 && dimensions.y == 0);
    assert(unowned_resolutions == 0);
    assert(render->LoadTexture(dimensions, "/borrowed", resources) == borrowed_handle);
    assert(dimensions.x == 1 && dimensions.y == 1);
    render->ReleaseTexture(borrowed_handle);
    render->textures.at(borrowedTextureBit + 2).aliases = std::numeric_limits<size_t>::max();
    assert(urui_document_register_external_texture(owner, "/overflow", 1, 1, borrowedTextureBit + 2) < 0);
    assert(resources->FindImage("/overflow") == 0);
    assert(render->textures.at(borrowedTextureBit + 2).aliases == std::numeric_limits<size_t>::max());
    render->textures.at(borrowedTextureBit + 2).aliases = 1;
    // 防御内部种类冲突；失败不能替换有效的资源绑定。
    render->textures.emplace(borrowedTextureBit + 3, Texture{});
    assert(urui_document_register_external_texture(owner, "/good", 1, 1, borrowedTextureBit + 3) < 0);
    assert(resources->FindImage("/good") == owned);
    render->textures.erase(borrowedTextureBit + 3);
    assert(urui_document_create(&other_owner) == 1);
    assert(urui_document_register_external_texture(other_owner, "/borrowed", 1, 1, borrowedTextureBit + 2) == 1);
    resources.reset();
    assert(urui_document_close(owner) == 1);
    assert(urui_get_texture_kind(owned) == 0 && urui_get_texture_kind(borrowedTextureBit + 2) == 2);
    assert(urui_document_close(other_owner) == 1 && urui_get_texture_kind(borrowedTextureBit + 2) == 0);
    urui_destroy();
    std::cout
        << "texture handles: allocator limits, scoped deduplication, alias overflow, failed mutation preservation and shared-owner release PASS\n";
}
