// SimplePlotter_Firmware/src/main.cpp

#include <Arduino.h>
#include "config.h"
#include "motion/stepper_control.h"
#include "motion/kinematics.h"
#include "motion/homing.h"
#include "gcode/parser.h"
#include "gcode/buffer.h"
#include "io/serial_handler.h"
#include "io/endstops.h"
#include "ui/lcd_menu.h"
#include "ui/screens.h"
#include "io/sd_card.h"
#include "io/potentiometer.h"
#include "io/buzzer.h"
#include <avr/wdt.h>

// Machine state variables
Point3D current_position_mm(0.0, 0.0, 0.0); // Current machine position in millimeters
bool absolute_mode = true; // G90 (absolute) or G91 (relative) positioning
float current_feedrate_mm_min = 0; // Current feedrate in mm/min (for G0/G1)
float speed_factor = 100.0; // M220 S<percent> (100% by default)

// Endstop-aware jog: static state for callback
static bool _jog_check_x = false;
static bool _jog_check_y = false;
static bool _jog_check_z = false;

static bool jogEndstopCheck() {
    if (_jog_check_x && endstops.isTriggered('X')) return true;
    if (_jog_check_y && endstops.isTriggered('Y')) return true;
    if (_jog_check_z && endstops.isTriggered('Z')) return true;
    return false;
}

// Stepper idle timeout management definitions (declared extern in globals.h)
long stepper_disable_timeout_ms = 0; // Default: 0 (no timeout)
unsigned long last_stepper_activity_time = 0;

void setup() {
    // Disable watchdog timer first in case a previous reset was due to WDT
    wdt_disable(); 
    wdt_enable(WDTO_8S); // Enable watchdog timer with 8-second timeout

    // Initialize serial communication
    serialHandler.init();
    serialHandler.sendFirmwareInfo();
    serialHandler.sendInfo("SimplePlotter Firmware starting...");

    // Initialize endstops
    endstops.init();

    // Initialize stepper control
    stepperControl.init();

    // Set initial position of steppers (corresponds to 0,0,0)
    stepperControl.setCurrentPosition(0, 0, 0);

    // Initial feedrate set to a default (e.g., rapid feedrate)
    current_feedrate_mm_min = MAX_VELOCITY_XY * 60; // Convert mm/s to mm/min

    // Setup SD detect pin
    pinMode(SD_DETECT_PIN, INPUT_PULLUP);

    // Initialize potentiometer
    potentiometer.init();

    // Initialize LCD menu system
    lcdMenu.init();

    // Initialize stepper timeout. Use DISABLE_STEPPERS_AFTER_IDLE_S from config.h
    if (DISABLE_STEPPERS_AFTER_IDLE_S > 0) {
        stepper_disable_timeout_ms = (long)DISABLE_STEPPERS_AFTER_IDLE_S * 1000UL;
    } else {
        stepper_disable_timeout_ms = 0; // Never disable
    }
    last_stepper_activity_time = millis(); // Initial activity

    // Startup melody
    Buzzer::playStartup();
}

