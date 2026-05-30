#define NOMINMAX
#include "Core/MatchScheduler.hpp"
#include "Utils/Logger.hpp"
#include <chrono>
#include <climits>
#include <vector>
#include <future>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

static size_t resolvePoolSize() {
    const char* v = std::getenv("NUM_WORKERS");
    if (v && std::atoi(v) > 0) return static_cast<size_t>(std::atoi(v));
#ifdef PROFILING_SINGLE_CORE
    return 1;   // PROFILING: pin to 1 worker → isolate GPC limit per core
#else
    return std::thread::hardware_concurrency();
#endif
}

MatchScheduler::MatchScheduler(INetworkBackend& network)
    : _pool(resolvePoolSize())
    , _network(network)
{}

MatchScheduler::~MatchScheduler() { stop(); }

void MatchScheduler::start(const std::string& kafkaBrokers) {
    _network.setRouteCallback([this](GameCommand cmd) { routeCommand(std::move(cmd)); });

    if (!kafkaBrokers.empty())
        _producer.connect(kafkaBrokers);

    _running = true;
    _tickThread = std::jthread([this](std::stop_token st) {
        while (!st.stop_requested()) tickDispatcher();
    });

    LOG_INFO("MatchScheduler: started ({} pool workers)", _pool.size());
}

void MatchScheduler::stop() {
    _running = false;
    _tickThread.request_stop();
    if (_tickThread.joinable()) _tickThread.join();
    _producer.flush();
    LOG_INFO("MatchScheduler: stopped");
}

// ────────────────────────────────────────────────────────────────────────────
// Tick dispatcher — runs in dedicated jthread at 60 Hz
// Submits each active match's tick() to the thread pool and waits for all.
// ────────────────────────────────────────────────────────────────────────────

void MatchScheduler::tickDispatcher() {
    using Clock  = std::chrono::steady_clock;
    using Micros = std::chrono::microseconds;

    constexpr Micros  tickBudget  { 1'000'000 / TICK_RATE_HZ };
    constexpr int64_t kBudgetUs   = 1'000'000 / TICK_RATE_HZ;
    constexpr float   kDtMin      = 0.001f;   // 1 ms floor (avoid div-by-zero)
    constexpr float   kDtMax      = 0.05f;    // 50 ms ceiling (avoid physics explosion on lag)

    auto tickStart = Clock::now();

    // Measure actual elapsed time since last tick so the simulation advances
    // by real wall-clock time — this cancels out Windows sleep imprecision
    // (sleep_for has ~15 ms resolution, causing the server to run at ~64 Hz
    // with a fixed dt=1/60, drifting +6.7% vs the 60 Hz Unity FixedUpdate).
    float dt;
    if (_firstTick) {
        dt = 1.f / TICK_RATE_HZ;   // safe default on very first tick
        _firstTick = false;
    } else {
        dt = std::chrono::duration<float>(tickStart - _lastTickStart).count();
        dt = std::clamp(dt, kDtMin, kDtMax);
    }
    _lastTickStart = tickStart;

    // ── 1. Snapshot active matches ───────────────────────────────────────────
    std::vector<Match*> active;
    {
        std::shared_lock lock(_matchesMutex);
        active.reserve(_matches.size());
        for (auto& [id, m] : _matches)
            if (m->isRunning()) active.push_back(m.get());
    }

    // ── 2. Submit ticks to thread pool, barrier via futures ──────────────────
    std::vector<std::future<void>> futures;
    futures.reserve(active.size());
    
    // Calculate strict budget: ~16.6ms / (matches per worker thread)
    int64_t availableBudgetUs = 16000; 
    int matchesPerWorker = std::max(1, static_cast<int>(active.size() / std::max(size_t(1), _pool.size())));
    int64_t budgetPerMatchUs = availableBudgetUs / matchesPerWorker;

    for (Match* m : active) {
        futures.push_back(_pool.submit([m, dt, budgetPerMatchUs, this] { 
            auto runStart = std::chrono::steady_clock::now();
            
            // Execute the match tick with strict budget
            m->tick(dt, budgetPerMatchUs, _metrics);
            
            auto runEnd = std::chrono::steady_clock::now();
            int64_t elUs = std::chrono::duration_cast<std::chrono::microseconds>(runEnd - runStart).count();
            _metrics.recordTick(elUs);
        }));
    }

    for (auto& f : futures) f.get();  // wait for all ticks this frame

    // ── 3. Cleanup finished matches ──────────────────────────────────────────
    {
        std::unique_lock lock(_matchesMutex);
        for (auto it = _matches.begin(); it != _matches.end(); )
            it = it->second->isRunning() ? std::next(it) : _matches.erase(it);
    }

    // ── 4. Measure tick wall-time ────────────────────────────────────────────
    auto elapsed  = Clock::now() - tickStart;
    int64_t elUs  = std::chrono::duration_cast<Micros>(elapsed).count();

    // ── 5. Print rolling stats every STATS_INTERVAL_TICKS ticks ─────────────
    static int dispatchTicks = 0;
    if (++dispatchTicks >= STATS_INTERVAL_TICKS) {
        double avgUs = _metrics.getTicks() > 0 ? static_cast<double>(_metrics.getSumUs()) / _metrics.getTicks() : 0.0;
        double overrunPct = _metrics.getTicks() > 0 ? 100.0 * _metrics.getOverruns() / _metrics.getTicks() : 0.0;

        uint64_t recvSumUs = 0; uint32_t recvCount = 0;
        _network.drainRecvStats(recvSumUs, recvCount);
        double recvAvgUs = recvCount > 0 ? static_cast<double>(recvSumUs) / recvCount : 0.0;

        LOG_INFO(
            "[Perf] dispatch_ticks={} matches={} pool_pending={} | "
            "match_tick avg={:.0f}µs min={}µs max={}µs | overruns={} ({:.1f}%) | "
            "recv_parse avg={:.1f}µs pkts={}",
            dispatchTicks,
            active.size(),
            _pool.pendingCount(),
            avgUs, 
            _metrics.getMinUs() == INT64_MAX ? 0 : _metrics.getMinUs(), 
            _metrics.getMaxUs(),
            _metrics.getOverruns(), overrunPct,
            recvAvgUs, recvCount
        );

        for (Match* m : active) {
            LOG_INFO("  match {} — {}/{} players connected",
                     m->id(), m->connectedPlayers(), m->totalSlots());
            m->logPositions();
        }
        dispatchTicks = 0;
        _metrics.reset();
    }

    // ── 6. Sleep for remainder of tick budget ────────────────────────────────
    auto sleepFor = tickBudget - std::chrono::duration_cast<Micros>(Clock::now() - tickStart);
    if (sleepFor > Micros{0})
        std::this_thread::sleep_for(sleepFor);
}

