#ifndef POTENTIOMETER_H
#define POTENTIOMETER_H

#include <Arduino.h>
#include "../config.h"

#define POT_SAMPLES 32
#define POT_HYSTERESIS_PERCENT 1

class Potentiometer {
public:
    void init();
    void update(); // Call in loop(), reads and averages
    int getSpeedPercent() const { return _speedPercent; }
    bool hasChanged() { bool c = _changed; _changed = false; return c; }

private:
    int _samples[POT_SAMPLES];
    int _sampleIdx = 0;
    int _speedPercent = 100;
    bool _filled = false;
    bool _changed = false;
    unsigned long _lastUpdate = 0;
};

extern Potentiometer potentiometer;

#endif // POTENTIOMETER_H
