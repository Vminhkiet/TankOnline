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
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Player lifecycle
// ════════════════════════════════════════════════════════════════════════════

void GameWorld::addPlayer(uint32_t playerId, const Vector3& spawnPos, const TankStats& stats)
{
    _tanks.emplace(playerId, Tank(playerId, spawnPos, stats));
    const GameMap::TankConfig& cfg = _map.getTankConfig(stats.name);

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

    LOG_INFO("GameWorld: player {} spawned at ({:.1f},{:.1f},{:.1f})",
             playerId, spawnPos.x, spawnPos.y, spawnPos.z);
             
    // Initialize tracking for this player
    _maxHealth[playerId] = 100;
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

    for (auto& b : _bullets) {
        if (!b.isActive) continue;

        b.timeToLive -= deltaTime;
        if (b.timeToLive <= 0.f) {
            LOG_INFO("[Bullet] EXPIRE id={} owner={} pos=({:.2f},{:.2f},{:.2f}) — TTL exhausted (miss)",
                     b.id, b.ownerTankId, b.position.x, b.position.y, b.position.z);
            b.isActive = false;
            _physics.removeSphere(b.id);
            continue;
        }

        b.velocity.y -= BULLET_GRAVITY * deltaTime;

        Vector3 delta   = b.velocity * deltaTime;
        float   hitFrac = 1.f;
        Vector3 hitNorm;

        if (_physics.sweptSphereVsStatic(b.position, Bullet::RADIUS, delta, hitFrac, hitNorm)) {
            Vector3 hitPos = b.position + delta * hitFrac;
            LOG_INFO("[Bullet] HIT_WALL id={} owner={} hitPos=({:.2f},{:.2f},{:.2f}) norm=({:.2f},{:.2f},{:.2f})",
                     b.id, b.ownerTankId, hitPos.x, hitPos.y, hitPos.z,
                     hitNorm.x, hitNorm.y, hitNorm.z);
            b.position = hitPos;
            b.isActive = false;
            _physics.removeSphere(b.id);
        } else {
            b.position = b.position + delta;
            _physics.updateSphere(b.id, b.position);

            for (auto& [tid, tank] : _tanks) {
                if (!tank.isAlive || tid == b.ownerTankId) continue;
                const GameMap::TankConfig& tankCfg = _map.getTankConfig(tank.stats.name);
                float bulletHitX = tankCfg.extentX + _map.getBulletConfig().hitRadius;
                float bulletHitZ = tankCfg.extentZ + _map.getBulletConfig().hitRadius;
                float sy = std::sin(tank.yaw), cy = std::cos(tank.yaw);
                
                // Calculate OBB center in world space
                float centerX = tank.position.x + tankCfg.offsetX * cy + tankCfg.offsetZ * sy;
                float centerZ = tank.position.z - tankCfg.offsetX * sy + tankCfg.offsetZ * cy;
                
                float dx = b.position.x - centerX;
                float dz = b.position.z - centerZ;
                float lx =  dx * cy - dz * sy;
                float lz =  dx * sy + dz * cy;
                if (std::fabs(lx) > bulletHitX || std::fabs(lz) > bulletHitZ) continue;
                bool wasAlive = tank.isAlive;
                tank.takeDamage(b.damage);
                
                // Track damage for score and assist
                _damageDealt[b.ownerTankId] += b.damage;
                _damageHistory[tid][b.ownerTankId] += b.damage;
                
                // Update combat time for anti-camp
                _lastCombatTime[b.ownerTankId] = _elapsedTime;
                _lastCombatTime[tid] = _elapsedTime;

                if (tank.isDead() && wasAlive) {
                    LOG_INFO("[Bullet] HIT_TANK id={} owner={} → tank={} KILLED (dealt {} dmg)",
                             b.id, b.ownerTankId, tid, b.damage);
                    _kills[b.ownerTankId]++;
                    _deaths[tid]++;
                    
                    // Kill bonus
                    _matchScoreBase[b.ownerTankId] += 5;
                    
                    // Assist bonus
                    float maxHp = _maxHealth[tid] > 0 ? _maxHealth[tid] : 100.0f;
                    float assistThreshold = maxHp * 0.3f;
                    for (auto const& [attacker, dmg] : _damageHistory[tid]) {
                        if (attacker != b.ownerTankId && dmg >= assistThreshold) {
                            _matchScoreBase[attacker] += 2; // Assist!
                        }
                    }
                    
                    // Assign survival placement dynamically based on CURRENT number of alive players
                    int aliveCount = 0;
                    for (auto const& [otherId, otherTank] : _tanks) {
                        if (otherTank.isAlive) aliveCount++;
                    }
                    _survivalPlacement[tid] = aliveCount + 1; // e.g. 5 alive + 1 just died = 6th place
                } else {
                    LOG_INFO("[Bullet] HIT_TANK id={} owner={} → tank={} hp={} (dealt {} dmg)",
                             b.id, b.ownerTankId, tid, tank.health, b.damage);
                }
                b.isActive = false;
                _physics.removeSphere(b.id);
                break;
            }
        }
    }

    // Ground check — deferred from bullet move (after physics manifolds processed)
    for (auto& b : _bullets) {
        if (!b.isActive) continue;
        float groundY = surfaceHeight(b.position.x, b.position.z);
        if (b.position.y <= groundY + Bullet::RADIUS) {
            LOG_INFO("[Bullet] LAND id={} owner={} pos=({:.2f},{:.2f},{:.2f})",
                     b.id, b.ownerTankId, b.position.x, b.position.y, b.position.z);
            b.isActive = false;
            _physics.removeSphere(b.id);
        }
    }
}

