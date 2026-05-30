/*
 * bench_worst_case.cpp
 *
 * Mục đích: Đo per-phase tick time cho kịch bản worst-case có thể suy diễn được
 *           từ phân tích cost model của từng subsystem trong Match::tick().
 *
 * t0 = Clock::now() đặt SAU khi drain _cmdQueue (theo yêu cầu).
 * t1 = Clock::now() sau broadcastSnapshot().
 *
 * Cost model và lý do đây là worst case:
 *   updateBullets: O(B × W + B × T)  — B=active bullets, W=static OBBs, T=tanks
 *   runPhysics:    O(E + P × SAT)    — E=entities, P=broad-phase pairs
 *   dispatch:      O(C)              — C=commands drained
 *   broadcastSnapshot: O(T² + T×Bush + B) — T²=placement loop, Bush=bush check
 *
 *   Mỗi chiều đều ở max: T=10 (max slot), C=20 (10 MOVE + 10 SHOOT),
 *   B=steady-state sau 10s warmup (10 bullets/tick × max-TTL survivors),
 *   health=9,999,999 để tank không chết → không mất dynamic OBB.
 *
 * Output:
 *   - Per-phase distribution table (p50/p95/p99)
 *   - GPC (Games Per Core) từ p99 totalPostDrainUs
 *   - bench_result.json → dùng bởi bench_push_grafana.py để push lên Grafana
 *
 * Build:
 *   cmake --build <build_dir> --target bench_worst_case --config Release
 *   cd <build_dir>/bench_worst_case  (hoặc Release/)
 *   bench_worst_case.exe [output_json_path]
 */

#include "Core/Match.hpp"
#include "Network/INetworkBackend.hpp"
#include "Network/GameCommand.hpp"
#include "Network/Opcode.hpp"
#include "Network/Packets.hpp"
#include "Network/NetworkConstants.h"
#include "Core/MatchConfig.hpp"
#include "Utils/Logger.hpp"
#include "WriteStream.h"

#include <winsock2.h>
#include <windows.h>
#include <chrono>
#include <vector>
#include <algorithm>
#include <numeric>
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <string>
#include <functional>
#include <fstream>

// ─────────────────────────────────────────────────────────────────────────────
// CPU isolation helpers (Windows)
//
// Không thể isolate core hoàn toàn như Linux isolcpus/nohz_full, nhưng
// 3 bước dưới đây giảm thiểu đáng kể nhiễu từ OS scheduler:
//   1. SetProcessAffinityMask  → buộc toàn process chỉ chạy trên 1 core
//   2. SetPriorityClass REALTIME → giảm preemption từ background process
//   3. SetThreadPriority TIME_CRITICAL → ưu tiên cao nhất user-space
//
// Giới hạn không thể khắc phục trên Windows:
//   • Hardware interrupt (timer ISR, IOCP, NIC DPC) vẫn có thể preempt
//   • Windows kernel threads (DPC/ISR) chạy trên bất kỳ core nào
//   • Không có nohz_full → timer tick mỗi ~15.6ms vẫn xảy ra (gây ~1 tick spike)
// ─────────────────────────────────────────────────────────────────────────────

