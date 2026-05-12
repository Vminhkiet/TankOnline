# Physics System – Tank 3D Server

> Tham chiếu: `server_tank/include/Physics/`, `server_tank/src/Physics/`

---

## Tổng quan

```
PhysicsWorld::Tick()
 ├─ buildGrid()          ← Uniform Grid (broad-phase)
 ├─ broadPhasePairs()    ← loại bỏ cặp không cần kiểm tra
 ├─ DetectCollisions()   ← narrow-phase (SAT, Capsule, Sphere)
 └─ HandleCollisions()   ← tính correction vector & ghi manifold
```

GameWorld đọc kết quả sau Tick():
- `_corrections[entityId]` → cộng vào tank.position
- `_manifolds[]` → xử lý game event (damage, destroy bullet)

---

## Collider Types

| Collider | Entity | Struct | Dùng cho |
|----------|--------|--------|---------|
| `OBBCollider` | Static wall/terrain | center, extents, axisX/Y/Z | Tường, vách từ JSON |
| `CapsuleCollider` | Tank (dynamic) | pA (bottom), pB (top), radius | Thân xe tăng |
| `SphereCollider` | Bullet (dynamic) | center, radius=0.25 | Đạn |

### Tank Capsule constants
```cpp
CAPSULE_RADIUS = 1.0f   // bán kính nửa chiều rộng
CAPSULE_HEIGHT = 2.2f   // chiều cao tổng
// pA = position + (0, CAPSULE_RADIUS, 0)          (tâm cầu đáy)
// pB = position + (0, HEIGHT - CAPSULE_RADIUS, 0) (tâm cầu đỉnh)
```

---

## Broad-Phase: Uniform Grid

**File:** `UniformGrid.hpp/cpp`

Cell size = **16 units**. Mỗi collider convert sang AABB rồi insert vào tất cả cells giao nhau.

```
worldToCell(x,y,z) = { floor(x/16), floor(y/16), floor(z/16) }
```

Hash: `x*73856093 ^ y*19349663 ^ z*83492791` (large prime XOR)

**Lọc trước khi narrow-phase:**
- Box vs Box → **skip** (static-static không bao giờ va chạm)
- Canonical order: Box luôn là `entB` trong pair, giúp narrow-phase đồng nhất

```
broadPhasePairs() → unique (entA, entB) pairs trong cùng cell
```

---

## Narrow-Phase Algorithms

### 1. OBB vs OBB — SAT 15 axes

**Dùng cho:** Tank vs Tank (2 capsule → check extremes qua OBB) *(hiện chưa dùng trực tiếp, reserved)*

```
Axes tested (15 tổng):
  A.axisX, A.axisY, A.axisZ        (3 trục của OBB A)
  B.axisX, B.axisY, B.axisZ        (3 trục của OBB B)
  A.axisX×B.axisX, ..., A.axisZ×B.axisZ  (9 cross-products)

Nếu projection overlap <= 0 ở BẤT KỲ trục nào → không va chạm
Trục có overlap nhỏ nhất = collision normal & depth
```

```cpp
float projectOnAxis(OBB box, axis) {
    return extents.x * |axisX·axis|
         + extents.y * |axisY·axis|
         + extents.z * |axisZ·axis|;
}
```

---

### 2. Capsule vs OBB — Closest-point

**Dùng cho:** Tank (capsule) vs Wall (OBB) — `TANK_VS_WALL`

```
1. t = clamp( dot(boxCenter - pA, seg) / |seg|², 0, 1 )
2. segPt = pA + seg * t          ← điểm gần nhất trên segment
3. obbPt = closestPtOnOBB(segPt) ← điểm gần nhất trên OBB surface
4. diff = segPt - obbPt
5. va chạm khi |diff| < radius
   depth  = radius - |diff|
   normal = diff / |diff|        ← đẩy capsule ra khỏi OBB
```

`closestPtOnOBB` dùng project-and-clamp lên 3 local axes:
```cpp
for each axis i:
    dist = dot(pt - center, axis[i])
    dist = clamp(dist, -extents[i], +extents[i])
    result += axis[i] * dist
```

---

### 3. Sphere vs Capsule — Closest point on segment