void GameWorld::runPhysics(float deltaTime)
{
    constexpr float GRAVITY = 20.f;

    // Tank input integration
    for (auto& [id, tank] : _tanks)
        if (tank.isAlive) tank.update(deltaTime);

    // Apply gravity and snap to terrain
    for (auto& [id, tank] : _tanks) {
        if (!tank.isAlive) continue;
        uint32_t boxId  = 0;
        float groundY   = surfaceHeight(tank.position.x, tank.position.z, &boxId);
        uint32_t prevBox = _tankOnBox.count(id) ? _tankOnBox.at(id) : 0;

        if (boxId != prevBox) {
            if (boxId != 0)
                LOG_INFO("[Surface] tank {} entered walkable box {} (top={:.3f}) pos=({:.2f},{:.2f},{:.2f})",
                         id, boxId, groundY,
                         tank.position.x, tank.position.y, tank.position.z);
            else
                LOG_INFO("[Surface] tank {} left walkable box {} back to terrain y={:.3f}",
                         id, prevBox, groundY);
            _tankOnBox[id] = boxId;
        }

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

    // Push tank positions into physics broadphase
    syncColliders();

    // Broad-phase → narrow-phase → generate manifolds + corrections
    _physics.Tick();

    // Apply position corrections and spawn bullets from wantsShoot flags
    applyPhysicsResults(deltaTime);

    // Respawn (non-match/training mode only)
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
                LOG_INFO("GameWorld: tank {} respawned", id);
            }
        }
    }
}

void GameWorld::update(float deltaTime)
{
    _elapsedTime += deltaTime;
    updateBullets(deltaTime);
    runPhysics(deltaTime);
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
            Vector3 center{ tank.position.x + cfg.offsetX * cy + cfg.offsetZ * sy,
                            tank.position.y + cfg.offsetY,
                            tank.position.z - cfg.offsetX * sy + cfg.offsetZ * cy };
            // Muzzle is slightly in front and above center
            Vector3 baseMuzzlePos = center + Vector3{0.f, cfg.extentY * 0.7f, 0.f};
            
            // Calculate the local Right direction of the tank in world space:
            // Since forward is {sin(yaw), 0, cos(yaw)}, local right vector is {cos(yaw), 0, -sin(yaw)}.
            Vector3 localRight = { cy, 0.f, -sy };

            int bulletDamage = tank.stats.damage;
            if (tank.stats.holdsToCharge) {
                float ratio = tank.wantsShootForce / 30.0f;
                if (ratio < 0.1f) ratio = 0.1f;
                if (ratio > 1.0f) ratio = 1.0f;
                bulletDamage = static_cast<int>(bulletDamage * ratio);
            }

            int count = tank.stats.barrelCount > 0 ? tank.stats.barrelCount : 1;
            float spacing = tank.stats.barrelSpacing;

            for (int i = 0; i < count; ++i) {
                // Calculate lateral offset for multi-barrels
                float offset = (i - (count - 1) / 2.0f) * spacing;
                Vector3 muzzlePos = baseMuzzlePos + localRight * offset;
                spawnBullet(id, muzzlePos, tank.yaw, tank.wantsShootForce, bulletDamage);
            }
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
        t.health = static_cast<int16_t>(tank.health);
        t.flags  = tank.isAlive ? 1u : 0u;

        // Pack tank type index into bits 2-7 of flags
        uint8_t typeIndex = _map.getTankTypeIndex(tank.stats.name);
        t.flags |= (typeIndex << 2);
        
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
