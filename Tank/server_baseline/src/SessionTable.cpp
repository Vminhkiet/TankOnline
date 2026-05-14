#include "SessionTable.hpp"
#include "Utils/Logger.hpp"

bool SessionTable::getPlayerId(const sockaddr_in& addr, uint32_t& outPid) const {
    auto it = _byAddr.find(key(addr));
    if (it == _byAddr.end()) return false;
    outPid = it->second.playerId;
    return true;
}

bool SessionTable::getAddress(uint32_t playerId, sockaddr_in& outAddr) const {
    auto it = _byId.find(playerId);
    if (it == _byId.end()) return false;
    auto it2 = _byAddr.find(it->second);
    if (it2 == _byAddr.end()) return false;
    outAddr = it2->second.addr;
    return true;
}

bool SessionTable::resolve(const sockaddr_in& addr,
                           const std::vector<uint32_t>& playerIds,
                           uint32_t& outPid) {
    if (getPlayerId(addr, outPid)) return true;

    if (_nextSlot >= static_cast<uint32_t>(playerIds.size())) return false;

    outPid = playerIds[_nextSlot++];
    uint64_t k = key(addr);

    PlayerSession s{};
    s.playerId = outPid;
    s.addr     = addr;
    s.lastSeen = std::chrono::steady_clock::now();

    _byAddr[k]      = s;
    _byId[outPid]   = k;

    LOG_INFO("[Session] player {} assigned from {}:{}",
             outPid, ntohl(addr.sin_addr.s_addr), ntohs(addr.sin_port));
    return true;
}

void SessionTable::touch(const sockaddr_in& addr) {
    auto it = _byAddr.find(key(addr));
    if (it != _byAddr.end())
        it->second.lastSeen = std::chrono::steady_clock::now();
}

void SessionTable::remove(uint32_t playerId) {
    auto it = _byId.find(playerId);
    if (it == _byId.end()) return;
    _byAddr.erase(it->second);
    _byId.erase(it);
}

std::vector<uint32_t> SessionTable::collectTimeouts(float timeoutSecs) const {
    auto now = std::chrono::steady_clock::now();
    std::vector<uint32_t> out;
    for (auto& [k, s] : _byAddr) {
        float elapsed = std::chrono::duration<float>(now - s.lastSeen).count();
        if (elapsed > timeoutSecs)
            out.push_back(s.playerId);
    }
    return out;
}