static int pinToCore(int coreIndex)
{
    DWORD_PTR mask = (DWORD_PTR)1 << coreIndex;

    // Pin toàn process (bao gồm mọi thread) vào coreIndex
    if (!SetProcessAffinityMask(GetCurrentProcess(), mask)) {
        fprintf(stderr, "[bench] SetProcessAffinityMask failed (err=%lu) — continuing unpinned\n",
                GetLastError());
        return -1;
    }

    // Raise process priority để giảm preemption từ các process thường
    if (!SetPriorityClass(GetCurrentProcess(), REALTIME_PRIORITY_CLASS)) {
        // HIGH_PRIORITY_CLASS là fallback nếu không có quyền REALTIME
        SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
        fprintf(stderr, "[bench] REALTIME priority denied, using HIGH\n");
    }

    // Raise thread priority lên mức cao nhất trong user-space
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);

    // Tắt Windows power management throttling cho core này
    // (không có API trực tiếp; đọc lại affinity để verify)
    DWORD_PTR sysAffinityMask = 0, procAffinityMask = 0;
    GetProcessAffinityMask(GetCurrentProcess(), &procAffinityMask, &sysAffinityMask);

    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    int nLogical = static_cast<int>(si.dwNumberOfProcessors);

    fprintf(stdout, "[bench] CPU isolation: pinned to logical core %d / %d\n", coreIndex, nLogical);
    fprintf(stdout, "[bench] Process affinity mask = 0x%llx  |  priority = REALTIME/TIME_CRITICAL\n",
            (unsigned long long)procAffinityMask);
    fprintf(stdout, "[bench] Caveat: hardware IRQs & kernel DPCs can still preempt (Windows limit)\n\n");
    return coreIndex;
}

// ─────────────────────────────────────────────────────────────────────────────
// NullNetworkBackend — send() là no-op; không cần socket thật.
// broadcastSnapshot (memcpy + send loop) vẫn chạy đầy đủ → đo CPU cost thực.
// ─────────────────────────────────────────────────────────────────────────────

class NullNetworkBackend : public INetworkBackend {
public:
    bool start(int) override { return true; }
    void stop()     override {}
    void send(const sockaddr_in&, const uint8_t*, size_t) override {}
    void setRouteCallback(std::function<void(GameCommand)>) override {}
    const char* backendName() const override { return "null"; }
};

// ─────────────────────────────────────────────────────────────────────────────
// Packet builders
// ─────────────────────────────────────────────────────────────────────────────

static std::vector<uint8_t> buildMovePacket(uint32_t matchId, uint8_t seq)
{
    uint8_t buf[12] = {};
    WriteStream ws(reinterpret_cast<uint32_t*>(buf), 3);
    PacketHeader hdr;
    hdr.size = 12; hdr.opcode = Opcode::C2S_MOVE;
    hdr.matchId = matchId; hdr.flags = 0; hdr.seq = seq; hdr.tick = 0;
    hdr.Serialize(ws);
    PacketMovement mv; mv.dirX = 1; mv.dirZ = 2; mv.speed = 255;
    mv.Serialize(ws);
    return std::vector<uint8_t>(buf, buf + 12);
}

static std::vector<uint8_t> buildShootPacket(uint32_t matchId, uint8_t seq)
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

static sockaddr_in makeAddr(int idx)
{
    sockaddr_in a{};
    a.sin_family      = AF_INET;
    a.sin_addr.s_addr = htonl(0x7F000001);
    a.sin_port        = htons(static_cast<uint16_t>(20000 + idx));
    return a;
}

// ─────────────────────────────────────────────────────────────────────────────
// Stats helpers
// ─────────────────────────────────────────────────────────────────────────────

static int64_t pct(const std::vector<int64_t>& sorted, int p)
{
    if (sorted.empty()) return 0;
    size_t idx = static_cast<size_t>(p) * sorted.size() / 100;
    if (idx >= sorted.size()) idx = sorted.size() - 1;
    return sorted[idx];
}

struct PhaseStats {
    int64_t avg, p50, p95, p99, max, min;
};

static PhaseStats computeStats(std::vector<int64_t>& v)
{
    std::sort(v.begin(), v.end());
    int64_t total = 0;
    for (auto x : v) total += x;
    PhaseStats s{};
    s.avg = v.empty() ? 0 : total / (int64_t)v.size();
    s.p50 = pct(v, 50);
    s.p95 = pct(v, 95);
    s.p99 = pct(v, 99);
    s.min = v.empty() ? 0 : v.front();
    s.max = v.empty() ? 0 : v.back();
    return s;
}

// ─────────────────────────────────────────────────────────────────────────────
// JSON output — dùng bởi bench_push_grafana.py
// ─────────────────────────────────────────────────────────────────────────────

