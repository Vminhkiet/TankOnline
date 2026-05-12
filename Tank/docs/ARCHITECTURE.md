# Project Architecture – Tank 3D Server

> Cấu trúc toàn bộ project, component relationships, key files

---

## Folder Layout

```
d:\SOURCE_C++\Tank/
├── CMakeLists.txt (root, add_subdirectory: bit_packing, server_tank, load_client)
├── vcpkg.json (dependencies: asio, fmt)
├── docs/ (documentation .md files)
│
├── bit_packing/ (serialization library)
│   ├── CMakeLists.txt
│   ├── src/
│   │   ├── BitWriter.h, BitReader.h (bit-level I/O)
│   │   ├── WriteStream.h, ReadStream.h (wrappers)
│   │   ├── Serialization.h (macros)
│   │   ├── SerializationUtils.h (bits_required)
│   │   └── [*.cpp implementations]
│   └── main.cpp (test only)
│
├── server_tank/ (main game server)
│   ├── CMakeLists.txt
│   ├── assests/map/world.json (loaded at startup)
│   ├── include/
│   │   ├── Core/
│   │   │   └── GameServer.hpp (main loop orchestrator)
│   │   ├── Entities/
│   │   │   ├── Tank.hpp (player entity, 3D position, health)
│   │   │   └── Bullet.hpp (projectile, sphere collider)
│   │   ├── Network/
│   │   │   ├── NetworkManager.hpp (IOCP async UDP server, 16 worker threads)
│   │   │   ├── SessionManager.hpp (playerID ↔ addr mapping)
│   │   │   ├── CommandDispatcher.hpp (function pointer dispatch: unordered_map<Opcode, Handler>)
│   │   │   ├── GameHandlers.hpp (handler functions void(*)(GameCommand&))
│   │   │   ├── Packets.hpp (all packet structs, bit layout)
│   │   │   ├── Opcode.hpp (enum C2S_*/S2C_*)
│   │   │   ├── NetworkConstants.h (bit ranges)
│   │   │   └── GameCommand.hpp
│   │   ├── Physics/
│   │   │   ├── PhysicsTypes.hpp (Vector3, OBBCollider, CapsuleCollider, SphereCollider)
│   │   │   ├── PhysicsWorld.hpp (SAT, swept sphere, capsule-OBB, manifolds)
│   │   │   └── UniformGrid.hpp (broad-phase spatial hash grid)
│   │   ├── World/
│   │   │   ├── GameWorld.hpp (player/bullet management, game tick)
│   │   │   └── GameMap.hpp (JSON map loader, heightmap bilinear interpolation)
│   │   ├── Memory/
│   │   │   ├── BufferPool.hpp (thread-safe pre-alloc IoContext pool)
│   │   │   └── IoContext.hpp (WSAOVERLAPPED + WSABUF wrapper)
│   │   └── Utils/
│   │       ├── Logger.hpp (async file + console logging)
│   │       └── Vector2D.hpp (legacy, can deprecate)
│   │
│   └── src/
│       ├── main.cpp (server entry point, 60 Hz game loop)
│       ├── Core/GameServer.cpp (runGameLoop, command dispatch)
│       ├── Entities/[Tank.cpp, Bullet.cpp]
│       ├── Network/[NetworkManager.cpp, SessionManager.cpp, ...]
│       ├── Physics/[PhysicsWorld.cpp, UniformGrid.cpp]
│       ├── World/[GameWorld.cpp, GameMap.cpp]
│       ├── Memory/BufferPool.cpp
│       └── Utils/Logger.cpp
│
└── load_client/ (UDP load test client)
    ├── CMakeLists.txt (links: ws2_32, fmt, bit_packing, server_tank/include)
    ├── include/
    │   ├── Config.hpp (CLI args)
    │   ├── Metrics.hpp (atomic counters, latency histogram)
    │   ├── UdpSocket.hpp (RAII non-blocking UDP)
    │   ├── PacketBuilder.hpp (build bit-packed C2S packets)
    │   ├── VirtualPlayer.hpp (simulate 1 client)
    │   └── WorkerThread.hpp (manage N players per thread)
    │
    └── src/
        ├── main.cpp (spawn workers, 1sec stats loop)
        ├── Config.cpp
        ├── Metrics.cpp
        ├── UdpSocket.cpp
        ├── PacketBuilder.cpp
        ├── VirtualPlayer.cpp
        └── WorkerThread.cpp
```

---

## Component Interaction Map

