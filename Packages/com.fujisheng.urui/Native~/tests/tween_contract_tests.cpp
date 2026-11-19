#include <URUI/Core/Tween.h>
#include <cassert>
#include <cmath>
#include <iostream>
int main()
{
    URUI::Tween standard(.25f, .1f, .25f, 1.f);
    assert(standard(0) == 0 && standard(1) == 1);
    assert(std::abs(standard(.5f) - .802403f) < .00005f);
    const float forward = standard(.8f);
    standard.reverse();
    assert(std::abs(standard(.2f) - (1 - forward)) < .00005f);
    URUI::Tween linear(0.f, 0.f, 1.f, 1.f);
    for (int i = 0; i <= 20; i++)
    {
        assert(std::abs(linear(i / 20.f) - i / 20.f) < .00001f);
    }
    assert(linear.to_string().find("cubic-bezier") != URUI::String::npos);
    URUI::Tween piece;
    assert(URUI::Tween::ParseLinear("linear(0, 0.5 25% 75%, 1)", piece));
    assert(piece(.25f) == .5f && piece(.5f) == .5f && piece(.75f) == .5f);
    assert(std::abs(piece(-.25f) + .5f) < .00001f);
    const float prior = piece(.1f);
    piece.reverse();
    assert(std::abs(piece(.9f) - (1 - prior)) < .00001f);
    assert(URUI::Tween::ParseLinear("linear(0 20%, .5 10%, 1)", piece));
    assert(piece(.2f) == .5f);
    assert(URUI::Tween::ParseLinear("linear(1,1)", piece));
    assert(piece(0) == 1 && piece(.5f) == 1);
    assert(!URUI::Tween::ParseLinear("linear(0)", piece));
    assert(!URUI::Tween::ParseLinear("linear(0, nan)", piece));
    assert(!URUI::Tween::ParseLinear("linear(0 1px,1)", piece));
    std::cout << "CSS cubic-bezier timing contracts passed" << std::endl;
}
