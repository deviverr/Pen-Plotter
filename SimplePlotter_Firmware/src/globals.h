// SimplePlotter_Firmware/src/globals.h

#ifndef GLOBALS_H
#define GLOBALS_H

#include "motion/kinematics.h" // For Point3D

// Global machine state variables (defined in main.cpp)
extern Point3D current_position_mm;
extern bool absolute_mode;
extern float current_feedrate_mm_min;
extern float speed_factor;

// Stepper idle timeout management
extern long stepper_disable_timeout_ms; // 0 means never disable, or specific timeout in ms
extern unsigned long last_stepper_activity_time;

// Global instances of core modules (defined in their respective .cpp files)
#include "motion/homing.h"
#include "io/endstops.h"
#include "motion/stepper_control.h"
#include "io/serial_handler.h" // Add this
#include "gcode/parser.h"      // Add this
#include "gcode/buffer.h"      // Add this

extern Homing homing;
extern Endstops endstops;
extern StepperControl stepperControl;
extern SerialHandler serialHandler; // Add this
extern GCodeParser gcodeParser;      // Add this
extern GCodeBuffer gcodeBuffer;      // Add this

#endif // GLOBALS_H
