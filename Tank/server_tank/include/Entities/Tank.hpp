#pragma once
#include <cstdint>
#include "Physics/PhysicsTypes.hpp"
#include "Network/Packets.hpp"

class Tank {
public:
    static constexpr float MAX_SPEED    = 12.f;
    static constexpr float TURN_SPEED   = 3.14159f; // radians / second (= 180 deg/s, matches Unity client)
    static constexpr int   MAX_HEALTH   = 100;
    static constexpr int   BULLET_DAMAGE = 25;

    uint32_t id;
    Vector3  position;    // bottom-center of capsule
    float    yaw = 0.f;   // rotation around Y axis (radians)
    Vector3  velocity;    // world-space velocity (set from input each tick)
    float    velocityY = 0.f; // vertical velocity (gravity)
    int      health   = MAX_HEALTH;
    bool     isAlive  = true;
    bool     wantsShoot = false; // set true by processInput when shoot pressed

    ClientInput lastInput; // last received input — reapplied every tick

    explicit Tank(uint32_t id, const Vector3& spawnPos);

    void processInput(const ClientInput& input); // store input only, no movement
    void update(float deltaTime);                // apply lastInput every tick
    void takeDamage(int damage);
    bool isDead() const { return !isAlive || health <= 0; }

};
