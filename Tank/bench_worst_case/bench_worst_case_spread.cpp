// bench_worst_case_spread.cpp
//
// Đo lại K2 worst-case trên codebase hiện tại — ĐÚNG Y HỆT điều kiện K2 gốc:
//
//   Map:       bench_spread.json  (không tường, defaultSpawn R=22m, turretOffset.y điều chỉnh)
//   Players:   10, health=9,999,999 (không chết), damage=25, speed=12
//   MOVE:      dirX=1(none), dirZ=1(none) — STATIONARY (K2 gốc: "tank đứng yên")
//   SHOOT:     launchForce=20, turretYaw = outward per player (K2 gốc: "bắn ra ngoài tâm")
//   Timing:    Match::lastBreakdown() — dispatch tính vào [t0,t1]
//   CPU:       core 2, REALTIME/TIME_CRITICAL, timeBeginPeriod(1)
//   Warmup:    600 tick  |  Đo: 6000 tick  → steady-state ~590 bullets
//   GPC = floor(16667 / totalPostDrain_P99)
//
// Log lines cho bench_metrics_agent.py:
//   [BenchPhase] bullet avg=Xus | physics avg=Xus | snap avg=Xus | total avg=Xus | overruns=N/T
//   [BenchFinal] phase=bullet    avg=Xus p50=Xus p95=Xus p99=Xus max=Xus
//   [BenchFinal] GPC=floor(16667/X)=N match/core

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winsock2.h>
#include <mmsystem.h>   // timeBeginPeriod / timeEndPeriod

#include "Core/Match.hpp"
#include "Network/INetworkBackend.hpp"
#include "Network/GameCommand.hpp"
#include "Network/Opcode.hpp"
#include "Network/Packets.hpp"
#include "Core/MatchConfig.hpp"
#include "WriteStream.h"

#include <chrono>
#include <thread>
#include <vector>
#include <algorithm>
#include <numeric>
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <string>
#include <functional>
#include <fstream>
#include <cmath>

// ── Cấu hình ─────────────────────────────────────────────────────────────────
static constexpr int    PINNED_CORE    = 2;
static constexpr int    N_PLAYERS      = 10;
static constexpr int    WARMUP_TICKS   = 600;
static constexpr int    MEASURE_TICKS  = 6000;
static constexpr int    LOG_INTERVAL   = 600;
static constexpr float  DT             = 1.f / 60.f;
static constexpr int64_t BUDGET_US     = 16667;

// bench_spread.json: no static colliders, no spawn points (→ defaultSpawn circle R=22m),
// max_health=999999 so tanks survive. This replicates the SPAWN GEOMETRY of the old
// benchmark (tanks spread far apart → bullets fly long before hitting → high bullet count).
// world.json now only has 2 spawn points 8m apart → all tanks cluster → bullets die instantly.
static const char* MAP_NAME  = "bench_spread";
static const char* LOG_FILE  = "bench_worst_case.log";

// ── NullNetworkBackend ────────────────────────────────────────────────────────
class NullNetworkBackend : public INetworkBackend {
public:
    bool start(int) override { return true; }
    void stop()     override {}
    void send(const sockaddr_in&, const uint8_t*, size_t) override {}
    void setRouteCallback(std::function<void(GameCommand)>) override {}
    const char* backendName() const override { return "null"; }
};

// ── Packet builders (same as old benchmark) ───────────────────────────────────
static std::vector<uint8_t> buildMovePacket(uint32_t matchId, uint8_t seq)
{
    uint8_t buf[12] = {};
    WriteStream ws(reinterpret_cast<uint32_t*>(buf), 3);
    PacketHeader hdr;
    hdr.size = 12; hdr.opcode = Opcode::C2S_MOVE;
    hdr.matchId = matchId; hdr.flags = 0; hdr.seq = seq; hdr.tick = 0;
    hdr.Serialize(ws);
    PacketMovement mv; mv.dirX = 1; mv.dirZ = 1; mv.speed = 0;  // stationary — K2 gốc: "tank đứng yên"
    mv.Serialize(ws);
    return std::vector<uint8_t>(buf, buf + 12);
}

