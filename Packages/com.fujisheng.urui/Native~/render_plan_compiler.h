#pragma once
#include "render_plan_protocol.h"
#include "render_direct_mask.h"
#include "render_external_geometry.h"
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>
#include <new>
#include <stdexcept>
#include <thread>
#include <vector>

namespace urui_plan
{
    // 单叶直绘仍保留抽象组依赖；二值遮罩删除的是源内容修改，两者不能共用缓存签名语义。
    static constexpr uint8_t OmittedDirectMask=1,OmittedBinaryMask=2;
    static_assert(sizeof(UruiPlanVertex)==32 && offsetof(UruiPlanVertex,parameter)==28,"plan vertex ABI");
    static_assert(sizeof(UruiPlanParameter)==128,"plan parameter ABI");
    static_assert(sizeof(UruiPlanOperation)==128,"operation ABI");
    static_assert(offsetof(UruiPlanInputPacket,command_count)==16+4*sizeof(void*),"input pointer ABI");
    static_assert(sizeof(UruiPlanInputPacket)==(sizeof(void*)==8?80:56),"input packet ABI");
    static_assert(sizeof(UruiPlanBindings)==(sizeof(void*)==8?80:60)&&sizeof(UruiPlanExternalBinding)==8,"bindings ABI");
    static_assert(sizeof(UruiRenderPlanPacket)==(sizeof(void*)==8?144:128),"output packet ABI");
    static_assert(offsetof(UruiRenderPlanPacket,extra_parameters)==(sizeof(void*)==8?120:108),"extra parameter pointer ABI");
    static_assert(sizeof(UruiPlanPrepareRequest)==128&&sizeof(UruiPlanFeedback)==104,"preparation ABI");
    static_assert(sizeof(UruiGeometryView)==(sizeof(void*)==8?32:24),"geometry view ABI");
    static_assert(sizeof(UruiPlanLayerRegion)==40&&sizeof(UruiPlanPreparePacket)==(sizeof(void*)==8?56:48),"prepared layer ABI");
    struct AllocationCounters { uint64_t allocations=0, frees=0, bytes=0; };
    struct Budget
    {
        AllocationCounters* counters;
        size_t retained=0, limit;
    };
    // Every heap allocation owned by the compiler, including metadata, uses this allocator.
    template<class T> struct Allocator
    {
        using value_type=T;
        using propagate_on_container_move_assignment=std::true_type;
        Budget* budget=nullptr;
        explicit Allocator(Budget* b=nullptr):budget(b){}
        template<class U> Allocator(const Allocator<U>& other):budget(other.budget){}
        T* allocate(size_t n)
        {
            if(n>SIZE_MAX/sizeof(T))throw std::bad_alloc();
            const size_t bytes=n*sizeof(T);
            if(!budget || bytes>budget->limit-budget->retained)throw std::bad_alloc();
            auto* out=static_cast<T*>(::operator new(bytes));
            budget->retained+=bytes;
            budget->counters->allocations++;budget->counters->bytes+=bytes;
            return out;
        }
        void deallocate(T* pointer,size_t n) noexcept
        {
            if(!pointer)return;
            budget->retained-=n*sizeof(T);budget->counters->frees++;::operator delete(pointer);
        }
        template<class U> bool operator==(const Allocator<U>& b)const{return budget==b.budget;}
        template<class U> bool operator!=(const Allocator<U>& b)const{return !(*this==b);}
    };
    template<class T> using Buffer=std::vector<T,Allocator<T>>;
    inline uint64_t Identity()
    {
        static std::atomic<uint64_t> next{0};
        auto value=next.load(std::memory_order_relaxed);
        do {if(value==UINT64_MAX)throw std::overflow_error("URUI render identity exhausted");}
        while(!next.compare_exchange_weak(value,value+1,std::memory_order_relaxed));
        return value+1;
    }
    struct GeometrySource
    {
        const UruiVertex* vertices=nullptr;
        size_t vertex_count=0;
        const int* indices=nullptr;
        size_t index_count=0;
        const float* bounds=nullptr;
        const UruiPlanExternalGeometry* external=nullptr;
    };
    struct GeometryKey
    {
        uint64_t geometry;
        uint32_t parameter, texture;
        uint64_t version=0;
        float origin_x=0,origin_y=0; // bit31 of texture marks ordinary layout-space vertices.
        bool operator==(const GeometryKey& other)const{return geometry==other.geometry&&parameter==other.parameter&&texture==other.texture&&version==other.version;}
    };
    struct Batch
    {
        uint64_t id=0,epoch=0,last_use=0;
        Buffer<GeometryKey> keys;
        Buffer<UruiPlanVertex> vertices;
        Buffer<uint32_t> indices;
        explicit Batch(Budget* budget):keys(Allocator<GeometryKey>(budget)),vertices(Allocator<UruiPlanVertex>(budget)),indices(Allocator<uint32_t>(budget)){}
        Batch(Batch&&)=default;
        Batch& operator=(Batch&&)=default;
    };
    struct InkRegion
    {
        double x0=0,y0=0,x1=0,y1=0;
        bool valid=false;
        void Point(double x,double y)
        {
            if(!std::isfinite(x)||!std::isfinite(y))throw std::invalid_argument("nonfinite ink");
            if(!valid){x0=x1=x;y0=y1=y;valid=true;}
            else{x0=std::min(x0,x);y0=std::min(y0,y);x1=std::max(x1,x);y1=std::max(y1,y);}
        }
        void Join(const InkRegion& b){if(b.valid){Point(b.x0,b.y0);Point(b.x1,b.y1);}}
        void Rect(const float* b){Point(b[0],b[1]);Point(double(b[0])+b[2],double(b[1])+b[3]);}
        void Capture(float* out)const
        {
            double x=valid?std::floor(x0):0,y=valid?std::floor(y0):0;
            double w=valid?std::max(1.,std::ceil(x1)-x):1,h=valid?std::max(1.,std::ceil(y1)-y):1;
            if(w>8192||h>8192||std::abs(x)>1000000||std::abs(y)>1000000)throw std::invalid_argument("capture bounds");
            out[0]=float(x);out[1]=float(y);out[2]=float(w);out[3]=float(h);
        }
    };
    struct MaskCropUsage { uint32_t creates=0, reads=0; bool opaque=false; };
    struct LayerEffect
    {
        uint32_t layer_index;
        uint64_t document,element,program;
        uint32_t kind; // 1 blur, 2 shadow, 3 managed element, 0 point operation.
        float sigma,offset_x,offset_y,value;
        std::array<float,16> projection{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
        bool cacheable=false;
    };
    struct ColorFilterInput {uint32_t command,kind;float value;};
    struct PrepareSlot
    {
        UruiPlanPrepareRequest request{};
        InkRegion ink;
        std::array<float,4> expansion{};
        uint32_t transform_offset=0,transform_count=0,layer_index=0;
        uint32_t source_index=0,geometry_index=UINT32_MAX;
        std::array<float,16> geometry_transform{};
        bool image=false,layout=false,prepared=false,source_ready=false,unity_blend=false;
    };
    struct SourceSnapshot
    {
        uint64_t geometry=0,version=0,epoch=0,composed_version=0;
        UruiPlanUniform uniform{};
        Buffer<UruiVertex> base_vertices;
        Buffer<UruiPlanVertex> vertices;
        Buffer<uint32_t> indices;
        explicit SourceSnapshot(Budget* geometry_budget):base_vertices(Allocator<UruiVertex>(geometry_budget)),vertices(Allocator<UruiPlanVertex>(geometry_budget)),indices(Allocator<uint32_t>(geometry_budget)){}
        SourceSnapshot(SourceSnapshot&&)=default;
        SourceSnapshot& operator=(SourceSnapshot&&)=default;
    };
    #include "render_layer_dependencies.h"
    struct ImageProgram {uint64_t program=0;uint32_t offset=0,count=0,clock=UINT32_MAX;};
    struct Frame
    {
        uint64_t token=0,epoch=0,version=0,parameter_version=0;
        bool input=false,compiled=false;
        Buffer<UruiCommand> commands;
        Buffer<std::array<float,16>> matrices;
        Buffer<UruiPlanUniform> uniforms;
        Buffer<uint64_t> command_layers;
        Buffer<int32_t> raw_flags,layer_composites;
        Buffer<uint8_t> halo_layers;
        Buffer<UruiPaintCommand> paints;
        Buffer<UruiLayerCommand> layers;
        Buffer<UruiPlanOperation> operations;
        Buffer<UruiPlanTargetRequest> targets;
        Buffer<uint32_t> target_lookup;
        Buffer<uint8_t> target_capabilities;
        Buffer<UruiPlanParameter> parameters;
        Buffer<std::array<float,4>> extra_parameters;
        Buffer<std::array<float,4>> clocks;
        Buffer<uint32_t> clock_lookup;
        Buffer<ImageProgram> image_programs,captured_programs;
        Buffer<uint32_t> captured_parameters;
        Buffer<std::array<float,4>> image_parameters;
        Buffer<uint32_t> image_pending;
        Buffer<ColorFilterInput> color_filters;
        Buffer<int32_t> color_offsets;
        Buffer<uint32_t> color_counts;
        Buffer<uint64_t> parameter_pages;
        Buffer<LayerEffect> effects;
        Buffer<PrepareSlot> preparation;
        Buffer<SourceSnapshot> sources;
        Buffer<int32_t> image_slots,effect_indices,effect_slots;
        Buffer<std::array<float,16>> coverage;
        Buffer<InkRegion> layer_ink;
        Buffer<MaskCropUsage> mask_crop_usage;
        Buffer<uint8_t> created_layers;
        Buffer<UruiPlanLayerRegion> layer_regions;
        Buffer<uint32_t> layer_indices;
        Buffer<uint8_t> omitted_layers,omitted_operations,suppressed_commands;
        Buffer<int32_t> direct_mask_indices;
        Buffer<DirectMask> direct_masks;
        struct BinaryMaskClip { uint32_t layer_index;std::array<float,4> bounds; };
        Buffer<BinaryMaskClip> binary_mask_clips;
        Buffer<LayerDependencyState> dependencies;
        Buffer<uint8_t> cached_layers,known_providers;
        Buffer<UruiPlanExternalGeometry> external_geometry;
        Buffer<UruiPlanExternalDraw> external_draws;
        Buffer<int32_t> external_offsets;
        Buffer<uint32_t> external_counts,external_lookup;
        Buffer<std::array<float,6>> external_bounds;
        Buffer<uint32_t> external_pending;
        Buffer<UruiPlanTextureBinding> resources;
        uint32_t resource_flags=0;
        Buffer<uint64_t> layer_stack,last_layer_write,mask_save_sequence;
        Buffer<uint32_t> mask_copy_sources,mask_copy_counts;
        Buffer<uint8_t> mask_copy_state;
        uint32_t pending=UINT32_MAX;
        uint32_t image_cursor=0,layout_cursor=0,element_cursor=0;
        uint32_t capture_command=0,capture_layer=0;bool capture_started=false;
        uint32_t width=8192,height=8192;
        bool resolving=false,ready=false;
        explicit Frame(Budget* budget):commands(Allocator<UruiCommand>(budget)),matrices(Allocator<std::array<float,16>>(budget)),uniforms(Allocator<UruiPlanUniform>(budget)),command_layers(Allocator<uint64_t>(budget)),
            raw_flags(Allocator<int32_t>(budget)),layer_composites(Allocator<int32_t>(budget)),halo_layers(Allocator<uint8_t>(budget)),
            paints(Allocator<UruiPaintCommand>(budget)),layers(Allocator<UruiLayerCommand>(budget)),
            operations(Allocator<UruiPlanOperation>(budget)),targets(Allocator<UruiPlanTargetRequest>(budget)),target_lookup(Allocator<uint32_t>(budget)),target_capabilities(Allocator<uint8_t>(budget)),parameters(Allocator<UruiPlanParameter>(budget)),
            extra_parameters(Allocator<std::array<float,4>>(budget)),clocks(Allocator<std::array<float,4>>(budget)),clock_lookup(Allocator<uint32_t>(budget)),image_programs(Allocator<ImageProgram>(budget)),captured_programs(Allocator<ImageProgram>(budget)),captured_parameters(Allocator<uint32_t>(budget)),image_parameters(Allocator<std::array<float,4>>(budget)),image_pending(Allocator<uint32_t>(budget)),color_filters(Allocator<ColorFilterInput>(budget)),color_offsets(Allocator<int32_t>(budget)),color_counts(Allocator<uint32_t>(budget)),parameter_pages(Allocator<uint64_t>(budget)),
            effects(Allocator<LayerEffect>(budget)),preparation(Allocator<PrepareSlot>(budget)),sources(Allocator<SourceSnapshot>(budget)),image_slots(Allocator<int32_t>(budget)),
            effect_indices(Allocator<int32_t>(budget)),effect_slots(Allocator<int32_t>(budget)),
            coverage(Allocator<std::array<float,16>>(budget)),layer_ink(Allocator<InkRegion>(budget)),mask_crop_usage(Allocator<MaskCropUsage>(budget)),
            created_layers(Allocator<uint8_t>(budget)),layer_regions(Allocator<UruiPlanLayerRegion>(budget)),
            layer_indices(Allocator<uint32_t>(budget)),omitted_layers(Allocator<uint8_t>(budget)),omitted_operations(Allocator<uint8_t>(budget)),
            suppressed_commands(Allocator<uint8_t>(budget)),direct_mask_indices(Allocator<int32_t>(budget)),direct_masks(Allocator<DirectMask>(budget)),binary_mask_clips(Allocator<BinaryMaskClip>(budget)),dependencies(Allocator<LayerDependencyState>(budget)),cached_layers(Allocator<uint8_t>(budget)),known_providers(Allocator<uint8_t>(budget)),
            external_geometry(Allocator<UruiPlanExternalGeometry>(budget)),external_draws(Allocator<UruiPlanExternalDraw>(budget)),
            external_offsets(Allocator<int32_t>(budget)),external_counts(Allocator<uint32_t>(budget)),external_lookup(Allocator<uint32_t>(budget)),external_bounds(Allocator<std::array<float,6>>(budget)),external_pending(Allocator<uint32_t>(budget)),resources(Allocator<UruiPlanTextureBinding>(budget)),layer_stack(Allocator<uint64_t>(budget)),
            last_layer_write(Allocator<uint64_t>(budget)),mask_save_sequence(Allocator<uint64_t>(budget)),
            mask_copy_sources(Allocator<uint32_t>(budget)),mask_copy_counts(Allocator<uint32_t>(budget)),mask_copy_state(Allocator<uint8_t>(budget)){}
    };
#include "render_target_requests.h"
    class Session
    {
        AllocationCounters counters;
        Budget geometry_budget,frame_budget;
        LayerDependencyCache dependency_cache;
        std::thread::id owner=std::this_thread::get_id();
        uint64_t epoch=0;
        bool retired_epoch_batches=false;
        uint32_t width=8192,height=8192;
        std::array<Frame,4> frames;
        Buffer<Batch> batches;
        Buffer<GeometryKey> keys;
        Buffer<size_t> geometry_hints;
        size_t geometry_hint_cursor=0;
        Buffer<UruiPaintCommand> paint_scratch;
        Buffer<LayerEffect> effect_scratch;
        Buffer<ColorFilterInput> color_scratch;
        Buffer<UruiPlanOperation> previous_operations;
        Buffer<UruiPlanParameter> previous_parameters;
        Buffer<std::array<float,4>> previous_extra_parameters;
        Buffer<uint64_t> previous_parameter_pages;
        uint64_t previous_epoch=0,operation_version=0,parameter_version=0;
        Buffer<UruiCommand> ordinary_commands;
        Buffer<std::array<float,16>> ordinary_matrices;
        Buffer<UruiPlanUniform> ordinary_uniforms;
        Buffer<UruiPlanTextureBinding> ordinary_resources,ordinary_source_resources;
        uint64_t ordinary_epoch=0;
        uint32_t ordinary_width=0,ordinary_height=0,ordinary_source_flags=0,ordinary_binding_flags=0;
        bool ordinary_valid=false;
        struct SortNode {uint32_t command=0,state=0,depth=0,seen=0;InkRegion bounds;};
        struct SortState {uint64_t texture=0,mask=0;uint32_t domain=0,generation=0,value=0;};
        struct Cell {uint64_t key=0;uint32_t generation=0;int head=-1;};
        struct Link {uint32_t item;int next;};
        struct SortMemoKey { UruiCommand command{};std::array<float,16> matrix{};uint64_t mask=0;uint32_t domain=0; };
        Buffer<SortMemoKey> sort_memo;
        Buffer<uint32_t> sort_memo_order;
        uint64_t sort_memo_epoch=0;
        Buffer<SortNode> sort_nodes;
        Buffer<SortState> sort_states;
        Buffer<Cell> sort_cells;
        Buffer<Link> sort_links;
        Buffer<uint32_t> pending,order,large;
        uint32_t sort_generation=0;
        uint64_t sort_builds=0,ordinary_reuses=0,geometry_searches=0;
        Frame* compiling=nullptr;
        template<class T> static bool Same(const Buffer<T>& a,const Buffer<T>& b)
        {return a.size()==b.size()&&(a.empty()||std::memcmp(a.data(),b.data(),a.size()*sizeof(T))==0);}
        Frame* Find(uint64_t token)
        {
            if(token)for(auto& frame:frames)if(frame.token==token)return &frame;
            return nullptr;
        }
        Frame* Free(){for(auto& frame:frames)if(!frame.token)return &frame;return nullptr;}
        static bool RectValid(const float* b)
        {return std::isfinite(b[0])&&std::isfinite(b[1])&&std::isfinite(b[2])&&std::isfinite(b[3])&&b[2]>=0&&b[3]>=0;}
        static InkRegion LocalBounds(const GeometrySource& mesh)
        {
            InkRegion out;
            if(mesh.bounds){out.Point(mesh.bounds[0],mesh.bounds[1]);out.Point(mesh.bounds[2],mesh.bounds[3]);}
            else for(size_t v=0;v<mesh.vertex_count;v++)out.Point(mesh.vertices[v].x,mesh.vertices[v].y);
            return out;
        }
        static void TransformInk(InkRegion& out,const InkRegion& ink,const float* m,double tx=0,double ty=0)
        {
            if(!ink.valid)return;
            if(m[3]==0&&m[7]==0&&m[15]==1)
            {
                const double x0=ink.x0+tx,x1=ink.x1+tx,y0=ink.y0+ty,y1=ink.y1+ty;
                out.Point(m[0]*(m[0]>=0?x0:x1)+m[4]*(m[4]>=0?y0:y1)+m[12],
                    m[1]*(m[1]>=0?x0:x1)+m[5]*(m[5]>=0?y0:y1)+m[13]);
                out.Point(m[0]*(m[0]>=0?x1:x0)+m[4]*(m[4]>=0?y1:y0)+m[12],
                    m[1]*(m[1]>=0?x1:x0)+m[5]*(m[5]>=0?y1:y0)+m[13]);
                return;
            }
            double low=std::numeric_limits<double>::infinity(),high=-low;
            for(int corner=0;corner<4;corner++)
            {
                double x=(corner&1?ink.x1:ink.x0)+tx,y=(corner&2?ink.y1:ink.y0)+ty;
                double w=m[3]*x+m[7]*y+m[15];low=std::min(low,w);high=std::max(high,w);
                if(std::abs(w)<1e-9)throw std::invalid_argument("unbounded projected ink");
                out.Point((m[0]*x+m[4]*y+m[12])/w,(m[1]*x+m[5]*y+m[13])/w);
            }
            if(low<0&&high>0)throw std::invalid_argument("projection crosses infinity");
        }
        static void TransformExternalInk(InkRegion& out,const std::array<float,6>& bounds,const float* matrix)
        {
            double low=std::numeric_limits<double>::infinity(),high=-low;
            const int corners=bounds[2]==bounds[5]?4:8;
            for(int corner=0;corner<corners;corner++)
            {
                const double x=bounds[corner&1?3:0],y=bounds[corner&2?4:1],z=bounds[corner&4?5:2];
                const double w=matrix[3]*x+matrix[7]*y+matrix[11]*z+matrix[15];low=std::min(low,w);high=std::max(high,w);
                if(std::abs(w)<1e-9)throw std::invalid_argument("unbounded external ink");
                out.Point((matrix[0]*x+matrix[4]*y+matrix[8]*z+matrix[12])/w,(matrix[1]*x+matrix[5]*y+matrix[9]*z+matrix[13])/w);
            }
            if(low<0&&high>0)throw std::invalid_argument("external projection crosses infinity");
        }
        static uint64_t Hash(uint64_t value)
        {value^=value>>30;value*=0xbf58476d1ce4e5b9ULL;value^=value>>27;value*=0x94d049bb133111ebULL;return value^(value>>31);}
        static bool CellBounds(const InkRegion& b,int& x0,int& y0,int& x1,int& y1)
        {
            if(!b.valid||std::abs(b.x0)>1e7||std::abs(b.y0)>1e7||std::abs(b.x1)>1e7||std::abs(b.y1)>1e7)return false;
            x0=int(std::floor(b.x0/32));y0=int(std::floor(b.y0/32));x1=int(std::floor(b.x1/32));y1=int(std::floor(b.y1/32));
            return int64_t(x1-x0+1)*(y1-y0+1)<=64;
        }
        static bool Disjoint(const InkRegion& a,const InkRegion& b)
        {return a.valid&&b.valid&&(a.x1<=b.x0||b.x1<=a.x0||a.y1<=b.y0||b.y1<=a.y0);}
        template<class Resolve> void Order(const Frame& source,const Frame& target,const UruiPlanBindings& bindings,Resolve resolve)
        {
            order.resize(pending.size());
            // A compatible palette needs no spatial graph or per-node graph data.
            std::array<SortState,8> palette{};uint32_t palette_count=0,domain=0;uint64_t mask=0;bool graph=false;
            for(uint32_t n=0;n<pending.size();n++)
            {
                order[n]=n;const auto command=pending[n];const auto& c=source.commands[command];
                const auto d=Domain(c.texture,bindings);const auto index=source.direct_mask_indices[command];
                const auto m=index>=0?source.direct_masks[index].texture:0;
                if(!n)domain=d;
                if(d!=domain||(mask&&m&&mask!=m)){graph=true;break;}
                if(m)mask=m;
                uint32_t slot=0;while(slot<palette_count&&(palette[slot].texture!=c.texture||palette[slot].domain!=d||palette[slot].mask!=m))slot++;
                if(slot==palette_count){if(palette_count==8){graph=true;break;}palette[palette_count++]={c.texture,m,d,0,0};}
            }
            if(!graph)return;
            bool same=sort_memo_epoch==source.epoch&&sort_memo.size()==pending.size();
            for(uint32_t n=0;same&&n<pending.size();n++)
            {
                const auto command=pending[n];const auto& c=source.commands[command];const auto& key=sort_memo[n];
                const auto index=source.direct_mask_indices[command];const auto m=index>=0?source.direct_masks[index].texture:0;
                same=std::memcmp(&key.command,&c,sizeof(c))==0&&std::memcmp(key.matrix.data(),target.parameters[command].matrix,16*sizeof(float))==0&&key.mask==m&&key.domain==Domain(c.texture,bindings);
            }
            if(same){std::copy(sort_memo_order.begin(),sort_memo_order.end(),order.begin());return;}
            sort_nodes.resize(pending.size());
            size_t capacity=16;while(capacity<pending.size()*2)capacity*=2;
            if(sort_states.size()<capacity)sort_states.resize(capacity);
            if(++sort_generation==0)
            {for(auto& entry:sort_states)entry.generation=0;for(auto& entry:sort_cells)entry.generation=0;sort_generation=1;}
            uint32_t state_count=0;bool one_domain=true,one_mask=true;uint32_t first_domain=0;uint64_t first_mask=0;
            for(uint32_t n=0;n<pending.size();n++)
            {
                const auto command=pending[n];order[n]=n;auto& node=sort_nodes[n];node={};node.command=command;
                const auto& c=source.commands[command];auto domain=Domain(c.texture,bindings);
                const auto mask_index=source.direct_mask_indices[command];
                const uint64_t mask=mask_index>=0?source.direct_masks[mask_index].texture:0;
                if(mask){if(!first_mask)first_mask=mask;else if(first_mask!=mask)one_mask=false;}
                if(!n)first_domain=domain;else if(domain!=first_domain)one_domain=false;
                auto slot=size_t(Hash(c.texture^Hash(domain)^Hash(mask)))&(sort_states.size()-1);
                while(sort_states[slot].generation==sort_generation&&
                    (sort_states[slot].texture!=c.texture||sort_states[slot].domain!=domain||sort_states[slot].mask!=mask))slot=(slot+1)&(sort_states.size()-1);
                auto& state=sort_states[slot];
                if(state.generation!=sort_generation)state={c.texture,mask,domain,sort_generation,state_count++};
                node.state=state.value;
            }
            // Already one compatible palette: keep the original triangle order and
            // avoid sorting or rebuilding indices when only parameters change.
            if(state_count<=8&&one_domain&&one_mask)return;
            sort_builds++;
            if(sort_cells.size()<32768)sort_cells.resize(32768);
            sort_links.clear();large.clear();uint32_t comparisons=0;
            for(uint32_t n=0;n<pending.size();n++)
            {
                auto& node=sort_nodes[n];const auto& c=source.commands[node.command];
                auto mesh=resolve(c.geometry);auto local=LocalBounds(mesh);
                try{TransformInk(node.bounds,local,target.parameters[node.command].matrix);}
                catch(const std::invalid_argument&){node.bounds={};} // Unbounded projection is an ordering barrier, not a culling claim.
                auto dependency=[&](uint32_t previous)
                {
                    if(++comparisons>262144)return false;
                    const auto& prior=sort_nodes[previous];
                    if(!Disjoint(node.bounds,prior.bounds))node.depth=std::max(node.depth,prior.depth+(node.state==prior.state?0u:1u));
                    return true;
                };
                int x0=0,y0=0,x1=0,y1=0;bool small=CellBounds(node.bounds,x0,y0,x1,y1);
                if(!small){for(uint32_t previous=0;previous<n;previous++)if(!dependency(previous))return;}
                else
                {
                    for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                    {
                        const uint64_t key=(uint64_t(uint32_t(x))<<32)|uint32_t(y);auto slot=size_t(Hash(key))&(sort_cells.size()-1);
                        while(sort_cells[slot].generation==sort_generation&&sort_cells[slot].key!=key)slot=(slot+1)&(sort_cells.size()-1);
                        const auto& cell=sort_cells[slot];if(cell.generation!=sort_generation)continue;
                        for(int link=cell.head;link>=0;link=sort_links[link].next)
                        {
                            auto& prior=sort_nodes[sort_links[link].item];
                            if(prior.seen!=n+1){prior.seen=n+1;if(!dependency(sort_links[link].item))return;}
                        }
                    }
                    for(auto previous:large)if(!dependency(previous))return;
                }
                if(!small){large.push_back(n);continue;}
                if(sort_links.size()+size_t(x1-x0+1)*(y1-y0+1)>16384)return;
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    const uint64_t key=(uint64_t(uint32_t(x))<<32)|uint32_t(y);auto slot=size_t(Hash(key))&(sort_cells.size()-1);
                    while(sort_cells[slot].generation==sort_generation&&sort_cells[slot].key!=key)slot=(slot+1)&(sort_cells.size()-1);
                    auto& cell=sort_cells[slot];if(cell.generation!=sort_generation)cell={key,sort_generation,-1};
                    sort_links.push_back({n,cell.head});cell.head=int(sort_links.size()-1);
                }
            }
            std::sort(order.begin(),order.end(),[&](uint32_t a,uint32_t b)
            {const auto& x=sort_nodes[a];const auto& y=sort_nodes[b];return x.depth!=y.depth?x.depth<y.depth:x.state!=y.state?x.state<y.state:a<b;});
            // Logical geometry IDs are immutable within an epoch. Keep the exact
            // inputs, not a hash-only dirty approximation or a resource pointer.
            sort_memo_epoch=0;sort_memo.resize(pending.size());sort_memo_order.assign(order.begin(),order.end());
            for(uint32_t n=0;n<pending.size();n++)
            {
                const auto command=pending[n];auto& key=sort_memo[n];key.command=source.commands[command];
                std::copy_n(target.parameters[command].matrix,16,key.matrix.data());key.domain=Domain(key.command.texture,bindings);
                const auto index=source.direct_mask_indices[command];key.mask=index>=0?source.direct_masks[index].texture:0;
            }
            sort_memo_epoch=source.epoch;
        }
        // Replay the frozen semantic stream only up to the effect being prepared. Child
        // outputs are already frozen, so a parent's input includes their final ink.
        template<class Resolve> InkRegion Capture(Frame& frame,uint32_t stop,Resolve resolve)
        {
            if(!frame.capture_started)
            {
                uint64_t maximum=0;
                for(const auto& c:frame.layers)maximum=std::max(maximum,std::max(c.source,c.target));
                if(maximum>65536)throw std::invalid_argument("layer identity");
                frame.layer_ink.assign(size_t(maximum)+1,InkRegion{});frame.created_layers.assign(size_t(maximum)+1,0);
                frame.layer_stack.clear();frame.layer_stack.push_back(0);frame.capture_started=true;
            }
            auto& layer=frame.capture_layer;
            auto paint=std::lower_bound(frame.paints.begin(),frame.paints.end(),frame.capture_command,
                [](const UruiPaintCommand& value,uint32_t command){return uint32_t(value.command_index)<command;});
            for(auto& n=frame.capture_command;n<=frame.commands.size();n++)
            {
                while(layer<frame.layers.size()&&frame.layers[layer].command_index==int(n))
                {
                    const auto& c=frame.layers[layer];
                    if(layer==stop)return frame.layer_ink[c.source];
                    if(c.kind==1){if(!c.target)throw std::invalid_argument("root layer push");frame.layer_ink[c.target]={};frame.created_layers[c.target]=1;frame.layer_stack.push_back(c.target);}
                    else if(c.kind==2){if(frame.layer_stack.size()<2||frame.layer_stack.back()!=c.source||frame.layer_stack[frame.layer_stack.size()-2]!=c.target)throw std::invalid_argument("unbalanced layer");frame.layer_stack.pop_back();}
                    else if(c.kind==3){if(!c.target)throw std::invalid_argument("root mask");frame.layer_ink[c.target]=frame.layer_ink[c.source];frame.created_layers[c.target]=1;}
                    else if(c.kind==4)
                    {
                        if(!c.source){InkRegion background;const float region[4]={float(c.flags?c.clip_x:0),float(c.flags?c.clip_y:0),float(c.flags?c.clip_w:frame.width),float(c.flags?c.clip_h:frame.height)};background.Rect(region);frame.layer_ink[c.target].Join(background);}
                        else frame.layer_ink[c.target].Join(frame.layer_ink[c.source]);
                    }
                    else if(c.kind==6&&!c.blend)
                    {
                        const int effect_index=frame.effect_indices[layer];
                        if(effect_index>=0)
                        {
                            const auto& effect=frame.effects[effect_index];
                            auto& ink=frame.layer_ink[c.source];
                            if(effect.kind==1||effect.kind==2)
                            {
                                auto shifted=ink;double halo=std::max(0.f,effect.sigma)*3;
                                if(shifted.valid){shifted.x0+=effect.offset_x-halo;shifted.y0+=effect.offset_y-halo;shifted.x1+=effect.offset_x+halo;shifted.y1+=effect.offset_y+halo;ink.Join(shifted);}
                            }
                            else if(effect.kind==3)
                            {
                                const int index=frame.effect_slots[layer];
                                if(index<0)throw std::invalid_argument("missing effect slot");
                                const auto& slot=frame.preparation[index];
                                if(!slot.prepared)throw std::invalid_argument("effect dependency not ready");ink.Join(slot.ink);
                            }
                        }
                    }
                    layer++;
                }
                if(n==frame.commands.size())break;
                const auto& command=frame.commands[n];
                while(paint!=frame.paints.end()&&uint32_t(paint->command_index)<n)++paint;
                if(frame.known_providers[n])
                {
                    for(uint32_t item=0;item<frame.external_counts[n];item++)
                    {
                        const auto& draw=frame.external_draws[frame.external_offsets[n]+item];
                        if(!frame.external_geometry[draw.geometry_index].index_count)continue;
                        TransformExternalInk(frame.layer_ink[frame.layer_stack.back()],frame.external_bounds[draw.geometry_index],draw.parameter.matrix);
                    }
                    continue;
                }
                if(paint!=frame.paints.end()&&uint32_t(paint->command_index)==n&&paint->reserved==0)
                {
                    // 任意回调的输出不能用作者底图的 AABB 证明。覆盖目标可见域，
                    // 并保留祖先邻域滤镜可能从屏幕外采入的范围；已知 provider 不走此分支。
                    double px=0,py=0;
                    for(const auto& effect:frame.effects)
                    {
                        if(effect.kind!=1&&effect.kind!=2)continue;
                        const auto source=frame.layers[effect.layer_index].source;
                        if(std::find(frame.layer_stack.begin(),frame.layer_stack.end(),source)==frame.layer_stack.end())continue;
                        px+=3*std::max(0.f,effect.sigma)+std::abs(effect.offset_x);
                        py+=3*std::max(0.f,effect.sigma)+std::abs(effect.offset_y);
                    }
                    double left=-px,top=-py,right=frame.width+px,bottom=frame.height+py;
                    if(frame.raw_flags[n]&1)
                    {
                        left=std::max(left,double(command.clip_x));top=std::max(top,double(command.clip_y));
                        right=std::min(right,double(command.clip_x)+command.clip_w);bottom=std::min(bottom,double(command.clip_y)+command.clip_h);
                    }
                    if(right>left&&bottom>top)
                    {
                        auto& ink=frame.layer_ink[frame.layer_stack.back()];ink.Point(left,top);ink.Point(right,bottom);
                    }
                    continue;
                }
                auto geometry=resolve(command.geometry);
                auto local=LocalBounds(geometry);
                const int image=frame.image_slots[n];
                if(image>=0&&frame.preparation[image].prepared)local.Join(frame.preparation[image].ink);
                TransformInk(frame.layer_ink[frame.layer_stack.back()],local,frame.matrices[n].data(),command.tx,command.ty);
            }
            if(stop==UINT32_MAX)
            {
                if(frame.layer_stack.size()!=1)throw std::invalid_argument("unbalanced layer end");
                return frame.layer_ink[0];
            }
            throw std::invalid_argument("missing capture operation");
        }
        bool NormalizedTexture(const Frame& frame,uint64_t id)const
        {
            if(!id)return true;
            for(const auto& resource:frame.resources)if(resource.texture==id)return resource.domain==0&&(resource.flags&1);
            return false;
        }
        static bool AnalyticUnitQuad(const UruiPlanExternalGeometry& geometry,QuadMapping& mapping)
        {
            if(geometry.vertex_count!=4||geometry.index_count!=6)return false;
            std::array<UruiVertex,4> vertices{};std::array<int,6> indices{};
            for(uint32_t n=0;n<4;n++)
            {
                const auto value=ReadExternalVertex(geometry,n);if(value.z!=0)return false;
                auto& vertex=vertices[n];vertex.x=value.x;vertex.y=value.y;vertex.u=value.x;vertex.v=value.y;vertex.r=vertex.g=vertex.b=vertex.a=255;
            }
            for(uint32_t n=0;n<6;n++)indices[n]=int(ExternalIndex(geometry,n));
            return DirectQuad(vertices.data(),4,indices.data(),6,mapping,true)&&mapping.rectangle==std::array<float,4>{0,0,1,1};
        }
        template<class Resolve> void LowerDirectMasks(Frame& frame,Resolve resolve)
        {
            frame.direct_masks.clear();frame.omitted_layers.assign(frame.created_layers.size(),0);
            std::fill(frame.omitted_operations.begin(),frame.omitted_operations.end(),0);
            std::fill(frame.suppressed_commands.begin(),frame.suppressed_commands.end(),0);
            std::fill(frame.direct_mask_indices.begin(),frame.direct_mask_indices.end(),-1);
            for(size_t n=0;n+7<frame.layers.size();n++)
            {
                const auto& a=frame.layers[n];const auto& b=frame.layers[n+1];const auto& c=frame.layers[n+2];const auto& d=frame.layers[n+3];
                const auto& e=frame.layers[n+4];const auto& f=frame.layers[n+5];const auto& g=frame.layers[n+6];const auto& h=frame.layers[n+7];
                if(a.kind!=1||b.kind!=1||c.kind!=3||d.kind!=2||e.kind!=6||f.kind!=4||g.kind!=2||h.kind!=5)continue;
                const auto source=a.target,mask=b.target,saved=c.target;
                if(!source||!mask||!saved||source==mask||source==saved||mask==saved)continue;
                if(c.source!=mask||d.source!=mask||d.target!=source||e.source!=source||e.target!=source||e.mask!=saved||e.blend!=1||
                    f.source!=source||f.mask||f.blend||g.source!=source||g.target!=f.target||h.source!=saved)continue;
                if(!f.target&&!(frame.resource_flags&1))continue;
                const int command=a.command_index,mask_command=command+1,end=command+2;
                if(command<0||size_t(end)>frame.commands.size()||b.command_index!=mask_command||c.command_index!=end||d.command_index!=end||
                    e.command_index!=end||f.command_index!=end||g.command_index!=end||h.command_index!=end)continue;
                if(frame.command_layers[command]!=source||frame.command_layers[mask_command]!=mask)continue;
                auto paint=std::lower_bound(frame.paints.begin(),frame.paints.end(),command,[](const UruiPaintCommand& p,int value){return p.command_index<value;});
                if(paint!=frame.paints.end()&&paint->command_index==command&&paint->reserved!=2)continue;
                paint=std::lower_bound(frame.paints.begin(),frame.paints.end(),mask_command,[](const UruiPaintCommand& p,int value){return p.command_index<value;});
                const UruiPlanExternalDraw* analytic=nullptr;
                if(paint!=frame.paints.end()&&paint->command_index==mask_command)
                {
                    if(!frame.known_providers[mask_command]||frame.external_counts[mask_command]!=1)continue;
                    const auto& draw=frame.external_draws[frame.external_offsets[mask_command]];if(draw.flags!=4)continue;analytic=&draw;
                }
                const auto& content=frame.commands[command];const auto& alpha=frame.commands[mask_command];
                if(!NormalizedTexture(frame,content.texture)||(!analytic&&!NormalizedTexture(frame,alpha.texture)))continue;
                auto content_mesh=resolve(content.geometry);QuadMapping content_mapping,mask_mapping;
                if(!DirectQuad(content_mesh.vertices,content_mesh.vertex_count,content_mesh.indices,content_mesh.index_count,content_mapping,false))continue;
                if(analytic)
                {if(!AnalyticUnitQuad(frame.external_geometry[analytic->geometry_index],mask_mapping))continue;}
                else
                {
                    const auto mask_mesh=resolve(alpha.geometry);
                    if(!DirectQuad(mask_mesh.vertices,mask_mesh.vertex_count,mask_mesh.indices,mask_mesh.index_count,mask_mapping,true))continue;
                }
                const auto parameter=analytic?analytic->parameter:Parameter(alpha,frame.matrices[mask_command],frame.uniforms[mask_command]);
                DirectMask descriptor{};std::array<std::array<float,4>,4> inverse{};
                if(!DirectMaskRasterGrid(parameter.matrix,mask_mapping)||!InverseMaskProjection(parameter.matrix,inverse))continue;
                descriptor.command=uint32_t(command);descriptor.mask_command=uint32_t(mask_command);descriptor.texture=analytic?0:alpha.texture;descriptor.composite=f;
                std::copy(inverse.begin(),inverse.end(),descriptor.values.begin());descriptor.values[4]=mask_mapping.rectangle;
                if(analytic)
                {
                    // XY 投影只读取逆矩阵列0/1/3；列2存放解析羽化距离，无需扩大8texel契约。
                    std::copy_n(analytic->parameter.tint,4,descriptor.values[2].data());
                    descriptor.values[5]={analytic->parameter.uv[0],analytic->parameter.uv[1],0,8};descriptor.values[6]={0,0,0,1};
                }
                else
                {
                    const auto& uniform=frame.uniforms[mask_command];
                    for(int axis=0;axis<2;axis++)
                    {
                        auto& mapping=descriptor.values[5+axis];mapping=axis==0?mask_mapping.u:mask_mapping.v;
                        mapping[0]*=uniform.uv[axis];mapping[1]*=uniform.uv[axis];mapping[2]=mapping[2]*uniform.uv[axis]+uniform.uv[2+axis];
                    }
                    descriptor.values[5][3]=float(((alpha.flags>>1)&3)|(alpha.texture?0:4));
                    descriptor.values[6][3]=mask_mapping.alpha*uniform.tint[3];
                }
                descriptor.values[7]=(alpha.flags&1)?std::array<float,4>{float(alpha.clip_x),float(alpha.clip_y),float(double(alpha.clip_x)+alpha.clip_w),float(double(alpha.clip_y)+alpha.clip_h)}:
                    std::array<float,4>{-1e20f,-1e20f,1e20f,1e20f};
                frame.direct_mask_indices[command]=int32_t(frame.direct_masks.size());frame.direct_masks.push_back(descriptor);
                frame.suppressed_commands[mask_command]=1;
                frame.omitted_layers[source]=frame.omitted_layers[mask]=frame.omitted_layers[saved]=1;
                for(size_t operation=n;operation<n+8;operation++)frame.omitted_operations[operation]=OmittedDirectMask;
                n+=7;
            }
            frame.layer_indices.clear();
            for(uint32_t n=0;n<frame.layers.size();n++)if(!frame.omitted_operations[n])frame.layer_indices.push_back(n);
        }
        static const Frame::BinaryMaskClip* BinaryClip(const Frame& frame,uint32_t index)
        {
            const auto found=std::lower_bound(frame.binary_mask_clips.begin(),frame.binary_mask_clips.end(),index,
                [](const Frame::BinaryMaskClip& clip,uint32_t layer){return clip.layer_index<layer;});
            return found!=frame.binary_mask_clips.end()&&found->layer_index==index?&*found:nullptr;
        }
        void LowerBinaryMaskClips(Frame& frame)
        {
            for(uint32_t n=0;n+6<frame.layers.size();n++)
            {
                bool omitted=false;for(uint32_t k=n;k<n+7;k++)omitted|=frame.omitted_operations[k]!=0;
                if(omitted)continue;
                const auto& b=frame.layers[n];const auto& c=frame.layers[n+1];const auto& d=frame.layers[n+2];
                const auto& e=frame.layers[n+3];const auto& f=frame.layers[n+4];const auto& g=frame.layers[n+5];const auto& h=frame.layers[n+6];
                if(b.kind!=1||c.kind!=3||d.kind!=2||e.kind!=6||f.kind!=4||g.kind!=2||h.kind!=5)continue;
                const auto source=e.source,mask=b.target,saved=c.target;
                if(!source||!mask||!saved||source==mask||source==saved||mask==saved||c.source!=mask||d.source!=mask||d.target!=source||
                    e.target!=source||e.mask!=saved||e.blend!=1||f.source!=source||f.mask||f.blend||g.source!=source||g.target!=f.target||h.source!=saved)continue;
                const auto command=b.command_index,end=c.command_index;
                if(command<0||end!=command+1||size_t(end)>frame.commands.size()||d.command_index!=end||e.command_index!=end||f.command_index!=end||g.command_index!=end||h.command_index!=end)continue;
                // 根输出必须与 UI 像素一一对应；缩放提交会对原遮罩边缘作双线性重采样。
                if(!f.target&&!(frame.resource_flags&1))continue;
                if(frame.command_layers[command]!=mask||!frame.known_providers[command]||frame.external_counts[command]!=1)continue;
                const auto& mask_usage=frame.mask_crop_usage[mask];const auto& saved_usage=frame.mask_crop_usage[saved];
                if(mask_usage.creates!=1||mask_usage.reads!=1||saved_usage.creates!=1||saved_usage.reads!=1)continue;
                const auto& draw=frame.external_draws[frame.external_offsets[command]];
                if(draw.flags!=4)continue;
                std::array<float,4> bounds{};if(!BinaryRectangleClip(draw.parameter,f,bounds))continue;
                frame.binary_mask_clips.push_back({n+4,bounds});frame.suppressed_commands[command]=1;
                frame.omitted_layers[mask]=frame.omitted_layers[saved]=1;
                for(uint32_t offset:{0u,1u,2u,3u,6u})frame.omitted_operations[n+offset]=OmittedBinaryMask;
                n+=6;
            }
            frame.layer_indices.clear();
            for(uint32_t n=0;n<frame.layers.size();n++)if(!frame.omitted_operations[n])frame.layer_indices.push_back(n);
        }
        void PlanMaskCopies(Frame& frame)
        {
            const auto count=frame.created_layers.size();
            frame.last_layer_write.assign(count,0);frame.mask_save_sequence.assign(count,0);
            frame.mask_copy_sources.assign(count,0);frame.mask_copy_counts.assign(count,0);frame.mask_copy_state.assign(count,0);
            uint64_t sequence=0;size_t operation=0;
            for(size_t command=0;command<=frame.commands.size();command++)
            {
                while(operation<frame.layers.size()&&frame.layers[operation].command_index==int(command))
                {
                    const auto& layer=frame.layers[operation++];sequence++;
                    if(layer.kind==1||layer.kind==3||layer.kind==4)frame.last_layer_write[layer.target]=sequence;
                    else if(layer.kind==6)frame.last_layer_write[layer.source]=sequence;
                    if(layer.kind==3)
                    {
                        frame.mask_copy_sources[layer.target]=uint32_t(layer.source);
                        frame.mask_copy_counts[layer.target]++;frame.mask_save_sequence[layer.target]=sequence;
                    }
                }
                if(command<frame.commands.size())frame.last_layer_write[frame.command_layers[command]]=++sequence;
            }
            // A mask snapshot uses the physical source allocation, including frozen
            // custom-effect capture padding. This does not feed padding into parent ink.
            for(uint32_t layer=1;layer<count;layer++)
            {
                frame.layer_stack.clear();uint32_t current=layer;
                while(frame.mask_copy_counts[current]==1&&frame.mask_copy_state[current]==0)
                {
                    frame.mask_copy_state[current]=1;frame.layer_stack.push_back(current);
                    current=frame.mask_copy_sources[current];
                }
                if(frame.mask_copy_state[current]==1)throw std::invalid_argument("cyclic mask copy");
                while(!frame.layer_stack.empty())
                {
                    const auto target=uint32_t(frame.layer_stack.back());frame.layer_stack.pop_back();
                    const auto source=frame.mask_copy_sources[target];
                    frame.layer_ink[target]=frame.layer_ink[source];frame.mask_copy_state[target]=2;
                }
            }
        }
        void ConstrainTerminalMasks(Frame& frame)
        {
            // 只有最终遮罩之外的像素不再被任何消费者读取时，才能收紧共用的物理目标。
            // 先计数整帧读取，避免对每个遮罩重复扫描全部图层。
            auto& usage=frame.mask_crop_usage;usage.assign(frame.created_layers.size(),MaskCropUsage{});
            for(uint32_t n=0;n<frame.layers.size();n++)if(!frame.omitted_operations[n])
            {
                const auto& op=frame.layers[n];
                if(op.kind==1||op.kind==3)usage[op.target].creates++;
                if(op.kind==3||op.kind==4||op.kind==6)usage[op.source].reads++;
                if((op.kind==4||(op.kind==6&&op.blend==1))&&op.mask)
                {
                    if(op.mask>=usage.size())throw std::out_of_range("mask layer");
                    usage[op.mask].reads++;
                }
            }
            for(const auto& paint:frame.paints)if(paint.reserved==0&&!frame.known_providers[paint.command_index])
                usage[frame.command_layers[paint.command_index]].opaque=true;
            for(const auto& slot:frame.preparation)if(!slot.image)usage[slot.request.layer].opaque=true;
            const auto& order=frame.layer_indices;
            for(size_t n=0;n+7<order.size();n++)
            {
                const auto& a=frame.layers[order[n]];const auto& b=frame.layers[order[n+1]];
                const auto& c=frame.layers[order[n+2]];const auto& d=frame.layers[order[n+3]];
                const auto& e=frame.layers[order[n+4]];const auto& f=frame.layers[order[n+5]];
                const auto& g=frame.layers[order[n+6]];const auto& h=frame.layers[order[n+7]];
                if(a.kind!=1||b.kind!=1||c.kind!=3||d.kind!=2||e.kind!=6||f.kind!=4||g.kind!=2||h.kind!=5)continue;
                const auto source=a.target,mask=b.target,saved=c.target;
                if(!source||!mask||!saved||source==mask||source==saved||mask==saved)continue;
                if(c.source!=mask||d.source!=mask||d.target!=source||e.source!=source||e.target!=source||e.mask!=saved||e.blend!=1||
                    f.source!=source||f.mask||f.blend||g.source!=source||g.target!=f.target||h.source!=saved)continue;
                if(a.command_index>b.command_index||b.command_index>=c.command_index||d.command_index!=c.command_index||
                    e.command_index!=c.command_index||f.command_index!=c.command_index||g.command_index!=c.command_index||h.command_index!=c.command_index)continue;
                if(usage[source].creates!=1||usage[source].reads!=2||usage[source].opaque||
                    usage[mask].creates!=1||usage[saved].creates!=1)continue;
                // 遮罩矩形外的像素为零，但最终双线性采样仍可能读到相邻透明纹素。
                // 每边保留一个纹素，避免缩放输出时被 Clamp 扩大硬边或截断半像素覆盖。
                // 邻域滤镜、提前保存/读取和任意捕获不满足上述证明。
                float rectangle[4];frame.layer_ink[saved].Capture(rectangle);
                rectangle[0]-=1;rectangle[1]-=1;rectangle[2]+=2;rectangle[3]+=2;
                auto& ink=frame.layer_ink[source];
                if(ink.valid)
                {
                    ink.x0=std::max(ink.x0,double(rectangle[0]));ink.y0=std::max(ink.y0,double(rectangle[1]));
                    ink.x1=std::min(ink.x1,double(rectangle[0])+rectangle[2]);ink.y1=std::min(ink.y1,double(rectangle[1])+rectangle[3]);
                    if(ink.x1<=ink.x0||ink.y1<=ink.y0)ink={};
                }
            }
        }
        void BuildLayerDependencies(Frame& frame)
        {
            if(frame.layers.empty())return;
            const auto count=frame.created_layers.size();dependency_cache.Begin(count);
            while(frame.dependencies.size()<count)frame.dependencies.emplace_back(&frame_budget);
            for(size_t layer=0;layer<count;layer++)
            {
                auto& state=frame.dependencies[layer];state.Reset();
                state.Add(frame.epoch);state.Add(frame.resource_flags);state.Add(frame.width);state.Add(frame.height);
                if(layer){float bounds[4];frame.layer_ink[layer].Capture(bounds);state.Bytes(bounds,sizeof(bounds));}
            }
            // A final source cannot replace an earlier snapshot if that layer
            // is written again later. Keep only that dependency chain live.
            uint64_t sequence=0;size_t flow=0;
            for(uint32_t command=0;command<=frame.commands.size();command++)
            {
                while(flow<frame.layers.size()&&frame.layers[flow].command_index==int(command))
                {
                    const auto& op=frame.layers[flow++];sequence++;
                    auto forbid=[&](uint64_t layer){if(layer&&frame.last_layer_write[layer]>sequence)frame.dependencies[layer].dynamic=true;};
                    if(op.kind==3||(op.kind==4&&op.source!=op.target))forbid(op.source);
                    if(op.kind==6&&op.blend==1)forbid(op.mask);
                }
                if(command<frame.commands.size())sequence++;
            }
            size_t operation=0,paint=0;
            auto version=[&](uint64_t id){return dependency_cache.Version(frame.dependencies[id]);};
            auto descriptor=[](LayerDependencyState& state,const UruiLayerCommand& op)
            {
                state.Add(uint64_t(op.kind));state.Add(uint64_t(op.blend));state.Add(uint64_t(op.flags));
                state.Add(uint64_t(op.clip_x));state.Add(uint64_t(op.clip_y));state.Add(uint64_t(op.clip_w));state.Add(uint64_t(op.clip_h));
            };
            for(uint32_t command=0;command<=frame.commands.size();command++)
            {
                while(operation<frame.layers.size()&&frame.layers[operation].command_index==int(command))
                {
                    const auto index=operation++;if(frame.omitted_operations[index]==OmittedBinaryMask)continue;const auto& op=frame.layers[index];
                    auto& source=frame.dependencies[op.source];auto& target=frame.dependencies[op.target];
                    if(op.kind==3)
                    {target.words=source.words;target.version=source.version;target.dynamic|=source.dynamic;}
                    else if(op.kind==4&&op.target)
                    {
                        if(!op.source)target.dynamic=true;
                        else {const auto child=version(op.source);target.Add(child);target.dynamic|=source.dynamic;}
                        if(const auto* clip=BinaryClip(frame,uint32_t(index)))
                        {
                            auto effective=op;effective.flags|=1;
                            effective.clip_x=int(clip->bounds[0]);effective.clip_y=int(clip->bounds[1]);
                            effective.clip_w=int(clip->bounds[2]-clip->bounds[0]);effective.clip_h=int(clip->bounds[3]-clip->bounds[1]);
                            descriptor(target,effective);
                        }
                        else descriptor(target,op);
                        if(op.mask){const auto mask=version(op.mask);target.Add(mask);target.dynamic|=frame.dependencies[op.mask].dynamic;}
                    }
                    else if(op.kind==6)
                    {
                        descriptor(source,op);
                        if(op.blend==1)
                        {source.Add(version(op.mask));source.dynamic|=frame.dependencies[op.mask].dynamic;}
                        else
                        {
                            source.Add(op.mask);
                            const auto effect=frame.effect_indices[index];
                            if(effect<0||frame.effects[effect].kind==3||
                                (frame.effects[effect].kind==0&&!frame.effects[effect].cacheable))source.dynamic=true;
                            if(effect>=0)
                            {
                                const auto& value=frame.effects[effect];source.Add(value.kind);
                                source.Bytes(&value.sigma,sizeof(float)*4);
                            }
                        }
                    }
                }
                if(command==frame.commands.size())break;
                const auto layer=frame.command_layers[command];if(!layer)continue;
                auto& state=frame.dependencies[layer];const auto& draw=frame.commands[command];
                if(frame.known_providers[command])
                {
                    state.Add(0x455854);
                    for(uint32_t item=0;item<frame.external_counts[command];item++)
                    {
                        const auto& external=frame.external_draws[frame.external_offsets[command]+item];const auto& geometry=frame.external_geometry[external.geometry_index];
                        state.Add(geometry.geometry);state.Add(geometry.version);state.Add(external.program);state.Add(external.flags);state.dynamic|=(external.flags&3)!=0;state.Bytes(&external.parameter,sizeof(external.parameter));
                    }
                    continue;
                }
                state.Add(0x44524157);state.Add(draw.geometry);state.Add(draw.texture);
                const auto parameter=Parameter(draw,frame.matrices[command],frame.uniforms[command]);state.Bytes(&parameter,sizeof(parameter));
                if(draw.texture)
                {
                    auto resource=std::find_if(frame.resources.begin(),frame.resources.end(),[&](const auto& value){return value.texture==draw.texture;});
                    if(resource==frame.resources.end())state.dynamic=true;
                    else {state.Add(resource->version);state.Add(resource->domain);state.Add(resource->flags);state.dynamic|=resource->domain!=0;}
                }
                while(paint<frame.paints.size()&&uint32_t(frame.paints[paint].command_index)<command)paint++;
                if(paint<frame.paints.size()&&uint32_t(frame.paints[paint].command_index)==command)
                {
                    const auto& program=frame.paints[paint];state.Add(uint64_t(program.reserved));
                    if(program.reserved!=2)state.dynamic=true;
                    else for(uint32_t n=0;n<frame.color_counts[command];n++)
                    {const auto& field=frame.color_filters[frame.color_offsets[command]+n];state.Add(field.kind);state.Bytes(&field.value,sizeof(float));}
                }
            }
        }
        template<class Resolve> void CompleteRegions(Frame& frame,Resolve resolve)
        {
            frame.layer_regions.clear();
            if(frame.layers.empty()){frame.ready=true;return;}
            Capture(frame,UINT32_MAX,resolve);
            LowerDirectMasks(frame,resolve);
            // Ink propagation above is complete. Allocation padding must stay local
            // to its layer, so union captures only now and never inflate parent ink.
            for(const auto& slot:frame.preparation)if(!slot.image)
            {frame.layer_ink[slot.request.layer].Rect(slot.request.capture);frame.created_layers[slot.request.layer]|=8;}
            for(const auto& operation:frame.layers)if(operation.kind==3)frame.created_layers[operation.source]|=2;
            for(const auto& effect:frame.effects)if(effect.kind==1||effect.kind==2)frame.created_layers[frame.layers[effect.layer_index].source]|=4;
            PlanMaskCopies(frame);ConstrainTerminalMasks(frame);LowerBinaryMaskClips(frame);BuildLayerDependencies(frame);
            for(size_t layer=1;layer<frame.created_layers.size();layer++)if(frame.created_layers[layer]&&!frame.omitted_layers[layer])
            {
                UruiPlanLayerRegion result{};result.layer=layer;result.flags=frame.created_layers[layer]>>1;frame.layer_ink[layer].Capture(result.bounds);
                const auto source=frame.mask_copy_sources[layer];const auto saved=frame.mask_save_sequence[layer];
                if(frame.mask_copy_counts[layer]==1&&source!=layer&&frame.last_layer_write[source]<saved&&frame.last_layer_write[layer]==saved)
                {result.flags|=8;result.reserved=source;}
                result.version=dependency_cache.Version(frame.dependencies[layer]);
                if(!frame.dependencies[layer].dynamic)result.flags|=16;
                frame.layer_regions.push_back(result);
            }
            frame.ready=true;
        }
        template<class T> void ResizeSource(Buffer<T>& data,size_t size)
        {
            if(size>data.capacity())
            {
                if(size>geometry_budget.limit/sizeof(T))throw std::bad_alloc();
                const auto bytes=size*sizeof(T);
                while(bytes>geometry_budget.limit-geometry_budget.retained)
                {
                    size_t victim=SIZE_MAX;
                    for(size_t n=0;n<batches.size();n++)
                    {
                        const auto& batch=batches[n];
                        if(InUse(batch.id)||(!batch.keys.capacity()&&!batch.vertices.capacity()&&!batch.indices.capacity()))continue;
                        if(victim==SIZE_MAX||batch.last_use<batches[victim].last_use)victim=n;
                    }
                    if(victim==SIZE_MAX)throw std::bad_alloc();
                    Drop(batches[victim]);
                }
                // 明确扩容大小，避免 vector 的隐式倍增超出已核对的预算。
                data.reserve(size);
            }
            data.resize(size);
        }
        void ComposeSource(SourceSnapshot& snapshot)
        {
            if(snapshot.composed_version==snapshot.version)return;
            ResizeSource(snapshot.vertices,snapshot.base_vertices.size());const auto& uniform=snapshot.uniform;
            for(size_t n=0;n<snapshot.base_vertices.size();n++)
            {
                const auto& v=snapshot.base_vertices[n];
                auto color=[](uint8_t value,float tint){return uint8_t(std::clamp(std::lround(value*tint),0l,255l));};
                snapshot.vertices[n]={v.x,v.y,0,color(v.r,uniform.tint[0]),color(v.g,uniform.tint[1]),color(v.b,uniform.tint[2]),color(v.a,uniform.tint[3]),
                    v.u*uniform.uv[0]+uniform.uv[2],v.v*uniform.uv[1]+uniform.uv[3],0,0};
            }
            snapshot.composed_version=snapshot.version;
        }
        template<class Resolve> void PrepareSource(Frame& frame,PrepareSlot& slot,Resolve resolve)
        {
            while(frame.sources.size()<=slot.source_index)frame.sources.emplace_back(&geometry_budget);
            auto& snapshot=frame.sources[slot.source_index];const auto geometry=frame.commands[slot.request.slot].geometry;
            const auto& uniform=frame.uniforms[slot.request.slot];
            for(int n=0;n<4;n++)if(!std::isfinite(uniform.tint[n])||!std::isfinite(uniform.uv[n]))throw std::invalid_argument("source uniform");
            if(snapshot.geometry!=geometry||snapshot.epoch!=frame.epoch)
            {
                const auto source=resolve(geometry);
                if(!source.vertices||!source.indices)throw std::out_of_range("source geometry expired");
                snapshot.version=0;snapshot.composed_version=0;
                ResizeSource(snapshot.base_vertices,source.vertex_count);ResizeSource(snapshot.indices,source.index_count);
                std::copy_n(source.vertices,source.vertex_count,snapshot.base_vertices.data());
                for(size_t n=0;n<source.index_count;n++)
                {
                    if(source.indices[n]<0||size_t(source.indices[n])>=source.vertex_count)throw std::invalid_argument("source geometry index");
                    snapshot.indices[n]=uint32_t(source.indices[n]);
                }
                snapshot.geometry=geometry;snapshot.epoch=frame.epoch;
            }
            if(!snapshot.version||std::memcmp(&snapshot.uniform,&uniform,sizeof(uniform)))
            {
                const UruiPlanUniform identity{{1,1,1,1},{1,1,0,0}};
                if(std::memcmp(&identity,&uniform,sizeof(uniform))==0)snapshot.version=geometry;
                else
                {
                    const auto next=Identity();if(next>=(uint64_t{1}<<63))throw std::overflow_error("source geometry identity exhausted");
                    snapshot.version=next|(uint64_t{1}<<63);
                }
                snapshot.uniform=uniform;snapshot.composed_version=0;
            }
            slot.request.geometry=snapshot.version;slot.source_ready=true;
        }
        uint32_t Domain(uint64_t texture,const UruiPlanBindings& bindings)const
        {
            for(uint32_t i=0;i<bindings.texture_count;i++)if(bindings.textures[i].texture==texture)return bindings.textures[i].domain;
            return 0;
        }
        bool InUse(uint64_t id)const
        {
            if(!id)return false;
            for(const auto& frame:frames)if((&frame==compiling)||(frame.token&&!frame.input))
                for(const auto& operation:frame.operations)if(operation.geometry==id)return true;
            return false;
        }
        void CollectRetiredEpochs()
        {
            if(!retired_epoch_batches)return;
            retired_epoch_batches=false;
            for(auto& batch:batches)if(batch.id&&batch.epoch!=epoch)
            {
                if(InUse(batch.id)){retired_epoch_batches=true;continue;}
                // 旧文档世代不会再次命中；仅撤销身份，空闲容量由现有预算和空槽路径复用。
                // 真实 GPU 租约仍持有的版本不进入此分支。
                batch.id=0;batch.last_use=0;
            }
        }
        size_t Reclaimable(size_t except=SIZE_MAX)const
        {
            size_t best=SIZE_MAX;
            for(size_t n=0;n<batches.size();n++)if(n!=except&&!InUse(batches[n].id)&&
                (best==SIZE_MAX||batches[n].last_use<batches[best].last_use))best=n;
            return best;
        }
        void Drop(Batch& batch)
        {
            batch.id=0;batch.last_use=0;
            Buffer<GeometryKey>(Allocator<GeometryKey>(&geometry_budget)).swap(batch.keys);
            Buffer<UruiPlanVertex>(Allocator<UruiPlanVertex>(&geometry_budget)).swap(batch.vertices);
            Buffer<uint32_t>(Allocator<uint32_t>(&geometry_budget)).swap(batch.indices);
        }
        static void ApplyLayoutOrigins(const Batch& batch,const Frame& source,Frame& target)
        {
            for(const auto& key:batch.keys)if(key.texture&0x80000000u)
            {
                const auto index=key.parameter;auto& parameter=target.parameters[index];const auto& matrix=source.matrices[index];
                const double dx=double(source.commands[index].tx)-key.origin_x,dy=double(source.commands[index].ty)-key.origin_y;
                for(int row=0;row<4;row++)
                {
                    const double value=matrix[12+row]+double(matrix[row])*dx+double(matrix[4+row])*dy;
                    if(!std::isfinite(value)||std::abs(value)>std::numeric_limits<float>::max())throw std::invalid_argument("layout delta matrix");
                    parameter.matrix[12+row]=float(value);
                }
            }
        }
        template<class Resolve> Batch& Geometry(uint64_t e,Resolve resolve,const Frame* source_frame=nullptr,Frame* target_frame=nullptr)
        {
            const auto hint=geometry_hint_cursor++;
            if(hint==geometry_hints.size())geometry_hints.push_back(SIZE_MAX);
            auto reuse=[&](size_t index) -> Batch& {
                auto& batch=batches[index];geometry_hints[hint]=index;batch.last_use=Identity();
                if(source_frame&&target_frame)ApplyLayoutOrigins(batch,*source_frame,*target_frame);
                return batch;
            };
            // 帧内调用顺序通常稳定；提示只避免重复搜索，不作为身份或内容的依据。
            const auto previous=geometry_hints[hint];
            if(previous<batches.size())
            {
                const auto& batch=batches[previous];geometry_searches++;
                if(batch.id&&batch.epoch==e&&batch.keys==keys)return reuse(previous);
            }
            for(size_t index=0;index<batches.size();index++)
            {
                const auto& batch=batches[index];
                geometry_searches++;
                if(batch.id&&batch.epoch==e&&batch.keys==keys)return reuse(index);
            }
            size_t nv=0,ni=0;
            for(const auto& key:keys)
            {
                const auto source=resolve(key.geometry,key.version);
                if((!source.external&&(!source.vertices||!source.indices))||source.vertex_count>geometry_budget.limit/sizeof(UruiPlanVertex)||source.index_count>geometry_budget.limit/sizeof(uint32_t))throw std::invalid_argument("geometry expired");
                nv+=source.vertex_count;ni+=source.index_count;
            }
            size_t selected=SIZE_MAX;
            for(size_t n=0;n<batches.size();n++)if(!batches[n].id){selected=n;break;}
            // Versioned source payloads may change every frame without changing
            // capacity. Recycle only unleased storage for the same source slots.
            if(selected==SIZE_MAX)for(size_t n=0;n<batches.size();n++)
            {
                const auto& candidate=batches[n];if(candidate.epoch!=e||candidate.keys.size()!=keys.size()||InUse(candidate.id))continue;
                bool same=true,changed=false;
                for(size_t key=0;key<keys.size();key++)
                {
                    const auto& old=candidate.keys[key];const auto& next=keys[key];
                    if(old.geometry!=next.geometry||old.parameter!=next.parameter||old.texture!=next.texture){same=false;break;}
                    changed|=old.version!=next.version;
                }
                if(same&&changed&&(selected==SIZE_MAX||candidate.last_use<batches[selected].last_use))selected=n;
            }
            const size_t payload=keys.size()*sizeof(GeometryKey)+nv*sizeof(UruiPlanVertex)+ni*sizeof(uint32_t);
            if(selected==SIZE_MAX)
            {
                const size_t capacity=batches.size()==batches.capacity()?std::max(size_t(1),batches.capacity()*2):batches.capacity();
                const size_t metadata=batches.size()==batches.capacity()?capacity*sizeof(Batch):0;
                if(payload+metadata<=geometry_budget.limit-geometry_budget.retained)
                {
                    if(capacity>batches.capacity())batches.reserve(capacity);
                    selected=batches.size();batches.emplace_back(&geometry_budget);
                }
                else selected=Reclaimable();
            }
            if(selected==SIZE_MAX)throw std::bad_alloc();
            auto& batch=batches[selected];
            batch.id=0;
            // Recycle buffers without shrinking when capacity suffices. Only actual
            // budget pressure retires unpublished versions; leased/in-build IDs are pinned.
            if(batch.keys.capacity()<keys.size())Buffer<GeometryKey>(Allocator<GeometryKey>(&geometry_budget)).swap(batch.keys);
            if(batch.vertices.capacity()<nv)Buffer<UruiPlanVertex>(Allocator<UruiPlanVertex>(&geometry_budget)).swap(batch.vertices);
            if(batch.indices.capacity()<ni)Buffer<uint32_t>(Allocator<uint32_t>(&geometry_budget)).swap(batch.indices);
            auto required=[&](){return (batch.keys.capacity()<keys.size()?keys.size()*sizeof(GeometryKey):0)+
                (batch.vertices.capacity()<nv?nv*sizeof(UruiPlanVertex):0)+(batch.indices.capacity()<ni?ni*sizeof(uint32_t):0);};
            while(required()>geometry_budget.limit-geometry_budget.retained)
            {
                auto victim=Reclaimable(selected);if(victim==SIZE_MAX)throw std::bad_alloc();
                // Empty entries cannot release further storage.
                if(!batches[victim].keys.capacity()&&!batches[victim].vertices.capacity()&&!batches[victim].indices.capacity())
                {
                    size_t next=SIZE_MAX;
                    for(size_t n=0;n<batches.size();n++)if(n!=selected&&!InUse(batches[n].id)&&
                        (batches[n].keys.capacity()||batches[n].vertices.capacity()||batches[n].indices.capacity())){next=n;break;}
                    if(next==SIZE_MAX)throw std::bad_alloc();victim=next;
                }
                Drop(batches[victim]);
            }
            try
            {
                batch.keys=keys;batch.vertices.resize(nv);batch.indices.resize(ni);
                size_t v=0,k=0;
                for(auto& key:batch.keys)
                {
                    const auto source=resolve(key.geometry,key.version);
                    if((key.texture&0x80000000u)&&!source.external)
                    {
                        // 只有能无损还原局部坐标的布局原点才进入不可变顶点。
                        for(size_t n=0;n<source.vertex_count;n++)
                        {
                            const auto& p=source.vertices[n];
                            const float x=p.x+key.origin_x,y=p.y+key.origin_y;
                            if(!std::isfinite(x)||x-key.origin_x!=p.x)key.origin_x=0;
                            if(!std::isfinite(y)||y-key.origin_y!=p.y)key.origin_y=0;
                        }
                    }
                    for(size_t n=0;n<source.vertex_count;n++)
                    {
                        if(source.external)
                        {
                            auto value=ReadExternalVertex(*source.external,uint32_t(n));value.texture=float(key.texture&7);value.parameter=float(key.parameter);batch.vertices[v+n]=value;
                        }
                        else
                        {
                            const auto& p=source.vertices[n];
                            const bool layout=(key.texture&0x80000000u)!=0;
                            const float x=p.x+(layout?key.origin_x:0),y=p.y+(layout?key.origin_y:0);
                            if(!std::isfinite(x)||!std::isfinite(y))throw std::invalid_argument("layout vertex");
                            batch.vertices[v+n]={x,y,0,p.r,p.g,p.b,p.a,p.u,p.v,float(key.texture&7),float(key.parameter)};
                        }
                    }
                    for(size_t n=0;n<source.index_count;n++)
                    {
                        const auto index=source.external?ExternalIndex(*source.external,uint32_t(n)):uint32_t(source.indices[n]);
                        if(index>=source.vertex_count||v>UINT32_MAX-index)throw std::invalid_argument("geometry index");
                        batch.indices[k+n]=uint32_t(v)+index;
                    }
                    v+=source.vertex_count;k+=source.index_count;
                }
                const auto id=Identity();if(id&(uint64_t{1}<<63))throw std::overflow_error("canonical geometry namespace exhausted");
                batch.id=id;batch.epoch=e;batch.last_use=batch.id;
                geometry_hints[hint]=selected;
                if(source_frame&&target_frame)ApplyLayoutOrigins(batch,*source_frame,*target_frame);
                return batch;
            }
            catch(...){batch.id=0;throw;}
        }
        static UruiPlanParameter Parameter(const UruiCommand& command,const std::array<float,16>& matrix,const UruiPlanUniform& uniform)
        {
            UruiPlanParameter p{};
            if(!std::isfinite(command.tx)||!std::isfinite(command.ty))throw std::invalid_argument("translation");
            for(int n=0;n<16;n++){if(!std::isfinite(matrix[n]))throw std::invalid_argument("matrix");p.matrix[n]=matrix[n];}
            for(int r=0;r<4;r++)p.matrix[12+r]=matrix[12+r]+matrix[r]*command.tx+matrix[4+r]*command.ty;
            for(float value:p.matrix)if(!std::isfinite(value))throw std::invalid_argument("world matrix");
            p.clip[0]=float(command.clip_x);p.clip[1]=float(command.clip_y);
            p.clip[2]=float(double(command.clip_x)+command.clip_w);p.clip[3]=float(double(command.clip_y)+command.clip_h);
            for(int n=0;n<4;n++)
            {
                if(!std::isfinite(uniform.tint[n])||!std::isfinite(uniform.uv[n]))throw std::invalid_argument("image uniform");
                p.tint[n]=uniform.tint[n];p.uv[n]=uniform.uv[n];
            }
            p.metadata[0]=float(command.flags);
            return p;
        }
    public:
        explicit Session(size_t geometry_limit=64u*1024*1024,size_t frame_limit=48u*1024*1024):
            geometry_budget{&counters,0,geometry_limit},frame_budget{&counters,0,frame_limit},dependency_cache(&frame_budget),
            frames{Frame(&frame_budget),Frame(&frame_budget),Frame(&frame_budget),Frame(&frame_budget)},
            batches(Allocator<Batch>(&geometry_budget)),keys(Allocator<GeometryKey>(&frame_budget)),geometry_hints(Allocator<size_t>(&frame_budget)),
            paint_scratch(Allocator<UruiPaintCommand>(&frame_budget)),effect_scratch(Allocator<LayerEffect>(&frame_budget)),color_scratch(Allocator<ColorFilterInput>(&frame_budget)),
            ordinary_commands(Allocator<UruiCommand>(&frame_budget)),ordinary_matrices(Allocator<std::array<float,16>>(&frame_budget)),ordinary_uniforms(Allocator<UruiPlanUniform>(&frame_budget)),
            ordinary_resources(Allocator<UruiPlanTextureBinding>(&frame_budget)),ordinary_source_resources(Allocator<UruiPlanTextureBinding>(&frame_budget)),
            previous_operations(Allocator<UruiPlanOperation>(&frame_budget)),previous_parameters(Allocator<UruiPlanParameter>(&frame_budget)),previous_extra_parameters(Allocator<std::array<float,4>>(&frame_budget)),previous_parameter_pages(Allocator<uint64_t>(&frame_budget)),
            sort_memo(Allocator<SortMemoKey>(&frame_budget)),sort_memo_order(Allocator<uint32_t>(&frame_budget)),
            sort_nodes(Allocator<SortNode>(&frame_budget)),sort_states(Allocator<SortState>(&frame_budget)),sort_cells(Allocator<Cell>(&frame_budget)),
            sort_links(Allocator<Link>(&frame_budget)),pending(Allocator<uint32_t>(&frame_budget)),order(Allocator<uint32_t>(&frame_budget)),large(Allocator<uint32_t>(&frame_budget)){}
        UruiPaintCommand* PaintScratch(size_t count){paint_scratch.resize(count);return paint_scratch.data();}
        LayerEffect* EffectScratch(size_t count){effect_scratch.resize(count);return effect_scratch.data();}
        int SetImageDocument(uint64_t input,uint32_t command,uint64_t document)
        {
            if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch)return -2;
            if(frame->resolving||!document)return -1;
            for(auto& slot:frame->preparation)if(slot.image&&slot.request.slot==command){slot.request.document=document;return 1;}
            return -2;
        }
        AllocationCounters Allocations()const{return counters;}
        uint64_t SortBuilds()const{return sort_builds;}
        uint64_t OrdinaryReuses()const{return ordinary_reuses;}
        uint64_t GeometrySearches()const{return geometry_searches;}
        bool Thread()const{return owner==std::this_thread::get_id();}
        void Viewport(uint32_t w,uint32_t h){width=w;height=h;}
        void Invalidate(uint64_t value)
        {
            if(epoch==value)return;
            epoch=value;retired_epoch_batches=true;CollectRetiredEpochs();
        }
        int Begin(uint64_t value,const UruiCommand* commands,const std::array<float,16>* matrices,uint32_t count,
                  const UruiPaintCommand* paints,uint32_t paint_count,const UruiLayerCommand* layers,uint32_t layer_count,UruiPlanInputPacket& out,const UruiPlanUniform* uniforms=nullptr)
        {
            out={};if(!Thread())return -3;
            if(!value||count>65536||(!commands&&count)||(!matrices&&count)||(!paints&&paint_count)||(!layers&&layer_count))return -1;
            auto* frame=Free();if(!frame)return -5;
            struct UnpublishedInput
            {
                Frame* frame;Budget* budget;bool published=false;
                ~UnpublishedInput(){if(!published)*frame=Frame(budget);}
            } build{frame,&frame_budget};
            try
            {
                // Avoid pointer arithmetic on null even for an empty input.
                frame->commands.resize(count);frame->matrices.resize(count);frame->paints.resize(paint_count);frame->layers.resize(layer_count);
                frame->uniforms.resize(count);
                if(uniforms&&count)std::copy_n(uniforms,count,frame->uniforms.data());
                else std::fill(frame->uniforms.begin(),frame->uniforms.end(),UruiPlanUniform{{1,1,1,1},{1,1,0,0}});
                if(count){std::copy_n(commands,count,frame->commands.data());std::copy_n(matrices,count,frame->matrices.data());}
                frame->raw_flags.resize(count);for(uint32_t n=0;n<count;n++)frame->raw_flags[n]=commands[n].flags;
                if(paint_count)std::copy_n(paints,paint_count,frame->paints.data());
                if(layer_count)std::copy_n(layers,layer_count,frame->layers.data());
                int previous_paint=-1;
                for(const auto& p:frame->paints)
                {
                    if(p.command_index<=previous_paint||uint32_t(p.command_index)>=count||p.reserved<0||p.reserved>2)return -1;
                    previous_paint=p.command_index;
                }
                int previous=0;
                for(const auto& l:frame->layers){if(l.command_index<previous||uint32_t(l.command_index)>count)return -1;previous=l.command_index;}
                frame->command_layers.resize(count);
                size_t layer=0;uint64_t current_layer=0;
                for(uint32_t command=0;command<count;command++)
                {
                    while(layer<layer_count&&frame->layers[layer].command_index==int(command))
                    {
                        const auto& operation=frame->layers[layer++];
                        if(operation.kind==1||operation.kind==2)current_layer=operation.target;
                    }
                    frame->command_layers[command]=current_layer;
                }
                frame->effects.clear();frame->preparation.clear();frame->coverage.clear();frame->layer_regions.clear();frame->pending=UINT32_MAX;frame->resolving=false;frame->ready=false;
                frame->color_filters.clear();frame->color_offsets.assign(count,-1);frame->color_counts.assign(count,0);
                frame->resources.clear();frame->resource_flags=0;
                frame->external_geometry.clear();frame->external_lookup.clear();frame->external_draws.clear();frame->external_bounds.clear();frame->external_pending.clear();
                frame->known_providers.assign(count,0);frame->external_offsets.assign(count,-1);frame->external_counts.assign(count,0);frame->direct_masks.clear();frame->binary_mask_clips.clear();frame->layer_indices.clear();
                frame->direct_mask_indices.assign(count,-1);frame->suppressed_commands.assign(count,0);frame->omitted_operations.assign(layer_count,0);
                frame->image_cursor=frame->layout_cursor=frame->element_cursor=0;
                frame->capture_command=frame->capture_layer=0;frame->capture_started=false;
                frame->clocks.clear();frame->clock_lookup.clear();frame->image_programs.assign(count,{});frame->captured_programs.assign(layer_count,{});frame->captured_parameters.assign(layer_count,UINT32_MAX);frame->image_parameters.clear();frame->image_pending.clear();
                frame->image_slots.assign(count,-1);frame->effect_indices.assign(layer_count,-1);frame->effect_slots.assign(layer_count,-1);
                uint32_t source_index=0;
                for(const auto& paint:frame->paints)if(paint.reserved==1)
                {
                    PrepareSlot slot{};slot.image=true;slot.source_index=source_index++;
                    slot.request.element=paint.element;slot.request.program=paint.shader;slot.request.geometry=commands[paint.command_index].geometry;
                    slot.request.slot=uint32_t(paint.command_index);slot.request.kind=URUI_PLAN_PREPARE_IMAGE;
                    frame->image_slots[paint.command_index]=int32_t(frame->preparation.size());frame->preparation.push_back(slot);
                }
                Invalidate(value);frame->input=true;frame->compiled=false;frame->epoch=value;frame->token=Identity();frame->width=width;frame->height=height;
                out={frame->token,value,frame->commands.data(),reinterpret_cast<const float*>(frame->matrices.data()),frame->paints.data(),frame->layers.data(),count,paint_count,layer_count,URUI_RENDER_PLAN_PROTOCOL,frame->uniforms.data(),frame->command_layers.data()};
                build.published=true;return 1;
            }
            catch(const std::bad_alloc&){return -4;}catch(...){return -8;}
        }
        ColorFilterInput* ColorScratch(size_t count){color_scratch.resize(count);return color_scratch.data();}
        int SetColorFilters(uint64_t input,const ColorFilterInput* values,uint32_t count)
        {
            if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch)return -2;
            if(frame->resolving||(!values&&count)||count>65536u*16)return -1;
            try
            {
                uint32_t paint=0,consecutive=0;
                for(uint32_t n=0;n<count;n++)
                {
                    const auto& value=values[n];
                    if(value.command>=frame->commands.size()||value.kind>4||!std::isfinite(value.value)||(n&&value.command<values[n-1].command))return -1;
                    consecutive=n&&value.command==values[n-1].command?consecutive+1:1;if(consecutive>16)return -1;
                    while(paint<frame->paints.size()&&uint32_t(frame->paints[paint].command_index)<value.command)paint++;
                    if(paint==frame->paints.size()||uint32_t(frame->paints[paint].command_index)!=value.command||frame->paints[paint].reserved!=2)return -1;
                }
                frame->color_filters.resize(count);if(count)std::copy_n(values,count,frame->color_filters.data());
                std::fill(frame->color_offsets.begin(),frame->color_offsets.end(),-1);std::fill(frame->color_counts.begin(),frame->color_counts.end(),0);
                for(uint32_t n=0;n<count;n++)
                {const auto command=values[n].command;if(frame->color_offsets[command]<0)frame->color_offsets[command]=int32_t(n);frame->color_counts[command]++;}
                return 1;
            }
            catch(const std::bad_alloc&){return -4;}catch(...){return -8;}
        }
        int SetLayerEffects(uint64_t input,const LayerEffect* effects,uint32_t count)
        {
            if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch)return -2;
            if(frame->resolving||(!effects&&count)||count>65536)return -1;
            try
            {
                for(uint32_t n=0;n<count;n++)
                {
                    const auto& effect=effects[n];
                    if(effect.layer_index>=frame->layers.size()||frame->layers[effect.layer_index].kind!=6||effect.kind>3)return -1;
                    if(n&&effect.layer_index<=effects[n-1].layer_index)return -1;
                }
                frame->effects.resize(count);if(count)std::copy_n(effects,count,frame->effects.data());
                // Reconfiguration before resolve replaces custom slots rather than duplicating them.
                while(!frame->preparation.empty()&&!frame->preparation.back().image)frame->preparation.pop_back();
                std::fill(frame->effect_indices.begin(),frame->effect_indices.end(),-1);std::fill(frame->effect_slots.begin(),frame->effect_slots.end(),-1);
                for(uint32_t index=0;index<frame->effects.size();index++)
                {
                    const auto& effect=frame->effects[index];frame->effect_indices[effect.layer_index]=int32_t(index);
                    if(effect.kind!=3)continue;
                    PrepareSlot slot{};slot.layer_index=effect.layer_index;
                    slot.request.document=effect.document;slot.request.element=effect.element;slot.request.program=effect.program;
                    slot.request.slot=effect.layer_index;slot.request.layer=frame->layers[effect.layer_index].source;
                    slot.request.kind=URUI_PLAN_ELEMENT_LAYOUT;frame->effect_slots[effect.layer_index]=int32_t(frame->preparation.size());frame->preparation.push_back(slot);
                }
                uint64_t maximum=0;
                for(const auto& operation:frame->layers)maximum=std::max(maximum,std::max(operation.source,operation.target));
                if(maximum>65536)return -1;
                frame->layer_composites.assign(size_t(maximum)+1,-1);frame->halo_layers.assign(size_t(maximum)+1,0);
                for(uint32_t n=0;n<frame->layers.size();n++)
                    if(frame->layers[n].kind==4)frame->layer_composites[frame->layers[n].source]=int32_t(n);
                for(const auto& effect:frame->effects)if(effect.kind==1||effect.kind==2)frame->halo_layers[frame->layers[effect.layer_index].source]=1;
                for(uint32_t n=0;n<frame->commands.size();n++)
                {
                    auto& command=frame->commands[n];command.flags=frame->raw_flags[n];
                    const auto layer=frame->command_layers[n];
                    if(layer>=frame->halo_layers.size()||!frame->halo_layers[layer]||frame->layer_composites[layer]<0)continue;
                    const auto& output=frame->layers[frame->layer_composites[layer]];
                    if((command.flags&1)==output.flags&&command.clip_x==output.clip_x&&command.clip_y==output.clip_y&&command.clip_w==output.clip_w&&command.clip_h==output.clip_h)
                        command.flags&=~1;
                }
                return 1;
            }
            catch(const std::bad_alloc&){return -4;}catch(...){return -8;}
        }
        static size_t ExternalSlot(uint64_t geometry,uint64_t version,size_t mask)
        {
            uint64_t hash=geometry^(version+0x9e3779b97f4a7c15ull+(geometry<<6)+(geometry>>2));
            hash^=hash>>30;hash*=0xbf58476d1ce4e5b9ull;hash^=hash>>27;hash*=0x94d049bb133111ebull;hash^=hash>>31;
            return size_t(hash)&mask;
        }
        uint32_t TryFindExternal(const Frame& frame,uint64_t id,uint64_t version)const
        {
            if(frame.external_lookup.empty())return UINT32_MAX;
            const auto mask=frame.external_lookup.size()-1;size_t slot=ExternalSlot(id,version,mask);
            for(size_t probe=0;probe<frame.external_lookup.size();probe++,slot=(slot+1)&mask)
            {
                const auto index=frame.external_lookup[slot];if(index==UINT32_MAX)break;
                const auto& value=frame.external_geometry[index];if(value.geometry==id&&value.version==version)return index;
            }
            return UINT32_MAX;
        }
        uint32_t FindExternal(const Frame& frame,uint64_t id,uint64_t version)const
        {
            const auto found=TryFindExternal(frame,id,version);if(found==UINT32_MAX)throw std::out_of_range("external geometry version");return found;
        }
        static void RebuildExternalLookup(Frame& frame)
        {
            size_t size=1;while(size<frame.external_geometry.size()*2)size*=2;
            frame.external_lookup.assign(size,UINT32_MAX);const auto mask=size-1;
            for(uint32_t n=0;n<frame.external_geometry.size();n++)
            {
                const auto& geometry=frame.external_geometry[n];auto slot=ExternalSlot(geometry.geometry,geometry.version,mask);
                while(frame.external_lookup[slot]!=UINT32_MAX)slot=(slot+1)&mask;
                frame.external_lookup[slot]=n;
            }
        }
        void SetExternalInputs(Frame& frame,const UruiPlanBindings& bindings)
        {
            if(bindings.external_geometry_count>65536||bindings.external_draw_count>65536||
                (!bindings.external_geometry&&bindings.external_geometry_count)||(!bindings.external_draws&&bindings.external_draw_count)||
                (!bindings.externals&&bindings.external_count)||bindings.external_count>frame.paints.size())throw std::invalid_argument("external geometry bindings");
            std::fill(frame.known_providers.begin(),frame.known_providers.end(),0);
            std::fill(frame.external_offsets.begin(),frame.external_offsets.end(),-1);std::fill(frame.external_counts.begin(),frame.external_counts.end(),0);
            frame.external_geometry.resize(bindings.external_geometry_count);
            if(bindings.external_geometry_count)std::copy_n(bindings.external_geometry,bindings.external_geometry_count,frame.external_geometry.data());
            frame.external_bounds.resize(bindings.external_geometry_count);
            size_t lookup=1;while(lookup<size_t(bindings.external_geometry_count)*2)lookup*=2;frame.external_lookup.assign(lookup,UINT32_MAX);
            for(uint32_t n=0;n<bindings.external_geometry_count;n++)
            {
                const auto& geometry=frame.external_geometry[n];
                if(!ValidateExternalGeometry(geometry))throw std::invalid_argument("external geometry layout/range");
                const auto mask=frame.external_lookup.size()-1;size_t slot=ExternalSlot(geometry.geometry,geometry.version,mask);
                while(frame.external_lookup[slot]!=UINT32_MAX)
                {
                    const auto& previous=frame.external_geometry[frame.external_lookup[slot]];
                    if(previous.geometry==geometry.geometry&&previous.version==geometry.version)throw std::invalid_argument("duplicate external geometry version");
                    slot=(slot+1)&mask;
                }
                frame.external_lookup[slot]=n;
                std::array<float,6> bounds{};
                for(uint32_t vertex=0;vertex<geometry.vertex_count;vertex++)
                {
                    const auto value=ReadExternalVertex(geometry,vertex);const float position[3]{value.x,value.y,value.z};
                    for(uint32_t axis=0;axis<3;axis++)
                    {
                        if(!vertex)bounds[axis]=bounds[axis+3]=position[axis];
                        else {bounds[axis]=std::min(bounds[axis],position[axis]);bounds[axis+3]=std::max(bounds[axis+3],position[axis]);}
                    }
                }
                frame.external_bounds[n]=bounds;
            }
            for(uint32_t n=0;n<bindings.external_count;n++)if(bindings.externals[n].flags&8)
            {
                const auto& provider=bindings.externals[n];
                if(provider.command>=frame.commands.size()||provider.flags!=8||frame.known_providers[provider.command])throw std::invalid_argument("external provider identity");
                auto paint=std::lower_bound(frame.paints.begin(),frame.paints.end(),provider.command,[](const auto& a,uint32_t b){return uint32_t(a.command_index)<b;});
                if(paint==frame.paints.end()||uint32_t(paint->command_index)!=provider.command||paint->reserved!=0)throw std::invalid_argument("external provider paint");
                frame.known_providers[provider.command]=1;
            }
            frame.external_draws.resize(bindings.external_draw_count);
            if(bindings.external_draw_count)std::copy_n(bindings.external_draws,bindings.external_draw_count,frame.external_draws.data());
            for(uint32_t n=0;n<bindings.external_draw_count;n++)
            {
                auto& draw=frame.external_draws[n];
                if(draw.command>=frame.commands.size()||!frame.known_providers[draw.command]||draw.geometry_index>=frame.external_geometry.size()||
                    !draw.provider||!draw.program||(draw.flags&~7u)||draw.reserved||(n&&draw.command<frame.external_draws[n-1].command))throw std::invalid_argument("external draw binding");
                std::array<float,32> values;std::memcpy(values.data(),&draw.parameter,sizeof(draw.parameter));
                for(uint32_t field=0;field<values.size();field++)if(!std::isfinite(values[field]))throw std::invalid_argument("external draw parameter");
                if(draw.flags&4)
                {
                    QuadMapping mapping;
                    if(draw.flags!=4||draw.parameter.uv[0]<=0||draw.parameter.uv[1]<=0||!AnalyticUnitQuad(frame.external_geometry[draw.geometry_index],mapping))throw std::invalid_argument("analytic rectangle mask contract");
                    for(float feather:draw.parameter.tint)if(feather<0)throw std::invalid_argument("negative analytic feather");
                }
                const UruiPlanUniform identity{{1,1,1,1},{1,1,0,0}};
                const auto base=Parameter(frame.commands[draw.command],frame.matrices[draw.command],identity);
                float local[16];std::copy_n(draw.parameter.matrix,16,local);
                for(int column=0;column<4;column++)for(int row=0;row<4;row++)
                {
                    float value=0;for(int axis=0;axis<4;axis++)value+=base.matrix[axis*4+row]*local[column*4+axis];
                    if(!std::isfinite(value))throw std::invalid_argument("external world matrix");draw.parameter.matrix[column*4+row]=value;
                }
                std::copy_n(base.clip,4,draw.parameter.clip);draw.parameter.metadata[0]=float(frame.commands[draw.command].flags&1);
                if(frame.external_offsets[draw.command]<0)frame.external_offsets[draw.command]=int32_t(n);frame.external_counts[draw.command]++;
            }
        }
        int SetImageGeometry(uint64_t input,uint64_t request,const UruiPlanExternalGeometry& geometry,const float* transform)
        {
            if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch||frame->pending==UINT32_MAX)return -2;
            auto& slot=frame->preparation[frame->pending];if((!slot.image&&slot.request.kind!=URUI_PLAN_PREPARE_ELEMENT)||slot.request.request!=request)return -2;
            if(slot.geometry_index!=UINT32_MAX)return -5;
            if(!transform||!ValidateExternalLayout(geometry))return -1;
            for(uint32_t n=0;n<16;n++)if(!std::isfinite(transform[n]))return -1;
            const auto count=frame->external_geometry.size();
            try
            {
                auto index=TryFindExternal(*frame,geometry.geometry,geometry.version);
                if(index==UINT32_MAX)
                {
                    if(count==65536||!ValidateExternalGeometry(geometry))return -1;index=uint32_t(count);
                    frame->external_geometry.push_back(geometry);frame->external_bounds.push_back({});
                    RebuildExternalLookup(*frame);
                }
                slot.geometry_index=index;std::copy_n(transform,16,slot.geometry_transform.data());return 1;
            }
            catch(const std::bad_alloc&){frame->external_geometry.resize(count);frame->external_bounds.resize(count);return -4;}
            catch(...){frame->external_geometry.resize(count);frame->external_bounds.resize(count);return -8;}
        }
        template<class Fill> int SetImageProgram(uint64_t input,uint64_t request,uint64_t program,uint32_t rows,Fill fill,uint64_t clock=0)
        {
            if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch||frame->pending==UINT32_MAX)return -2;
            auto& slot=frame->preparation[frame->pending];
            if((!slot.image&&slot.request.kind!=URUI_PLAN_PREPARE_ELEMENT)||slot.request.request!=request)return -2;
            if(!program||rows<2||rows>544||clock>65536)return -1;
            auto& binding=slot.image?frame->image_programs[slot.request.slot]:frame->captured_programs[slot.layer_index];if(binding.program)return -5;
            const auto offset=frame->image_parameters.size();
            try
            {
                frame->image_parameters.resize(offset+rows);
                auto* data=frame->image_parameters[offset].data();
                const auto status=fill(slot.request,data,rows);
                if(status!=1){frame->image_parameters.resize(offset);return status;}
                for(uint32_t n=0;n<rows*4;n++)if(!std::isfinite(data[n])){frame->image_parameters.resize(offset);return -1;}
                uint32_t clock_index=UINT32_MAX;
                if(clock)
                {
                    if(frame->clock_lookup.size()<clock)frame->clock_lookup.resize(size_t(clock),UINT32_MAX);
                    clock_index=frame->clock_lookup[size_t(clock)-1];
                    if(clock_index==UINT32_MAX)
                    {
                        clock_index=uint32_t(frame->clocks.size());frame->clocks.push_back({data[6],0,0,0});
                        frame->clock_lookup[size_t(clock)-1]=clock_index;
                    }
                    else if(frame->clocks[clock_index][0]!=data[6]){frame->image_parameters.resize(offset);return -1;}
                }
                data[6]=-1; // remapped only after the compiled atlas layout is final
                binding={program,uint32_t(offset),rows,clock_index};return 1;
            }
            catch(const std::bad_alloc&){frame->image_parameters.resize(offset);return -4;}
            catch(...){frame->image_parameters.resize(offset);return -8;}
        }
        template<class GeometryResolve> int Resolve(uint64_t input,const UruiPlanFeedback* feedback,GeometryResolve resolve,UruiPlanPreparePacket& out,const UruiPlanBindings* resources=nullptr)
        {
            out={};if(!Thread())return -3;auto* frame=Find(input);
            if(!frame||!frame->input||frame->epoch!=epoch)return -2;
            try
            {
                if(!frame->resolving&&resources)
                {
                    if(resources->texture_count>65536||(!resources->textures&&resources->texture_count))return -1;
                    frame->resources.resize(resources->texture_count);
                    if(resources->texture_count)std::copy_n(resources->textures,resources->texture_count,frame->resources.data());
                    frame->resource_flags=resources->frame_flags;SetExternalInputs(*frame,*resources);
                }
                frame->resolving=true;
                if(feedback)
                {
                    if(frame->pending==UINT32_MAX)return -1;
                    auto& slot=frame->preparation[frame->pending];auto& request=slot.request;
                    if(feedback->request!=request.request||feedback->kind!=request.kind)return -2;
                    if(request.kind==URUI_PLAN_PREPARE_IMAGE||request.kind==URUI_PLAN_PREPARE_ELEMENT)
                    {
                        if(!RectValid(feedback->ink))return -1;
                        slot.ink.Rect(feedback->ink);
                        if(request.kind==URUI_PLAN_PREPARE_ELEMENT)
                        {
                            const int effect=frame->effect_indices[slot.layer_index];
                            if(effect<0)return -2;
                            InkRegion projected;TransformInk(projected,slot.ink,frame->effects[effect].projection.data());slot.ink=projected;
                        }
                        slot.prepared=true;
                    }
                    else if(request.kind==URUI_PLAN_ELEMENT_LAYOUT)
                    {
                        for(float value:feedback->expansion)if(!std::isfinite(value)||value<0)return -1;
                        if(feedback->transform_count>64||(!feedback->transforms&&feedback->transform_count))return -1;
                        for(uint32_t n=0;n<feedback->transform_count*16;n++)if(!std::isfinite(feedback->transforms[n]))return -1;
                        slot.transform_offset=uint32_t(frame->coverage.size());slot.transform_count=feedback->transform_count;
                        frame->coverage.resize(frame->coverage.size()+feedback->transform_count);
                        for(uint32_t n=0;n<feedback->transform_count;n++)std::copy_n(feedback->transforms+n*16,16,frame->coverage[slot.transform_offset+n].data());
                        if(feedback->capabilities>1||feedback->reserved)return -1;
                        slot.unity_blend=(feedback->capabilities&1)!=0;
                        std::copy_n(feedback->expansion,4,slot.expansion.data());slot.layout=true;
                    }
                    else if(request.kind==URUI_PLAN_ACQUIRE_CAPTURE)
                    {
                        if(!feedback->resource||!feedback->source_version||!feedback->pixel_width||!feedback->pixel_height||
                            feedback->pixel_width>16384||feedback->pixel_height>16384||!RectValid(feedback->ink)||feedback->ink[2]<=0||feedback->ink[3]<=0)return -1;
                        const auto* actual=feedback->ink;
                        if(actual[0]>request.capture[0]||actual[1]>request.capture[1]||
                            double(actual[0])+actual[2]<double(request.capture[0])+request.capture[2]||
                            double(actual[1])+actual[3]<double(request.capture[1])+request.capture[3])return -1;
                        request.resource=feedback->resource;request.source_version=feedback->source_version;
                        request.pixel_width=feedback->pixel_width;request.pixel_height=feedback->pixel_height;
                        std::copy_n(actual,4,request.capture);
                        const auto* ink=request.source_ink;
                        request.uv[0]=(ink[0]-actual[0])/actual[2];
                        request.uv[1]=(actual[1]+actual[3]-ink[1]-ink[3])/actual[3];
                        request.uv[2]=ink[2]/actual[2];request.uv[3]=ink[3]/actual[3];
                        request.kind=URUI_PLAN_PREPARE_ELEMENT;request.request=Identity();
                        out={input,&request,1,0};return 1;
                    }
                    frame->pending=UINT32_MAX;
                }
                if(frame->pending!=UINT32_MAX){out={input,&frame->preparation[frame->pending].request,1,0};return 1;}
                uint32_t next=UINT32_MAX;
                auto advance=[&](uint32_t& cursor,auto pending)
                {while(cursor<frame->preparation.size()&&!pending(frame->preparation[cursor]))cursor++;if(cursor<frame->preparation.size())next=cursor;};
                advance(frame->image_cursor,[](const PrepareSlot& slot){return slot.image&&!slot.prepared;});
                if(next==UINT32_MAX)advance(frame->layout_cursor,[](const PrepareSlot& slot){return !slot.image&&!slot.layout;});
                if(next==UINT32_MAX)advance(frame->element_cursor,[](const PrepareSlot& slot){return !slot.prepared;});
                if(next==UINT32_MAX)
                {
                    if(!frame->ready)CompleteRegions(*frame,resolve);
                    out={input,nullptr,0,1,frame->layer_regions.data(),uint32_t(frame->layer_regions.size()),NeedsRootTexture(*frame)?1u:0u,frame->layer_indices.data(),uint32_t(frame->layer_indices.size()),0};return 1;
                }
                auto& slot=frame->preparation[next];auto& request=slot.request;
                if(slot.image)PrepareSource(*frame,slot,resolve);
                if(!slot.image&&slot.layout)
                {
                    auto ink=Capture(*frame,slot.layer_index,resolve);
                    request.source_ink[0]=float(ink.x0);request.source_ink[1]=float(ink.y0);
                    request.source_ink[2]=float(ink.x1-ink.x0);request.source_ink[3]=float(ink.y1-ink.y0);
                    // Input capture is frozen here; later output ink cannot resize it.
                    if(ink.valid)
                    {
                        ink.x0-=slot.expansion[0];ink.y0-=slot.expansion[1];ink.x1+=slot.expansion[2];ink.y1+=slot.expansion[3];
                        const auto original=ink;
                        for(uint32_t n=0;n<slot.transform_count;n++)TransformInk(ink,original,frame->coverage[slot.transform_offset+n].data());
                    }
                    ink.Capture(request.capture);request.kind=URUI_PLAN_ACQUIRE_CAPTURE;
                }
                request.request=Identity();frame->pending=next;out={input,&request,1,0};return 1;
            }
            catch(const std::bad_alloc&){return -4;}catch(const std::invalid_argument&){return -1;}catch(const std::out_of_range&){return -2;}catch(...){return -8;}
        }
        template<class Resolve> int Compile(uint64_t input,const UruiPlanBindings& bindings,Resolve resolve,UruiRenderPlanPacket& out)
        {
            out={};if(!Thread())return -3;
            auto* source=Find(input);if(!source||!source->input||source->epoch!=epoch)return -2;
            if(!source->preparation.empty()&&!source->ready)return -5;
            if(bindings.texture_count>65536||(!bindings.textures&&bindings.texture_count))return -1;
            if(bindings.external_count>source->paints.size()||(!bindings.externals&&bindings.external_count))return -1;
            size_t external_paint=0;
            for(uint32_t n=0;n<bindings.external_count;n++)
            {
                const auto& binding=bindings.externals[n];
                if(binding.flags>15||(binding.flags&8&&binding.flags!=8)||(binding.flags&3)==3||(n&&binding.command<=bindings.externals[n-1].command))return -1;
                while(external_paint<source->paints.size()&&uint32_t(source->paints[external_paint].command_index)<binding.command)external_paint++;
                if(external_paint==source->paints.size()||uint32_t(source->paints[external_paint].command_index)!=binding.command||source->paints[external_paint].reserved==2)return -1;
                if((source->resolving||source->ready)&&(binding.flags&8)&&!source->known_providers[binding.command])return -1;
                if((bindings.frame_flags&32)&&(binding.flags&2)&&source->paints[external_paint].reserved==0)return -1;
            }
            if((source->resolving||source->ready)&&((bindings.external_geometry_count&&bindings.external_geometry_count!=source->external_geometry.size())||
                (bindings.external_draw_count&&bindings.external_draw_count!=source->external_draws.size())))return -1;
            auto* target=Free();if(!target)return -5;
            geometry_hint_cursor=0;
            compiling=target;
            struct BuildScope
            {
                Frame*& current;Frame* target;Budget* budget;bool published=false;
                ~BuildScope(){current=nullptr;if(!published)*target=Frame(budget);}
            } scope{compiling,target,&frame_budget};
            try
            {
                if(!source->ready)
                {
                    source->resources.resize(bindings.texture_count);if(bindings.texture_count)std::copy_n(bindings.textures,bindings.texture_count,source->resources.data());
                    source->resource_flags=bindings.frame_flags;SetExternalInputs(*source,bindings);CompleteRegions(*source,resolve);
                }
            if(bindings.layer_binding_count)
            {
                if(bindings.frame_flags!=source->resource_flags||bindings.texture_count!=source->resources.size())return -1;
                for(uint32_t n=0;n<bindings.texture_count;n++)
                {const auto& a=bindings.textures[n];const auto& b=source->resources[n];if(a.texture!=b.texture||a.version!=b.version||a.domain!=b.domain||a.flags!=b.flags)return -1;}
            }
            if(bindings.layer_binding_count>source->layer_regions.size()||(!bindings.layer_bindings&&bindings.layer_binding_count))return -1;
            source->cached_layers.assign(std::max(size_t(1),source->created_layers.size()),0);
            for(uint32_t n=0;n<bindings.layer_binding_count;n++)
            {
                const auto& cached=bindings.layer_bindings[n];
                auto region=std::find_if(source->layer_regions.begin(),source->layer_regions.end(),[&](const auto& value){return value.layer==cached.layer;});
                if(cached.flags!=1||region==source->layer_regions.end()||!(region->flags&16)||cached.version!=region->version)return -1;
                source->cached_layers[cached.layer]=1;
            }
            // Reusing a finished parent also suppresses writes in its nested scopes.
            source->layer_stack.clear();source->layer_stack.push_back(0);
            for(const auto& layer:source->layers)
            {
                if(layer.kind==1){source->cached_layers[layer.target]|=source->cached_layers[source->layer_stack.back()];source->layer_stack.push_back(layer.target);}
                else if(layer.kind==2)source->layer_stack.pop_back();
            }
                for(const auto& mask:source->direct_masks)
                {
                    auto normalized=[&](uint64_t texture)
                    {if(!texture)return true;for(uint32_t n=0;n<bindings.texture_count;n++)if(bindings.textures[n].texture==texture)return bindings.textures[n].domain==0&&(bindings.textures[n].flags&1);return false;};
                    if(!normalized(source->commands[mask.command].texture)||!normalized(mask.texture)||(!mask.composite.target&&!(bindings.frame_flags&1)))throw std::invalid_argument("mask resource proof changed");
                }
                // Resolve 的裁剪证明不能在 Compile 时沿用已撤销的根像素网格条件。
                for(const auto& clip:source->binary_mask_clips)
                    if(!source->layers[clip.layer_index].target&&!(bindings.frame_flags&1))throw std::invalid_argument("binary mask pixel-grid proof changed");
                const bool ordinary=source->paints.empty()&&source->layers.empty()&&source->preparation.empty()&&source->direct_masks.empty()&&source->clocks.empty()&&source->image_parameters.empty()&&source->external_geometry.empty()&&source->external_draws.empty()&&bindings.external_count==0&&bindings.external_geometry_count==0&&bindings.external_draw_count==0&&bindings.layer_binding_count==0;
                bool reuse=ordinary&&ordinary_valid&&ordinary_epoch==source->epoch&&ordinary_width==source->width&&ordinary_height==source->height&&ordinary_source_flags==source->resource_flags&&ordinary_binding_flags==bindings.frame_flags&&Same(ordinary_commands,source->commands)&&Same(ordinary_matrices,source->matrices)&&Same(ordinary_uniforms,source->uniforms)&&Same(ordinary_source_resources,source->resources)&&ordinary_resources.size()==bindings.texture_count;
                if(reuse&&bindings.texture_count)reuse=std::memcmp(ordinary_resources.data(),bindings.textures,bindings.texture_count*sizeof(UruiPlanTextureBinding))==0;
                if(reuse)
                {
                    // The immutable geometry payload can expire independently of an
                    // input lease. Validate it even when every input value is equal.
                    for(const auto& command:source->commands)if(command.geometry)
                    {const auto geometry=resolve(command.geometry);if(!geometry.vertices||!geometry.indices)throw std::out_of_range("geometry expired");}
                    for(const auto& operation:previous_operations)if(operation.geometry)
                    {
                        auto found=std::find_if(batches.begin(),batches.end(),[&](const auto& batch){return batch.id==operation.geometry&&batch.epoch==source->epoch;});
                        if(found==batches.end()){reuse=false;break;}
                        found->last_use=Identity();
                    }
                }
                ordinary_valid=false;
                if(reuse)
                {
                    // Each output lease owns its arrays; reuse never aliases a
                    // client's window or overwrites a still-published snapshot.
                    target->operations=previous_operations;target->parameters=previous_parameters;target->extra_parameters=previous_extra_parameters;target->parameter_pages=previous_parameter_pages;
                    BuildTargetRequests(*source,*target);
                    target->input=false;target->compiled=true;target->epoch=epoch;target->token=Identity();target->version=operation_version;target->parameter_version=parameter_version;
                    out={target->token,epoch,target->version,target->parameter_version,target->operations.data(),target->parameters.data(),uint32_t(target->operations.size()),uint32_t(target->parameters.size()),URUI_RENDER_PLAN_PROTOCOL,uint32_t(target->extra_parameters.size()),geometry_budget.retained,frame_budget.retained,counters.allocations,counters.frees,counters.bytes,target->parameter_pages.data(),uint32_t(target->parameter_pages.size()),0,reinterpret_cast<const float*>(target->extra_parameters.data()),target->targets.data(),uint32_t(target->targets.size()),0};
                    ordinary_valid=true;ordinary_reuses++;scope.published=true;return 1;
                }
                size_t parameter_count=source->commands.size()+source->external_draws.size();
                for(uint32_t layer=0;layer<source->captured_programs.size();layer++)if(source->captured_programs[layer].program)source->captured_parameters[layer]=uint32_t(parameter_count++);
                target->operations.clear();target->parameters.resize(parameter_count);keys.clear();pending.clear();
                target->extra_parameters.clear();target->color_offsets.assign(source->commands.size(),-1);
                for(uint32_t command=0;command<source->commands.size();command++)if(source->color_counts[command])
                {
                    target->color_offsets[command]=int32_t(target->extra_parameters.size());
                    target->extra_parameters.push_back({float(source->color_counts[command]),0,0,0});
                    for(uint32_t n=0;n<source->color_counts[command];n++)
                    {const auto& value=source->color_filters[source->color_offsets[command]+n];target->extra_parameters.push_back({float(value.kind),value.value,0,0});}
                }
                const size_t image_offset=target->extra_parameters.size();
                target->extra_parameters.insert(target->extra_parameters.end(),source->image_parameters.begin(),source->image_parameters.end());
                const size_t mask_offset=target->extra_parameters.size();
                for(const auto& mask:source->direct_masks)target->extra_parameters.insert(target->extra_parameters.end(),mask.values.begin(),mask.values.end());
                const size_t clock_offset=target->extra_parameters.size();
                target->extra_parameters.insert(target->extra_parameters.end(),source->clocks.begin(),source->clocks.end());
                for(const auto& image:source->image_programs)if(image.program&&image.clock!=UINT32_MAX)
                    target->extra_parameters[image_offset+image.offset+1][2]=float(target->parameters.size()*8+clock_offset+image.clock);
                for(const auto& image:source->captured_programs)if(image.program&&image.clock!=UINT32_MAX)
                    target->extra_parameters[image_offset+image.offset+1][2]=float(target->parameters.size()*8+clock_offset+image.clock);
                auto geometry_source=[&](uint64_t id,uint64_t version)
                {
                    if(!version)return resolve(id);
                    const auto& data=source->external_geometry[FindExternal(*source,id,version)];
                    return GeometrySource{nullptr,data.vertex_count,nullptr,data.index_count,nullptr,&data};
                };
                auto flush=[&]()
                {
                    if(pending.empty())return;
                    Order(*source,*target,bindings,resolve);
                    UruiPlanOperation operation{};uint32_t domain=0;size_t vertices=0,indices=0;InkRegion bounds;
                    auto batch=[&]()
                    {
                        if(keys.empty())return;
                        if(bounds.valid&&!(operation.reason&0x80000000u))
                        {
                            const double values[4]={bounds.x0,bounds.y0,bounds.x1,bounds.y1};
                            for(int n=0;n<4;n++)
                            {
                                if(std::abs(values[n])>std::numeric_limits<float>::max())operation.reason|=0x80000000u;
                                else operation.bounds[n]=float(values[n]);
                            }
                        }
                        const auto& geometry_batch=Geometry(source->epoch,geometry_source,source,target);
                        operation.geometry=geometry_batch.id;operation.count=uint32_t(keys.size());
                        bool complete=operation.mask_texture==0;
                        for(const auto& key:keys)
                        {
                            const auto& p=target->parameters[key.parameter];
                            if(uint32_t(p.metadata[0])||p.metadata[1]!=0||p.metadata[2]!=0||p.metadata[3]!=0)complete=false;
                            for(int item=0;item<16;item++)if(p.matrix[item]!=(item%5==0?1.f:0.f))complete=false;
                            if(p.tint[0]!=1||p.tint[1]!=1||p.tint[2]!=1||p.tint[3]!=1||p.uv[0]!=1||p.uv[1]!=1||p.uv[2]!=0||p.uv[3]!=0)complete=false;
                        }
                        if(complete)operation.reason|=0x40000000u;
                        else
                        {
                            for(const auto& key:geometry_batch.keys)
                            {
                                const auto index=key.parameter;auto& p=target->parameters[index];const auto& c=source->commands[index];const auto& m=source->matrices[index];
                                // 恢复原始矩阵的计算顺序，避免大坐标抵消放大精度误差。
                                for(int row=0;row<4;row++)p.matrix[12+row]=m[12+row]+m[row]*c.tx+m[4+row]*c.ty;
                                if(key.origin_x!=0||key.origin_y!=0)
                                {
                                    // 普通源顶点 z 恒为零，第三列 XY 可承载无损解码原点。
                                    p.matrix[8]=key.origin_x;p.matrix[9]=key.origin_y;
                                    p.metadata[0]=float(uint32_t(p.metadata[0])|512u);
                                }
                            }
                        }
                        target->operations.push_back(operation);keys.clear();operation={};vertices=indices=0;bounds={};
                    };
                    for(auto ordered:order)
                    {
                        const auto n=pending[ordered];const auto& c=source->commands[n];
                        const auto geometry=resolve(c.geometry);if(!geometry.vertices||!geometry.indices)throw std::out_of_range("geometry expired");
                        const auto current_domain=Domain(c.texture,bindings);
                        const int mask_index=source->direct_mask_indices[n];
                        const uint64_t mask_texture=mask_index>=0?source->direct_masks[mask_index].texture:0;
                        int slot=-1;for(uint32_t t=0;t<operation.texture_count;t++)if(operation.textures[t]==c.texture)slot=int(t);
                        const uint32_t reason=operation.mask_texture&&mask_texture&&operation.mask_texture!=mask_texture?4u:current_domain!=domain?1u:(slot<0&&operation.texture_count==8)?2u:
                            (vertices+geometry.vertex_count>100000||indices+geometry.index_count>300000)?3u:0u;
                        if(!keys.empty()&&reason){batch();operation.reason=reason;slot=-1;}
                        if(keys.empty()){operation.kind=URUI_PLAN_GEOMETRY;operation.command_index=n;domain=current_domain;}
                        if(mask_texture)operation.mask_texture=mask_texture;
                        if(slot<0){slot=int(operation.texture_count);operation.textures[operation.texture_count++]=c.texture;}
                        const auto local=LocalBounds(geometry);
                        try{TransformInk(bounds,local,target->parameters[n].matrix);}
                        catch(const std::invalid_argument&){operation.reason|=0x80000000u;}
                        keys.push_back({c.geometry,n,uint32_t(slot)|0x80000000u,0,c.tx,c.ty});vertices+=geometry.vertex_count;indices+=geometry.index_count;
                    }
                    batch();pending.clear();
                };
                source->external_pending.clear();
                auto flush_external=[&]()
                {
                    if(source->external_pending.empty())return;
                    keys.clear();UruiPlanOperation operation{};
                    auto batch=[&]()
                    {
                        if(keys.empty())return;operation.geometry=Geometry(epoch,geometry_source).id;target->operations.push_back(operation);keys.clear();operation={};
                    };
                    for(auto index:source->external_pending)
                    {
                        const auto& draw=source->external_draws[index];const auto& geometry=source->external_geometry[draw.geometry_index];
                        if(!geometry.vertex_count||!geometry.index_count)continue;
                        if(draw.flags&1)
                        {
                            batch();UruiPlanOperation raw{};raw.kind=URUI_PLAN_EXTERNAL_RAW_GEOMETRY;raw.program=draw.program;
                            raw.command_index=draw.command;raw.layer_index=index;raw.count=1;target->operations.push_back(raw);continue;
                        }
                        if(operation.program&&operation.program!=draw.program)batch();
                        if(!operation.program){operation.kind=URUI_PLAN_EXTERNAL_GEOMETRY;operation.program=draw.program;operation.command_index=draw.command;operation.layer_index=index;}
                        operation.count=index+1-operation.layer_index;
                        keys.push_back({geometry.geometry,uint32_t(source->commands.size()+index),0,geometry.version});
                    }
                    batch();source->external_pending.clear();
                };
                for(uint32_t n=0;n<source->external_draws.size();n++)target->parameters[source->commands.size()+n]=source->external_draws[n].parameter;
                auto flush_images=[&]()
                {
                    if(source->image_pending.empty())return;
                    keys.clear();UruiPlanOperation operation{};size_t vertices=0,indices=0;
                    auto batch=[&]()
                    {if(keys.empty())return;operation.geometry=Geometry(source->epoch,geometry_source).id;operation.count=uint32_t(keys.size());target->operations.push_back(operation);keys.clear();operation={};vertices=indices=0;};
                    for(auto command:source->image_pending)
                    {
                        const auto& draw=source->commands[command];const auto& program=source->image_programs[command];
                        auto geometry_id=draw.geometry;uint64_t geometry_version=0;auto geometry=resolve(draw.geometry);
                        const auto& prepared=source->preparation[source->image_slots[command]];
                        if(prepared.geometry_index!=UINT32_MAX)
                        {
                            const auto& external=source->external_geometry[prepared.geometry_index];geometry_id=external.geometry;geometry_version=external.version;
                            geometry={nullptr,external.vertex_count,nullptr,external.index_count,nullptr,&external};
                        }
                        else if(!geometry.vertices||!geometry.indices)throw std::out_of_range("image geometry expired");
                        int slot=-1;for(uint32_t n=0;n<operation.texture_count;n++)if(operation.textures[n]==draw.texture)slot=int(n);
                        if(!keys.empty()&&(operation.program!=program.program||(slot<0&&operation.texture_count==8)||vertices+geometry.vertex_count>100000||indices+geometry.index_count>300000))
                        {batch();slot=-1;}
                        if(keys.empty()){operation.kind=URUI_PLAN_PARAMETER_IMAGE;operation.program=program.program;operation.command_index=command;}
                        if(slot<0){slot=int(operation.texture_count);operation.textures[operation.texture_count++]=draw.texture;}
                        keys.push_back({geometry_id,command,uint32_t(slot),geometry_version});vertices+=geometry.vertex_count;indices+=geometry.index_count;
                    }
                    batch();source->image_pending.clear();
                };
                size_t layer=0,paint=0,external=0;
                for(uint32_t n=0;n<=source->commands.size();n++)
                {
                    while(layer<source->layers.size()&&source->layers[layer].command_index==int(n))
                    {
                        const auto index=layer++;if(source->omitted_operations[index])continue;
                        flush_images();flush_external();flush();UruiPlanOperation op{};op.kind=URUI_PLAN_LAYER;op.command_index=n;op.layer_index=uint32_t(index);
                        const auto& layer_op=source->layers[index];
                        const auto* clipped=BinaryClip(*source,uint32_t(index));
                        if(clipped)
                        {op.reason|=URUI_PLAN_LAYER_CLIP;std::copy(clipped->bounds.begin(),clipped->bounds.end(),op.bounds);}
                        const auto write_layer=layer_op.kind==6?layer_op.source:layer_op.target;
                        if((layer_op.kind==1||layer_op.kind==3||layer_op.kind==4||layer_op.kind==6)&&source->cached_layers[write_layer])op.reason|=1;
                        const auto& captured=source->captured_programs[index];
                        if(captured.program) {
                            const auto slot_index=source->effect_slots[index];if(slot_index<0)throw std::invalid_argument("captured program slot");
                            const auto& prepared=source->preparation[slot_index];
                            if(prepared.geometry_index==UINT32_MAX)throw std::invalid_argument("captured program needs immutable geometry");
                            const auto& geometry=source->external_geometry[prepared.geometry_index];
                            const auto parameter_index=source->captured_parameters[index];
                            UruiCommand command{};const UruiPlanUniform uniform{{1,1,1,1},{1,1,0,0}};
                            auto parameter=Parameter(command,prepared.geometry_transform,uniform);
                            parameter.metadata[1]=2;parameter.metadata[2]=float(target->parameters.size()*8+image_offset+captured.offset);
                            target->parameters[parameter_index]=parameter;
                            keys.clear();keys.push_back({geometry.geometry,parameter_index,0,geometry.version});
                            op.geometry=Geometry(source->epoch,geometry_source).id;op.program=captured.program;op.count=1;keys.clear();
                        }
                        target->operations.push_back(op);
                    }
                    if(n==source->commands.size())break;
                    const auto& c=source->commands[n];target->parameters[n]=Parameter(c,source->matrices[n],source->uniforms[n]);
                    if(source->suppressed_commands[n]||source->cached_layers[source->command_layers[n]])
                    {
                        if(paint<source->paints.size()&&source->paints[paint].command_index==int(n))paint++;
                        if(external<bindings.external_count&&bindings.externals[external].command==n)external++;
                        continue;
                    }
                    const int direct_mask=source->direct_mask_indices[n];
                    if(direct_mask>=0)
                    {
                        const auto& mask=source->direct_masks[direct_mask];auto& parameter=target->parameters[n];
                        parameter.metadata[0]=float(int(parameter.metadata[0])|256);
                        parameter.metadata[3]=float(target->parameters.size()*8+mask_offset+size_t(direct_mask)*8);
                        if(mask.composite.flags&1)
                        {
                            const float clip[4]={float(mask.composite.clip_x),float(mask.composite.clip_y),float(double(mask.composite.clip_x)+mask.composite.clip_w),float(double(mask.composite.clip_y)+mask.composite.clip_h)};
                            if(c.flags&1){for(int axis=0;axis<2;axis++){parameter.clip[axis]=std::max(parameter.clip[axis],clip[axis]);parameter.clip[axis+2]=std::min(parameter.clip[axis+2],clip[axis+2]);}}
                            else std::copy_n(clip,4,parameter.clip);
                            parameter.metadata[0]=float(int(parameter.metadata[0])|1);
                        }
                    }
                    if(paint<source->paints.size()&&source->paints[paint].command_index==int(n))
                    {
                        const auto& p=source->paints[paint++];
                        const auto& image=source->image_programs[n];
                        if(image.program)
                        {
                            flush_external();flush();
                            auto& parameter=target->parameters[n];
                            const auto& prepared=source->preparation[source->image_slots[n]];
                            if(prepared.geometry_index!=UINT32_MAX)
                            {
                                const auto original=parameter;const auto& local=prepared.geometry_transform;
                                for(int column=0;column<4;column++)for(int row=0;row<4;row++)
                                {
                                    float value=0;for(int axis=0;axis<4;axis++)value+=original.matrix[axis*4+row]*local[column*4+axis];
                                    if(!std::isfinite(value))throw std::invalid_argument("image geometry matrix");parameter.matrix[column*4+row]=value;
                                }
                            }
                            parameter.metadata[1]=2;
                            parameter.metadata[2]=float(target->parameters.size()*8+image_offset+image.offset);
                            if(c.geometry)source->image_pending.push_back(n);continue;
                        }
                        flush_images();
                        if(source->known_providers[n])
                        {
                            if(external>=bindings.external_count||bindings.externals[external].command!=n||bindings.externals[external].flags!=8)throw std::invalid_argument("external provider proof changed");
                            external++;flush();
                            for(uint32_t item=0;item<source->external_counts[n];item++)source->external_pending.push_back(uint32_t(source->external_offsets[n])+item);
                            continue;
                        }
                        flush_external();
                        if(p.reserved==2)
                        {
                            if(source->color_offsets[n]<0||!source->color_counts[n])throw std::out_of_range("point filter program fields missing");
                            auto& parameter=target->parameters[n];parameter.metadata[1]=1;
                            parameter.metadata[2]=float(target->parameters.size()*8+target->color_offsets[n]);
                            if(c.geometry)pending.push_back(n);continue;
                        }
                        const bool bound=external<bindings.external_count&&bindings.externals[external].command==n;
                        const auto flags=bound?bindings.externals[external++].flags:1u;
                        if(p.reserved!=2&&bound&&!(flags&5))
                        {if((flags&2)&&c.geometry)pending.push_back(n);continue;}
                        flush();UruiPlanOperation op{};
                        op.kind=p.reserved==0?URUI_PLAN_EXTERNAL:URUI_PLAN_IMAGE;
                        op.command_index=n;op.count=1;target->operations.push_back(op);
                        if(bound&&(flags&2)&&c.geometry)pending.push_back(n);
                        continue;
                    }
                    flush_images();flush_external();if(c.geometry)pending.push_back(n);
                }
                flush_images();flush_external();flush();
                WrapRootOperations(*source,*target);
                PlanRasterPasses(*source,*target,bindings);
                BuildTargetRequests(*source,*target);
                if(previous_epoch!=epoch||!Same(previous_operations,target->operations))
                {previous_operations=target->operations;operation_version=Identity();}
                const size_t base_bytes=target->parameters.size()*sizeof(UruiPlanParameter),extra_bytes=target->extra_parameters.size()*sizeof(std::array<float,4>);
                const size_t bytes=base_bytes+extra_bytes,previous_bytes=previous_parameters.size()*sizeof(UruiPlanParameter)+previous_extra_parameters.size()*sizeof(std::array<float,4>);
                target->parameter_pages.resize((bytes+4095)/4096);
                for(size_t page=0;page<target->parameter_pages.size();page++)
                {
                    const size_t first=page*4096,count=std::min(size_t(4096),bytes-first);
                    const size_t old_count=previous_bytes>first?std::min(size_t(4096),previous_bytes-first):0;
                    bool same=target->parameters.size()==previous_parameters.size()&&page<previous_parameter_pages.size()&&count==old_count;
                    const size_t base_count=first<base_bytes?std::min(count,base_bytes-first):0;
                    if(same&&base_count)same=std::memcmp(reinterpret_cast<const char*>(target->parameters.data())+first,reinterpret_cast<const char*>(previous_parameters.data())+first,base_count)==0;
                    const size_t extra_count=count-base_count,extra_start=first+base_count-base_bytes;
                    if(same&&extra_count)same=std::memcmp(reinterpret_cast<const char*>(target->extra_parameters.data())+extra_start,reinterpret_cast<const char*>(previous_extra_parameters.data())+extra_start,extra_count)==0;
                    target->parameter_pages[page]=same?previous_parameter_pages[page]:Identity();
                }
                if(previous_parameters.size()!=target->parameters.size()||!Same(previous_parameter_pages,target->parameter_pages))
                {previous_parameters=target->parameters;previous_extra_parameters=target->extra_parameters;previous_parameter_pages=target->parameter_pages;parameter_version=Identity();}
                previous_epoch=epoch;
                if(ordinary)
                {
                    ordinary_commands=source->commands;ordinary_matrices=source->matrices;ordinary_uniforms=source->uniforms;ordinary_source_resources=source->resources;
                    ordinary_resources.resize(bindings.texture_count);if(bindings.texture_count)std::copy_n(bindings.textures,bindings.texture_count,ordinary_resources.data());
                    ordinary_epoch=source->epoch;ordinary_width=source->width;ordinary_height=source->height;ordinary_source_flags=source->resource_flags;ordinary_binding_flags=bindings.frame_flags;ordinary_valid=true;
                }
                target->input=false;target->compiled=true;target->epoch=epoch;target->token=Identity();target->version=operation_version;target->parameter_version=parameter_version;
                out={target->token,epoch,target->version,target->parameter_version,target->operations.data(),target->parameters.data(),
                    uint32_t(target->operations.size()),uint32_t(target->parameters.size()),URUI_RENDER_PLAN_PROTOCOL,uint32_t(target->extra_parameters.size()),geometry_budget.retained,frame_budget.retained,
                    counters.allocations,counters.frees,counters.bytes,target->parameter_pages.data(),uint32_t(target->parameter_pages.size()),0,reinterpret_cast<const float*>(target->extra_parameters.data()),target->targets.data(),uint32_t(target->targets.size()),0};
                scope.published=true;return 1;
            }
            catch(const std::bad_alloc&){return -4;}catch(const std::invalid_argument&){return -1;}catch(const std::out_of_range&){return -2;}catch(...){return -8;}
        }
        int Geometry(uint64_t lease,uint64_t geometry,UruiGeometryView& out) try
        {
            out={};if(!Thread())return -3;
            if(!geometry)return -2;
            auto* frame=Find(lease);if(!frame)return -2;
            if(frame->input)
            {
                if(frame->epoch!=epoch)return -2;
                for(const auto& slot:frame->preparation)if(slot.image&&slot.source_ready&&slot.request.geometry==geometry)
                {
                    auto& source=frame->sources[slot.source_index];ComposeSource(source);
                    out={source.vertices.data(),source.indices.data(),uint32_t(source.vertices.size()),uint32_t(source.indices.size()),source.version};return 1;
                }
                return -2;
            }
            if(!frame->compiled)return -2;
            bool used=false;for(const auto& op:frame->operations)if(op.geometry==geometry)used=true;
            if(!used)return -2;
            for(const auto& batch:batches)if(batch.id==geometry)
            {out={batch.vertices.data(),batch.indices.data(),uint32_t(batch.vertices.size()),uint32_t(batch.indices.size()),batch.id};return 1;}
            return -2;
        }
        catch(const std::bad_alloc&){out={};return -4;}
        catch(...){out={};return -8;}
        int Release(uint64_t token)
        {
            if(!Thread())return -3;
            auto* frame=Find(token);if(!frame)return -2;frame->token=0;CollectRetiredEpochs();return 1;
        }
    };
}