```
┌─────────────────────────────────────────────────────────────┐
│                        UDP Network                          │
└──────────────────────────────────────────────┬──────────────┘
                                               │
                                    ┌──────────┴──────────┐
                                    │                     │
                            ┌───────▼────────┐  ┌────────▼────────┐
                            │ load_client    │  │  server_tank    │
                            │ (stress test)  │  │  (prod server)  │
                            └────────────────┘  └────────┬────────┘
                                                         │
                                    ┌────────────────────┼────────────────────┐
                                    │                    │                    │
                            ┌───────▼──────┐    ┌───────▼──────┐    ┌───────▼──────┐
                            │ NetworkMgr   │    │ GameServer   │    │ SessionMgr   │
                            │ (IOCP 16x)   │    │ (tick loop)  │    │ (playerID)   │
                            └───────┬──────┘    └───────┬──────┘    └──────────────┘
                                    │                   │
                            ┌───────▼──────┐    ┌───────▼──────┐
                            │ RawPackets   │    │ CommandDisp. │
                            │ (UDP bytes)  │    │ map<Opcode,  │
                            └───────┬──────┘    │ void(*)(Cmd) │
                                    │           └───────┬──────┘
                            ┌───────▼──────┐           │
                            │ ReadStream   │    ┌───────▼──────┐
                            │ (deserialize)│    │ GameWorld    │
                            └───────┬──────┘    │ (tank/bullet)│
                                    │           └───────┬──────┘
                            ┌───────▼──────┐    ┌───────▼──────────┐
                            │ ClientInput  │    │ PhysicsWorld     │
                            │ (tank move)  │    │ (collision +     │
                            └──────────────┘    │  swept sphere)   │
                                                └──────┬───────────┘
                                                       │
                                                ┌──────▼──────┐
                                                │ UniformGrid │
                                                │ (broad-phase)│
                                                └──────────────┘
```

---

## Execution Flow – 1 Server Tick (60 Hz)

```
main() → GameServer::start(port=8080, map="world.json")
         ├─ NetworkManager::start()
         │  ├─ Create IOCP completion port
         │  ├─ Bind UDP socket
         │  └─ Spawn 16 worker threads (GetQueuedCompletionStatus loop)
         ├─ GameWorld::loadMap("world.json") → PhysicsWorld loads 82 boxes, 91 capsules
         └─ runGameLoop() [infinite while isRunning]
            │
            ├─ [Frame START time = now]
            │
            ├─ NetworkManager::fetchCommands(outCmd) → move commands from queue
            │
            ├─ For each command:
            │  └─ CommandDispatcher::dispatch(cmd)
            │        unordered_map<Opcode, void(*)(GameCommand&)> _handlers
            │        ├─ C2S_LOGIN  handler → GameWorld::addPlayer(id, spawn)
            │        ├─ C2S_MOVE   handler → GameWorld::processInput(id, input, dt)
            │        └─ C2S_SHOOT  handler → Tank::wantsShoot = true
            │
            ├─ GameWorld::update(dt=1/60):
            │  ├─ For each bullet:
            │  │  ├─ Swept sphere vs static OBBs (anti-tunneling)
            │  │  ├─ Update SphereCollider position
            │  │  └─ Decrement TTL
            │  ├─ syncColliders() → push Tank position → CapsuleCollider
            │  ├─ PhysicsWorld::Tick():
            │  │  ├─ buildGrid() → clear & insert all AABB into UniformGrid
            │  │  ├─ broadPhasePairs() → only test non-Box pairs
            │  │  ├─ DetectCollisions() → narrow-phase SAT/swept checks
            │  │  │  ├─ OBB vs OBB (15 axes SAT)
            │  │  │  ├─ Capsule vs OBB
            │  │  │  ├─ Sphere vs Capsule (bullet vs tank)
            │  │  │  └─ Sphere vs OBB (bullet vs wall)
            │  │  ├─ Generate CollisionManifold[]
            │  │  └─ HandleCollisions() → position corrections & game events
            │  ├─ applyPhysicsResults():
            │  │  ├─ Apply corrections to tank.position
            │  │  ├─ For BULLET_VS_TANK manifold: tank.takeDamage(25)
            │  │  ├─ Deactivate bullet + remove from physics
            │  │  └─ For wantsShoot: spawnBullet() → add SphereCollider
            │  └─ Respawn dead tanks at spawn points
            │
            ├─ GameWorld::getSnapshot() → TankState[]+BulletState[] binary blob
            │  (broadcast would go here if send() implemented)
            │
            ├─ [Frame sleep to maintain 60 Hz]
            └─ LOOP
```

---

## CommandDispatcher – Function Pointer Dispatch

**File:** `server_tank/include/Network/CommandDispatcher.hpp`

Không dùng switch-case. Dùng `unordered_map<Opcode, Handler>` với:

