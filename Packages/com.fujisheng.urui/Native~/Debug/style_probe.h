#pragma once
#if defined(URUI_DEBUG)
// Diagnostic-only hooks. Never cache an Element pointer; active exists only
// during the synchronous urui_set_style call on the calling thread.
#include "../urui.h"
#include <URUI/Core/Transform.h>
#include <chrono>
namespace urui_style_probe
{
    extern thread_local UruiStyleProbe total;
    extern thread_local int mode;
    extern thread_local UruiStyleProbe* active;
    struct Scope
    {
        uint64_t* out;
        std::chrono::steady_clock::time_point start;
        explicit Scope(uint64_t* p) : out(p)
        {
            if (out)
                start = std::chrono::steady_clock::now();
        }
        ~Scope()
        {
            Stop();
        }
        void Stop()
        {
            if (out)
            {
                *out += std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - start)
                            .count();
                out = nullptr;
            }
        }
    };
    // 严格 CSS 校验可能提前返回，所有拒绝都必须进入解析计数和耗时。
    struct ParseScope
    {
        UruiStyleProbe* probe = active;
        Scope timer{probe ? &probe->parse_ns : nullptr};
        bool accepted = false;
        ParseScope() { if (probe) ++probe->parsed_calls; }
        ~ParseScope() { if (probe && !accepted) ++probe->rejected_calls; }
        void Complete() { accepted = true; timer.Stop(); }
    };
    // Exact comparison of the existing inline declaration, not computed style.
    // Complex unknown representations are counted unsupported, never equal.
    inline bool Supported(const URUI::Property& a)
    {
        if (a.unit == URUI::Unit::TRANSFORM)
        {
            auto t = a.Get<URUI::TransformPtr>();
            if (!t)
                return true;
            for (int i = 0; i < t->GetNumPrimitives(); ++i)
            {
                auto k = t->GetPrimitive(i).type;
                if (k != URUI::TransformPrimitive::TRANSLATE2D && k != URUI::TransformPrimitive::SCALE2D &&
                    k != URUI::TransformPrimitive::MATRIX2D)
                    return false;
            }
            return true;
        }
        switch (a.value.GetType())
        {
        case URUI::Variant::FLOAT:
        case URUI::Variant::INT:
        case URUI::Variant::STRING:
        case URUI::Variant::COLOURB:
        case URUI::Variant::COLOURF:
            return true;
        default:
            return false;
        }
    }
    inline bool Equal(const URUI::Property& a, const URUI::Property& b, bool& supported)
    {
        supported = Supported(a) && Supported(b);
        if (!supported)
            return false;
        if (a.priority.originImportance != b.priority.originImportance)
            return false;
        if (a.unit != b.unit)
            return false;
        if (a.value.GetType() != b.value.GetType())
            return false;
        if (a.unit != URUI::Unit::TRANSFORM)
        {
            switch (a.value.GetType())
            {
            case URUI::Variant::FLOAT:
            case URUI::Variant::INT:
            case URUI::Variant::STRING:
            case URUI::Variant::COLOURB:
            case URUI::Variant::COLOURF:
                return a == b;
            default:
                supported = false;
                return false;
            }
        }
        auto x = a.Get<URUI::TransformPtr>(), y = b.Get<URUI::TransformPtr>();
        if (!x || !y)
            return x == y;
        if (x->GetNumPrimitives() != y->GetNumPrimitives())
            return false;
        bool equal = true;
        for (int i = 0; i < x->GetNumPrimitives(); ++i)
        {
            auto& p = x->GetPrimitive(i);
            auto& q = y->GetPrimitive(i);
            if (p.type != q.type)
                return false;
            switch (p.type)
            {
            case URUI::TransformPrimitive::TRANSLATE2D:
                equal &= p.translate_2d.values == q.translate_2d.values;
                break;
            case URUI::TransformPrimitive::SCALE2D:
                equal &= p.scale_2d.values == q.scale_2d.values;
                break;
            case URUI::TransformPrimitive::MATRIX2D:
                equal &= p.matrix_2d.values == q.matrix_2d.values;
                break;
            default:
                supported = false;
                return false;
            }
        }
        return equal;
    }
} // namespace urui_style_probe

#endif
