#if defined(URUI_DEBUG)
// Included in the bridge's private namespace; never compiled in production.
inline int ApplyTypedTransformTimed(URUI::Element* el, float x, float y, float sx, float sy)
{
    const bool measure = true;
    auto property = [&]()
    {
        ProfileScope scope(typed_probe.construct_ns, true);
        ++typed_probe.clock_pairs;
        URUI::Transform::PrimitiveList primitives;
        primitives.reserve(2);
        primitives.emplace_back(URUI::Transforms::Translate2D(x, y, URUI::Unit::PX));
        primitives.emplace_back(URUI::Transforms::Scale2D(sx, sy));
        return URUI::Transform::MakeProperty(std::move(primitives));
    }();
    // Same primitives and normal dirty/transition semantics, including repeat writes.
    bool ok;
    {
        ProfileScope scope(typed_probe.apply_ns, measure);
        if (measure)
        {
            ++typed_probe.clock_pairs;
        }
        ok = el->SetProperty(URUI::PropertyId::Transform, property);
    }
    if (!ok)
    {
        error_message = "URUI rejected typed transform";
        return -2;
    }
    error_message.clear();
    return 1;
}

#endif
