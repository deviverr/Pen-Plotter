// SimplePlotter_Firmware/src/ui/screens.cpp

#include "screens.h"
#include "lcd_menu.h"
#include "ui_helpers.h"
#include "cat_animation.h"
#include "../globals.h"
#include "../io/sd_card.h"
#include "../io/buzzer.h"
#include <avr/wdt.h>

// Global U8g2 object definition
U8G2_ST7920_128X64_2_SW_SPI u8g2(U8G2_R0, LCD_PINS_D4, LCD_PINS_ENABLE, LCD_PINS_RS);

// Runtime-adjustable pen Z positions
float pen_up_z = PEN_UP_Z;
float pen_down_z = PEN_DOWN_Z;

// Lines plotted counter
unsigned long lines_plotted = 0;

// Shared jog step
float current_jog_step_mm = 1.0;

// Forward declarations for menu navigation
extern void menuGoTo(ScreenType screen);
extern void menuBack();
extern void menuUpdateDisplay();

//===========================================================================
// MainStatusScreen - shows position, status, cat animation
//===========================================================================

void MainStatusScreen::draw() {
    // Title bar
    drawTitleBar(u8g2, "SimplePlotter");

    u8g2.setFont(u8g2_font_5x7_tf);

    // Position display
    char buf[22];
    snprintf(buf, sizeof(buf), "X:%.1f Y:%.1f", (double)current_position_mm.x, (double)current_position_mm.y);
    u8g2.drawStr(0, 22, buf);

    snprintf(buf, sizeof(buf), "Z:%.1f", (double)current_position_mm.z);
    u8g2.drawStr(0, 31, buf);

    // Homing status
    if (homing.isHomed()) {
        u8g2.drawStr(50, 31, "[HOMED]");
    } else {
        u8g2.drawStr(50, 31, "[!HOME]");
    }

    // Speed factor
    snprintf(buf, sizeof(buf), "Spd:%d%%", (int)speed_factor);
    u8g2.drawStr(0, 41, buf);

    // SD status
    u8g2.drawStr(60, 41, "SD:");
    if (digitalRead(SD_DETECT_PIN) == LOW) {
        u8g2.drawStr(78, 41, "OK");
    } else {
        u8g2.drawStr(78, 41, "--");
    }

    // Sans animation (bottom-right corner) - desktop pet behavior!
    // Frame 0: idle, Frame 1: wink, Frame 2: glowing eyes, Frame 3: shrug
    // Frame 4: jump, Frame 5: walk left, Frame 6: walk right
    unsigned long now = millis();
    if (now - _lastCatUpdate > 300) {  // Update every 300ms for livelier animation
        _lastCatUpdate = now;

        // Random behavior: mostly idle, occasionally do something fun
        int rnd = random(100);
        if (rnd < 60) {
            _catFrame = 0;  // 60% idle
        } else if (rnd < 70) {
            _catFrame = 1;  // 10% wink
        } else if (rnd < 75) {
            _catFrame = 2;  // 5% glowing eyes
        } else if (rnd < 82) {
            _catFrame = 3;  // 7% shrug
        } else if (rnd < 87) {
            _catFrame = 4;  // 5% jump
        } else if (rnd < 93) {
            _catFrame = 5;  // 6% walk left
        } else {
            _catFrame = 6;  // 7% walk right
        }
    }
    const unsigned char* frame = (const unsigned char*)pgm_read_ptr(&cat_frames[_catFrame]);
    u8g2.drawXBMP(110, 48, CAT_WIDTH, CAT_HEIGHT, frame);

    // Hint at bottom
    u8g2.setFont(u8g2_font_4x6_tf);
    u8g2.drawStr(0, 63, "Click: Menu");
}

void MainStatusScreen::onButtonClick() {
    menuGoTo(SCREEN_MANUAL_CONTROL);
}

//===========================================================================
// ManualControlScreen - main menu with scrollable list
//===========================================================================

