// SimplePlotter_Firmware/src/motion/stepper_control.cpp

#include "stepper_control.h"
#include <avr/wdt.h>

StepperControl stepperControl; // Global instance definition

StepperControl::StepperControl() :
    _stepperX(DRIVER_TYPE, X_STEP_PIN, X_DIR_PIN),
    _stepperY(DRIVER_TYPE, Y_STEP_PIN, Y_DIR_PIN),
    _stepperZ(DRIVER_TYPE, Z_STEP_PIN, Z_DIR_PIN),
    _steppers_are_disabled(true) // Initialize as disabled
{
    // Steppers are initialized, but further setup is done in init()
}

void StepperControl::init() {
    _stepperX.setEnablePin(X_ENABLE_PIN);
    _stepperY.setEnablePin(Y_ENABLE_PIN);
    _stepperZ.setEnablePin(Z_ENABLE_PIN);

    // setPinsInverted(directionInvert, stepInvert, enableInvert)
    // Enable is active-LOW on MKS Gen v1.4 (HIGH disables), so enableInvert=true
    // Direction inversion configured in config.h per axis
    _stepperX.setPinsInverted(INVERT_X_DIR, false, true);
    _stepperY.setPinsInverted(INVERT_Y_DIR, false, true);
    _stepperZ.setPinsInverted(INVERT_Z_DIR, false, true);

    // Set initial maximum speeds and accelerations from config
    setMaxSpeed(MAX_VELOCITY_XY * X_STEPS_PER_MM,
                MAX_VELOCITY_XY * Y_STEPS_PER_MM,
                MAX_VELOCITY_Z * Z_STEPS_PER_MM);
    setAcceleration(MAX_ACCEL_X * X_STEPS_PER_MM,
                    MAX_ACCEL_Y * Y_STEPS_PER_MM,
                    MAX_ACCEL_Z * Z_STEPS_PER_MM);

    disableSteppers(); // Start with steppers disabled
}

void StepperControl::enableSteppers() {
    _stepperX.enableOutputs();
    _stepperY.enableOutputs();
    _stepperZ.enableOutputs();
    _steppers_are_disabled = false;
}

void StepperControl::disableSteppers() {
    _stepperX.disableOutputs();
    _stepperY.disableOutputs();
    _stepperZ.disableOutputs();
    _steppers_are_disabled = true;
}

void StepperControl::setMaxSpeed(float x_steps_per_s, float y_steps_per_s, float z_steps_per_s) {
    // Guard against zero — AccelStepper::setMaxSpeed(0) causes division by zero internally.
    // Skip updating an axis if speed is zero (axis doesn't move, keeps previous safe value).
    if (x_steps_per_s > 0.0f) _stepperX.setMaxSpeed(x_steps_per_s);
    if (y_steps_per_s > 0.0f) _stepperY.setMaxSpeed(y_steps_per_s);
    if (z_steps_per_s > 0.0f) _stepperZ.setMaxSpeed(z_steps_per_s);
}

void StepperControl::setAcceleration(float x_steps_per_s2, float y_steps_per_s2, float z_steps_per_s2) {
    _stepperX.setAcceleration(x_steps_per_s2);
    _stepperY.setAcceleration(y_steps_per_s2);
    _stepperZ.setAcceleration(z_steps_per_s2);
}

void StepperControl::moveTo(long x_steps, long y_steps, long z_steps) {
    _stepperX.moveTo(x_steps);
    _stepperY.moveTo(y_steps);
    _stepperZ.moveTo(z_steps);
}

