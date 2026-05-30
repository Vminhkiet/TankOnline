#pragma once
#include <cstdint>
#include "Physics/PhysicsTypes.hpp"
#include "Network/Packets.hpp"
#include "Core/MatchConfig.hpp"

class Tank {
public:
    static constexpr float TURN_SPEED   = 3.14159f; // radians / second (= 180 deg/s, matches Unity client)

    uint32_t id;
    TankStats stats;
    
    Vector3  position;    // bottom-center of capsule
    float    yaw = 0.f;   // rotation around Y axis (radians)
    float    targetYaw = 0.f; // authoritative yaw from client, clamped by server
    float    turretYaw = 0.f; // rotation of the turret (radians)
    Vector3  velocity;    // world-space velocity (set from input each tick)
    float    velocityY = 0.f; // vertical velocity (gravity)
    int      health;
    bool     isAlive  = true;
    bool     wantsShoot      = false; // set true by processInput when shoot pressed
    float    wantsShootForce = 20.f;  // bullet speed (m/s) for next shot
    float    wantsShootYaw   = 0.f;   // actual aiming yaw of the turret
    uint8_t  wantsShootBarrels = 1;   // number of barrels
    float    shootFreezeTimer = 0.f;  // timer tracking freeze duration
    float    fireCooldownTimer = 0.f; // timer for strict server-side fire rate enforcement

    int      currentAmmo = 1;         // current shots in magazine
    float    reloadTimer = 0.f;       // timer for reload
    bool     isReloading = false;     // true if currently reloading

    ClientInput lastInput; // last received input — reapplied every tick

    explicit Tank(uint32_t id, const Vector3& spawnPos, const TankStats& stats = TankStats{});

    void processInput(const ClientInput& input); // store input only, no movement
    void update(float deltaTime);                // apply lastInput every tick
    void takeDamage(int damage);
    bool isDead() const { return !isAlive || health <= 0; }

};
