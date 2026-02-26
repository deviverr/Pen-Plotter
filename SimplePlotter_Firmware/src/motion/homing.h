// SimplePlotter_Firmware/src/motion/homing.h

#ifndef HOMING_H
#define HOMING_H

#include <Arduino.h>
#include "../config.h"
#include "stepper_control.h" // For moving steppers
#include "kinematics.h"      // For mm to steps conversion and max travel
#include "../io/endstops.h"  // For reading endstop states
#include "../io/serial_handler.h" // For error reporting

class Homing {
public:
    Homing();

    // Perform homing sequence for specified axis or all axes
    bool homeAxis(char axis); // 'X', 'Y', 'Z'
    bool homeAllAxes();

    // Check if homing is complete for all axes
    bool isHomed() const { return _is_homed_x && _is_homed_y && _is_homed_z; }
    void setHomed(bool x, bool y, bool z) { _is_homed_x = x; _is_homed_y = y; _is_homed_z = z; }
    bool isHomedX() const { return _is_homed_x; } // Added getter
    bool isHomedY() const { return _is_homed_y; } // Added getter
    bool isHomedZ() const { return _is_homed_z; } // Added getter

private:
    bool _is_homed_x;
    bool _is_homed_y;
    bool _is_homed_z;

    // Get homing direction for axis: -1 (toward min) or 1 (toward max)
    int _getHomeDir(char axis);

    // Get maximum position for axis in mm
    float _getMaxPos(char axis);

    // Internal helper for homing a single axis
    bool _singleAxisHomingSequence(char axis, long max_travel_steps, float fast_feedrate_mm_s, float slow_feedrate_mm_s);

    // Helper to move towards endstop until triggered or timeout
    // direction: -1 or 1, controlling which way the axis moves
    bool _moveUntilTriggered(char axis, float speed_mm_s, long max_distance_steps, unsigned long timeout_ms, int direction);

    // Helper to move away from endstop for a specified distance
    // direction: -1 or 1, controlling which way to back off
    bool _moveAwayFromEndstop(char axis, float distance_mm, float speed_mm_s, int direction);
};

extern Homing homing; // Global instance

#endif // HOMING_H
