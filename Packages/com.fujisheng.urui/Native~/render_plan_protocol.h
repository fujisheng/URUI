#pragma once
#include <stdint.h>
#include "urui.h"

// 绘制计划协议6。所有视图归其租约所有，不归 Unity 对象所有。
enum { URUI_RENDER_PLAN_PROTOCOL = 8 };
// Layer 操作 reason bit1 表示 bounds 为最终合成的整数裁剪 min/max；bit0仍为免写缓存。
enum { URUI_PLAN_LAYER_CLIP = 2 };
// 原有操作不增删：起止标志包围最多8组颜色往返，bits24..26编码组数减一。
enum { URUI_PLAN_RASTER_BEGIN=0x20000000u,URUI_PLAN_RASTER_END=0x10000000u,
       URUI_PLAN_RASTER_GROUP_SHIFT=24,URUI_PLAN_RASTER_REGION=0x00800000u };
enum UruiPlanOperationKind
{
    URUI_PLAN_GEOMETRY = 1, URUI_PLAN_EXTERNAL = 2, URUI_PLAN_IMAGE = 3,
    URUI_PLAN_LAYER = 5, URUI_PLAN_EXTERNAL_GEOMETRY = 6, URUI_PLAN_EXTERNAL_RAW_GEOMETRY = 7, URUI_PLAN_PARAMETER_IMAGE = 8, URUI_PLAN_ROOT_COPY = 9, URUI_PLAN_ROOT_PRESENT = 10
};
enum UruiPlanPrepareKind
{
    URUI_PLAN_READY = 0, URUI_PLAN_PREPARE_IMAGE = 1, URUI_PLAN_ELEMENT_LAYOUT = 2,
    URUI_PLAN_ACQUIRE_CAPTURE = 3, URUI_PLAN_PREPARE_ELEMENT = 4
};
typedef struct
{
    float x, y, z;
    uint8_t r, g, b, a;
    float u, v, texture, parameter;
} UruiPlanVertex;
typedef struct
{
    // 普通几何 metadata[0] bit9：第三列 XY 为无损布局原点，先解码局部顶点再乘矩阵。
    // 其他程序仍使用完整 4x4；该标记不改变任何外部几何的 z 或投影语义。
    float matrix[16], clip[4], tint[4], uv[4], metadata[4];
} UruiPlanParameter;
typedef struct
{
    float tint[4], uv[4];
} UruiPlanUniform;
typedef struct
{
    uint64_t geometry, textures[8];
    uint32_t kind, command_index, count, texture_count, layer_index, reason;
    // Window-space min/max; reason bit31 marks an unbounded projection.
    // For ordinary geometry, bit30 proves vertices/UV/colors are complete with no parameter reads.
    // bit23 marks a CSS group's first operation: bounds conservatively cover the
    // whole group's writes, for paired color conversions on a proven 8-bit root.
    float bounds[4];
    uint64_t mask_texture,program;
} UruiPlanOperation;
// Unity mesh storage is borrowed only for the input lease's compile window.
// Formats are protocol values, independent of Unity's enum numbers.
enum UruiPlanAttributeFormat { URUI_PLAN_FLOAT32=1,URUI_PLAN_UNORM8=2,URUI_PLAN_FLOAT16=3 };
typedef struct {const void* data;uint64_t bytes;uint32_t stride,reserved;} UruiPlanVertexStream;
typedef struct {uint32_t stream,offset,format,dimension;} UruiPlanAttribute;
typedef struct
{
    uint64_t geometry,version;
    UruiPlanVertexStream streams[4];
    const void* indices;
    uint64_t index_bytes;
    uint32_t vertex_count,index_count,index_start;
    int32_t base_vertex;
    uint32_t stream_count,index_stride;
    UruiPlanAttribute position,color,uv;
} UruiPlanExternalGeometry;
typedef struct
{
    uint32_t command,geometry_index;
    uint64_t provider,program;
    UruiPlanParameter parameter;
    // Flags: 1 raw vertex contract, 2 unknown or externally changing program resources, 4 analytic unit rectangle (exclusive).
    uint32_t flags,reserved;
} UruiPlanExternalDraw;
typedef struct
{
    uint64_t request, document, element, program, geometry, layer, source_version, resource;
    uint32_t kind, slot, pixel_width, pixel_height;
    float capture[4], uv[4], source_ink[4];
} UruiPlanPrepareRequest;
typedef struct
{
    uint64_t lease, epoch;
    const UruiCommand* commands;
    const float* matrices;
    const UruiPaintCommand* paints;
    const UruiLayerCommand* layers;
    uint32_t command_count, paint_count, layer_count, protocol;
    const UruiPlanUniform* uniforms;
    const uint64_t* command_layers;
} UruiPlanInputPacket;
typedef struct
{
    uint64_t request, resource, source_version;
    uint32_t kind, pixel_width, pixel_height, transform_count;
    float ink[4], expansion[4], uv[4];
    const float* transforms;
    // Element preparation capability: bit0 uses its explicit Unity blend contract.
    uint32_t capabilities,reserved;
} UruiPlanFeedback;
typedef struct
{
    uint64_t layer;
    float bounds[4];
    uint32_t flags, reserved;
    uint64_t version;
} UruiPlanLayerRegion;
typedef struct
{
    uint64_t lease;
    const UruiPlanPrepareRequest* requests;
    uint32_t count, ready;
    const UruiPlanLayerRegion* regions;
    // Target flags: bit0 requires a sampleable screen-root working texture.
    uint32_t region_count, target_flags;
    const uint32_t* layer_indices;
    uint32_t layer_index_count, layer_reserved;
} UruiPlanPreparePacket;
typedef struct
{
    uint64_t texture, version;
    uint32_t domain, flags;
} UruiPlanTextureBinding;
typedef struct
{
    // Flags: 1 parent writes, 2 original geometry, 4 offscreen writes.
    uint32_t command, flags;
} UruiPlanExternalBinding;
typedef struct
{
    uint64_t layer,version;
    uint32_t flags,reserved;
} UruiPlanLayerBinding;
typedef struct
{
    const UruiPlanTextureBinding* textures;
    // Frame flags: 1 pixel grid, 2 linear color, 4 HDR, 8 encoded raster, 16 screen root, 32 raw callbacks never return original geometry.
    // 512 proves the ordinary shader contract and an 8-bit screen-root color round trip.
    uint32_t texture_count, frame_flags;
    const UruiPlanExternalBinding* externals;
    uint32_t external_count, external_reserved;
    const UruiPlanLayerBinding* layer_bindings;
    uint32_t layer_binding_count,layer_reserved;
    const UruiPlanExternalGeometry* external_geometry;
    uint32_t external_geometry_count,geometry_reserved;
    const UruiPlanExternalDraw* external_draws;
    uint32_t external_draw_count,draw_reserved;
} UruiPlanBindings;
typedef struct
{
    uint64_t layer,related_layer;
    int32_t slot;
    uint32_t kind,first_operation,last_operation;
} UruiPlanTargetRequest;
typedef struct
{
    uint64_t lease, epoch, version, parameter_version;
    const UruiPlanOperation* operations;
    const UruiPlanParameter* parameters;
    uint32_t operation_count, parameter_count, protocol, extra_parameter_count;
    uint64_t geometry_bytes, frame_bytes, allocation_count, free_count, allocated_bytes;
    const uint64_t* parameter_pages;
    uint32_t parameter_page_count, page_reserved;
    const float* extra_parameters;
    const UruiPlanTargetRequest* targets;
    uint32_t target_count,target_reserved;
} UruiRenderPlanPacket;
typedef struct
{
    const UruiPlanVertex* vertices;
    const uint32_t* indices;
    uint32_t vertex_count, index_count;
    uint64_t version;
} UruiGeometryView;

