#pragma once
#include <string>
#include <vector>
#include "Physics/PhysicsWorld.hpp"

class GameMap {
public:
    struct SpawnPoint { int id; float x, y, z; };

    GameMap() = default;
    ~GameMap() = default;

    bool LoadFromFile(const std::string& filepath, PhysicsWorld& physicsWorld);
    float GetHeightAt(float x, float z) const;
    const std::vector<SpawnPoint>& getSpawnPoints() const { return _spawnPoints; }

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
};