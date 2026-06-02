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
    _dispatcher.registerHandler(Opcode::C2S_PING,
        [this](GameCommand& cmd) { handlePing(cmd); });
}

// ────────────────────────────────────────────────────────────────────────────

void Match::pushCommand(GameCommand cmd) {
    std::lock_guard lock(_queueMutex);
    _cmdQueue.push_back(std::move(cmd));
}

// ────────────────────────────────────────────────────────────────────────────

void Match::tick(float dt, int64_t budgetUs, MetricsCollector& metrics) {
    if (!_running.load()) return;

    using Clock = std::chrono::high_resolution_clock;
    using Us    = std::chrono::microseconds;
    
    auto tickStart = Clock::now();

    // 1. Drain command queue
    auto t_drain_start = Clock::now();
    std::deque<GameCommand> local;
    {
        std::lock_guard lock(_queueMutex);
        local.swap(_cmdQueue);
    }
    auto t_drain_end = Clock::now();

    // 2. Dispatch — timed to capture handler overhead (handleMove / handleShoot)
    auto t_dispatch_start = Clock::now();
    for (auto& cmd : local) {
        uint32_t pid = 0;
        std::string tankNameOverride = "";

        if (cmd.op == Opcode::C2S_LOGIN) {
            if (!_sessions.getPlayerID(cmd.sender, pid)) {
                if (!cmd.rawBuffer.empty()) {
                    ReadStream rs(reinterpret_cast<const uint32_t*>(cmd.rawBuffer.data()), static_cast<int>(cmd.rawBuffer.size()));
                    PacketHeader dummy;
                    dummy.Serialize(rs); // skip header

                    int32_t typeIndex = 0;
                    rs.SerializeInteger(typeIndex, 0, 255);
                    tankNameOverride = _world.getMap().getTankNameByIndex(static_cast<uint8_t>(typeIndex));

                    // Doc token (32 ASCII chars) theo sau typeIndex
                    // Token duoc ghi phia sau phan bit-packed: [typeIndex bits][32 bytes token]
                    int byteOffset = rs.GetBytesRead();
                    const auto& raw = cmd.rawBuffer;
                    if ((int)raw.size() >= byteOffset + 32) {
                        std::string token(reinterpret_cast<const char*>(raw.data() + byteOffset), 32);
                        if (!resolvePlayerWithToken(cmd.sender, token, pid, tankNameOverride)) {
                            LOG_WARN("Match {}: rejected C2S_LOGIN from {}:{} — invalid token '{}'",
                                     _config.matchId,
                                     ntohl(cmd.sender.sin_addr.s_addr), ntohs(cmd.sender.sin_port),
                                     token);
                            continue; // tu choi ket noi
                        }
                        LOG_INFO("Match {}: player {} authenticated via token, tank='{}'",
                                 _config.matchId, pid, tankNameOverride);
                        if (cmd.op == Opcode::C2S_LOGIN) continue;
                        cmd.dt = dt;
                        _dispatcher.dispatch(cmd);
                        continue;
                    }
                    // Fallback: khong co token → dung resolvePlayer cu (backward compat)
                    LOG_WARN("Match {}: C2S_LOGIN from {}:{} has no token — using legacy slot assign",
                             _config.matchId,
                             ntohl(cmd.sender.sin_addr.s_addr), ntohs(cmd.sender.sin_port));
                }
            }
        }

        if (!resolvePlayer(cmd.sender, pid, tankNameOverride)) continue;
        
        if (cmd.op == Opcode::C2S_LOGIN) continue; // LOGIN packet has no further logic

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
    _world.spawnItems();
    auto t_phys_end = Clock::now();

    // Broadcast shoot events instantly (don't wait for snapshot tick rate)
    auto shootEvents = _world.getShootEvents();
    if (!shootEvents.empty()) {
        for (auto& ev : shootEvents) {
            ev.matchId = _config.matchId;
            for (uint32_t pid : _config.playerIds) {
                sockaddr_in addr{};
                if (_sessions.getAddress(pid, addr)) {
                    _network.send(addr, reinterpret_cast<const uint8_t*>(&ev), sizeof(EventShootPacket));
                }
            }
        }
    }

    auto spawnEvents = _world.getItemSpawnEvents();
    if (!spawnEvents.empty()) {
        for (auto& ev : spawnEvents) {
            ev.matchId = _config.matchId;
            for (uint32_t pid : _config.playerIds) {
                sockaddr_in addr{};
                if (_sessions.getAddress(pid, addr)) {
                    _network.send(addr, reinterpret_cast<const uint8_t*>(&ev), sizeof(PacketSpawnItem));
                }
            }
        }
    }

    auto despawnEvents = _world.getItemDespawnEvents();
    if (!despawnEvents.empty()) {
        for (auto& ev : despawnEvents) {
            ev.matchId = _config.matchId;
            for (uint32_t pid : _config.playerIds) {
                sockaddr_in addr{};
                if (_sessions.getAddress(pid, addr)) {
                    _network.send(addr, reinterpret_cast<const uint8_t*>(&ev), sizeof(PacketDespawnItem));
                }
            }
        }
    }

    if (playing) {
        _accumBulletUs    += std::chrono::duration_cast<Us>(t_collision  - t_bullet).count();
        _accumCollisionUs += std::chrono::duration_cast<Us>(t_phys_end   - t_collision).count();
    }
    
    // 3.5 Graceful Degradation Check
    auto t_critical_end = Clock::now();
    int64_t elapsedUs = std::chrono::duration_cast<Us>(t_critical_end - tickStart).count();
    
    if (elapsedUs <= budgetUs) {
        // [NON-CRITICAL SYSTEMS]
        // Example: visibility calculations, minimap updates, analytics, telemetry, AI perception
        // Simulated execution for academic report:
        // if (playing) logPositions();
    } else {
        // Budget violated, skip non-critical systems
        metrics.recordBudgetViolation();
    }

    // 4. Broadcast snapshot (SNAPSHOT_EVERY = 1 → every tick = 60 Hz)
    int64_t snapUs = 0;
    if (++_tickCount >= SNAPSHOT_EVERY) {
        _tickCount = 0;
        auto t_snap_start = Clock::now();
        broadcastSnapshot();
        auto t_snap_end = Clock::now();
        snapUs = std::chrono::duration_cast<Us>(t_snap_end - t_snap_start).count();
        if (playing) _accumSnapUs += snapUs;
    }

    // 5. Record per-tick breakdown (every tick — benchmark reads via lastBreakdown())
    {
        int64_t drainUs    = std::chrono::duration_cast<Us>(t_drain_end    - t_drain_start).count();
        int64_t dispatchUs = std::chrono::duration_cast<Us>(t_dispatch_end - t_dispatch_start).count();
        int64_t bulletUs   = std::chrono::duration_cast<Us>(t_collision    - t_bullet).count();
        int64_t physUs     = std::chrono::duration_cast<Us>(t_phys_end     - t_collision).count();
        _lastBreakdown.drainUs         = drainUs;
        _lastBreakdown.dispatchUs      = dispatchUs;
        _lastBreakdown.bulletUs        = bulletUs;
        _lastBreakdown.physicsUs       = physUs;
        _lastBreakdown.snapUs          = snapUs;
        _lastBreakdown.activeBullets   = static_cast<int64_t>(_world.activeBulletCount());
        _lastBreakdown.totalPostDrainUs = dispatchUs + bulletUs + physUs + snapUs;
    }

    // 5b. [Task] log — emit once per TASK_LOG_TICKS PLAYING ticks (no per-tick disk I/O)
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
            
            // Compute RP for early quit
            size_t totalPlayers = r.userIds.size();
            for (auto& [pid, uid] : r.userIds) {
                r.rpRewards[pid] = 0; // everyone gets 0 RP for early quit
            }
            
            _running.store(false);
            _onEnd(r);
            return;
        }
    }

    // 6. Win condition
    // Only start counting match time after all expected players have connected AND the intro (5.5s) has finished
    bool allPlayersReady = (_sessions.size() >= _config.playerIds.size());
    if (allPlayersReady) {
        if (!_introStarted) {
            _introStarted = true;
            _introTimer = 0.f;
        }
    }

    if (_introStarted) {
        if (_introTimer < 11.5f) {
            _introTimer += dt;
        } else {
            _elapsed += dt;
        }
    }

    MatchResult result;
    result.matchId = _config.matchId;

    auto outcome = _world.checkOutcome(
        _elapsed,
        static_cast<float>(_config.maxDurationSecs),
        _config.playerIds, result);

    if (outcome != MatchOutcome::Running) {
        result.userIds = _config.userIds;
        result.mapName = _config.mapName;

        // Compute RP for each player before broadcasting
        size_t totalPlayers = result.userIds.size();
        for (auto& [pid, uid] : result.userIds) {
            int placement = result.placements.count(pid) ? result.placements.at(pid) : totalPlayers;
            int placementRp = 0;
            if (placement == 1) placementRp = 25;
            else if (placement == 2) placementRp = 18;
            else if (placement == 3) placementRp = 12;
            else if (placement <= (totalPlayers + 1) / 2) placementRp = 4;
            else placementRp = -5;

            int kills = result.kills.count(pid) ? result.kills.at(pid) : 0;
            int damage = result.damageDealt.count(pid) ? result.damageDealt.at(pid) : 0;
            
            int combatBonus = kills + (damage / 1000);
            if (combatBonus > 5) combatBonus = 5;
            
            int finalRp = placementRp + combatBonus;
            if (finalRp < 0) finalRp = 0;
            
            result.rpRewards[pid] = finalRp;
        }

        broadcastMatchEnd(result);
        _running.store(false);
        LOG_INFO("Match {}: ended (outcome={}, winner={}, dur={:.1f}s)",
                 _config.matchId,
                 static_cast<int>(outcome), result.winnerId, result.durationSecs);
        _onEnd(result);
    }
}