static const char* const manualMenuItems[] = {
    "Jog Step",
    "Home Axes",
    "Pen Settings",
    "Motion Settings",
    "Info",
    "SD Card",
    "Back"
};

void ManualControlScreen::draw() {
    drawTitleBar(u8g2, "Menu");
    drawMenuList(u8g2, manualMenuItems, ITEM_COUNT, _selectedItem, _scrollOffset);
}

void ManualControlScreen::onEncoderTurn(int direction) {
    _selectedItem = clampInt(_selectedItem + direction, 0, ITEM_COUNT - 1);
    _scrollOffset = calcScrollOffset(_selectedItem, _scrollOffset, 4);
}

void ManualControlScreen::onButtonClick() {
    switch (_selectedItem) {
        case 0: menuGoTo(SCREEN_JOG_STEP); break;
        case 1: menuGoTo(SCREEN_HOME_AXIS); break;
        case 2: menuGoTo(SCREEN_PEN_SETTINGS); break;
        case 3: menuGoTo(SCREEN_MOTION_SETTINGS); break;
        case 4: menuGoTo(SCREEN_INFO); break;
        case 5: menuGoTo(SCREEN_SD_CARD); break;
        case 6: menuBack(); break;
    }
}

void ManualControlScreen::onEnter() {
    _selectedItem = 0;
    _scrollOffset = 0;
}

void ManualControlScreen::onExit() {}

//===========================================================================
// JogStepScreen - select jog step size
//===========================================================================

static const float jogStepOptions[] = {0.1, 0.5, 1.0, 5.0, 10.0};
static const char* const jogStepLabels[] = {
    "0.1 mm", "0.5 mm", "1.0 mm", "5.0 mm", "10.0 mm", "Back"
};

void JogStepScreen::draw() {
    drawTitleBar(u8g2, "Jog Step");
    drawMenuList(u8g2, jogStepLabels, STEP_COUNT, _selectedIdx, _scrollOffset);
}

void JogStepScreen::onEncoderTurn(int direction) {
    _selectedIdx = clampInt(_selectedIdx + direction, 0, STEP_COUNT - 1);
    _scrollOffset = calcScrollOffset(_selectedIdx, _scrollOffset, 4);
}

void JogStepScreen::onButtonClick() {
    if (_selectedIdx == STEP_COUNT - 1) { // Back
        menuBack();
        return;
    }
    current_jog_step_mm = jogStepOptions[_selectedIdx];
    menuBack();
}

void JogStepScreen::onEnter() {
    _scrollOffset = 0;
    // Find closest match to current jog step
    _selectedIdx = 2; // default 1.0mm
    for (int i = 0; i < STEP_COUNT - 1; i++) { // -1 to skip "Back"
        if (abs(jogStepOptions[i] - current_jog_step_mm) < 0.01) {
            _selectedIdx = i;
            break;
        }
    }
}

void JogStepScreen::onExit() {}

//===========================================================================
// HomeAxisScreen - select axis to home, with spinner feedback
//===========================================================================

static const char* const homeMenuItems[] = {
    "Home X", "Home Y", "Home Z", "Home All", "Back"
};

void HomeAxisScreen::draw() {
    drawTitleBar(u8g2, "Home Axes");

    if (_isHoming) {
        // Show homing in progress
        u8g2.setFont(u8g2_font_6x10_tf);
        u8g2.drawStr(10, 30, _homingLabel);
        drawSpinner(u8g2, 100, 35, 8, _spinnerFrame++);
        drawProgressBar(u8g2, 10, 48, 108, 8, -1); // indeterminate
    } else {
        drawMenuList(u8g2, homeMenuItems, ITEM_COUNT, _selectedItem, _scrollOffset);
    }
}

void HomeAxisScreen::onEncoderTurn(int direction) {
    if (_isHoming) return;
    _selectedItem = clampInt(_selectedItem + direction, 0, ITEM_COUNT - 1);
    _scrollOffset = calcScrollOffset(_selectedItem, _scrollOffset, 4);
}

