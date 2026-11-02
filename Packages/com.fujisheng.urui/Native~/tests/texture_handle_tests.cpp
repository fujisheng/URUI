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

    const std::array<Rml::byte, 4> pixels{{1, 2, 3, 4}};
    Rml::Span<const Rml::byte> bytes(pixels.data(), pixels.size());
    Renderer local;
    local.next_texture = borrowed_texture_bit;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.textures.empty());
    local.next_texture = 1;
    assert(local.GenerateTexture(bytes, {1, 1}) == 1);
    local.next_texture = 1;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.textures.size() == 1 && local.textures.at(1).pixels[0] == 1);
    local.borrowed_texture_handles.emplace(2, borrowed_texture_bit + 99);
    local.next_texture = 2;
    assert(local.GenerateTexture(bytes, {1, 1}) == 0);
    assert(local.borrowed_texture_handles.at(2) == borrowed_texture_bit + 99);

    assert(urui_create(32, 32, nullptr) == 1);
    uint64_t owned = 0;
    assert(urui_register_rgba_texture("good", 1, 1, pixels.data(), 4, &owned) == 1);
    render->next_texture = borrowed_texture_bit;
    assert(urui_register_external_texture("good", 1, 1, borrowed_texture_bit + 1) < 0);
    assert(render->named_textures.at("good") == owned && urui_get_texture_kind(owned) == 1);
    uint64_t sentinel = 17;
    assert(urui_register_rgba_texture("good", 1, 1, pixels.data(), 4, &sentinel) < 0);
    assert(sentinel == 17 && render->named_textures.at("good") == owned);
    render->next_texture = 2;
    assert(urui_register_external_texture("borrowed", 1, 1, borrowed_texture_bit + 2) == 1);
    auto borrowed_handle = render->textures.at(borrowed_texture_bit + 2).render_handle;
    assert(borrowed_handle == 2 && render->LogicalTextureId(borrowed_handle) == borrowed_texture_bit + 2);
    render->ReleaseTexture(borrowed_handle);
    assert(urui_get_texture_kind(borrowed_texture_bit + 2) == 2);
    Rml::Vector2i dimensions;
    assert(render->LoadTexture(dimensions, "borrowed") == borrowed_handle);
    assert(dimensions.x == 1 && dimensions.y == 1);
    render->textures.at(borrowed_texture_bit + 2).aliases = std::numeric_limits<size_t>::max();
    assert(urui_register_external_texture("overflow", 1, 1, borrowed_texture_bit + 2) < 0);
    assert(!render->named_textures.count("overflow"));
    assert(render->textures.at(borrowed_texture_bit + 2).aliases == std::numeric_limits<size_t>::max());
    // A kind conflict cannot arise through the public high-/low-bit domains;
    // nevertheless reject an inconsistent record before touching a good alias.
    render->textures.emplace(borrowed_texture_bit + 3, Texture{});
    assert(urui_register_external_texture("good", 1, 1, borrowed_texture_bit + 3) < 0);
    assert(render->named_textures.at("good") == owned);
    urui_destroy();
    std::cout << "texture handle boundaries: uint32 max, uint64 low-domain max, zero, collision, alias overflow, and validation preservation PASS\n";
}
