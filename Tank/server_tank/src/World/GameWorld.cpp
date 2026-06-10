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

    const auto& defaultCfg = _map.getTankConfig("");
    LOG_INFO("GameWorld: map loaded – {} boxes, {} capsules | tank({:.2f},{:.2f},{:.2f}) bullet_r={:.3f}",
             _physics._boxes.size(), _physics._capsules.size(),
             defaultCfg.extentX, defaultCfg.extentY, defaultCfg.extentZ,
             _map.getBulletConfig().radius);

    // Generate 30 random spawn points for items
    std::mt19937 rng(1337); // Fixed seed or random device
    std::uniform_real_distribution<float> distXZ(-38.0f, 38.0f);
    for (int i = 0; i < 30; ++i) {
        float x = distXZ(rng);
        float z = distXZ(rng);
        float y = surfaceHeight(x, z);
        _itemSpawnPoints.push_back({x, y + 0.5f, z}); // Float slightly above ground
    }

    return true;
}



// ════════════════════════════════════════════════════════════════════════════
// Skills
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::castSkill(uint32_t playerId, const PacketCastSkill& pkt)
{
    auto it = _tanks.find(playerId);
    if (it == _tanks.end()) return;
    Tank& tank = it->second;
    if (!tank.isAlive) return;

    std::string skillName(pkt.skillName);
    const GameMap::SkillConfig* config = _map.getSkillConfig(skillName);
    if (!config) {
        LOG_WARN("GameWorld: player {} tried to cast unknown skill '{}'", playerId, skillName);
        return;
    }

    // Cooldown check ONLY if not charging
    float now = _elapsedTime;
    if (!pkt.isCharging) {
        if (now < _skillCooldowns[playerId][skillName]) {
            LOG_WARN("GameWorld: player {} tried to cast '{}' on cooldown", playerId, skillName);
            return;
        }
        // Set cooldown
        _skillCooldowns[playerId][skillName] = now + config->cooldown;
    }

    if (!pkt.isCharging) {
        // Logic based on skillType
    // 1: Dash, 2: Buff, 3: ShieldDome, 4: Laser
    if (config->skillType == 1) { // Dash
        float dashDist = config->parameters.size() > 0 ? config->parameters[0] : 10.f;
        Vector3 dir = {pkt.dirX, 0.f, pkt.dirZ};
        if (dir.x == 0.f && dir.z == 0.f) dir = { std::sin(tank.yaw), 0.f, std::cos(tank.yaw) };
        tank.position.x += dir.x * dashDist;
        tank.position.z += dir.z * dashDist;
        tank.position.z += dir.z * dashDist;
    } 
    else if (config->skillType == 2) { // Buff
        // Nhập 20 nghĩa là +20% sát thương (1.2x)
        float dmgMult = config->parameters.size() > 0 ? (1.0f + config->parameters[0] / 100.0f) : 1.15f;
        // Nhập 5 nghĩa là 5% máu mỗi giây (0.05)
        float hpRegenPct = config->parameters.size() > 1 ? (config->parameters[1] / 100.0f) : 0.05f;
        float maxHp = static_cast<float>(_maxHealth[playerId]);
        
        ActiveBuff buff;
        buff.timeToLive = config->duration > 0.f ? config->duration : 8.f;
        buff.dmgMultiplier = dmgMult;
        buff.hpRegenPerSec = maxHp * hpRegenPct;
        tank.activeBuffs.push_back(buff);
    }
    else if (config->skillType == 3) { // ShieldDome
        ActiveShield shield;
        shield.ownerId = playerId;
        shield.position = tank.position; // Dome spawns at caster
        shield.radius = config->radius > 0.f ? config->radius : 5.f;
        shield.health = config->parameters.size() > 0 ? config->parameters[0] : 1000.f;
        shield.timeToLive = config->duration > 0.f ? config->duration : 5.f;
        // Nhập 50 nghĩa là 50% slow (0.5)
        shield.slowPercent = config->parameters.size() > 1 ? (config->parameters[1] / 100.0f) : 0.2f;
        _activeShields.push_back(shield);
        LOG_INFO("GameWorld: player {} spawned shield (hp={}, radius={})", playerId, shield.health, shield.radius);
    }
    else if (config->skillType == 4) { // Laser (Hitscan)
        float damage = config->parameters.size() > 0 ? config->parameters[0] : 50.f;
        float length = config->length > 0.f ? config->length : 20.f;
        Vector3 dir = {pkt.dirX, 0.f, pkt.dirZ};
        if (dir.x == 0.f && dir.z == 0.f) dir = { std::sin(tank.turretYaw), 0.f, std::cos(tank.turretYaw) };
        
        // Simple raycast against tanks
        for (auto& [tid, target] : _tanks) {
            if (tid == playerId || !target.isAlive) continue;
            
            float dx = target.position.x - tank.position.x;
            float dz = target.position.z - tank.position.z;
            float distSq = dx*dx + dz*dz;
            
            // Fast check if within laser length (approx)
            if (distSq <= length*length) {
                // Project target onto laser ray
                float t = dx*dir.x + dz*dir.z;
                if (t > 0 && t <= length) {
                    float px = t * dir.x;
                    float pz = t * dir.z;
                    float distToLineSq = (dx-px)*(dx-px) + (dz-pz)*(dz-pz);
                    if (distToLineSq <= 4.0f) { // 2m width laser approx
                        // Hit by laser! Check if blocked by shield first
                        bool blocked = false;
                        for (auto& shield : _activeShields) {
                            if (shield.ownerId == playerId) continue;
                            
                            // If caster is inside the shield, the laser freely passes out!
                            float cDx = tank.position.x - shield.position.x;
                            float cDz = tank.position.z - shield.position.z;
                            if (cDx*cDx + cDz*cDz <= shield.radius * shield.radius) {
                                continue;
                            }

                            float sDx = shield.position.x - tank.position.x;
                            float sDz = shield.position.z - tank.position.z;
                            float sT = sDx*dir.x + sDz*dir.z;
                            if (sT > 0) { 
                                float sPx = sT * dir.x;
                                float sPz = sT * dir.z;
                                float sDistToLineSq = (sDx-sPx)*(sDx-sPx) + (sDz-sPz)*(sDz-sPz);
                                if (sDistToLineSq <= shield.radius * shield.radius) {
                                    blocked = true;
                                    shield.health -= damage;
                                    break;
                                }
                            }
                        }
                        
                        if (!blocked) {
                            int dmgInt = static_cast<int>(damage);
                            target.takeDamage(dmgInt);
                            _damageDealt[playerId] += dmgInt;
                            _damageHistory[tid][playerId] += dmgInt;
                            
                            if (target.isDead()) {
                                _kills[playerId]++;
                                _deaths[tid]++;
                                _matchScoreBase[playerId] += 5;
                            }
                        }
                    }
                }
            }
        }
        }
    }

    // Broadcast event
    EventSkillCastPacket ev;
    ev.matchId = 0; // Assigned in Match.cpp
    ev.opcode = static_cast<uint16_t>(Opcode::S2C_EVENT_SKILL_CAST);
    ev.casterId = playerId;
    std::strncpy(ev.skillName, pkt.skillName, sizeof(ev.skillName) - 1);
    ev.targetX = pkt.targetX;
    ev.targetY = pkt.targetY;
    ev.targetZ = pkt.targetZ;
    ev.dirX = pkt.dirX;
    ev.dirZ = pkt.dirZ;
    ev.isCharging = pkt.isCharging;
    _skillCastEvents.push_back(ev);
}