**Dùng cho:** Bullet (sphere) vs Tank (capsule) — `BULLET_VS_TANK`

```
t = clamp( dot(sphereCenter - pA, seg) / |seg|², 0, 1 )
closest = pA + seg * t
dist = |sphereCenter - closest|
va chạm khi dist < sphere.radius + capsule.radius
```

---

### 4. Sphere vs OBB — Direct closest point

**Dùng cho:** Bullet (sphere) vs Wall (OBB) — `BULLET_VS_WALL`

```
closest = closestPtOnOBB(sphere.center, box)
dist = |sphere.center - closest|
va chạm khi dist < sphere.radius
```

---

### 5. Capsule vs Capsule — Segment-Segment distance

**Dùng cho:** Tank vs Tank — `TANK_VS_TANK`

Dùng Ericson §5.1.9 (Real-Time Collision Detection):
```
closestPtSegSeg(pA1,pB1, pA2,pB2) → c1, c2
dist = |c1 - c2|
va chạm khi dist < r1 + r2
normal = (c1 - c2) / dist    ← đẩy 2 xe ra xa nhau
depth  = (r1+r2) - dist
correction A += normal * depth * 0.5
correction B -= normal * depth * 0.5
```

---

## AABB Swept Sphere — Anti-Tunneling

**Dùng cho:** Bullet di chuyển nhanh (60 units/s) có thể xuyên qua tường mỏng nếu dùng discrete check.

**File:** `PhysicsWorld::sweptSphereVsStatic()`

Thuật toán: Minkowski Expansion + Ray-Slab (AABB slab method trong OBB local space)

```
Với mỗi static OBB:
  1. Expand OBB extents += bullet.radius   (Minkowski sum)
  2. Transform ray vào local space của OBB:
       rayOrig = (bulletPos - obb.center) projected onto axes
       dir     = delta projected onto axes
  3. Slab test trên 3 trục (local X, Y, Z):
       t1 = (-e - o) / d,  t2 = (+e - o) / d
       tMin = max(all t1),  tMax = min(all t2)
  4. Hit khi tMin <= tMax và tMin in [0, 1]
  5. Giữ hit nhỏ nhất → bullet dừng tại position + delta * hitFraction
```

---

## Collision Types & Response

| Type | entityA | entityB | Response |
|------|---------|---------|---------|
| `TANK_VS_WALL` | Tank ID | Static OBB ID | `_corrections[A] += normal * depth` |
| `TANK_VS_TANK` | Tank A ID | Tank B ID | `_corrections[A] += push*0.5`, `_corrections[B] -= push*0.5` |
| `BULLET_VS_TANK` | Bullet ID | Tank ID | Đọc bởi GameWorld → `takeDamage(25)`, bullet deactivated |
| `BULLET_VS_WALL` | Bullet ID | OBB ID | Đọc bởi GameWorld → bullet deactivated |

---

## World.json → Physics Loading

**File:** `GameMap::LoadFromFile()` → `server_tank/src/World/GameMap.cpp`

```json
{
  "colliders": [
    {
      "type": "box",
      "center": {"x":0, "y":0, "z":0},
      "rotation": {"x":0, "y":45, "z":0},   // Euler degrees → quaternion → OBB axes
      "size": {"x":10, "y":3, "z":2}         // → extents = size * 0.5
    },
    {
      "type": "capsule",
      "center": {"x":-5.4, "y":3.9, "z":-48.9},
      "radius": 4.67,
      "height": 18.6
    }
  ],
  "heightmaps": [{
    "resolutionX": N,
    "resolutionZ": M,
    "heights": [...]   // N*M floats
  }]
}
```

Kết quả load `world.json`:
- **82 OBBCollider** (static boxes, entityId 10000+)
- **91 CapsuleCollider** (static cliffs, entityId tiếp theo)

`GetHeightAt(x, z)` → bilinear interpolation trên heightmap grid.

---

## Entity ID Ranges

| Range | Loại |
|-------|------|
| 1 – 49999 | Dynamic entities (players, ID = playerID) |
| 50000+ | Bullets (`_nextBulletId` bắt đầu từ 50000) |
| 10000 – ~10173 | Static colliders từ JSON map |
