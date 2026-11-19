#include "../../Include/URUI/Core/Tween.h"
#include "../../Include/URUI/Core/StringUtilities.h"
#include <cmath>
#include "../../Include/URUI/Core/Math.h"
#include <utility>
#include <cstdlib>
#include <limits>
#include <algorithm>

namespace URUI
{

    namespace TweenFunctions
    {

        /**
	    Tweening functions below.
	    Partly based on http://libclaw.sourceforge.net/tweeners.html
	 */

        static inline float square(float t)
        {
            return t * t;
        }

        static float back(float t)
        {
            return t * t * (2.70158f * t - 1.70158f);
        }

        static float bounce(float t)
        {
            if (t > 1.f - 1.f / 2.75f)
            {
                return 1.f - 7.5625f * square(1.f - t);
            }
            else if (t > 1.f - 2.f / 2.75f)
            {
                return 1.0f - (7.5625f * square(1.f - t - 1.5f / 2.75f) + 0.75f);
            }
            else if (t > 1.f - 2.5f / 2.75f)
            {
                return 1.0f - (7.5625f * square(1.f - t - 2.25f / 2.75f) + 0.9375f);
            }
            return 1.0f - (7.5625f * square(1.f - t - 2.625f / 2.75f) + 0.984375f);
        }

        static float circular(float t)
        {
            return 1.f - Math::SquareRoot(1.f - t * t);
        }

        static float cubic(float t)
        {
            return t * t * t;
        }

        static float elastic(float t)
        {
            if (t == 0)
            {
                return t;
            }
            if (t == 1)
            {
                return t;
            }
            return -Math::Exp(7.24f * (t - 1.f)) * Math::Sin((t - 1.1f) * 2.f * Math::URUI_PI / 0.4f);
        }

        static float exponential(float t)
        {
            if (t == 0)
            {
                return t;
            }
            if (t == 1)
            {
                return t;
            }
            return Math::Exp(7.24f * (t - 1.f));
        }

        static float linear(float t)
        {
            return t;
        }

        static float quadratic(float t)
        {
            return t * t;
        }

        static float quartic(float t)
        {
            return t * t * t * t;
        }

        static float quintic(float t)
        {
            return t * t * t * t * t;
        }

        static float sine(float t)
        {
            return 1.f - Math::Cos(t * Math::URUI_PI * 0.5f);
        }

    }

    Tween::Tween(Type type, Direction direction)
    {
        if (direction & In)
        {
            type_in = type;
        }
        if (direction & Out)
        {
            type_out = type;
        }
    }
    Tween::Tween(Type type_in, Type type_out) : type_in(type_in), type_out(type_out)
    {
    }
    Tween::Tween(CallbackFnc callback, Direction direction) : callback(callback)
    {
        if (direction & In)
        {
            type_in = Callback;
        }
        if (direction & Out)
        {
            type_out = Callback;
        }
    }
    Tween::Tween(float x1, float y1, float x2, float y2) : bezier(true), x1(x1), y1(y1), x2(x2), y2(y2)
    {
    }
    bool Tween::ParseLinear(const String& value, Tween& result)
    {
        if (value.size() < 8 || value.substr(0, 7) != "linear(" || value.back() != ')')
        {
            return false;
        }
        StringList stops;
        StringUtilities::ExpandString(stops, value.substr(7, value.size() - 8), ',');
        if (stops.size() < 2)
        {
            return false;
        }
        Vector<std::pair<float, float>> parsed;
        float largest = -std::numeric_limits<float>::infinity();
        for (size_t i = 0; i < stops.size(); ++i)
        {
            const char* cursor = stops[i].c_str();
            char* end = nullptr;
            const float output = std::strtof(cursor, &end);
            if (end == cursor || !std::isfinite(output))
            {
                return false;
            }
            cursor = end;
            float inputs[2];
            int count = 0;
            while (true)
            {
                while (*cursor == ' ' || *cursor == '\t' || *cursor == '\n' || *cursor == '\r')
                {
                    ++cursor;
                }
                if (!*cursor)
                {
                    break;
                }
                if (count == 2)
                {
                    return false;
                }
                const float percent = std::strtof(cursor, &end);
                if (end == cursor || *end != '%' || !std::isfinite(percent))
                {
                    return false;
                }
                inputs[count++] = percent * .01f;
                cursor = end + 1;
            }
            if (count)
            {
                for (int j = 0; j < count; j++)
                {
                    largest = std::max(largest, inputs[j]);
                    parsed.emplace_back(largest, output);
                }
            }
            else if (i == 0)
            {
                largest = 0;
                parsed.emplace_back(0, output);
            }
            else if (i + 1 == stops.size())
            {
                largest = std::max(largest, 1.f);
                parsed.emplace_back(largest, output);
            }
            else
            {
                parsed.emplace_back(std::numeric_limits<float>::quiet_NaN(), output);
            }
        }
        for (size_t i = 1; i + 1 < parsed.size();)
        {
            if (!std::isnan(parsed[i].first))
            {
                ++i;
                continue;
            }
            size_t next = i + 1;
            while (next < parsed.size() && std::isnan(parsed[next].first))
            {
                ++next;
            }
            const float from = parsed[i - 1].first;
            const float to = parsed[next].first;
            const size_t previous = i - 1;
            for (; i < next; ++i)
            {
                parsed[i].first = from + (to - from) * float(i - previous) / float(next - previous);
            }
        }
        Tween tween;
        tween.points = std::move(parsed);
        result = std::move(tween);
        return true;
    }
    float Tween::operator()(float t) const
    {
        if (!points.empty())
        {
            size_t left = 0;
            size_t right = 1;
            if (t >= points.back().first)
            {
                right = points.size() - 1;
                left = right - 1;
            }
            else if (t >= points.front().first)
            {
                const auto next = std::upper_bound(points.begin(), points.end(), t,
                                                   [](float input, const auto& point) { return input < point.first; });
                right = size_t(next - points.begin());
                left = right - 1;
            }
            const float span = points[right].first - points[left].first;
            if (span == 0)
            {
                return t < points[left].first ? points[left].second : points[right].second;
            }
            return points[left].second + (points[right].second - points[left].second) * (t - points[left].first) / span;
        }
        if (bezier)
        {
            if (t <= 0)
            {
                return 0;
            }
            if (t >= 1)
            {
                return 1;
            }
            const auto cubic = [](float u, float a, float b)
            {
                const float v = 1 - u;
                return 3 * v * v * u * a + 3 * v * u * u * b + u * u * u;
            };
            float u = t;
            float lower = 0;
            float upper = 1;
            for (int i = 0; i < 16; i++)
            {
                const float delta = cubic(u, x1, x2) - t;
                if (std::abs(delta) < 1e-7f)
                {
                    break;
                }
                if (delta > 0)
                {
                    upper = u;
                }
                else
                {
                    lower = u;
                }
                const float v = 1 - u;
                const float slope = 3 * v * v * x1 + 6 * v * u * (x2 - x1) + 3 * u * u * (1 - x2);
                float next = slope > 1e-6f ? u - delta / slope : (lower + upper) * .5f;
                if (next <= lower || next >= upper)
                {
                    next = (lower + upper) * .5f;
                }
                u = next;
            }
            return cubic(u, y1, y2);
        }

        if (type_in != None && type_out == None)
        {
            return in(t);
        }
        if (type_in == None && type_out != None)
        {
            return out(t);
        }
        if (type_in != None && type_out != None)
        {
            return in_out(t);
        }
        return t;
    }