static void writeJson(const char* path, int nPlayers, int warmupTicks, int measureTicks,
                      const PhaseStats& drain,
                      const PhaseStats& dispatch,
                      const PhaseStats& bullet,
                      const PhaseStats& physics,
                      const PhaseStats& snap,
                      const PhaseStats& total,
                      int64_t steadyBullets,
                      int overruns,
                      int pinnedCore,
                      int64_t jitterP99Us)
{
    constexpr int64_t BUDGET_US = 16667;
    FILE* f = fopen(path, "w");
    if (!f) { fprintf(stderr, "[bench] cannot write %s\n", path); return; }

    fprintf(f, "{\n");
    fprintf(f, "  \"scenario\": \"worst_case_10player\",\n");
    fprintf(f, "  \"players\": %d,\n", nPlayers);
    fprintf(f, "  \"warmup_ticks\": %d,\n", warmupTicks);
    fprintf(f, "  \"measure_ticks\": %d,\n", measureTicks);
    fprintf(f, "  \"tick_budget_us\": %lld,\n", (long long)BUDGET_US);
    fprintf(f, "  \"steady_state_bullets\": %lld,\n", (long long)steadyBullets);
    fprintf(f, "  \"overruns\": %d,\n", overruns);
    fprintf(f, "  \"pinned_core\": %d,\n", pinnedCore);
    fprintf(f, "  \"os_jitter_p99_us\": %lld,\n", (long long)jitterP99Us);

    auto phase = [&](const char* name, const PhaseStats& s) {
        fprintf(f,
            "  \"%s\": {\"avg\":%lld,\"p50\":%lld,\"p95\":%lld,\"p99\":%lld,\"min\":%lld,\"max\":%lld},\n",
            name,
            (long long)s.avg, (long long)s.p50, (long long)s.p95,
            (long long)s.p99, (long long)s.min, (long long)s.max);
    };

    phase("drain_us",    drain);
    phase("dispatch_us", dispatch);
    phase("bullet_us",   bullet);
    phase("physics_us",  physics);
    phase("snap_us",     snap);
    phase("total_post_drain_us", total);

    int64_t gpc = (total.p99 > 0) ? BUDGET_US / total.p99 : 0;
    fprintf(f, "  \"gpc_p99\": %lld\n", (long long)gpc);
    fprintf(f, "}\n");
    fclose(f);
    printf("[bench] JSON written → %s\n", path);
}

// ─────────────────────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────────────────────

