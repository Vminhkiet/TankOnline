#include "Physics/PhysicsWorld.hpp"
#include <algorithm>
#include <cmath>
#include <limits>

// ════════════════════════════════════════════════════════════════════════════
// AABB helpers for grid insertion
// ════════════════════════════════════════════════════════════════════════════

static void aabbOBB(const OBBCollider& b, Vector3& mn, Vector3& mx)
{
    // World-space AABB enclosing an oriented box
    float ex = std::fabs(b.axisX.x)*b.extents.x + std::fabs(b.axisY.x)*b.extents.y + std::fabs(b.axisZ.x)*b.extents.z;
    float ey = std::fabs(b.axisX.y)*b.extents.x + std::fabs(b.axisY.y)*b.extents.y + std::fabs(b.axisZ.y)*b.extents.z;
    float ez = std::fabs(b.axisX.z)*b.extents.x + std::fabs(b.axisY.z)*b.extents.y + std::fabs(b.axisZ.z)*b.extents.z;
    mn = { b.center.x-ex, b.center.y-ey, b.center.z-ez };
    mx = { b.center.x+ex, b.center.y+ey, b.center.z+ez };
}

static void aabbCapsule(const CapsuleCollider& c, Vector3& mn, Vector3& mx)
{
    float r = c.radius;
    mn = { std::min(c.pA.x,c.pB.x)-r, std::min(c.pA.y,c.pB.y)-r, std::min(c.pA.z,c.pB.z)-r };
    mx = { std::max(c.pA.x,c.pB.x)+r, std::max(c.pA.y,c.pB.y)+r, std::max(c.pA.z,c.pB.z)+r };
}

static void aabbSphere(const SphereCollider& s, Vector3& mn, Vector3& mx)
{
    mn = { s.center.x-s.radius, s.center.y-s.radius, s.center.z-s.radius };
    mx = { s.center.x+s.radius, s.center.y+s.radius, s.center.z+s.radius };
}

// ════════════════════════════════════════════════════════════════════════════
// CRUD
// ════════════════════════════════════════════════════════════════════════════

void PhysicsWorld::addBox    (const OBBCollider& box)    { _boxes.push_back(box); }
void PhysicsWorld::addCapsule(const CapsuleCollider& cap){ _capsules.push_back(cap); }
void PhysicsWorld::addSphere (const SphereCollider& sph) { _spheres.push_back(sph); }

template<typename Vec>
static void swapRemove(Vec& v, uint32_t id) {
    for (size_t i = 0; i < v.size(); ++i) {
        if (v[i].entityId == id) { v[i] = v.back(); v.pop_back(); return; }
    }
}

void PhysicsWorld::removeBox    (uint32_t id) { swapRemove(_boxes,    id); }
void PhysicsWorld::removeCapsule(uint32_t id) { swapRemove(_capsules, id); }
void PhysicsWorld::removeSphere (uint32_t id) { swapRemove(_spheres,  id); }

void PhysicsWorld::updateCapsule(uint32_t id, const Vector3& pA, const Vector3& pB) {
    for (auto& c : _capsules) if (c.entityId == id) { c.pA = pA; c.pB = pB; return; }
}

void PhysicsWorld::updateSphere(uint32_t id, const Vector3& center) {
    for (auto& s : _spheres) if (s.entityId == id) { s.center = center; return; }
}

// ════════════════════════════════════════════════════════════════════════════
// Math utilities
// ════════════════════════════════════════════════════════════════════════════

float PhysicsWorld::obbProject(const OBBCollider& box, const Vector3& axis)
{
    return box.extents.x * box.axisX.absDot(axis)
         + box.extents.y * box.axisY.absDot(axis)
         + box.extents.z * box.axisZ.absDot(axis);
}

Vector3 PhysicsWorld::closestPtOnOBB(const Vector3& pt, const OBBCollider& box)
{
    Vector3 d      = pt - box.center;
    Vector3 result = box.center;

    const Vector3* axes[3] = { &box.axisX, &box.axisY, &box.axisZ };
    const float    exts[3] = { box.extents.x, box.extents.y, box.extents.z };

    for (int i = 0; i < 3; ++i) {
        float dist = d.dot(*axes[i]);
        dist  = std::max(-exts[i], std::min(exts[i], dist));
        result = result + *axes[i] * dist;
    }
    return result;
}

