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
    std::vector<UruiCommand> previous;
    std::vector<std::array<float, 16>> previous_matrices;
    uint64_t epoch = 0, commands = 0, resources = 0;
    URUI_DEBUG_CODE(uint64_t compared = 0;)
    bool enabled = false, supported = true, complete = false, have_previous = false;
    void ClearPrevious()
    {
        std::vector<UruiCommand>().swap(previous);
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
    void Enable(bool value)
    {
        if (value == enabled)
            return;
        enabled = value;
        complete = false;
        ClearPrevious();
        commands = serial.Next();
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
    void Unsupported()
    {
        supported = false;
        complete = false;
        ClearPrevious();
    }
    void Begin(std::vector<UruiCommand>& current, std::vector<std::array<float, 16>>& matrices)
    {
        complete = false;
        URUI_DEBUG_CODE(compared = 0;)
        if (enabled && supported)
        {
            previous.swap(current);
            previous_matrices.swap(matrices);
        }
        current.clear();
        matrices.clear();
    }
    void Finish(const std::vector<UruiCommand>& current, const std::vector<std::array<float, 16>>& matrices)
    {
        if (!enabled || !supported)
            return;
        if (current.size() != matrices.size())
        {
            complete = false;
            return;
        }
        bool same = have_previous && previous.size() == current.size() && previous_matrices.size() == matrices.size();
        if (same && !current.empty())
        {
            URUI_DEBUG_CODE(compared += current.size() * sizeof(UruiCommand);)
            same = std::memcmp(previous.data(), current.data(), current.size() * sizeof(UruiCommand)) == 0;
        }
        if (same && !matrices.empty())
        {
            URUI_DEBUG_CODE(compared += matrices.size() * sizeof(matrices[0]);)
            same = std::memcmp(previous_matrices.data(), matrices.data(), matrices.size() * sizeof(matrices[0])) == 0;
        }
        if (!same)
            commands = serial.Next();
        have_previous = true;
        complete = true;
    }
    UruiFrameStamp Snapshot(size_t count) const
    {
        UruiFrameStamp out{};
        out.epoch = epoch;
        out.commands = commands;
        out.resources = resources;
        URUI_DEBUG_CODE(out.retained_bytes = previous.capacity() * sizeof(UruiCommand) +
                                             previous_matrices.capacity() * sizeof(std::array<float, 16>);)
        URUI_DEBUG_CODE(out.compare_bytes = compared;)
        if (count <= std::numeric_limits<uint32_t>::max())
            out.count = static_cast<uint32_t>(count);
        if (enabled && !supported)
            out.flags = 2;
        else if (enabled && supported && complete && !serial.exhausted && count <= std::numeric_limits<uint32_t>::max())
            out.flags = 1;
        return out;
    }
};
