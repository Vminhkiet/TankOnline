#pragma once
#include <unordered_map>
#include <vector>
#include <cstdint>
#include "Entities/Tank.hpp"
#include "Entities/Bullet.hpp"
#include "Physics/PhysicsWorld.hpp"
#include "World/GameMap.hpp"
#include "Network/Packets.hpp"
#include "Core/MatchConfig.hpp"

class GameWorld {
public:
    GameWorld();

    bool loadMap(const std::string& mapPath);

    void addPlayer   (uint32_t playerId, const Vector3& spawnPos);
    void removePlayer(uint32_t playerId);

    void processInput(uint32_t playerId, const ClientInput& input);
    void update(float deltaTime);

    // Check win/timeout condition. Returns Running while game is still active.
    MatchOutcome checkOutcome(float elapsed, float maxDuration,
                              const std::vector<uint32_t>& playerIds,
                              MatchResult& outResult) const;

    std::vector<uint8_t> getSnapshot() const;

    size_t playerCount() const { return _tanks.size(); }
    const std::unordered_map<uint32_t, int>& getKills() const { return _kills; }
    void logTankPositions(uint32_t matchId) const;
    void killPlayer(uint32_t playerId);   // mark disconnected player as dead

    // Returns map spawn point for the given slot index, or default circle if unavailable
    Vector3 getSpawnPosition(size_t slotIndex) const;

    // Call once to disable auto-respawn (match mode: dead = eliminated)
    void disableRespawn() { _respawnOnDeath = false; }

private:
    std::unordered_map<uint32_t, Tank>   _tanks;
    std::vector<Bullet>                   _bullets;
    PhysicsWorld                          _physics;
    GameMap                               _map;
    std::unordered_map<uint32_t, int>     _kills;  // ownerTankId → kills this match
    uint32_t                              _nextBulletId = 50000;
    bool                                  _respawnOnDeath = true;

    void syncColliders();
    void applyPhysicsResults(float deltaTime);
    void spawnBullet(uint32_t ownerTankId, const Vector3& pos, float yaw);
    static Vector3 defaultSpawn(uint32_t playerId);
};
