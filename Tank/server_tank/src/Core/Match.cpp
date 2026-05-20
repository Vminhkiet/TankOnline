#include "Core/Match.hpp"
#include "Network/Packets.hpp"
#include "Utils/Logger.hpp"
#include "ReadStream.h"
#include <cmath>
#include <cstring>
#include <vector>

// Raw S2C snapshot header — not bit-packed, Unity reads with BinaryReader
#pragma pack(push, 1)
struct SnapshotHeader {
    uint32_t matchId;
    uint16_t opcode;         // Opcode::S2C_SNAPSHOT = 2000
    uint16_t serverTick;
    uint16_t tankCount;
    uint16_t localPlayerId;  // which tank belongs to the recipient
    uint16_t timeRemainingTenths;
};
#pragma pack(pop)

Match::Match(MatchConfig config, INetworkBackend& network,
             std::function<void(MatchResult)> onEnd)
    : _config(std::move(config)), _network(network), _onEnd(std::move(onEnd))
{
    _world.disableRespawn();

    std::string mapPath = "assests/map/" + _config.mapName + ".json";
    if (!_world.loadMap(mapPath))
        LOG_ERR("Match {}: failed to load map '{}'", _config.matchId, mapPath);

    // Không pre-spawn — tank chỉ được tạo khi player thực sự connect (resolvePlayer)

    registerHandlers();

    LOG_INFO("Match {}: created ({} players, map={}, maxDur={}s)",
             _config.matchId, _config.playerIds.size(),
             _config.mapName, _config.maxDurationSecs);
}

// ────────────────────────────────────────────────────────────────────────────

void Match::registerHandlers() {
    _dispatcher.registerHandler(Opcode::C2S_MOVE,
        [this](GameCommand& cmd) { handleMove(cmd); });
    _dispatcher.registerHandler(Opcode::C2S_SHOOT,
        [this](GameCommand& cmd) { handleShoot(cmd); });
}

// ────────────────────────────────────────────────────────────────────────────

void Match::pushCommand(GameCommand cmd) {
    std::lock_guard lock(_queueMutex);
    _cmdQueue.push_back(std::move(cmd));
}

// ────────────────────────────────────────────────────────────────────────────

