#include "DecoratorGradient.h"
#include "CssImageGeometry.h"
#include "../../Include/URUI/Core/CallbackTexture.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include <cmath>
#include <functional>
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/Math.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "ComputeProperty.h"
#include "DecoratorShader.h"

namespace URUI
{

    namespace
    {
        struct GradientField
        {
            ColorStopList stops;
            std::function<float(Vector2f)> coordinate;
            bool repeating = false;
            bool degenerate = false;
        };
        struct GradientImagePart
        {
            Geometry geometry;
            CallbackTexture texture;
        };
        struct GradientImageData
        {
            Vector<GradientImagePart> parts;
        };

        ColourbPremultiplied SampleGradient(const GradientField& field, float position)
        {
            const auto& stops = field.stops;
            if (stops.empty())
            {
                return ColourbPremultiplied(0, 0);
            }
            if (field.repeating)
            {
                const float period = stops.back().position.number - stops.front().position.number;
                if (field.degenerate || period <= 1e-8f)
                {
                    // CSS 零周期按等距色标的加权平均绘制，不能任取末端颜色。
                    ColourbPremultiplied result(0, 0);
                    for (int channel = 0; channel < 4; ++channel)
                    {
                        float sum = 0;
                        for (size_t i = 1; i < stops.size(); ++i)
                        {
                            const float weight =
                                period > 1e-8f ? (stops[i].position.number - stops[i - 1].position.number) / period
                                               : 1.f / float(stops.size() - 1);
                            sum += .5f * weight * (stops[i - 1].color[channel] + stops[i].color[channel]);
                        }
                        result[channel] = byte(Math::Round(sum));
                    }
                    return result;
                }
                position = stops.front().position.number +
                           std::fmod(std::fmod(position - stops.front().position.number, period) + period, period);
            }
            if (std::isnan(position) || position >= stops.back().position.number)
            {
                return stops.back().color;
            }
            if (position < stops.front().position.number)
            {
                return stops.front().color;
            }
            for (size_t index = 1; index < stops.size(); ++index)
            {
                if (position < stops[index].position.number)
                {
                    const auto& from = stops[index - 1];
                    const auto& to = stops[index];
                    const float fraction =
                        (position - from.position.number) / (to.position.number - from.position.number);
                    ColourbPremultiplied result(0, 0);
                    for (int channel = 0; channel < 4; ++channel)
                    {
                        result[channel] =
                            byte(Math::Round((1 - fraction) * from.color[channel] + fraction * to.color[channel]));
                    }
                    return result;
                }
            }
            return stops.back().color;
        }

        template <class Prepare>
        DecoratorDataHandle GenerateGradientImage(Element* element, BoxArea paint_area, PropertyId image_property,
                                                  size_t layer, Prepare prepare)
        {
            auto* manager = element->GetRenderManager();
            if (!manager)
            {
                return 0;
            }
            auto data = MakeUnique<GradientImageData>();
            for (int box = 0; box < element->GetNumBoxes(); ++box)
            {
                Mesh mesh;
                Vector2f size;
                if (image_property != PropertyId::Invalid)
                {
                    const auto layout = ResolveCssImageLayout(element, image_property, layer, {}, false, box);
                    size = layout.size;
                    mesh = GenerateCssImageMesh(element, image_property, layer, {}, false, box);
                }
                else
                {
                    const auto render_box = element->GetRenderBox(paint_area, box);
                    size = render_box.GetFillSize();
                    MeshUtilities::GenerateBackground(mesh, render_box, ColourbPremultiplied(255));
                    if (size.x > 0 && size.y > 0)
                    {
                        for (auto& vertex : mesh.vertices)
                        {
                            vertex.tex_coord =
                                (vertex.position - render_box.GetBorderOffset() - render_box.GetFillOffset()) / size;
                        }
                    }
                }
                if (size.x <= 0 || size.y <= 0 || mesh.indices.empty())
                {
                    continue;
                }
                if (!std::isfinite(size.x) || !std::isfinite(size.y) || size.x > 8192 || size.y > 8192 ||
                    double(std::ceil(size.x)) * std::ceil(size.y) > 16u * 1024u * 1024u)
                {
                    return 0;
                }
                const Vector2i pixels{int(std::ceil(size.x)), int(std::ceil(size.y))};
                auto field = prepare(size);
                auto texture = manager->MakeCallbackTexture(
                    [field = std::move(field), size, pixels](const CallbackTextureInterface& output)
                    {
                        Vector<byte> rgba(size_t(pixels.x) * pixels.y * 4);
                        for (int y = 0; y < pixels.y; ++y)
                        {
                            for (int x = 0; x < pixels.x; ++x)
                            {
                                const Vector2f point{(x + .5f) * size.x / pixels.x, (y + .5f) * size.y / pixels.y};
                                const auto color = SampleGradient(field, field.coordinate(point));
                                for (int channel = 0; channel < 4; ++channel)
                                {
                                    rgba[4 * (size_t(y) * pixels.x + x) + channel] = color[channel];
                                }
                            }
                        }
                        return output.GenerateTexture(rgba, pixels);
                    });
                data->parts.push_back({manager->MakeGeometry(std::move(mesh)), std::move(texture)});
            }
            return reinterpret_cast<DecoratorDataHandle>(data.release());
        }
        void ReleaseGradientImage(DecoratorDataHandle handle)
        {
            delete reinterpret_cast<GradientImageData*>(handle);
        }
        void RenderGradientImage(Element* element, DecoratorDataHandle handle)
        {
            for (const auto& part : reinterpret_cast<GradientImageData*>(handle)->parts)
            {
                part.geometry.Render(element->GetAbsoluteOffset(BoxArea::Border), part.texture);
            }
        }
    }

