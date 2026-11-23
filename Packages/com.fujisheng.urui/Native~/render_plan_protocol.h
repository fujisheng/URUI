#pragma once
#include <stdint.h>
#include "urui.h"

// RenderPlan Protocol v9 (C ABI v13).
// Legacy sRGB/root-copy/raster-pass paths have been retired.
enum
{
    URUI_RENDER_PLAN_PROTOCOL = 9
};

enum URUIPlanResourceFlag
{
    URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT = 1,
    URUI_PLAN_RESOURCE_HDR_CAMERA = 4,
    URUI_PLAN_RESOURCE_SCREEN_OUTPUT = 16,
    URUI_PLAN_RESOURCE_SHADER_IMAGE_PAINT = 32,
    URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH = 64,
    URUI_PLAN_RESOURCE_SCALAR_MASK_SUPPORTED = 8192
};

enum URUIPlanLayerReasonFlag
{
    URUI_PLAN_LAYER_SKIP_CACHE_WRITE = 1,
    URUI_PLAN_LAYER_CLIP = 2,
    URUI_PLAN_LAYER_DAMAGE = 4
};

enum URUIPlanLayerRegionFlag
{
    URUI_PLAN_LAYER_REGION_MASK_SOURCE = 1,
    URUI_PLAN_LAYER_REGION_HALO = 2,
    URUI_PLAN_LAYER_REGION_CAPTURE = 4,
    URUI_PLAN_LAYER_REGION_ALIAS = 8,
    URUI_PLAN_LAYER_REGION_CACHEABLE = 16,
    URUI_PLAN_LAYER_SCALAR_MASK = 64
};

enum URUIPlanImageCapabilityFlag
{
    URUI_PLAN_ELEMENT_UNITY_BLEND = 1,
    URUI_PLAN_IMAGE_LOCAL = 2
};

enum URUIPlanOperationReasonFlag
{
    URUI_PLAN_REASON_SHARED_MATRIX = 0x08000000u,
    URUI_PLAN_REASON_NO_MASK_OR_COLOR_FILTER = 0x10000000u,
    URUI_PLAN_REASON_COMPLETE_GEOMETRY = 0x40000000u,
    URUI_PLAN_REASON_UNBOUNDED_BOUNDS = 0x80000000u
};

enum URUIPlanOperationKind
{
    URUI_PLAN_GEOMETRY = 1,
    URUI_PLAN_EXTERNAL = 2,
    URUI_PLAN_IMAGE = 3,
    URUI_PLAN_LAYER = 5,
    URUI_PLAN_EXTERNAL_GEOMETRY = 6,
    URUI_PLAN_EXTERNAL_RAW_GEOMETRY = 7,
    URUI_PLAN_PARAMETER_IMAGE = 8
};

enum URUIPlanPrepareKind
{
    URUI_PLAN_READY = 0,
    URUI_PLAN_PREPARE_IMAGE = 1,
    URUI_PLAN_ELEMENT_LAYOUT = 2,
    URUI_PLAN_ACQUIRE_CAPTURE = 3,
    URUI_PLAN_PREPARE_ELEMENT = 4
};

enum URUIPlanTargetKind
{
    URUI_PLAN_TARGET_LAYER = 1,
    URUI_PLAN_TARGET_MASK_SCRATCH = 3,
    URUI_PLAN_TARGET_ROOT_CAPTURE = 6
};

enum URUIPlanExternalBindingFlag
{
    URUI_PLAN_EXTERNAL_WRITES_PARENT = 1,
    URUI_PLAN_EXTERNAL_ORIGINAL_GEOMETRY = 2,
    URUI_PLAN_EXTERNAL_WRITES_OFFSCREEN = 4,
    URUI_PLAN_EXTERNAL_KNOWN_PROVIDER = 8
};

enum URUIPlanExternalDrawFlag
{
    URUI_PLAN_DRAW_RAW_CONTRACT = 1,
    URUI_PLAN_DRAW_DYNAMIC_RESOURCES = 2,
    URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT = 4
};

