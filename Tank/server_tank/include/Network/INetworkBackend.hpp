#pragma once
#include <functional>
#include <cstdint>
#include <winsock2.h>
#include "Network/GameCommand.hpp"

// Abstract network backend — the only contract MatchManager and Match care about.
// Implementations: IocpBackend (Windows IOCP) and BlockingBackend (blocking recvfrom).
class INetworkBackend {
public:
    virtual ~INetworkBackend() = default;

    // Bind to port and start I/O. Returns false on failure.
    virtual bool start(int port) = 0;

    // Drain all pending I/O and join worker threads.
    virtual void stop() = 0;

    // Send a UDP datagram. Thread-safe.
    virtual void send(const sockaddr_in& target, const uint8_t* data, size_t len) = 0;

    // Register the routing callback invoked for every decoded packet.
    // Must be called before start(). Called from worker threads — must be thread-safe.
    virtual void setRouteCallback(std::function<void(GameCommand)> cb) = 0;

    virtual const char* backendName() const = 0;

    // Atomically drain accumulated recv-parse stats since last call.
    // sumUs = total microseconds spent parsing packets, count = number of packets.
    // Default no-op so other backends don't need to implement it.
    virtual void drainRecvStats(uint64_t& sumUs, uint32_t& count) { sumUs = 0; count = 0; }
};