    // Returns the point along the input line ('line_point', 'line_vector') closest to the input 'point'.
    static Vector2f IntersectionPointToLineNormal(const Vector2f point, const Vector2f line_point,
                                                  const Vector2f line_vector)
    {
        const Vector2f delta = line_point - point;
        return line_point - delta.DotProduct(line_vector) * line_vector;
    }

    /// Convert all color stop positions to normalized numbers.
    /// @param[in] element The element to resolve lengths against.
    /// @param[in] gradient_line_length The length of the gradient line, along which color stops are placed.
    /// @param[in] soft_spacing The desired minimum distance between stops to avoid aliasing, in normalized number units.
    /// @param[in] unresolved_stops
    /// @return A list of resolved color stops, all in number units.
    static ColorStopList ResolveColorStops(Element* element, const float gradient_line_length, const float soft_spacing,
                                           const ColorStopList& unresolved_stops)
    {
        ColorStopList stops = unresolved_stops;
        const int num_stops = (int)stops.size();

        // Resolve all lengths, percentages, and angles to numbers. After this step all stops with a unit other than Number are considered as Auto.
        for (ColorStop& stop : stops)
        {
            if (Any(stop.position.unit & Unit::LENGTH))
            {
                const float resolved_position = element->ResolveLength(stop.position);
                stop.position = NumericValue(resolved_position / gradient_line_length, Unit::NUMBER);
            }
            else if (stop.position.unit == Unit::PERCENT)
            {
                stop.position = NumericValue(stop.position.number * 0.01f, Unit::NUMBER);
            }
            else if (Any(stop.position.unit & Unit::ANGLE))
            {
                stop.position = NumericValue(ComputeAngle(stop.position) * (1.f / (2.f * Math::URUI_PI)), Unit::NUMBER);
            }
        }

        // Resolve auto positions of the first and last color stops.
        auto resolve_edge_stop = [](ColorStop& stop, float auto_to_number)
        {
            if (stop.position.unit != Unit::NUMBER)
            {
                stop.position = NumericValue(auto_to_number, Unit::NUMBER);
            }
        };
        resolve_edge_stop(stops[0], 0.f);
        resolve_edge_stop(stops[num_stops - 1], 1.f);

        // Ensures that color stop positions are strictly increasing, and have at least 1px spacing to avoid aliasing.
        auto nudge_stop = [prev_position = stops[0].position.number](ColorStop& stop, bool update_prev = true) mutable
        {
            stop.position.number = Math::Max(stop.position.number, prev_position);
            if (update_prev)
            {
                prev_position = stop.position.number;
            }
        };
        int auto_begin_i = -1;

        // Evenly space stops with sequential auto indices, and nudge stop positions to ensure strictly increasing positions.
        for (int i = 1; i < num_stops; i++)
        {
            ColorStop& stop = stops[i];
            if (stop.position.unit != Unit::NUMBER)
            {
                // Mark the first of any consecutive auto stops.
                if (auto_begin_i < 0)
                {
                    auto_begin_i = i;
                }
            }
            else if (auto_begin_i < 0)
            {
                // The stop has a definite position and there are no previous autos to handle, just ensure it is properly spaced.
                nudge_stop(stop);
            }
            else
            {
                // Space out all the previous auto stops, indices [auto_begin_i, i).
                nudge_stop(stop, false);
                const int num_auto_stops = i - auto_begin_i;
                const float t0 = stops[auto_begin_i - 1].position.number;
                const float t1 = stop.position.number;

                for (int j = 0; j < num_auto_stops; j++)
                {
                    const float fraction_along_t0_t1 = float(j + 1) / float(num_auto_stops + 1);
                    stops[j + auto_begin_i].position =
                        NumericValue(t0 + (t1 - t0) * fraction_along_t0_t1, Unit::NUMBER);
                    nudge_stop(stops[j + auto_begin_i]);
                }

                nudge_stop(stop);
                auto_begin_i = -1;
            }
        }

        // Ensures that stops are placed some minimum distance from each other to avoid aliasing, if possible.
        for (int i = 1; i < num_stops - 1; i++)
        {
            const float p0 = stops[i - 1].position.number;
            const float p1 = stops[i].position.number;
            const float p2 = stops[i + 1].position.number;
            float& new_position = stops[i].position.number;

            if (p1 - p0 < soft_spacing)
            {
                if (p2 - p0 < 2.f * soft_spacing)
                {
                    new_position = 0.5f * (p2 + p0);
                }
                else
                {
                    new_position = p0 + soft_spacing;
                }
            }
        }

        URUI_ASSERT(
            std::all_of(stops.begin(), stops.end(), [](auto&& stop) { return stop.position.unit == Unit::NUMBER; }));

        return stops;
    }

