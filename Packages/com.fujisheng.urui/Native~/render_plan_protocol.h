#pragma once
#include <stdint.h>
#include "urui.h"

// 绘制计划协议6。所有视图归其租约所有，不归 Unity 对象所有。
enum
{
    URUI_RENDER_PLAN_PROTOCOL = 8
};
enum URUIPlanResourceFlag
{
    URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT = 1,
    URUI_PLAN_RESOURCE_LINEAR_COLOR = 2,
    URUI_PLAN_RESOURCE_HDR_CAMERA = 4,
    URUI_PLAN_RESOURCE_ENCODED_RASTER = 8,
    URUI_PLAN_RESOURCE_SCREEN_OUTPUT = 16,
    URUI_PLAN_RESOURCE_SHADER_IMAGE_PAINT = 32,
    URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH = 64,
    URUI_PLAN_RESOURCE_HIGH_PRECISION_ROOT = 128,
    URUI_PLAN_RESOURCE_COLOR_PASS = 256,
    URUI_PLAN_RESOURCE_BOUNDED_COLOR_CONVERSIONS = 512,
    URUI_PLAN_RESOURCE_SHARED_FINAL_ROOT_COLOR_PASS = 1024
};

// Layer 操作 reason bit1 表示 bounds 为最终合成的整数裁剪 min/max；bit0仍为免写缓存。
enum
{
    URUI_PLAN_LAYER_CLIP = 2
};
// 原有操作不增删：起止标志包围最多8组颜色往返，bits24..26编码组数减一。
enum
{
    URUI_PLAN_RASTER_BEGIN = 0x20000000u,
    URUI_PLAN_RASTER_END = 0x10000000u,
    URUI_PLAN_RASTER_GROUP_SHIFT = 24,
    URUI_PLAN_RASTER_REGION = 0x00800000u
};
enum URUIPlanOperationKind
{
    URUI_PLAN_GEOMETRY = 1,
    URUI_PLAN_EXTERNAL = 2,
    URUI_PLAN_IMAGE = 3,
    URUI_PLAN_LAYER = 5,
    URUI_PLAN_EXTERNAL_GEOMETRY = 6,
    URUI_PLAN_EXTERNAL_RAW_GEOMETRY = 7,
    URUI_PLAN_PARAMETER_IMAGE = 8,
    URUI_PLAN_ROOT_COPY = 9,
    URUI_PLAN_ROOT_PRESENT = 10
};
enum URUIPlanPrepareKind
{
    URUI_PLAN_READY = 0,
    URUI_PLAN_PREPARE_IMAGE = 1,
    URUI_PLAN_ELEMENT_LAYOUT = 2,
    URUI_PLAN_ACQUIRE_CAPTURE = 3,
    URUI_PLAN_PREPARE_ELEMENT = 4
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
    float parameter;
} URUIPlanVertex;
typedef struct
{
    // 普通几何 metadata[0] bit9：第三列 XY 为无损布局原点，先解码局部顶点再乘矩阵。
    // 其他程序仍使用完整 4x4；该标记不改变任何外部几何的 z 或投影语义。
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
    // Window-space min/max; reason bit31 marks an unbounded projection.
    // For ordinary geometry, bit30 proves vertices/UV/colors are complete with no parameter reads.
    // bit23 marks a CSS group's first operation: bounds conservatively cover the
    // whole group's writes, for paired color conversions on a proven 8-bit root.
    float bounds[4];
    uint64_t mask_texture;
    uint64_t program;
} URUIPlanOperation;
// Unity mesh storage is borrowed only for the input lease's compile window.
// Formats are protocol values, independent of Unity's enum numbers.
enum URUIPlanAttributeFormat
{
    URUI_PLAN_FLOAT32 = 1,
    URUI_PLAN_UNORM8 = 2,
    URUI_PLAN_FLOAT16 = 3
};
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
    // Flags: 1 raw vertex contract, 2 unknown or externally changing program resources, 4 analytic unit rectangle (exclusive).
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
typedef struct
{
    uint64_t request;
    uint64_t resource;
    uint64_t source_version;
    uint32_t kind;
    uint32_t pixel_width;
    uint32_t pixel_height;
    uint32_t transform_count;
    float ink[4];
    float expansion[4];
    float uv[4];
    const float* transforms;
    // Element preparation capability: bit0 uses its explicit Unity blend contract.
    uint32_t capabilities;
    uint32_t reserved;
} URUIPlanFeedback;
typedef struct
{
    uint64_t layer;
    float bounds[4];
    uint32_t flags;
    uint32_t reserved;
    uint64_t version;
} URUIPlanLayerRegion;
typedef struct
{
    uint64_t lease;
    const URUIPlanPrepareRequest* requests;
    uint32_t count;
    uint32_t ready;
    const URUIPlanLayerRegion* regions;
    // Target flags: bit0 requires a sampleable screen-root working texture.
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
    // Flags: 1 parent writes, 2 original geometry, 4 offscreen writes.
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
    // Frame flags: 1 pixel grid, 2 linear color, 4 HDR, 8 encoded raster, 16 screen root, 32 raw callbacks never return original geometry.
    // 512 proves the ordinary shader contract and an 8-bit screen-root color round trip.
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
    URUI_API int urui_plan_begin(URUIPlanInputPacket* out);
    URUI_API int urui_plan_resolve(uint64_t input_lease, const URUIPlanFeedback* feedback,
                                   const URUIPlanBindings* resources, URUIPlanPreparePacket* out);
    URUI_API int urui_plan_compile(uint64_t input_lease, const URUIPlanBindings* bindings, URUIRenderPlanPacket* out);
    URUI_API int urui_plan_geometry(uint64_t plan_lease, uint64_t geometry, URUIGeometryView* out);
    // Called only for the pending image or captured-element preparation request.
    // Runtime rows (2..32) precede fixed CSS rows; all are frozen in the input lease.
    // The first two runtime rows retain the common bounds/size/time/source contract.
    URUI_API int urui_plan_image_program(uint64_t input_lease, uint64_t request, uint64_t program, uint64_t owner,
                                         uint64_t layout, const float* runtime_rows, uint32_t runtime_count,
                                         uint32_t field_count, const float* bound_rows, uint64_t clock);
    URUI_API int urui_plan_image_geometry(uint64_t input_lease, uint64_t request,
                                          const URUIPlanExternalGeometry* geometry, const float* local_transform);
    URUI_API int urui_plan_release(uint64_t lease);
#ifdef __cplusplus
}
#endif