void loop() {
    wdt_reset(); // Pet the watchdog timer

    // Handle incoming serial data and populate G-code buffer
    serialHandler.handleSerialInput();

    // Update potentiometer - only override speed_factor when pot is physically turned.
    // This allows M220 (serial) and LCD speed changes to persist until the knob moves.
    potentiometer.update();
    if (potentiometer.hasChanged()) {
        speed_factor = (float)potentiometer.getSpeedPercent();
    }

    // Update LCD menu system (handles encoder input and display refresh)
    lcdMenu.update();

    // Check for stepper timeout
    if (stepper_disable_timeout_ms > 0 && millis() - last_stepper_activity_time > (unsigned long)stepper_disable_timeout_ms) {
        if (!stepperControl.is_steppers_disabled()) {
            stepperControl.disableSteppers();
            serialHandler.sendInfo("Steppers auto-disabled due to idle timeout.");
        }
    }

    // Feed G-code lines from SD card when executing
    if (sd_exec_state == SD_EXEC_RUNNING && !gcodeBuffer.isFull()) {
        char lineBuf[GCODE_MAX_LENGTH];
        if (sdCard.readLine(lineBuf, GCODE_MAX_LENGTH)) {
            // Skip empty lines and comments
            if (lineBuf[0] != '\0' && lineBuf[0] != ';') {
                // Strip inline comments
                char* semi = strchr(lineBuf, ';');
                if (semi) *semi = '\0';
                // Parse and add to buffer
                ParsedGCodeCommand sdCmd = gcodeParser.parse(lineBuf);
                if (sdCmd.type != GCODE_UNKNOWN) {
                    gcodeBuffer.push(sdCmd);
                }
            }
            plotPreviewScreen.setProgress(sdCard.progressPercent());
        } else {
            // File done
            sd_exec_state = SD_EXEC_DONE;
            Buzzer::playPlotFinish();
            sdCard.closeFile();
        }
    }

    // If there are commands in the buffer, process the next one
    if (!gcodeBuffer.isEmpty()) {
        ParsedGCodeCommand cmd;
        if (gcodeBuffer.pop(cmd)) {
            // Process the command
            switch (cmd.type) {
                case GCODE_G0: // Rapid Move
                case GCODE_G1: { // Linear Move
                    Point3D target_mm = current_position_mm;
                    float feedrate_mm_min = current_feedrate_mm_min;

                    // Apply parameters
                    if (cmd.move.has_x) target_mm.x = cmd.move.x_val;
                    if (cmd.move.has_y) target_mm.y = cmd.move.y_val;
                    if (cmd.move.has_z) target_mm.z = cmd.move.z_val;
                    if (cmd.move.has_f) feedrate_mm_min = cmd.move.f_val;

                    // Apply relative positioning if G91 is active
                    if (!absolute_mode) {
                        if (cmd.move.has_x) target_mm.x = current_position_mm.x + cmd.move.x_val;
                        if (cmd.move.has_y) target_mm.y = current_position_mm.y + cmd.move.y_val;
                        if (cmd.move.has_z) target_mm.z = current_position_mm.z + cmd.move.z_val;
                    }
                    
                    // Apply speed factor (M220)
                    if (speed_factor != 100.0f) {
                        float base_feedrate = feedrate_mm_min;
                        feedrate_mm_min = feedrate_mm_min * (speed_factor / 100.0);
                        char spd_msg[64];
                        snprintf(spd_msg, sizeof(spd_msg), "Feed=%d (base=%d * %d%%)",
                                 (int)feedrate_mm_min, (int)base_feedrate, (int)speed_factor);
                        serialHandler.sendInfo(spd_msg);
                    }

                    // Convert feedrate to mm/s
                    float feedrate_mm_s = feedrate_mm_min / 60.0;

                    // Calculate Euclidean distance for jump detection
                    float dx = target_mm.x - current_position_mm.x;
                    float dy = target_mm.y - current_position_mm.y;
                    float dz = target_mm.z - current_position_mm.z;
                    float jump_distance_sq = (dx*dx) + (dy*dy) + (dz*dz);

                    if (jump_distance_sq > (MAX_ALLOWED_JUMP_MM * MAX_ALLOWED_JUMP_MM)) {
                        serialHandler.sendError(ERR_OUT_OF_RANGE, "Impossible position jump detected");
                        serialHandler.sendOK();
                        break;
                    }

                    // Check soft limits — only in absolute mode (G90).
                    // In relative mode (G91), jogging must work without homing.
                    if (absolute_mode) {
                        // Per-axis homing check: only require homing for axes being commanded
                        if ((cmd.move.has_x && !homing.isHomedX()) ||
                            (cmd.move.has_y && !homing.isHomedY()) ||
                            (cmd.move.has_z && !homing.isHomedZ())) {
                            serialHandler.sendError(ERR_NOT_HOMED, "Required axis not homed");
                            serialHandler.sendOK();
                            break;
                        }
                        if (!kinematics.isValidPosition(target_mm)) {
                            serialHandler.sendError(ERR_OUT_OF_RANGE, "Target position out of bounds");
                            serialHandler.sendOK();
                            break;
                        }
                    }

                    // Convert target mm to steps
                    long target_steps[3];
                    kinematics.mmToSteps(target_mm, target_steps);

                    // Compute per-axis speeds proportional to move vector so all axes arrive together
                    float total_dist = sqrtf(dx*dx + dy*dy + dz*dz);
                    float vx, vy, vz;
                    if (total_dist > 0.001f) {
                        vx = feedrate_mm_s * (fabsf(dx) / total_dist);
                        vy = feedrate_mm_s * (fabsf(dy) / total_dist);
                        vz = feedrate_mm_s * (fabsf(dz) / total_dist);
                    } else {
                        vx = feedrate_mm_s;
                        vy = feedrate_mm_s;
                        vz = MAX_VELOCITY_Z;
                    }
                    vz = min(vz, (float)MAX_VELOCITY_Z);

                    // Set speeds and accelerations for movement
                    stepperControl.setMaxSpeed(
                        vx * X_STEPS_PER_MM,
                        vy * Y_STEPS_PER_MM,
                        vz * Z_STEPS_PER_MM
                    );
                    // Use configured MAX_ACCEL for G0/G1
                    stepperControl.setAcceleration(
                        MAX_ACCEL_X * X_STEPS_PER_MM,
                        MAX_ACCEL_Y * Y_STEPS_PER_MM,
                        MAX_ACCEL_Z * Z_STEPS_PER_MM
                    );

                    // Debug: log target steps (disabled by default to avoid flooding serial)
#ifdef DEBUG_MOVES
                    {
                        char dbg[96];
                        snprintf(dbg, sizeof(dbg), "MOVE to X=%ld Y=%ld Z=%ld (from X=%ld Y=%ld Z=%ld)",
                                 target_steps[0], target_steps[1], target_steps[2],
                                 stepperControl.getCurrentXSteps(),
                                 stepperControl.getCurrentYSteps(),
                                 stepperControl.getCurrentZSteps());
                        serialHandler.sendInfo(dbg);
                    }
#endif

                    // Move to target
                    stepperControl.enableSteppers();
                    stepperControl.moveTo(target_steps[0], target_steps[1], target_steps[2]);

                    // Endstop-safe jogging: in relative mode, check endstops for axes moving toward home
                    char endstop_triggered = '\0';
                    if (!absolute_mode) {
                        // Check if jog is moving TOWARD the home endstop for each axis
                        // HOME_DIR_X=1 means endstop is at max, so positive jog = toward endstop
                        // HOME_DIR_Y=-1 means endstop is at min, so negative jog = toward endstop
                        _jog_check_x = cmd.move.has_x && ((HOME_DIR_X < 0) ? (cmd.move.x_val < -0.001f) : (cmd.move.x_val > 0.001f));
                        _jog_check_y = cmd.move.has_y && ((HOME_DIR_Y < 0) ? (cmd.move.y_val < -0.001f) : (cmd.move.y_val > 0.001f));
                        _jog_check_z = cmd.move.has_z && ((HOME_DIR_Z < 0) ? (cmd.move.z_val < -0.001f) : (cmd.move.z_val > 0.001f));

                        if (_jog_check_x || _jog_check_y || _jog_check_z) {
                            bool stopped = stepperControl.runBlockingWithCheck(jogEndstopCheck);
                            if (stopped) {
                                if (_jog_check_x && endstops.isTriggered('X')) endstop_triggered = 'X';
                                else if (_jog_check_y && endstops.isTriggered('Y')) endstop_triggered = 'Y';
                                else if (_jog_check_z && endstops.isTriggered('Z')) endstop_triggered = 'Z';
                            }
                            _jog_check_x = _jog_check_y = _jog_check_z = false;
                        } else {
                            stepperControl.runBlocking();
                        }
                    } else {
                        stepperControl.runBlocking();
                    }
                    // Steppers stay enabled - idle timeout handles disabling

                    // Feed plot preview with XY segments (only for drawing moves, not Z-only)
                    if (cmd.move.has_x || cmd.move.has_y) {
                        plotPreviewScreen.addSegment(
                            current_position_mm.x, current_position_mm.y,
                            target_mm.x, target_mm.y);
                    }

                    // Handle endstop hit during jog: auto-home the triggered axis
                    if (endstop_triggered != '\0') {
                        // Read actual stepper positions since move was interrupted
                        current_position_mm.x = (float)stepperControl.getCurrentXSteps() / X_STEPS_PER_MM;
                        current_position_mm.y = (float)stepperControl.getCurrentYSteps() / Y_STEPS_PER_MM;
                        current_position_mm.z = (float)stepperControl.getCurrentZSteps() / Z_STEPS_PER_MM;

                        char msg[64];
                        snprintf(msg, sizeof(msg), "Endstop hit on %c during jog, auto-homing", endstop_triggered);
                        serialHandler.sendInfo(msg);
                        homing.homeAxis(endstop_triggered);
                        if (endstop_triggered == 'X') current_position_mm.x = (HOME_DIR_X == 1) ? X_MAX_POS : 0.0f;
                        else if (endstop_triggered == 'Y') current_position_mm.y = (HOME_DIR_Y == 1) ? Y_MAX_POS : 0.0f;
                        else if (endstop_triggered == 'Z') current_position_mm.z = PEN_UP_Z;
                        stepperControl.setCurrentPosition(
                            kinematics.mmToStepsX(current_position_mm.x),
                            kinematics.mmToStepsY(current_position_mm.y),
                            kinematics.mmToStepsZ(current_position_mm.z));
                    } else {
                        // Normal completion
                        current_position_mm = target_mm;
                    }
                    lines_plotted++;
                    last_stepper_activity_time = millis();
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_G28: { // Home
                    stepperControl.enableSteppers(); // Ensure steppers are enabled for homing
                    bool homing_success = false;
                    if (cmd.g28_args.home_all) {
                        homing_success = homing.homeAllAxes();
                    } else {
                        // Home specific axis/axes
                        bool x_success = true, y_success = true, z_success = true;
                        if (cmd.g28_args.home_x) x_success = homing.homeAxis('X');
                        if (cmd.g28_args.home_y) y_success = homing.homeAxis('Y');
                        if (cmd.g28_args.home_z) z_success = homing.homeAxis('Z');
                        homing_success = x_success && y_success && z_success;
                    }
                    // Update position based on which axes actually homed
                    // X homes to max (right side), so position = X_MAX_POS
                    // Y homes to min (front), so position = 0
                    // Z homes to min, then moves to Z_HOME_POSITION
                    if (homing.isHomedX()) current_position_mm.x = (HOME_DIR_X == 1) ? X_MAX_POS : 0.0f;
                    if (homing.isHomedY()) current_position_mm.y = (HOME_DIR_Y == 1) ? Y_MAX_POS : 0.0f;
                    if (homing.isHomedZ()) current_position_mm.z = Z_HOME_POSITION;
                    // Sync stepper positions with current_position_mm
                    stepperControl.setCurrentPosition(
                        kinematics.mmToStepsX(current_position_mm.x),
                        kinematics.mmToStepsY(current_position_mm.y),
                        kinematics.mmToStepsZ(current_position_mm.z));

                    if (homing_success) {
                        serialHandler.sendInfo("Homing complete.");
                        Buzzer::playHomingDone();
                    } else {
                        serialHandler.sendError(ERR_HOMING_FAILED, "Partial homing - check serial log for details.");
                        Buzzer::playError();
                    }
                    // Steppers stay enabled - idle timeout handles disabling
                    last_stepper_activity_time = millis(); // Update activity
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_M84: { // Disable Steppers
                    if (cmd.m84_args.has_s) {
                        if (cmd.m84_args.s_val == 0) { // M84 S0 means disable indefinitely
                            stepper_disable_timeout_ms = 0; // Never timeout
                            stepperControl.disableSteppers();
                            serialHandler.sendInfo("Steppers permanently disabled (timeout 0).");
                        } else { // M84 S<seconds>
                            stepper_disable_timeout_ms = (long)cmd.m84_args.s_val * 1000UL;
                            stepperControl.disableSteppers(); // Disable now, then re-enable on next activity
                            last_stepper_activity_time = millis(); // Reset timer
                            serialHandler.sendInfo(("Stepper timeout set to " + String(cmd.m84_args.s_val) + "s. Steppers disabled.").c_str());
                        }
                    } else { // M84 without S means disable immediately and use default timeout from config.h
                        stepperControl.disableSteppers();
                        if (DISABLE_STEPPERS_AFTER_IDLE_S > 0) {
                            stepper_disable_timeout_ms = (long)DISABLE_STEPPERS_AFTER_IDLE_S * 1000UL;
                        } else {
                            stepper_disable_timeout_ms = 0; // Never disable
                        }
                        last_stepper_activity_time = millis(); // Reset timer
                        serialHandler.sendInfo("Steppers disabled. Default timeout applied.");
                    }
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_G90: // Absolute Positioning
                    absolute_mode = true;
                    serialHandler.sendInfo("Absolute positioning mode (G90)");
                    serialHandler.sendOK();
                    break;
                case GCODE_G91: // Relative Positioning
                    absolute_mode = false;
                    serialHandler.sendInfo("Relative positioning mode (G91)");
                    serialHandler.sendOK();
                    break;
                case GCODE_G92: { // Set Position
                    // Set current position to new values without moving
                    if (cmd.g92_args.has_x) current_position_mm.x = cmd.g92_args.x_val;
                    if (cmd.g92_args.has_y) current_position_mm.y = cmd.g92_args.y_val;
                    if (cmd.g92_args.has_z) current_position_mm.z = cmd.g92_args.z_val;
                    
                    // Also update AccelStepper's internal position for consistency
                    long new_x_steps = kinematics.mmToStepsX(current_position_mm.x);
                    long new_y_steps = kinematics.mmToStepsY(current_position_mm.y);
                    long new_z_steps = kinematics.mmToStepsZ(current_position_mm.z);
                    stepperControl.setCurrentPosition(new_x_steps, new_y_steps, new_z_steps);
                    serialHandler.sendInfo("Current position set.");
                    last_stepper_activity_time = millis(); // Update activity
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_M220: { // Set Speed Factor
                    if (cmd.m220_args.has_s) {
                        speed_factor = constrain(cmd.m220_args.s_val, 1, 999); // Constrain between 1% and 999%
                        serialHandler.sendInfo(("Speed factor set to " + String(speed_factor) + "%").c_str());
                    }
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_M0:   // Unconditional Stop
                    serialHandler.sendInfo("M0: Stop.");
                    while(!gcodeBuffer.isEmpty()) {
                        ParsedGCodeCommand dummy_cmd;
                        gcodeBuffer.pop(dummy_cmd);
                    }
                    if (sd_exec_state == SD_EXEC_RUNNING || sd_exec_state == SD_EXEC_PAUSED) {
                        sd_exec_state = SD_EXEC_DONE;
                        sdCard.closeFile();
                    }
                    stepperControl.disableSteppers();
                    serialHandler.sendOK();
                    break;
                case GCODE_M24:  // Resume execution
                    if (sd_exec_state == SD_EXEC_PAUSED) {
                        sd_exec_state = SD_EXEC_RUNNING;
                        serialHandler.sendInfo("Execution resumed.");
                    } else {
                        serialHandler.sendInfo("Nothing to resume.");
                    }
                    serialHandler.sendOK();
                    break;
                case GCODE_M25:  // Pause execution
                    if (sd_exec_state == SD_EXEC_RUNNING) {
                        sd_exec_state = SD_EXEC_PAUSED;
                        serialHandler.sendInfo("Execution paused.");
                    } else {
                        serialHandler.sendInfo("Not running.");
                    }
                    serialHandler.sendOK();
                    break;
                case GCODE_M114: // Get Current Position
                    serialHandler.sendPosition(current_position_mm.x, current_position_mm.y, current_position_mm.z);
                    serialHandler.sendOK();
                    break;
                case GCODE_M115: // Get Firmware Info (already sent by SerialHandler during setup)
                    serialHandler.sendFirmwareInfo(); // Resend if requested again
                    serialHandler.sendOK();
                    break;
                case GCODE_M119: // Get Endstop Status
                    serialHandler.sendEndstopStatus(endstops.isTriggered('X'), endstops.isTriggered('Y'), endstops.isTriggered('Z'));
                    serialHandler.sendOK();
                    break;
                case GCODE_M410: // Quickstop
                    // Also clear G-code buffer for quickstop
                    while(!gcodeBuffer.isEmpty()) {
                        ParsedGCodeCommand dummy_cmd;
                        gcodeBuffer.pop(dummy_cmd); // Pop all commands
                    }
                    stepperControl.disableSteppers(); // Emergency stop effect
                    // Further actions (reset state) can be added here
                    serialHandler.sendInfo("M410: Quickstop initiated. G-code buffer cleared.");
                    serialHandler.sendOK();
                    break;
                case GCODE_M503: // Report Settings
                    serialHandler.sendInfo("Reporting settings (placeholder)...");
                    // Current position
                    serialHandler.sendInfo(("Current position (mm): X:" + String(current_position_mm.x) + 
                                            " Y:" + String(current_position_mm.y) + 
                                            " Z:" + String(current_position_mm.z)).c_str());
                    // Positioning mode
                    serialHandler.sendInfo(("Positioning mode: " + String(absolute_mode ? "Absolute" : "Relative")).c_str());
                    // Speed factor
                    serialHandler.sendInfo(("Speed factor: " + String(speed_factor) + "%").c_str());
                    // Stepper timeout
                    serialHandler.sendInfo(("Stepper timeout (ms): " + String(stepper_disable_timeout_ms)).c_str());
                    // Homing status
                    serialHandler.sendInfo(("Homed: X:" + String(homing.isHomedX() ? "true" : "false") +
                                            " Y:" + String(homing.isHomedY() ? "true" : "false") +
                                            " Z:" + String(homing.isHomedZ() ? "true" : "false")).c_str());
                    // Feedrates
                    serialHandler.sendInfo(("Max XY Speed (mm/s): " + String(MAX_VELOCITY_XY)).c_str());
                    serialHandler.sendInfo(("Max Z Speed (mm/s): " + String(MAX_VELOCITY_Z)).c_str());
                    serialHandler.sendOK();
                    break;
                case GCODE_M999: { // Motor Raw Test (per-axis diagnostic)
                    char test_axis = cmd.m999_args.axis;
                    char msg_buf[80];
                    snprintf(msg_buf, sizeof(msg_buf), "M999: Testing %c motor with raw pin toggles...", test_axis);
                    serialHandler.sendInfo(msg_buf);

                    uint8_t step_pin = 0;
                    if (test_axis == 'X') step_pin = X_STEP_PIN;
                    else if (test_axis == 'Y') step_pin = Y_STEP_PIN;
                    else step_pin = Z_STEP_PIN;
                    snprintf(msg_buf, sizeof(msg_buf), "Sending 800 steps at 1kHz to %c_STEP_PIN (%d)...", test_axis, step_pin);
                    serialHandler.sendInfo(msg_buf);

                    stepperControl.testMotorDirect(test_axis, 800, 500);

                    snprintf(msg_buf, sizeof(msg_buf), "M999: %c raw test complete. Did the motor move?", test_axis);
                    serialHandler.sendInfo(msg_buf);
                    serialHandler.sendInfo("If YES: AccelStepper config issue. If NO: hardware issue (wiring/driver/current).");
                    serialHandler.sendOK();
                    break;
                }
                case GCODE_UNKNOWN:
                    // Should be caught by SerialHandler, but defensive check
                    serialHandler.sendError(ERR_UNKNOWN_COMMAND, "Unknown command encountered in main loop.");
                    serialHandler.sendOK();
                    break;
            }
        }
    }
}