// ════════════════════════════════════════════════════════════════════════════
// Player lifecycle
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::addPlayer(uint32_t playerId, const Vector3& spawnPos, const TankStats& stats)
{
    // Override TankStats defaults with real gameplay stats from world.json (TankConfig)
    TankStats finalStats = stats;
    const GameMap::TankConfig& cfg = _map.getTankConfig(stats.name);
    // Use the larger of map config or explicitly-set stats.health.
    // Production: stats.health defaults to 100 → map overrides to cfg.maxHealth (correct).
    // Benchmark:  stats.health = 9,999,999 → preserved so tanks never die during measurement.
    finalStats.health    = std::max(stats.health, static_cast<int>(cfg.maxHealth));
    finalStats.speed     = cfg.movementSpeed;
    finalStats.damage    = static_cast<int>(cfg.damage);
    finalStats.fireRate  = cfg.fireRate;
    finalStats.fireRange = cfg.fireRange;
    finalStats.magazineCapacity = cfg.magazineCapacity;
    finalStats.reloadTime = cfg.reloadTime;
    finalStats.speedReductionWhileShooting = cfg.speedReductionWhileShooting;
    finalStats.turretRotationSpeed = cfg.turretRotationSpeed;

    _tanks.emplace(playerId, Tank(playerId, spawnPos, finalStats));

    OBBCollider obb;
    obb.entityId = playerId;
    obb.isActive = true;
    obb.isStatic = false;
    obb.extents  = { cfg.extentX, cfg.extentY, cfg.extentZ };
    obb.center   = { spawnPos.x + cfg.offsetX, spawnPos.y + cfg.offsetY, spawnPos.z + cfg.offsetZ };
    obb.axisX = {1.f, 0.f, 0.f};
    obb.axisY = {0.f, 1.f, 0.f};
    obb.axisZ = {0.f, 0.f, 1.f};
    _physics.addDynamicBox(obb);

    LOG_INFO("GameWorld: player {} spawned at ({:.1f},{:.1f},{:.1f}) tank={} hp={} spd={:.1f} dmg={} fr={:.1f}",
             playerId, spawnPos.x, spawnPos.y, spawnPos.z,
             finalStats.name, finalStats.health, finalStats.speed, finalStats.damage, finalStats.fireRate);
             
    // Initialize tracking for this player using the real max health
    _maxHealth[playerId] = finalStats.health;
    _lastCombatTime[playerId] = 0.0f; // Will be properly initialized in first update
    _matchScoreBase[playerId] = 0;
}

