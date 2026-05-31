#include <iostream>
#include <cstdint>

struct TankState {
    uint32_t tankId = 0;
    float    x = 0.f, y = 0.f, z = 0.f;
    float    yaw    = 0.f;
    float    turretYaw = 0.f;
    int16_t  health = 100;
    uint8_t  flags  = 0;   
    uint16_t score  = 0;
    uint8_t  placement = 0;
    uint8_t  bushRegion = 0;
};

int main() {
    std::cout << sizeof(TankState) << std::endl;
    return 0;
}