void StepperControl::runBlocking() {
    // Trapezoidal speed profile: accelerate, cruise, decelerate.
    // We recalculate speed every ~5ms (200Hz) to keep per-step loop lightweight
    // while providing smooth acceleration. Uses runSpeedToPosition() for the actual
    // stepping (no sqrt per step like run()).

    long distX = abs(_stepperX.distanceToGo());
    long distY = abs(_stepperY.distanceToGo());
    long distZ = abs(_stepperZ.distanceToGo());

    // Find dominant axis (longest travel in steps)
    long maxDist = max(distX, max(distY, distZ));
    if (maxDist == 0) return; // Nothing to move

    // Get max speeds for each axis
    float maxSpeedX = _stepperX.maxSpeed();
    float maxSpeedY = _stepperY.maxSpeed();
    float maxSpeedZ = _stepperZ.maxSpeed();

    // Determine the dominant axis and its parameters
    float dominantMaxSpeed, dominantAccel;
    long dominantDist;
    if (distX >= distY && distX >= distZ) {
        dominantMaxSpeed = maxSpeedX;
        dominantAccel = MAX_ACCEL_X * X_STEPS_PER_MM;
        dominantDist = distX;
    } else if (distY >= distX && distY >= distZ) {
        dominantMaxSpeed = maxSpeedY;
        dominantAccel = MAX_ACCEL_Y * Y_STEPS_PER_MM;
        dominantDist = distY;
    } else {
        dominantMaxSpeed = maxSpeedZ;
        dominantAccel = MAX_ACCEL_Z * Z_STEPS_PER_MM;
        dominantDist = distZ;
    }

    // Compute trapezoidal profile for dominant axis
    // accelSteps = v_max^2 / (2 * accel)
    float accelSteps = (dominantMaxSpeed * dominantMaxSpeed) / (2.0f * dominantAccel);
    float decelSteps = accelSteps;

    // If not enough room for full accel+decel, use triangle profile
    if (accelSteps + decelSteps > dominantDist) {
        accelSteps = dominantDist / 2.0f;
        decelSteps = dominantDist - accelSteps;
    }
    float cruiseStart = accelSteps;
    float cruiseEnd = dominantDist - decelSteps;

    // Record starting positions to track progress
    long startX = _stepperX.currentPosition();
    long startY = _stepperY.currentPosition();
    long startZ = _stepperZ.currentPosition();

    // Prime all axes with initial speed BEFORE entering the loop.
    // runSpeedToPosition() won't generate steps if speed=0 (stepInterval=0),
    // so we MUST set a non-zero speed on every moving axis before the first call.
    // runSpeedToPosition() handles direction internally (target vs current), so
    // only the speed magnitude matters here.
    float initSpeed = max(dominantMaxSpeed * 0.05f, 50.0f);
    float initRatio = initSpeed / dominantMaxSpeed;
    if (distX > 0) _stepperX.setSpeed((distX == dominantDist) ? initSpeed : maxSpeedX * initRatio);
    if (distY > 0) _stepperY.setSpeed((distY == dominantDist) ? initSpeed : maxSpeedY * initRatio);
    if (distZ > 0) _stepperZ.setSpeed((distZ == dominantDist) ? initSpeed : maxSpeedZ * initRatio);

    unsigned long lastSpeedUpdate = millis();

    while (_stepperX.distanceToGo() != 0 ||
           _stepperY.distanceToGo() != 0 ||
           _stepperZ.distanceToGo() != 0) {
        wdt_reset();

        // Recalculate speed every 5ms (200Hz)
        unsigned long now = millis();
        if (now - lastSpeedUpdate >= 5) {
            lastSpeedUpdate = now;

            // Estimate progress along dominant axis
            long progressX = abs(_stepperX.currentPosition() - startX);
            long progressY = abs(_stepperY.currentPosition() - startY);
            long progressZ = abs(_stepperZ.currentPosition() - startZ);
            long progress = max(progressX, max(progressY, progressZ));

            // Determine which phase we're in and compute target speed
            float targetSpeed;
            if (progress < (long)cruiseStart) {
                // Acceleration phase: v = sqrt(2 * accel * distance)
                float v = sqrt(2.0f * dominantAccel * (float)max(progress, 1L));
                targetSpeed = min(v, dominantMaxSpeed);
                targetSpeed = max(targetSpeed, dominantMaxSpeed * 0.05f);
            } else if (progress < (long)cruiseEnd) {
                // Cruise phase
                targetSpeed = dominantMaxSpeed;
            } else {
                // Deceleration phase
                long remaining = dominantDist - progress;
                float v = sqrt(2.0f * dominantAccel * (float)max(remaining, 1L));
                targetSpeed = min(v, dominantMaxSpeed);
                targetSpeed = max(targetSpeed, dominantMaxSpeed * 0.05f);
            }

            // Scale axis speeds proportionally to dominant axis speed.
            // runSpeedToPosition() determines direction internally, so only magnitude matters.
            float ratio = targetSpeed / dominantMaxSpeed;
            if (distX > 0) _stepperX.setSpeed((distX == dominantDist) ? targetSpeed : maxSpeedX * ratio);
            if (distY > 0) _stepperY.setSpeed((distY == dominantDist) ? targetSpeed : maxSpeedY * ratio);
            if (distZ > 0) _stepperZ.setSpeed((distZ == dominantDist) ? targetSpeed : maxSpeedZ * ratio);
        }

        _stepperX.runSpeedToPosition();
        _stepperY.runSpeedToPosition();
        _stepperZ.runSpeedToPosition();
    }
}

