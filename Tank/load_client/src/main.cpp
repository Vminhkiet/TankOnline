#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <thread>
#include <chrono>
#include <csignal>

#include "Config.hpp"
#include "Metrics.hpp"
#include "UdpSocket.hpp"
#include "WorkerThread.hpp"

// ─── CLI parser ───────────────────────────────────────────────────────────────

Config parseArgs(int argc, char* argv[])
{
    Config cfg;
    for (int i = 1; i < argc; ++i) {
        std::string key = argv[i];
        auto val = [&]() -> const char* {
            return (i + 1 < argc) ? argv[++i] : "";
        };
        if      (key == "--host")     cfg.host        = val();
        else if (key == "--port")     cfg.port        = static_cast<uint16_t>(std::atoi(val()));
        else if (key == "--clients")  cfg.clients     = std::atoi(val());
        else if (key == "--threads")  cfg.threads     = std::atoi(val());
        else if (key == "--duration") cfg.duration    = std::atoi(val());
        else if (key == "--rate")     cfg.tickRate    = std::atoi(val());
        else if (key == "--shoot")    cfg.shootChance = static_cast<float>(std::atof(val()));
        else if (key == "--match")    cfg.matchId     = static_cast<uint32_t>(std::atoi(val()));
        else if (key == "--verbose")  cfg.verbose     = true;
        else if (key == "--help")     { printUsage(argv[0]); std::exit(0); }
    }
    return cfg;
}

// ─── Graceful stop ────────────────────────────────────────────────────────────

static volatile bool g_running = true;
static void onSignal(int) { g_running = false; }

// ─── Entry point ──────────────────────────────────────────────────────────────

int main(int argc, char* argv[])
{
    // Init Winsock once for the whole process
    WSADATA wsa{};
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        fprintf(stderr, "WSAStartup failed\n");
        return 1;
    }

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    Config cfg = parseArgs(argc, argv);

    int numThreads = cfg.threads > 0
        ? cfg.threads
        : static_cast<int>(std::thread::hardware_concurrency());
    if (numThreads < 1) numThreads = 1;

    sockaddr_in serverAddr = makeAddr(cfg.host, cfg.port);

    printf("╔══════════════════════════════════════════════════╗\n");
    printf("║          IOCP LOAD TEST CLIENT                   ║\n");
    printf("╚══════════════════════════════════════════════════╝\n");
    printf("  Target   : %s:%d\n", cfg.host.c_str(), cfg.port);
    printf("  Clients  : %d  (virtual players)\n", cfg.clients);
    printf("  Threads  : %d  (worker threads)\n",  numThreads);
    printf("  Tick rate: %d  pkt/s/client\n",      cfg.tickRate);
    printf("  Duration : %d  seconds\n",           cfg.duration);
    printf("  Shoot p  : %.0f%%\n\n",              cfg.shootChance * 100.f);

    // Distribute clients evenly across threads
    Metrics metrics;
    std::vector<std::unique_ptr<WorkerThread>> workers;
    workers.reserve(numThreads);

    int assigned  = 0;
    int baseCount = cfg.clients / numThreads;
    int extra     = cfg.clients % numThreads;

    for (int t = 0; t < numThreads; ++t) {
        int count = baseCount + (t < extra ? 1 : 0);
        if (count == 0) break;
        workers.push_back(std::make_unique<WorkerThread>(
            t, assigned, count, cfg, serverAddr, metrics));
        assigned += count;
    }

    // Start all workers
    for (auto& w : workers) w->start();

    printf("  Workers started. Running for %d seconds...\n\n", cfg.duration);
    printf("  %-5s  %-30s  %-20s  %-15s\n",
           "Time", "Sent (total / pps)", "Recv (total / rps)", "BW out");
    printf("  %s\n", std::string(72, '-').c_str());

    // Stats loop – print every second
    using Clock   = std::chrono::steady_clock;
    auto startT   = Clock::now();
    auto deadline = startT + std::chrono::seconds(cfg.duration);

    uint64_t prevSent = 0, prevRecv = 0, prevBytes = 0;
    int      elapsed  = 0;

    while (g_running) {
        std::this_thread::sleep_for(std::chrono::seconds(1));
        ++elapsed;

        metrics.printSnapshot(elapsed, prevSent, prevRecv, prevBytes);

        if (Clock::now() >= deadline) break;
    }

    // Stop workers
    for (auto& w : workers) w->stop();
    for (auto& w : workers) w->join();

    int actualDuration = static_cast<int>(
        std::chrono::duration_cast<std::chrono::seconds>(Clock::now() - startT).count());
    metrics.printSummary(actualDuration);

    WSACleanup();
    return 0;
}