void GameWorld::removePlayer(uint32_t playerId)
{
    _tanks.erase(playerId);
    _physics.removeDynamicBox(playerId);
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

void GameWorld::updateBullets(float deltaTime)
{
    constexpr float BULLET_GRAVITY = 3.0f;

    struct TankHitCache {
        uint32_t id;
        float hitX, hitZ;
        float offsetX, offsetZ;
    };
    std::vector<TankHitCache> tankCache;
    tankCache.reserve(_tanks.size());
    const float bulletHitR = _map.getBulletConfig().hitRadius;
    for (const auto& [tid, tank] : _tanks) {
        if (!tank.isAlive) continue;
        const GameMap::TankConfig& cfg = _map.getTankConfig(tank.stats.name);
        tankCache.push_back({ tid,
            cfg.extentX + bulletHitR, cfg.extentZ + bulletHitR,
            cfg.offsetX, cfg.offsetZ });
    }

    for (auto& b : _bullets) {
        if (!b.isActive) continue;

        b.timeToLive -= deltaTime;
        if (b.timeToLive <= 0.f) {
            b.isActive = false;
            _physics.removeSphere(b.id);
            continue;
        }

        b.velocity.y -= BULLET_GRAVITY * deltaTime;

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

            // 1) Check shield collisions
            bool blockedByShield = false;
            for (auto& shield : _activeShields) {
                if (shield.ownerId == b.ownerTankId) continue;

                // If bullet spawned inside the shield, it can freely pass through this shield!
                float dxSpawn = b.spawnPosition.x - shield.position.x;
                float dySpawn = b.spawnPosition.y - shield.position.y;
                float dzSpawn = b.spawnPosition.z - shield.position.z;
                if (dxSpawn * dxSpawn + dySpawn * dySpawn + dzSpawn * dzSpawn <= shield.radius * shield.radius) {
                    continue; // Skip this shield, bullet originated inside it
                }

                float dxS = b.position.x - shield.position.x;
                float dyS = b.position.y - shield.position.y;
                float dzS = b.position.z - shield.position.z;
                float distSq = dxS*dxS + dyS*dyS + dzS*dzS;
                float rSum = shield.radius + Bullet::RADIUS;
                if (distSq <= rSum * rSum) {
                    blockedByShield = true;
                    shield.health -= b.damage;
                    break;
                }
            }

            if (blockedByShield) {
                b.isActive = false;
                _physics.removeSphere(b.id);
                continue;
            }

            // 2) Check tank collisions
            for (const auto& tc : tankCache) {
                if (tc.id == b.ownerTankId) continue;
                auto& tank = _tanks.at(tc.id);
                if (!tank.isAlive) continue;
                float bulletHitX = tc.hitX, bulletHitZ = tc.hitZ;
                float sy = std::sin(tank.yaw), cy = std::cos(tank.yaw);
                
                float centerX = tank.position.x + tc.offsetX * cy + tc.offsetZ * sy;
                float centerZ = tank.position.z - tc.offsetX * sy + tc.offsetZ * cy;
                
                float dx = b.position.x - centerX;
                float dz = b.position.z - centerZ;
                float lx =  dx * cy - dz * sy;
                float lz =  dx * sy + dz * cy;
                if (std::fabs(lx) > bulletHitX || std::fabs(lz) > bulletHitZ) continue;
                bool wasAlive = tank.isAlive;
                tank.takeDamage(b.damage);
                
                const uint32_t hitTid = tc.id;
                _damageDealt[b.ownerTankId] += b.damage;
                _damageHistory[hitTid][b.ownerTankId] += b.damage;

                _lastCombatTime[b.ownerTankId] = _elapsedTime;
                _lastCombatTime[hitTid] = _elapsedTime;

                if (tank.isDead() && wasAlive) {
                    _kills[b.ownerTankId]++;
                    _deaths[hitTid]++;
                    _matchScoreBase[b.ownerTankId] += 5;

                    float maxHp = _maxHealth[hitTid] > 0 ? _maxHealth[hitTid] : 100.0f;
                    float assistThreshold = maxHp * 0.3f;
                    for (auto const& [attacker, dmg] : _damageHistory[hitTid]) {
                        if (attacker != b.ownerTankId && dmg >= assistThreshold) {
                            _matchScoreBase[attacker] += 2;
                        }
                    }

                    int aliveCount = 0;
                    for (auto const& [otherId, otherTank] : _tanks) {
                        if (otherTank.isAlive) aliveCount++;
                    }
                    _survivalPlacement[hitTid] = aliveCount + 1;
                }
                b.isActive = false;
                _physics.removeSphere(b.id);
                break;
            }
        }
    }

    for (auto& b : _bullets) {
        if (!b.isActive) continue;
        float groundY = surfaceHeight(b.position.x, b.position.z);
        if (b.position.y <= groundY + Bullet::RADIUS) {
            b.isActive = false;
            _physics.removeSphere(b.id);
        }
    }

    _bullets.erase(
        std::remove_if(_bullets.begin(), _bullets.end(),
                       [](const Bullet& b){ return !b.isActive; }),
        _bullets.end());
}

void GameWorld::runPhysics(float deltaTime)
{
    _elapsedTime += deltaTime;
    constexpr float GRAVITY = 20.f;

    for (auto& [id, tank] : _tanks)
        if (tank.isAlive) tank.update(deltaTime);

    for (auto it = _activeShields.begin(); it != _activeShields.end(); ) {
        it->timeToLive -= deltaTime;
        if (it->timeToLive <= 0.f || it->health <= 0.f) {
            it = _activeShields.erase(it);
        } else {
            ++it;
        }
    }

    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        tank.stats.speed = _map.getTankConfig(tank.stats.name).movementSpeed;
        for (const auto& shield : _activeShields) {
            if (shield.ownerId == id) continue;
            float dx = tank.position.x - shield.position.x;
            float dz = tank.position.z - shield.position.z;
            if (dx*dx + dz*dz <= shield.radius * shield.radius) {
                tank.stats.speed *= (1.0f - shield.slowPercent);
            }
        }
    }

    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        uint32_t boxId  = 0;
        float groundY   = surfaceHeight(tank.position.x, tank.position.z, &boxId);
        uint32_t prevBox = _tankOnBox.count(id) ? _tankOnBox.at(id) : 0;
        if (boxId != prevBox) _tankOnBox[id] = boxId;

        if (tank.position.y <= groundY) {
            tank.position.y = groundY;
            tank.velocityY  = 0.f;
        } else {
            tank.velocityY  -= GRAVITY * deltaTime;
            tank.position.y += tank.velocityY * deltaTime;
            if (tank.position.y <= groundY) {
                tank.position.y = groundY;
                tank.velocityY  = 0.f;
            }
        }
    }

    syncColliders();
    _physics.Tick();
    applyPhysicsResults(deltaTime);

    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        for (auto it = _activeItems.begin(); it != _activeItems.end(); ) {
            float dist = std::sqrt((tank.position.x - it->pos.x)*(tank.position.x - it->pos.x) + 
                                   (tank.position.z - it->pos.z)*(tank.position.z - it->pos.z));
            if (dist < 2.5f) {
                tank.health += 500;
                float maxHp = _maxHealth[id] > 0 ? _maxHealth[id] : 100.0f;
                if (tank.health > maxHp) tank.health = static_cast<int>(maxHp);

                PacketDespawnItem pkt{};
                pkt.opcode = static_cast<uint16_t>(Opcode::S2C_EVENT_DESPAWN_ITEM);
                pkt.itemId = it->id;
                _itemDespawnEvents.push_back(pkt);
                it = _activeItems.erase(it);
            } else {
                ++it;
            }
        }
    }

    if (_respawnOnDeath) {
        for (auto& [id, tank] : _tanks) {
            if (tank.isDead()) {
                const GameMap::TankConfig& cfg = _map.getTankConfig(tank.stats.name);
                tank.position = defaultSpawn(id);
                tank.health   = tank.stats.health;
                tank.isAlive  = true;
                tank.velocity = {};
                OBBCollider obb;
                obb.entityId = id;
                obb.isActive = true;
                obb.isStatic = false;
                obb.extents  = { cfg.extentX, cfg.extentY, cfg.extentZ };
                obb.center   = { tank.position.x + cfg.offsetX, tank.position.y + cfg.offsetY, tank.position.z + cfg.offsetZ };
                obb.axisX = {1.f, 0.f, 0.f};
                obb.axisY = {0.f, 1.f, 0.f};
                obb.axisZ = {0.f, 0.f, 1.f};
                _physics.addDynamicBox(obb);
            }
        }
    }
}

