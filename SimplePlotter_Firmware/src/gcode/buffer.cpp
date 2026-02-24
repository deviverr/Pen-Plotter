// SimplePlotter_Firmware/src/gcode/buffer.cpp

#include "buffer.h"

GCodeBuffer gcodeBuffer; // Global instance definition

GCodeBuffer::GCodeBuffer() {
    // Constructor, RingBuffer is automatically initialized
}

bool GCodeBuffer::push(const ParsedGCodeCommand& command) {
    return _buffer.push(command);
}

bool GCodeBuffer::pop(ParsedGCodeCommand& command) {
    return _buffer.pop(command);
}

bool GCodeBuffer::isFull() const {
    return _buffer.isFull();
}

bool GCodeBuffer::isEmpty() const {
    return _buffer.isEmpty();
}

int GCodeBuffer::size() const {
    return _buffer.size();
}
