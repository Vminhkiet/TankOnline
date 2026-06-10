#include "Entities/Tank.hpp"
#include "Network/Packets.hpp"
#include <cmath>

Tank::Tank(uint32_t _id, const Vector3& spawnPos, const TankStats& _stats)
    : id(_id), stats(_stats), position(spawnPos), shootSlowTimer(0.f) 
{
    health = stats.health;
    currentAmmo = stats.magazineCapacity;
}

void Tank::processInput(const ClientInput& input)
{
    if (!isAlive) return;
    lastInput = input;
    targetTurretYaw = input.turretYaw;
    // Only update hull yaw from move packets; shoot packets don't carry hullYaw
    // so they would reset targetYaw to 0, causing the tank to spin back to origin.
    if (!input.shoot) {
        targetYaw = input.hullYaw;
    }
    
    if (input.reload && currentAmmo < stats.magazineCapacity && !isReloading) {
        isReloading = true;
        reloadTimer = stats.reloadTime;
    }

    if (input.shoot && !isReloading && currentAmmo > 0 && fireCooldownTimer <= 0.f) {
        wantsShoot        = true;
        wantsShootForce   = input.launchForce;
        wantsShootYaw     = turretYaw; // use actual interpolated turret yaw instead of instant input yaw
        wantsShootBarrels = input.barrelCount;
        
        currentAmmo--;
        if (currentAmmo <= 0) {
            isReloading = true;
            reloadTimer = stats.reloadTime;
        }

        if (stats.speedReductionWhileShooting > 0.f) {
            shootSlowTimer = 0.5f; // Duration of slow effect after firing
        }
        fireCooldownTimer = 1.0f / stats.fireRate;
    }
}

void Tank::update(float deltaTime)
{
    if (!isAlive) return;

    if (fireCooldownTimer > 0.f) {
        fireCooldownTimer -= deltaTime;
    }

    float currentSpeedMult = 1.0f;
    if (shootSlowTimer > 0.f) {
        shootSlowTimer -= deltaTime;
        currentSpeedMult = 1.0f - (stats.speedReductionWhileShooting / 100.f);
        if (currentSpeedMult < 0.f) currentSpeedMult = 0.f;
    }

    if (isReloading) {
        reloadTimer -= deltaTime;
        if (reloadTimer <= 0.f) {
            isReloading = false;
            currentAmmo = stats.magazineCapacity;
        }
    }

    for (auto it = activeBuffs.begin(); it != activeBuffs.end(); ) {
        it->timeToLive -= deltaTime;
        if (it->hpRegenPerSec > 0.f) {
            it->tickTimer += deltaTime;
            if (it->tickTimer >= 1.0f) {
                int healAmt = static_cast<int>(it->hpRegenPerSec);
                health += healAmt;
                it->tickTimer -= 1.0f;
            }
        }
        if (it->timeToLive <= 0.f) {
            it = activeBuffs.erase(it);
        } else {
            ++it;
        }
    }
    if (health > stats.health) health = stats.health;

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
            targetYaw += lastInput.moveX * TURN_SPEED * currentSpeedMult * deltaTime;
            yaw = targetYaw;
        }
    }
    
    // Anti-spinbot for turret
    float turretAngleDiff = targetTurretYaw - turretYaw;
    while (turretAngleDiff > 3.14159265f)  turretAngleDiff -= 6.2831853f;
    while (turretAngleDiff < -3.14159265f) turretAngleDiff += 6.2831853f;

    float maxTurretTurn = stats.turretRotationSpeed * (3.14159265f / 180.f) * deltaTime * 1.5f;

    if (turretAngleDiff > maxTurretTurn) {
        turretYaw += maxTurretTurn;
    } else if (turretAngleDiff < -maxTurretTurn) {
        turretYaw -= maxTurretTurn;
    } else {
        turretYaw = targetTurretYaw;
    }
    
    // Normalize yaw
    while (yaw > 3.14159265f)  yaw -= 6.2831853f;
    while (yaw < -3.14159265f) yaw += 6.2831853f;

    float sinY = std::sin(yaw);
    float cosY = std::cos(yaw);
    Vector3 forward = { sinY, 0.f, cosY };

    velocity = forward * (lastInput.moveZ * stats.speed * currentSpeedMult);
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