    DecoratorStraightGradient::DecoratorStraightGradient()
    {
    }

    DecoratorStraightGradient::~DecoratorStraightGradient()
    {
    }

    bool DecoratorStraightGradient::Initialise(const Direction in_direction, const Colourb in_start,
                                               const Colourb in_stop)
    {
        direction = in_direction;
        start = in_start;
        stop = in_stop;
        return true;
    }

    DecoratorDataHandle DecoratorStraightGradient::GenerateElementData(Element* element, BoxArea paint_area) const
    {
        const RenderBox render_box = element->GetRenderBox(paint_area);
        const ComputedValues& computed = element->GetComputedValues();
        const float opacity = 1.f;

        Mesh mesh;
        MeshUtilities::GenerateBackground(mesh, render_box, ColourbPremultiplied());

        ColourbPremultiplied colour_start = start.ToPremultiplied(opacity);
        ColourbPremultiplied colour_stop = stop.ToPremultiplied(opacity);

        const Vector2f offset = render_box.GetFillOffset();
        const Vector2f size = render_box.GetFillSize();

        Vector<Vertex>& vertices = mesh.vertices;

        if (direction == Direction::Horizontal)
        {
            for (int i = 0; i < (int)vertices.size(); i++)
            {
                const float t = Math::Clamp((vertices[i].position.x - offset.x) / size.x, 0.0f, 1.0f);
                vertices[i].colour = Math::RoundedLerp(t, colour_start, colour_stop);
            }
        }
        else if (direction == Direction::Vertical)
        {
            for (int i = 0; i < (int)vertices.size(); i++)
            {
                const float t = Math::Clamp((vertices[i].position.y - offset.y) / size.y, 0.0f, 1.0f);
                vertices[i].colour = Math::RoundedLerp(t, colour_start, colour_stop);
            }
        }

        Geometry* geometry = new Geometry(element->GetRenderManager()->MakeGeometry(std::move(mesh)));

        return reinterpret_cast<DecoratorDataHandle>(geometry);
    }

    void DecoratorStraightGradient::ReleaseElementData(DecoratorDataHandle element_data) const
    {
        delete reinterpret_cast<Geometry*>(element_data);
    }

