// SimplePlotter_Firmware/src/ui/lcd_menu.h

#ifndef LCD_MENU_H
#define LCD_MENU_H

#include <Arduino.h>
#include "screens.h" // For BaseScreen and ScreenType
#include "encoder.h" // For Encoder
#include "../config.h" // For BEEPER_PIN

// Max depth for menu navigation history (how many screens can be "backed" from)
#define MAX_MENU_DEPTH 5

class LCDMenu {
public:
    LCDMenu();

    void init();
    void update(); // Call frequently in loop() to handle input and redraw

    void goToScreen(ScreenType screen_type); // Navigate to a specific screen without history management
    void back(); // Go back to the previous screen in history
    void updateDisplay(); // Force a redraw of the current screen

    // Beeper control
    void beep(unsigned int duration_ms = 50, unsigned int frequency_hz = 2000);

private:
    BaseScreen* _screens[SCREEN_NUM_SCREENS]; // Array of all screen objects
    BaseScreen* _current_screen; // Pointer to the currently active screen
    ScreenType _current_screen_type; // Track currently active screen type

    // Menu navigation history (stack)
    ScreenType _screen_history[MAX_MENU_DEPTH];
    int _history_depth;

    unsigned long _last_redraw_time;
    bool _needs_redraw = false; // Deferred redraw flag
    static const unsigned long REDRAW_INTERVAL_MS = 150; // Redraw rate for static elements

    void _drawCurrentScreen(); // Internal function to draw the active screen

    friend void menuGoTo(ScreenType screen); // Allow global menuGoTo to access private members
    friend void menuBack();                   // Allow global menuBack to access private members
};

extern LCDMenu lcdMenu; // Global instance

// External functions for menu navigation, which also manage history
void menuGoTo(ScreenType screen);
void menuBack();
void menuUpdateDisplay(); // Declared here for external access

#endif // LCD_MENU_H
