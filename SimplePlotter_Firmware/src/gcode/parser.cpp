// SimplePlotter_Firmware/src/gcode/parser.cpp

#include "parser.h"
#include <string.h> // For strlen, strchr, strstr
#include <ctype.h>  // For isspace, toupper
#include <stdlib.h> // For atof

GCodeParser gcodeParser; // Global instance definition

GCodeParser::GCodeParser() {
    // Constructor, nothing to initialize currently
}

// Helper function to extract a float value for a given address character
bool GCodeParser::extract_float_param(const char* line, char address, float& value) {
    char search_str[2] = {address, '\0'}; // e.g., "X"
    const char* ptr = strstr(line, search_str);
    if (ptr) {
        ptr += 1; // Move past the address character (e.g., 'X')
        // Skip any non-numeric characters immediately after the address (e.g., space)
        while (*ptr && (isspace((unsigned char)*ptr) || *ptr == '=')) { // Allow '=' like in Marlin's M203 X=100
            ptr++;
        }
        if (isdigit((unsigned char)*ptr) || (*ptr == '-' && isdigit((unsigned char)*(ptr + 1)))) {
            value = atof(ptr);
            return true;
        }
    }
    return false;
}

// Helper to determine if an axis is present in G28 (e.g., G28 X)
bool GCodeParser::has_axis_param(const char* line, char axis_char) {
    char search_str[2] = {axis_char, '\0'}; // e.g., "X"
    return strstr(line, search_str) != nullptr;
}


ParsedGCodeCommand GCodeParser::parse(const char* raw_line) {
    ParsedGCodeCommand cmd;
    cmd.type = GCODE_UNKNOWN;

    if (!raw_line || strlen(raw_line) == 0) {
        return cmd;
    }

    // Create a mutable copy and convert to uppercase, strip comments
    char line[GCODE_MAX_LENGTH + 1];
    strncpy(line, raw_line, GCODE_MAX_LENGTH);
    line[GCODE_MAX_LENGTH] = '\0'; // Ensure null termination

    char* comment_ptr = strchr(line, ';');
    if (comment_ptr) {
        *comment_ptr = '\0'; // Null-terminate at the start of the comment
    }

    // Strip leading/trailing whitespace and convert to uppercase
    char* read_ptr = line;
    while (isspace((unsigned char)*read_ptr)) {
        read_ptr++;
    }
    // Convert to uppercase in place. The parsing logic will handle parameters after this.
    for (char* p = read_ptr; *p; p++) {
        *p = toupper((unsigned char)*p);
    }
    
    // Check if the line is empty after stripping comments and whitespace
    if (strlen(read_ptr) == 0) {
        return cmd; 
    }

    // Identify G or M command
    char command_char = read_ptr[0];
    int command_num = atoi(&read_ptr[1]); // Read number after G/M

    // Using `read_ptr` for parameter extraction to ensure only the cleaned
    // and uppercase part of the line is processed.
    const char* line_for_param_extraction = read_ptr;

    switch (command_char) {
        case 'G':
            switch (command_num) {
                case 0: // G0 Rapid Move
                case 1: { // G1 Linear Move
                    cmd.type = (command_num == 0) ? GCODE_G0 : GCODE_G1;
                    cmd.move.has_x = extract_float_param(line_for_param_extraction, 'X', cmd.move.x_val);
                    cmd.move.has_y = extract_float_param(line_for_param_extraction, 'Y', cmd.move.y_val);
                    cmd.move.has_z = extract_float_param(line_for_param_extraction, 'Z', cmd.move.z_val);
                    cmd.move.has_f = extract_float_param(line_for_param_extraction, 'F', cmd.move.f_val);
                    break;
                }
                case 28: { // G28 Home
                    cmd.type = GCODE_G28;
                    cmd.g28_args.home_x = has_axis_param(line_for_param_extraction, 'X');
                    cmd.g28_args.home_y = has_axis_param(line_for_param_extraction, 'Y');
                    cmd.g28_args.home_z = has_axis_param(line_for_param_extraction, 'Z');
                    // If no axes specified, home all
                    if (!cmd.g28_args.home_x && !cmd.g28_args.home_y && !cmd.g28_args.home_z) {
                        cmd.g28_args.home_all = true;
                    }
                    break;
                }
                case 90: { // G90 Absolute Positioning
                    cmd.type = GCODE_G90;
                    break;
                }
                case 91: { // G91 Relative Positioning
                    cmd.type = GCODE_G91;
                    break;
                }
                case 92: { // G92 Set Position
                    cmd.type = GCODE_G92;
                    cmd.g92_args.has_x = extract_float_param(line_for_param_extraction, 'X', cmd.g92_args.x_val);
                    cmd.g92_args.has_y = extract_float_param(line_for_param_extraction, 'Y', cmd.g92_args.y_val);
                    cmd.g92_args.has_z = extract_float_param(line_for_param_extraction, 'Z', cmd.g92_args.z_val);
                    break;
                }
                default:
                    // If a G-code with parameters but not G0/G1/G28/G92 is passed
                    // it should still extract common parameters if they exist,
                    // even if the G-code itself is unknown.
                    // However, for this project's defined scope, an unknown G-code
                    // will simply result in GCODE_UNKNOWN.
                    cmd.type = GCODE_UNKNOWN; 
                    break;
            }
            break;

        case 'M':
            switch (command_num) {
                case 0: { // M0 Unconditional Stop
                    cmd.type = GCODE_M0;
                    break;
                }
                case 24: { // M24 Resume execution
                    cmd.type = GCODE_M24;
                    break;
                }
                case 25: { // M25 Pause execution
                    cmd.type = GCODE_M25;
                    break;
                }
                case 84: { // M84 Disable Steppers
                    cmd.type = GCODE_M84;
                    cmd.m84_args.has_s = extract_float_param(line_for_param_extraction, 'S', cmd.m84_args.s_val);
                    break;
                }
                case 114: { // M114 Get Current Position
                    cmd.type = GCODE_M114;
                    break;
                }
                case 115: { // M115 Get Firmware Info
                    cmd.type = GCODE_M115;
                    break;
                }
                case 119: { // M119 Get Endstop Status
                    cmd.type = GCODE_M119;
                    break;
                }
                case 220: { // M220 Set Speed Factor
                    cmd.type = GCODE_M220;
                    cmd.m220_args.has_s = extract_float_param(line_for_param_extraction, 'S', cmd.m220_args.s_val);
                    break;
                }
                case 410: { // M410 Quickstop
                    cmd.type = GCODE_M410;
                    break;
                }
                case 503: { // M503 Report Settings
                    cmd.type = GCODE_M503;
                    break;
                }
                case 999: { // M999 Motor Raw Test (per-axis diagnostic)
                    cmd.type = GCODE_M999;
                    // Default to Z for backward compatibility
                    cmd.m999_args.axis = 'Z';
                    if (has_axis_param(line_for_param_extraction, 'X')) cmd.m999_args.axis = 'X';
                    else if (has_axis_param(line_for_param_extraction, 'Y')) cmd.m999_args.axis = 'Y';
                    else if (has_axis_param(line_for_param_extraction, 'Z')) cmd.m999_args.axis = 'Z';
                    break;
                }
                default:
                    cmd.type = GCODE_UNKNOWN;
                    break;
            }
            break;

        default:
            cmd.type = GCODE_UNKNOWN;
            break;
    }

    return cmd;
}
