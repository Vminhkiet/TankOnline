#pragma once
#include <winsock2.h>
#include <unordered_map>
#include <vector>
#include <chrono>
#include <cstdint>

struct PlayerSession {
    uint32_t  playerId;
    sockaddr_in addr;
    std::chrono::steady_clock::time_point lastSeen;
};

// Flat bidirectional table: addr <-> playerId.
// No locks — all access is from the single main thread.
class SessionTable {
public:
    // If addr is already registered, fill outPid and return true.
    // Otherwise assign the next free slot from playerIds and return true.
    // Returns false only when all slots are taken and addr is unknown.
    bool resolve(const sockaddr_in& addr,
                 const std::vector<uint32_t>& playerIds,
                 uint32_t& outPid);

    bool getPlayerId(const sockaddr_in& addr, uint32_t& outPid) const;
    bool getAddress (uint32_t playerId, sockaddr_in& outAddr)  const;

    void touch (const sockaddr_in& addr); // refresh heartbeat
    void remove(uint32_t playerId);

    // Returns players whose lastSeen > timeoutSecs ago.
    std::vector<uint32_t> collectTimeouts(float timeoutSecs) const;

    size_t size() const { return _byId.size(); }

private:
    static uint64_t key(const sockaddr_in& a) {
        return (static_cast<uint64_t>(a.sin_addr.s_addr) << 16) | a.sin_port;
    }

    std::unordered_map<uint64_t, PlayerSession> _byAddr;
    std::unordered_map<uint32_t, uint64_t>      _byId;   // playerId -> addr key
    uint32_t _nextSlot = 0;
};