bool StepperControl::runBlockingWithCheck(bool (*shouldStop)()) {
    // Same as runBlocking but calls shouldStop() every 5ms.
    // If shouldStop returns true, all axes are stopped immediately.
    // Returns true if stopped by callback, false if completed normally.

    long distX = abs(_stepperX.distanceToGo());
    long distY = abs(_stepperY.distanceToGo());
    long distZ = abs(_stepperZ.distanceToGo());

    long maxDist = max(distX, max(distY, distZ));
    if (maxDist == 0) return false;

    float maxSpeedX = _stepperX.maxSpeed();
    float maxSpeedY = _stepperY.maxSpeed();
    float maxSpeedZ = _stepperZ.maxSpeed();

    float dominantMaxSpeed, dominantAccel;
    long dominantDist;
    if (distX >= distY && distX >= distZ) {
        dominantMaxSpeed = maxSpeedX;
        dominantAccel = MAX_ACCEL_X * X_STEPS_PER_MM;
        dominantDist = distX;
    } else if (distY >= distX && distY >= distZ) {
        dominantMaxSpeed = maxSpeedY;
        dominantAccel = MAX_ACCEL_Y * Y_STEPS_PER_MM;
        dominantDist = distY;
    } else {
        dominantMaxSpeed = maxSpeedZ;
        dominantAccel = MAX_ACCEL_Z * Z_STEPS_PER_MM;
        dominantDist = distZ;
    }

    float accelSteps = (dominantMaxSpeed * dominantMaxSpeed) / (2.0f * dominantAccel);
    float decelSteps = accelSteps;
    if (accelSteps + decelSteps > dominantDist) {
        accelSteps = dominantDist / 2.0f;
        decelSteps = dominantDist - accelSteps;
    }
    float cruiseStart = accelSteps;
    float cruiseEnd = dominantDist - decelSteps;

    long startX = _stepperX.currentPosition();
    long startY = _stepperY.currentPosition();
    long startZ = _stepperZ.currentPosition();

    float initSpeed = max(dominantMaxSpeed * 0.05f, 50.0f);
    float initRatio = initSpeed / dominantMaxSpeed;
    if (distX > 0) _stepperX.setSpeed((distX == dominantDist) ? initSpeed : maxSpeedX * initRatio);
    if (distY > 0) _stepperY.setSpeed((distY == dominantDist) ? initSpeed : maxSpeedY * initRatio);
    if (distZ > 0) _stepperZ.setSpeed((distZ == dominantDist) ? initSpeed : maxSpeedZ * initRatio);

    unsigned long lastSpeedUpdate = millis();

    while (_stepperX.distanceToGo() != 0 ||
           _stepperY.distanceToGo() != 0 ||
           _stepperZ.distanceToGo() != 0) {
        wdt_reset();

        unsigned long now = millis();
        if (now - lastSpeedUpdate >= 5) {
            lastSpeedUpdate = now;

            // Check stop callback
            if (shouldStop && shouldStop()) {
                _stepperX.setCurrentPosition(_stepperX.currentPosition());
                _stepperY.setCurrentPosition(_stepperY.currentPosition());
                _stepperZ.setCurrentPosition(_stepperZ.currentPosition());
                return true; // Stopped by callback
            }

            long progressX = abs(_stepperX.currentPosition() - startX);
            long progressY = abs(_stepperY.currentPosition() - startY);
            long progressZ = abs(_stepperZ.currentPosition() - startZ);
            long progress = max(progressX, max(progressY, progressZ));

            float targetSpeed;
            if (progress < (long)cruiseStart) {
                float v = sqrt(2.0f * dominantAccel * (float)max(progress, 1L));
                targetSpeed = min(v, dominantMaxSpeed);
                targetSpeed = max(targetSpeed, dominantMaxSpeed * 0.05f);
            } else if (progress < (long)cruiseEnd) {
                targetSpeed = dominantMaxSpeed;
            } else {
                long remaining = dominantDist - progress;
                float v = sqrt(2.0f * dominantAccel * (float)max(remaining, 1L));
                targetSpeed = min(v, dominantMaxSpeed);
                targetSpeed = max(targetSpeed, dominantMaxSpeed * 0.05f);
            }

            float ratio = targetSpeed / dominantMaxSpeed;
            if (distX > 0) _stepperX.setSpeed((distX == dominantDist) ? targetSpeed : maxSpeedX * ratio);
            if (distY > 0) _stepperY.setSpeed((distY == dominantDist) ? targetSpeed : maxSpeedY * ratio);
            if (distZ > 0) _stepperZ.setSpeed((distZ == dominantDist) ? targetSpeed : maxSpeedZ * ratio);
        }

        _stepperX.runSpeedToPosition();
        _stepperY.runSpeedToPosition();
        _stepperZ.runSpeedToPosition();
    }
    return false; // Completed normally
}

