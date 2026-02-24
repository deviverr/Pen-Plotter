// SimplePlotter_Firmware/src/gcode/buffer.h

#ifndef GCODE_BUFFER_H
#define GCODE_BUFFER_H

#include <Arduino.h>
#include "../config.h"           // Include our configuration for GCODE_BUFFER_SIZE
#include "../utils/ringbuffer.h" // Include our generic ring buffer
#include "commands.h"            // Include our G-code command definitions

// Define the size of the G-code command buffer
#define GCODE_COMMAND_BUFFER_SIZE GCODE_BUFFER_SIZE // From config.h (8 commands)

class GCodeBuffer {
public:
    GCodeBuffer();

    // Adds a parsed command to the buffer. Returns true on success, false if buffer is full.
    bool push(const ParsedGCodeCommand& command);

    // Retrieves the next command from the buffer. Returns true on success, false if buffer is empty.
    bool pop(ParsedGCodeCommand& command);

    // Returns true if the buffer is full.
    bool isFull() const;

    // Returns true if the buffer is empty.
    bool isEmpty() const;

    // Returns the number of commands currently in the buffer.
    int size() const;

private:
    // Instantiate a RingBuffer to hold ParsedGCodeCommand objects
    RingBuffer<ParsedGCodeCommand, GCODE_COMMAND_BUFFER_SIZE> _buffer;
};

extern GCodeBuffer gcodeBuffer; // Global instance

#endif // GCODE_BUFFER_H