    void DecoratorStraightGradient::RenderElement(Element* element, DecoratorDataHandle element_data) const
    {
        auto* data = reinterpret_cast<Geometry*>(element_data);
        data->Render(element->GetAbsoluteOffset(BoxArea::Border));
    }

    DecoratorStraightGradientInstancer::DecoratorStraightGradientInstancer()
    {
        ids.direction =
            RegisterProperty("direction", "horizontal").AddParser("keyword", "horizontal, vertical").GetId();
        ids.start = RegisterProperty("start-color", "#ffffff").AddParser("color").GetId();
        ids.stop = RegisterProperty("stop-color", "#ffffff").AddParser("color").GetId();
        RegisterShorthand("decorator", "direction, start-color, stop-color", ShorthandType::FallThrough);
    }

    DecoratorStraightGradientInstancer::~DecoratorStraightGradientInstancer()
    {
    }

    SharedPtr<Decorator>
    DecoratorStraightGradientInstancer::InstanceDecorator(const String& name, const PropertyDictionary& properties_,
                                                          const DecoratorInstancerInterface& /*interface_*/)
    {
        using Direction = DecoratorStraightGradient::Direction;
        Direction direction;
        if (name == "horizontal-gradient")
        {
            direction = Direction::Horizontal;
        }
        else if (name == "vertical-gradient")
        {
            direction = Direction::Vertical;
        }
        else
        {
            direction = (Direction)properties_.GetProperty(ids.direction)->Get<int>();
            Log::Message(
                Log::LT_WARNING,
                "Decorator syntax 'gradient(horizontal|vertical ...)' is deprecated, please replace with 'horizontal-gradient(...)' or "
                "'vertical-gradient(...)'");
        }

        Colourb start = properties_.GetProperty(ids.start)->Get<Colourb>();
        Colourb stop = properties_.GetProperty(ids.stop)->Get<Colourb>();

        auto decorator = MakeShared<DecoratorStraightGradient>();
        if (decorator->Initialise(direction, start, stop))
        {
            return decorator;
        }

        return nullptr;
    }

    DecoratorLinearGradient::DecoratorLinearGradient()
    {
    }

    DecoratorLinearGradient::~DecoratorLinearGradient()
    {
    }

    bool DecoratorLinearGradient::Initialise(bool in_repeating, Corner in_corner, float in_angle,
                                             const ColorStopList& in_color_stops)
    {
        repeating = in_repeating;
        corner = in_corner;
        angle = in_angle;
        color_stops = in_color_stops;
        return !color_stops.empty();
    }

    DecoratorDataHandle DecoratorLinearGradient::GenerateElementData(Element* element, BoxArea paint_area) const
    {
        return GenerateGradientImage(
            element, paint_area, css_image_property, css_image_layer,
            [this, element](Vector2f dimensions)
            {
                const auto shape = CalculateShape(dimensions);
                const auto delta = shape.p1 - shape.p0;
                const float square = delta.DotProduct(delta);
                GradientField field;
                field.stops = ResolveColorStops(element, std::max(shape.length, 1e-8f), 0.f, color_stops);
                field.repeating = repeating;
                field.degenerate = square <= 1e-8f;
                field.coordinate = [shape, delta, square](Vector2f point)
                { return square > 1e-8f ? (point - shape.p0).DotProduct(delta) / square : INFINITY; };
                return field;
            });
    }

    void DecoratorLinearGradient::ReleaseElementData(DecoratorDataHandle handle) const
    {
        ReleaseGradientImage(handle);
    }
    void DecoratorLinearGradient::RenderElement(Element* element, DecoratorDataHandle handle) const
    {
        RenderGradientImage(element, handle);
    }

