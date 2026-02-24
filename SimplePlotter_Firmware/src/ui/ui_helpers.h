#ifndef UI_HELPERS_H
#define UI_HELPERS_H

#include <U8g2lib.h>

// Draw a title bar at the top of the screen
void drawTitleBar(U8G2 &u8g2, const char* title);

// Draw a scrollable menu list with highlight on selected item
// Returns the first visible item index (for scroll management)
void drawMenuList(U8G2 &u8g2, const char* const items[], int itemCount,
                  int selectedIndex, int scrollOffset, int startY = 14);

// Draw a progress bar
void drawProgressBar(U8G2 &u8g2, int x, int y, int width, int height, int percent);

// Draw a spinner animation (rotating line)
void drawSpinner(U8G2 &u8g2, int cx, int cy, int radius, int frame);

// Format uptime from millis to HH:MM:SS string
void formatUptime(unsigned long ms, char* buffer, int bufSize);

// Get free SRAM on ATmega2560
int freeMemory();

// Clamp an integer between min and max
int clampInt(int value, int minVal, int maxVal);

// Calculate scroll offset to keep selected item visible
int calcScrollOffset(int selectedIndex, int scrollOffset, int visibleItems);

#endif // UI_HELPERS_H
