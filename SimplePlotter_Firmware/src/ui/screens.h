// SimplePlotter_Firmware/src/ui/screens.h

#ifndef SCREENS_H
#define SCREENS_H

#include <Arduino.h>
#include <U8g2lib.h>
#include "../config.h"
#include "../motion/kinematics.h"

// SD card constants (must match sd_card.h)
#ifndef SD_MAX_FILES
#define SD_MAX_FILES 20
#endif
#ifndef SD_MAX_FILENAME
#define SD_MAX_FILENAME 13
#endif

// Global U8g2 object declaration
// ST7920 SW_SPI constructor: (rotation, clock, data, cs [, reset])
//   Clock (SCK):  LCD_PINS_D4     (D23)
//   Data  (SID):  LCD_PINS_ENABLE (D17)
//   CS:           LCD_PINS_RS     (D16)
// Using _2_ (2-page buffer, 512 bytes) for faster refresh vs _1_ (1-page, 256 bytes).
extern U8G2_ST7920_128X64_2_SW_SPI u8g2;

// Runtime-adjustable pen Z positions (initialized from config.h defines)
extern float pen_up_z;
extern float pen_down_z;

// Lines plotted counter
extern unsigned long lines_plotted;

// Current jog step (shared between ManualControl and JogStep screens)
extern float current_jog_step_mm;

// Base class for all screens
class BaseScreen {
public:
    virtual void draw() = 0;
    virtual void onEncoderTurn(int direction) {}
    virtual void onButtonClick() {}
    virtual void onButtonLongPress() {}
    virtual void onEnter() {}
    virtual void onExit() {}
};

// Enum for screen types
enum ScreenType {
    SCREEN_MAIN_STATUS,
    SCREEN_MANUAL_CONTROL,
    SCREEN_JOG_STEP,
    SCREEN_HOME_AXIS,
    SCREEN_PEN_SETTINGS,
    SCREEN_MOTION_SETTINGS,
    SCREEN_INFO,
    SCREEN_SD_CARD,
    SCREEN_PLOT_PREVIEW,
    SCREEN_NUM_SCREENS
};

// --- Screen classes ---

class MainStatusScreen : public BaseScreen {
public:
    void draw() override;
    void onButtonClick() override;
private:
    uint8_t _catFrame = 0;
    unsigned long _lastCatUpdate = 0;
};

class ManualControlScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedItem = 0;
    int _scrollOffset = 0;
    static const int ITEM_COUNT = 7;
};

class JogStepScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedIdx = 0;
    int _scrollOffset = 0;
    static const int STEP_COUNT = 6; // 5 steps + Back
};

class HomeAxisScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedItem = 0;
    int _scrollOffset = 0;
    bool _isHoming = false;
    char _homingLabel[16];
    uint8_t _spinnerFrame = 0;
    static const int ITEM_COUNT = 5;
};

class PenSettingsScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedItem = 0;
    int _scrollOffset = 0;
    bool _editing = false;
    static const int ITEM_COUNT = 5; // Pen Up Z, Pen Down Z, Test Pen, Test Z Raw, Back
};

class MotionSettingsScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedItem = 0;
    bool _editing = false;
    static const int ITEM_COUNT = 2;
};

class InfoScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
private:
    int _scrollOffset = 0;
};

// SD execution state (shared between SDScreen and main loop)
enum SDExecState {
    SD_EXEC_IDLE,
    SD_EXEC_RUNNING,
    SD_EXEC_PAUSED,
    SD_EXEC_DONE
};

extern volatile SDExecState sd_exec_state;
extern char sd_exec_filename[13];

class SDScreen : public BaseScreen {
public:
    void draw() override;
    void onEncoderTurn(int direction) override;
    void onButtonClick() override;
    void onEnter() override;
    void onExit() override;
private:
    int _selectedItem = 0;
    int _scrollOffset = 0;
    int _fileCount = 0;
    char _fileList[SD_MAX_FILES][SD_MAX_FILENAME];
    bool _showingExec = false; // true when showing execution progress
};

// Plot preview screen - shows scaled XY path during G-code execution
#define PLOT_PREVIEW_MAX_SEGMENTS 200

struct PreviewSegment {
    uint8_t x0, y0, x1, y1;
};

class PlotPreviewScreen : public BaseScreen {
public:
    void draw() override;
    void onButtonClick() override;
    void onEnter() override;

    // Call from G-code executor to add line segments
    void addSegment(float fromX, float fromY, float toX, float toY);
    void clear();
    void setProgress(uint8_t percent) { _progress = percent; }

private:
    PreviewSegment _segments[PLOT_PREVIEW_MAX_SEGMENTS];
    int _segmentCount = 0;
    uint8_t _progress = 0;

    // Mapping from machine coords to screen coords
    uint8_t _mapX(float x);
    uint8_t _mapY(float y);
};

extern PlotPreviewScreen plotPreviewScreen;

#endif // SCREENS_H
