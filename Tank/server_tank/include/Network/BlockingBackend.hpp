#pragma once
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <vector>
#include <thread>
#include <atomic>
#include <functional>
#include "Network/INetworkBackend.hpp"
#include "Memory/BufferPool.hpp"

// Blocking-recvfrom backend — apples-to-apples baseline for IOCP comparison.
//
// Architecture:
//   numReceivers OS threads each call blocking recvfrom() on the shared socket.
//   Only one thread wakes per datagram (OS arbitrates). The woken thread decodes
//   the packet and calls routeCb — identical to what an IOCP worker does.
//
// Variable isolated:  how the OS delivers received bytes to userspace.
// Everything else (BufferPool, ThreadPool, Match, MatchScheduler) is shared code.
class BlockingBackend : public INetworkBackend {
public:
    // numReceivers: dedicated blocking-recv threads (default 2).
    explicit BlockingBackend(int numReceivers = 2);
    ~BlockingBackend() override { stop(); }

    bool        start(int port)                                   override;
    void        stop()                                            override;
    void        send(const sockaddr_in& target,
                     const uint8_t* data, size_t len)             override;
    void        setRouteCallback(std::function<void(GameCommand)>) override;
    const char* backendName() const                               override { return "Blocking-recvfrom"; }
    void        drainRecvStats(uint64_t& sumUs, uint32_t& count)  override;

private:
    void recvLoop();

    SOCKET                           _sock        = INVALID_SOCKET;
    std::atomic<bool>                _running     {false};
    int                              _numReceivers;
    std::vector<std::thread>         _recvThreads;
    BufferPool                       _pool;
    std::function<void(GameCommand)> _routeCb;

    // Recv-parse profiling — accumulated by recvLoop threads, drained by MatchScheduler [Perf]
    std::atomic<uint64_t> _accumRecvParseUs{0};
    std::atomic<uint32_t> _recvCount{0};
};