void PhysicsWorld::closestPtSegSeg(
    const Vector3& p1, const Vector3& q1,
    const Vector3& p2, const Vector3& q2,
    float& s, float& t, Vector3& c1, Vector3& c2)
{
    // Ericson, "Real-Time Collision Detection" §5.1.9
    Vector3 d1 = q1 - p1;
    Vector3 d2 = q2 - p2;
    Vector3 r  = p1 - p2;

    float a = d1.dot(d1), e = d2.dot(d2), f = d2.dot(r);
    const float EPS = 1e-8f;

    if (a <= EPS && e <= EPS) { s = t = 0.f; c1 = p1; c2 = p2; return; }

    if (a <= EPS) {
        s = 0.f;
        t = std::max(0.f, std::min(1.f, f / e));
    } else {
        float c = d1.dot(r);
        if (e <= EPS) {
            t = 0.f;
            s = std::max(0.f, std::min(1.f, -c / a));
        } else {
            float b     = d1.dot(d2);
            float denom = a*e - b*b;
            s = (std::fabs(denom) > EPS)
                ? std::max(0.f, std::min(1.f, (b*f - c*e) / denom))
                : 0.f;
            t = (b*s + f) / e;
            if (t < 0.f) {
                t = 0.f; s = std::max(0.f, std::min(1.f, -c / a));
            } else if (t > 1.f) {
                t = 1.f; s = std::max(0.f, std::min(1.f, (b - c) / a));
            }
        }
    }
    c1 = p1 + d1 * s;
    c2 = p2 + d2 * t;
}

// ════════════════════════════════════════════════════════════════════════════
// Narrow-phase: OBB vs OBB (SAT – 15 axes)
// ════════════════════════════════════════════════════════════════════════════