// K2 gốc: turretYaw = outward per player (π/2 − angle_i)
// angle_i = i/N × 2π, tank i tại (R·cos(a),1,R·sin(a)), hướng ra ngoài = (cos(a),0,sin(a))
// game forward = (sin(yaw),0,cos(yaw)) → yaw = π/2 − angle_i
static float outwardYaw(int playerIdx, int nPlayers) {
    const float PI = 3.14159265f;
    float angle = (float)playerIdx / nPlayers * (2.f * PI);
    return PI / 2.f - angle;
}

static std::vector<uint8_t> buildShootPacket(uint32_t matchId, uint8_t seq, float turretYaw)
{
    uint8_t buf[16] = {};
    WriteStream ws(reinterpret_cast<uint32_t*>(buf), 4);
    PacketHeader hdr;
    hdr.size = 16; hdr.opcode = Opcode::C2S_SHOOT;
    hdr.matchId = matchId; hdr.flags = 0; hdr.seq = seq; hdr.tick = 0;
    hdr.Serialize(ws);
    PacketShoot sh; sh.launchForce = 20; sh.turretYaw = turretYaw;
    sh.Serialize(ws);
    return std::vector<uint8_t>(buf, buf + 16);
}

static sockaddr_in makeAddr(int idx)
{
    sockaddr_in a{};
    a.sin_family      = AF_INET;
    a.sin_addr.s_addr = htonl(0x7F000001);
    a.sin_port        = htons(static_cast<uint16_t>(20000 + idx));
    return a;
}

// ── Stats ─────────────────────────────────────────────────────────────────────
static int64_t pct(std::vector<int64_t>& sorted, int p)
{
    if (sorted.empty()) return 0;
    size_t idx = static_cast<size_t>(p) * sorted.size() / 100;
    if (idx >= sorted.size()) idx = sorted.size() - 1;
    return sorted[idx];
}

struct PhaseStats { int64_t avg, p50, p95, p99, mx; };

static PhaseStats computeStats(std::vector<int64_t>& v)
{
    std::sort(v.begin(), v.end());
    int64_t sum = 0; for (auto x : v) sum += x;
    return { v.empty() ? 0 : sum / (int64_t)v.size(),
             pct(v,50), pct(v,95), pct(v,99),
             v.empty() ? 0 : v.back() };
}

// ── Log writer ────────────────────────────────────────────────────────────────
static FILE* g_log = nullptr;

static void logLine(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt); vprintf(fmt, ap); va_end(ap); printf("\n"); fflush(stdout);
    if (g_log) {
        va_start(ap, fmt); vfprintf(g_log, fmt, ap); va_end(ap);
        fprintf(g_log, "\n"); fflush(g_log);
    }
}

// ── CPU isolation ─────────────────────────────────────────────────────────────
static bool pinToCore(int core, bool noIsolation = false)
{
    if (noIsolation) {
        logLine("[bench] NO ISOLATION — running without affinity/priority");
        return true;
    }
    DWORD_PTR mask = (DWORD_PTR)1 << core;
    BOOL ok1 = SetProcessAffinityMask(GetCurrentProcess(), mask);
    BOOL ok2 = SetPriorityClass(GetCurrentProcess(), REALTIME_PRIORITY_CLASS);
    BOOL ok3 = SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    if (ok1 && ok2 && ok3)
        logLine("[bench] Pinned to core %d / %d, REALTIME/TIME_CRITICAL",
                core, (int)std::thread::hardware_concurrency());
    else
        logLine("[bench] WARNING: pin partially failed (aff=%d prio=%d thr=%d) — run as Admin",
                (int)ok1, (int)ok2, (int)ok3);
    return ok1 && ok2 && ok3;
}