```cpp
using Handler = void(*)(GameCommand&);  // con trỏ hàm thuần

class CommandDispatcher {
    std::unordered_map<Opcode, Handler> _handlers;
public:
    void registerHandler(Opcode op, Handler handler); // đăng ký handler
    void dispatch(GameCommand& command);              // gọi handler tương ứng
};
```

**Cách hoạt động:**
```
dispatch(cmd):
    it = _handlers.find(cmd.op)   → O(1) lookup
    if found: it->second(cmd)     → gọi function pointer
```

**Đăng ký handler (tại startup):**
```cpp
dispatcher.registerHandler(Opcode::C2S_LOGIN,  &GameHandlers::handleLogin);
dispatcher.registerHandler(Opcode::C2S_MOVE,   &GameHandlers::handleMove);
dispatcher.registerHandler(Opcode::C2S_SHOOT,  &GameHandlers::handleShoot);
```

> **Status:** Fully implemented and wired. `CommandDispatcher.cpp`, `GameHandlers.cpp` complete.  
> `GameServer::registerHandlers()` injects `GameWorld*` + `SessionManager*` into `GameHandlers::init()`,  
> then registers all three handlers. The game loop calls `_dispatcher.dispatch(cmd)` for every command.

---

## Key Algorithms

| Component | Algorithm | File | Purpose |
|-----------|-----------|------|---------|
| **Broad-phase** | Uniform grid spatial hashing | `UniformGrid.cpp` | Fast O(1) pair candidate generation |
| **Narrow-phase (OBB)** | SAT (Separating Axis Theorem, 15 axes) | `PhysicsWorld.cpp` | Precise OBB collision detection |
| **Narrow-phase (Capsule)** | Closest-point segment-surface | `PhysicsWorld.cpp` | Capsule-OBB intersection |
| **Bullet anti-tunnel** | Swept sphere ray vs expanded OBB (Minkowski) | `PhysicsWorld.cpp` | Prevent bullets passing through thin walls |
| **Manifold generation** | MTD (Minimum Translation Distance) + normal | `PhysicsWorld.cpp` | Collision info: normal, depth, type |
| **Input handling** | ReadStream bit-unpacking | `NetworkManager.cpp` | Decode variable-width fields |
| **Network I/O** | IOCP (I/O Completion Ports) | `NetworkManager.cpp` | Scalable async UDP on Windows |

---

## Memory & Threading

| Subsystem | Threads | Memory |
|-----------|---------|--------|
| **NetworkManager IOCP** | 16 (hardware_concurrency × 2) | BufferPool: 10,000 × IoContext (10KB each) |
| **GameServer loop** | 1 (main) | ~100 MB (world data, entities) |
| **Logger async writer** | 1 (worker) | Queue-based, bounded |
| **load_client workers** | N (default = cpu_count) | Per-player: ~500B overhead |

---

## Critical Files to Know

| File | Purpose | Size (LOC) |
|------|---------|-----------|
| `server_tank/src/main.cpp` | Entry point, startup config | 20 |
| `server_tank/src/Core/GameServer.cpp` | Game loop orchestration | 40 |
| `server_tank/src/World/GameWorld.cpp` | Game state + tick update | 200 |
| `server_tank/src/Physics/PhysicsWorld.cpp` | Full collision system | 400 |
| `server_tank/src/Network/NetworkManager.cpp` | IOCP + async UDP | 150 |
| `server_tank/include/Network/Packets.hpp` | All packet structs + bit layout | 100 |
| `load_client/src/PacketBuilder.cpp` | Build C2S packets | 80 |
| `load_client/src/main.cpp` | CLI + stats loop | 100 |

---

## Build & Run

```bash
# Configure (re-run if new files added)
cmake -S d:\SOURCE_C++\Tank -B d:\SOURCE_C++\Tank\out\build\x64-Debug -G Ninja

# Build (from Developer Command Prompt or after vcvars64.bat)
cmake --build out/build/x64-Debug --target server_tank
cmake --build out/build/x64-Debug --target load_client

# Run server
cd out/build/x64-Debug/server_tank
.\server_tank.exe
# Output: server.log, listens on 127.0.0.1:8080

# Run load test (in another window)
cd out/build/x64-Debug/load_client
.\load_client.exe --clients 100 --duration 30 --rate 20
# Output: live stats every 1s, final summary
```

---

## TODO / Known Limitations

- [ ] `send()` not wired up – server doesn't broadcast snapshots back to clients yet
- [ ] Session manager not fully integrated – `CommandDispatcher` doesn't match incoming addr to playerID
- [ ] No client-server validation – trust client input without cheating detection
- [ ] Single map hardcoded – no map switching at runtime
- [ ] No persistence – all game state lost on shutdown
- [ ] Bullet latency RTT measurement in load_client needs server echo support