void Match::tick(float dt) {
    if (!_running.load()) return;

    using Clock = std::chrono::high_resolution_clock;
    using Us    = std::chrono::microseconds;

    // 1. Drain command queue
    std::deque<GameCommand> local;
    {
        std::lock_guard lock(_queueMutex);
        local.swap(_cmdQueue);
    }

    // 2. Dispatch — timed to capture handler overhead (handleMove / handleShoot)
    auto t_dispatch_start = Clock::now();
    for (auto& cmd : local) {
        cmd.dt = dt;
        _dispatcher.dispatch(cmd);
    }
    auto t_dispatch_end = Clock::now();

    // 3. Physics — timed separately only when match is PLAYING (has live sessions)
    const bool playing = (_sessions.size() > 0);

    if (playing) {
        _accumDispatchUs += std::chrono::duration_cast<Us>(t_dispatch_end - t_dispatch_start).count();
        _accumCmdQDepth  += static_cast<uint64_t>(local.size());
    }

    auto t_bullet = Clock::now();
    _world.updateBullets(dt);
    auto t_collision = Clock::now();
    _world.runPhysics(dt);
    auto t_phys_end = Clock::now();

    if (playing) {
        _accumBulletUs    += std::chrono::duration_cast<Us>(t_collision  - t_bullet).count();
        _accumCollisionUs += std::chrono::duration_cast<Us>(t_phys_end   - t_collision).count();
    }

    // 4. Broadcast snapshot (SNAPSHOT_EVERY = 1 → every tick = 60 Hz)
    if (++_tickCount >= SNAPSHOT_EVERY) {
        _tickCount = 0;
        auto t_snap_start = Clock::now();
        broadcastSnapshot();
        if (playing)
            _accumSnapUs += std::chrono::duration_cast<Us>(Clock::now() - t_snap_start).count();
    }

    // 5. [Task] log — emit once per TASK_LOG_TICKS PLAYING ticks (no per-tick disk I/O)
    if (playing && ++_taskTickCount >= TASK_LOG_TICKS) {
        uint64_t avgBullet    = _accumBulletUs    / _taskTickCount;
        uint64_t avgCollision = _accumCollisionUs / _taskTickCount;
        uint64_t avgSnap      = _accumSnapUs      / _taskTickCount;
        uint64_t avgDispatch  = _accumDispatchUs  / _taskTickCount;
        uint64_t avgCmdQ      = _accumCmdQDepth   / _taskTickCount;
        LOG_INFO("[Task] match={} bullet={}us physics={}us snap={}us dispatch={}us cmdQ={}",
                 _config.matchId, avgBullet, avgCollision, avgSnap, avgDispatch, avgCmdQ);
        _accumBulletUs    = 0;
        _accumCollisionUs = 0;
        _accumSnapUs      = 0;
        _accumDispatchUs  = 0;
        _accumCmdQDepth   = 0;
        _taskTickCount    = 0;
    }

    // 5. Disconnect timeout — 5s không nhận packet thì coi là out
    {
        int cur = static_cast<int>(_sessions.size());
        if (cur > _peakConnected) _peakConnected = cur;

#ifdef PROFILING_SINGLE_CORE
        constexpr int SESSION_TIMEOUT_SECS = 60;  // PROFILING: keep bots alive longer
#else
        constexpr int SESSION_TIMEOUT_SECS = 5;
#endif
        for (uint32_t pid : _sessions.collectTimeouts(SESSION_TIMEOUT_SECS)) {
            LOG_INFO("Match {}: player {} timed out (disconnected)", _config.matchId, pid);
            _sessions.removeSession(pid);
            _world.killPlayer(pid);
        }

        // Nếu đã từng có người chơi mà giờ tất cả đã out → kết thúc match
        if (_peakConnected > 0 && _sessions.size() == 0) {
            LOG_INFO("Match {}: all players disconnected, ending match", _config.matchId);
            MatchResult r;
            r.matchId      = _config.matchId;
            r.outcome      = MatchOutcome::Draw;
            r.durationSecs = _elapsed;
            r.kills        = _world.getKills();
            r.userIds      = _config.userIds;
            r.mapName      = _config.mapName;
            _running.store(false);
            _onEnd(r);
            return;
        }
    }

    // 6. Win condition
    _elapsed += dt;
    MatchResult result;
    result.matchId = _config.matchId;

    auto outcome = _world.checkOutcome(
        _elapsed,
        static_cast<float>(_config.maxDurationSecs),
        _config.playerIds, result);

    if (outcome != MatchOutcome::Running) {
        result.userIds = _config.userIds;
        result.mapName = _config.mapName;
        broadcastMatchEnd(result);
        _running.store(false);
        LOG_INFO("Match {}: ended (outcome={}, winner={}, dur={:.1f}s)",
                 _config.matchId,
                 static_cast<int>(outcome), result.winnerId, result.durationSecs);
        _onEnd(result);
    }
}

// ────────────────────────────────────────────────────────────────────────────

void Match::broadcastSnapshot() {
    auto body = _world.getSnapshot(); // [uint16 tankCount][TankState...][uint16 bulletCount][BulletState...]

    static uint16_t sTick = 0;
    const uint16_t tick = sTick++;

    // Build one packet per recipient so each gets their own localPlayerId
    for (uint32_t pid : _config.playerIds) {
        sockaddr_in addr{};
        if (!_sessions.getAddress(pid, addr)) continue;

        SnapshotHeader hdr;
        hdr.matchId       = _config.matchId;
        hdr.opcode        = static_cast<uint16_t>(Opcode::S2C_SNAPSHOT);
        hdr.serverTick    = tick;
        hdr.localPlayerId = static_cast<uint16_t>(pid); // tell client which tank is theirs
        hdr.timeRemainingTenths = static_cast<uint16_t>(std::roundf(
            std::max(0.0f, _config.maxDurationSecs - _elapsed) * 10.0f));
        std::memcpy(&hdr.tankCount, body.data(), 2);

        std::vector<uint8_t> pkt(sizeof(SnapshotHeader) + body.size());
        std::memcpy(pkt.data(), &hdr, sizeof(hdr));
        std::memcpy(pkt.data() + sizeof(hdr), body.data(), body.size());
        _network.send(addr, pkt.data(), pkt.size());
    }
}

