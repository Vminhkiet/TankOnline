#include "GameLoop.hpp"
#include "Network/Packets.hpp"
#include "Network/Opcode.hpp"
#include "Utils/Logger.hpp"
#include "ReadStream.h"
#include <cstring>
#include <vector>

// Same wire layout as Match.cpp SnapshotHeader (not bit-packed, Unity reads raw)
#pragma pack(push, 1)
struct SnapshotHeader {
    uint32_t matchId;
    uint16_t opcode;
    uint16_t serverTick;
    uint16_t tankCount;
    uint16_t localPlayerId;
};
#pragma pack(pop)

// ────────────────────────────────────────────────────────────────────────────

GameLoop::GameLoop(UdpServer& udp, MatchConfig cfg)
    : _udp(udp), _cfg(std::move(cfg)) {}

bool GameLoop::init() {
    _world.disableRespawn();
    std::string mapPath = "assests/map/" + _cfg.mapName + ".json";
    if (!_world.loadMap(mapPath)) {
        LOG_ERR("[Baseline] failed to load map '{}'", mapPath);
        return false;
    }
    LOG_INFO("[Baseline] ready (matchId={}, map={}, players={})",
             _cfg.matchId, _cfg.mapName, _cfg.playerIds.size());
    return true;
}

// ────────────────────────────────────────────────────────────────────────────

bool GameLoop::resolvePlayer(const sockaddr_in& from, uint32_t& outPid) {
    if (_sessions.getPlayerId(from, outPid)) return true;
    if (!_sessions.resolve(from, _cfg.playerIds, outPid)) return false;

    // Find slot index to pick the right spawn point
    size_t slot = 0;
    for (size_t i = 0; i < _cfg.playerIds.size(); ++i)
        if (_cfg.playerIds[i] == outPid) { slot = i; break; }

    Vector3 spawn = _world.getSpawnPosition(slot);
    _world.addPlayer(outPid, spawn);
    LOG_INFO("[Baseline] player {} spawned ({:.1f},{:.1f},{:.1f})",
             outPid, spawn.x, spawn.y, spawn.z);
    return true;
}

// ────────────────────────────────────────────────────────────────────────────

void GameLoop::handlePacket(const uint8_t* buf, int len, const sockaddr_in& from) {
    if (len < 4) return;

    // Parse only the header to get opcode — same bit-packed format as server_tank
    ReadStream rs(reinterpret_cast<const uint32_t*>(buf), len);
    PacketHeader hdr{};
    if (!hdr.Serialize(rs)) return;

    // ── Direct switch — no dispatcher map ────────────────────────────────────
    switch (hdr.opcode) {
        case Opcode::C2S_LOGIN: handleLogin(from);            break;
        case Opcode::C2S_MOVE:  handleMove(buf, len, from);   break;
        case Opcode::C2S_SHOOT: handleShoot(from);            break;
        default:
            LOG_WARN("[Baseline] unknown opcode {}", static_cast<uint16_t>(hdr.opcode));
            break;
    }
}

void GameLoop::handleLogin(const sockaddr_in& from) {
    uint32_t pid = 0;
    resolvePlayer(from, pid); // register if first time, touch heartbeat below
    _sessions.touch(from);
}

void GameLoop::handleMove(const uint8_t* buf, int len, const sockaddr_in& from) {
    uint32_t pid = 0;
    if (!resolvePlayer(from, pid)) return;
    _sessions.touch(from);

    ReadStream rs(reinterpret_cast<const uint32_t*>(buf), len);
    PacketHeader hdr{};
    if (!hdr.Serialize(rs)) return;
    PacketMovement pkt{};
    if (!pkt.Serialize(rs)) return;

    _world.processInput(pid, pkt.toClientInput());
}

void GameLoop::handleShoot(const sockaddr_in& from) {
    uint32_t pid = 0;
    if (!resolvePlayer(from, pid)) return;
    _sessions.touch(from);

    ClientInput ci{};
    ci.shoot = true;
    _world.processInput(pid, ci);
}

// ────────────────────────────────────────────────────────────────────────────

void GameLoop::tick(float dt) {
    _world.update(dt);
}

void GameLoop::broadcastSnapshot() {
    auto body = _world.getSnapshot(); // [uint16 tankCount][TankState...][uint16 bulletCount][BulletState...]

    for (uint32_t pid : _cfg.playerIds) {
        sockaddr_in addr{};
        if (!_sessions.getAddress(pid, addr)) continue;

        SnapshotHeader hdr{};
        hdr.matchId       = _cfg.matchId;
        hdr.opcode        = static_cast<uint16_t>(Opcode::S2C_SNAPSHOT);
        hdr.serverTick    = _serverTick;
        hdr.localPlayerId = static_cast<uint16_t>(pid);
        std::memcpy(&hdr.tankCount, body.data(), 2);

        std::vector<uint8_t> pkt(sizeof(SnapshotHeader) + body.size());
        std::memcpy(pkt.data(), &hdr, sizeof(hdr));
        std::memcpy(pkt.data() + sizeof(hdr), body.data(), body.size());
        _udp.send(addr, pkt.data(), static_cast<int>(pkt.size()));
    }
    ++_serverTick;
}