void HomeAxisScreen::onButtonClick() {
    if (_isHoming) return;

    switch (_selectedItem) {
        case 0:
            _isHoming = true;
            strncpy(_homingLabel, "Homing X...", sizeof(_homingLabel));
            menuUpdateDisplay();
            homing.homeAxis('X');
            _isHoming = false;
            break;
        case 1:
            _isHoming = true;
            strncpy(_homingLabel, "Homing Y...", sizeof(_homingLabel));
            menuUpdateDisplay();
            homing.homeAxis('Y');
            _isHoming = false;
            break;
        case 2:
            _isHoming = true;
            strncpy(_homingLabel, "Homing Z...", sizeof(_homingLabel));
            menuUpdateDisplay();
            homing.homeAxis('Z');
            _isHoming = false;
            break;
        case 3:
            _isHoming = true;
            strncpy(_homingLabel, "Homing All...", sizeof(_homingLabel));
            menuUpdateDisplay();
            homing.homeAllAxes();
            _isHoming = false;
            break;
        case 4:
            menuBack();
            return;
    }
}

void HomeAxisScreen::onEnter() {
    _selectedItem = 0;
    _scrollOffset = 0;
    _isHoming = false;
    _spinnerFrame = 0;
}

void HomeAxisScreen::onExit() {
    _isHoming = false;
}

//===========================================================================
// PenSettingsScreen - adjust pen up/down Z + test pen
//===========================================================================

void PenSettingsScreen::draw() {
    drawTitleBar(u8g2, "Pen Settings");

    u8g2.setFont(u8g2_font_6x10_tf);
    const int startY = 14;
    const int lineH = 11;
    const int visibleLines = (64 - startY) / lineH; // 4 visible

    // Build dynamic labels
    char buf0[24], buf1[24];
    snprintf(buf0, sizeof(buf0), "Pen Up Z: %.1f", (double)pen_up_z);
    snprintf(buf1, sizeof(buf1), "Pen Dn Z: %.1f", (double)pen_down_z);
    const char* labels[5] = { buf0, buf1, "Test Pen", "Test Z Raw", "Back" };

    for (int i = 0; i < visibleLines && (_scrollOffset + i) < ITEM_COUNT; i++) {
        int itemIdx = _scrollOffset + i;
        int y = startY + i * lineH + 9;

        if (itemIdx == _selectedItem) {
            u8g2.drawBox(0, startY + i * lineH, 128, lineH);
            u8g2.setDrawColor(0);
        }
        u8g2.drawStr(2, y, labels[itemIdx]);
        if (_editing && itemIdx == _selectedItem && itemIdx < 2) {
            u8g2.drawStr(108, y, "<>");
        }
        u8g2.setDrawColor(1);
    }

    // Scrollbar if needed
    if (ITEM_COUNT > visibleLines) {
        int barHeight = max(4, (64 - startY) * visibleLines / ITEM_COUNT);
        int barY = startY + (int)((long)(64 - startY - barHeight) * _scrollOffset / max(1, ITEM_COUNT - visibleLines));
        u8g2.drawBox(126, barY, 2, barHeight);
    }
}

void PenSettingsScreen::onEncoderTurn(int direction) {
    if (_editing) {
        float step = 0.5;
        if (_selectedItem == 0) {
            pen_up_z = constrain(pen_up_z + direction * step, 0.0, Z_MAX_POS);
        } else if (_selectedItem == 1) {
            pen_down_z = constrain(pen_down_z + direction * step, 0.0, Z_MAX_POS);
        }
    } else {
        _selectedItem = clampInt(_selectedItem + direction, 0, ITEM_COUNT - 1);
        _scrollOffset = calcScrollOffset(_selectedItem, _scrollOffset, 4);
    }
}