    DecoratorLinearGradient::LinearGradientShape DecoratorLinearGradient::CalculateShape(Vector2f dim) const
    {
        using uint = unsigned int;
        const Vector2f corners[(int)Corner::Count] = {Vector2f(dim.x, 0), dim, Vector2f(0, dim.y), Vector2f(0, 0)};
        const Vector2f center = 0.5f * dim;

        uint quadrant = 0;
        Vector2f line_vector;

        if (corner == Corner::None)
        {
            // Find the target quadrant and unit vector for the given angle.
            quadrant = uint(Math::NormaliseAngle(angle) * (4.f / (2.f * Math::URUI_PI))) % 4u;
            line_vector = Vector2f(Math::Sin(angle), -Math::Cos(angle));
        }
        else
        {
            // Quadrant given by the corner, need to find the vector perpendicular to the line connecting the neighboring corners.
            quadrant = uint(corner);
            const Vector2f v_neighbors = (corners[(quadrant + 1u) % 4u] - corners[(quadrant + 3u) % 4u]).Normalise();
            line_vector = {v_neighbors.y, -v_neighbors.x};
        }

        const uint quadrant_opposite = (quadrant + 2u) % 4u;

        const Vector2f starting_point = IntersectionPointToLineNormal(corners[quadrant_opposite], center, line_vector);
        const Vector2f ending_point = IntersectionPointToLineNormal(corners[quadrant], center, line_vector);

        const float length = Math::Absolute(dim.x * line_vector.x) + Math::Absolute(-dim.y * line_vector.y);

        return LinearGradientShape{starting_point, ending_point, length};
    }

    DecoratorLinearGradientInstancer::DecoratorLinearGradientInstancer()
    {
        ids.angle = RegisterProperty("angle", "180deg").AddParser("angle").GetId();
        ids.direction_to = RegisterProperty("to", "unspecified").AddParser("keyword", "unspecified, to").GetId();
        // See Direction enum for keyword values.
        ids.direction_x = RegisterProperty("direction-x", "unspecified")
                              .AddParser("keyword", "unspecified=0, left=8, right=2")
                              .GetId();
        ids.direction_y = RegisterProperty("direction-y", "unspecified")
                              .AddParser("keyword", "unspecified=0, top=1, bottom=4")
                              .GetId();
        ids.color_stop_list = RegisterProperty("color-stops", "").AddParser("color_stop_list").GetId();

        RegisterShorthand("direction", "angle, to, direction-x, direction-y, direction-x", ShorthandType::FallThrough);
        RegisterShorthand("decorator", "direction?, color-stops#", ShorthandType::RecursiveCommaSeparated);
    }

    DecoratorLinearGradientInstancer::~DecoratorLinearGradientInstancer()
    {
    }

    SharedPtr<Decorator>
    DecoratorLinearGradientInstancer::InstanceDecorator(const String& name, const PropertyDictionary& properties_,
                                                        const DecoratorInstancerInterface& /*interface_*/)
    {
        const Property* p_angle = properties_.GetProperty(ids.angle);
        const Property* p_direction_to = properties_.GetProperty(ids.direction_to);
        const Property* p_direction_x = properties_.GetProperty(ids.direction_x);
        const Property* p_direction_y = properties_.GetProperty(ids.direction_y);
        const Property* p_color_stop_list = properties_.GetProperty(ids.color_stop_list);

        if (!p_angle || !p_direction_to || !p_direction_x || !p_direction_y || !p_color_stop_list)
        {
            return nullptr;
        }

        using Corner = DecoratorLinearGradient::Corner;
        Corner corner = Corner::None;
        float angle = 0.f;

        if (p_direction_to->Get<bool>())
        {
            const Direction direction = (Direction)(p_direction_x->Get<int>() | p_direction_y->Get<int>());
            switch (direction)
            {
            case Direction::Top:
                angle = 0.f;
                break;
            case Direction::Right:
                angle = 0.5f * Math::URUI_PI;
                break;
            case Direction::Bottom:
                angle = Math::URUI_PI;
                break;
            case Direction::Left:
                angle = 1.5f * Math::URUI_PI;
                break;
            case Direction::TopLeft:
                corner = Corner::TopLeft;
                break;
            case Direction::TopRight:
                corner = Corner::TopRight;
                break;
            case Direction::BottomRight:
                corner = Corner::BottomRight;
                break;
            case Direction::BottomLeft:
                corner = Corner::BottomLeft;
                break;
            case Direction::None:
            default:
                return nullptr;
                break;
            }
        }
        else
        {
            angle = ComputeAngle(p_angle->GetNumericValue());
        }

        if (p_color_stop_list->unit != Unit::COLORSTOPLIST)
        {
            return nullptr;
        }
        const ColorStopList& color_stop_list = p_color_stop_list->value.GetReference<ColorStopList>();
        const bool repeating = (name == "repeating-linear-gradient");

        auto decorator = MakeShared<DecoratorLinearGradient>();
        if (decorator->Initialise(repeating, corner, angle, color_stop_list))
        {
            return decorator;
        }

        return nullptr;
    }