void GameWorld::update(float deltaTime)
{
    // _elapsedTime is now updated in updateBullets or runPhysics, or we can just keep it here if someone uses update() instead
    // But to avoid double counting if someone uses update(), we'll remove it here and put it in runPhysics
    updateBullets(deltaTime);
    runPhysics(deltaTime);
    spawnItems();
}


void GameWorld::spawnItems()
{
    if (_itemSpawnPoints.empty()) return;
    if (_activeItems.size() < 5) {
        int numPoints = static_cast<int>(_itemSpawnPoints.size());
        std::vector<int> indices(numPoints);
        for (int i = 0; i < numPoints; ++i) indices[i] = i;
        std::random_device rd;
        std::mt19937 g(rd());
        std::shuffle(indices.begin(), indices.end(), g);

        int bestIndex = -1;
        float maxDist = -1.0f;
        for (int idx : indices) {
            Vector3 pt = _itemSpawnPoints[idx];
            bool occupied = false;
            for (const auto& item : _activeItems) {
                if (std::fabs(item.pos.x - pt.x) < 0.5f && std::fabs(item.pos.z - pt.z) < 0.5f) {
                    occupied = true; break;
                }
            }
            if (occupied) continue;
            float closestDistToTank = 999999.0f;
            for (const auto& [tid, tank] : _tanks) {
                if (!tank.isAlive) continue;
                float dist = std::sqrt((tank.position.x - pt.x)*(tank.position.x - pt.x) + 
                                       (tank.position.z - pt.z)*(tank.position.z - pt.z));
                if (dist < closestDistToTank) closestDistToTank = dist;
            }
            if (closestDistToTank > 10.0f || _tanks.empty()) {
                bestIndex = idx; break;
            }
            if (closestDistToTank > maxDist) {
                maxDist = closestDistToTank;
                bestIndex = idx;
            }
        }
        if (bestIndex != -1) {
            Item newItem;
            newItem.id = _nextItemId++;
            newItem.pos = _itemSpawnPoints[bestIndex];
            _activeItems.push_back(newItem);

            PacketSpawnItem pkt{};
            pkt.opcode = static_cast<uint16_t>(Opcode::S2C_EVENT_SPAWN_ITEM);
            pkt.itemId = newItem.id;
            pkt.x = newItem.pos.x;
            pkt.y = newItem.pos.y;
            pkt.z = newItem.pos.z;
            _itemSpawnEvents.push_back(pkt);
        }
    }
}

std::vector<EventShootPacket> GameWorld::getShootEvents()
{
    std::vector<EventShootPacket> res;
    res.swap(_shootEvents);
    return res;
}

std::vector<EventSkillCastPacket> GameWorld::getSkillCastEvents()
{
    std::vector<EventSkillCastPacket> res;
    res.swap(_skillCastEvents);
    return res;
}

std::vector<PacketSpawnItem> GameWorld::getItemSpawnEvents() {
    auto copy = _itemSpawnEvents;
    _itemSpawnEvents.clear();
    return copy;
}

std::vector<PacketDespawnItem> GameWorld::getItemDespawnEvents() {
    auto copy = _itemDespawnEvents;
    _itemDespawnEvents.clear();
    return copy;
}

// ════════════════════════════════════════════════════════════════════════════
// Sync helpers
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::syncColliders()
{
    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        const GameMap::TankConfig& cfg = _map.getTankConfig(tank.stats.name);
        // Rotate OBB axes with tank yaw: forward = {sin, 0, cos}, right = {cos, 0, -sin}
        float sy = std::sin(tank.yaw), cy = std::cos(tank.yaw);
        Vector3 axisX{  cy, 0.f, -sy };
        Vector3 axisZ{  sy, 0.f,  cy };
        Vector3 center{ tank.position.x + cfg.offsetX * cy + cfg.offsetZ * sy,
                        tank.position.y + cfg.offsetY,
                        tank.position.z - cfg.offsetX * sy + cfg.offsetZ * cy };
        _physics.updateDynamicBox(id, center, axisX, {0.f, 1.f, 0.f}, axisZ);
    }
}

