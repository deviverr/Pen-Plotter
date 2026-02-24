// SimplePlotter_Firmware/src/motion/homing.cpp

#include "homing.h"
#include <avr/wdt.h> // For watchdog timer reset during long operations
#include "../ui/lcd_menu.h" // For menuUpdateDisplay() during homing spinner animation

Homing homing; // Global instance definition

Homing::Homing() : _is_homed_x(false), _is_homed_y(false), _is_homed_z(false) {
    // Constructor
}

// Perform homing sequence for specified axis
bool Homing::homeAxis(char axis) {
    if (axis != 'X' && axis != 'Y' && axis != 'Z') {
        serialHandler.sendError(ERR_INVALID_SYNTAX, "Invalid axis for homing");
        return false;
    }

    // DIAGNOSTIC: Check initial endstop state
    bool initial_endstop_state = endstops.isTriggered(axis);
    serialHandler.sendInfo(("Homing " + String(axis) + ": Initial endstop=" + String(initial_endstop_state ? "TRIGGERED" : "open")).c_str());

    // DIAGNOSTIC: Check initial position
    long initial_pos = 0;
    if (axis == 'X') initial_pos = stepperControl.getCurrentXSteps();
    else if (axis == 'Y') initial_pos = stepperControl.getCurrentYSteps();
    else if (axis == 'Z') initial_pos = stepperControl.getCurrentZSteps();
    serialHandler.sendInfo(("Homing " + String(axis) + ": Initial position=" + String(initial_pos) + " steps").c_str());

    // Determine max travel for the axis (used for stall detection)
    // Use 2x MAX_POS to ensure we can reach the endstop from any starting position,
    // even if the carriage starts beyond the soft limit boundary.
    long max_travel_steps_for_stall = 0;
    if (axis == 'X') {
        max_travel_steps_for_stall = kinematics.mmToStepsX(X_MAX_POS * 2.0f);
    } else if (axis == 'Y') {
        max_travel_steps_for_stall = kinematics.mmToStepsY(Y_MAX_POS * 2.0f);
    } else if (axis == 'Z') {
        max_travel_steps_for_stall = kinematics.mmToStepsZ(Z_MAX_POS * 2.0f);
    }
    serialHandler.sendInfo(("Homing " + String(axis) + ": Max travel=" + String(max_travel_steps_for_stall) + " steps").c_str());

    // Cap feedrates for Z axis — Z uses 400 steps/mm (leadscrew) vs X/Y's 80 (belt).
    // HOMING_FEEDRATE_FAST=50mm/s × 400 = 20,000 steps/s would stall the Z motor.
    // MAX_VELOCITY_Z=10mm/s × 400 = 4,000 steps/s is within AccelStepper's AVR limit.
    float fast_rate = HOMING_FEEDRATE_FAST;
    float slow_rate = HOMING_FEEDRATE_SLOW;
    if (axis == 'Z') {
        fast_rate = min(fast_rate, (float)MAX_VELOCITY_Z);
        slow_rate = min(slow_rate, (float)MAX_VELOCITY_Z);
    }

    // Call the internal sequence
    bool success = _singleAxisHomingSequence(axis, max_travel_steps_for_stall, fast_rate, slow_rate);

    if (success) {
        // Update homed status
        if (axis == 'X') _is_homed_x = true;
        else if (axis == 'Y') _is_homed_y = true;
        else if (axis == 'Z') _is_homed_z = true;

        // After homing, set current position to 0 for the homed axis
        long current_x = stepperControl.getCurrentXSteps();
        long current_y = stepperControl.getCurrentYSteps();
        long current_z = stepperControl.getCurrentZSteps();

        if (axis == 'X') stepperControl.setCurrentPosition(0, current_y, current_z);
        else if (axis == 'Y') stepperControl.setCurrentPosition(current_x, 0, current_z);
        else if (axis == 'Z') {
            stepperControl.setCurrentPosition(current_x, current_y, 0);
            // Move Z to configured home position (above sensor) for pen clearance
            long z_home_steps = kinematics.mmToStepsZ(Z_HOME_POSITION);
            stepperControl.setAxisMaxSpeed('Z', MAX_VELOCITY_Z * Z_STEPS_PER_MM);
            stepperControl.setAxisAcceleration('Z', MAX_ACCEL_Z * Z_STEPS_PER_MM);
            stepperControl.moveAxisTo('Z', z_home_steps);
            stepperControl.enableSteppers();
            while (stepperControl.isAxisRunning('Z')) {
                wdt_reset();
                stepperControl.runAxis('Z');
                yield();
            }
            serialHandler.sendInfo("Z moved to home position");
        }

        return true;
    } else {
        serialHandler.sendError(ERR_HOMING_FAILED, "Homing failed for axis");
        // Reset software position to 0 for the failed axis so the next homing attempt
        // moves the full max_travel distance and can reach the endstop from any position.
        long current_x = stepperControl.getCurrentXSteps();
        long current_y = stepperControl.getCurrentYSteps();
        long current_z = stepperControl.getCurrentZSteps();
        if (axis == 'X') stepperControl.setCurrentPosition(0, current_y, current_z);
        else if (axis == 'Y') stepperControl.setCurrentPosition(current_x, 0, current_z);
        else if (axis == 'Z') stepperControl.setCurrentPosition(current_x, current_y, 0);
        return false;
    }
}

