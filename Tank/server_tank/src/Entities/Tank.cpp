#include "Entities/Tank.hpp"
#include "Network/Packets.hpp"
#include <cmath>

Tank::Tank(uint32_t _id, const Vector3& spawnPos, const TankStats& _stats)
    : id(_id), stats(_stats), position(spawnPos), shootFreezeTimer(0.f) 
{
    health = stats.health;
}

void Tank::processInput(const ClientInput& input)
{
    if (!isAlive) return;
    lastInput = input;
    if (input.shoot) {
        wantsShoot        = true;
        wantsShootForce   = input.launchForce;
        wantsShootYaw     = input.turretYaw;
        wantsShootBarrels = input.barrelCount;
        if (!stats.canMoveWhileShooting) {
            shootFreezeTimer = 0.5f; // matches Unity's freeze duration
        }
    }
}

void Tank::update(float deltaTime)
{
    if (!isAlive) return;

    if (shootFreezeTimer > 0.f) {
        shootFreezeTimer -= deltaTime;
        lastInput.moveX = 0;
        lastInput.moveZ = 0;
    }

    // Apply last received input every server tick so movement is continuous
    yaw += lastInput.moveX * TURN_SPEED * deltaTime;

    float sinY = std::sin(yaw);
    float cosY = std::cos(yaw);
    Vector3 forward = { sinY, 0.f, cosY };

    velocity = forward * (lastInput.moveZ * stats.speed);
    position = position + velocity * deltaTime;
}

void Tank::takeDamage(int damage)
{
    if (!isAlive) return;
    health -= damage;
    if (health <= 0) {
        health  = 0;
        isAlive = false;
    }
}