void GameWorld::applyPhysicsResults(float /*deltaTime*/)
{
    // Step-up detection: xác định tank nào cần leo lên mặt box thay vì bị đẩy ra
    // Key insight: khi SAT chọn trục ngang (overlap nhỏ khi vừa chạm mép),
    // nếu bỏ correction ngang và snap lên top, tick sau tank sẽ nằm trong footprint
    // và surfaceHeight sẽ giữ đúng height.
    constexpr float STEP_HEIGHT = 0.8f;
    std::unordered_map<uint32_t, float> stepUpY; // tankId → target Y

    for (const auto& m : _physics._manifolds) {
        if (m.type != CollisionType::TANK_VS_WALL) continue;
        if (std::fabs(m.normal.y) > 0.5f) continue; // đã là push lên, không cần xử lý
        auto tankIt = _tanks.find(m.entityA);
        if (tankIt == _tanks.end()) continue;
        float tankY = tankIt->second.position.y;
        for (const auto& box : _physics._boxes) {
            if (box.entityId != m.entityB) continue;
            float topY = box.center.y
                + std::fabs(box.axisX.y) * box.extents.x
                + std::fabs(box.axisY.y) * box.extents.y
                + std::fabs(box.axisZ.y) * box.extents.z;
            float gap = topY - tankY;
            if (gap > 0.f && gap <= STEP_HEIGHT) {
                auto prev = stepUpY.find(m.entityA);
                if (prev == stepUpY.end() || topY > prev->second)
                    stepUpY[m.entityA] = topY;
            }
            break;
        }
    }

    // Position corrections: push tanks out of walls / each other
    // Nếu tank đang step-up: chỉ giữ phần Y của correction, bỏ ngang
    // (để tank tiếp tục tiến vào footprint, không bị đẩy ra ngoài mép)
    for (auto& [entityId, correction] : _physics._corrections) {
        auto it = _tanks.find(entityId);
        if (it == _tanks.end()) continue;
        if (stepUpY.count(entityId))
            it->second.position.y += correction.y; // chỉ áp dụng vertical
        else
            it->second.position += correction;
    }

    // Snap step-up tanks lên top của box
    for (auto& [entityId, targetY] : stepUpY) {
        auto it = _tanks.find(entityId);
        if (it != _tanks.end())
            it->second.position.y = targetY;
    }

    // Spawn bullets for tanks that fired this tick
    for (auto& [id, tank] : _tanks) {
        if (tank.wantsShoot && tank.isAlive) {
            const GameMap::TankConfig& cfg = _map.getTankConfig(tank.stats.name);
            float sy = std::sin(tank.yaw), cy = std::cos(tank.yaw);
            
            // Calculate world turret pivot
            Vector3 worldTurretPivot = {
                tank.position.x + cfg.turretOffset.x * cy + cfg.turretOffset.z * sy,
                tank.position.y + cfg.turretOffset.y,
                tank.position.z - cfg.turretOffset.x * sy + cfg.turretOffset.z * cy
            };

            // Calculate the local Right direction of the turret in world space:
            Vector3 localRight = { std::cos(tank.wantsShootYaw), 0.f, -std::sin(tank.wantsShootYaw) };

            float totalDmgMult = 1.0f;
            for (const auto& buff : tank.activeBuffs) {
                totalDmgMult *= buff.dmgMultiplier;
            }
            
            int bulletDamage = static_cast<int>(tank.stats.damage * totalDmgMult);
            if (tank.stats.holdsToCharge) {
                float ratio = tank.wantsShootForce / 30.0f;
                if (ratio < 0.1f) ratio = 0.1f;
                if (ratio > 1.0f) ratio = 1.0f;
                bulletDamage = static_cast<int>(bulletDamage * ratio);
            }

            int count = tank.wantsShootBarrels > 0 ? tank.wantsShootBarrels : 1;

            EventShootPacket ev{};
            ev.opcode = static_cast<uint16_t>(Opcode::S2C_EVENT_SHOOT);
            ev.shooterId = id;
            ev.weaponType = static_cast<uint8_t>(cfg.weaponType);
            ev.barrelCount = count;
            ev.turretYaw = tank.wantsShootYaw;
            ev.hitTankId = 0; // Hitscan logic can populate this if we implement it later

            if (!cfg.barrelOffsets.empty()) {
                count = (std::min)((int)cfg.barrelOffsets.size(), count);
                for (int i = 0; i < count; ++i) {
                    Vector3 bo = cfg.barrelOffsets[i];
                    // Rotate local barrel offset by tank's turret yaw
                    float by = std::sin(tank.wantsShootYaw), cty = std::cos(tank.wantsShootYaw);
                    Vector3 worldOffset = {
                        bo.x * cty + bo.z * by,
                        bo.y,
                        -bo.x * by + bo.z * cty
                    };
                    Vector3 muzzlePos = worldTurretPivot + worldOffset;
                    
                    if (cfg.weaponType == 0) { // Projectile
                        spawnBullet(id, muzzlePos, tank.wantsShootYaw, tank.wantsShootForce, bulletDamage);
                    } else if (cfg.weaponType == 1) { // Hitscan
                        Vector3 dir = { std::sin(tank.wantsShootYaw), 0.f, std::cos(tank.wantsShootYaw) };
                        PhysicsWorld::RaycastHit hit = _physics.Raycast(muzzlePos, dir, tank.stats.fireRange, id, _map.getBulletConfig().hitRadius);
                        
                        bool blockedByShield = false;
                        float maxDist = hit.hit ? hit.distance : tank.stats.fireRange;
                        for (auto& shield : _activeShields) {
                            if (shield.ownerId == id) continue;
                            float dxSpawn = muzzlePos.x - shield.position.x;
                            float dySpawn = muzzlePos.y - shield.position.y;
                            float dzSpawn = muzzlePos.z - shield.position.z;
                            if (dxSpawn*dxSpawn + dySpawn*dySpawn + dzSpawn*dzSpawn <= shield.radius * shield.radius) {
                                continue;
                            }
                            float sDx = shield.position.x - muzzlePos.x;
                            float sDz = shield.position.z - muzzlePos.z;
                            float sT = sDx*dir.x + sDz*dir.z;
                            if (sT > 0) {
                                float sPx = sT * dir.x;
                                float sPz = sT * dir.z;
                                float distToLineSq = (sDx - sPx)*(sDx - sPx) + (sDz - sPz)*(sDz - sPz);
                                if (distToLineSq <= shield.radius * shield.radius) {
                                    float halfChord = std::sqrt(shield.radius * shield.radius - distToLineSq);
                                    float tEnter = sT - halfChord;
                                    if (tEnter > 0 && tEnter < maxDist) {
                                        blockedByShield = true;
                                        shield.health -= bulletDamage;
                                        break;
                                    }
                                }
                            }
                        }

                        if (blockedByShield) {
                            LOG_INFO("[Hitscan] BLOCKED by shield shooter={}", id);
                            // Hit the shield, so it didn't hit the tank
                        } else if (hit.hit) {
                            auto targetIt = _tanks.find(hit.entityId);
                            if (targetIt != _tanks.end() && targetIt->second.isAlive) {
                                ev.hitTankId = hit.entityId;
                                
                                bool wasAlive = targetIt->second.isAlive;
                                targetIt->second.takeDamage(bulletDamage);
                                
                                _damageDealt[id] += bulletDamage;
                                _damageHistory[hit.entityId][id] += bulletDamage;
                                _lastCombatTime[id] = _elapsedTime;
                                _lastCombatTime[hit.entityId] = _elapsedTime;
                                
                                if (targetIt->second.isDead() && wasAlive) {
                                    LOG_INFO("[Hitscan] HIT_TANK shooter={} → victim={} KILLED (dealt {} dmg)", id, hit.entityId, bulletDamage);
                                    _kills[id]++;
                                    _deaths[hit.entityId]++;
                                    _matchScoreBase[id] += 5;
                                    
                                    float maxHp = _maxHealth[hit.entityId] > 0 ? _maxHealth[hit.entityId] : 100.0f;
                                    float assistThreshold = maxHp * 0.3f;
                                    for (auto const& [attacker, dmg] : _damageHistory[hit.entityId]) {
                                        if (attacker != id && dmg >= assistThreshold) {
                                            _matchScoreBase[attacker] += 2; // Assist
                                        }
                                    }
                                    
                                    int aliveCount = 0;
                                    for (auto const& [otherId, otherTank] : _tanks) {
                                        if (otherTank.isAlive) aliveCount++;
                                    }
                                    _survivalPlacement[hit.entityId] = aliveCount + 1;
                                } else {
                                    LOG_INFO("[Hitscan] HIT_TANK shooter={} → victim={} hp={} (dealt {} dmg) dist={:.2f}", id, hit.entityId, targetIt->second.health, bulletDamage, hit.distance);
                                }
                            } else {
                                LOG_INFO("[Hitscan] MISS (Hit static wall or dead tank) shooter={} hitEntityId={} dist={:.2f}", id, hit.entityId, hit.distance);
                            }
                        } else {
                            LOG_INFO("[Hitscan] MISS (Hit nothing) shooter={} range={:.2f}", id, tank.stats.fireRange);
                        }
                    }
                }
            } else {
                // Fallback heuristic if no exact offsets exist
                Vector3 center{ tank.position.x + cfg.offsetX * std::cos(tank.yaw) + cfg.offsetZ * std::sin(tank.yaw),
                                tank.position.y + cfg.offsetY,
                                tank.position.z - cfg.offsetX * std::sin(tank.yaw) + cfg.offsetZ * std::cos(tank.yaw) };
                Vector3 localForward = { std::sin(tank.wantsShootYaw), 0.f, std::cos(tank.wantsShootYaw) };
                Vector3 baseMuzzlePos = center + Vector3{0.f, cfg.extentY * 0.7f, 0.f} + localForward * (cfg.extentZ + 0.8f);

                float spacing = tank.stats.barrelSpacing > 0 ? tank.stats.barrelSpacing : 0.4f;
                for (int i = 0; i < count; ++i) {
                    float offset = (i - (count - 1) / 2.0f) * spacing;
                    Vector3 muzzlePos = baseMuzzlePos + localRight * offset;
                    if (cfg.weaponType == 0) {
                        spawnBullet(id, muzzlePos, tank.wantsShootYaw, tank.wantsShootForce, bulletDamage);
                    } else if (cfg.weaponType == 1) {
                        Vector3 dir = { std::sin(tank.wantsShootYaw), 0.f, std::cos(tank.wantsShootYaw) };
                        PhysicsWorld::RaycastHit hit = _physics.Raycast(muzzlePos, dir, tank.stats.fireRange, id, _map.getBulletConfig().hitRadius);
                        
                        bool blockedByShield = false;
                        float maxDist = hit.hit ? hit.distance : tank.stats.fireRange;
                        for (auto& shield : _activeShields) {
                            if (shield.ownerId == id) continue;
                            float dxSpawn = muzzlePos.x - shield.position.x;
                            float dySpawn = muzzlePos.y - shield.position.y;
                            float dzSpawn = muzzlePos.z - shield.position.z;
                            if (dxSpawn*dxSpawn + dySpawn*dySpawn + dzSpawn*dzSpawn <= shield.radius * shield.radius) {
                                continue;
                            }
                            float sDx = shield.position.x - muzzlePos.x;
                            float sDz = shield.position.z - muzzlePos.z;
                            float sT = sDx*dir.x + sDz*dir.z;
                            if (sT > 0) {
                                float sPx = sT * dir.x;
                                float sPz = sT * dir.z;
                                float distToLineSq = (sDx - sPx)*(sDx - sPx) + (sDz - sPz)*(sDz - sPz);
                                if (distToLineSq <= shield.radius * shield.radius) {
                                    float halfChord = std::sqrt(shield.radius * shield.radius - distToLineSq);
                                    float tEnter = sT - halfChord;
                                    if (tEnter > 0 && tEnter < maxDist) {
                                        blockedByShield = true;
                                        shield.health -= bulletDamage;
                                        break;
                                    }
                                }
                            }
                        }

                        if (blockedByShield) {
                            LOG_INFO("[Hitscan] BLOCKED by shield shooter={}", id);
                        } else if (hit.hit) {
                            auto targetIt = _tanks.find(hit.entityId);
                            if (targetIt != _tanks.end() && targetIt->second.isAlive) {
                                ev.hitTankId = hit.entityId;
                                
                                bool wasAlive = targetIt->second.isAlive;
                                targetIt->second.takeDamage(bulletDamage);
                                
                                _damageDealt[id] += bulletDamage;
                                _damageHistory[hit.entityId][id] += bulletDamage;
                                _lastCombatTime[id] = _elapsedTime;
                                _lastCombatTime[hit.entityId] = _elapsedTime;
                                
                                if (targetIt->second.isDead() && wasAlive) {
                                    LOG_INFO("[Hitscan] HIT_TANK shooter={} → victim={} KILLED (dealt {} dmg)", id, hit.entityId, bulletDamage);
                                    _kills[id]++;
                                    _deaths[hit.entityId]++;
                                    _matchScoreBase[id] += 5;
                                    
                                    float maxHp = _maxHealth[hit.entityId] > 0 ? _maxHealth[hit.entityId] : 100.0f;
                                    float assistThreshold = maxHp * 0.3f;
                                    for (auto const& [attacker, dmg] : _damageHistory[hit.entityId]) {
                                        if (attacker != id && dmg >= assistThreshold) {
                                            _matchScoreBase[attacker] += 2; // Assist
                                        }
                                    }
                                    
                                    int aliveCount = 0;
                                    for (auto const& [otherId, otherTank] : _tanks) {
                                        if (otherTank.isAlive) aliveCount++;
                                    }
                                    _survivalPlacement[hit.entityId] = aliveCount + 1;
                                } else {
                                    LOG_INFO("[Hitscan] HIT_TANK shooter={} → victim={} hp={} (dealt {} dmg) dist={:.2f}", id, hit.entityId, targetIt->second.health, bulletDamage, hit.distance);
                                }
                            } else {
                                LOG_INFO("[Hitscan] MISS (Hit static wall or dead tank) shooter={} hitEntityId={} dist={:.2f}", id, hit.entityId, hit.distance);
                            }
                        } else {
                            LOG_INFO("[Hitscan] MISS (Hit nothing) shooter={} range={:.2f}", id, tank.stats.fireRange);
                        }
                    }
                }
            }
            
            _shootEvents.push_back(ev);
            tank.wantsShoot = false;
        }
    }
}



