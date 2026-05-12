#pragma once
#include <vector>
#include <thread>
#include <atomic>
#include <cstdint>
#include "VirtualPlayer.hpp"
#include "Config.hpp"
#include "Metrics.hpp"

class WorkerThread {
public:
    WorkerThread(int workerId,
                 int firstClientId, int clientCount,
                 const Config& cfg,
                 const sockaddr_in& serverAddr,
                 Metrics& metrics);

    void start();
    void stop();
    void join();

private:
    void run();

    int         _workerId;
    int         _firstId;
    int         _count;
    const Config&   _cfg;
    sockaddr_in     _serverAddr;
    Metrics&        _metrics;

    std::vector<VirtualPlayer> _players;
    std::thread                _thread;
    std::atomic<bool>          _running{false};
};
