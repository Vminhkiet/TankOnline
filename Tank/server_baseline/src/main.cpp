#include "UdpServer.hpp"
#include "GameLoop.hpp"
#include "Core/MatchConfig.hpp"
#include "Utils/Logger.hpp"
#include <winsock2.h>
#include <timeapi.h>
#include <csignal>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>
#include <chrono>
#include <atomic>

static std::atomic<bool> g_running{true};
static void onSignal(int) { g_running = false; }

static std::string getEnv(const char* key, const char* def) {
    const char* v = std::getenv(key);
    return v ? v : def;
}

int main() {
    // 1 ms timer resolution so sleep_for is accurate
    timeBeginPeriod(1);

    std::cout << "=========================================\n"
              << "  SERVER-BASELINE  (non-blocking / switch)\n"
              << "=========================================\n\n";

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    Logger::getInstance().init("server_baseline.log");

    // Default port 8081 — run alongside server_tank (8080) for side-by-side comparison
    const int udpPort = std::stoi(getEnv("UDP_PORT", "8081"));

    // ── UDP socket (non-blocking, no IOCP) ───────────────────────────────────
    UdpServer udp;
    if (!udp.init(udpPort)) return 1;

    // ── Match config (same hardcode as server_tank for fair comparison) ───────
    MatchConfig cfg;
    cfg.matchId         = 1;
    cfg.mapName         = "world";
    cfg.maxDurationSecs = 600;
    cfg.playerIds       = {1, 2};

    // ── Game loop ─────────────────────────────────────────────────────────────
    GameLoop loop(udp, std::move(cfg));
    if (!loop.init()) return 1;

    LOG_INFO("main: running on UDP:{}", udpPort);

    // ── 60 Hz tick loop ───────────────────────────────────────────────────────
    constexpr float TICK_DT       = 1.0f / 60.0f;
    constexpr int   SNAPSHOT_EVERY = 3;   // snapshot at ~20 Hz
    int snapshotCounter = 0;

    uint8_t recvBuf[2048];

    while (g_running) {
        auto tickStart = std::chrono::steady_clock::now();

        // 1. Drain all pending UDP datagrams (non-blocking loop)
        sockaddr_in from{};
        int len = 0;
        while (udp.tryRecv(recvBuf, sizeof(recvBuf), from, len))
            loop.handlePacket(recvBuf, len, from);

        // 2. Advance world simulation
        loop.tick(TICK_DT);

        // 3. Disconnect players that haven't sent a packet in 5 s
        for (uint32_t pid : loop.sessions().collectTimeouts(5.0f)) {
            LOG_INFO("main: player {} timed out", pid);
            loop.sessions().remove(pid);
        }

        // 4. Broadcast snapshot at 20 Hz
        if (++snapshotCounter >= SNAPSHOT_EVERY) {
            snapshotCounter = 0;
            loop.broadcastSnapshot();
        }

        // 5. Sleep for the remainder of the tick budget
        auto tickEnd = std::chrono::steady_clock::now();
        float workMs = std::chrono::duration<float, std::milli>(tickEnd - tickStart).count();
        float remainMs = TICK_DT * 1000.0f - workMs;
        if (remainMs > 0.5f)
            std::this_thread::sleep_for(
                std::chrono::microseconds(static_cast<long long>(remainMs * 1000.0f)));
    }

    udp.close();
    timeEndPeriod(1);
    LOG_INFO("main: shutdown");
    return 0;
}