void GameWorld::spawnBullet(uint32_t ownerTankId, const Vector3& pos, float yaw, float speed, int damage)
{
    Bullet b;
    b.id          = _nextBulletId++;
    b.ownerTankId = ownerTankId;
    b.damage      = damage;
    b.position    = pos;
    b.spawnPosition = pos;
    b.velocity    = Vector3{ std::sin(yaw), 0.f, std::cos(yaw) } * speed;
    b.timeToLive  = Bullet::TTL;
    b.isActive    = true;
    _bullets.push_back(b);

    SphereCollider sph;
    sph.entityId = b.id;
    sph.isActive = true;
    sph.center   = b.position;
    sph.radius   = _map.getBulletConfig().hitRadius;   // from world.json "hit_radius"
    _physics.addSphere(sph);

    LOG_INFO("[Bullet] SPAWN  id={} owner={} pos=({:.2f},{:.2f},{:.2f}) yaw={:.2f}rad spd={:.1f} ttl={:.2f}s",
             b.id, ownerTankId, pos.x, pos.y, pos.z, yaw, speed, Bullet::TTL);
}


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
        t.turretYaw = tank.turretYaw;
        t.health = static_cast<int16_t>(tank.health);
        t.flags  = tank.isAlive ? 1u : 0u;

        // Pack tank type index into bits 2-7 of flags
        uint8_t typeIndex = _map.getTankTypeIndex(tank.stats.name);
        t.flags |= (typeIndex << 2);

        float baseSpeed = _map.getTankConfig(tank.stats.name).movementSpeed;
        float mult = baseSpeed > 0.f ? (tank.stats.speed / baseSpeed) : 1.0f;
        float multClamped = mult * 100.0f;
        if (multClamped < 0.f) multClamped = 0.f;
        if (multClamped > 255.f) multClamped = 255.f;
        t.speedMultiplier = static_cast<uint8_t>(multClamped);
        
        // Calculate dynamic match score
        int score = _matchScoreBase.count(id) ? _matchScoreBase.at(id) : 0;
        int damage = _damageDealt.count(id) ? _damageDealt.at(id) : 0;
        t.score = score + (damage / 100);
        
        // Dynamic placement logic
        if (!tank.isAlive && _survivalPlacement.count(id)) {
            t.placement = _survivalPlacement.at(id); // Dead: locked placement
        } else {
            // Alive: their current dynamic placement is the number of alive players
            int aliveCount = 0;
            for (auto const& [otherId, otherTank] : _tanks) {
                if (otherTank.isAlive) aliveCount++;
            }
            t.placement = aliveCount;
        }
        
        // Anti-camp visualization (optional flag)
        float lastCombat = _lastCombatTime.count(id) ? _lastCombatTime.at(id) : 0.0f;
        if (_elapsedTime - lastCombat > 30.0f) {
            // Mark inactive (could use flag bit if desired, but for now just limits score gain optionally)
            // (Current requirement: "temporarily disable passive score gain or mark inactive")
            // We just let the UI know they are inactive if needed, but not implemented flag for it yet.
        }

        // Check Bush overlap for stealth (flags bit 1)
        if (tank.isAlive) {
            const auto& cfg = _map.getTankConfig(tank.stats.name);
            float sy = std::sin(tank.yaw), cy = std::cos(tank.yaw);
            Vector3 center{ tank.position.x + cfg.offsetX * cy + cfg.offsetZ * sy,
                            tank.position.y + cfg.offsetY,
                            tank.position.z - cfg.offsetX * sy + cfg.offsetZ * cy };
            Vector3 tmin = { center.x - cfg.extentX, center.y - cfg.extentY, center.z - cfg.extentZ };
            Vector3 tmax = { center.x + cfg.extentX, center.y + cfg.extentY, center.z + cfg.extentZ };
            
            for (const auto& b : _map.getBushes()) {
                if (tmin.x <= b.max.x && tmax.x >= b.min.x &&
                    tmin.y <= b.max.y && tmax.y >= b.min.y &&
                    tmin.z <= b.max.z && tmax.z >= b.min.z) {
                    t.flags |= 2u; // InBush
                    t.bushRegion = static_cast<uint8_t>(b.regionId);
                    break;
                }
            }
        }

        ts.push_back(t);
    }
    for (const auto& b : _bullets) {
        if (!b.isActive) continue;
        BulletState bl;
        bl.bulletId = b.id;
        bl.ownerId  = b.ownerTankId;
        bl.x = b.position.x;
        bl.y = b.position.y;
        bl.z = b.position.z;
        bs.push_back(bl);
    }

    uint16_t tc = static_cast<uint16_t>(ts.size());
    uint16_t bc = static_cast<uint16_t>(bs.size());
    uint16_t ic = static_cast<uint16_t>(_activeItems.size());
    size_t   totalBytes = 2 + tc * sizeof(TankState) + 2 + bc * sizeof(BulletState) + 2 + ic * (4 + 12);

    std::vector<uint8_t> buf(totalBytes);
    uint8_t* ptr = buf.data();

    std::memcpy(ptr, &tc, 2); ptr += 2;
    for (auto& t : ts) { std::memcpy(ptr, &t, sizeof(t)); ptr += sizeof(t); }
    std::memcpy(ptr, &bc, 2); ptr += 2;
    for (auto& bl : bs) { std::memcpy(ptr, &bl, sizeof(bl)); ptr += sizeof(bl); }
    std::memcpy(ptr, &ic, 2); ptr += 2;
    for (auto& item : _activeItems) {
        std::memcpy(ptr, &item.id, 4); ptr += 4;
        std::memcpy(ptr, &item.pos.x, 4); ptr += 4;
        std::memcpy(ptr, &item.pos.y, 4); ptr += 4;
        std::memcpy(ptr, &item.pos.z, 4); ptr += 4;
    }

    return buf;
}

