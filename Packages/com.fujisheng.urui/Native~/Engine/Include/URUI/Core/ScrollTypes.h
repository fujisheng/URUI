#pragma once

namespace URUI
{

    enum class ScrollBehavior
    {
        // Scroll using the context's configured setting.
        Auto,
        // Scroll using a smooth animation.
        Smooth,
        // Scroll instantly.
        Instant,
    };

    enum class ScrollAlignment
    {
        // Align to the top or left edge of the scroll container.
        Start,
        // Align to the center of the scroll container.
        Center,
        // Align to the bottom or right edge of the scroll container.
        End,
        // Align with minimal scroll change.
        Nearest,
        // Do not scroll if already in view, otherwise align to the center of the scroll container.
        Adaptive,
    };

    enum class ScrollParentage
    {
        // Scroll all ancestor scroll containers as needed.
        All,
        // Scroll only the closest scroll container.
        Closest,
    };

    /**
    Defines behavior of Element::ScrollIntoView.
 */
    struct ScrollIntoViewOptions
    {
        ScrollIntoViewOptions(ScrollAlignment vertical = ScrollAlignment::Start,
                              ScrollAlignment horizontal = ScrollAlignment::Nearest,
                              ScrollBehavior behavior = ScrollBehavior::Instant,
                              ScrollParentage parentage = ScrollParentage::All)
            : vertical(vertical), horizontal(horizontal), behavior(behavior), parentage(parentage)
        {
        }
        ScrollAlignment vertical;
        ScrollAlignment horizontal;
        ScrollBehavior behavior;
        ScrollParentage parentage;
    };

}