    void Tween::reverse()
    {
        if (!points.empty())
        {
            std::reverse(points.begin(), points.end());
            for (auto& point : points)
            {
                point.first = 1 - point.first;
                point.second = 1 - point.second;
            }
        }
        if (bezier)
        {
            const float a = 1 - x2;
            const float b = 1 - y2;
            x2 = 1 - x1;
            y2 = 1 - y1;
            x1 = a;
            y1 = b;
        }
        std::swap(type_in, type_out);
    }

    bool Tween::operator==(const Tween& other) const
    {
        return points == other.points && type_in == other.type_in && type_out == other.type_out &&
               callback == other.callback && bezier == other.bezier &&
               (!bezier || (x1 == other.x1 && y1 == other.y1 && x2 == other.x2 && y2 == other.y2));
    }

    bool Tween::operator!=(const Tween& other) const
    {
        return !(*this == other);
    }

    String Tween::to_string() const
    {
        if (!points.empty())
        {
            String text = "linear(";
            for (size_t i = 0; i < points.size(); ++i)
            {
                if (i)
                {
                    text += ",";
                }
                text += CreateString("%g %g%%", points[i].second, points[i].first * 100);
            }
            return text + ")";
        }
        if (bezier)
        {
            return CreateString("cubic-bezier(%g,%g,%g,%g)", x1, y1, x2, y2);
        }
        if ((type_in == Linear || type_in == None) && (type_out == Linear || type_out == None) &&
            (type_in == Linear || type_out == Linear))
        {
            return "linear";
        }
        static const Array<String, size_t(Count)> type_str = {{"none", "back", "bounce", "circular", "cubic", "elastic",
                                                               "exponential", "linear", "quadratic", "quartic",
                                                               "quintic", "sine", "callback"}};

        if (size_t(type_in) < type_str.size() && size_t(type_out) < type_str.size())
        {
            if (type_in == None && type_out == None)
            {
                return "none";
            }
            else if (type_in == type_out)
            {
                return type_str[size_t(type_in)] + String("-in-out");
            }
            else if (type_in == None)
            {
                return type_str[size_t(type_out)] + String("-out");
            }
            else if (type_out == None)
            {
                return type_str[size_t(type_in)] + String("-in");
            }
            else if (type_in != type_out)
            {
                return type_str[size_t(type_in)] + String("-in-") + type_str[size_t(type_out)] + String("-out");
            }
        }
        return "unknown";
    }

    float Tween::tween(Type type, float t) const
    {
        using namespace TweenFunctions;

        switch (type)
        {
        case Back:
            return back(t);
        case Bounce:
            return bounce(t);
        case Circular:
            return circular(t);
        case Cubic:
            return cubic(t);
        case Elastic:
            return elastic(t);
        case Exponential:
            return exponential(t);
        case Linear:
            return linear(t);
        case Quadratic:
            return quadratic(t);
        case Quartic:
            return quartic(t);
        case Quintic:
            return quintic(t);
        case Sine:
            return sine(t);
        case Callback:
            if (callback)
            {
                return (*callback)(t);
            }
            break;
        default:
            break;
        }
        return t;
    }

    float Tween::in(float t) const
    {
        return tween(type_in, t);
    }

    float Tween::out(float t) const
    {
        return 1.0f - tween(type_out, 1.0f - t);
    }

    float Tween::in_out(float t) const
    {
        if (t < 0.5f)
        {
            return tween(type_in, 2.0f * t) * 0.5f;
        }
        else
        {
            return 0.5f + out(2.0f * t - 1.0f) * 0.5f;
        }
    }

}