// ════════════════════════════════════════════════════════════════════════════
// Default spawn positions – 10 slots spread around a circle (r=22)
// ════════════════════════════════════════════════════════════════════════════

Vector3 GameWorld::defaultSpawn(uint32_t playerId)
{
    constexpr float R     = 22.f;
    constexpr int   SLOTS = 10;
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
    result.kills  = _kills;
    result.deaths = _deaths;
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
    
    // Assign survivor placement to result structure early if match is ending
    auto assignSurvivorPlacements = [&]() {
        for (uint32_t pid : alive) {
            result.placements[pid] = alive.size();
        }
        for (uint32_t pid : playerIds) {
            if (_survivalPlacement.count(pid)) {
                result.placements[pid] = _survivalPlacement.at(pid);
            }
            int score = _matchScoreBase.count(pid) ? _matchScoreBase.at(pid) : 0;
            int damage = _damageDealt.count(pid) ? _damageDealt.at(pid) : 0;
            result.matchScores[pid] = score + (damage / 100);
            result.damageDealt[pid] = damage;
        }
    };

    // Win/Draw only make sense once at least 2 players have joined
    if (spawned >= 2) {
        if (alive.size() == 1) {
            result.outcome  = MatchOutcome::Win;
            result.winnerId = alive[0];
            assignSurvivorPlacements();
            return MatchOutcome::Win;
        }
        if (alive.empty()) {
            result.outcome = MatchOutcome::Draw;
            assignSurvivorPlacements();
            return MatchOutcome::Draw;
        }
    }

    if (elapsed >= maxDuration) {
        result.outcome = MatchOutcome::Timeout;
        // Winner by match score
        uint32_t best = 0; int bestScore = -1;
        assignSurvivorPlacements();
        for (uint32_t pid : playerIds) {
            int score = result.matchScores[pid];
            if (score > bestScore) { bestScore = score; best = pid; }
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

// Trả về chiều cao mặt đứng tại (x,z): max của heightmap và top của các walkable box.
// Non-walkable walls bị bỏ qua để tránh snap tank lên đỉnh tường.
//
// Phân biệt 2 loại walkable box:
//   Flat  (|axisX.y| <= 0.1 && |axisZ.y| <= 0.1): mặt phẳng nằm ngang (sàn cầu, mặt đất)
//         → dùng topY cố định vì toàn bộ mặt cùng một chiều cao.
//   Tilted (|axisX.y| > 0.1 || |axisZ.y| > 0.1): panel nghiêng (ramp, dốc)
//         → KHÔNG dùng topY (= đỉnh cao nhất của box, gây teleport tức thì).
//         → Dùng baked heightmap từ BakeColliderHeightmap, vốn đã raycast từng điểm
//           trên mặt nghiêng → chiều cao tăng dần theo vị trí → tank đi lên mượt.
float GameWorld::surfaceHeight(float x, float z, uint32_t* outBoxId) const
{
    float    h     = _map.GetHeightAt(x, z);  // heightmap (terrain + baked colliders)
    uint32_t boxId = 0;
    for (const auto& box : _physics._boxes) {
        if (!box.isActive || !box.isWalkable) continue;

        // Chuyển (x,z) về local space của box để kiểm tra footprint.
        // Chỉ dùng thành phần XZ của axis vì footprint là hình chiếu xuống mặt phẳng Y=const.
        float dx = x - box.center.x;
        float dz = z - box.center.z;
        float lx = dx * box.axisX.x + dz * box.axisX.z;
        float lz = dx * box.axisZ.x + dz * box.axisZ.z;
        if (std::fabs(lx) > box.extents.x) continue;
        if (std::fabs(lz) > box.extents.z) continue;

        // Kiểm tra xem box có bị nghiêng không (ramp / panel xoay).
        // axisX.y và axisZ.y khác 0 khi box xoay quanh trục ngang → mặt nghiêng.
        const bool isTilted = (std::fabs(box.axisX.y) > 0.1f || std::fabs(box.axisZ.y) > 0.1f);

        if (isTilted) {
            // Ramp: chiều cao thực tế thay đổi từng điểm → đã được bake vào heightmap.
            // Không tính topY ở đây để tránh snap tức thì lên đỉnh ramp.
            // Vẫn ghi nhận boxId để _tankOnBox tracking biết tank đang trên surface này.
            if (boxId == 0) boxId = box.entityId;
            continue;
        }

        // Flat box: topY là hằng số trên toàn mặt → dùng trực tiếp để snap tank lên sàn.
        float topY = box.center.y
            + std::fabs(box.axisX.y) * box.extents.x
            + std::fabs(box.axisY.y) * box.extents.y
            + std::fabs(box.axisZ.y) * box.extents.z;
        if (topY > h) { h = topY; boxId = box.entityId; }
    }
    if (outBoxId) *outBoxId = boxId;
    return h;
}

void GameWorld::killPlayer(uint32_t playerId) {
    auto it = _tanks.find(playerId);
    if (it == _tanks.end()) return;
    it->second.isAlive = false;
    _physics.removeDynamicBox(playerId);
}

void GameWorld::logTankPositions(uint32_t matchId) const {
    for (const auto& [id, tank] : _tanks)
        LOG_INFO("  [match {}] player {} | pos=({:.1f},{:.1f},{:.1f}) yaw={:.2f} hp={} alive={}",
                 matchId, id,
                 tank.position.x, tank.position.y, tank.position.z,
                 tank.yaw, tank.health, tank.isAlive ? "yes" : "no");
}