long StepperControl::getCurrentXSteps() {
    return _stepperX.currentPosition();
}

long StepperControl::getCurrentYSteps() {
    return _stepperY.currentPosition();
}

long StepperControl::getCurrentZSteps() {
    return _stepperZ.currentPosition();
}

void StepperControl::setCurrentPosition(long x, long y, long z) {
    _stepperX.setCurrentPosition(x);
    _stepperY.setCurrentPosition(y);
    _stepperZ.setCurrentPosition(z);
}

// Individual axis control for homing/jogging
void StepperControl::moveAxisTo(char axis, long steps) {
    if (axis == 'X') {
        _stepperX.moveTo(steps);
    } else if (axis == 'Y') {
        _stepperY.moveTo(steps);
    } else if (axis == 'Z') {
        _stepperZ.moveTo(steps);
    }
}

void StepperControl::setAxisSpeed(char axis, float speed_steps_per_s) {
    if (axis == 'X') {
        _stepperX.setSpeed(speed_steps_per_s);
    } else if (axis == 'Y') {
        _stepperY.setSpeed(speed_steps_per_s);
    } else if (axis == 'Z') {
        _stepperZ.setSpeed(speed_steps_per_s);
    }
}

void StepperControl::setAxisAcceleration(char axis, float acceleration_steps_per_s2) {
    if (axis == 'X') {
        _stepperX.setAcceleration(acceleration_steps_per_s2);
    } else if (axis == 'Y') {
        _stepperY.setAcceleration(acceleration_steps_per_s2);
    } else if (axis == 'Z') {
        _stepperZ.setAcceleration(acceleration_steps_per_s2);
    }
}