// ────────────────────────────────────────────────────────────────────────────

void Match::handlePing(GameCommand& cmd) {

    if (cmd.rawBuffer.empty()) return;
    ReadStream rs(reinterpret_cast<const uint32_t*>(cmd.rawBuffer.data()), static_cast<int>(cmd.rawBuffer.size()));
    PacketHeader hdr;
    if (!hdr.Serialize(rs)) return;

    PacketPing ping;
    if (!ping.Serialize(rs)) return;

    // Calculate queue processing delay (server internal lag)
    auto now = std::chrono::high_resolution_clock::now();
    double queueDelayMs = std::chrono::duration<double, std::milli>(now - cmd.recvTime).count();

    uint32_t pid = 0;
    resolvePlayer(cmd.sender, pid);

    // Log the internal processing delay so we know if the server is lagging internally
    LOG_INFO("Match {}: Processed PING from Player {}. Server internal queue delay: {:.3f} ms", _config.matchId, pid, queueDelayMs);

    PacketPong pong;
    pong.matchId = _config.matchId;
    pong.opcode = static_cast<uint16_t>(Opcode::S2C_PONG);
    pong.clientTimeMs = ping.clientTimeMs;

    _network.send(cmd.sender, reinterpret_cast<const uint8_t*>(&pong), sizeof(PacketPong));
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
            (std::max)(0.0f, _config.maxDurationSecs - _elapsed) * 10.0f));
        std::memcpy(&hdr.tankCount, body.data(), 2);

        std::vector<uint8_t> pkt(sizeof(SnapshotHeader) + body.size());
        std::memcpy(pkt.data(), &hdr, sizeof(hdr));
        std::memcpy(pkt.data() + sizeof(hdr), body.data(), body.size());
        _network.send(addr, pkt.data(), pkt.size());
    }
}

