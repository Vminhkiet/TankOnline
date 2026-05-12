#pragma once
#include <unordered_map>
#include <memory>
#include <shared_mutex>
#include <thread>
#include <atomic>
#include <string>
#include "Core/Match.hpp"
#include "Core/MatchConfig.hpp"
#include "Kafka/KafkaProducer.hpp"
#include "Utils/ThreadPool.hpp"
#include "Network/NetworkManager.hpp"

class MatchManager {
public:
    static constexpr int MAX_CONCURRENT_MATCHES = 64;
    static constexpr int TICK_RATE_HZ           = 60;

    explicit MatchManager(NetworkManager& network);
    ~MatchManager();

    void start(const std::string& kafkaBrokers);
    void stop();

    // Called from Kafka consumer (main thread).
    void createMatch(MatchConfig config);

    // Called from NetworkManager IOCP workers (multiple threads).
    void routeCommand(GameCommand cmd);

private:
    void tickDispatcher();
    void onMatchEnd(MatchResult r);

    std::unordered_map<uint32_t, std::unique_ptr<Match>> _matches;
    std::shared_mutex  _matchesMutex;

    ThreadPool         _pool;
    std::jthread       _tickThread;
    KafkaProducer      _producer;
    NetworkManager&    _network;
    std::atomic<bool>  _running{false};

    // Wall-clock time of the previous tick start — used to compute measured dt
    std::chrono::steady_clock::time_point _lastTickStart{};
    bool _firstTick = true;

    // Rolling stats (reset every STATS_INTERVAL_TICKS ticks)
    static constexpr int STATS_INTERVAL_TICKS = 600; // 10s at 60Hz
    int      _statTicks     = 0;
    int64_t  _statSumUs     = 0;   // total tick dispatch time
    int64_t  _statMaxUs     = 0;   // worst tick
    int64_t  _statMinUs     = INT64_MAX;
    int      _statOverruns  = 0;
};