enum URUIPlanAttributeFormat
{
    URUI_PLAN_FLOAT32 = 1,
    URUI_PLAN_UNORM8 = 2,
    URUI_PLAN_FLOAT16 = 3
};

typedef struct
{
    float x;
    float y;
    float z;
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t a;
    float u;
    float v;
    float texture;
    // Batch-local parameter offset; shaders read floor(parameter + 0.5) + _UruiBaseParameter.
    float parameter;
} URUIPlanVertex;

typedef struct
{
    // 普通几何 metadata[0] bit9：第三列 XY 为无损布局原点，先解码局部顶点再乘矩阵。
    float matrix[16];
    float clip[4];
    float tint[4];
    float uv[4];
    float metadata[4];
} URUIPlanParameter;

typedef struct
{
    float tint[4];
    float uv[4];
} URUIPlanUniform;

typedef struct
{
    uint64_t geometry;
    uint64_t textures[8];
    uint32_t kind;
    uint32_t command_index;
    uint32_t count;
    uint32_t texture_count;
    uint32_t layer_index;
    uint32_t reason;
    float bounds[4];
    uint64_t mask_texture;
    uint64_t program;
    uint32_t base_parameter;
    uint32_t reserved;
} URUIPlanOperation;

typedef struct
{
    const void* data;
    uint64_t bytes;
    uint32_t stride;
    uint32_t reserved;
} URUIPlanVertexStream;

typedef struct
{
    uint32_t stream;
    uint32_t offset;
    uint32_t format;
    uint32_t dimension;
} URUIPlanAttribute;

typedef struct
{
    uint64_t geometry;
    uint64_t version;
    URUIPlanVertexStream streams[4];
    const void* indices;
    uint64_t index_bytes;
    uint32_t vertex_count;
    uint32_t index_count;
    uint32_t index_start;
    int32_t base_vertex;
    uint32_t stream_count;
    uint32_t index_stride;
    URUIPlanAttribute position;
    URUIPlanAttribute color;
    URUIPlanAttribute uv;
} URUIPlanExternalGeometry;

typedef struct
{
    uint32_t command;
    uint32_t geometry_index;
    uint64_t provider;
    uint64_t program;
    URUIPlanParameter parameter;
    uint32_t flags;
    uint32_t reserved;
} URUIPlanExternalDraw;

typedef struct
{
    uint64_t request;
    uint64_t document;
    uint64_t element;
    uint64_t program;
    uint64_t geometry;
    uint64_t layer;
    uint64_t source_version;
    uint64_t resource;
    uint32_t kind;
    uint32_t slot;
    uint32_t pixel_width;
    uint32_t pixel_height;
    float capture[4];
    float uv[4];
    float source_ink[4];
} URUIPlanPrepareRequest;

typedef struct
{
    uint64_t lease;
    uint64_t epoch;
    const URUICommand* commands;
    const float* matrices;
    const URUIPaintCommand* paints;
    const URUILayerCommand* layers;
    uint32_t command_count;
    uint32_t paint_count;
    uint32_t layer_count;
    uint32_t protocol;
    const URUIPlanUniform* uniforms;
    const uint64_t* command_layers;
} URUIPlanInputPacket;

// Pointer placed at end so all field offsets and total size (104B) match on 32-bit and 64-bit.
typedef struct
{
    uint64_t request;
    uint64_t resource;
    uint64_t source_version;
    uint32_t kind;
    uint32_t pixel_width;
    uint32_t pixel_height;
    uint32_t transform_count;
    uint32_t capabilities;
    uint32_t reserved;
    float ink[4];
    float expansion[4];
    float uv[4];
    const float* transforms;
} URUIPlanFeedback;

typedef struct
{
    uint64_t layer;
    float bounds[4];
    uint32_t flags;
    uint32_t reserved;
    uint64_t version;
    uint64_t alias_layer;
} URUIPlanLayerRegion;

