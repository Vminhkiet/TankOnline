#include "Physics/UniformGrid.hpp"
#include <cmath>
#include <algorithm>

UniformGrid::UniformGrid(float cellSize) : _cellSize(cellSize) {}

void UniformGrid::clear() { _cells.clear(); }

GridCell UniformGrid::toCell(float x, float y, float z) const {
    return {
        (int)std::floor(x / _cellSize),
        (int)std::floor(y / _cellSize),
        (int)std::floor(z / _cellSize)
    };
}

void UniformGrid::insert(uint32_t entityId, ColliderKind kind,
                         const Vector3& aabbMin, const Vector3& aabbMax)
{
    GridCell minC = toCell(aabbMin.x, aabbMin.y, aabbMin.z);
    GridCell maxC = toCell(aabbMax.x, aabbMax.y, aabbMax.z);

    GridEntry entry{ entityId, kind };
    for (int cx = minC.x; cx <= maxC.x; ++cx)
    for (int cy = minC.y; cy <= maxC.y; ++cy)
    for (int cz = minC.z; cz <= maxC.z; ++cz)
        _cells[{cx, cy, cz}].push_back(entry);
}

std::vector<std::pair<GridEntry, GridEntry>> UniformGrid::broadPhasePairs() const
{
    // Track unique pairs via canonical (lowerID, higherID) key
    struct U64Hash { size_t operator()(uint64_t k) const { return k * 2654435761u; } };
    std::unordered_map<uint64_t, std::pair<GridEntry,GridEntry>, U64Hash> seen;

    for (auto& [cell, entries] : _cells) {
        for (size_t i = 0; i < entries.size(); ++i) {
        for (size_t j = i + 1; j < entries.size(); ++j) {
            const GridEntry& a = entries[i];
            const GridEntry& b = entries[j];

            if (a.entityId == b.entityId) continue;
            // Skip static-static pairs (Box/Capsule vs Box/Capsule)
            bool aStatic = (a.kind == ColliderKind::Box || a.kind == ColliderKind::Capsule);
            bool bStatic = (b.kind == ColliderKind::Box || b.kind == ColliderKind::Capsule);
            if (aStatic && bStatic) continue;

            uint64_t lo = a.entityId, hi = b.entityId;
            if (lo > hi) std::swap(lo, hi);
            uint64_t key = (lo << 32) | hi;

            if (seen.count(key)) continue;

            // Canonical order: static colliders (Box/Capsule) go second
            if (aStatic)
                seen[key] = {b, a};
            else
                seen[key] = {a, b};
        }}
    }

    std::vector<std::pair<GridEntry,GridEntry>> result;
    result.reserve(seen.size());
    for (auto& [k, p] : seen) result.push_back(p);
    return result;
}
