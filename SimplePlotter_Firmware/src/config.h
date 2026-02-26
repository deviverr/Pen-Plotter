// SimplePlotter_Firmware/src/config.h

#ifndef CONFIG_H
#define CONFIG_H

#include <Arduino.h>

//===========================================================================
//                               VERSION INFO
//===========================================================================
#define FIRMWARE_VERSION_MAJOR  1
#define FIRMWARE_VERSION_MINOR  4
#define FIRMWARE_VERSION_PATCH  0
#define FIRMWARE_VERSION_STRING "1.4.0"
#define BOARD_TYPE              "MKS_Gen_v1.4"

//===========================================================================
//                               PIN MAPPINGS
//===========================================================================
// MKS Gen v1.4 Pin Definitions based on Work_Plan

// Steppers
#define X_STEP_PIN      54
#define X_DIR_PIN       55
#define X_ENABLE_PIN    38

#define Y_STEP_PIN      60
#define Y_DIR_PIN       61
#define Y_ENABLE_PIN    56

#define Z_STEP_PIN      46
#define Z_DIR_PIN       48
#define Z_ENABLE_PIN    62

// Endstops
#define X_MIN_PIN       3     // INT5
#define Y_MIN_PIN       14    // PJ1
#define Z_MIN_PIN       18    // PD3 / INT3

// Display (EXP1/EXP2) - RepRap Discount Full Graphic Smart Controller
#define LCD_PINS_RS     16
#define LCD_PINS_ENABLE 17
#define LCD_PINS_D4     23
#define LCD_PINS_D5     25
#define LCD_PINS_D6     27
#define LCD_PINS_D7     29

#define BTN_EN1         31    // Rotary encoder pin A
#define BTN_EN2         33    // Rotary encoder pin B
#define BTN_ENC         35    // Rotary encoder push button

#define BEEPER_PIN      37    // Piezo buzzer

#define SD_DETECT_PIN   49
#define SDSS            53    // SD Card Slave Select

//===========================================================================
//                            MACHINE DIMENSIONS & STEPS
//===========================================================================

// Machine dimensions measured from physical plotter (in mm)
#define X_MAX_POS       234.0  // Measured pen travel: home (right) to far wall (left)
#define Y_MAX_POS       191.0  // Measured bed travel: home (front) to far wall (back)
#define Z_MAX_POS       203.0  // Measured Z travel (only 0-5mm used for pen)

// Steps per mm calculation
// DRV8825 drivers at 1/32 microstepping, GT2 belt, 20-tooth pulley:
// Steps per revolution = 200 (full steps) * 32 (microstepping) = 6400
// Pulley circumference = 20 teeth * 2mm = 40mm
// Steps per mm = 6400 / 40 = 160 steps/mm
// Verified: 50mm command moved 24.5mm at 80 steps/mm → 4000/24.5 ≈ 163 ≈ 160
#define X_STEPS_PER_MM  160.0
#define Y_STEPS_PER_MM  160.0

// For Z-axis Leadscrew (T8 leadscrew: 8mm pitch, 1/16 microstepping)
// Steps per revolution = 200 (full steps) * 16 (microstepping) = 3200
// Leadscrew pitch = 8mm/rev
// Steps per mm = 3200 / 8 = 400 steps/mm
// TODO: If Z uses DRV8825 at 1/32, this should be 800 — verify physically
#define Z_STEPS_PER_MM  400.0

//===========================================================================
//                               MOTION PARAMETERS
//===========================================================================

// Max Acceleration (mm/s^2)
#define MAX_ACCEL_X     1000.0  // Configurable: 500-2000
#define MAX_ACCEL_Y     1000.0
#define MAX_ACCEL_Z     500.0   // Pen lift - gentle

// Max Velocity (mm/s)
#define MAX_VELOCITY_XY 100.0   // 6000 mm/min rapid moves
#define DEFAULT_DRAW_VELOCITY_XY 50.0 // 3000 mm/min default drawing speed
#define MAX_VELOCITY_Z  10.0    // 600 mm/min pen lift

// Jerk (mm/s - for trapezoidal velocity profiles, if used)
// AccelStepper handles this internally, but conceptually for software limits
#define JUNCTION_DEVIATION_MM 0.05 // Equivalent to Marlin's DEFAULT_JERK in AccelStepper context

