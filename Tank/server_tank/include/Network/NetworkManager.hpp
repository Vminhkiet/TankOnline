#pragma once
#include "Network/INetworkBackend.hpp"
#include "Network/Packets.hpp"
#include "Network/GameCommand.hpp"
#include "Network/Opcode.hpp"
#include "Memory/IoContext.hpp"
#include "Memory/BufferPool.hpp"
#include "Utils/Logger.hpp"
#include "ReadStream.h"
#include <vector>
#include <functional>
#include <winsock2.h>
#include <cstdint>
#include <mutex>
#include <atomic>
#include <thread>
#include <iostream>

class NetworkManager : public INetworkBackend {
public:
    explicit NetworkManager(size_t threadCount = 0);
    ~NetworkManager() override;

    bool        start(int port)                                   override;
    void        stop()                                            override;
    void        send(const sockaddr_in& target,
                     const uint8_t* data, size_t len)             override;
    void        setRouteCallback(std::function<void(GameCommand)> cb) override { _routeCb = std::move(cb); }
    const char* backendName() const                               override { return "IOCP"; }

private:
    void workerThread();
    void handleReader(IoContext* iocontext, DWORD lengthBuf);
    bool postReceive(IoContext* ctx);

    SOCKET _serverSocket = INVALID_SOCKET;
    HANDLE _hCompPort    = NULL;
    std::vector<std::thread> _workers;
    std::atomic<bool>        _running{false};
    BufferPool               _pool;
    std::function<void(GameCommand)> _routeCb;
};
