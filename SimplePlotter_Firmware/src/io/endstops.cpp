// SimplePlotter_Firmware/src/io/endstops.cpp

#include "endstops.h"

Endstops endstops; // Global instance definition

Endstops::Endstops() :
    _x_min_config({X_MIN_PIN, ENDSTOP_X_MIN_INVERTING, ENDSTOP_X_MIN_PULLUP, ENDSTOP_DEBOUNCE_MS}),
    _y_min_config({Y_MIN_PIN, ENDSTOP_Y_MIN_INVERTING, ENDSTOP_Y_MIN_PULLUP, ENDSTOP_DEBOUNCE_MS}),
    _z_min_config({Z_MIN_PIN, ENDSTOP_Z_MIN_INVERTING, ENDSTOP_Z_MIN_PULLUP, ENDSTOP_DEBOUNCE_MS})
{
    // Constructor initializes config structs
}

void Endstops::init() {
    setupEndstopPin(_x_min_config);
    setupEndstopPin(_y_min_config);
    setupEndstopPin(_z_min_config);

    // Initialize debounce state from actual pin readings so first isTriggered()
    // call doesn't spuriously reset the debounce timer
    _last_stable_raw_state[0] = getPinTriggeredState(_x_min_config);
    _last_stable_raw_state[1] = getPinTriggeredState(_y_min_config);
    _last_stable_raw_state[2] = getPinTriggeredState(_z_min_config);
    _debounced_triggered_state[0] = _last_stable_raw_state[0];
    _debounced_triggered_state[1] = _last_stable_raw_state[1];
    _debounced_triggered_state[2] = _last_stable_raw_state[2];
}

void Endstops::setupEndstopPin(const EndstopConfig& config) {
    if (config.pullup) {
        pinMode(config.pin, INPUT_PULLUP);
    } else {
        pinMode(config.pin, INPUT);
    }
}

// Helper to get raw digital read, inverted if necessary based on config.inverting
// Returns true if the endstop is considered "triggered" based on its config.
bool Endstops::getPinTriggeredState(const EndstopConfig& config) const {
    bool raw_read_low = (digitalRead(config.pin) == LOW); // True if pin is LOW
    return config.inverting ? raw_read_low : !raw_read_low; // If inverting (NC), LOW is triggered. If not (NO), HIGH is triggered.
}


// Read raw state of an endstop pin (no debouncing)
// Returns true if the endstop is considered "triggered" based on its config.
bool Endstops::getRawState(char axis) const {
    const EndstopConfig* config = nullptr;
    if (axis == 'X') config = &_x_min_config;
    else if (axis == 'Y') config = &_y_min_config;
    else if (axis == 'Z') config = &_z_min_config;
    
    if (config == nullptr) return false; // Invalid axis

    return getPinTriggeredState(*config);
}

// Check if a specific endstop is currently triggered (debounced)
// Returns true if the endstop is considered "triggered" based on its config and debouncing.
bool Endstops::isTriggered(char axis) { // No longer const
    const EndstopConfig* config = nullptr;
    int axis_idx = -1;
    if (axis == 'X') { config = &_x_min_config; axis_idx = 0; }
    else if (axis == 'Y') { config = &_y_min_config; axis_idx = 1; }
    else if (axis == 'Z') { config = &_z_min_config; axis_idx = 2; }
    
    if (config == nullptr || axis_idx == -1) return false; // Invalid axis

    bool current_raw_triggered_state = getPinTriggeredState(*config); // Get raw state, already inverted as per config

    // If the raw state has changed from the last time we checked its 'stable' state
    if (current_raw_triggered_state != _last_stable_raw_state[axis_idx]) {
        _last_debounce_time[axis_idx] = millis(); // Reset the debounce timer
        _last_stable_raw_state[axis_idx] = current_raw_triggered_state; // Update the last observed raw state
    }

    // If the current state has been stable for longer than the debounce time
    if ((millis() - _last_debounce_time[axis_idx]) > config->debounce_ms) {
        _debounced_triggered_state[axis_idx] = current_raw_triggered_state; // Update the debounced state
    }

    return _debounced_triggered_state[axis_idx];
}