void PenSettingsScreen::onButtonClick() {
    if (_selectedItem == 4) { // Back
        menuBack();
        return;
    }

    if (_selectedItem == 2) {
        // Test pen: move Z down then up
        long downSteps = (long)(pen_down_z * Z_STEPS_PER_MM);
        long upSteps = (long)(pen_up_z * Z_STEPS_PER_MM);
        stepperControl.enableSteppers();
        stepperControl.setAxisMaxSpeed('Z', MAX_VELOCITY_Z * Z_STEPS_PER_MM);
        stepperControl.setAxisAcceleration('Z', MAX_ACCEL_Z * Z_STEPS_PER_MM);
        stepperControl.moveAxisTo('Z', downSteps);
        while (stepperControl.runAxis('Z')) { wdt_reset(); }
        delay(500);
        wdt_reset();
        stepperControl.moveAxisTo('Z', upSteps);
        while (stepperControl.runAxis('Z')) { wdt_reset(); }
        return;
    }

    if (_selectedItem == 3) {
        // Raw Z motor test - bypasses AccelStepper, directly toggles pins
        stepperControl.testZMotorDirect(800, 500);
        return;
    }

    // Toggle editing mode for Z values (items 0, 1)
    _editing = !_editing;
}

void PenSettingsScreen::onEnter() {
    _selectedItem = 0;
    _scrollOffset = 0;
    _editing = false;
}

void PenSettingsScreen::onExit() {
    _editing = false;
}

//===========================================================================
// MotionSettingsScreen - adjust speed factor
//===========================================================================

void MotionSettingsScreen::draw() {
    drawTitleBar(u8g2, "Motion");

    u8g2.setFont(u8g2_font_6x10_tf);
    const int startY = 14;
    const int lineH = 13;

    // Item 0: Speed Factor (M220)
    int y = startY + 9;
    if (_selectedItem == 0) {
        u8g2.drawBox(0, startY, 128, lineH);
        u8g2.setDrawColor(0);
    }
    char buf[24];
    snprintf(buf, sizeof(buf), "Speed: %d%%", (int)speed_factor);
    u8g2.drawStr(2, y, buf);
    if (_editing && _selectedItem == 0) u8g2.drawStr(115, y, "<>");
    u8g2.setDrawColor(1);

    // Item 1: Back
    y = startY + lineH + 9;
    if (_selectedItem == 1) {
        u8g2.drawBox(0, startY + lineH, 128, lineH);
        u8g2.setDrawColor(0);
    }
    u8g2.drawStr(2, y, "Back");
    u8g2.setDrawColor(1);

    // Show speed bar (map 0-200% to 0-100 for progress bar)
    int pct = (int)speed_factor;
    drawProgressBar(u8g2, 10, 48, 108, 8, clampInt(pct, 0, 200) / 2);
}

void MotionSettingsScreen::onEncoderTurn(int direction) {
    if (_editing && _selectedItem == 0) {
        int pct = (int)speed_factor + direction * 1;
        speed_factor = (float)clampInt(pct, 10, 200);
    } else {
        _selectedItem = clampInt(_selectedItem + direction, 0, ITEM_COUNT - 1);
    }
}

void MotionSettingsScreen::onButtonClick() {
    if (_selectedItem == 1) {
        menuBack();
        return;
    }
    _editing = !_editing;
}

void MotionSettingsScreen::onEnter() {
    _selectedItem = 0;
    _editing = false;
}

void MotionSettingsScreen::onExit() {
    _editing = false;
}

//===========================================================================
// InfoScreen - firmware info, SRAM, uptime, lines plotted
//===========================================================================

void InfoScreen::draw() {
    drawTitleBar(u8g2, "Info");

    u8g2.setFont(u8g2_font_5x7_tf);

    u8g2.drawStr(2, 22, "FW: SimplePlotter " FIRMWARE_VERSION_STRING);

    char buf[24];
    snprintf(buf, sizeof(buf), "Free RAM: %d B", freeMemory());
    u8g2.drawStr(2, 31, buf);

    char uptimeBuf[12];
    formatUptime(millis(), uptimeBuf, sizeof(uptimeBuf));
    snprintf(buf, sizeof(buf), "Uptime: %s", uptimeBuf);
    u8g2.drawStr(2, 40, buf);

    snprintf(buf, sizeof(buf), "Lines: %lu", lines_plotted);
    u8g2.drawStr(2, 49, buf);

    u8g2.setFont(u8g2_font_4x6_tf);
    u8g2.drawStr(2, 63, "Click: Back");
}