// ────────────────────────────────────────────────────────────────────────────

void MatchScheduler::createMatch(MatchConfig config) {
    {
        std::shared_lock lock(_matchesMutex);
        if (_matches.size() >= MAX_CONCURRENT_MATCHES) {
            LOG_WARN("MatchScheduler: at capacity ({} matches), rejecting matchId={}",
                     MAX_CONCURRENT_MATCHES, config.matchId);
            return;
        }
    }

    auto match = std::make_unique<Match>(config, _network, [this](MatchResult r) { onMatchEnd(r); });

    {
        std::unique_lock lock(_matchesMutex);
        _matches.emplace(config.matchId, std::move(match));
    }

    LOG_INFO("MatchScheduler: match {} created (total active: {})",
             config.matchId, _matches.size());

    // Publish match.ready to Kafka to acknowledge MatchmakingService
    json j;
    j["matchId"] = config.matchId;
    if (_producer.publish("match.ready", j.dump())) {
        LOG_INFO("MatchScheduler: match {} ready signal published to Kafka", config.matchId);
    } else {
        LOG_WARN("MatchScheduler: match {} ready signal not published (Kafka unavailable)", config.matchId);
    }
}

void MatchScheduler::routeCommand(GameCommand cmd) {
    std::shared_lock lock(_matchesMutex);
    auto it = _matches.find(cmd.matchId);
    if (it != _matches.end() && it->second->isRunning()) {
        it->second->pushCommand(std::move(cmd));
    } else {
        LOG_WARN("MatchScheduler: no running match for matchId={} (known matches: {})",
                 cmd.matchId, _matches.size());
    }
}

bool MatchScheduler::forceLogoutByUserId(const std::string& userId, uint16_t code,
                                       const std::string& message, uint32_t disconnectAfterMs) {
    std::shared_lock lock(_matchesMutex);
    bool kicked = false;
    for (auto& [id, m] : _matches) {
        if (!m || !m->isRunning()) continue;
        if (m->forceLogoutByUserId(userId, code, message, disconnectAfterMs)) {
            LOG_INFO("MatchScheduler: duplicate-login kick routed to match {} for userId={}", id, userId);
            kicked = true;
            break;
        }
    }
    if (!kicked) {
        LOG_WARN("MatchScheduler: duplicate-login event for userId={} but no active session found", userId);
    }
    return kicked;
}

void MatchScheduler::onMatchEnd(MatchResult r) {
    static constexpr const char* kOutcomeStr[] = { "running", "win", "draw", "timeout" };
    const char* outcomeStr = kOutcomeStr[std::min(static_cast<int>(r.outcome), 3)];

    LOG_INFO("MatchScheduler: match {} ended (outcome={}, winner={}, dur={:.1f}s)",
             r.matchId, outcomeStr, r.winnerId, r.durationSecs);

    // RP is already computed in Match.cpp checkOutcome
    
    json j;
    j["matchId"]      = r.matchId;
    j["outcome"]      = outcomeStr;
    j["winnerId"]     = r.winnerId;
    j["durationSecs"] = r.durationSecs;
    j["mapName"]      = r.mapName;
    
    j["stats"] = json::object();
    for (auto& [pid, uid] : r.userIds) {
        json pStat;
        pStat["rp_reward"] = r.rpRewards[pid];
        pStat["match_score"] = r.matchScores[pid];
        pStat["placement"] = r.placements[pid];
        j["stats"][std::to_string(pid)] = pStat;
    }

    j["kills"]        = json::object();
    for (auto& [pid, k] : r.kills)
        j["kills"][std::to_string(pid)] = k;
    j["deaths"]       = json::object();
    for (auto& [pid, d] : r.deaths)
        j["deaths"][std::to_string(pid)] = d;
    j["userIds"]      = json::object();
    for (auto& [pid, uid] : r.userIds)
        j["userIds"][std::to_string(pid)] = uid;

    if (_producer.publish("match.result", j.dump()))
        LOG_INFO("MatchScheduler: match {} result published to Kafka", r.matchId);
    else
        LOG_WARN("MatchScheduler: match {} result not published (Kafka unavailable)", r.matchId);
}
