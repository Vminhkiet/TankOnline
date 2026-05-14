#include "Entities/Tank.hpp"
#include "Network/Packets.hpp"
#include <cmath>

Tank::Tank(uint32_t _id, const Vector3& spawnPos)
    : id(_id), position(spawnPos) {}

void Tank::processInput(const ClientInput& input)
{
    if (!isAlive) return;
    lastInput = input;
    if (input.shoot) {
        wantsShoot      = true;
        wantsShootForce = input.launchForce;
    }
}

void Tank::update(float deltaTime)
{
    if (!isAlive) return;

    // Apply last received input every server tick so movement is continuous
    yaw += lastInput.moveX * TURN_SPEED * deltaTime;

    float sinY = std::sin(yaw);
    float cosY = std::cos(yaw);
    Vector3 forward = { sinY, 0.f, cosY };

    velocity = forward * (lastInput.moveZ * MAX_SPEED);
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
