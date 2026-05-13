#pragma once
#include <cstdint>
#include <cmath>

struct Vector3 {
    float x = 0.f, y = 0.f, z = 0.f;

    Vector3() = default;
    Vector3(float x, float y, float z) : x(x), y(y), z(z) {}

    Vector3 operator+(const Vector3& o) const { return {x+o.x, y+o.y, z+o.z}; }
    Vector3 operator-(const Vector3& o) const { return {x-o.x, y-o.y, z-o.z}; }
    Vector3 operator*(float s)          const { return {x*s,   y*s,   z*s  }; }
    Vector3 operator/(float s)          const { return {x/s,   y/s,   z/s  }; }
    Vector3 operator-()                 const { return {-x,    -y,    -z   }; }
    Vector3& operator+=(const Vector3& o) { x+=o.x; y+=o.y; z+=o.z; return *this; }
    Vector3& operator-=(const Vector3& o) { x-=o.x; y-=o.y; z-=o.z; return *this; }
    Vector3& operator*=(float s)          { x*=s;   y*=s;   z*=s;   return *this; }

    float dot(const Vector3& o)   const { return x*o.x + y*o.y + z*o.z; }
    float absDot(const Vector3& o)const { return std::fabs(dot(o)); }

    Vector3 cross(const Vector3& o) const {
        return { y*o.z - z*o.y,
                 z*o.x - x*o.z,
                 x*o.y - y*o.x };
    }

    float lengthSq() const { return x*x + y*y + z*z; }
    float length()   const { return std::sqrt(lengthSq()); }

    Vector3 normalized() const {
        float len = length();
        return (len < 1e-8f) ? Vector3{} : *this * (1.f / len);
    }
};

inline Vector3 operator*(float s, const Vector3& v) { return v * s; }

// ─── Colliders ────────────────────────────────────────────────────────────────

struct OBBCollider {
    uint32_t entityId  = 0;
    bool     isActive  = false;
    bool     isStatic  = true;
    bool     isWalkable = false; // true = mặt phẳng đi được (bridge/floor), bỏ qua wall collision

    Vector3 center;
    Vector3 extents;                  // half-sizes
    Vector3 axisX = {1.f, 0.f, 0.f}; // local X in world space
    Vector3 axisY = {0.f, 1.f, 0.f};
    Vector3 axisZ = {0.f, 0.f, 1.f};
};

struct CapsuleCollider {
    uint32_t entityId = 0;
    bool     isActive = false;

    Vector3 pA;      // bottom sphere center
    Vector3 pB;      // top sphere center
    float   radius = 1.f;
};

struct SphereCollider {
    uint32_t entityId = 0;
    bool     isActive = false;

    Vector3 center;
    float   radius = 0.5f;
};

// ─── Collision result ─────────────────────────────────────────────────────────

enum class CollisionType {
    TANK_VS_WALL,
    TANK_VS_TANK,
    BULLET_VS_TANK,
    BULLET_VS_WALL
};

struct CollisionManifold {
    uint32_t      entityA;
    uint32_t      entityB;
    Vector3       normal;           // points from B toward A (push A out)
    float         penetrationDepth;
    CollisionType type;
};
