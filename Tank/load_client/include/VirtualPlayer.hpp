#pragma once
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <cstdint>
#include <chrono>
#include "UdpSocket.hpp"
#include "Metrics.hpp"
#include "PacketBuilder.hpp"

class VirtualPlayer {
public:
    VirtualPlayer(uint32_t id, const sockaddr_in& serverAddr, Metrics* metrics,
                  uint32_t matchId = 1);

    // Open socket; returns false on failure
    bool init();

    // Send LOGIN once, then returns
    void sendLogin();

    // Called each simulated tick:
    //   - sends MOVE with random direction
    //   - sends SHOOT if rand() < shootChance
    //   - drains recv buffer and records RTT latency
    void tick(float shootChance);

private:
    uint32_t    _id;
    sockaddr_in _server;
    Metrics*    _metrics;
    UdpSocket   _sock;

    uint32_t _matchId = 1;
    uint8_t  _seq  = 0;
    uint16_t _tick = 0;
    bool     _loggedIn = false;

    // For RTT: store the send timestamp in the seq slot
    // We use a simple ring of 256 send timestamps keyed by seq
    using Clock     = std::chrono::steady_clock;
    using TimePoint = std::chrono::time_point<Clock>;
    TimePoint _sendTimes[256];

    uint8_t  _buf[PacketBuilder::BUF_SIZE];
    uint8_t  _recvBuf[512];
};