bool Match::resolvePlayer(const sockaddr_in& addr, uint32_t& outPid) {
    // Fast path: already registered
    if (_sessions.getPlayerID(addr, outPid)) return true;

    // Assign the next free slot sequentially
    std::lock_guard lock(_slotMutex);
    if (_nextSlot >= _config.playerIds.size()) return false;

    size_t slot = _nextSlot++;
    outPid = _config.playerIds[slot];
    _sessions.addSession(outPid, "player" + std::to_string(outPid), addr);

    // Spawn tank now that the player has actually connected
    Vector3 spawn = _world.getSpawnPosition(slot);
    _world.addPlayer(outPid, spawn);

    LOG_INFO("Match {}: player {} joined from {}:{} → spawn ({:.1f},{:.1f},{:.1f})",
             _config.matchId, outPid,
             ntohl(addr.sin_addr.s_addr), ntohs(addr.sin_port),
             spawn.x, spawn.y, spawn.z);
    return true;
}

// ────────────────────────────────────────────────────────────────────────────

void Match::handleMove(GameCommand& cmd) {
    uint32_t pid = 0;
    if (!resolvePlayer(cmd.sender, pid)) return;
    _sessions.updateHeartbeat(cmd.sender);

    const auto& buf = cmd.rawBuffer;
    if (buf.empty()) return;

    ReadStream rs(reinterpret_cast<const uint32_t*>(buf.data()),
                  static_cast<int>(buf.size()));

    PacketHeader hdr{};
    if (!hdr.Serialize(rs)) return;

    PacketMovement pkt{};
    if (!pkt.Serialize(rs)) return;

    _world.processInput(pid, pkt.toClientInput());
}

void Match::broadcastMatchEnd(const MatchResult& r) {
    MatchEndPacket pkt;
    pkt.matchId      = r.matchId;
    pkt.opcode       = static_cast<uint16_t>(Opcode::S2C_MATCH_END);
    pkt.winnerId     = r.winnerId;
    pkt.durationSecs = static_cast<uint16_t>(r.durationSecs);

    for (uint32_t pid : _config.playerIds) {
        sockaddr_in addr{};
        if (!_sessions.getAddress(pid, addr)) continue;

        // outcome from this player's perspective
        if (r.outcome == MatchOutcome::Win)
            pkt.outcome = static_cast<uint8_t>(pid == r.winnerId ? 0 : 1); // 0=win 1=lose
        else
            pkt.outcome = static_cast<uint8_t>(r.outcome); // 2=draw 3=timeout

        auto it = r.kills.find(pid);
        pkt.myKills = static_cast<uint16_t>(it != r.kills.end() ? it->second : 0);

        _network.send(addr, reinterpret_cast<const uint8_t*>(&pkt), sizeof(pkt));
    }

    LOG_INFO("Match {}: S2C_MATCH_END broadcast ({} recipients)",
             r.matchId, _sessions.size());
}

bool Match::forceLogoutByUserId(const std::string& userId, uint16_t code,
                                const std::string& message, uint32_t disconnectAfterMs) {
    for (auto& [pid, uid] : _config.userIds) {
        if (uid != userId) continue;

        sockaddr_in addr{};
        if (_sessions.getAddress(pid, addr)) {
            std::vector<uint8_t> buf(sizeof(ForceLogoutPacket) + message.size());
            auto* pkt                = reinterpret_cast<ForceLogoutPacket*>(buf.data());
            pkt->matchId             = _config.matchId;
            pkt->opcode              = static_cast<uint16_t>(Opcode::S2C_FORCE_LOGOUT);
            pkt->code                = code;
            pkt->messageLen          = static_cast<uint16_t>(message.size());
            pkt->disconnectAfterMs   = disconnectAfterMs;
            std::memcpy(buf.data() + sizeof(ForceLogoutPacket), message.data(), message.size());
            _network.send(addr, buf.data(), buf.size());
        }

        _sessions.removeSession(pid);
        _world.killPlayer(pid);
        LOG_INFO("Match {}: force-logout player {} (userId={})", _config.matchId, pid, userId);
        return true;
    }
    return false;
}

void Match::handleShoot(GameCommand& cmd) {
    uint32_t pid = 0;
    if (!resolvePlayer(cmd.sender, pid)) return;
    _sessions.updateHeartbeat(cmd.sender);

    const auto& buf = cmd.rawBuffer;
    ClientInput ci{};
    ci.shoot = true;
    if (!buf.empty()) {
        ReadStream rs(reinterpret_cast<const uint32_t*>(buf.data()),
                      static_cast<int>(buf.size()));
        PacketHeader hdr{};
        PacketShoot  pkt{};
        if (hdr.Serialize(rs) && pkt.Serialize(rs))
            ci = pkt.toClientInput();
    }
    _world.processInput(pid, ci);
}
