#pragma once

#include "Header.h"
#include "Types.h"
#include <utility>

namespace URUI {

class URUI_CORE_API Tween {
public:
	enum Type { None, Back, Bounce, Circular, Cubic, Elastic, Exponential, Linear, Quadratic, Quartic, Quintic, Sine, Callback, Count };
	enum Direction { In = 1, Out = 2, InOut = 3 };
	using CallbackFnc = float (*)(float);

	Tween(Type type = Linear, Direction direction = Out);
	Tween(Type type_in, Type type_out);
	Tween(CallbackFnc callback, Direction direction = In);
    static bool ParseLinear(const String& value, Tween& result);
    /// CSS cubic-bezier timing; finite control values, x coordinates in [0, 1].
    Tween(float x1, float y1, float x2, float y2);

	// Evaluate the Tweening function at point t in [0, 1].
	float operator()(float t) const;

	// Reverse direction of the tweening function.
	void reverse();

	bool operator==(const Tween& other) const;
	bool operator!=(const Tween& other) const;

	String to_string() const;

private:
	float tween(Type type, float t) const;
	float in(float t) const;
	float out(float t) const;
	float in_out(float t) const;

	Type type_in = None;
	Type type_out = None;
	CallbackFnc callback = nullptr;
    Vector<std::pair<float,float>> points;
    bool bezier = false;
    float x1 = 0, y1 = 0, x2 = 1, y2 = 1;
};

} // namespace URUI