void InfoScreen::onEncoderTurn(int direction) {
    // Nothing to scroll
}

void InfoScreen::onButtonClick() {
    menuBack();
}

//===========================================================================
// SDScreen - file browser + execution control
//===========================================================================

volatile SDExecState sd_exec_state = SD_EXEC_IDLE;
char sd_exec_filename[13] = {0};

void SDScreen::draw() {
    drawTitleBar(u8g2, "SD Card");

    // If currently executing, show progress
    if (_showingExec && sd_exec_state != SD_EXEC_IDLE) {
        u8g2.setFont(u8g2_font_5x7_tf);

        char buf[24];
        snprintf(buf, sizeof(buf), "File: %.12s", sd_exec_filename);
        u8g2.drawStr(2, 22, buf);

        if (sd_exec_state == SD_EXEC_RUNNING) {
            u8g2.drawStr(2, 32, "Printing...");
        } else if (sd_exec_state == SD_EXEC_PAUSED) {
            u8g2.drawStr(2, 32, "PAUSED");
        } else if (sd_exec_state == SD_EXEC_DONE) {
            u8g2.drawStr(2, 32, "Done!");
        }

        uint8_t pct = sdCard.progressPercent();
        drawProgressBar(u8g2, 2, 38, 124, 8, pct);

        snprintf(buf, sizeof(buf), "%d%%", pct);
        u8g2.drawStr(55, 55, buf);

        u8g2.setFont(u8g2_font_4x6_tf);
        if (sd_exec_state == SD_EXEC_DONE) {
            u8g2.drawStr(2, 63, "Click: Back");
        } else {
            u8g2.drawStr(2, 63, "Click:Pause Long:Cancel");
        }
        return;
    }

    // File browser mode
    u8g2.setFont(u8g2_font_6x10_tf);

    if (!sdCard.isPresent()) {
        u8g2.drawStr(10, 30, "No SD card");
        u8g2.drawStr(10, 42, "Insert card...");
        u8g2.setFont(u8g2_font_4x6_tf);
        u8g2.drawStr(2, 63, "Click: Back");
        return;
    }

    if (_fileCount == 0) {
        u8g2.drawStr(10, 30, "No .gcode files");
        u8g2.setFont(u8g2_font_4x6_tf);
        u8g2.drawStr(2, 63, "Click: Back");
        return;
    }

    // Draw file list as menu with Back option at end
    const char* ptrs[SD_MAX_FILES + 1];
    for (int i = 0; i < _fileCount; i++) {
        ptrs[i] = _fileList[i];
    }
    ptrs[_fileCount] = "Back";
    int totalItems = _fileCount + 1;
    drawMenuList(u8g2, ptrs, totalItems, _selectedItem, _scrollOffset);
}

void SDScreen::onEncoderTurn(int direction) {
    if (_showingExec) return;
    if (_fileCount == 0) return;
    _selectedItem = clampInt(_selectedItem + direction, 0, _fileCount); // +1 for Back
    _scrollOffset = calcScrollOffset(_selectedItem, _scrollOffset, 4);
}