typedef struct
{
    uint64_t lease;
    const URUIPlanPrepareRequest* requests;
    uint32_t count;
    uint32_t ready;
    const URUIPlanLayerRegion* regions;
    uint32_t region_count;
    uint32_t target_flags;
    const uint32_t* layer_indices;
    uint32_t layer_index_count;
    uint32_t layer_reserved;
} URUIPlanPreparePacket;

typedef struct
{
    uint64_t texture;
    uint64_t version;
    uint32_t domain;
    uint32_t flags;
} URUIPlanTextureBinding;

typedef struct
{
    uint32_t command;
    uint32_t flags;
} URUIPlanExternalBinding;

typedef struct
{
    uint64_t layer;
    uint64_t version;
    uint32_t flags;
    uint32_t reserved;
} URUIPlanLayerBinding;

typedef struct
{
    const URUIPlanTextureBinding* textures;
    uint32_t texture_count;
    uint32_t frame_flags;
    const URUIPlanExternalBinding* externals;
    uint32_t external_count;
    uint32_t external_reserved;
    const URUIPlanLayerBinding* layer_bindings;
    uint32_t layer_binding_count;
    uint32_t layer_reserved;
    const URUIPlanExternalGeometry* external_geometry;
    uint32_t external_geometry_count;
    uint32_t geometry_reserved;
    const URUIPlanExternalDraw* external_draws;
    uint32_t external_draw_count;
    uint32_t draw_reserved;
} URUIPlanBindings;

typedef struct
{
    uint64_t layer;
    uint64_t related_layer;
    int32_t slot;
    uint32_t kind;
    uint32_t first_operation;
    uint32_t last_operation;
} URUIPlanTargetRequest;

typedef struct
{
    uint64_t lease;
    uint64_t epoch;
    uint64_t version;
    uint64_t parameter_version;
    const URUIPlanOperation* operations;
    const URUIPlanParameter* parameters;
    uint32_t operation_count;
    uint32_t parameter_count;
    uint32_t protocol;
    uint32_t extra_parameter_count;
    uint64_t geometry_bytes;
    uint64_t frame_bytes;
    uint64_t allocation_count;
    uint64_t free_count;
    uint64_t allocated_bytes;
    const uint64_t* parameter_pages;
    uint32_t parameter_page_count;
    uint32_t page_reserved;
    const float* extra_parameters;
    const URUIPlanTargetRequest* targets;
    uint32_t target_count;
    uint32_t target_reserved;
} URUIRenderPlanPacket;

typedef struct
{
    const URUIPlanVertex* vertices;
    const uint32_t* indices;
    uint32_t vertex_count;
    uint32_t index_count;
    uint64_t version;
} URUIGeometryView;

#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_plan_begin(URUISurfaceHandle surface, URUIPlanInputPacket* out);
    URUI_API int urui_plan_resolve(URUISurfaceHandle surface, uint64_t input_lease, const URUIPlanFeedback* feedbacks,
                                   uint32_t feedback_count, const URUIPlanBindings* resources,
                                   URUIPlanPreparePacket* out);
    URUI_API int urui_plan_compile(URUISurfaceHandle surface, uint64_t input_lease, const URUIPlanBindings* bindings,
                                   URUIRenderPlanPacket* out);
    URUI_API int urui_plan_geometry(URUISurfaceHandle surface, uint64_t plan_lease, uint64_t geometry,
                                    URUIGeometryView* out);
    URUI_API int urui_plan_image_program(URUISurfaceHandle surface, uint64_t input_lease, uint64_t request,
                                         uint64_t program, uint64_t owner, uint64_t layout, const float* runtime_rows,
                                         uint32_t runtime_count, uint32_t field_count, const float* bound_rows,
                                         uint64_t clock);
    URUI_API int urui_plan_image_geometry(URUISurfaceHandle surface, uint64_t input_lease, uint64_t request,
                                          const URUIPlanExternalGeometry* geometry, const float* local_transform);
    URUI_API int urui_plan_release(URUISurfaceHandle surface, uint64_t lease);
#ifdef __cplusplus
}
#endif