    DecoratorRadialGradient::DecoratorRadialGradient()
    {
    }

    DecoratorRadialGradient::~DecoratorRadialGradient()
    {
    }

    bool DecoratorRadialGradient::Initialise(bool in_repeating, Shape in_shape, SizeType in_size_type,
                                             Vector2Numeric in_size, Vector2Numeric in_position,
                                             const ColorStopList& in_color_stops)
    {
        repeating = in_repeating;
        shape = in_shape;
        size_type = in_size_type;
        size = in_size;
        position = in_position;
        color_stops = in_color_stops;
        return !color_stops.empty();
    }

    DecoratorDataHandle DecoratorRadialGradient::GenerateElementData(Element* element, BoxArea paint_area) const
    {
        return GenerateGradientImage(element, paint_area, css_image_property, css_image_layer,
                                     [this, element](Vector2f dimensions)
                                     {
                                         const auto gradient = CalculateRadialGradientShape(element, dimensions);
                                         GradientField field;
                                         field.stops = ResolveColorStops(element, std::max(gradient.radius.x, 1e-8f),
                                                                         0.f, color_stops);
                                         field.repeating = repeating;
                                         field.degenerate = gradient.radius.x <= 1e-8f || gradient.radius.y <= 1e-8f;
                                         field.coordinate = [gradient](Vector2f point)
                                         {
                                             if (gradient.radius.x <= 1e-8f || gradient.radius.y <= 1e-8f)
                                             {
                                                 return float(INFINITY);
                                             }
                                             const auto relative = (point - gradient.center) / gradient.radius;
                                             return std::sqrt(relative.DotProduct(relative));
                                         };
                                         return field;
                                     });
    }

    void DecoratorRadialGradient::ReleaseElementData(DecoratorDataHandle handle) const
    {
        ReleaseGradientImage(handle);
    }
    void DecoratorRadialGradient::RenderElement(Element* element, DecoratorDataHandle handle) const
    {
        RenderGradientImage(element, handle);
    }

    DecoratorRadialGradient::RadialGradientShape
    DecoratorRadialGradient::CalculateRadialGradientShape(Element* element, Vector2f dimensions) const
    {
        RadialGradientShape result;
        result.center.x = element->ResolveNumericValue(position.x, dimensions.x);
        result.center.y = element->ResolveNumericValue(position.y, dimensions.y);
        const bool is_circle = (shape == Shape::Circle);

        auto Abs = [](Vector2f v) { return Vector2f{Math::Absolute(v.x), Math::Absolute(v.y)}; };
        auto d = dimensions;
        auto c = result.center;
        Vector2f r;

        switch (size_type)
        {
        case SizeType::ClosestSide:
        {
            r = Abs(Math::Min(c, d - c));
            result.radius = (is_circle ? Vector2f(Math::Min(r.x, r.y)) : r);
        }
        break;
        case SizeType::FarthestSide:
        {
            r = Abs(Math::Max(c, d - c));
            result.radius = (is_circle ? Vector2f(Math::Max(r.x, r.y)) : r);
        }
        break;
        case SizeType::ClosestCorner:
        case SizeType::FarthestCorner:
        {
            if (size_type == SizeType::ClosestCorner)
            {
                // Same as closest-side.
                r = Abs(Math::Min(c, d - c));
            }
            else
            {
                // Same as farthest-side.
                r = Abs(Math::Max(c, d - c));
            }

            if (is_circle)
            {
                result.radius = Vector2f(r.Magnitude());
            }
            else
            {
                // In case r.x ~= 0
                r = Math::Max(r, Vector2f(1));
                result.radius.x = Math::SquareRoot(2.f * r.x * r.x);
                result.radius.y = result.radius.x * (r.y / r.x);
            }
        }
        break;
        case SizeType::LengthPercentage:
        {
            result.radius.x = element->ResolveNumericValue(size.x, d.x);
            result.radius.y = (is_circle ? result.radius.x : element->ResolveNumericValue(size.y, d.y));
            result.radius = Abs(result.radius);
        }
        break;
        }

        result.radius = Math::Max(result.radius, Vector2f(1.f));
        return result;
    }

