// bench_wc_live.cpp
//
// Worst-case benchmark chạy VÔ HẠN vòng — dùng với bench_wc_live_agent.py
//
// CLI:  bench_wc_live.exe [--players=N] [--no-isolation] [--log=PATH]
//       N mặc định = 10
//
// Log format (stdout + file):
//   [WC_Live] players=N warmup=done steady_bullets=X
//   [WC_Phase] players=N window=W bullet_avg=Xus physics_avg=Xus snap_avg=Xus total_avg=Xus overruns=K/T
//   [WC_Final] players=N window=W bullet_p99=Xus physics_p99=Xus snap_p99=Xus total_p99=Xus gpc=N budget_pct=X.X overruns=K/T
//
// agent parse [WC_Final] để cập nhật Prometheus metrics sau mỗi window (600 ticks)
// agent parse [WC_Phase] để streaming avg theo thời gian thực

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winsock2.h>
#include <mmsystem.h>

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
#include <cmath>
#include <cassert>

// ── Cấu hình mặc định ─────────────────────────────────────────────────────────
static int      g_players     = 10;
static int      g_matches     = 1;    // --matches=K: số match chạy tuần tự / tick
static int      g_pinnedCore  = 2;
static bool     g_noIso       = false;
static const char* g_mapName  = "bench_spread";
static const char* g_logFile  = "bench_wc_live.log";

static constexpr int     WARMUP_TICKS  = 600;
static constexpr int     WINDOW_TICKS  = 600;    // 600 ticks/window = ~10s
static constexpr float   DT            = 1.f / 60.f;
static constexpr int64_t BUDGET_US     = 16667;

// ── NullNetworkBackend ────────────────────────────────────────────────────────
class NullNetworkBackend : public INetworkBackend {
public:
    bool start(int) override { return true; }
    void stop()     override {}
    void send(const sockaddr_in&, const uint8_t*, size_t) override {}
    void setRouteCallback(std::function<void(GameCommand)>) override {}
    const char* backendName() const override { return "null"; }
};

// ── Packet builders ───────────────────────────────────────────────────────────
static std::vector<uint8_t> buildMovePacket(uint32_t matchId, uint8_t seq)
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

static sockaddr_in makeAddr(int idx) {
    sockaddr_in a{};
    a.sin_family      = AF_INET;
    a.sin_addr.s_addr = htonl(0x7F000001);
    a.sin_port        = htons(static_cast<uint16_t>(20000 + idx));
    return a;
}

// ── Stats ─────────────────────────────────────────────────────────────────────
static int64_t pctVal(std::vector<int64_t>& sorted, int p) {
    if (sorted.empty()) return 0;
    size_t idx = static_cast<size_t>(p) * sorted.size() / 100;
    if (idx >= sorted.size()) idx = sorted.size() - 1;
    return sorted[idx];
}

struct PhaseStats { int64_t avg, p50, p95, p99, mx; };

static PhaseStats computeStats(std::vector<int64_t>& v) {
    std::sort(v.begin(), v.end());
    int64_t sum = 0; for (auto x : v) sum += x;
    return { v.empty() ? 0 : sum / (int64_t)v.size(),
             pctVal(v,50), pctVal(v,95), pctVal(v,99),
             v.empty() ? 0 : v.back() };
}

// ── Log ───────────────────────────────────────────────────────────────────────
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
static void pinToCore(int core) {
    if (g_noIso) {
        logLine("[WC_Live] players=%d NO_ISOLATION mode", g_players);
        return;
    }
    DWORD_PTR mask = (DWORD_PTR)1 << core;
    BOOL ok1 = SetProcessAffinityMask(GetCurrentProcess(), mask);
    BOOL ok2 = SetPriorityClass(GetCurrentProcess(), REALTIME_PRIORITY_CLASS);
    BOOL ok3 = SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    if (ok1 && ok2 && ok3)
        logLine("[WC_Live] players=%d Pinned to core %d / %d, REALTIME/TIME_CRITICAL",
                g_players, core, (int)std::thread::hardware_concurrency());
    else
        logLine("[WC_Live] players=%d WARNING pin failed (run as Admin)", g_players);
}

// ── Parse CLI ─────────────────────────────────────────────────────────────────
static void parseCLI(int argc, char** argv) {
    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg.rfind("--players=", 0) == 0)
            g_players = std::stoi(arg.substr(10));
        else if (arg == "--no-isolation")
            g_noIso = true;
        else if (arg.rfind("--log=", 0) == 0)
            g_logFile = argv[i] + 6;
        else if (arg.rfind("--core=", 0) == 0)
            g_pinnedCore = std::stoi(arg.substr(7));
        else if (arg.rfind("--matches=", 0) == 0) {
            int v = std::stoi(arg.substr(10));
            g_matches = (v < 1) ? 1 : v;
        }
    }
}

