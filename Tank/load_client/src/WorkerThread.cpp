#include "WorkerThread.hpp"
#include <chrono>
#include <cstdio>

using Clock  = std::chrono::steady_clock;
using Micros = std::chrono::microseconds;

WorkerThread::WorkerThread(int workerId,
                            int firstClientId, int clientCount,
                            const Config& cfg,
                            const sockaddr_in& serverAddr,
                            Metrics& metrics)
    : _workerId(workerId)
    , _firstId(firstClientId)
    , _count(clientCount)
    , _cfg(cfg)
    , _serverAddr(serverAddr)
    , _metrics(&metrics)
{
    _players.reserve(clientCount);
    for (int i = 0; i < clientCount; ++i)
        _players.emplace_back(firstClientId + i, serverAddr, _metrics, cfg.matchId);
}

void WorkerThread::start()
{
    _running = true;
    _thread  = std::thread(&WorkerThread::run, this);
}

void WorkerThread::stop()  { _running = false; }
void WorkerThread::join()  { if (_thread.joinable()) _thread.join(); }

void WorkerThread::run()
{
    // Open sockets and login all players
    int opened = 0;
    for (auto& p : _players) {
        if (p.init()) { p.sendLogin(); ++opened; }
    }
    if (_cfg.verbose)
        printf("[Worker %d] %d/%d players initialised\n", _workerId, opened, _count);

    // Fixed tick interval in microseconds
    const Micros tickInterval{ 1'000'000 / (_cfg.tickRate > 0 ? _cfg.tickRate : 20) };

    auto nextTick = Clock::now();

    while (_running) {
        auto now = Clock::now();
        if (now < nextTick) {
            std::this_thread::sleep_until(nextTick);
            now = Clock::now();
        }
        nextTick = now + tickInterval;

        // Drive one tick for every player managed by this thread
        for (auto& p : _players)
            p.tick(_cfg.shootChance);
    }

    if (_cfg.verbose)
        printf("[Worker %d] stopped\n", _workerId);
}
