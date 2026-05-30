/*
 * bench_32match.cpp
 *
 * 32 matches running concurrently, 3-phase timing:
 *   t0 → t1  : updateBullets  (Bullet phase)
 *   t1 → t2  : PhysicsWorld::Tick (Physics phase)
 *   t2 → t3  : broadcastSnapshot (Snap phase)
 *
 * CPU isolation:
 *   - Process affinity mask 0x3FC → cores 2-9 (8 logical cores)
 *   - REALTIME_PRIORITY_CLASS + THREAD_PRIORITY_TIME_CRITICAL on main thread
 *   - ThreadPool(8 workers, startCore=2) → each worker pinned to one of cores 2-9
 *
 * No Kafka, no real UDP — NullNetworkBackend (all methods are no-ops).
 *
 * Map: bench_spread.json (~590 steady-state bullets per match after warmup).
 *
 * Log lines (emitted every LOG_INTERVAL ticks, unbuffered):
 *   [M32Perf]  ticks=N matches=32 workers=8 mask=0x3FC
 *              | wallclock avg=Xus p95=Xus p99=Xus max=Xus | overruns=N
 *   [M32Phase] bullet avg=Xus p95=Xus p99=Xus max=Xus
 *              | physics avg=Xus p95=Xus p99=Xus max=Xus
 *              | snap avg=Xus p95=Xus p99=Xus max=Xus
 *   [M32Match] id=M bullet=Xus physics=Xus snap=Xus total=Xus   (×32 lines)
 *
 * Build:  cmake --build <build_dir> --target bench_32match --config Release
 * Run:    bench_32match.exe > bench_32match.log 2>&1
 *         python bench_32match_agent.py bench_32match.log
 */

#include "Core/Match.hpp"
#include "Network/INetworkBackend.hpp"
#include "Network/GameCommand.hpp"
#include "Network/Opcode.hpp"
#include "Network/Packets.hpp"
#include "Network/NetworkConstants.h"
#include "Core/MatchConfig.hpp"
#include "Utils/Logger.hpp"
#include "Utils/ThreadPool.hpp"
#include "WriteStream.h"

#include <winsock2.h>
#include <windows.h>
#include <chrono>
#include <vector>
#include <algorithm>
#include <cstdio>
#include <cstdint>
#include <string>
#include <functional>
#include <future>
#include <atomic>
#include <memory>

// ─── Graceful shutdown ────────────────────────────────────────────────────────
static std::atomic<bool> g_running{true};

static BOOL WINAPI ctrlHandler(DWORD type)
{
    if (type == CTRL_C_EVENT || type == CTRL_BREAK_EVENT) {
        g_running.store(false, std::memory_order_relaxed);
        return TRUE;
    }
    return FALSE;
}

// ─── CPU isolation ────────────────────────────────────────────────────────────
static void pinCores(DWORD_PTR mask)
{
    if (!SetProcessAffinityMask(GetCurrentProcess(), mask))
        fprintf(stderr, "[M32] SetProcessAffinityMask failed (err=%lu)\n", GetLastError());

    if (!SetPriorityClass(GetCurrentProcess(), REALTIME_PRIORITY_CLASS))
        SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);

    int nCores = 0;
    for (DWORD_PTR m = mask; m; m >>= 1) nCores += static_cast<int>(m & 1);

    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    printf("[M32] Pinned to %d cores (mask=0x%03llX) / %d total, REALTIME/TIME_CRITICAL\n",
           nCores, static_cast<unsigned long long>(mask),
           static_cast<int>(si.dwNumberOfProcessors));
    fflush(stdout);
}

// ─── NullNetworkBackend ───────────────────────────────────────────────────────
class NullNetworkBackend : public INetworkBackend {
public:
    bool start(int) override { return true; }
    void stop()     override {}
    void send(const sockaddr_in&, const uint8_t*, size_t) override {}
    void setRouteCallback(std::function<void(GameCommand)>) override {}
    const char* backendName() const override { return "null"; }
};

// ─── Packet builders ──────────────────────────────────────────────────────────
static std::vector<uint8_t> buildMoveStationary(uint32_t matchId, uint8_t seq)
{
    uint8_t buf[12] = {};
    WriteStream ws(reinterpret_cast<uint32_t*>(buf), 3);
    PacketHeader hdr;
    hdr.size = 12; hdr.opcode = Opcode::C2S_MOVE;
    hdr.matchId = matchId; hdr.flags = 0; hdr.seq = seq; hdr.tick = 0;
    hdr.Serialize(ws);
    PacketMovement mv; mv.dirX = 1; mv.dirZ = 1; mv.speed = 0;
    mv.Serialize(ws);
    return std::vector<uint8_t>(buf, buf + 12);
}

