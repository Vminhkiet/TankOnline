#include "Entities/Tank.hpp"
#include "Network/Packets.hpp"
#include <cmath>

Tank::Tank(uint32_t _id, const Vector3& spawnPos, const TankStats& _stats)
    : id(_id), stats(_stats), position(spawnPos), shootFreezeTimer(0.f) 
{
    health = stats.health;
    currentAmmo = stats.magazineCapacity;
}

void Tank::processInput(const ClientInput& input)
{
    if (!isAlive) return;
    lastInput = input;
    turretYaw = input.turretYaw;
    targetYaw = input.hullYaw; // authoritative sync from client, validated in update()
    
    if (input.reload && currentAmmo < stats.magazineCapacity && !isReloading) {
        isReloading = true;
        reloadTimer = stats.reloadTime;
    }

    if (input.shoot && !isReloading && currentAmmo > 0) {
        wantsShoot        = true;
        wantsShootForce   = input.launchForce;
        wantsShootYaw     = input.turretYaw;
        wantsShootBarrels = input.barrelCount;
        
        currentAmmo--;
        if (currentAmmo <= 0) {
            isReloading = true;
            reloadTimer = stats.reloadTime;
        }

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

    if (isReloading) {
        reloadTimer -= deltaTime;
        if (reloadTimer <= 0.f) {
            isReloading = false;
            currentAmmo = stats.magazineCapacity;
        }
    }

    // Anti-spinbot rotation sync
    float angleDiff = targetYaw - yaw;
    while (angleDiff > 3.14159265f)  angleDiff -= 6.2831853f;
    while (angleDiff < -3.14159265f) angleDiff += 6.2831853f;

    // Allow turning up to TURN_SPEED + 50% tolerance for network jitter
    float maxTurn = TURN_SPEED * deltaTime * 1.5f;

    if (angleDiff > maxTurn) {
        yaw += maxTurn;
    } else if (angleDiff < -maxTurn) {
        yaw -= maxTurn;
    } else {
        yaw = targetYaw;
        // Extrapolate if we've caught up and user is still holding the turn key (smoothness between packets)
        if (lastInput.moveX != 0) {
            targetYaw += lastInput.moveX * TURN_SPEED * deltaTime;
            yaw = targetYaw;
        }
    }
    
    // Normalize yaw
    while (yaw > 3.14159265f)  yaw -= 6.2831853f;
    while (yaw < -3.14159265f) yaw += 6.2831853f;

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
