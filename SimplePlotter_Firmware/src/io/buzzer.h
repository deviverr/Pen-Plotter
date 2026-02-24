#ifndef BUZZER_H
#define BUZZER_H

#include <Arduino.h>
#include "../config.h"

// Musical note frequencies (Hz)
#define NOTE_C4  262
#define NOTE_D4  294
#define NOTE_E4  330
#define NOTE_F4  349
#define NOTE_G4  392
#define NOTE_A4  440
#define NOTE_B4  494
#define NOTE_C5  523
#define NOTE_D5  587
#define NOTE_E5  659
#define NOTE_G5  784
#define NOTE_REST 0

namespace Buzzer {
    void beep(int durationMs);
    void playNote(int frequency, int durationMs);

    // Event melodies
    void playStartup();     // Boot jingle
    void playPlotStart();   // Plot begins
    void playPlotFinish();  // Plot completed successfully
    void playPlotStop();    // Plot stopped/canceled by user
    void playPlotPause();   // Plot paused
    void playHomingDone();  // Homing complete
    void playError();       // Error occurred
}

#endif // BUZZER_H