#ifdef __cplusplus
extern "C" {
#endif
URUI_API int urui_plan_begin(UruiPlanInputPacket* out);
URUI_API int urui_plan_resolve(uint64_t input_lease, const UruiPlanFeedback* feedback, const UruiPlanBindings* resources, UruiPlanPreparePacket* out);
URUI_API int urui_plan_compile(uint64_t input_lease, const UruiPlanBindings* bindings, UruiRenderPlanPacket* out);
URUI_API int urui_plan_geometry(uint64_t plan_lease, uint64_t geometry, UruiGeometryView* out);
// Called only for the pending image or captured-element preparation request.
// Runtime rows (2..32) precede fixed CSS rows; all are frozen in the input lease.
// The first two runtime rows retain the common bounds/size/time/source contract.
URUI_API int urui_plan_image_program(uint64_t input_lease,uint64_t request,uint64_t program,
    uint64_t owner,uint64_t layout,const float* runtime_rows,uint32_t runtime_count,uint32_t field_count,const float* bound_rows,uint64_t clock);
URUI_API int urui_plan_image_geometry(uint64_t input_lease,uint64_t request,const UruiPlanExternalGeometry* geometry,const float* local_transform);
URUI_API int urui_plan_release(uint64_t lease);
#ifdef __cplusplus
}
#endif
