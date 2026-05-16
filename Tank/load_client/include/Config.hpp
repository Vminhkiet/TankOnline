#pragma once
#include <string>
#include <cstdint>

struct Config {
    std::string host        = "127.0.0.1";
    uint16_t    port        = 8080;
    int         clients     = 100;  // total virtual players
    int         threads     = 0;    // 0 = hardware_concurrency
    int         duration    = 30;   // seconds
    int         tickRate    = 20;   // packets/sec per client (simulated tick)
    float       shootChance = 0.05f;// probability of shooting each tick
    bool        verbose     = false;// print per-player events
    uint32_t    matchId     = 1;    // which match to target
};

// Parse --key value pairs from argv
Config parseArgs(int argc, char* argv[]);

inline void printUsage(const char* prog) {
    printf(
        "Usage: %s [options]\n"
        "  --host        <ip>    target server IP  (default: 127.0.0.1)\n"
        "  --port        <n>     target port        (default: 8080)\n"
        "  --clients     <n>     virtual players    (default: 100)\n"
        "  --threads     <n>     worker threads     (default: cpu_count)\n"
        "  --duration    <n>     test seconds       (default: 30)\n"
        "  --rate        <n>     ticks/sec/client   (default: 20)\n"
        "  --shoot       <0..1>  shoot probability  (default: 0.05)\n"
        "  --match       <n>     target matchId     (default: 1)\n"
        "  --verbose             log per-player     (default: off)\n"
        , prog);
}
