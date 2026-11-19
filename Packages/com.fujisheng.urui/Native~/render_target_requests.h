#pragma once
// Included inside urui_plan after Frame. Decisions belong to the compiled plan;
// Unity maps these logical regions to actual viewport pixels and owns the leases.
static bool NeedsRootTexture(const Frame& source)
{
    if (!(source.resourceFlags & URUI_PLAN_RESOURCE_SCREEN_OUTPUT) ||
        ((source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) &&
         (source.resourceFlags & URUI_PLAN_RESOURCE_HIGH_PRECISION_ROOT)))
    {
        return false;
    }
    size_t paint = 0;
    if (source.resourceFlags & URUI_PLAN_RESOURCE_ENCODED_RASTER)
    {
        for (uint32_t n = 0; n < source.commands.size(); n++)
        {
            const auto& command = source.commands[n];
            while (paint < source.paints.size() && uint32_t(source.paints[paint].command_index) < n)
            {
                paint++;
            }
            if ((source.resourceFlags & URUI_PLAN_RESOURCE_SHADER_IMAGE_PAINT) && paint < source.paints.size() &&
                uint32_t(source.paints[paint].command_index) == n && source.paints[paint].reserved == 0)
            {
                continue;
            }
            const auto direct = source.directMaskIndices[n];
            const auto layer = direct >= 0 ? source.directMasks[direct].composite.target : source.command_layers[n];
            if (!command.geometry || layer || source.suppressedCommands[n] || source.imagePrograms[n].program ||
                source.knownProviders[n])
            {
                continue;
            }
            uint32_t domain = 0;
            for (const auto& texture : source.resources)
            {
                if (texture.texture == command.texture)
                {
                    domain = texture.domain;
                    break;
                }
            }
            if (!domain)
            {
                return true;
            }
        }
    }
    return false;
}
static void WrapRootOperations(const Frame& source, Frame& target)
{
    if (!NeedsRootTexture(source) || target.operations.empty())
    {
        return;
    }
    URUIPlanOperation begin{};
    URUIPlanOperation end{};
    begin.kind = URUI_PLAN_ROOT_COPY;
    end.kind = URUI_PLAN_ROOT_PRESENT;
    // 纯几何证明允许把屏幕来源归一化合入编码 pass；保留几何范围作为证明，
    // 不再按逐帧 ink 尺寸租用工作目标。未知回调、图层、RT 和非像素网格继续保守处理。
    // 检查最终操作而非作者图层：已直接化的 mask 不再有离屏/回调操作。
    if ((source.resourceFlags & URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT) &&
        !(source.resourceFlags & URUI_PLAN_RESOURCE_COLOR_PASS))
    {
        bool valid = true;
        float bounds[4] = {float(source.width), float(source.height), 0, 0};
        for (const auto& operation : target.operations)
        {
            if (operation.kind != URUI_PLAN_GEOMETRY || (operation.reason & 0x80000000u))
            {
                valid = false;
                break;
            }
            for (uint32_t n = 0; n < operation.texture_count; n++)
            {
                for (const auto& texture : source.resources)
                {
                    if (texture.texture == operation.textures[n] && texture.domain)
                    {
                        valid = false;
                    }
                }
            }
            bounds[0] = std::min(bounds[0], operation.bounds[0]);
            bounds[1] = std::min(bounds[1], operation.bounds[1]);
            bounds[2] = std::max(bounds[2], operation.bounds[2]);
            bounds[3] = std::max(bounds[3], operation.bounds[3]);
        }
        bounds[0] = std::max(0.f, std::floor(bounds[0]));
        bounds[1] = std::max(0.f, std::floor(bounds[1]));
        bounds[2] = std::min(float(source.width), std::ceil(bounds[2]));
        bounds[3] = std::min(float(source.height), std::ceil(bounds[3]));
        if (valid && bounds[2] > bounds[0] && bounds[3] > bounds[1])
        {
            begin.reason = 1;
            std::copy_n(bounds, 4, begin.bounds);
        }
    }
    target.operations.insert(target.operations.begin(), begin);
    target.operations.push_back(end);
}
static void PlanRasterPasses(const Frame& source, Frame& target, const URUIPlanBindings& bindings)
{
    if (!(source.resourceFlags & URUI_PLAN_RESOURCE_COLOR_PASS) ||
        !(source.resourceFlags & URUI_PLAN_RESOURCE_ENCODED_RASTER))
    {
        return;
    }
    // 只写离屏的回调原本保持父层编码域，提前解析会新增LDR量化边界。
    // 该层继续使用可跨缓冲保留的存储附件；其他层仍独立规划原生区间。
    auto& stored = target.targetCapabilities;
    stored.assign(std::max(size_t(1), source.createdLayers.size()), 0);
    bool finalRoot = (source.resourceFlags &
                      (URUI_PLAN_RESOURCE_SHARED_FINAL_ROOT_COLOR_PASS | URUI_PLAN_RESOURCE_BOUNDED_COLOR_CONVERSIONS |
                       URUI_PLAN_RESOURCE_SCREEN_OUTPUT | URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT)) ==
                     (URUI_PLAN_RESOURCE_SHARED_FINAL_ROOT_COLOR_PASS | URUI_PLAN_RESOURCE_BOUNDED_COLOR_CONVERSIONS |
                      URUI_PLAN_RESOURCE_SCREEN_OUTPUT | URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT);
    uint64_t current = 0;
    for (const auto& op : target.operations)
    {
        if ((op.kind == URUI_PLAN_EXTERNAL || op.kind == URUI_PLAN_IMAGE) && !current)
        {
            finalRoot = false;
        }
        if ((op.kind == URUI_PLAN_EXTERNAL || op.kind == URUI_PLAN_IMAGE) && bindings.external_count)
        {
            const auto first = bindings.externals;
            const auto last = first + bindings.external_count;
            const auto binding =
                std::lower_bound(first, last, op.command_index,
                                 [](const auto& item, uint32_t command) { return item.command < command; });
            if (binding != last && binding->command == op.command_index && (binding->flags & 4) &&
                !(binding->flags & 1))
            {
                stored.at(current) = 1;
            }
        }
        if (op.kind == URUI_PLAN_LAYER)
        {
            const auto& layer = source.layers[op.layer_index];
            if (layer.kind == URUI_LAYER_PUSH || layer.kind == URUI_LAYER_POP ||
                (layer.kind == URUI_LAYER_COMPOSITE && layer.source && layer.source != layer.target))
            {
                current = layer.target;
            }
        }
    }
    // 资源表在本次规划内不可变；相邻判断复用同一查询结果，不保存跨帧历史。
    uint64_t domainTexture = 0;
    uint32_t domainValue = 0;
    bool domainKnown = false;
    auto domain = [&](const URUIPlanOperation& op)
    {
        if (!op.texture_count)
        {
            return 0u;
        }
        if (domainKnown && domainTexture == op.textures[0])
        {
            return domainValue;
        }
        domainTexture = op.textures[0];
        domainValue = 0;
        domainKnown = true;
        for (const auto& texture : source.resources)
        {
            if (texture.texture == domainTexture)
            {
                domainValue = texture.domain;
                break;
            }
        }
        return domainValue;
    };
    if (target.operations.size() > 1 && target.operations.back().kind == URUI_PLAN_ROOT_PRESENT)
    {
        const auto& last = target.operations[target.operations.size() - 2];
        // 仅已证明完整8bit输出且无根不透明回调时，最终解析可经根存储再1:1呈现。
        // 偏移viewport的Blit会覆盖外部alpha，继续保留原来的直接解析。
        if (last.kind == URUI_PLAN_GEOMETRY && !domain(last) &&
            !((source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) &&
              (source.resourceFlags & URUI_PLAN_RESOURCE_HIGH_PRECISION_ROOT)) &&
            !finalRoot)
        {
            stored[0] = 1;
        }
    }
    auto share = [&](const URUIPlanOperation& op, uint64_t active)
    {
        if (op.kind == URUI_PLAN_GEOMETRY || op.kind == URUI_PLAN_EXTERNAL_GEOMETRY ||
            op.kind == URUI_PLAN_PARAMETER_IMAGE)
        {
            return true;
        }
        if (op.kind != URUI_PLAN_LAYER || !(source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH))
        {
            return false;
        }
        const auto& layer = source.layers[op.layer_index];
        if (layer.kind != URUI_LAYER_COMPOSITE || layer.blend != 0 || !layer.source || layer.source == layer.target ||
            layer.target != active)
        {
            return false;
        }
        for (const auto& slot : source.preparation)
        {
            if (!slot.image && slot.unityBlend && slot.request.layer == layer.source)
            {
                return false;
            }
        }
        // 别名源若实际等于目标，旧后端需要临时复制，不能留在同一原生pass。
        auto physical = [&](uint64_t id)
        {
            for (size_t n = 0;
                 n < source.maskCopySources.size() && id < source.maskCopySources.size() && source.maskCopySources[id];
                 n++)
            {
                id = source.maskCopySources[id];
            }
            return id;
        };
        return physical(layer.source) != physical(layer.target);
    };
    uint64_t active = 0;
    for (uint32_t n = 0; n < target.operations.size();)
    {
        auto& op = target.operations[n];
        const bool fetch = (source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) &&
                           (active ? (source.resourceFlags & URUI_PLAN_RESOURCE_HDR_CAMERA)
                                   : (source.resourceFlags & URUI_PLAN_RESOURCE_HIGH_PRECISION_ROOT));
        if (op.kind == URUI_PLAN_GEOMETRY && !domain(op) && !fetch && !stored.at(active))
        {
            const auto first = n;
            uint32_t groups = 0;
            bool encoded = false;
            const bool bounded =
                !active &&
                (source.resourceFlags & (URUI_PLAN_RESOURCE_BOUNDED_COLOR_CONVERSIONS |
                                         URUI_PLAN_RESOURCE_SCREEN_OUTPUT | URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT)) ==
                    (URUI_PLAN_RESOURCE_BOUNDED_COLOR_CONVERSIONS | URUI_PLAN_RESOURCE_SCREEN_OUTPUT |
                     URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT);
            uint32_t boundsStart = 0;
            bool boundsValid = false;
            float bounds[4]{};
            auto finishBounds = [&]()
            {
                if (boundsValid)
                {
                    auto& item = target.operations[boundsStart];
                    item.reason |= URUI_PLAN_RASTER_REGION;
                    std::copy_n(bounds, 4, item.bounds);
                }
            };
            while (n < target.operations.size() && share(target.operations[n], active))
            {
                const auto& item = target.operations[n];
                const bool next = item.kind == URUI_PLAN_GEOMETRY && !domain(item) && !fetch;
                if (next && !encoded)
                {
                    if (groups == 8)
                    {
                        break;
                    }
                    groups++;
                    if (bounded)
                    {
                        boundsStart = n;
                        boundsValid = true;
                        bounds[0] = bounds[1] = std::numeric_limits<float>::max();
                        bounds[2] = bounds[3] = std::numeric_limits<float>::lowest();
                    }
                }
                if (bounded)
                {
                    // 与原有区间遍历同时收集范围，避免再次遍历操作和资源表。
                    if (encoded && !next)
                    {
                        finishBounds();
                    }
                    if (next)
                    {
                        boundsValid = boundsValid && !(item.reason & 0x80000000u) && item.bounds[2] > item.bounds[0] &&
                                      item.bounds[3] > item.bounds[1];
                        for (int axis = 0; axis < 4; axis++)
                        {
                            boundsValid = boundsValid && std::isfinite(item.bounds[axis]);
                        }
                        bounds[0] = std::min(bounds[0], item.bounds[0]);
                        bounds[1] = std::min(bounds[1], item.bounds[1]);
                        bounds[2] = std::max(bounds[2], item.bounds[2]);
                        bounds[3] = std::max(bounds[3], item.bounds[3]);
                    }
                }
                encoded = next;
                n++;
            }
            if (bounded && encoded)
            {
                finishBounds();
            }
            auto& begin = target.operations[first];
            begin.reason |= URUI_PLAN_RASTER_BEGIN | ((groups - 1) << URUI_PLAN_RASTER_GROUP_SHIFT);
            target.operations[n - 1].reason |= URUI_PLAN_RASTER_END;
            continue;
        }
        if (op.kind == URUI_PLAN_LAYER)
        {
            const auto& layer = source.layers[op.layer_index];
            if (layer.kind == URUI_LAYER_PUSH || layer.kind == URUI_LAYER_POP ||
                (layer.kind == URUI_LAYER_COMPOSITE && layer.source && layer.source != layer.target))
            {
                active = layer.target;
            }
        }
        n++;
    }
}
static void BuildTargetRequests(const Frame& source, Frame& target)
{
    auto& requests = target.targets;
    requests.clear();
    target.targetLookup.assign(std::max(size_t(1), source.createdLayers.size()) * 4, UINT32_MAX);
    target.targetCapabilities.assign(std::max(size_t(1), source.createdLayers.size()), 0);
    for (const auto& slot : source.preparation)
    {
        if (!slot.image && slot.unityBlend)
        {
            target.targetCapabilities[slot.request.layer] |= 1;
        }
    }
    auto add = [&](uint64_t layer, int32_t slot, uint32_t kind, uint64_t related, uint32_t ordinal)
    {
        uint32_t* lookup = nullptr;
        if (slot < 3)
        {
            const size_t key = size_t(layer) * 4 + size_t(slot + 1);
            if (key >= target.targetLookup.size())
            {
                throw std::out_of_range("target layer");
            }
            lookup = &target.targetLookup[key];
        }
        if (lookup && *lookup != UINT32_MAX)
        {
            auto& value = requests[*lookup];
            value.first_operation = std::min(value.first_operation, ordinal);
            value.last_operation = std::max(value.last_operation, ordinal);
            return;
        }
        if (lookup)
        {
            *lookup = uint32_t(requests.size());
        }
        requests.push_back({layer, related, slot, kind, ordinal, ordinal});
    };
    for (const auto& region : source.layerRegions)
    {
        if (!(region.flags & 8))
        {
            add(region.layer, 0, 1, 0, UINT32_MAX);
        }
    }
    auto touch = [&](uint64_t layer, uint32_t ordinal)
    {
        if (!layer)
        {
            return;
        }
        size_t key = size_t(layer) * 4 + 1;
        for (size_t steps = 0; key < target.targetLookup.size() && target.targetLookup[key] == UINT32_MAX &&
                               steps < source.maskCopySources.size();
             steps++)
        {
            if (layer >= source.maskCopySources.size() || !source.maskCopySources[layer])
            {
                return;
            }
            layer = source.maskCopySources[layer];
            key = size_t(layer) * 4 + 1;
        }
        if (key >= target.targetLookup.size() || target.targetLookup[key] == UINT32_MAX)
        {
            return;
        }
        auto& request = requests[target.targetLookup[key]];
        request.first_operation = std::min(request.first_operation, ordinal);
        request.last_operation =
            request.last_operation == UINT32_MAX ? ordinal : std::max(request.last_operation, ordinal);
    };
    if (!target.operations.empty() && target.operations.front().kind == URUI_PLAN_ROOT_COPY)
    {
        requests.push_back({0, 0, -2, 5, 0, uint32_t(target.operations.size() - 1)});
    }
    // 能直接读取当前颜色附件时，独立 CSS 图层不再需要背景快照。
    auto physicalLayer = [&](uint64_t layer)
    {
        for (size_t steps = 0; layer < source.maskCopySources.size() && source.maskCopySources[layer]; steps++)
        {
            if (steps >= source.maskCopySources.size())
            {
                throw std::out_of_range("cyclic target alias");
            }
            layer = source.maskCopySources[layer];
        }
        return layer;
    };
    auto fetchBlend = [&](const auto& layer)
    {
        return (source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) && layer.source &&
               physicalLayer(layer.source) != physicalLayer(layer.target);
    };
    uint32_t rootCapture = UINT32_MAX;
    auto captureRoot = [&](uint32_t ordinal)
    {
        if (rootCapture == UINT32_MAX)
        {
            rootCapture = uint32_t(requests.size());
            requests.push_back({0, 0, -3, 6, ordinal, ordinal});
        }
        else
        {
            requests[rootCapture].last_operation = ordinal;
        }
    };
    const bool screenWithoutWork = (source.resourceFlags & URUI_PLAN_RESOURCE_SCREEN_OUTPUT) &&
                                   (target.operations.empty() || target.operations.front().kind != URUI_PLAN_ROOT_COPY);
    uint64_t activeLayer = 0;
    bool inRaster = false;
    for (uint32_t ordinal = 0; ordinal < target.operations.size(); ordinal++)
    {
        const auto& operation = target.operations[ordinal];
        const bool transient = inRaster || (operation.reason & URUI_PLAN_RASTER_BEGIN);
        inRaster = transient && !(operation.reason & URUI_PLAN_RASTER_END);
        // 完整根快照由 Unity 标准 Blit 同时解析和归一化方向，不需要额外全屏中转。
        // 无工作目标时的局部屏幕读取仍在对应图层操作处声明解析目标。
        if (operation.kind != URUI_PLAN_LAYER)
        {
            const auto layer = activeLayer;
            touch(layer, ordinal);
            if (operation.kind == URUI_PLAN_GEOMETRY && (source.resourceFlags & URUI_PLAN_RESOURCE_ENCODED_RASTER))
            {
                uint32_t domain = 0;
                if (operation.texture_count)
                {
                    for (const auto& texture : source.resources)
                    {
                        if (texture.texture == operation.textures[0])
                        {
                            domain = texture.domain;
                            break;
                        }
                    }
                }
                const bool fetch = (source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) &&
                                   (layer ? (source.resourceFlags & URUI_PLAN_RESOURCE_HDR_CAMERA)
                                          : (source.resourceFlags & URUI_PLAN_RESOURCE_HIGH_PRECISION_ROOT));
                if (!domain && !fetch)
                {
                    add(layer, -1, transient ? 7 : 2, 0, ordinal);
                }
            }
            continue;
        }
        const auto& layer = source.layers[operation.layer_index];
        touch(layer.source, ordinal);
        touch(layer.target, ordinal);
        touch(layer.mask, ordinal);
        if (layer.kind == URUI_LAYER_PUSH || layer.kind == URUI_LAYER_POP ||
            (layer.kind == URUI_LAYER_COMPOSITE && layer.source && layer.source != layer.target))
        {
            activeLayer = layer.target;
        }
        if (operation.reason & 1)
        {
            continue;
        }
        if (screenWithoutWork && layer.kind == URUI_LAYER_COMPOSITE &&
            (!layer.source ||
             (!layer.target && layer.blend == 0 && (source.resourceFlags & URUI_PLAN_RESOURCE_LINEAR_COLOR) &&
              !fetchBlend(layer) && !(target.targetCapabilities[layer.source] & 1))))
        {
            captureRoot(ordinal);
        }
        if (layer.kind == URUI_LAYER_APPLY_MASK)
        {
            const bool element = operation.layer_index < source.effectIndices.size() &&
                                 source.effectIndices[operation.layer_index] >= 0 &&
                                 source.effects[source.effectIndices[operation.layer_index]].kind == 3;
            // 自遮罩可直接读写当前颜色附件；不交换内容租约，也不保留无用的写回目标。
            const bool maskFetch = layer.blend == 1 && (source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH);
            if (!element && !maskFetch)
            {
                add(layer.source, 1, 3, 0, ordinal);
            }
            if (layer.blend != 1)
            {
                add(layer.source, 2, 3, 0, ordinal);
            }
        }
        if (layer.kind == URUI_LAYER_COMPOSITE && layer.blend == 0 && layer.source &&
            (source.resourceFlags & URUI_PLAN_RESOURCE_LINEAR_COLOR) && !fetchBlend(layer))
        {
            if (!(target.targetCapabilities[layer.source] & 1))
            {
                add(layer.source, 3 + int32_t(operation.layer_index), 4, layer.target, ordinal);
                if (layer.source == layer.target)
                {
                    add(layer.source, 1, 3, 0, ordinal);
                }
            }
        }
    }
    for (auto& request : requests)
    {
        if (request.first_operation == UINT32_MAX)
        {
            request.first_operation = request.last_operation = 0;
        }
    }
}
