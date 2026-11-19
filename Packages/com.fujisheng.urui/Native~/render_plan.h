#pragma once
#include "urui.h"
#include <array>
#include <vector>
#include <cstring>
#include <limits>
struct RenderSerial
{
    uint64_t value = 0;
    bool exhausted = false;
    uint64_t Next()
    {
        if (exhausted || value == std::numeric_limits<uint64_t>::max())
        {
            exhausted = true;
            return 0;
        }
        return ++value;
    }
};
// Independent exact plan and resource versions. Not a DOM dirty approximation.
class RenderPlan
{
    RenderSerial& serial;
    std::vector<URUICommand> previous;
    std::vector<std::array<float, 16>> previous_matrices;
    uint64_t epoch = 0;
    uint64_t commands = 0;
    uint64_t resources = 0;
    URUI_DEBUG_CODE(uint64_t compared = 0;)
    bool supported = true;
    bool complete = false;
    bool have_previous = false;
    void ClearPrevious()
    {
        std::vector<URUICommand>().swap(previous);
        std::vector<std::array<float, 16>>().swap(previous_matrices);
        have_previous = false;
    }

public:
    explicit RenderPlan(RenderSerial& source) : serial(source)
    {
        epoch = serial.Next();
        commands = serial.Next();
        resources = serial.Next();
    }
    void ResourceChanged()
    {
        resources = serial.Next();
        complete = false;
    }
    void NewDocument()
    {
        epoch = serial.Next();
        complete = false;
        supported = true;
        URUI_DEBUG_CODE(compared = 0;)
        ClearPrevious();
    }
    void DocumentsChanged()
    {
        complete = false;
    }
    void Unsupported()
    {
        supported = false;
        complete = false;
        ClearPrevious();
    }
    void ReuseRecording()
    {
        // 复用帧不进行数组比较；诊断不能继续报告上次重录的比较字节数。
        URUI_DEBUG_CODE(compared = 0;)
    }
    void Begin(std::vector<URUICommand>& current, std::vector<std::array<float, 16>>& matrices)
    {
        complete = false;
        URUI_DEBUG_CODE(compared = 0;)
        if (supported)
        {
            previous.swap(current);
            previous_matrices.swap(matrices);
        }
        current.clear();
        matrices.clear();
    }
    void Finish(const std::vector<URUICommand>& current, const std::vector<std::array<float, 16>>& matrices)
    {
        if (!supported)
        {
            return;
        }
        if (current.size() != matrices.size())
        {
            complete = false;
            return;
        }
        bool same = have_previous && previous.size() == current.size() && previous_matrices.size() == matrices.size();
        if (same && !current.empty())
        {
            URUI_DEBUG_CODE(compared += current.size() * sizeof(URUICommand);)
            same = std::memcmp(previous.data(), current.data(), current.size() * sizeof(URUICommand)) == 0;
        }
        if (same && !matrices.empty())
        {
            URUI_DEBUG_CODE(compared += matrices.size() * sizeof(matrices[0]);)
            same = std::memcmp(previous_matrices.data(), matrices.data(), matrices.size() * sizeof(matrices[0])) == 0;
        }
        if (!same)
        {
            commands = serial.Next();
        }
        have_previous = true;
        complete = true;
    }
    URUIFrameStamp Snapshot(size_t count) const
    {
        URUIFrameStamp out{};
        out.epoch = epoch;
        out.commands = commands;
        out.resources = resources;
        URUI_DEBUG_CODE(out.retained_bytes = previous.capacity() * sizeof(URUICommand) +
                                             previous_matrices.capacity() * sizeof(std::array<float, 16>);)
        URUI_DEBUG_CODE(out.compare_bytes = compared;)
        if (count <= std::numeric_limits<uint32_t>::max())
        {
            out.count = static_cast<uint32_t>(count);
        }
        if (!supported)
        {
            out.flags = 2;
        }
        else if (complete && !serial.exhausted && count <= std::numeric_limits<uint32_t>::max())
        {
            out.flags = 1;
        }
        return out;
    }
};
