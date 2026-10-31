#if defined(URUI_DEBUG)
// Included in the bridge's private namespace; never compiled in production.
inline int ApplyTypedTransformTimed(Rml::Element* el, float x, float y, float sx, float sy)
{
    const bool measure = true;
    auto property = [&]()
    {
        ProfileScope scope(typed_probe.construct_ns, true);
        ++typed_probe.clock_pairs;
        Rml::Transform::PrimitiveList primitives;
        primitives.reserve(2);
        primitives.emplace_back(Rml::Transforms::Translate2D(x, y, Rml::Unit::PX));
        primitives.emplace_back(Rml::Transforms::Scale2D(sx, sy));
        return Rml::Transform::MakeProperty(std::move(primitives));
    }();
    // Same primitives and normal dirty/transition semantics, including repeat writes.
    bool ok;
    {
        ProfileScope scope(typed_probe.apply_ns, measure);
        if (measure)
            ++typed_probe.clock_pairs;
        ok = el->SetProperty(Rml::PropertyId::Transform, property);
    }
    if (!ok)
    {
        error_message = "RmlUi rejected typed transform";
        return -2;
    }
    error_message.clear();
    return 1;
}

#endif
