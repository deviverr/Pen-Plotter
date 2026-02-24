// SimplePlotter_Firmware/src/ui/encoder.cpp

#include "encoder.h"

// Initialize the global instance
Encoder uiEncoder;

Encoder::Encoder() :
    _encoder_pos_change(0),
    _encoder_a_last_state(HIGH),
    _encoder_b_last_state(HIGH),
    _button_raw_reading(HIGH),
    _button_last_stable_state(HIGH),
    _button_last_debounce_time(0),
    _button_press_start_time(0),
    _button_is_currently_pressed(false),
    _button_just_released(false),
    _long_press_triggered(false)
{
}

void Encoder::init() {
    // Setup encoder pins as INPUT_PULLUP
    pinMode(BTN_EN1, INPUT_PULLUP);
    pinMode(BTN_EN2, INPUT_PULLUP);
    pinMode(BTN_ENC, INPUT_PULLUP);

    // Initial read of encoder states
    _encoder_a_last_state = digitalRead(BTN_EN1);
    _encoder_b_last_state = digitalRead(BTN_EN2);

    // Initial read of button raw state
    _button_raw_reading = digitalRead(BTN_ENC);
    _button_last_stable_state = _button_raw_reading;

    // NOTE: Pins 31/33 are NOT interrupt-capable on ATmega2560.
    // Only pins 2, 3, 18, 19, 20, 21 support attachInterrupt().
    // Using polling-based encoder reading instead.
}

void Encoder::update() {
    _button_just_released = false; // Reset for this cycle

    // --- Poll encoder rotation using quadrature state machine ---
    // Quadrature encoder lookup table for state transitions
    // States: 00, 01, 10, 11 (2 bits: AB where A=bit1, B=bit0)
    // CW sequence:  00 -> 01 -> 11 -> 10 -> 00
    // CCW sequence: 00 -> 10 -> 11 -> 01 -> 00
    // Lookup table[old_state][new_state] = direction (-1=CCW, 0=no change/invalid, +1=CW)
    static const int8_t ENCODER_TABLE[4][4] = {
        // old=00 (AB=00): new states 00, 01, 10, 11
        {  0,  1, -1,  0 },  // 00->01=CW, 00->10=CCW, others invalid
        // old=01 (AB=01): new states 00, 01, 10, 11
        { -1,  0,  0,  1 },  // 01->00=CCW, 01->11=CW
        // old=10 (AB=10): new states 00, 01, 10, 11
        {  1,  0,  0, -1 },  // 10->00=CW, 10->11=CCW
        // old=11 (AB=11): new states 00, 01, 10, 11
        {  0, -1,  1,  0 }   // 11->01=CCW, 11->10=CW
    };

    byte a_now = digitalRead(BTN_EN1);
    byte b_now = digitalRead(BTN_EN2);

    // Combine A and B into 2-bit state (A is bit 1, B is bit 0)
    byte old_state = (_encoder_a_last_state << 1) | _encoder_b_last_state;
    byte new_state = (a_now << 1) | b_now;

    // Look up direction from state transition table
    int8_t direction = ENCODER_TABLE[old_state][new_state];
    _encoder_pos_change += direction;

    _encoder_a_last_state = a_now;
    _encoder_b_last_state = b_now;

    // --- Button debounce ---
    byte current_raw_reading = digitalRead(BTN_ENC);

    // If raw reading changed, reset debounce timer
    if (current_raw_reading != _button_raw_reading) {
        _button_last_debounce_time = millis();
        _button_raw_reading = current_raw_reading;
    }

    // After debounce delay, update stable state
    if ((millis() - _button_last_debounce_time) > BUTTON_DEBOUNCE_DELAY_MS) {
        if (current_raw_reading != _button_last_stable_state) {
            _button_last_stable_state = current_raw_reading;

            if (_button_last_stable_state == LOW) { // Button is now pressed (debounced)
                _button_is_currently_pressed = true;
                _button_press_start_time = millis();
                _long_press_triggered = false;
            } else { // Button is now released (debounced)
                _button_is_currently_pressed = false;
                _button_just_released = true;
            }
        }
    }

    // Check for long press while button is held
    if (_button_is_currently_pressed && !_long_press_triggered) {
        if ((millis() - _button_press_start_time) > LONG_PRESS_DURATION_MS) {
            _long_press_triggered = true;
        }
    }
}

EncoderDirection Encoder::getRotation() {
    // Rotary encoders vary: some produce 4 transitions per detent, some only 2
    // Using threshold of 2 for better responsiveness
    const int DETENT_THRESHOLD = 2;

    EncoderDirection result = ENCODER_NO_CHANGE;

    if (_encoder_pos_change >= DETENT_THRESHOLD) {
        result = ENCODER_CLOCKWISE;
        _encoder_pos_change -= DETENT_THRESHOLD;
    } else if (_encoder_pos_change <= -DETENT_THRESHOLD) {
        result = ENCODER_COUNTER_CLOCKWISE;
        _encoder_pos_change += DETENT_THRESHOLD;
    }

    return result;
}

ButtonEvent Encoder::getButtonEvent() {
    ButtonEvent event = BUTTON_NO_EVENT;

    if (_long_press_triggered) {
        event = BUTTON_LONG_PRESS_START;
        _long_press_triggered = false;
    }
    else if (_button_just_released) {
        event = BUTTON_CLICK;
    }

    return event;
}
