#pragma once
#include <deque>
#include <mutex>
#include <atomic>
#include <functional>
#include "Core/MatchConfig.hpp"
#include "World/GameWorld.hpp"
#include "Network/SessionManager.hpp"
#include "Network/CommandDispatcher.hpp"
#include "Network/GameCommand.hpp"
#include "Network/INetworkBackend.hpp"

class Match {
public:
    Match(MatchConfig config,
          INetworkBackend& network,
          std::function<void(MatchResult)> onEnd);

    uint32_t id()              const { return _config.matchId; }
    bool     isRunning()       const { return _running.load(std::memory_order_relaxed); }
    size_t   connectedPlayers()const { return _sessions.size(); }
    size_t   totalSlots()      const { return _config.playerIds.size(); }
    void     logPositions()    const { _world.logTankPositions(_config.matchId); }

    // Thread-safe. Called from NetworkManager IOCP workers.
    void pushCommand(GameCommand cmd);

    // Called from tick dispatcher thread (one match per pool task).
    void tick(float dt);

    // Kick player by userId string (duplicate-login detection). Returns true if found.
    bool forceLogoutByUserId(const std::string& userId, uint16_t code,
                             const std::string& message, uint32_t disconnectAfterMs);

private:
    MatchConfig   _config;
    GameWorld     _world;
    SessionManager _sessions;
    CommandDispatcher _dispatcher;
    INetworkBackend& _network;
    std::function<void(MatchResult)> _onEnd;

    std::deque<GameCommand>  _cmdQueue;
    std::mutex               _queueMutex;
    std::atomic<bool>        _running{true};
    float                    _elapsed = 0.f;

    uint32_t  _nextSlot      = 0;
    int       _tickCount     = 0;
    int       _peakConnected = 0;   // max simultaneous sessions ever
    std::mutex _slotMutex;

    static constexpr int SNAPSHOT_EVERY  = 1;   // broadcast every tick = 60 Hz
    static constexpr int TASK_LOG_TICKS  = 600; // aggregate window (10s @ 60Hz)

    // [Task] profiling accumulators — reset every TASK_LOG_TICKS ticks
    // Only accumulated when match is PLAYING (has connected sessions)
    uint64_t _accumBulletUs    = 0;   // updateBullets: swept-sphere CCD + O(B*T) hit check
    uint64_t _accumCollisionUs = 0;   // runPhysics: tank movement + PhysicsWorld::Tick
    uint64_t _accumSnapUs      = 0;   // broadcastSnapshot: serialize + UDP send
    uint64_t _accumDispatchUs  = 0;   // drain _cmdQueue + dispatcher.dispatch() per tick
    uint64_t _accumCmdQDepth   = 0;   // sum of command queue depth at drain time
    uint32_t _taskTickCount    = 0;   // only increments while PLAYING

    void registerHandlers();
    bool resolvePlayer(const sockaddr_in& addr, uint32_t& outPid, const std::string& overrideTankName = "");
    void broadcastSnapshot();
    void broadcastMatchEnd(const MatchResult& r);

    void handleMove (GameCommand& cmd);
    void handleShoot(GameCommand& cmd);
};
