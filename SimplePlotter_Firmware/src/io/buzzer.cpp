#include "buzzer.h"
#include <avr/wdt.h>

namespace Buzzer {

    void beep(int durationMs) {
        tone(BEEPER_PIN, 2000, durationMs);
    }

    void playNote(int frequency, int durationMs) {
        wdt_reset();
        if (frequency > 0) {
            tone(BEEPER_PIN, frequency, durationMs);
        } else {
            noTone(BEEPER_PIN);
        }
        delay(durationMs);
        noTone(BEEPER_PIN);
        delay(20); // Short gap between notes
    }

    // Startup: ascending C-E-G-C5 arpeggio (cheerful boot jingle)
    void playStartup() {
        playNote(NOTE_C4, 100);
        playNote(NOTE_E4, 100);
        playNote(NOTE_G4, 100);
        playNote(NOTE_C5, 200);
    }

    // Plot start: two quick ascending notes (let's go!)
    void playPlotStart() {
        playNote(NOTE_G4, 80);
        playNote(NOTE_C5, 120);
    }

    // Plot finish: triumphant ascending + long finish note
    void playPlotFinish() {
        playNote(NOTE_C5, 100);
        playNote(NOTE_E5, 100);
        playNote(NOTE_G5, 300);
    }

    // Plot stop: two descending notes (cancelled)
    void playPlotStop() {
        playNote(NOTE_G4, 100);
        playNote(NOTE_C4, 200);
    }

    // Plot pause: single mid-tone beep
    void playPlotPause() {
        playNote(NOTE_E4, 200);
    }

    // Homing done: quick double beep
    void playHomingDone() {
        playNote(NOTE_C5, 80);
        playNote(NOTE_REST, 50);
        playNote(NOTE_C5, 80);
    }

    // Error: low descending tone
    void playError() {
        playNote(NOTE_A4, 150);
        playNote(NOTE_F4, 150);
        playNote(NOTE_D4, 300);
    }
}