static std::vector<uint8_t> buildShoot(uint32_t matchId, uint8_t seq)
{
    uint8_t buf[12] = {};
    WriteStream ws(reinterpret_cast<uint32_t*>(buf), 3);
    PacketHeader hdr;
    hdr.size = 12; hdr.opcode = Opcode::C2S_SHOOT;
    hdr.matchId = matchId; hdr.flags = 0; hdr.seq = seq; hdr.tick = 0;
    hdr.Serialize(ws);
    PacketShoot sh; sh.launchForce = 20;
    sh.Serialize(ws);
    return std::vector<uint8_t>(buf, buf + 12);
}

static sockaddr_in makeAddr(int matchIdx, int playerIdx)
{
    sockaddr_in a{};
    a.sin_family      = AF_INET;
    a.sin_addr.s_addr = htonl(0x7F000001);
    a.sin_port        = htons(static_cast<uint16_t>(30000 + matchIdx * 16 + playerIdx));
    return a;
}

// ─── Stats ────────────────────────────────────────────────────────────────────
static int64_t pct(const std::vector<int64_t>& sorted, int p)
{
    if (sorted.empty()) return 0;
    size_t idx = static_cast<size_t>(p) * sorted.size() / 100;
    if (idx >= sorted.size()) idx = sorted.size() - 1;
    return sorted[idx];
}

struct PhaseStats { int64_t avg, p95, p99, max; };

static PhaseStats computeStats(std::vector<int64_t> v)
{
    if (v.empty()) return {};
    std::sort(v.begin(), v.end());
    int64_t sum = 0;
    for (auto x : v) sum += x;
    return { sum / (int64_t)v.size(), pct(v, 95), pct(v, 99), v.back() };
}

