// SimplePlotter_Firmware/src/gcode/parser.h

#ifndef GCODE_PARSER_H
#define GCODE_PARSER_H

#include <Arduino.h>
#include "../config.h" // Include our configuration for GCODE_MAX_LENGTH
#include "commands.h" // Include our command definitions

class GCodeParser {
public:
    GCodeParser();

    // Parses a G-code string and returns a ParsedGCodeCommand object.
    // Returns a command with type GCODE_UNKNOWN if parsing fails.
    ParsedGCodeCommand parse(const char* line);

private:
    // Helper function to extract a float value for a given address character (e.g., 'X')
    // and store it in value. Returns true if found, false otherwise.
    bool extract_float_param(const char* line, char address, float& value);

    // Helper to determine if an axis is present in G28 (e.g., G28 X)
    bool has_axis_param(const char* line, char axis_char);
};

extern GCodeParser gcodeParser; // Global instance

#endif // GCODE_PARSER_H
