// SimplePlotter_Firmware/src/io/serial_handler.h

#ifndef SERIAL_HANDLER_H
#define SERIAL_HANDLER_H

#include <Arduino.h>
#include "../config.h"
#include "../gcode/commands.h" // For ParsedGCodeCommand
#include "../gcode/parser.h"   // For GCodeParser
#include "../gcode/buffer.h"   // For GCodeBuffer

// Error Codes as defined in Work_Plan
enum ErrorCode {
    ERR_NONE = 0,
    ERR_UNKNOWN_COMMAND = 1,
    ERR_INVALID_SYNTAX = 2,
    ERR_OUT_OF_RANGE = 3,
    ERR_ENDSTOP_HIT = 4,
    ERR_HOMING_FAILED = 5,
    ERR_NOT_HOMED = 6,
    ERR_BUFFER_OVERFLOW = 7,
    ERR_TIMEOUT = 8, // Added for general timeouts, e.g., serial response
    ERR_EMPTY_COMMAND = 9 // Added for empty lines after parsing
};

class SerialHandler {
public:
    SerialHandler();

    void init();
    void handleSerialInput(); // To be called in main loop()

    // Send responses to the host
    void sendOK();
    void sendError(ErrorCode code, const char* description = nullptr);
    void sendInfo(const char* message);
    void sendPosition(float x, float y, float z);
    void sendFirmwareInfo();
    void sendEndstopStatus(bool x_min_triggered, bool y_min_triggered, bool z_min_triggered);

private:
    char _serial_line[GCODE_MAX_LENGTH + 1]; // Buffer for incoming serial line
    byte _line_idx;                          // Current index in _serial_line

    void processIncomingLine(); // Parses and queues a complete line
};

extern SerialHandler serialHandler; // Global instance

#endif // SERIAL_HANDLER_H
