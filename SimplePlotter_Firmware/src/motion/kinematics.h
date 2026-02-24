// SimplePlotter_Firmware/src/motion/kinematics.h

#ifndef KINEMATICS_H
#define KINEMATICS_H

#include "../config.h"

// Define a simple structure for 3D coordinates in millimeters
struct Point3D {
    float x;
    float y;
    float z;

    // Default constructor
    Point3D() : x(0.0), y(0.0), z(0.0) {}

    // Parameterized constructor
    Point3D(float _x, float _y, float _z) : x(_x), y(_y), z(_z) {}
};

class Kinematics {
public:
    Kinematics();

    // Convert millimeters to steps
    long mmToStepsX(float mm) { return (long)(mm * X_STEPS_PER_MM); }
    long mmToStepsY(float mm) { return (long)(mm * Y_STEPS_PER_MM); }
    long mmToStepsZ(float mm) { return (long)(mm * Z_STEPS_PER_MM); }

    // Convert steps to millimeters
    float stepsToMmX(long steps) { return (float)steps / X_STEPS_PER_MM; }
    float stepsToMmY(long steps) { return (float)steps / Y_STEPS_PER_MM; }
    float stepsToMmZ(long steps) { return (float)steps / Z_STEPS_PER_MM; }

    // Convert a Point3D (mm) to an array of steps
    void mmToSteps(const Point3D& mm_coords, long (&steps)[3]);

    // Convert an array of steps to a Point3D (mm)
    Point3D stepsToMm(const long steps[3]);

    // Validate if a target position is within soft limits
    bool isValidPosition(const Point3D& target_mm);
    
    // Get machine limits in millimeters
    Point3D getMaxMachineCoords() {
        return Point3D(X_MAX_POS, Y_MAX_POS, Z_MAX_POS);
    }

private:
    // Any internal state or configuration if needed in the future
};

extern Kinematics kinematics; // Global instance

#endif // KINEMATICS_H