// Pen Z positions (Z=0 is at endstop/paper level, Z+ moves up)
#define PEN_UP_Z        3.0     // Z position when pen is raised (above paper)
#define PEN_DOWN_Z      0.5     // Z position when pen contacts paper

// Homing Parameters
#define HOMING_FEEDRATE_FAST    20.0  // mm/s for fast approach (gentle to avoid missed steps)
#define HOMING_FEEDRATE_SLOW    5.0   // mm/s for slow approach (precision)
#define HOMING_BACKOFF_MM       10.0  // mm to retract after initial endstop trigger
#define HOMING_TIMEOUT_S        60    // seconds before homing times out per axis
#define HOMING_ACCEL_FACTOR     0.5   // Use 50% of normal acceleration during homing
#define Z_HOME_POSITION         2.0   // mm above sensor after Z homing (pen start position)

//===========================================================================
//                             ENDSTOP CONFIGURATION
//===========================================================================

// Endstop inverting logic:
//   false = triggered when pin is HIGH (NO switch with pullup, or active-HIGH sensor)
//   true  = triggered when pin is LOW  (active-LOW sensor outputting LOW when resting)
// FIXED: X endstop was showing TRIGGERED when open - inversion was backwards
// X = optical sensor, outputs HIGH when triggered → inverting=false
// Y = mechanical button, active-LOW → inverting=true
// Z = optical sensor, outputs HIGH when triggered → inverting=false
#define ENDSTOP_X_MIN_INVERTING     false  // Optical sensor, HIGH=triggered
#define ENDSTOP_Y_MIN_INVERTING     true   // Mechanical button, active-LOW
#define ENDSTOP_Z_MIN_INVERTING     false  // Optical sensor, HIGH=triggered

// Motor direction inversion (set true if motor moves the wrong way)
#define INVERT_X_DIR                true   // X+ = physical right (pen homes to right)
#define INVERT_Y_DIR                false  // Y direction is correct
#define INVERT_Z_DIR                false  // Z direction is correct

// Homing direction per axis: -1 = home to min endstop, 1 = home to max endstop
#define HOME_DIR_X                  1      // X endstop is at right (max) side
#define HOME_DIR_Y                  (-1)   // Y endstop is at front (min) side
#define HOME_DIR_Z                  (-1)   // Z endstop is at bottom (min) side

// Enable internal pullup resistors for endstop pins (recommended)
#define ENDSTOP_X_MIN_PULLUP        true
#define ENDSTOP_Y_MIN_PULLUP        true
#define ENDSTOP_Z_MIN_PULLUP        true

// Debounce time for endstop switches (in milliseconds)
#define ENDSTOP_DEBOUNCE_MS         10

//===========================================================================
//                           SERIAL COMMUNICATION
//===========================================================================
#define BAUDRATE                115200

// G-code buffer size
#define GCODE_BUFFER_SIZE       8       // Number of G-code commands to buffer
#define GCODE_MAX_LENGTH        64      // Max characters per G-code line

//===========================================================================
//                               MISCELLANEOUS
//===========================================================================

// Stepper idle timeout
#define DISABLE_STEPPERS_AFTER_IDLE_S   600 // Disable steppers after 10 minutes of idle

// Watchdog Timer (in seconds)
#define WATCHDOG_TIMEOUT_S              8   // ATmega2560 hardware watchdog timeout

// Debugging
#define DEBUG_SERIAL_COMMUNICATION      false // Set to true to echo received commands

// Status Icons for LCD
#define ICON_USB_CONNECTED    "#" // Example: filled square
#define ICON_USB_DISCONNECTED "O" // Example: empty circle
#define ICON_SD_DETECTED      "S" // Example: S for SD
#define ICON_SD_NOT_DETECTED  "-"

#define ICON_HOMED     "v" // Example: checkmark
#define ICON_NOT_HOMED "x" // Example: cross
#define ICON_MOVING    ">" // Example: arrow
#define ICON_IDLE      "=" // Example: two lines

// Potentiometer (analog speed control)
#define POT_PIN         A0    // Analog input for speed potentiometer
#define POT_MIN_SPEED   10    // Minimum speed percent
#define POT_MAX_SPEED   200   // Maximum speed percent

// Safety Features
#define MAX_ALLOWED_JUMP_MM     1000.0 // Maximum allowed jump in single G0/G1 command (mm)

#endif // CONFIG_H
