// SimplePlotter_Firmware/src/ui/encoder.h

#ifndef ENCODER_H
#define ENCODER_H

#include <Arduino.h>
#include "../config.h" // For BTN_EN1, BTN_EN2, BTN_ENC pins

// Enum for encoder rotation direction
enum EncoderDirection {
    ENCODER_NO_CHANGE = 0,
    ENCODER_CLOCKWISE = 1,
    ENCODER_COUNTER_CLOCKWISE = -1
};

// Enum for button events
enum ButtonEvent {
    BUTTON_NO_EVENT = 0,
    BUTTON_CLICK,        // Short press and release
    BUTTON_LONG_PRESS_START // Held for 2 seconds
};

class Encoder {
public:
    Encoder();

    void init();
    void update(); // Call frequently in loop() - polls encoder pins

    EncoderDirection getRotation();
    ButtonEvent getButtonEvent();

private:
    // Rotary encoder state (polled, not interrupt-driven)
    int _encoder_pos_change;
    byte _encoder_a_last_state;
    byte _encoder_b_last_state;

    // Button state
    byte _button_raw_reading;
    byte _button_last_stable_state;
    unsigned long _button_last_debounce_time;

    unsigned long _button_press_start_time;
    bool _button_is_currently_pressed;
    bool _button_just_released;
    bool _long_press_triggered;

    static const unsigned long BUTTON_DEBOUNCE_DELAY_MS = 50;
    static const unsigned long LONG_PRESS_DURATION_MS = 2000;
};

extern Encoder uiEncoder; // Global instance

#endif // ENCODER_H