bool StepperControl::runAxis(char axis) {
    if (axis == 'X') {
        return _stepperX.run();
    } else if (axis == 'Y') {
        return _stepperY.run();
    } else if (axis == 'Z') {
        return _stepperZ.run();
    }
    return false;
}

bool StepperControl::runAllAxes() {
    bool x = _stepperX.run();
    bool y = _stepperY.run();
    bool z = _stepperZ.run();
    return x || y || z;
}

void StepperControl::moveAxisBy(char axis, long steps) {
    if (axis == 'X') {
        _stepperX.move(steps);
    } else if (axis == 'Y') {
        _stepperY.move(steps);
    } else if (axis == 'Z') {
        _stepperZ.move(steps);
    }
}

void StepperControl::setAxisMaxSpeed(char axis, float max_speed_steps_per_s) {
    if (axis == 'X') {
        _stepperX.setMaxSpeed(max_speed_steps_per_s);
    } else if (axis == 'Y') {
        _stepperY.setMaxSpeed(max_speed_steps_per_s);
    } else if (axis == 'Z') {
        _stepperZ.setMaxSpeed(max_speed_steps_per_s);
    }
}

void StepperControl::stopAxis(char axis) {
    if (axis == 'X') {
        _stepperX.stop();
    } else if (axis == 'Y') {
        _stepperY.stop();
    } else if (axis == 'Z') {
        _stepperZ.stop();
    }
}

void StepperControl::stopAxisImmediate(char axis) {
    // setCurrentPosition(currentPosition()) zeros both speed and distanceToGo instantly
    if (axis == 'X') _stepperX.setCurrentPosition(_stepperX.currentPosition());
    else if (axis == 'Y') _stepperY.setCurrentPosition(_stepperY.currentPosition());
    else if (axis == 'Z') _stepperZ.setCurrentPosition(_stepperZ.currentPosition());
}

bool StepperControl::isAxisRunning(char axis) {
    if (axis == 'X') {
        return _stepperX.distanceToGo() != 0;
    } else if (axis == 'Y') {
        return _stepperY.distanceToGo() != 0;
    } else if (axis == 'Z') {
        return _stepperZ.distanceToGo() != 0;
    }
    return false;
}

void StepperControl::testMotorDirect(char axis, int steps, int delayUs) {
    // Raw pin-toggle test that bypasses AccelStepper entirely.
    // This tests the hardware path: MCU pin -> driver -> motor.
    // If this moves the motor but AccelStepper doesn't, the issue is in
    // AccelStepper configuration. If this doesn't move it either, the
    // issue is hardware (wiring, driver, current).

    uint8_t enablePin, stepPin, dirPin;
    if (axis == 'X') {
        enablePin = X_ENABLE_PIN; stepPin = X_STEP_PIN; dirPin = X_DIR_PIN;
    } else if (axis == 'Y') {
        enablePin = Y_ENABLE_PIN; stepPin = Y_STEP_PIN; dirPin = Y_DIR_PIN;
    } else {
        enablePin = Z_ENABLE_PIN; stepPin = Z_STEP_PIN; dirPin = Z_DIR_PIN;
    }

    pinMode(enablePin, OUTPUT);
    pinMode(stepPin, OUTPUT);
    pinMode(dirPin, OUTPUT);

    // Enable driver (active LOW for A4988/DRV8825)
    digitalWrite(enablePin, LOW);
    delay(5); // Let driver stabilize

    // Set direction
    digitalWrite(dirPin, HIGH);
    delay(1);

    // Generate step pulses
    for (int i = 0; i < steps; i++) {
        wdt_reset();
        digitalWrite(stepPin, HIGH);
        delayMicroseconds(delayUs);
        digitalWrite(stepPin, LOW);
        delayMicroseconds(delayUs);
    }

    // Disable driver
    digitalWrite(enablePin, HIGH);
}

void StepperControl::testZMotorDirect(int steps, int delayUs) {
    testMotorDirect('Z', steps, delayUs);
}
