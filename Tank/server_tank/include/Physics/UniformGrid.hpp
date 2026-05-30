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

enum class ColliderKind : uint8_t { Box = 0, Capsule = 1, Sphere = 2, DynamicBox = 3 };

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

    // Fills _pairsResult with unique candidate pairs. Reuses pre-allocated memory
    // across ticks — avoids per-tick heap allocation that causes P99 latency spikes.
    const std::vector<std::pair<GridEntry, GridEntry>>& broadPhasePairs();

private:
    float _cellSize;
    std::unordered_map<GridCell, std::vector<GridEntry>, GridCellHash> _cells;

    // Persistent (pre-allocated) buffers — cleared not freed each tick
    struct U64Hash { size_t operator()(uint64_t k) const { return k * 2654435761u; } };
    std::unordered_map<uint64_t, std::pair<GridEntry,GridEntry>, U64Hash> _seen;
    std::vector<std::pair<GridEntry, GridEntry>> _pairsResult;

    GridCell toCell(float x, float y, float z) const;
};
