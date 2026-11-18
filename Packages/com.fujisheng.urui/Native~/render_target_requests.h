#pragma once
// Included inside urui_plan after Frame. Decisions belong to the compiled plan;
// Unity maps these logical regions to actual viewport pixels and owns the leases.
static bool NeedsRootTexture(const Frame& source)
{
    if(!(source.resource_flags&16)||((source.resource_flags&64)&&(source.resource_flags&128)))return false;
    size_t paint=0;
    if(source.resource_flags&8)for(uint32_t n=0;n<source.commands.size();n++)
    {
        const auto& command=source.commands[n];
        while(paint<source.paints.size()&&uint32_t(source.paints[paint].command_index)<n)paint++;
        if((source.resource_flags&32)&&paint<source.paints.size()&&uint32_t(source.paints[paint].command_index)==n&&source.paints[paint].reserved==0)continue;
        const auto direct=source.direct_mask_indices[n];
        const auto layer=direct>=0?source.direct_masks[direct].composite.target:source.command_layers[n];
        if(!command.geometry||layer||source.suppressed_commands[n]||source.image_programs[n].program||source.known_providers[n])continue;
        uint32_t domain=0;
        for(const auto& texture:source.resources)if(texture.texture==command.texture){domain=texture.domain;break;}
        if(!domain)return true;
    }
    return false;
}
static void WrapRootOperations(const Frame& source,Frame& target)
{
    if(!NeedsRootTexture(source)||target.operations.empty())return;
    UruiPlanOperation begin{},end{};begin.kind=URUI_PLAN_ROOT_COPY;end.kind=URUI_PLAN_ROOT_PRESENT;
    // 纯几何证明允许把屏幕来源归一化合入编码 pass；保留几何范围作为证明，
    // 不再按逐帧 ink 尺寸租用工作目标。未知回调、图层、RT 和非像素网格继续保守处理。
    // 检查最终操作而非作者图层：已直接化的 mask 不再有离屏/回调操作。
    if((source.resource_flags&1)&&!(source.resource_flags&256))
    {
        bool valid=true;float bounds[4]={float(source.width),float(source.height),0,0};
        for(const auto& operation:target.operations)
        {
            if(operation.kind!=URUI_PLAN_GEOMETRY||(operation.reason&0x80000000u)){valid=false;break;}
            for(uint32_t n=0;n<operation.texture_count;n++)for(const auto& texture:source.resources)
                if(texture.texture==operation.textures[n]&&texture.domain)valid=false;
            bounds[0]=std::min(bounds[0],operation.bounds[0]);bounds[1]=std::min(bounds[1],operation.bounds[1]);
            bounds[2]=std::max(bounds[2],operation.bounds[2]);bounds[3]=std::max(bounds[3],operation.bounds[3]);
        }
        bounds[0]=std::max(0.f,std::floor(bounds[0]));bounds[1]=std::max(0.f,std::floor(bounds[1]));
        bounds[2]=std::min(float(source.width),std::ceil(bounds[2]));bounds[3]=std::min(float(source.height),std::ceil(bounds[3]));
        if(valid&&bounds[2]>bounds[0]&&bounds[3]>bounds[1]){begin.reason=1;std::copy_n(bounds,4,begin.bounds);}
    }
    target.operations.insert(target.operations.begin(),begin);target.operations.push_back(end);
}
static void PlanRasterPasses(const Frame& source,Frame& target,const UruiPlanBindings& bindings)
{
    if(!(source.resource_flags&256)||!(source.resource_flags&8))return;
    // 只写离屏的回调原本保持父层编码域，提前解析会新增LDR量化边界。
    // 该层继续使用可跨缓冲保留的存储附件；其他层仍独立规划原生区间。
    auto& stored=target.target_capabilities;stored.assign(std::max(size_t(1),source.created_layers.size()),0);
    bool final_root=(source.resource_flags&(1024u|512u|16u|1u))==(1024u|512u|16u|1u);
    uint64_t current=0;
    for(const auto& op:target.operations)
    {
        if((op.kind==URUI_PLAN_EXTERNAL||op.kind==URUI_PLAN_IMAGE)&&!current)final_root=false;
        if((op.kind==URUI_PLAN_EXTERNAL||op.kind==URUI_PLAN_IMAGE)&&bindings.external_count)
        {
            const auto first=bindings.externals,last=first+bindings.external_count;
            const auto binding=std::lower_bound(first,last,op.command_index,[](const auto& item,uint32_t command){return item.command<command;});
            if(binding!=last&&binding->command==op.command_index&&(binding->flags&4)&&!(binding->flags&1))stored.at(current)=1;
        }
        if(op.kind==URUI_PLAN_LAYER)
        {
            const auto& layer=source.layers[op.layer_index];
            if(layer.kind==1||layer.kind==2||(layer.kind==4&&layer.source&&layer.source!=layer.target))current=layer.target;
        }
    }
    // 资源表在本次规划内不可变；相邻判断复用同一查询结果，不保存跨帧历史。
    uint64_t domain_texture=0;uint32_t domain_value=0;bool domain_known=false;
    auto domain=[&](const UruiPlanOperation& op) {
        if(!op.texture_count)return 0u;
        if(domain_known&&domain_texture==op.textures[0])return domain_value;
        domain_texture=op.textures[0];domain_value=0;domain_known=true;
        for(const auto& texture:source.resources)if(texture.texture==domain_texture){domain_value=texture.domain;break;}
        return domain_value;
    };
    if(target.operations.size()>1&&target.operations.back().kind==URUI_PLAN_ROOT_PRESENT)
    {
        const auto& last=target.operations[target.operations.size()-2];
        // 仅已证明完整8bit输出且无根不透明回调时，最终解析可经根存储再1:1呈现。
        // 偏移viewport的Blit会覆盖外部alpha，继续保留原来的直接解析。
        if(last.kind==URUI_PLAN_GEOMETRY&&!domain(last)&&!((source.resource_flags&64)&&(source.resource_flags&128))&&
            !final_root)stored[0]=1;
    }
    auto share=[&](const UruiPlanOperation& op,uint64_t active) {
        if(op.kind==URUI_PLAN_GEOMETRY||op.kind==URUI_PLAN_EXTERNAL_GEOMETRY||op.kind==URUI_PLAN_PARAMETER_IMAGE)return true;
        if(op.kind!=URUI_PLAN_LAYER||!(source.resource_flags&64))return false;
        const auto& layer=source.layers[op.layer_index];
        if(layer.kind!=4||layer.blend!=0||!layer.source||layer.source==layer.target||layer.target!=active)return false;
        for(const auto& slot:source.preparation)if(!slot.image&&slot.unity_blend&&slot.request.layer==layer.source)return false;
        // 别名源若实际等于目标，旧后端需要临时复制，不能留在同一原生pass。
        auto physical=[&](uint64_t id){for(size_t n=0;n<source.mask_copy_sources.size()&&id<source.mask_copy_sources.size()&&source.mask_copy_sources[id];n++)id=source.mask_copy_sources[id];return id;};
        return physical(layer.source)!=physical(layer.target);
    };
    uint64_t active=0;
    for(uint32_t n=0;n<target.operations.size();)
    {
        auto& op=target.operations[n];
        const bool fetch=(source.resource_flags&64)&&(active?(source.resource_flags&4):(source.resource_flags&128));
        if(op.kind==URUI_PLAN_GEOMETRY&&!domain(op)&&!fetch&&!stored.at(active))
        {
            const auto first=n;uint32_t groups=0;bool encoded=false;
            const bool bounded=!active&&(source.resource_flags&(512u|16u|1u))==(512u|16u|1u);
            uint32_t bounds_start=0;bool bounds_valid=false;float bounds[4]{};
            auto finish_bounds=[&]() {
                if(bounds_valid){auto& item=target.operations[bounds_start];item.reason|=URUI_PLAN_RASTER_REGION;std::copy_n(bounds,4,item.bounds);}
            };
            while(n<target.operations.size()&&share(target.operations[n],active))
            {
                const auto& item=target.operations[n];
                const bool next=item.kind==URUI_PLAN_GEOMETRY&&!domain(item)&&!fetch;
                if(next&&!encoded)
                {
                    if(groups==8)break;groups++;
                    if(bounded)
                    {
                        bounds_start=n;bounds_valid=true;
                        bounds[0]=bounds[1]=std::numeric_limits<float>::max();
                        bounds[2]=bounds[3]=std::numeric_limits<float>::lowest();
                    }
                }
                if(bounded)
                {
                    // 与原有区间遍历同时收集范围，避免再次遍历操作和资源表。
                    if(encoded&&!next)finish_bounds();
                    if(next)
                    {
                        bounds_valid=bounds_valid&&!(item.reason&0x80000000u)&&item.bounds[2]>item.bounds[0]&&item.bounds[3]>item.bounds[1];
                        for(int axis=0;axis<4;axis++)bounds_valid=bounds_valid&&std::isfinite(item.bounds[axis]);
                        bounds[0]=std::min(bounds[0],item.bounds[0]);bounds[1]=std::min(bounds[1],item.bounds[1]);
                        bounds[2]=std::max(bounds[2],item.bounds[2]);bounds[3]=std::max(bounds[3],item.bounds[3]);
                    }
                }
                encoded=next;n++;
            }
            if(bounded&&encoded)finish_bounds();
            auto& begin=target.operations[first];begin.reason|=URUI_PLAN_RASTER_BEGIN|((groups-1)<<URUI_PLAN_RASTER_GROUP_SHIFT);
            target.operations[n-1].reason|=URUI_PLAN_RASTER_END;
            continue;
        }
        if(op.kind==URUI_PLAN_LAYER)
        {
            const auto& layer=source.layers[op.layer_index];
            if(layer.kind==1||layer.kind==2||(layer.kind==4&&layer.source&&layer.source!=layer.target))active=layer.target;
        }
        n++;
    }
}
static void BuildTargetRequests(const Frame& source,Frame& target)
{
    auto& requests=target.targets;requests.clear();
    target.target_lookup.assign(std::max(size_t(1),source.created_layers.size())*4,UINT32_MAX);
    target.target_capabilities.assign(std::max(size_t(1),source.created_layers.size()),0);
    for(const auto& slot:source.preparation)if(!slot.image&&slot.unity_blend)target.target_capabilities[slot.request.layer]|=1;
    auto add=[&](uint64_t layer,int32_t slot,uint32_t kind,uint64_t related,uint32_t ordinal)
    {
        uint32_t* lookup=nullptr;
        if(slot<3)
        {
            const size_t key=size_t(layer)*4+size_t(slot+1);
            if(key>=target.target_lookup.size())throw std::out_of_range("target layer");
            lookup=&target.target_lookup[key];
        }
        if(lookup&&*lookup!=UINT32_MAX)
        {auto& value=requests[*lookup];value.first_operation=std::min(value.first_operation,ordinal);value.last_operation=std::max(value.last_operation,ordinal);return;}
        if(lookup)*lookup=uint32_t(requests.size());
        requests.push_back({layer,related,slot,kind,ordinal,ordinal});
    };
    for(const auto& region:source.layer_regions)if(!(region.flags&8))add(region.layer,0,1,0,UINT32_MAX);
    auto touch=[&](uint64_t layer,uint32_t ordinal)
    {
        if(!layer)return;
        size_t key=size_t(layer)*4+1;
        for(size_t steps=0;key<target.target_lookup.size()&&target.target_lookup[key]==UINT32_MAX&&steps<source.mask_copy_sources.size();steps++)
        {
            if(layer>=source.mask_copy_sources.size()||!source.mask_copy_sources[layer])return;
            layer=source.mask_copy_sources[layer];key=size_t(layer)*4+1;
        }
        if(key>=target.target_lookup.size()||target.target_lookup[key]==UINT32_MAX)return;
        auto& request=requests[target.target_lookup[key]];
        request.first_operation=std::min(request.first_operation,ordinal);
        request.last_operation=request.last_operation==UINT32_MAX?ordinal:std::max(request.last_operation,ordinal);
    };
    if(!target.operations.empty()&&target.operations.front().kind==URUI_PLAN_ROOT_COPY)
        requests.push_back({0,0,-2,5,0,uint32_t(target.operations.size()-1)});
    // 能直接读取当前颜色附件时，独立 CSS 图层不再需要背景快照。
    auto physical_layer=[&](uint64_t layer) {
        for(size_t steps=0;layer<source.mask_copy_sources.size()&&source.mask_copy_sources[layer];steps++) {
            if(steps>=source.mask_copy_sources.size())throw std::out_of_range("cyclic target alias");
            layer=source.mask_copy_sources[layer];
        }
        return layer;
    };
    auto fetch_blend=[&](const auto& layer) {
        return (source.resource_flags&64)&&layer.source&&physical_layer(layer.source)!=physical_layer(layer.target);
    };
    uint32_t root_capture=UINT32_MAX;
    auto capture_root=[&](uint32_t ordinal)
    {
        if(root_capture==UINT32_MAX){root_capture=uint32_t(requests.size());requests.push_back({0,0,-3,6,ordinal,ordinal});}
        else requests[root_capture].last_operation=ordinal;
    };
    const bool screen_without_work=(source.resource_flags&16)&&(target.operations.empty()||target.operations.front().kind!=URUI_PLAN_ROOT_COPY);
    uint64_t active_layer=0;
    bool in_raster=false;
    for(uint32_t ordinal=0;ordinal<target.operations.size();ordinal++)
    {
        const auto& operation=target.operations[ordinal];
        const bool transient=in_raster||(operation.reason&URUI_PLAN_RASTER_BEGIN);
        in_raster=transient&&!(operation.reason&URUI_PLAN_RASTER_END);
        // 完整根快照由 Unity 标准 Blit 同时解析和归一化方向，不需要额外全屏中转。
        // 无工作目标时的局部屏幕读取仍在对应图层操作处声明解析目标。
        if(operation.kind!=URUI_PLAN_LAYER)
        {
            const auto layer=active_layer;touch(layer,ordinal);
            if(operation.kind==URUI_PLAN_GEOMETRY&&(source.resource_flags&8))
            {
                uint32_t domain=0;
                if(operation.texture_count)for(const auto& texture:source.resources)if(texture.texture==operation.textures[0]){domain=texture.domain;break;}
                const bool fetch=(source.resource_flags&64)&&(layer?(source.resource_flags&4):(source.resource_flags&128));
                if(!domain&&!fetch)add(layer,-1,transient?7:2,0,ordinal);
            }
            continue;
        }
        const auto& layer=source.layers[operation.layer_index];
        touch(layer.source,ordinal);touch(layer.target,ordinal);touch(layer.mask,ordinal);
        if(layer.kind==1||layer.kind==2||(layer.kind==4&&layer.source&&layer.source!=layer.target))active_layer=layer.target;
        if(operation.reason&1)continue;
        if(screen_without_work&&layer.kind==4&&(!layer.source||(!layer.target&&layer.blend==0&&(source.resource_flags&2)&&!fetch_blend(layer)&&!(target.target_capabilities[layer.source]&1))))capture_root(ordinal);
        if(layer.kind==6)
        {
            const bool element=operation.layer_index<source.effect_indices.size()&&source.effect_indices[operation.layer_index]>=0&&
                source.effects[source.effect_indices[operation.layer_index]].kind==3;
            // 自遮罩可直接读写当前颜色附件；不交换内容租约，也不保留无用的写回目标。
            const bool mask_fetch=layer.blend==1&&(source.resource_flags&64);
            if(!element&&!mask_fetch)add(layer.source,1,3,0,ordinal);
            if(layer.blend!=1)add(layer.source,2,3,0,ordinal);
        }
        if(layer.kind==4&&layer.blend==0&&layer.source&&(source.resource_flags&2)&&!fetch_blend(layer))
        {
            if(!(target.target_capabilities[layer.source]&1))
            {
                add(layer.source,3+int32_t(operation.layer_index),4,layer.target,ordinal);
                if(layer.source==layer.target)add(layer.source,1,3,0,ordinal);
            }
        }
    }
    for(auto& request:requests)if(request.first_operation==UINT32_MAX)request.first_operation=request.last_operation=0;
}
