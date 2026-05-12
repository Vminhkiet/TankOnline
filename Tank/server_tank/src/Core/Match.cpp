#include "Core/Match.hpp"
#include "Network/Packets.hpp"
#include "Utils/Logger.hpp"
#include "ReadStream.h"
#include <cmath>
#include <cstring>

// Raw S2C snapshot header — not bit-packed, Unity reads with BinaryReader
#pragma pack(push, 1)
struct SnapshotHeader {
    uint32_t matchId;
    uint16_t opcode;         // Opcode::S2C_SNAPSHOT = 2000
    uint16_t serverTick;
    uint16_t tankCount;
    uint16_t localPlayerId;  // which tank belongs to the recipient
};
#pragma pack(pop)

Match::Match(MatchConfig config, NetworkManager& network,
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

    // 1. Drain command queue
    std::deque<GameCommand> local;
    {
        std::lock_guard lock(_queueMutex);
        local.swap(_cmdQueue);
    }

    // 2. Dispatch
    for (auto& cmd : local) {
        cmd.dt = dt;
        _dispatcher.dispatch(cmd);
    }

    // 3. Physics + game logic
    _world.update(dt);

    // 4. Broadcast snapshot at 20 Hz (every 3 ticks)
    if (++_tickCount >= SNAPSHOT_EVERY) {
        _tickCount = 0;
        broadcastSnapshot();
    }

    // 5. Disconnect timeout — 5s không nhận packet thì coi là out
    {
        int cur = static_cast<int>(_sessions.size());
        if (cur > _peakConnected) _peakConnected = cur;

        for (uint32_t pid : _sessions.collectTimeouts(5)) {
            LOG_INFO("Match {}: player {} timed out (disconnected)", _config.matchId, pid);
            _sessions.removeSession(pid);
            _world.killPlayer(pid);
        }

        // Nếu đã từng có người chơi mà giờ tất cả đã out → kết thúc match
        if (_peakConnected > 0 && _sessions.size() == 0) {
            LOG_INFO("Match {}: all players disconnected, ending match", _config.matchId);
            _running.store(false);
            MatchResult r;
            r.matchId      = _config.matchId;
            r.outcome      = MatchOutcome::Draw;
            r.durationSecs = _elapsed;
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

void Match::handleShoot(GameCommand& cmd) {
    uint32_t pid = 0;
    if (!resolvePlayer(cmd.sender, pid)) return;
    _sessions.updateHeartbeat(cmd.sender);

    ClientInput ci{};
    ci.shoot = true;
    _world.processInput(pid, ci);
}