bool Match::resolvePlayerWithToken(const sockaddr_in& addr, const std::string& token,
                                    uint32_t& outPid, const std::string& overrideTankName) {
    // Fast path: da dang ky roi
    if (_sessions.getPlayerID(addr, outPid)) return true;

    // Tim slotId tuong ung voi token nay
    uint32_t matchedPid = 0;
    for (auto& [pid, tok] : _config.playerTokens) {
        if (tok == token) { matchedPid = pid; break; }
    }
    if (matchedPid == 0) return false; // token khong hop le

    // Kiem tra slot chua bi chiem boi session khac
    {
        std::lock_guard lock(_slotMutex);
        if (_sessions.hasPlayerID(matchedPid)) {
            // Slot da duoc assign — co the la reconnect tu cung player
            // Cho phep neu cung token
            outPid = matchedPid;
            _sessions.updateAddress(matchedPid, addr);
            return true;
        }
        // Assign slot nay cho addr nay
        size_t slot = 0;
        for (size_t i = 0; i < _config.playerIds.size(); i++) {
            if (_config.playerIds[i] == matchedPid) { slot = i; break; }
        }
        outPid = matchedPid;
        _sessions.addSession(matchedPid, "player" + std::to_string(matchedPid), addr);

        Vector3 spawn = _world.getSpawnPosition(slot);
        TankStats stats = _config.playerStats.count(matchedPid) ? _config.playerStats.at(matchedPid) : TankStats{};
        if (!overrideTankName.empty()) stats.name = overrideTankName;
        _world.addPlayer(matchedPid, spawn, stats);

        LOG_INFO("Match {}: player {} authenticated (token={:.8}...) → spawn ({:.1f},{:.1f},{:.1f})",
                 _config.matchId, matchedPid, token, spawn.x, spawn.y, spawn.z);
    }
    return true;
}