    DecoratorRadialGradientInstancer::DecoratorRadialGradientInstancer()
    {
        ids.ending_shape = RegisterProperty("ending-shape", "unspecified")
                               .AddParser("keyword", "circle, ellipse, unspecified")
                               .GetId();
        ids.size_x = RegisterProperty("size-x", "farthest-corner")
                         .AddParser("keyword", "closest-side, farthest-side, closest-corner, farthest-corner")
                         .AddParser("length_percent")
                         .GetId();
        ids.size_y = RegisterProperty("size-y", "unspecified")
                         .AddParser("keyword", "unspecified")
                         .AddParser("length_percent")
                         .GetId();

        RegisterProperty("at", "unspecified").AddParser("keyword", "at, unspecified");
        ids.position_x = RegisterProperty("position-x", "center")
                             .AddParser("keyword", "left, center, right")
                             .AddParser("length_percent")
                             .GetId();
        ids.position_y = RegisterProperty("position-y", "center")
                             .AddParser("keyword", "top, center, bottom")
                             .AddParser("length_percent")
                             .GetId();

        ids.color_stop_list = RegisterProperty("color-stops", "").AddParser("color_stop_list").GetId();

        RegisterShorthand("shape", "ending-shape, size-x, size-y, at, position-x, position-y, position-x",
                          ShorthandType::FallThrough);

        RegisterShorthand("decorator", "shape?, color-stops#", ShorthandType::RecursiveCommaSeparated);
    }

    DecoratorRadialGradientInstancer::~DecoratorRadialGradientInstancer()
    {
    }

    SharedPtr<Decorator>
    DecoratorRadialGradientInstancer::InstanceDecorator(const String& name, const PropertyDictionary& properties_,
                                                        const DecoratorInstancerInterface& /*interface_*/)
    {
        const Property* p_ending_shape = properties_.GetProperty(ids.ending_shape);
        const Property* p_size_x = properties_.GetProperty(ids.size_x);
        const Property* p_size_y = properties_.GetProperty(ids.size_y);
        Array<const Property*, 2> p_position = {properties_.GetProperty(ids.position_x),
                                                properties_.GetProperty(ids.position_y)};
        const Property* p_color_stop_list = properties_.GetProperty(ids.color_stop_list);

        if (!p_ending_shape || !p_size_x || !p_size_y || !p_position[0] || !p_position[1] || !p_color_stop_list)
        {
            return nullptr;
        }

        using SizeType = DecoratorRadialGradient::SizeType;
        using Shape = DecoratorRadialGradient::Shape;

        Shape shape = (Shape)p_ending_shape->Get<int>();
        if (shape == Shape::Unspecified)
        {
            const bool circle_sized = (Any(p_size_x->unit & Unit::LENGTH_PERCENT) && p_size_y->unit == Unit::KEYWORD);
            shape = (circle_sized ? Shape::Circle : Shape::Ellipse);
        }
        if (shape == Shape::Circle && (p_size_x->unit == Unit::PERCENT || p_size_y->unit != Unit::KEYWORD))
        {
            return nullptr;
        }

        SizeType size_type = {};
        Vector2Numeric size;
        if (p_size_x->unit == Unit::KEYWORD)
        {
            size_type = (SizeType)p_size_x->Get<int>();
        }
        else
        {
            size_type = SizeType::LengthPercentage;
            size.x = p_size_x->GetNumericValue();
            size.y = (p_size_y->unit == Unit::KEYWORD ? size.x : p_size_y->GetNumericValue());
        }

        const Vector2Numeric position = ComputePosition(p_position);
        const bool repeating = (name == "repeating-radial-gradient");

        if (p_color_stop_list->unit != Unit::COLORSTOPLIST)
        {
            return nullptr;
        }
        const ColorStopList& color_stop_list = p_color_stop_list->value.GetReference<ColorStopList>();

        auto decorator = MakeShared<DecoratorRadialGradient>();
        if (decorator->Initialise(repeating, shape, size_type, size, position, color_stop_list))
        {
            return decorator;
        }

        return nullptr;
    }

