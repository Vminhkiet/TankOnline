#pragma once
#include <unordered_map>
#include <vector>
#include <cstdint>
#include "Physics/PhysicsTypes.hpp"

// ─── Cell key ─────────────────────────────────────────────────────────────────

struct GridCell {
    int x, y, z;
    bool operator==(const GridCell& o) const {
        return x == o.x && y == o.y && z == o.z;
    }
};

struct GridCellHash {
    size_t operator()(const GridCell& c) const {
        // Large primes to minimise hash collisions
        return ((size_t)(uint32_t)c.x * 73856093u)
             ^ ((size_t)(uint32_t)c.y * 19349663u)
             ^ ((size_t)(uint32_t)c.z * 83492791u);
    }
};

// ─── Entry stored per cell ────────────────────────────────────────────────────

enum class ColliderKind : uint8_t { Box = 0, Capsule = 1, Sphere = 2 };

struct GridEntry {
    uint32_t    entityId;
    ColliderKind kind;
};

// ─── UniformGrid ─────────────────────────────────────────────────────────────

class UniformGrid {
public:
    explicit UniformGrid(float cellSize = 16.f);

    void clear();

    // Insert collider AABB into all overlapping cells
    void insert(uint32_t entityId, ColliderKind kind,
                const Vector3& aabbMin, const Vector3& aabbMax);

    // Returns unique candidate pairs for narrow-phase (no static-static pairs)
    std::vector<std::pair<GridEntry, GridEntry>> broadPhasePairs() const;

private:
    float _cellSize;
    std::unordered_map<GridCell, std::vector<GridEntry>, GridCellHash> _cells;

    GridCell toCell(float x, float y, float z) const;
};