// Perform homing sequence for all axes (Z, then X, then Y)
// Attempts all axes even if one fails, reports per-axis results.
bool Homing::homeAllAxes() {
    bool z_ok = false, x_ok = false, y_ok = false;

    // Print endstop states before homing for diagnostics
    serialHandler.sendInfo("Pre-homing endstop check:");
    serialHandler.sendEndstopStatus(
        endstops.isTriggered('X'),
        endstops.isTriggered('Y'),
        endstops.isTriggered('Z'));

    // Home Z first (pen lift for safety)
    serialHandler.sendInfo("Homing Z axis...");
    z_ok = homeAxis('Z');
    _is_homed_z = z_ok;

    if (z_ok) {
        // Z already moved to Z_HOME_POSITION in homeAxis('Z')
        // No additional Z move needed here
    }

    // Home X (attempt even if Z failed)
    serialHandler.sendInfo("Homing X axis...");
    x_ok = homeAxis('X');
    _is_homed_x = x_ok;

    // Home Y (attempt even if X failed)
    serialHandler.sendInfo("Homing Y axis...");
    y_ok = homeAxis('Y');
    _is_homed_y = y_ok;

    // Report per-axis results
    char result_buf[64];
    snprintf(result_buf, sizeof(result_buf), "Homing result: X=%s Y=%s Z=%s",
             x_ok ? "OK" : "FAIL", y_ok ? "OK" : "FAIL", z_ok ? "OK" : "FAIL");
    serialHandler.sendInfo(result_buf);

    if (x_ok && y_ok && z_ok) {
        // All axes homed - move to origin with pen at safe height
        serialHandler.sendInfo("Moving to home position (0,0,Z_HOME)...");
        stepperControl.moveTo(0, 0, kinematics.mmToStepsZ(Z_HOME_POSITION));
        stepperControl.enableSteppers();
        stepperControl.runBlocking();
        serialHandler.sendInfo("All axes homed.");
        return true;
    } else {
        serialHandler.sendError(ERR_HOMING_FAILED, result_buf);
        return false;
    }
}

// Internal helper for homing a single axis (Phase 1-4)
bool Homing::_singleAxisHomingSequence(char axis, long max_travel_steps, float fast_feedrate_mm_s, float slow_feedrate_mm_s) {
    stepperControl.enableSteppers(); // Enable steppers for homing

    // Pre-check: if endstop is already triggered, back off to clear it first
    if (endstops.isTriggered(axis)) {
        serialHandler.sendInfo("Endstop pre-triggered, clearing...");
        if (!_moveAwayFromEndstop(axis, HOMING_BACKOFF_MM * 2, fast_feedrate_mm_s)) {
            stepperControl.disableSteppers();
            return false;
        }
        delay(50); // Allow debounce to settle
        if (endstops.isTriggered(axis)) {
            serialHandler.sendError(ERR_HOMING_FAILED, "Cannot clear pre-triggered endstop");
            stepperControl.disableSteppers();
            return false;
        }
    }

    // Phase 1: Fast approach towards endstop
    serialHandler.sendInfo("Homing Phase 1: Fast approach...");
    if (!_moveUntilTriggered(axis, fast_feedrate_mm_s, max_travel_steps, HOMING_TIMEOUT_S * 1000UL)) {
        stepperControl.disableSteppers();
        return false;
    }

    // Phase 2: Backoff from endstop
    serialHandler.sendInfo("Homing Phase 2: Backoff...");
    if (!_moveAwayFromEndstop(axis, HOMING_BACKOFF_MM, fast_feedrate_mm_s)) {
        stepperControl.disableSteppers();
        return false;
    }
    
    // Post-backoff validation: Ensure endstop is no longer triggered
    if (endstops.isTriggered(axis)) {
        serialHandler.sendError(ERR_HOMING_FAILED, "Endstop still triggered after backoff");
        stepperControl.disableSteppers();
        return false;
    }

    // Phase 3: Slow approach towards endstop
    serialHandler.sendInfo("Homing Phase 3: Slow approach...");
    // The maximum distance for the slow approach needs generous margin to account for any overshoot
    long slow_approach_max_steps = 0;
    if (axis == 'X') slow_approach_max_steps = kinematics.mmToStepsX(HOMING_BACKOFF_MM * 4);
    else if (axis == 'Y') slow_approach_max_steps = kinematics.mmToStepsY(HOMING_BACKOFF_MM * 4);
    else if (axis == 'Z') slow_approach_max_steps = kinematics.mmToStepsZ(HOMING_BACKOFF_MM * 4);
    if (!_moveUntilTriggered(axis, slow_feedrate_mm_s, slow_approach_max_steps, HOMING_TIMEOUT_S * 1000UL)) {
        stepperControl.disableSteppers();
        return false;
    }

    // Phase 4: Set zero position (handled by calling function homeAxis)
    stepperControl.disableSteppers(); // Disable steppers after homing
    return true;
}

