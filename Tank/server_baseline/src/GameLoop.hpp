#pragma once
#include "UdpServer.hpp"
#include "SessionTable.hpp"
#include "World/GameWorld.hpp"
#include "Core/MatchConfig.hpp"
#include <vector>
#include <cstdint>

// Wraps GameWorld + SessionTable + snapshot broadcast.
// Packet dispatch is a plain switch(opcode) — no dispatcher map.
class GameLoop {
public:
    GameLoop(UdpServer& udp, MatchConfig cfg);

    bool init(); // load map, disableRespawn

    // Called for every UDP datagram received this tick.
    void handlePacket(const uint8_t* buf, int len, const sockaddr_in& from);

    // Advance simulation by dt seconds.
    void tick(float dt);

    // Send one snapshot packet per connected player (20 Hz caller's responsibility).
    void broadcastSnapshot();

    SessionTable& sessions() { return _sessions; }

private:
    UdpServer&   _udp;
    MatchConfig  _cfg;
    GameWorld    _world;
    SessionTable _sessions;
    uint16_t     _serverTick = 0;

    // Returns true if player was already known or successfully assigned a slot.
    bool resolvePlayer(const sockaddr_in& from, uint32_t& outPid);

    void handleLogin(const sockaddr_in& from);
    void handleMove (const uint8_t* buf, int len, const sockaddr_in& from);
    void handleShoot(const sockaddr_in& from);
};