void SDScreen::onButtonClick() {
    // Execution mode
    if (_showingExec) {
        if (sd_exec_state == SD_EXEC_RUNNING) {
            sd_exec_state = SD_EXEC_PAUSED;
            Buzzer::playPlotPause();
        } else if (sd_exec_state == SD_EXEC_PAUSED) {
            sd_exec_state = SD_EXEC_RUNNING;
            Buzzer::playPlotStart();
        } else if (sd_exec_state == SD_EXEC_DONE) {
            sd_exec_state = SD_EXEC_IDLE;
            _showingExec = false;
            sdCard.closeFile();
            onEnter(); // Re-scan files
        }
        return;
    }

    // File browser mode
    if (_fileCount == 0 || !sdCard.isPresent()) {
        menuBack();
        return;
    }

    // Back option (last item after files)
    if (_selectedItem == _fileCount) {
        menuBack();
        return;
    }

    // Start executing selected file
    strncpy(sd_exec_filename, _fileList[_selectedItem], 12);
    sd_exec_filename[12] = '\0';

    if (sdCard.openFile(sd_exec_filename)) {
        sd_exec_state = SD_EXEC_RUNNING;
        _showingExec = true;
        plotPreviewScreen.clear();
        Buzzer::playPlotStart();
    }
}

void SDScreen::onEnter() {
    _selectedItem = 0;
    _scrollOffset = 0;
    _showingExec = false;

    if (sdCard.isPresent()) {
        if (!sdCard.isInitialized()) {
            sdCard.init();
        }
        _fileCount = sdCard.listGCodeFiles(_fileList, SD_MAX_FILES);
    } else {
        _fileCount = 0;
    }
}

void SDScreen::onExit() {
    // Don't close file if executing - main loop handles that
}

//===========================================================================
// PlotPreviewScreen - shows scaled XY path during G-code execution
//===========================================================================

PlotPreviewScreen plotPreviewScreen;

void PlotPreviewScreen::draw() {
    // Draw border for plot area (top portion of screen)
    u8g2.drawFrame(0, 0, 128, 48);

    // Draw all stored segments
    for (int i = 0; i < _segmentCount; i++) {
        u8g2.drawLine(_segments[i].x0, _segments[i].y0,
                       _segments[i].x1, _segments[i].y1);
    }

    // Status bar below preview
    u8g2.setFont(u8g2_font_4x6_tf);
    char buf[28];
    snprintf(buf, sizeof(buf), "Lines:%lu Spd:%d%%", lines_plotted, (int)speed_factor);
    u8g2.drawStr(0, 55, buf);

    // Progress bar at bottom
    drawProgressBar(u8g2, 0, 57, 100, 7, _progress);

    // Percentage text
    snprintf(buf, sizeof(buf), "%d%%", _progress);
    u8g2.drawStr(104, 63, buf);
}

void PlotPreviewScreen::onButtonClick() {
    menuBack();
}

void PlotPreviewScreen::onEnter() {
    // Don't clear on enter - preserve accumulated preview
}

void PlotPreviewScreen::clear() {
    _segmentCount = 0;
    _progress = 0;
}

uint8_t PlotPreviewScreen::_mapX(float x) {
    // Map machine X (0..X_MAX_POS) to screen X (1..126)
    float scaled = (x / X_MAX_POS) * 125.0;
    return (uint8_t)constrain((int)scaled + 1, 1, 126);
}

uint8_t PlotPreviewScreen::_mapY(float y) {
    // Map machine Y (0..Y_MAX_POS) to screen Y (1..54), inverted (screen Y=0 is top)
    float scaled = (y / Y_MAX_POS) * 53.0;
    return (uint8_t)constrain(54 - (int)scaled, 1, 54);
}

void PlotPreviewScreen::addSegment(float fromX, float fromY, float toX, float toY) {
    if (_segmentCount >= PLOT_PREVIEW_MAX_SEGMENTS) {
        // Shift array: remove oldest, add newest
        memmove(&_segments[0], &_segments[1], sizeof(PreviewSegment) * (PLOT_PREVIEW_MAX_SEGMENTS - 1));
        _segmentCount = PLOT_PREVIEW_MAX_SEGMENTS - 1;
    }

    _segments[_segmentCount].x0 = _mapX(fromX);
    _segments[_segmentCount].y0 = _mapY(fromY);
    _segments[_segmentCount].x1 = _mapX(toX);
    _segments[_segmentCount].y1 = _mapY(toY);
    _segmentCount++;
}
