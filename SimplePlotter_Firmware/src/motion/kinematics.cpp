// SimplePlotter_Firmware/src/motion/kinematics.cpp

#include "kinematics.h"

Kinematics kinematics; // Global instance definition

Kinematics::Kinematics() {
    // Constructor, currently nothing to initialize
}

void Kinematics::mmToSteps(const Point3D& mm_coords, long (&steps)[3]) {
    steps[0] = mmToStepsX(mm_coords.x);
    steps[1] = mmToStepsY(mm_coords.y);
    steps[2] = mmToStepsZ(mm_coords.z);
}

Point3D Kinematics::stepsToMm(const long steps[3]) {
    return Point3D(stepsToMmX(steps[0]), stepsToMmY(steps[1]), stepsToMmZ(steps[2]));
}

bool Kinematics::isValidPosition(const Point3D& target_mm) {
    // Check X and Y against 0 and MAX_POS
    if (target_mm.x < 0 || target_mm.x > X_MAX_POS) return false;
    if (target_mm.y < 0 || target_mm.y > Y_MAX_POS) return false;
    
    // Check Z against 0 (pen up) and Z_MAX_POS
    // According to Work_Plan, Z=0 is pen fully up, and pen down positions are positive.
    if (target_mm.z < 0 || target_mm.z > Z_MAX_POS) return false;
    
    return true;
}