// Helper to move towards endstop until triggered or timeout
bool Homing::_moveUntilTriggered(char axis, float speed_mm_s, long max_distance_steps, unsigned long timeout_ms) {
    float speed_steps_per_s = speed_mm_s; // Will be multiplied by steps_per_mm below
    long current_axis_pos_at_start = 0; // Position when starting this move

    // Set appropriate speed and reduced acceleration for homing (smoother motion)
    if (axis == 'X') {
        speed_steps_per_s *= X_STEPS_PER_MM;
        stepperControl.setAxisMaxSpeed('X', speed_steps_per_s);
        stepperControl.setAxisAcceleration('X', MAX_ACCEL_X * X_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        current_axis_pos_at_start = stepperControl.getCurrentXSteps();
        // Move towards min endstop: negative direction
        stepperControl.moveAxisBy('X', -max_distance_steps);
    } else if (axis == 'Y') {
        speed_steps_per_s *= Y_STEPS_PER_MM;
        stepperControl.setAxisMaxSpeed('Y', speed_steps_per_s);
        stepperControl.setAxisAcceleration('Y', MAX_ACCEL_Y * Y_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        current_axis_pos_at_start = stepperControl.getCurrentYSteps();
        stepperControl.moveAxisBy('Y', -max_distance_steps);
    } else if (axis == 'Z') {
        speed_steps_per_s *= Z_STEPS_PER_MM;
        stepperControl.setAxisMaxSpeed('Z', speed_steps_per_s);
        stepperControl.setAxisAcceleration('Z', MAX_ACCEL_Z * Z_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        current_axis_pos_at_start = stepperControl.getCurrentZSteps();
        stepperControl.moveAxisBy('Z', -max_distance_steps);
    }

    serialHandler.sendInfo(("Moving " + String(axis) + ": Start pos=" + String(current_axis_pos_at_start) +
                           ", target offset=" + String(-max_distance_steps) +
                           ", speed=" + String(speed_steps_per_s) + " steps/s").c_str());

    unsigned long start_time = millis();
    unsigned long last_ui_update = 0; // Track last LCD update for spinner animation
    while (!endstops.isTriggered(axis)) {
        wdt_reset(); // Feed watchdog timer to prevent reset during long homing moves

        if (millis() - start_time > timeout_ms) {
            // DIAGNOSTIC: Report timeout
            long final_pos = 0;
            if (axis == 'X') final_pos = stepperControl.getCurrentXSteps();
            else if (axis == 'Y') final_pos = stepperControl.getCurrentYSteps();
            else if (axis == 'Z') final_pos = stepperControl.getCurrentZSteps();
            serialHandler.sendInfo(("TIMEOUT " + String(axis) + ": Moved " + String(current_axis_pos_at_start - final_pos) + " steps").c_str());
            stepperControl.stopAxis(axis);
            return false; // Homing timeout handled by calling function
        }

        stepperControl.runAxis(axis);

        // Stall detection: Check if we've traveled the full distance without hitting endstop
        bool at_target = false;
        if (axis == 'X') at_target = !stepperControl.isAxisRunning('X');
        else if (axis == 'Y') at_target = !stepperControl.isAxisRunning('Y');
        else if (axis == 'Z') at_target = !stepperControl.isAxisRunning('Z');

        if (at_target) {
            // DIAGNOSTIC: Report stall detection
            long final_pos = 0;
            if (axis == 'X') final_pos = stepperControl.getCurrentXSteps();
            else if (axis == 'Y') final_pos = stepperControl.getCurrentYSteps();
            else if (axis == 'Z') final_pos = stepperControl.getCurrentZSteps();
            serialHandler.sendInfo(("STALL " + String(axis) + ": Moved " + String(current_axis_pos_at_start - final_pos) +
                                   " steps, endstop never triggered").c_str());
            stepperControl.stopAxis(axis);
            return false; // Max travel reached without endstop trigger handled by calling function
        }

        // Update LCD every 150ms to animate the homing spinner
        unsigned long now = millis();
        if (now - last_ui_update >= 150) {
            menuUpdateDisplay();
            last_ui_update = now;
        }

        yield(); // Allow other tasks to run
    }

    // DIAGNOSTIC: Report successful endstop trigger
    long final_pos = 0;
    if (axis == 'X') final_pos = stepperControl.getCurrentXSteps();
    else if (axis == 'Y') final_pos = stepperControl.getCurrentYSteps();
    else if (axis == 'Z') final_pos = stepperControl.getCurrentZSteps();
    serialHandler.sendInfo(("TRIGGERED " + String(axis) + ": Moved " + String(current_axis_pos_at_start - final_pos) + " steps").c_str());

    // Stop the stepper instantly (no deceleration overshoot)
    stepperControl.stopAxisImmediate(axis);

    return true;
}

// Helper to move away from endstop for a specified distance
bool Homing::_moveAwayFromEndstop(char axis, float distance_mm, float speed_mm_s) {
    long current_pos_steps = 0;
    long move_distance_steps = 0;
    float speed_steps_per_s = speed_mm_s; // Will be multiplied by steps_per_mm below
    long new_target_pos_steps = 0;

    // Set appropriate speed and reduced acceleration for homing backoff
    if (axis == 'X') {
        current_pos_steps = stepperControl.getCurrentXSteps();
        move_distance_steps = kinematics.mmToStepsX(distance_mm);
        speed_steps_per_s *= X_STEPS_PER_MM;
        new_target_pos_steps = current_pos_steps + move_distance_steps; // Move in positive direction (away from min endstop)
        stepperControl.setAxisMaxSpeed('X', speed_steps_per_s);
        stepperControl.setAxisAcceleration('X', MAX_ACCEL_X * X_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        stepperControl.moveAxisTo('X', new_target_pos_steps);
    } else if (axis == 'Y') {
        current_pos_steps = stepperControl.getCurrentYSteps();
        move_distance_steps = kinematics.mmToStepsY(distance_mm);
        speed_steps_per_s *= Y_STEPS_PER_MM;
        new_target_pos_steps = current_pos_steps + move_distance_steps;
        stepperControl.setAxisMaxSpeed('Y', speed_steps_per_s);
        stepperControl.setAxisAcceleration('Y', MAX_ACCEL_Y * Y_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        stepperControl.moveAxisTo('Y', new_target_pos_steps);
    } else if (axis == 'Z') {
        current_pos_steps = stepperControl.getCurrentZSteps();
        move_distance_steps = kinematics.mmToStepsZ(distance_mm);
        speed_steps_per_s *= Z_STEPS_PER_MM;
        new_target_pos_steps = current_pos_steps + move_distance_steps;
        stepperControl.setAxisMaxSpeed('Z', speed_steps_per_s);
        stepperControl.setAxisAcceleration('Z', MAX_ACCEL_Z * Z_STEPS_PER_MM * HOMING_ACCEL_FACTOR);
        stepperControl.moveAxisTo('Z', new_target_pos_steps);
    }

    serialHandler.sendInfo(("Backoff " + String(axis) + ": " + String(distance_mm) + "mm (" + String(move_distance_steps) +
                           " steps) from " + String(current_pos_steps) + " to " + String(new_target_pos_steps)).c_str());

    // Block until movement is complete (or timeout)
    // Calculate an approximate timeout based on distance and speed
    unsigned long timeout_calc_ms = (unsigned long)(distance_mm / speed_mm_s * 1500UL) + 500UL; // 1.5x expected time + buffer
    unsigned long start_time = millis();
    unsigned long last_ui_update_backoff = 0;
    while (stepperControl.isAxisRunning(axis)) {
        wdt_reset(); // Feed watchdog timer

        if (millis() - start_time > timeout_calc_ms) {
            serialHandler.sendInfo(("Backoff " + String(axis) + " TIMEOUT after " + String(millis() - start_time) + "ms").c_str());
            return false; // Backoff timeout handled by calling function
        }
        stepperControl.runAxis(axis);

        unsigned long now = millis();
        if (now - last_ui_update_backoff >= 150) {
            menuUpdateDisplay();
            last_ui_update_backoff = now;
        }
        yield();
    }

    long final_pos = 0;
    if (axis == 'X') final_pos = stepperControl.getCurrentXSteps();
    else if (axis == 'Y') final_pos = stepperControl.getCurrentYSteps();
    else if (axis == 'Z') final_pos = stepperControl.getCurrentZSteps();
    serialHandler.sendInfo(("Backoff " + String(axis) + " complete: final pos=" + String(final_pos)).c_str());

    return true;
}
