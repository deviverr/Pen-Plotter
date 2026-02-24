// timing.h - Millis/micros helpers
// SimplePlotter Firmware v1.0

#ifndef TIMING_H
#define TIMING_H

#include <Arduino.h>

namespace Timing {
    inline uint32_t now() { return millis(); }
    inline uint32_t microsNow() { return micros(); }
}

#endif // TIMING_H
