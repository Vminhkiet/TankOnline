#pragma once
#include <string>
#include <vector>
#include "Physics/PhysicsWorld.hpp"

class GameMap {
public:
    struct SpawnPoint { int id; float x, y, z; };

    // Half-extents of the tank's box collider (matches Unity BoxCollider / 2)
    struct TankConfig {
        float extentX = 0.9f;
        float extentY = 1.0f;
        float extentZ = 1.2f;
    };

    struct BulletConfig {
        float radius    = 0.25f;  // wall / ground collision radius
        float hitRadius = 0.80f;  // tank hit-detection radius (compensates network lag)
    };

    GameMap() = default;
    ~GameMap() = default;

    bool LoadFromFile(const std::string& filepath, PhysicsWorld& physicsWorld);
    float GetHeightAt(float x, float z) const;
    const std::vector<SpawnPoint>& getSpawnPoints() const { return _spawnPoints; }
    const TankConfig&   getTankConfig()   const { return _tankConfig; }
    const BulletConfig& getBulletConfig() const { return _bulletConfig; }

private:
    struct HeightLayer {
        int   width = 0, height = 0;
        float originX = 0.f, originZ = 0.f;
        float sizeX   = 0.f, sizeZ   = 0.f;
        float baseY   = 0.f;
        std::vector<float> data;

        float sample(float x, float z) const;
        bool  covers(float x, float z) const;
    };

    std::string _mapName;
    std::vector<HeightLayer> _layers;
    std::vector<SpawnPoint>  _spawnPoints;
    TankConfig               _tankConfig;
    BulletConfig             _bulletConfig;
};