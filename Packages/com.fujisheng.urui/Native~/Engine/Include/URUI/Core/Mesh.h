#pragma once

#include "Header.h"
#include "Texture.h"
#include "Vertex.h"

namespace URUI
{

    struct URUI_CORE_API Mesh
    {
        Vector<Vertex> vertices;
        Vector<int> indices;
        TextureSampling texture_sampling = TextureSampling::Clamp;

        explicit operator bool() const
        {
            return !indices.empty();
        }
        friend bool operator==(const Mesh& lhs, const Mesh& rhs)
        {
            return lhs.vertices == rhs.vertices && lhs.indices == rhs.indices &&
                   lhs.texture_sampling == rhs.texture_sampling;
        }
        friend bool operator!=(const Mesh& lhs, const Mesh& rhs)
        {
            return !(lhs == rhs);
        }
    };

    struct URUI_CORE_API TexturedMesh
    {
        Mesh mesh;
        Texture texture;
    };

    using TexturedMeshList = Vector<TexturedMesh>;

}
