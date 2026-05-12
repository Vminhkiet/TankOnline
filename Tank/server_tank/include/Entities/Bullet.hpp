#pragma once
#include <cstdint>
#include "Physics/PhysicsTypes.hpp"

struct Bullet {
    static constexpr float RADIUS = 0.25f;   // sphere collider radius (units)
    static constexpr float SPEED  = 60.f;    // units / second
    static constexpr float TTL    = 4.f;     // seconds before auto-destroy

    uint32_t id          = 0;
    uint32_t ownerTankId = 0;
    Vector3  position;
    Vector3  velocity;      // world-space, magnitude == SPEED
    float    timeToLive   = TTL;
    bool     isActive     = false;
};
