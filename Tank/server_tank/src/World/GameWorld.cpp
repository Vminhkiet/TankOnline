#include "World/GameWorld.hpp"
#include "Utils/Logger.hpp"
#include <cmath>
#include <cstring>

// ════════════════════════════════════════════════════════════════════════════
// Construction
// ════════════════════════════════════════════════════════════════════════════

GameWorld::GameWorld() {}

bool GameWorld::loadMap(const std::string& mapPath)
{
    if (!_map.LoadFromFile(mapPath, _physics)) {
        LOG_ERR("GameWorld: failed to load map: {}", mapPath);
        return false;
    }
    LOG_INFO("GameWorld: map loaded – {} static boxes, {} static capsules",
             _physics._boxes.size(), _physics._capsules.size());
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Player lifecycle
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::addPlayer(uint32_t playerId, const Vector3& spawnPos)
{
    _tanks.emplace(playerId, Tank(playerId, spawnPos));
    Tank& tank = _tanks.at(playerId);

    CapsuleCollider cap;
    cap.entityId = playerId;
    cap.isActive = true;
    cap.radius   = Tank::CAPSULE_RADIUS;
    cap.pA       = tank.capsuleBottom();
    cap.pB       = tank.capsuleTop();
    _physics.addCapsule(cap);

    LOG_INFO("GameWorld: player {} spawned at ({:.1f},{:.1f},{:.1f})",
             playerId, spawnPos.x, spawnPos.y, spawnPos.z);
}

void GameWorld::removePlayer(uint32_t playerId)
{
    _tanks.erase(playerId);
    _physics.removeCapsule(playerId);
    LOG_INFO("GameWorld: player {} removed", playerId);
}

// ════════════════════════════════════════════════════════════════════════════
// Input
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::processInput(uint32_t playerId, const ClientInput& input)
{
    auto it = _tanks.find(playerId);
    if (it == _tanks.end()) return;
    it->second.processInput(input); // store only; movement applied in update()
}

// ════════════════════════════════════════════════════════════════════════════
// World update – called once per server tick
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::update(float deltaTime)
{
    // 0. Apply last received input for every tank (runs every tick, not just on packet arrival)
    for (auto& [id, tank] : _tanks)
        if (tank.isAlive) tank.update(deltaTime);

    // 1. Move bullets with swept collision (prevents tunneling through thin walls)
    for (auto& b : _bullets) {
        if (!b.isActive) continue;

        b.timeToLive -= deltaTime;
        if (b.timeToLive <= 0.f) {
            b.isActive = false;
            _physics.removeSphere(b.id);
            continue;
        }

        Vector3 delta   = b.velocity * deltaTime;
        float   hitFrac = 1.f;
        Vector3 hitNorm;

        if (_physics.sweptSphereVsStatic(b.position, Bullet::RADIUS, delta, hitFrac, hitNorm)) {
            b.position = b.position + delta * hitFrac;
            b.isActive = false;
            _physics.removeSphere(b.id);
        } else {
            b.position = b.position + delta;
            _physics.updateSphere(b.id, b.position);
        }
    }

    // 2. Apply gravity and snap to terrain
    constexpr float GRAVITY = 20.f;
    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        float groundY = _map.GetHeightAt(tank.position.x, tank.position.z);
        if (tank.position.y <= groundY) {
            // On ground — no gravity, hold position
            tank.position.y = groundY;
            tank.velocityY  = 0.f;
        } else {
            // Airborne — integrate gravity
            tank.velocityY  -= GRAVITY * deltaTime;
            tank.position.y += tank.velocityY * deltaTime;
            if (tank.position.y <= groundY) {
                tank.position.y = groundY;
                tank.velocityY  = 0.f;
            }
        }
    }

    // 3. Push current tank positions into physics capsules
    syncColliders();

    // 4. Physics tick (broad-phase grid → narrow-phase → corrections)
    _physics.Tick();

    // 5. Apply corrections + handle collision events
    applyPhysicsResults(deltaTime);

    // 6. Respawn dead tanks (only when respawn is enabled, i.e. non-match mode)
    if (_respawnOnDeath) {
        for (auto& [id, tank] : _tanks) {
            if (tank.isDead()) {
                tank.position = defaultSpawn(id);
                tank.health   = Tank::MAX_HEALTH;
                tank.isAlive  = true;
                tank.velocity = {};
                _physics.updateCapsule(id, tank.capsuleBottom(), tank.capsuleTop());
                LOG_INFO("GameWorld: tank {} respawned", id);
            }
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Sync helpers
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::syncColliders()
{
    for (auto& [id, tank] : _tanks) {
        if (tank.isAlive)
            _physics.updateCapsule(id, tank.capsuleBottom(), tank.capsuleTop());
    }
}

void GameWorld::applyPhysicsResults(float /*deltaTime*/)
{
    // Position corrections: push tanks out of walls / each other
    for (auto& [entityId, correction] : _physics._corrections) {
        auto it = _tanks.find(entityId);
        if (it != _tanks.end())
            it->second.position += correction;
    }

    // Collision events
    for (const auto& m : _physics._manifolds) {
        if (m.type == CollisionType::BULLET_VS_TANK) {
            auto tankIt = _tanks.find(m.entityB);
            if (tankIt != _tanks.end()) {
                bool wasAlive = tankIt->second.isAlive;
                tankIt->second.takeDamage(Tank::BULLET_DAMAGE);
                LOG_INFO("GameWorld: bullet {} hit tank {} (hp={})",
                         m.entityA, m.entityB, tankIt->second.health);
                // Credit kill to bullet owner
                if (wasAlive && tankIt->second.isDead()) {
                    for (const auto& b : _bullets) {
                        if (b.id == m.entityA) { _kills[b.ownerTankId]++; break; }
                    }
                }
            }
            for (auto& b : _bullets) {
                if (b.id == m.entityA && b.isActive) {
                    b.isActive = false;
                    _physics.removeSphere(b.id);
                    break;
                }
            }
        }
    }

    // Spawn bullets for tanks that fired this tick
    for (auto& [id, tank] : _tanks) {
        if (tank.wantsShoot && tank.isAlive) {
            Vector3 muzzlePos = tank.position + Vector3{0.f, Tank::CAPSULE_HEIGHT * 0.7f, 0.f};
            spawnBullet(id, muzzlePos, tank.yaw);
            tank.wantsShoot = false;
        }
    }
}

void GameWorld::spawnBullet(uint32_t ownerTankId, const Vector3& pos, float yaw)
{
    Bullet b;
    b.id          = _nextBulletId++;
    b.ownerTankId = ownerTankId;
    b.position    = pos;
    b.velocity    = Vector3{ std::sin(yaw), 0.f, std::cos(yaw) } * Bullet::SPEED;
    b.timeToLive  = Bullet::TTL;
    b.isActive    = true;
    _bullets.push_back(b);

    SphereCollider sph;
    sph.entityId = b.id;
    sph.isActive = true;
    sph.center   = b.position;
    sph.radius   = Bullet::RADIUS;
    _physics.addSphere(sph);
}

// ════════════════════════════════════════════════════════════════════════════
// Snapshot serialisation
// Wire format: [uint16 tankCount][TankState...][uint16 bulletCount][BulletState...]
// ════════════════════════════════════════════════════════════════════════════

std::vector<uint8_t> GameWorld::getSnapshot() const
{
    std::vector<TankState>   ts;
    std::vector<BulletState> bs;

    for (const auto& [id, tank] : _tanks) {
        TankState t;
        t.tankId = id;
        t.x      = tank.position.x;
        t.y      = tank.position.y;
        t.z      = tank.position.z;
        t.yaw    = tank.yaw;
        t.health = static_cast<int16_t>(tank.health);
        t.flags  = tank.isAlive ? 1u : 0u;
        ts.push_back(t);
    }
    for (const auto& b : _bullets) {
        if (!b.isActive) continue;
        BulletState bl;
        bl.bulletId = b.id;
        bl.x = b.position.x;
        bl.y = b.position.y;
        bl.z = b.position.z;
        bs.push_back(bl);
    }

    uint16_t tc = static_cast<uint16_t>(ts.size());
    uint16_t bc = static_cast<uint16_t>(bs.size());
    size_t   totalBytes = 2 + tc * sizeof(TankState) + 2 + bc * sizeof(BulletState);

    std::vector<uint8_t> buf(totalBytes);
    uint8_t* ptr = buf.data();

    std::memcpy(ptr, &tc, 2); ptr += 2;
    for (auto& t : ts) { std::memcpy(ptr, &t, sizeof(t)); ptr += sizeof(t); }
    std::memcpy(ptr, &bc, 2); ptr += 2;
    for (auto& bl : bs) { std::memcpy(ptr, &bl, sizeof(bl)); ptr += sizeof(bl); }

    return buf;
}

// ════════════════════════════════════════════════════════════════════════════
// Default spawn positions – 8 slots spread around a circle (r=20)
// ════════════════════════════════════════════════════════════════════════════

Vector3 GameWorld::defaultSpawn(uint32_t playerId)
{
    constexpr float R     = 20.f;
    constexpr int   SLOTS = 8;
    float angle = static_cast<float>(playerId % SLOTS) / SLOTS * 6.2831853f;
    return { R * std::cos(angle), 1.f, R * std::sin(angle) };
}

// ════════════════════════════════════════════════════════════════════════════
// Match outcome
// ════════════════════════════════════════════════════════════════════════════

MatchOutcome GameWorld::checkOutcome(float elapsed, float maxDuration,
                                     const std::vector<uint32_t>& playerIds,
                                     MatchResult& result) const
{
    result.kills = _kills;
    result.durationSecs = elapsed;

    // Count players who have actually spawned (connected)
    size_t spawned = 0;
    std::vector<uint32_t> alive;
    for (uint32_t pid : playerIds) {
        auto it = _tanks.find(pid);
        if (it != _tanks.end()) {
            ++spawned;
            if (!it->second.isDead())
                alive.push_back(pid);
        }
    }

    // Win/Draw only make sense once at least 2 players have joined
    if (spawned >= 2) {
        if (alive.size() == 1) {
            result.outcome  = MatchOutcome::Win;
            result.winnerId = alive[0];
            return MatchOutcome::Win;
        }
        if (alive.empty()) {
            result.outcome = MatchOutcome::Draw;
            return MatchOutcome::Draw;
        }
    }

    if (elapsed >= maxDuration) {
        result.outcome = MatchOutcome::Timeout;
        // Winner by kill count
        uint32_t best = 0; int bestKills = -1;
        for (uint32_t pid : playerIds) {
            auto it = _kills.find(pid);
            int k = (it != _kills.end()) ? it->second : 0;
            if (k > bestKills) { bestKills = k; best = pid; }
        }
        result.winnerId = best;
        return MatchOutcome::Timeout;
    }
    return MatchOutcome::Running;
}

Vector3 GameWorld::getSpawnPosition(size_t slotIndex) const {
    const auto& spawns = _map.getSpawnPoints();
    if (!spawns.empty()) {
        const auto& sp = spawns[slotIndex % spawns.size()];
        return { sp.x, sp.y, sp.z };
    }
    return defaultSpawn(static_cast<uint32_t>(slotIndex));
}

void GameWorld::killPlayer(uint32_t playerId) {
    auto it = _tanks.find(playerId);
    if (it == _tanks.end()) return;
    it->second.isAlive = false;
    _physics.removeCapsule(playerId);
}

void GameWorld::logTankPositions(uint32_t matchId) const {
    for (const auto& [id, tank] : _tanks)
        LOG_INFO("  [match {}] player {} | pos=({:.1f},{:.1f},{:.1f}) yaw={:.2f} hp={} alive={}",
                 matchId, id,
                 tank.position.x, tank.position.y, tank.position.z,
                 tank.yaw, tank.health, tank.isAlive ? "yes" : "no");
}
