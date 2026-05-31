#include "Network/SessionManager.hpp"

bool SessionManager::getPlayerID(const sockaddr_in& addr, uint32_t& outPlayerID) const {
	uint64_t key =
		((uint64_t)ntohl(addr.sin_addr.s_addr) << 16)
		| ntohs(addr.sin_port);
	auto it = _sessionByAddr.find(key);
	if (it == _sessionByAddr.end())
		return false;

	outPlayerID = it->second.playerID;
	return true;
}

bool SessionManager::getAddress(uint32_t playerID, sockaddr_in& outAddr) const {
	auto it = _playerToAddr.find(playerID);
	if (it == _playerToAddr.end())
		return false;
	uint64_t key = it->second;
	uint32_t ip = (uint32_t)(key >> 16);
	uint16_t port = (uint16_t)(key & 0xFFFF);
	outAddr = {};
	outAddr.sin_family = AF_INET;
	outAddr.sin_port = htons(port);
	outAddr.sin_addr.s_addr = htonl(ip);
	
	return true;
}

bool SessionManager::addSession(uint32_t playerID, const std::string& name, const sockaddr_in& addr) {
	auto it = _playerToAddr.find(playerID);
	if (it != _playerToAddr.end())
		return false;
	
	uint64_t key = ((uint64_t)ntohl(addr.sin_addr.s_addr) << 16) | ntohs(addr.sin_port);

	Session insert;
	insert.ip = addr;
	insert.name = name;
	insert.playerID = playerID;
	insert.lastActiveTime = std::chrono::steady_clock::now();

	_playerToAddr[playerID] = key;
	_sessionByAddr[key] = insert;

	return true;
}

void SessionManager::removeSession(uint32_t playerID) {
	auto it = _playerToAddr.find(playerID);
	if (it == _playerToAddr.end())
		return;
	uint64_t key = it->second;
	_sessionByAddr.erase(key);
	_playerToAddr.erase(playerID);
}

void SessionManager::updateAddress(uint32_t playerID, const sockaddr_in& newAddr) {
	auto it = _playerToAddr.find(playerID);
	if (it == _playerToAddr.end()) return;
	// Xoa key cu
	_sessionByAddr.erase(it->second);
	// Them key moi
	uint64_t newKey = ((uint64_t)ntohl(newAddr.sin_addr.s_addr) << 16) | ntohs(newAddr.sin_port);
	Session& s = _sessionByAddr[newKey];
	s.playerID = playerID;
	s.ip       = newAddr;
	s.lastActiveTime = std::chrono::steady_clock::now();
	it->second = newKey;
}

bool SessionManager::updateHeartbeat(const sockaddr_in& addr) {
	uint64_t key = ((uint64_t)ntohl(addr.sin_addr.s_addr) << 16) | ntohs(addr.sin_port);
	
	auto it = _sessionByAddr.find(key);
	if (it == _sessionByAddr.end())
		return false;

	it->second.lastActiveTime = std::chrono::steady_clock::now();
	return true;
}

std::vector<uint32_t> SessionManager::collectTimeouts(int timeoutSeconds) {
	std::vector<uint32_t> results;
	auto now = std::chrono::steady_clock::now();

	for (const auto& it : _sessionByAddr)
	{
		if (now - it.second.lastActiveTime >= std::chrono::seconds(timeoutSeconds))
		{
			results.push_back(it.second.playerID);
		}
	}
	return results;
}