// ─── Main ─────────────────────────────────────────────────────────────────────
int main()
{
    setvbuf(stdout, nullptr, _IONBF, 0);
    setvbuf(stderr, nullptr, _IONBF, 0);

    constexpr int       N_MATCHES    = 32;
    constexpr int       N_PLAYERS    = 10;
    constexpr int       N_WORKERS    = 8;
    constexpr int       START_CORE   = 2;
    constexpr DWORD_PTR CPU_MASK     = 0x3FC;  // bits 2..9 = cores 2-9
    constexpr int       WARMUP_TICKS = 600;
    constexpr int       LOG_INTERVAL = 600;
    constexpr float     DT           = 1.f / 60.f;
    constexpr int64_t   BUDGET_US    = 16667;

    SetConsoleCtrlHandler(ctrlHandler, TRUE);

    WSADATA wsa{};
    WSAStartup(MAKEWORD(2, 2), &wsa);

    pinCores(CPU_MASK);
    Logger::getInstance().init("bench_32match.log");

    // ── Build 32 matches ─────────────────────────────────────────────────────
    NullNetworkBackend net;
    std::vector<std::unique_ptr<Match>> matches;
    matches.reserve(N_MATCHES);

    for (int m = 0; m < N_MATCHES; ++m) {
        MatchConfig cfg;
        cfg.matchId         = static_cast<uint32_t>(10000 + m);
        cfg.mapName         = "bench_spread";
        cfg.maxDurationSecs = 99999;
        for (int i = 1; i <= N_PLAYERS; ++i) {
            cfg.playerIds.push_back(static_cast<uint32_t>(i));
            TankStats stats;
            stats.health      = 9'999'999;
            stats.speed       = 12.f;
            stats.damage      = 25;
            stats.barrelCount = 1;
            cfg.playerStats[static_cast<uint32_t>(i)] = stats;
        }
        matches.push_back(std::make_unique<Match>(std::move(cfg), net, [](MatchResult){}));
    }

    // Precompute addresses (matchIdx × 16 + playerIdx avoids port collisions)
    std::vector<std::vector<sockaddr_in>> addrs(N_MATCHES);
    for (int m = 0; m < N_MATCHES; ++m)
        for (int i = 0; i < N_PLAYERS; ++i)
            addrs[m].push_back(makeAddr(m, i));

    auto injectCommands = [&](int t, int m) {
        uint32_t mid = static_cast<uint32_t>(10000 + m);
        uint8_t  seq = static_cast<uint8_t>(t & 0xFF);
        for (int i = 0; i < N_PLAYERS; ++i) {
            GameCommand mc; mc.sender = addrs[m][i]; mc.matchId = mid;
            mc.op = Opcode::C2S_MOVE;
            mc.rawBuffer = buildMoveStationary(mid, seq);
            matches[m]->pushCommand(mc);

            GameCommand sc; sc.sender = addrs[m][i]; sc.matchId = mid;
            sc.op = Opcode::C2S_SHOOT;
            sc.rawBuffer = buildShoot(mid, seq);
            matches[m]->pushCommand(sc);
        }
    };

    // ── Thread pool: 8 workers (process already pinned to cores 2-9 via mask) ──
    ThreadPool pool(N_WORKERS);

    // wall_us: thời gian thực để hoàn thành TẤT CẢ 32 match (1 super-tick)
    // sum_us:  tổng thời gian từng match (nếu chạy tuần tự = wall_us × N_MATCHES)
    // parallelism = sum_us / wall_us ≈ N_WORKERS nếu thật sự song song
    std::vector<int64_t> wWall, wSum, wParallel;
    wWall.reserve(LOG_INTERVAL);
    wSum.reserve(LOG_INTERVAL);
    wParallel.reserve(LOG_INTERVAL);

    auto tickAll = [&](int t) {
        for (int m = 0; m < N_MATCHES; ++m) injectCommands(t, m);

        // submit ALL trước khi đợi bất kỳ ai — đây là bằng chứng song song
        std::vector<std::future<void>> futs;
        futs.reserve(N_MATCHES);

        using clk = std::chrono::high_resolution_clock;
        auto wall_t0 = clk::now();

        for (int m = 0; m < N_MATCHES; ++m)
            futs.push_back(pool.submit([mp = matches[m].get()]{ mp->tick(DT); }));
        for (auto& f : futs) f.get();   // barrier: đợi TẤT CẢ xong

        int64_t wall_us = std::chrono::duration_cast<std::chrono::microseconds>(
            clk::now() - wall_t0).count();

        // tổng thời gian từng match (bullet+physics+snap)
        int64_t sum_us = 0;
        for (int m = 0; m < N_MATCHES; ++m) {
            const auto& bd = matches[m]->lastBreakdown();
            sum_us += bd.bulletUs + bd.physicsUs + bd.snapUs;
        }

        if (wall_us > 0) {
            wWall.push_back(wall_us);
            wSum.push_back(sum_us);
            // parallelism_factor × 100 (để tránh float)
            wParallel.push_back(sum_us * 100 / wall_us);
        }
    };

    // ── Warmup ───────────────────────────────────────────────────────────────
    printf("[M32] Warming up: %d ticks × %d matches...\n", WARMUP_TICKS, N_MATCHES);
    fflush(stdout);

    for (int t = 0; t < WARMUP_TICKS; ++t) {
        tickAll(t);
        if (t > 0 && t % 60 == 0) {
            int64_t b = matches[0]->lastBreakdown().activeBullets;
            printf("  [warmup t=%d] match[0] active bullets = %lld\n", t, (long long)b);
            fflush(stdout);
        }
    }
    printf("[M32] Warmup done. match[0] steady bullets = %lld\n\n",
           (long long)matches[0]->lastBreakdown().activeBullets);
    printf("[M32] Streaming. Every %d ticks → [M32Perf] [M32Phase] [M32Match]×%d\n",
           LOG_INTERVAL, N_MATCHES);
    printf("[M32] Press Ctrl+C to stop.\n\n");
    fflush(stdout);

    // ── Window accumulators (3 phases only — no wall-clock) ──────────────────
    std::vector<std::vector<int64_t>> wBullet(N_MATCHES), wPhysics(N_MATCHES), wSnap(N_MATCHES);
    for (int m = 0; m < N_MATCHES; ++m) {
        wBullet[m].reserve(LOG_INTERVAL);
        wPhysics[m].reserve(LOG_INTERVAL);
        wSnap[m].reserve(LOG_INTERVAL);
    }

    int64_t totalOverruns = 0;  // per-match overrun: tickTotal > 16667µs
    int     globalTick    = WARMUP_TICKS;

    // ── Measurement loop ─────────────────────────────────────────────────────
    while (g_running.load(std::memory_order_relaxed)) {
        tickAll(globalTick);

        for (int m = 0; m < N_MATCHES; ++m) {
            const auto& bd = matches[m]->lastBreakdown();
            wBullet[m].push_back(bd.bulletUs);
            wPhysics[m].push_back(bd.physicsUs);
            wSnap[m].push_back(bd.snapUs);
            // overrun: any match whose tick total exceeds 60Hz budget
            if (bd.bulletUs + bd.physicsUs + bd.snapUs > BUDGET_US) ++totalOverruns;
        }

        ++globalTick;

        if (static_cast<int>(wBullet[0].size()) < LOG_INTERVAL) continue;

        // ── Emit window stats ─────────────────────────────────────────────
        std::vector<int64_t> allBullet, allPhysics, allSnap;
        allBullet.reserve(N_MATCHES * LOG_INTERVAL);
        allPhysics.reserve(N_MATCHES * LOG_INTERVAL);
        allSnap.reserve(N_MATCHES * LOG_INTERVAL);
        for (int m = 0; m < N_MATCHES; ++m) {
            allBullet.insert(allBullet.end(),  wBullet[m].begin(),  wBullet[m].end());
            allPhysics.insert(allPhysics.end(), wPhysics[m].begin(), wPhysics[m].end());
            allSnap.insert(allSnap.end(),    wSnap[m].begin(),    wSnap[m].end());
        }
        auto sBullet  = computeStats(allBullet);
        auto sPhysics = computeStats(allPhysics);
        auto sSnap    = computeStats(allSnap);

        // ── Concurrency proof ─────────────────────────────────────────────
        int64_t wall_avg = 0, sum_avg = 0, par_avg = 0;
        if (!wWall.empty()) {
            for (auto x : wWall)    wall_avg += x;
            for (auto x : wSum)     sum_avg  += x;
            for (auto x : wParallel) par_avg += x;
            wall_avg /= (int64_t)wWall.size();
            sum_avg  /= (int64_t)wSum.size();
            par_avg  /= (int64_t)wParallel.size();
        }
        // par_avg/100 = parallelism factor: ~8 nếu song song, ~1 nếu tuần tự
        printf("[M32Concurrent]"
               " wall_avg=%lldus sum_match_avg=%lldus"
               " parallelism=%.1fx workers=%d"
               " (sequential_would_be=%lldus)\n",
               (long long)wall_avg,
               (long long)sum_avg,
               par_avg / 100.0,
               N_WORKERS,
               (long long)(wall_avg * N_MATCHES));
        wWall.clear(); wSum.clear(); wParallel.clear();

        printf("[M32Perf] ticks=%d matches=%d workers=%d mask=0x%03X"
               " | overruns=%lld\n",
               globalTick, N_MATCHES, N_WORKERS, static_cast<unsigned>(CPU_MASK),
               (long long)totalOverruns);

        printf("[M32Phase]"
               " bullet avg=%lldus p95=%lldus p99=%lldus max=%lldus"
               " | physics avg=%lldus p95=%lldus p99=%lldus max=%lldus"
               " | snap avg=%lldus p95=%lldus p99=%lldus max=%lldus\n",
               (long long)sBullet.avg,  (long long)sBullet.p95,
               (long long)sBullet.p99,  (long long)sBullet.max,
               (long long)sPhysics.avg, (long long)sPhysics.p95,
               (long long)sPhysics.p99, (long long)sPhysics.max,
               (long long)sSnap.avg,    (long long)sSnap.p95,
               (long long)sSnap.p99,    (long long)sSnap.max);

        for (int m = 0; m < N_MATCHES; ++m) {
            auto sbm = computeStats(wBullet[m]);
            auto spm = computeStats(wPhysics[m]);
            auto ssm = computeStats(wSnap[m]);
            printf("[M32Match] id=%d bullet=%lldus physics=%lldus snap=%lldus total=%lldus\n",
                   10000 + m,
                   (long long)sbm.avg, (long long)spm.avg,
                   (long long)ssm.avg, (long long)(sbm.avg + spm.avg + ssm.avg));
        }
        fflush(stdout);

        // Reset window
        for (int m = 0; m < N_MATCHES; ++m) {
            wBullet[m].clear();
            wPhysics[m].clear();
            wSnap[m].clear();
        }
    }

    printf("\n[M32] Stopped at tick %d. Total overruns: %lld\n",
           globalTick, (long long)totalOverruns);
    fflush(stdout);

    WSACleanup();
    return 0;
}