    DecoratorConicGradient::DecoratorConicGradient()
    {
    }

    DecoratorConicGradient::~DecoratorConicGradient()
    {
    }

    bool DecoratorConicGradient::Initialise(bool in_repeating, float in_angle, Vector2Numeric in_position,
                                            const ColorStopList& in_color_stops)
    {
        repeating = in_repeating;
        angle = in_angle;
        position = in_position;
        color_stops = in_color_stops;
        return !color_stops.empty();
    }

    DecoratorDataHandle DecoratorConicGradient::GenerateElementData(Element* element, BoxArea paint_area) const
    {
        return GenerateGradientImage(element, paint_area, css_image_property, css_image_layer,
                                     [this, element](Vector2f dimensions)
                                     {
                                         const Vector2f center{element->ResolveNumericValue(position.x, dimensions.x),
                                                               element->ResolveNumericValue(position.y, dimensions.y)};
                                         GradientField field;
                                         field.stops = ResolveColorStops(element, 1.f, 0.f, color_stops);
                                         field.repeating = repeating;
                                         const float rotation = angle;
                                         field.coordinate = [center, rotation](Vector2f point)
                                         {
                                             const auto relative = point - center;
                                             float turn = (std::atan2(relative.x, -relative.y) - rotation) /
                                                          (2.f * Math::URUI_PI);
                                             return turn - std::floor(turn);
                                         };
                                         return field;
                                     });
    }

    void DecoratorConicGradient::ReleaseElementData(DecoratorDataHandle handle) const
    {
        ReleaseGradientImage(handle);
    }
    void DecoratorConicGradient::RenderElement(Element* element, DecoratorDataHandle handle) const
    {
        RenderGradientImage(element, handle);
    }

    DecoratorConicGradientInstancer::DecoratorConicGradientInstancer()
    {
        RegisterProperty("from", "from").AddParser("keyword", "from");
        ids.angle = RegisterProperty("angle", "0deg").AddParser("angle").GetId();

        RegisterProperty("at", "unspecified").AddParser("keyword", "at, unspecified");
        ids.position_x = RegisterProperty("position-x", "center")
                             .AddParser("keyword", "left, center, right")
                             .AddParser("length_percent")
                             .GetId();
        ids.position_y = RegisterProperty("position-y", "center")
                             .AddParser("keyword", "top, center, bottom")
                             .AddParser("length_percent")
                             .GetId();

        ids.color_stop_list = RegisterProperty("color-stops", "").AddParser("color_stop_list", "angle").GetId();

        RegisterShorthand("shape", "from, angle, at, position-x, position-y, position-x", ShorthandType::FallThrough);
        RegisterShorthand("decorator", "shape?, color-stops#", ShorthandType::RecursiveCommaSeparated);
    }

    DecoratorConicGradientInstancer::~DecoratorConicGradientInstancer()
    {
    }

    SharedPtr<Decorator>
    DecoratorConicGradientInstancer::InstanceDecorator(const String& name, const PropertyDictionary& properties_,
                                                       const DecoratorInstancerInterface& /*interface_*/)
    {
        const Property* p_angle = properties_.GetProperty(ids.angle);
        Array<const Property*, 2> p_position = {properties_.GetProperty(ids.position_x),
                                                properties_.GetProperty(ids.position_y)};
        const Property* p_color_stop_list = properties_.GetProperty(ids.color_stop_list);

        if (!p_angle || !p_position[0] || !p_position[1] || !p_color_stop_list)
        {
            return nullptr;
        }

        const float angle = ComputeAngle(p_angle->GetNumericValue());
        const Vector2Numeric position = ComputePosition(p_position);
        const bool repeating = (name == "repeating-conic-gradient");

        if (p_color_stop_list->unit != Unit::COLORSTOPLIST)
        {
            return nullptr;
        }
        const ColorStopList& color_stop_list = p_color_stop_list->value.GetReference<ColorStopList>();

        auto decorator = MakeShared<DecoratorConicGradient>();
        if (decorator->Initialise(repeating, angle, position, color_stop_list))
        {
            return decorator;
        }

        return nullptr;
    }

}
