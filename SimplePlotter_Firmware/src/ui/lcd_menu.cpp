// SimplePlotter_Firmware/src/ui/lcd_menu.cpp

#include "lcd_menu.h"
#include "screens.h" // For sd_exec_state
#include "../globals.h"
#include "../io/sd_card.h"
#include "../io/buzzer.h"

// Instantiate all specific screen objects
MainStatusScreen mainStatusScreen;
ManualControlScreen manualControlScreen;
JogStepScreen jogStepScreen;
HomeAxisScreen homeAxisScreen;
PenSettingsScreen penSettingsScreen;
MotionSettingsScreen motionSettingsScreen;
InfoScreen infoScreen;
SDScreen sdScreen;
// plotPreviewScreen is defined in screens.cpp as a global extern

// Global instance of LCDMenu
LCDMenu lcdMenu;

// External functions to allow screens to call menu navigation
// These manage the screen history.
void menuGoTo(ScreenType screen) {
    LCDMenu& menu = lcdMenu; // Use reference for easier access

    // Only push to history if navigating to a different screen and history isn't full
    if (screen != menu._current_screen_type && menu._history_depth < MAX_MENU_DEPTH) {
        menu._screen_history[menu._history_depth++] = menu._current_screen_type;
    }
    menu.goToScreen(screen);
}

void menuBack() {
    LCDMenu& menu = lcdMenu;

    if (menu._history_depth > 0) {
        ScreenType prev_screen_type = menu._screen_history[--menu._history_depth];
        menu.goToScreen(prev_screen_type);
    } else {
        // Already at root, perhaps beep or do nothing
        menu.beep(50, 1000); // Beep to indicate no more history
    }
}

void menuUpdateDisplay() {
    lcdMenu.updateDisplay();
}


LCDMenu::LCDMenu() :
    _current_screen(nullptr),
    _current_screen_type(SCREEN_MAIN_STATUS), // Initialize to default screen type
    _history_depth(0),
    _last_redraw_time(0)
{
    // Map screen types to screen objects
    _screens[SCREEN_MAIN_STATUS]    = &mainStatusScreen;
    _screens[SCREEN_MANUAL_CONTROL] = &manualControlScreen;
    _screens[SCREEN_JOG_STEP]       = &jogStepScreen;
    _screens[SCREEN_HOME_AXIS]      = &homeAxisScreen;
    _screens[SCREEN_PEN_SETTINGS]   = &penSettingsScreen;
    _screens[SCREEN_MOTION_SETTINGS]= &motionSettingsScreen;
    _screens[SCREEN_INFO]           = &infoScreen;
    _screens[SCREEN_SD_CARD]        = &sdScreen;
    _screens[SCREEN_PLOT_PREVIEW]   = &plotPreviewScreen;
}

void LCDMenu::init() {
    // Initialize U8g2 library
    u8g2.begin();
    u8g2.enableUTF8Print(); // Enable UTF8 if using special characters (e.g. checkmark)
    u8g2.setContrast(128); // Set contrast (0-255)

    // Initialize encoder
    uiEncoder.init();

    // Setup beeper pin
    pinMode(BEEPER_PIN, OUTPUT);
    digitalWrite(BEEPER_PIN, LOW); // Ensure beeper is off

    // Go to the initial screen (Main Status)
    goToScreen(SCREEN_MAIN_STATUS);
}

void LCDMenu::update() {
    // Update encoder state
    uiEncoder.update();

    // Handle encoder rotation
    EncoderDirection rotation = uiEncoder.getRotation();
    if (rotation != ENCODER_NO_CHANGE) {
        _current_screen->onEncoderTurn(rotation);
        // Defer redraw to next periodic cycle to avoid double draws
        _needs_redraw = true;
    }

    // Handle button events
    ButtonEvent button_event = uiEncoder.getButtonEvent();
    switch (button_event) {
        case BUTTON_CLICK:
            _current_screen->onButtonClick();
            beep(20, 2000); // Short beep for click
            // Redraw is handled by menuGoTo/menuBack or periodic update
            break;
        case BUTTON_LONG_PRESS_START:
            // If SD card is executing, cancel it
            if (sd_exec_state == SD_EXEC_RUNNING || sd_exec_state == SD_EXEC_PAUSED) {
                sd_exec_state = SD_EXEC_IDLE;
                sdCard.closeFile();
                Buzzer::playPlotStop();
            }
            // Long press from any screen returns to Main Status
            if (_current_screen_type != SCREEN_MAIN_STATUS) {
                _history_depth = 0;
                goToScreen(SCREEN_MAIN_STATUS);
            }
            beep(100, 1500);
            _drawCurrentScreen();
            break;
        default:
            break;
    }

    // Redraw periodically for dynamic content, or immediately if flagged
    if (_needs_redraw || millis() - _last_redraw_time > REDRAW_INTERVAL_MS) {
        _drawCurrentScreen();
        _last_redraw_time = millis();
        _needs_redraw = false;
    }
}

void LCDMenu::goToScreen(ScreenType screen_type) {
    if (screen_type >= SCREEN_NUM_SCREENS || screen_type < 0) return; // Invalid screen

    if (_current_screen != nullptr) {
        _current_screen->onExit(); // Notify current screen it's being exited
    }
    
    _current_screen = _screens[screen_type];
    _current_screen_type = screen_type; // Update internal current screen type
    _current_screen->onEnter(); // Notify new screen it's active
    _drawCurrentScreen();
}

void LCDMenu::beep(unsigned int duration_ms, unsigned int frequency_hz) {
    if (frequency_hz == 0) {
        noTone(BEEPER_PIN);
    } else {
        tone(BEEPER_PIN, frequency_hz);
    }
    delay(duration_ms);
    noTone(BEEPER_PIN);
}

void LCDMenu::_drawCurrentScreen() {
    u8g2.firstPage();
    do {
        u8g2.setFontMode(1); // Transparent font background
        u8g2.setDrawColor(1); // White foreground
        _current_screen->draw();
    } while (u8g2.nextPage());
}

void LCDMenu::updateDisplay() {
    _drawCurrentScreen();
}
