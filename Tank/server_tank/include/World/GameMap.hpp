#pragma once
#include <string>
#include <vector>
#include <unordered_map>
#include "Physics/PhysicsWorld.hpp"

class GameMap {
public:
    struct SpawnPoint { int id; float x, y, z; };

    struct Bush {
        Vector3 min;
        Vector3 max;
        uint32_t regionId = 0;
    };

    // Half-extents of the tank's box collider (matches Unity BoxCollider / 2)
    struct TankConfig {
        float extentX = 0.9f;
        float extentY = 1.0f;
        float extentZ = 1.2f;
        float offsetX = 0.0f;
        float offsetY = 1.0f;
        float offsetZ = 0.0f;
        int weaponType = 0; // 0 = Projectile, 1 = Hitscan
        Vector3 turretOffset; // Offset of turret relative to root
        std::vector<Vector3> barrelOffsets; // Local to turret

        // Real gameplay stats (loaded from world.json, exported by MapExporter)
        float maxHealth = 100.f;
        float movementSpeed = 12.f;
        float fireRate = 1.0f;
        float damage = 20.f;
        float fireRange = 50.f;
        int magazineCapacity = 1;
        float reloadTime = 2.0f;
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
    const std::vector<Bush>& getBushes() const { return _bushes; }
    const TankConfig   getTankConfig(const std::string& tankName) const { 
        auto it = _tankConfigs.find(tankName);
        if (it != _tankConfigs.end()) {
            return it->second;
        }
        // Fallback to default or first available
        if (!_tankConfigs.empty()) {
            return _tankConfigs.begin()->second;
        }
        return TankConfig{};
    }
    const BulletConfig& getBulletConfig() const { return _bulletConfig; }

    uint8_t getTankTypeIndex(const std::string& name) const {
        auto it = _tankNameToIndex.find(name);
        if (it != _tankNameToIndex.end()) {
            return it->second;
        }
        // Case-insensitive fallback
        std::string lowercaseName = name;
        for (auto& c : lowercaseName) c = std::tolower(c);
        for (const auto& [k, v] : _tankNameToIndex) {
            std::string kLower = k;
            for (auto& c : kLower) c = std::tolower(c);
            if (kLower == lowercaseName) return v;
        }
        return 0; // Fallback to index 0
    }

    std::string getTankNameByIndex(uint8_t index) const {
        for (const auto& [name, idx] : _tankNameToIndex) {
            if (idx == index) return name;
        }
        return "BULLDOG";
    }

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
    std::vector<Bush>        _bushes;
    std::unordered_map<std::string, TankConfig> _tankConfigs;
    std::unordered_map<std::string, uint8_t>    _tankNameToIndex;
    BulletConfig             _bulletConfig;
};