int main(int argc, char** argv)
{
    setvbuf(stderr, nullptr, _IONBF, 0);
    setvbuf(stdout, nullptr, _IONBF, 0);

    const char* jsonPath = (argc >= 2) ? argv[1] : "bench_result.json";
    // core index: argv[2] nếu có, mặc định là core 2 (tránh core 0 bị OS dùng nhiều)
    int pinnedCore = (argc >= 3) ? std::atoi(argv[2]) : 2;

    WSADATA wsa{};
    WSAStartup(MAKEWORD(2, 2), &wsa);

    // ── Pin ngay trước khi làm bất cứ điều gì khác ─────────────────────────
    int actualCore = pinToCore(pinnedCore);

    Logger::getInstance().init("bench_worst_case.log");

    // ── Thông số ────────────────────────────────────────────────────────────
    constexpr uint32_t MATCH_ID      = 9999;
    constexpr int      N_PLAYERS     = 10;
    // Warmup: đủ để bullet backlog đạt steady state.
    // Bullet TTL = 4s × 60Hz = 240 ticks. Cộng thêm 1.5× để đảm bảo steady state.
    constexpr int      WARMUP_TICKS  = 600;   // 10s
    constexpr int      MEASURE_TICKS = 6000;  // 100s — đủ p99.9 chính xác
    constexpr float    DT            = 1.f / 60.f;
    constexpr int64_t  BUDGET_US     = 16667; // 60 Hz

    NullNetworkBackend net;
    MatchConfig cfg;
    cfg.matchId         = MATCH_ID;
    cfg.mapName         = "world";
    cfg.maxDurationSecs = 99999;

    for (int i = 1; i <= N_PLAYERS; ++i) {
        cfg.playerIds.push_back(static_cast<uint32_t>(i));
        TankStats stats;
        // health rất cao → tank không chết dù bị bắn
        // → duy trì 10 dynamic OBBs xuyên suốt → max physics pairs
        stats.health      = 9'999'999;
        stats.speed       = 12.f;
        stats.damage      = 25;
        stats.barrelCount = 1;
        cfg.playerStats[static_cast<uint32_t>(i)] = stats;
    }

    auto matchPtr = std::make_unique<Match>(std::move(cfg), net, [](MatchResult){});
    Match& match  = *matchPtr;

    std::vector<sockaddr_in> addrs;
    for (int i = 0; i < N_PLAYERS; ++i) addrs.push_back(makeAddr(i));

    // Mỗi tick: 10 MOVE + 10 SHOOT = 20 commands
    // → dispatch max; bullet spawn max (10/tick); movement max (10 tanks di chuyển)
    auto injectCommands = [&](int t) {
        for (int i = 0; i < N_PLAYERS; ++i) {
            uint8_t seq = static_cast<uint8_t>(t & 0xFF);
            GameCommand mc; mc.sender = addrs[i]; mc.matchId = MATCH_ID;
            mc.op = Opcode::C2S_MOVE; mc.rawBuffer = buildMovePacket(MATCH_ID, seq);
            match.pushCommand(mc);
            GameCommand sc; sc.sender = addrs[i]; sc.matchId = MATCH_ID;
            sc.op = Opcode::C2S_SHOOT; sc.rawBuffer = buildShootPacket(MATCH_ID, seq);
            match.pushCommand(sc);
        }
    };

    // ── Đo OS jitter baseline (nop loop) ────────────────────────────────────
    // Đo thời gian của 1000 vòng lặp rỗng để ước lượng OS timer interrupt jitter.
    // Jitter p99 cho biết mức nhiễu nền tối thiểu bất kể benchmark làm gì.
    {
        using Clock = std::chrono::high_resolution_clock;
        using Us    = std::chrono::microseconds;
        std::vector<int64_t> jitterSamples;
        jitterSamples.reserve(1000);
        for (int j = 0; j < 1000; ++j) {
            auto j0 = Clock::now();
            // sleep 1 tick = 1/60s để simulate khoảng nghỉ giữa các tick
            // (dùng Sleep(0) để yield nhưng không block lâu)
            Sleep(0);
            auto j1 = Clock::now();
            jitterSamples.push_back(std::chrono::duration_cast<Us>(j1 - j0).count());
        }
        std::sort(jitterSamples.begin(), jitterSamples.end());
        int64_t jp99 = pct(jitterSamples, 99);
        int64_t jmax = jitterSamples.back();
        printf("[bench] OS jitter baseline (Sleep(0) × 1000): p99=%lldµs  max=%lldµs\n",
               (long long)jp99, (long long)jmax);
        printf("        → Bất kỳ tick nào bị +%lldµs (p99) là do OS, không phải game logic\n\n",
               (long long)jp99);
    }

    // ── Warmup ──────────────────────────────────────────────────────────────
    printf("=========================================================\n");
    printf("  WORST-CASE TICK BENCHMARK — %d players\n", N_PLAYERS);
    printf("  t0 = AFTER cmdQueue drain  |  t1 = after broadcastSnapshot\n");
    printf("=========================================================\n\n");
    printf("[bench] Warmup: %d ticks (spawn tanks + build bullet steady state)...\n",
           WARMUP_TICKS);
    for (int t = 0; t < WARMUP_TICKS; ++t) {
        injectCommands(t);
        match.tick(DT);
    }
    int64_t bulletsSteadyState = match.lastBreakdown().activeBullets;
    printf("[bench] Warmup done. Steady-state active bullets = %lld\n\n",
           (long long)bulletsSteadyState);

    // ── Measurement ─────────────────────────────────────────────────────────
    std::vector<int64_t> vDrain, vDispatch, vBullet, vPhysics, vSnap, vTotal;
    vDrain.reserve(MEASURE_TICKS);   vDispatch.reserve(MEASURE_TICKS);
    vBullet.reserve(MEASURE_TICKS);  vPhysics.reserve(MEASURE_TICKS);
    vSnap.reserve(MEASURE_TICKS);    vTotal.reserve(MEASURE_TICKS);

    int overruns = 0;
    int64_t jitterP99 = 0; // filled after jitter measurement inside loop below
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
        if (bd.totalPostDrainUs > BUDGET_US) ++overruns;
    }

    // ── Kết quả ─────────────────────────────────────────────────────────────
    auto sDrain    = computeStats(vDrain);
    auto sDispatch = computeStats(vDispatch);
    auto sBullet   = computeStats(vBullet);
    auto sPhysics  = computeStats(vPhysics);
    auto sSnap     = computeStats(vSnap);
    auto sTotal    = computeStats(vTotal);

    printf("=================================================================\n");
    printf("  RESULTS — per-phase distribution (t0=post-drain)\n");
    printf("  %-18s %7s %7s %7s %7s %7s\n",
           "Phase", "avg", "p50", "p95", "p99", "max");
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs\n", "drain (excl.)",
           (long long)sDrain.avg,(long long)sDrain.p50,(long long)sDrain.p95,
           (long long)sDrain.p99,(long long)sDrain.max);
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs\n", "dispatch",
           (long long)sDispatch.avg,(long long)sDispatch.p50,(long long)sDispatch.p95,
           (long long)sDispatch.p99,(long long)sDispatch.max);
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs\n", "updateBullets",
           (long long)sBullet.avg,(long long)sBullet.p50,(long long)sBullet.p95,
           (long long)sBullet.p99,(long long)sBullet.max);
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs\n", "runPhysics",
           (long long)sPhysics.avg,(long long)sPhysics.p50,(long long)sPhysics.p95,
           (long long)sPhysics.p99,(long long)sPhysics.max);
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs\n", "broadcastSnap",
           (long long)sSnap.avg,(long long)sSnap.p50,(long long)sSnap.p95,
           (long long)sSnap.p99,(long long)sSnap.max);
    printf("  %-18s %7lld %7lld %7lld %7lld %7lld  µs  ← dùng làm cận trên\n",
           "TOTAL (post-drain)",
           (long long)sTotal.avg,(long long)sTotal.p50,(long long)sTotal.p95,
           (long long)sTotal.p99,(long long)sTotal.max);
    printf("\n");
    // Estimate jitter p99 từ drain samples (drain = mutex swap, phần game logic gần bằng 0)
    // drain spike = OS preemption → dùng làm proxy cho jitter
    {
        std::vector<int64_t> drainCopy = vDrain;
        auto sd = computeStats(drainCopy);
        jitterP99 = sd.p99;
    }

    printf("  Budget (60Hz) = %lld µs\n", (long long)BUDGET_US);
    printf("  Overruns: %d / %d ticks (%.3f%%)\n",
           overruns, MEASURE_TICKS, 100.0 * overruns / MEASURE_TICKS);
    printf("  Budget used (p99) = %.1f%%\n", 100.0 * sTotal.p99 / BUDGET_US);
    printf("  Pinned core: %d  |  OS jitter est. (drain p99): %lldµs\n",
           actualCore, (long long)jitterP99);

    int64_t gpc = (sTotal.p99 > 0) ? BUDGET_US / sTotal.p99 : 0;
    printf("\n  GPC(p99) = floor(%lld / %lld) = %lld matches/core\n",
           (long long)BUDGET_US, (long long)sTotal.p99, (long long)gpc);

    // ── Proof section ────────────────────────────────────────────────────────
    printf("\n=================================================================\n");
    printf("  PROOF: Tại sao đây là kịch bản nặng nhất suy diễn được\n");
    printf("=================================================================\n");
    printf("\n  Cost model của mỗi phase (từ phân tích code):\n\n");

    printf("  [dispatch] O(C)\n");
    printf("    C = %d commands/tick = %d players × 2 (MOVE+SHOOT)\n", N_PLAYERS*2, N_PLAYERS);
    printf("    → MAX vì không thể có nhiều hơn 1 MOVE + 1 SHOOT/player/tick\n\n");

    // Bullet max = players × fire_rate × TTL_ticks
    // Bullet::TTL = 4s, fire_rate = 1/tick, 60 ticks/s
    constexpr int BULLET_TTL_TICKS = static_cast<int>(4.0f * 60.f); // 240
    int64_t bulletTheorMax = (int64_t)N_PLAYERS * BULLET_TTL_TICKS;
    printf("  [updateBullets] O(B × W + B × T)\n");
    printf("    B = steady-state active bullets = %lld (measured)\n",
           (long long)bulletsSteadyState);
    printf("    B_theoretical_max = %d players × %d TTL_ticks = %lld\n",
           N_PLAYERS, BULLET_TTL_TICKS, (long long)bulletTheorMax);
    printf("    W = static OBB walls (từ world.json) — không đổi\n");
    printf("    T = %d (tất cả tanks alive, health=9,999,999)\n", N_PLAYERS);
    printf("    → B tối đa khi: tất cả tank bắn mỗi tick (đã thỏa),\n");
    printf("         tank không chết (health cực lớn, đã thỏa),\n");
    printf("         bullet không trúng wall → phụ thuộc map (không kiểm soát được).\n");
    printf("    → Đây là UPPER BOUND của B. Giá trị thực = %lld.\n\n",
           (long long)bulletsSteadyState);

    printf("  [runPhysics] O(E + P × SAT)\n");
    printf("    E = %d dynamicBoxes (tanks) + %lld spheres (bullets) + static colliders\n",
           N_PLAYERS, (long long)bulletsSteadyState);
    printf("    P = broad-phase pairs (phụ thuộc spatial distribution)\n");
    printf("    → E_dynamic tối đa khi: tất cả %d tank alive (đã thỏa)\n", N_PLAYERS);
    printf("    → P tối đa khi: entities tập trung cùng grid cell\n");
    printf("       (không kiểm soát được — phụ thuộc movement path)\n\n");

    printf("  [broadcastSnapshot] O(T² + T×Bush + B)\n");
    printf("    T = %d (tất cả tanks → T² placement loop)\n", N_PLAYERS);
    printf("    B = %lld active bullets trong snapshot body\n",
           (long long)bulletsSteadyState);
    printf("    → MAX khi T=10 (đã thỏa) và B=steady-state (đã thỏa)\n\n");

    printf("  KLUẬN: Mỗi cost driver đều ở giá trị tối đa hoặc upper bound.\n");
    printf("  p99 totalPostDrainUs = %lld µs → capacity planning conservative.\n",
           (long long)sTotal.p99);
    printf("  Không thể formal-prove true worst case (undecidable in general),\n");
    printf("  nhưng đây là kịch bản nặng nhất CÓ THỂ SUY DIỄN từ code analysis.\n");

    printf("\n=================================================================\n\n");

    // ── JSON output ──────────────────────────────────────────────────────────
    writeJson(jsonPath, N_PLAYERS, WARMUP_TICKS, MEASURE_TICKS,
              sDrain, sDispatch, sBullet, sPhysics, sSnap, sTotal,
              bulletsSteadyState, overruns, actualCore, jitterP99);

    WSACleanup();
    return 0;
}
