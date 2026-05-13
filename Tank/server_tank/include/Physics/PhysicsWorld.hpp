#pragma once
#include <vector>
#include <unordered_map>
#include "Physics/PhysicsTypes.hpp"
#include "Physics/UniformGrid.hpp"

class PhysicsWorld {
public:
    // Static geometry loaded from JSON (walls, cliffs)
    std::vector<OBBCollider>     _boxes;
    std::vector<CapsuleCollider> _capsules;     // static capsule obstacles (cliffs)
    // Dynamic entities updated each tick
    std::vector<OBBCollider>     _dynamicBoxes; // tanks (OBB rotates with yaw)
    std::vector<SphereCollider>  _spheres;      // bullets

    // Results readable by GameWorld after Tick()
    std::vector<CollisionManifold>          _manifolds;
    std::unordered_map<uint32_t, Vector3>   _corrections; // position push-out per entityId

    // ── CRUD ──────────────────────────────────────────────────────────────────
    void addBox       (const OBBCollider&     box);
    void addCapsule   (const CapsuleCollider& cap);  // static capsule from map
    void addDynamicBox(const OBBCollider&     box);
    void addSphere    (const SphereCollider&  sphere);

    void removeBox       (uint32_t entityId);
    void removeCapsule   (uint32_t entityId);
    void removeDynamicBox(uint32_t entityId);
    void removeSphere    (uint32_t entityId);

    // Update dynamic collider transforms (call before Tick)
    void updateDynamicBox(uint32_t entityId, const Vector3& center,
                          const Vector3& axisX, const Vector3& axisY, const Vector3& axisZ);
    void updateSphere    (uint32_t entityId, const Vector3& center);

    // ── Swept sphere vs all static OBBs (bullet tunneling prevention) ─────────
    // Returns true on hit. hitFraction in [0,1] is the fraction of `delta`
    // travelled before impact; hitNormal is the surface normal at the hit.
    bool sweptSphereVsStatic(const Vector3& start, float radius,
                             const Vector3& delta,
                             float& hitFraction, Vector3& hitNormal) const;

    // ── Main physics step ─────────────────────────────────────────────────────
    void Tick() {
        DetectCollisions();
        HandleCollisions();
    }

private:
    UniformGrid _grid{ 16.f };

    // Broad-phase helpers
    void buildGrid();

    // Narrow-phase checks – return true if overlapping, fill normal+depth
    bool checkOBBvsOBB       (const OBBCollider&     a,   const OBBCollider&     b,
                               Vector3& n, float& d) const;
    bool checkCapsuleVsOBB   (const CapsuleCollider& cap, const OBBCollider&     box,
                               Vector3& n, float& d) const;
    bool checkSphereVsCapsule(const SphereCollider&  sph, const CapsuleCollider& cap,
                               Vector3& n, float& d) const;
    bool checkSphereVsOBB    (const SphereCollider&  sph, const OBBCollider&     box,
                               Vector3& n, float& d) const;

    // Math utilities
    static Vector3 closestPtOnOBB(const Vector3& pt, const OBBCollider& box);
    static void    closestPtSegSeg(const Vector3& p1, const Vector3& q1,
                                   const Vector3& p2, const Vector3& q2,
                                   float& s, float& t,
                                   Vector3& c1, Vector3& c2);
    static float   obbProject(const OBBCollider& box, const Vector3& axis);

    void DetectCollisions();
    void HandleCollisions();
};
