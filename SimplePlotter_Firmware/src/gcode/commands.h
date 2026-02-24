// SimplePlotter_Firmware/src/gcode/commands.h

#ifndef GCODE_COMMANDS_H
#define GCODE_COMMANDS_H

#include <Arduino.h>

// Define possible G-code command types
enum GCodeType {
    GCODE_UNKNOWN = 0,
    // Motion Commands
    GCODE_G0,  // Rapid Move
    GCODE_G1,  // Linear Move
    GCODE_G28, // Home
    GCODE_G90, // Absolute Positioning
    GCODE_G91, // Relative Positioning
    GCODE_G92, // Set Position

    // Machine Commands
    GCODE_M0,   // Unconditional Stop
    GCODE_M24,  // Resume SD/serial execution
    GCODE_M25,  // Pause SD/serial execution
    GCODE_M84,  // Disable Steppers
    GCODE_M114, // Get Current Position
    GCODE_M115, // Get Firmware Info
    GCODE_M119, // Get Endstop Status
    GCODE_M220, // Set Speed Factor
    GCODE_M410, // Quickstop
    GCODE_M503, // Report Settings
    GCODE_M999  // Z Motor Raw Test (diagnostic)
};

// Structure for common parameters
struct GCodeParam {
    bool has_x = false; float x_val = 0.0;
    bool has_y = false; float y_val = 0.0;
    bool has_z = false; float z_val = 0.0;
    bool has_f = false; float f_val = 0.0; // Feedrate
    bool has_s = false; float s_val = 0.0; // Generic S parameter (e.g., M84 S, M220 S)
    // Add other common parameters if needed later
};

// Specific parameter structures for commands with unique arguments
struct G28Params {
    bool home_x = false;
    bool home_y = false;
    bool home_z = false;
    bool home_all = false; // True if no axes specified
};

struct G92Params {
    bool has_x = false; float x_val = 0.0;
    bool has_y = false; float y_val = 0.0;
    bool has_z = false; float z_val = 0.0;
};

struct M84Params {
    bool has_s = false; float s_val = 0.0; // Timeout in seconds
};

struct M220Params {
    bool has_s = false; float s_val = 0.0; // Speed factor in percent
};

struct M999Params {
    char axis = 'Z'; // Default to Z for backward compatibility
};

// Main G-code command structure
struct ParsedGCodeCommand {
    GCodeType type;
    
    union {
        GCodeParam  move;     // Used for G0, G1
        G28Params   g28_args;
        G92Params   g92_args;
        M84Params   m84_args;
        M220Params  m220_args;
        M999Params  m999_args;
    };

    // Default constructor to initialize the union (optional, but good practice)
    // For unions, it's good to explicitly initialize one member if there are non-POD types
    // or to ensure a known state. For PODs, it's less critical.
    ParsedGCodeCommand() : type(GCODE_UNKNOWN) { } // Initialize type and default-construct union
};

#endif // GCODE_COMMANDS_H
