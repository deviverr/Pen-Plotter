#include "potentiometer.h"

Potentiometer potentiometer;

void Potentiometer::init() {
    pinMode(POT_PIN, INPUT);
    // Pre-fill samples
    int val = analogRead(POT_PIN);
    for (int i = 0; i < POT_SAMPLES; i++) {
        _samples[i] = val;
    }
    _sampleIdx = 0;
    _filled = true;
    _speedPercent = map(val, 0, 1023, POT_MIN_SPEED, POT_MAX_SPEED);
}

void Potentiometer::update() {
    // Throttle updates to every 20ms (50 Hz) for very responsive updates
    unsigned long now = millis();
    if (now - _lastUpdate < 20) {
        return; // Skip update if too soon
    }
    _lastUpdate = now;

    _samples[_sampleIdx] = analogRead(POT_PIN);
    _sampleIdx = (_sampleIdx + 1) % POT_SAMPLES;

    // Compute average
    long sum = 0;
    for (int i = 0; i < POT_SAMPLES; i++) {
        sum += _samples[i];
    }
    int avg = (int)(sum / POT_SAMPLES);

    // Apply hysteresis: only update if change exceeds threshold to prevent display flicker
    int newPercent = map(avg, 0, 1023, POT_MIN_SPEED, POT_MAX_SPEED);
    if (abs(newPercent - _speedPercent) >= POT_HYSTERESIS_PERCENT) {
        _speedPercent = newPercent;
        _changed = true;
    }
}