bool PhysicsWorld::checkOBBvsOBB(const OBBCollider& A, const OBBCollider& B,
                                  Vector3& normal, float& depth) const
{
    Vector3 T = B.center - A.center;

    const Vector3* aA[3] = { &A.axisX, &A.axisY, &A.axisZ };
    const Vector3* aB[3] = { &B.axisX, &B.axisY, &B.axisZ };

    depth = std::numeric_limits<float>::max();

    // Test one candidate separating axis; returns false if separating
    auto test = [&](const Vector3& raw) -> bool {
        float lenSq = raw.lengthSq();
        if (lenSq < 1e-8f) return true; // parallel cross-product, skip
        Vector3 ax = raw * (1.f / std::sqrt(lenSq));

        float pA = obbProject(A, ax);
        float pB = obbProject(B, ax);
        float dist    = std::fabs(T.dot(ax));
        float overlap = pA + pB - dist;
        if (overlap <= 0.f) return false;
        if (overlap < depth) {
            depth  = overlap;
            normal = (T.dot(ax) < 0.f) ? -ax : ax;
        }
        return true;
    };

    for (int i = 0; i < 3; ++i) if (!test(*aA[i])) return false;
    for (int i = 0; i < 3; ++i) if (!test(*aB[i])) return false;
    for (int i = 0; i < 3; ++i)
    for (int j = 0; j < 3; ++j)
        if (!test(aA[i]->cross(*aB[j]))) return false;

    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Narrow-phase: Capsule vs OBB
// ════════════════════════════════════════════════════════════════════════════

bool PhysicsWorld::checkCapsuleVsOBB(const CapsuleCollider& cap, const OBBCollider& box,
                                      Vector3& normal, float& depth) const
{
    // Find closest point on segment to box center, then clamp to get OBB contact
    Vector3 seg = cap.pB - cap.pA;
    float   len = seg.lengthSq();

    float t = (len > 1e-8f)
        ? std::max(0.f, std::min(1.f, (box.center - cap.pA).dot(seg) / len))
        : 0.f;

    Vector3 segPt  = cap.pA + seg * t;
    Vector3 obbPt  = closestPtOnOBB(segPt, box);
    Vector3 diff   = segPt - obbPt;
    float   distSq = diff.lengthSq();

    if (distSq >= cap.radius * cap.radius) return false;

    float dist = std::sqrt(distSq);
    depth  = cap.radius - dist;
    normal = (dist < 1e-8f) ? Vector3{0.f,1.f,0.f} : diff * (1.f / dist);
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Narrow-phase: Sphere vs Capsule (bullet vs tank)
// ════════════════════════════════════════════════════════════════════════════

bool PhysicsWorld::checkSphereVsCapsule(const SphereCollider& sph,
                                         const CapsuleCollider& cap,
                                         Vector3& normal, float& depth) const
{
    Vector3 seg  = cap.pB - cap.pA;
    float   lenSq = seg.lengthSq();
    float   t     = (lenSq > 1e-8f)
        ? std::max(0.f, std::min(1.f, (sph.center - cap.pA).dot(seg) / lenSq))
        : 0.f;

    Vector3 closest = cap.pA + seg * t;
    Vector3 diff    = sph.center - closest;
    float   totalR  = sph.radius + cap.radius;
    float   distSq  = diff.lengthSq();

    if (distSq >= totalR * totalR) return false;

    float dist = std::sqrt(distSq);
    depth  = totalR - dist;
    normal = (dist < 1e-8f) ? Vector3{0.f,1.f,0.f} : diff * (1.f / dist);
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Narrow-phase: Sphere vs OBB (bullet vs wall)
// ════════════════════════════════════════════════════════════════════════════

bool PhysicsWorld::checkSphereVsOBB(const SphereCollider& sph, const OBBCollider& box,
                                     Vector3& normal, float& depth) const
{
    Vector3 closest = closestPtOnOBB(sph.center, box);
    Vector3 diff    = sph.center - closest;
    float   distSq  = diff.lengthSq();

    if (distSq >= sph.radius * sph.radius) return false;

    float dist = std::sqrt(distSq);
    depth  = sph.radius - dist;
    normal = (dist < 1e-8f) ? Vector3{0.f,1.f,0.f} : diff * (1.f / dist);
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// AABB Swept sphere vs static OBBs  (bullet tunneling prevention)
// Expands each OBB by sphere radius (Minkowski), then ray-slab test.
// ════════════════════════════════════════════════════════════════════════════

bool PhysicsWorld::sweptSphereVsStatic(const Vector3& start, float radius,
                                        const Vector3& delta,
                                        float& hitFraction, Vector3& hitNormal) const
{
    hitFraction = 1.f;
    bool hit    = false;

    for (const auto& box : _boxes) {
        if (!box.isActive) continue;

        // Expand extents by sphere radius (Minkowski sum)
        Vector3 ext = box.extents + Vector3{radius, radius, radius};

        // Transform ray into OBB local space
        Vector3 rayOrig = start - box.center;
        float   ox = rayOrig.dot(box.axisX), oy = rayOrig.dot(box.axisY), oz = rayOrig.dot(box.axisZ);
        float   dx = delta.dot(box.axisX),   dy = delta.dot(box.axisY),   dz = delta.dot(box.axisZ);

        float tMin = 0.f, tMax = 1.f;
        Vector3 candidateN;

        auto slab = [&](float o, float d, float e, const Vector3& axis) -> bool {
            if (std::fabs(d) < 1e-8f) return std::fabs(o) <= e; // parallel
            float t1 = (-e - o) / d, t2 = (e - o) / d;
            Vector3 n = axis;
            if (t1 > t2) { std::swap(t1, t2); n = -n; }
            if (t1 > tMin) { tMin = t1; candidateN = n; }
            tMax = std::min(tMax, t2);
            return tMin <= tMax;
        };

        if (!slab(ox, dx, ext.x, box.axisX)) continue;
        if (!slab(oy, dy, ext.y, box.axisY)) continue;
        if (!slab(oz, dz, ext.z, box.axisZ)) continue;
        if (tMin < 0.f) continue; // behind start

        if (tMin < hitFraction) {
            hitFraction = tMin;
            hitNormal   = candidateN;
            hit         = true;
        }
    }
    return hit;
}

// ════════════════════════════════════════════════════════════════════════════
// Build grid (broad phase)
// ════════════════════════════════════════════════════════════════════════════

void PhysicsWorld::buildGrid()
{
    _grid.clear();
    Vector3 mn, mx;

    for (auto& b : _boxes)    { if (b.isActive)  { aabbOBB(b, mn, mx);     _grid.insert(b.entityId, ColliderKind::Box,     mn, mx); } }
    for (auto& c : _capsules) { if (c.isActive)  { aabbCapsule(c, mn, mx); _grid.insert(c.entityId, ColliderKind::Capsule, mn, mx); } }
    for (auto& s : _spheres)  { if (s.isActive)  { aabbSphere(s, mn, mx);  _grid.insert(s.entityId, ColliderKind::Sphere,  mn, mx); } }
}

// ════════════════════════════════════════════════════════════════════════════
// DetectCollisions
// ════════════════════════════════════════════════════════════════════════════

void PhysicsWorld::DetectCollisions()
{
    _manifolds.clear();
    _corrections.clear();

    buildGrid();

    auto pairs = _grid.broadPhasePairs();

    // Fast lookup helpers (linear scan – small vectors in practice)
    auto findCap = [&](uint32_t id) -> CapsuleCollider* {
        for (auto& c : _capsules) if (c.entityId == id && c.isActive) return &c;
        return nullptr;
    };
    auto findSph = [&](uint32_t id) -> SphereCollider* {
        for (auto& s : _spheres)  if (s.entityId == id && s.isActive) return &s;
        return nullptr;
    };
    auto findBox = [&](uint32_t id) -> OBBCollider* {
        for (auto& b : _boxes)    if (b.entityId == id && b.isActive) return &b;
        return nullptr;
    };

    for (auto& [entA, entB] : pairs) {
        Vector3 normal;
        float   depth     = 0.f;
        bool    colliding = false;
        CollisionType ctype = CollisionType::TANK_VS_WALL;

        auto ka = entA.kind, kb = entB.kind;
        uint32_t ia = entA.entityId, ib = entB.entityId;

        if (ka == ColliderKind::Capsule && kb == ColliderKind::Box) {
            auto* cap = findCap(ia); auto* box = findBox(ib);
            if (cap && box) { colliding = checkCapsuleVsOBB(*cap, *box, normal, depth); ctype = CollisionType::TANK_VS_WALL; }
        }
        else if (ka == ColliderKind::Capsule && kb == ColliderKind::Capsule) {
            auto* cA = findCap(ia); auto* cB = findCap(ib);
            if (cA && cB) {
                float s, t; Vector3 c1, c2;
                closestPtSegSeg(cA->pA, cA->pB, cB->pA, cB->pB, s, t, c1, c2);
                Vector3 diff = c1 - c2;
                float   totR = cA->radius + cB->radius;
                float   dsq  = diff.lengthSq();
                if (dsq < totR * totR) {
                    float dist = std::sqrt(dsq);
                    depth  = totR - dist;
                    normal = (dist < 1e-8f) ? Vector3{0,1,0} : diff * (1.f/dist);
                    colliding = true;
                    ctype = CollisionType::TANK_VS_TANK;
                }
            }
        }
        else if (ka == ColliderKind::Sphere && kb == ColliderKind::Capsule) {
            auto* sph = findSph(ia); auto* cap = findCap(ib);
            if (sph && cap) { colliding = checkSphereVsCapsule(*sph, *cap, normal, depth); ctype = CollisionType::BULLET_VS_TANK; }
        }
        else if (ka == ColliderKind::Sphere && kb == ColliderKind::Box) {
            auto* sph = findSph(ia); auto* box = findBox(ib);
            if (sph && box) { colliding = checkSphereVsOBB(*sph, *box, normal, depth); ctype = CollisionType::BULLET_VS_WALL; }
        }

        if (colliding) {
            _manifolds.push_back({ ia, ib, normal, depth, ctype });
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// HandleCollisions – position corrections (game events handled by GameWorld)
// ════════════════════════════════════════════════════════════════════════════

void PhysicsWorld::HandleCollisions()
{
    for (const auto& m : _manifolds) {
        Vector3 push = m.normal * m.penetrationDepth;

        switch (m.type) {
        case CollisionType::TANK_VS_WALL:
            _corrections[m.entityA] += push;
            break;
        case CollisionType::TANK_VS_TANK:
            _corrections[m.entityA] += push * 0.5f;
            _corrections[m.entityB] -= push * 0.5f;
            break;
        case CollisionType::BULLET_VS_TANK:
        case CollisionType::BULLET_VS_WALL:
            // Gameplay events consumed by GameWorld via _manifolds
            break;
        }
    }
}
