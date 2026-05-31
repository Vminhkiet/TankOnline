#pragma once
#include <cstdint>
#include <winsock2.h>
#include <string>
#include <chrono>
#include <unordered_map>
#include <vector>

struct Session {
	uint32_t playerID;
	std::string name;
	sockaddr_in ip;
	std::chrono::steady_clock::time_point lastActiveTime;
};

class SessionManager {
private:
	std::unordered_map<uint32_t, uint64_t> _playerToAddr;
	std::unordered_map<uint64_t, Session> _sessionByAddr;
public:
	bool getPlayerID(const sockaddr_in& addr, uint32_t& outPlayerID) const;
	bool getAddress(uint32_t playerID, sockaddr_in& outAddr) const;
	bool addSession(uint32_t playerID, const std::string& name, const sockaddr_in& addr);
	void removeSession(uint32_t playerID);
	bool updateHeartbeat(const sockaddr_in& addr);
	std::vector<uint32_t> collectTimeouts(int timeoutSeconds);
	size_t size() const { return _playerToAddr.size(); }

	// Kiem tra slotId da co session chua (dung trong token auth)
	bool hasPlayerID(uint32_t playerID) const { return _playerToAddr.count(playerID) > 0; }
	// Cap nhat dia chi khi player reconnect
	void updateAddress(uint32_t playerID, const sockaddr_in& newAddr);
};