// SimplePlotter_Firmware/src/io/endstops.h

#ifndef ENDSTOPS_H
#define ENDSTOPS_H

#include <Arduino.h>
#include "../config.h" // For endstop pin definitions and configuration

// Configuration for a single endstop
struct EndstopConfig {
    uint8_t pin;
    bool inverting;      // true if NC (normally closed), false if NO
    bool pullup;         // enable internal pullup resistor
    uint8_t debounce_ms; // switch debounce time
};

class Endstops {
public:
    Endstops();

    void init();

    // Check if a specific endstop is currently triggered (debounced)
    bool isTriggered(char axis); 

    // Read raw state of an endstop pin (no debouncing, inverted as per config)
    bool getRawState(char axis) const;

private:
    EndstopConfig _x_min_config;
    EndstopConfig _y_min_config;
    EndstopConfig _z_min_config;

    // Internal debouncing state (non-const part of the class)
    unsigned long _last_debounce_time[3] = {0}; // For X, Y, Z
    bool _last_stable_raw_state[3] = {false, false, false}; // Last observed raw triggered state
    bool _debounced_triggered_state[3] = {false, false, false}; // Debounced triggered state
    
    // Helper to initialize a single endstop pin
    void setupEndstopPin(const EndstopConfig& config);
    
    // Helper to get raw digital read, inverted if necessary
    bool getPinTriggeredState(const EndstopConfig& config) const;
};

extern Endstops endstops; // Global instance

#endif // ENDSTOPS_H