// ── Main ──────────────────────────────────────────────────────────────────────
int main()
{
    WSADATA wsa{}; WSAStartup(MAKEWORD(2,2), &wsa);
    // Reduce Windows timer resolution from 15.6ms → 1ms to minimize OS jitter
    timeBeginPeriod(1);
    g_log = fopen(LOG_FILE, "w");

    logLine("[bench] ======================================================");
    logLine("[bench] Worst-Case GPC Benchmark v2 (Match::tick methodology)");
    logLine("[bench] Map: %s  Players: %d  Warmup: %d  Measure: %d ticks",
            MAP_NAME, N_PLAYERS, WARMUP_TICKS, MEASURE_TICKS);

    // NO_ISOLATION=1 để chạy không có isolation (dùng để so sánh)
    bool noIso = (getenv("NO_ISOLATION") != nullptr);
    pinToCore(PINNED_CORE, noIso);

    // ── Setup Match ───────────────────────────────────────────────────────────
    NullNetworkBackend net;
    MatchConfig cfg;
    cfg.matchId         = 9999;
    cfg.mapName         = MAP_NAME;
    cfg.maxDurationSecs = 99999;

    for (int i = 1; i <= N_PLAYERS; ++i) {
        cfg.playerIds.push_back(static_cast<uint32_t>(i));
        TankStats stats;
        stats.health      = 9'999'999;  // không chết → 10 OBB suốt phiên đo
        stats.speed       = 12.f;
        stats.damage      = 25;
        stats.barrelCount = 1;
        cfg.playerStats[static_cast<uint32_t>(i)] = stats;
    }

    auto matchPtr = std::make_unique<Match>(std::move(cfg), net, [](MatchResult){});
    Match& match  = *matchPtr;

    std::vector<sockaddr_in> addrs;
    for (int i = 0; i < N_PLAYERS; ++i) addrs.push_back(makeAddr(i));

    // Inject 10 MOVE + 10 SHOOT per tick (identical to old benchmark)
    // K2 gốc: tank đứng yên, bắn hướng ra ngoài tâm
    auto injectCommands = [&](int t) {
        for (int i = 0; i < N_PLAYERS; ++i) {
            uint8_t seq = static_cast<uint8_t>(t & 0xFF);
            GameCommand mc; mc.sender = addrs[i]; mc.matchId = 9999;
            mc.op = Opcode::C2S_MOVE; mc.rawBuffer = buildMovePacket(9999, seq);
            match.pushCommand(mc);
            GameCommand sc; sc.sender = addrs[i]; sc.matchId = 9999;
            sc.op = Opcode::C2S_SHOOT;
            sc.rawBuffer = buildShootPacket(9999, seq, outwardYaw(i, N_PLAYERS));
            match.pushCommand(sc);
        }
    };

    // ── Warmup ────────────────────────────────────────────────────────────────
    logLine("[bench] Starting warmup (%d ticks) ...", WARMUP_TICKS);
    using Clock = std::chrono::high_resolution_clock;
    auto loop_start = Clock::now();

    for (int t = 0; t < WARMUP_TICKS; ++t) {
        injectCommands(t);
        match.tick(DT);
        auto next = loop_start + std::chrono::microseconds((long long)((t + 1) * (1000000.0 / 60.0)));
        std::this_thread::sleep_until(next);
    }

    int64_t steadyBullets = match.lastBreakdown().activeBullets;
    logLine("[bench] Warmup done. Steady-state active bullets = %lld", (long long)steadyBullets);
    logLine("[bench] Starting measurement (%d ticks) ...", MEASURE_TICKS);

    // ── Measurement ───────────────────────────────────────────────────────────
    std::vector<int64_t> vDrain, vDispatch, vBullet, vPhysics, vSnap, vTotal;
    vDrain.reserve(MEASURE_TICKS); vDispatch.reserve(MEASURE_TICKS);
    vBullet.reserve(MEASURE_TICKS); vPhysics.reserve(MEASURE_TICKS);
    vSnap.reserve(MEASURE_TICKS); vTotal.reserve(MEASURE_TICKS);

    int overruns = 0;
    int64_t win_drain=0, win_dispatch=0, win_bullet=0, win_physics=0, win_snap=0, win_total=0;
    int win_count = 0;

    for (int t = 0; t < MEASURE_TICKS; ++t) {
        injectCommands(WARMUP_TICKS + t);
        match.tick(DT);

        const auto& bd = match.lastBreakdown();
        vDrain.push_back(bd.drainUs);
        vDispatch.push_back(bd.dispatchUs);
        vBullet.push_back(bd.bulletUs);
        vPhysics.push_back(bd.physicsUs);
        vSnap.push_back(bd.snapUs);
        vTotal.push_back(bd.totalPostDrainUs);

        win_drain    += bd.drainUs;
        win_dispatch += bd.dispatchUs;
        win_bullet   += bd.bulletUs;
        win_physics  += bd.physicsUs;
        win_snap     += bd.snapUs;
        win_total    += bd.totalPostDrainUs;
        ++win_count;

        if (bd.totalPostDrainUs > BUDGET_US) ++overruns;

        if (win_count >= LOG_INTERVAL) {
            int64_t ba = win_bullet   / win_count;
            int64_t pa = win_physics  / win_count;
            int64_t sa = win_snap     / win_count;
            int64_t ta = win_total    / win_count;
            logLine("[BenchPhase] bullet avg=%lldus | physics avg=%lldus | snap avg=%lldus "
                    "| total avg=%lldus | overruns=%d/%d",
                    (long long)ba, (long long)pa, (long long)sa, (long long)ta,
                    overruns, t + 1);
            win_drain=win_dispatch=win_bullet=win_physics=win_snap=win_total=0;
            win_count=0;
        }

        auto next = loop_start + std::chrono::microseconds(
            (long long)((WARMUP_TICKS + t + 1) * (1000000.0 / 60.0)));
        std::this_thread::sleep_until(next);
    }

    // Flush remaining window
    if (win_count > 0) {
        logLine("[BenchPhase] bullet avg=%lldus | physics avg=%lldus | snap avg=%lldus "
                "| total avg=%lldus | overruns=%d/%d",
                (long long)(win_bullet/win_count), (long long)(win_physics/win_count),
                (long long)(win_snap/win_count),   (long long)(win_total/win_count),
                overruns, MEASURE_TICKS);
    }

    // ── Compute percentiles ───────────────────────────────────────────────────
    auto sDrain    = computeStats(vDrain);
    auto sDispatch = computeStats(vDispatch);
    auto sBullet   = computeStats(vBullet);
    auto sPhysics  = computeStats(vPhysics);
    auto sSnap     = computeStats(vSnap);
    auto sTotal    = computeStats(vTotal);

    int64_t gpc = (sTotal.p99 > 0) ? BUDGET_US / sTotal.p99 : 0;
    double  budgetUsed = 100.0 * sTotal.p99 / BUDGET_US;

    logLine("[bench] ======================================================");
    logLine("[BenchFinal] ticks=%d pinned_core=%d warmup=%d steady_bullets=%lld",
            MEASURE_TICKS, PINNED_CORE, WARMUP_TICKS, (long long)steadyBullets);
    logLine("[BenchFinal] phase=drain      avg=%lldus p50=%lldus p95=%lldus p99=%lldus max=%lldus",
            (long long)sDrain.avg, (long long)sDrain.p50, (long long)sDrain.p95,
            (long long)sDrain.p99, (long long)sDrain.mx);
    logLine("[BenchFinal] phase=dispatch   avg=%lldus p50=%lldus p95=%lldus p99=%lldus max=%lldus",
            (long long)sDispatch.avg,(long long)sDispatch.p50,(long long)sDispatch.p95,
            (long long)sDispatch.p99,(long long)sDispatch.mx);
    logLine("[BenchFinal] phase=bullet     avg=%lldus p50=%lldus p95=%lldus p99=%lldus max=%lldus",
            (long long)sBullet.avg,(long long)sBullet.p50,(long long)sBullet.p95,
            (long long)sBullet.p99,(long long)sBullet.mx);
    logLine("[BenchFinal] phase=physics    avg=%lldus p50=%lldus p95=%lldus p99=%lldus max=%lldus",
            (long long)sPhysics.avg,(long long)sPhysics.p50,(long long)sPhysics.p95,
            (long long)sPhysics.p99,(long long)sPhysics.mx);
    logLine("[BenchFinal] phase=snapshot   avg=%lldus p50=%lldus p95=%lldus p99=%lldus max=%lldus",
            (long long)sSnap.avg,(long long)sSnap.p50,(long long)sSnap.p95,
            (long long)sSnap.p99,(long long)sSnap.mx);
    logLine("[BenchFinal] total            avg=%lldus p99=%lldus max=%lldus",
            (long long)sTotal.avg,(long long)sTotal.p99,(long long)sTotal.mx);
    logLine("[BenchFinal] overruns=%d/%d (%.3f%%)", overruns, MEASURE_TICKS,
            100.0 * overruns / MEASURE_TICKS);
    logLine("[BenchFinal] budget_us=16667 total_p99=%lldus budget_used=%.1f%%",
            (long long)sTotal.p99, budgetUsed);
    logLine("[BenchFinal] GPC=floor(16667/%lld)=%lld match/core",
            (long long)sTotal.p99, (long long)gpc);
    logLine("[bench] Done. Log: %s", LOG_FILE);

    timeEndPeriod(1);
    if (g_log) fclose(g_log);
    WSACleanup();
    return 0;
}
