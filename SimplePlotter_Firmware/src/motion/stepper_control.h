// SimplePlotter_Firmware/src/motion/stepper_control.h

#ifndef STEPPER_CONTROL_H
#define STEPPER_CONTROL_H

#include <AccelStepper.h>
#include "../config.h" // Include our configuration

// Define the stepper motor driver type
// AccelStepper::DRIVER is the default for step/dir drivers.
// For A4988, a short pulse width is generally fine.
#define DRIVER_TYPE AccelStepper::DRIVER 

class StepperControl {
public:
    StepperControl();

    void init();
    void enableSteppers();
    void disableSteppers();

    // Set maximum speed and acceleration for all axes (in steps/s and steps/s^2)
    void setMaxSpeed(float x_steps_per_s, float y_steps_per_s, float z_steps_per_s);
    void setAcceleration(float x_steps_per_s2, float y_steps_per_s2, float z_steps_per_s2);

    // Move to absolute positions (in steps)
    void moveTo(long x_steps, long y_steps, long z_steps);
    void runBlocking(); // Blocks until all moves are complete
    bool runBlockingWithCheck(bool (*shouldStop)()); // Same but calls shouldStop every 5ms; returns true if stopped early
    
    // Get current position in steps
    long getCurrentXSteps();
    long getCurrentYSteps();
    long getCurrentZSteps();

    // Check if steppers are disabled
    bool is_steppers_disabled() const { return _steppers_are_disabled; }

    // Set current position (for homing)
    void setCurrentPosition(long x, long y, long z);
    
    // Individual axis control (mainly for homing or manual jog)
    void moveAxisTo(char axis, long steps); // Move individual axis to absolute position in steps
    void moveAxisBy(char axis, long steps); // Move individual axis by relative distance in steps
    void setAxisSpeed(char axis, float speed_steps_per_s); // Set speed for individual axis in steps/s (constant speed mode)
    void setAxisMaxSpeed(char axis, float max_speed_steps_per_s); // Set max speed for acceleration mode
    void setAxisAcceleration(char axis, float acceleration_steps_per_s2); // Set acceleration for individual axis in steps/s^2
    bool runAxis(char axis); // Run individual axis (needs to be called repeatedly in loop)
    bool runAllAxes(); // Run all axes (needs to be called repeatedly in loop for non-blocking multi-stepper)
    void stopAxis(char axis); // Stop individual axis (decelerates)
    void stopAxisImmediate(char axis); // Instant stop, no deceleration
    bool isAxisRunning(char axis); // Check if axis is still moving towards target

    // Raw pin-toggle motor test (bypasses AccelStepper entirely for diagnostics)
    void testMotorDirect(char axis, int steps = 800, int delayUs = 500);
    void testZMotorDirect(int steps = 800, int delayUs = 500); // Kept for backward compat

private:
    AccelStepper _stepperX;
    AccelStepper _stepperY;
    AccelStepper _stepperZ;

    bool _steppers_are_disabled; // Track stepper enable/disable state
};

extern StepperControl stepperControl; // Global instance

#endif // STEPPER_CONTROL_H
