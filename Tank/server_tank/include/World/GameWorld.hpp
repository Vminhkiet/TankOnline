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

    void addPlayer   (uint32_t playerId, const Vector3& spawnPos, const TankStats& stats = TankStats{});
    void removePlayer(uint32_t playerId);

    void processInput(uint32_t playerId, const ClientInput& input);
    void update(float deltaTime);           // calls updateBullets + runPhysics in order
    void updateBullets(float deltaTime);    // bullet move + CCD wall hit + tank hit
    void runPhysics(float deltaTime);       // tank movement, gravity, PhysicsWorld::Tick, corrections

    // Check win/timeout condition. Returns Running while game is still active.
    MatchOutcome checkOutcome(float elapsed, float maxDuration,
                              const std::vector<uint32_t>& playerIds,
                              MatchResult& outResult) const;

    std::vector<uint8_t> getSnapshot() const;
    std::vector<EventShootPacket> getShootEvents();

    size_t playerCount()      const { return _tanks.size(); }
    size_t activeBulletCount() const {
        size_t n = 0;
        for (const auto& b : _bullets) if (b.isActive) ++n;
        return n;
    }
    const std::unordered_map<uint32_t, int>& getKills()  const { return _kills; }
    const std::unordered_map<uint32_t, int>& getDeaths() const { return _deaths; }
    void logTankPositions(uint32_t matchId) const;
    void killPlayer(uint32_t playerId);   // mark disconnected player as dead

    // Returns map spawn point for the given slot index, or default circle if unavailable
    Vector3 getSpawnPosition(size_t slotIndex) const;

    // Call once to disable auto-respawn (match mode: dead = eliminated)
    void disableRespawn() { _respawnOnDeath = false; }

    const GameMap& getMap() const { return _map; }

private:
    std::unordered_map<uint32_t, Tank>   _tanks;
    std::vector<Bullet>                   _bullets;
    PhysicsWorld                          _physics;
    GameMap                               _map;
    std::unordered_map<uint32_t, int>     _kills;  // ownerTankId → kills this match
    std::unordered_map<uint32_t, int>     _deaths; // victimTankId → deaths this match
    std::unordered_map<uint32_t, int>     _damageDealt;
    std::unordered_map<uint32_t, std::unordered_map<uint32_t, int>> _damageHistory; // victimId -> attackerId -> damage
    std::unordered_map<uint32_t, float>   _lastCombatTime; // playerId -> elapsed time
    std::unordered_map<uint32_t, int>     _matchScoreBase; // playerId -> score
    std::unordered_map<uint32_t, int>     _survivalPlacement; // playerId -> placement (1=first, 2=second)
    std::unordered_map<uint32_t, int>     _maxHealth; // playerId -> max health
    std::vector<EventShootPacket>         _shootEvents;
    uint32_t                              _nextBulletId = 50000;
    bool                                  _respawnOnDeath = true;
    float                                 _elapsedTime = 0.0f;

    void syncColliders();
    void applyPhysicsResults(float deltaTime);
    void spawnBullet(uint32_t ownerTankId, const Vector3& pos, float yaw, float speed, int damage);
    // Returns height + id of walkable box contributing (0 = terrain only)
    float surfaceHeight(float x, float z, uint32_t* outBoxId = nullptr) const;
    static Vector3 defaultSpawn(uint32_t playerId);

    // Track which tanks are on a walkable box — for entry/exit log
    std::unordered_map<uint32_t, uint32_t> _tankOnBox; // tankId → boxEntityId (0 = terrain)
};
