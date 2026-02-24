#include "ui_helpers.h"
#include <Arduino.h>

void drawTitleBar(U8G2 &u8g2, const char* title) {
    u8g2.setFont(u8g2_font_6x10_tf);
    u8g2.setDrawColor(1);
    u8g2.drawBox(0, 0, 128, 11);
    u8g2.setDrawColor(0);
    u8g2.drawStr(2, 9, title);
    u8g2.setDrawColor(1);
}

void drawMenuList(U8G2 &u8g2, const char* const items[], int itemCount,
                  int selectedIndex, int scrollOffset, int startY) {
    u8g2.setFont(u8g2_font_6x10_tf);
    const int lineHeight = 11;
    const int visibleLines = (64 - startY) / lineHeight;

    for (int i = 0; i < visibleLines && (scrollOffset + i) < itemCount; i++) {
        int itemIdx = scrollOffset + i;
        int y = startY + i * lineHeight + 9;

        if (itemIdx == selectedIndex) {
            // Draw highlight bar
            u8g2.drawBox(0, startY + i * lineHeight, 128, lineHeight);
            u8g2.setDrawColor(0);
            u8g2.drawStr(2, y, items[itemIdx]);
            u8g2.setDrawColor(1);
        } else {
            u8g2.drawStr(2, y, items[itemIdx]);
        }
    }

    // Draw scrollbar if needed
    if (itemCount > visibleLines) {
        int barHeight = max(4, (64 - startY) * visibleLines / itemCount);
        int barY = startY + (int)((long)(64 - startY - barHeight) * scrollOffset / max(1, itemCount - visibleLines));
        u8g2.drawBox(126, barY, 2, barHeight);
    }
}

void drawProgressBar(U8G2 &u8g2, int x, int y, int width, int height, int percent) {
    percent = constrain(percent, 0, 100);
    u8g2.drawFrame(x, y, width, height);
    int fillWidth = (int)((long)(width - 2) * percent / 100);
    if (fillWidth > 0) {
        u8g2.drawBox(x + 1, y + 1, fillWidth, height - 2);
    }
}

void drawSpinner(U8G2 &u8g2, int cx, int cy, int radius, int frame) {
    const int segments = 8;
    int seg = frame % segments;
    float angle = seg * 2.0 * PI / segments;
    int x2 = cx + (int)(cos(angle) * radius);
    int y2 = cy + (int)(sin(angle) * radius);
    u8g2.drawLine(cx, cy, x2, y2);
    // Draw a small dot at the end
    u8g2.drawDisc(x2, y2, 1);
}

void formatUptime(unsigned long ms, char* buffer, int bufSize) {
    unsigned long totalSec = ms / 1000;
    unsigned long hours = totalSec / 3600;
    unsigned long minutes = (totalSec % 3600) / 60;
    unsigned long seconds = totalSec % 60;
    snprintf(buffer, bufSize, "%02lu:%02lu:%02lu", hours, minutes, seconds);
}

// Get free SRAM by checking gap between heap and stack
extern unsigned int __heap_start;
extern void *__brkval;

int freeMemory() {
    int v;
    return (int)&v - (__brkval == 0 ? (int)&__heap_start : (int)__brkval);
}

int clampInt(int value, int minVal, int maxVal) {
    if (value < minVal) return minVal;
    if (value > maxVal) return maxVal;
    return value;
}

int calcScrollOffset(int selectedIndex, int scrollOffset, int visibleItems) {
    if (selectedIndex < scrollOffset) {
        return selectedIndex;
    }
    if (selectedIndex >= scrollOffset + visibleItems) {
        return selectedIndex - visibleItems + 1;
    }
    return scrollOffset;
}
