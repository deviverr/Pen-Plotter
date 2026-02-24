// math_utils.h - Fast math functions
// SimplePlotter Firmware v1.0

#ifndef MATH_UTILS_H
#define MATH_UTILS_H

#include <Arduino.h>

namespace MathUtils {
    inline float clamp(float val, float minVal, float maxVal) {
        return (val < minVal) ? minVal : (val > maxVal) ? maxVal : val;
    }
    inline float sqr(float x) { return x * x; }
    inline float dist(float x1, float y1, float x2, float y2) {
        return sqrt(sqr(x2 - x1) + sqr(y2 - y1));
    }
}

#endif // MATH_UTILS_H