bool Match::resolvePlayer(const sockaddr_in& addr, uint32_t& outPid, const std::string& overrideTankName) {
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
    TankStats stats = _config.playerStats.count(outPid) ? _config.playerStats[outPid] : TankStats{};
    if (!overrideTankName.empty()) {
        stats.name = overrideTankName;
    }

    _world.addPlayer(outPid, spawn, stats);

    LOG_INFO("Match {}: player {} joined from {}:{} → spawn ({:.1f},{:.1f},{:.1f})",
             _config.matchId, outPid,
             ntohl(addr.sin_addr.s_addr), ntohs(addr.sin_port),
             spawn.x, spawn.y, spawn.z);
    return true;
}

// ────────────────────────────────────────────────────────────────────────────

void Match::handleMove(GameCommand& cmd) {
    if (_introStarted && _introTimer < 5.5f) return; // Chặn di chuyển trong intro

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

    // Prepare leaderboard array
    std::vector<MatchEndPlayer> players;
    for (uint32_t pid : _config.playerIds) {
        MatchEndPlayer p;
        p.tankId = pid;
        p.rpReward = r.rpRewards.count(pid) ? r.rpRewards.at(pid) : 0;
        p.kills = r.kills.count(pid) ? r.kills.at(pid) : 0;
        
        std::string uidStr = r.userIds.count(pid) ? r.userIds.at(pid) : "";
        std::strncpy(p.userId, uidStr.c_str(), sizeof(p.userId) - 1);
        p.userId[sizeof(p.userId) - 1] = '\0';
        
        players.push_back(p);
    }
    
    // Sort by RP descending
    std::sort(players.begin(), players.end(), [](const MatchEndPlayer& a, const MatchEndPlayer& b) {
        return a.rpReward > b.rpReward;
    });

    pkt.playerCount = static_cast<uint8_t>(players.size());
    size_t totalSize = sizeof(MatchEndPacket) + players.size() * sizeof(MatchEndPlayer);
    std::vector<uint8_t> buffer(totalSize);
    std::memcpy(buffer.data() + sizeof(MatchEndPacket), players.data(), players.size() * sizeof(MatchEndPlayer));

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
        
        auto rpIt = r.rpRewards.find(pid);
        pkt.rpReward = static_cast<int16_t>(rpIt != r.rpRewards.end() ? rpIt->second : 0);
        
        auto pIt = r.placements.find(pid);
        pkt.placement = static_cast<uint8_t>(pIt != r.placements.end() ? pIt->second : 0);

        std::memcpy(buffer.data(), &pkt, sizeof(MatchEndPacket));
        _network.send(addr, buffer.data(), buffer.size());
    }

    LOG_INFO("Match {}: S2C_MATCH_END broadcast ({} recipients, {} players in leaderboard)",
             r.matchId, _sessions.size(), players.size());
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
    if (_introStarted && _introTimer < 5.5f) return; // Chặn bắn trong intro

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