// ── Build match ───────────────────────────────────────────────────────────────
static NullNetworkBackend g_net;

static std::unique_ptr<Match> buildMatch(int matchIdx = 0) {
    MatchConfig cfg;
    cfg.matchId         = static_cast<uint32_t>(9000 + matchIdx);
    cfg.mapName         = g_mapName;
    cfg.maxDurationSecs = 99999;
    for (int i = 1; i <= g_players; ++i) {
        cfg.playerIds.push_back(static_cast<uint32_t>(i));
        TankStats stats;
        stats.health      = 9'999'999;
        stats.speed       = 12.f;
        stats.damage      = 25;
        stats.barrelCount = 1;
        cfg.playerStats[static_cast<uint32_t>(i)] = stats;
    }
    return std::make_unique<Match>(std::move(cfg), g_net, [](MatchResult){});
}

// ── One warmup + infinite measurement windows ─────────────────────────────────
int main(int argc, char** argv)
{
    WSADATA wsa{}; WSAStartup(MAKEWORD(2,2), &wsa);
    timeBeginPeriod(1);
    parseCLI(argc, argv);

    g_log = fopen(g_logFile, "w");
    logLine("[WC_Live] players=%d matches=%d map=%s warmup=%d window=%d ticks",
            g_players, g_matches, g_mapName, WARMUP_TICKS, WINDOW_TICKS);

    pinToCore(g_pinnedCore);

    // ── Build K match instances ───────────────────────────────────────────────
    std::vector<std::unique_ptr<Match>> matches;
    for (int m = 0; m < g_matches; ++m)
        matches.push_back(buildMatch(m));

    // addresses per match
    std::vector<std::vector<sockaddr_in>> allAddrs(g_matches);
    for (int m = 0; m < g_matches; ++m)
        for (int i = 0; i < g_players; ++i)
            allAddrs[m].push_back(makeAddr(m * g_players + i));

    auto injectAll = [&](int t) {
        for (int m = 0; m < g_matches; ++m) {
            uint32_t mid = static_cast<uint32_t>(9000 + m);
            for (int i = 0; i < g_players; ++i) {
                uint8_t seq = static_cast<uint8_t>(t & 0xFF);
                GameCommand mc; mc.sender = allAddrs[m][i]; mc.matchId = mid;
                mc.op = Opcode::C2S_MOVE;
                mc.rawBuffer = buildMovePacket(mid, seq);
                matches[m]->pushCommand(mc);
                GameCommand sc; sc.sender = allAddrs[m][i]; sc.matchId = mid;
                sc.op = Opcode::C2S_SHOOT;
                sc.rawBuffer = buildShootPacket(mid, seq, outwardYaw(i, g_players));
                matches[m]->pushCommand(sc);
            }
        }
    };

    using Clock = std::chrono::high_resolution_clock;
    using us    = std::chrono::microseconds;

    // ── Warmup ────────────────────────────────────────────────────────────────
    logLine("[WC_Live] players=%d matches=%d Starting warmup (%d ticks)...",
            g_players, g_matches, WARMUP_TICKS);
    auto loop_start = Clock::now();
    for (int t = 0; t < WARMUP_TICKS; ++t) {
        injectAll(t);
        for (auto& mp : matches) mp->tick(DT);
        auto next = loop_start + us((long long)((t+1) * (1000000.0/60.0)));
        std::this_thread::sleep_until(next);
    }
    int64_t steadyBullets = matches[0]->lastBreakdown().activeBullets;
    logLine("[WC_Live] players=%d matches=%d warmup=done steady_bullets=%lld",
            g_players, g_matches, (long long)steadyBullets);

    // ── Infinite measurement windows ──────────────────────────────────────────
    int globalTick = WARMUP_TICKS;
    int windowIdx  = 0;

    // Per-tick accumulators for window stats
    std::vector<int64_t> wBullet, wPhysics, wSnap, wTotal;
    std::vector<int64_t> wOverhead;          // overhead per tick (wall - sum_individual)
    wBullet.reserve(WINDOW_TICKS);
    wPhysics.reserve(WINDOW_TICKS);
    wSnap.reserve(WINDOW_TICKS);
    wTotal.reserve(WINDOW_TICKS);
    wOverhead.reserve(WINDOW_TICKS);

    int64_t win_bullet=0, win_physics=0, win_snap=0, win_total=0, win_overhead=0;
    int win_count=0, win_overruns=0;

    while (true) {
        injectAll(globalTick++);

        // ── Tick all K matches, measure wall time ─────────────────────────────
        auto tick_wall_start = Clock::now();
        for (auto& mp : matches) mp->tick(DT);
        auto tick_wall_end   = Clock::now();

        int64_t wall_us = std::chrono::duration_cast<us>(
            tick_wall_end - tick_wall_start).count();

        // Sum of individual breakdown times (pure game logic, no switching cost)
        int64_t sum_individual = 0;
        int64_t sum_bullet = 0, sum_physics = 0, sum_snap = 0;
        for (auto& mp : matches) {
            const auto& bd = mp->lastBreakdown();
            sum_individual += bd.totalPostDrainUs;
            sum_bullet     += bd.bulletUs;
            sum_physics    += bd.physicsUs;
            sum_snap       += bd.snapUs;
        }

        // overhead = wall - sum_individual  (time "lost" between task switches)
        // For K matches there are (K-1) switches, so overhead_per_switch = overhead/(K-1)
        int64_t overhead_total = wall_us - sum_individual;
        if (overhead_total < 0) overhead_total = 0;  // clamp negative noise

        // Average per match (for single-match compatible metrics)
        int64_t avg_bullet  = sum_bullet  / g_matches;
        int64_t avg_physics = sum_physics / g_matches;
        int64_t avg_snap    = sum_snap    / g_matches;
        int64_t avg_total   = sum_individual / g_matches;

        wBullet.push_back(avg_bullet);
        wPhysics.push_back(avg_physics);
        wSnap.push_back(avg_snap);
        wTotal.push_back(avg_total);
        wOverhead.push_back(overhead_total);

        win_bullet   += avg_bullet;
        win_physics  += avg_physics;
        win_snap     += avg_snap;
        win_total    += avg_total;
        win_overhead += overhead_total;
        ++win_count;

        if (avg_total > BUDGET_US) ++win_overruns;

        // ── Window reporting ──────────────────────────────────────────────────
        if (win_count >= WINDOW_TICKS) {
            ++windowIdx;

            auto sBullet  = computeStats(wBullet);
            auto sPhysics = computeStats(wPhysics);
            auto sSnap    = computeStats(wSnap);
            auto sTotal   = computeStats(wTotal);
            auto sOvhd    = computeStats(wOverhead);

            int64_t gpc       = (sTotal.p99 > 0) ? BUDGET_US / sTotal.p99 : 0;
            double  budgetPct = 100.0 * sTotal.p99 / BUDGET_US;

            // overhead_per_switch: if K=1 → 0 (no switches); if K>1 → overhead/(K-1)
            int64_t ovhd_per_switch_avg = (g_matches > 1)
                ? sOvhd.avg / (g_matches - 1) : 0;
            int64_t ovhd_per_switch_p99 = (g_matches > 1)
                ? sOvhd.p99 / (g_matches - 1) : 0;

            logLine("[WC_Phase] players=%d matches=%d window=%d "
                    "bullet_avg=%lldus physics_avg=%lldus snap_avg=%lldus "
                    "total_avg=%lldus overruns=%d/%d",
                    g_players, g_matches, windowIdx,
                    (long long)(win_bullet/win_count),
                    (long long)(win_physics/win_count),
                    (long long)(win_snap/win_count),
                    (long long)(win_total/win_count),
                    win_overruns, win_count);

            logLine("[WC_Final] players=%d matches=%d window=%d "
                    "bullet_avg=%lldus bullet_p99=%lldus bullet_max=%lldus "
                    "physics_avg=%lldus physics_p99=%lldus physics_max=%lldus "
                    "snap_avg=%lldus snap_p99=%lldus snap_max=%lldus "
                    "total_avg=%lldus total_p99=%lldus total_max=%lldus "
                    "gpc=%lld budget_pct=%.1f overruns=%d/%d "
                    "steady_bullets=%lld "
                    "overhead_avg=%lldus overhead_p99=%lldus "
                    "overhead_per_switch_avg=%lldus overhead_per_switch_p99=%lldus",
                    g_players, g_matches, windowIdx,
                    (long long)sBullet.avg,  (long long)sBullet.p99,  (long long)sBullet.mx,
                    (long long)sPhysics.avg, (long long)sPhysics.p99, (long long)sPhysics.mx,
                    (long long)sSnap.avg,    (long long)sSnap.p99,    (long long)sSnap.mx,
                    (long long)sTotal.avg,   (long long)sTotal.p99,   (long long)sTotal.mx,
                    (long long)gpc, budgetPct, win_overruns, win_count,
                    (long long)steadyBullets,
                    (long long)sOvhd.avg, (long long)sOvhd.p99,
                    (long long)ovhd_per_switch_avg, (long long)ovhd_per_switch_p99);

            // reset window
            win_bullet=win_physics=win_snap=win_total=win_overhead=0;
            win_count=win_overruns=0;
            wBullet.clear(); wPhysics.clear(); wSnap.clear();
            wTotal.clear();  wOverhead.clear();
        }

        // sleep to maintain 60 Hz
        auto next = loop_start + us((long long)(globalTick * (1000000.0 / 60.0)));
        std::this_thread::sleep_until(next);
    }

    timeEndPeriod(1);
    if (g_log) fclose(g_log);
    WSACleanup();
    return 0;
}
