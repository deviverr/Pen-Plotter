// SimplePlotter_Firmware/src/io/serial_handler.cpp

#include "serial_handler.h"

// Global instance
SerialHandler serialHandler;

SerialHandler::SerialHandler() : _line_idx(0) {
    _serial_line[0] = '\0'; // Initialize buffer
}

void SerialHandler::init() {
    Serial.begin(BAUDRATE);
    // While (Serial) {}; // Wait for serial port to connect. Needed for native USB port only.
}

void SerialHandler::handleSerialInput() {
    while (Serial.available()) {
        char inChar = Serial.read();

        // Check for line termination characters
        if (inChar == '\n' || inChar == '\r') {
            if (_line_idx > 0) { // Only process if there's actual content
                _serial_line[_line_idx] = '\0'; // Null-terminate the string
                processIncomingLine();
            }
            _line_idx = 0; // Reset for the next line
            _serial_line[0] = '\0'; // Clear buffer
        } else {
            // Store character if there's space
            if (_line_idx < GCODE_MAX_LENGTH) {
                _serial_line[_line_idx++] = inChar;
            } else {
                // Line overflow, discard current line and report error if needed
                // For now, just reset and silently discard the overflowing part
                sendError(ERR_BUFFER_OVERFLOW, "Incoming line too long");
                _line_idx = 0;
                _serial_line[0] = '\0';
            }
        }
    }
}

void SerialHandler::processIncomingLine() {
    if (DEBUG_SERIAL_COMMUNICATION) {
        Serial.print(F("// Received: "));
        Serial.println(_serial_line);
    }

    ParsedGCodeCommand cmd = gcodeParser.parse(_serial_line);

    if (cmd.type == GCODE_UNKNOWN) {
        serialHandler.sendError(ERR_UNKNOWN_COMMAND, _serial_line);
        serialHandler.sendOK(); // Send ok even for errors, allows PC to proceed
        return;
    }
    
    if (gcodeBuffer.isFull()) {
        serialHandler.sendError(ERR_BUFFER_OVERFLOW, "Command buffer full");
        serialHandler.sendOK(); // Send ok even for errors, allows PC to proceed
        return;
    }

    gcodeBuffer.push(cmd);
    // DO NOT send OK here. The main loop will pop the command, execute it,
    // and then send OK (or data + OK) once it's ready for the next command.
    // This implements the "blocking mode: Wait for ok before sending next command".
}

void SerialHandler::sendOK() {
    Serial.println(F("ok"));
}

void SerialHandler::sendError(ErrorCode code, const char* description) {
    Serial.print(F("error: "));
    Serial.print(code);
    if (description) {
        Serial.print(F(" - "));
        Serial.print(description);
    }
    Serial.println();
}

void SerialHandler::sendInfo(const char* message) {
    Serial.print(F("// "));
    Serial.println(message);
}

void SerialHandler::sendPosition(float x, float y, float z) {
    Serial.print(F("X:"));
    Serial.print(x, 2);
    Serial.print(F(" Y:"));
    Serial.print(y, 2);
    Serial.print(F(" Z:"));
    Serial.print(z, 2);
    Serial.println();
}

void SerialHandler::sendFirmwareInfo() {
    Serial.print(F("FIRMWARE_NAME:SimplePlotter FIRMWARE_VERSION:"));
    Serial.print(F(FIRMWARE_VERSION_STRING));
    Serial.print(F(" PROTOCOL_VERSION:1.0 MACHINE_TYPE:PenPlotter BOARD_TYPE:"));
    Serial.print(F(BOARD_TYPE));
    Serial.println(F(" EXTRUDER_COUNT:0"));
}

void SerialHandler::sendEndstopStatus(bool x_min_triggered, bool y_min_triggered, bool z_min_triggered) {
    Serial.print(F("x_min: ")); Serial.println(x_min_triggered ? F("TRIGGERED") : F("open"));
    Serial.print(F("y_min: ")); Serial.println(y_min_triggered ? F("TRIGGERED") : F("open"));
    Serial.print(F("z_min: ")); Serial.println(z_min_triggered ? F("TRIGGERED") : F("open"));
}
