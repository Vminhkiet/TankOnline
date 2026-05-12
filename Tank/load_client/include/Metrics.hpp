#pragma once
#include <atomic>
#include <cstdint>
#include <cstdio>

// Latency histogram buckets (microseconds)
//  [0] < 1 ms
//  [1] 1–5 ms
//  [2] 5–10 ms
//  [3] 10–50 ms
//  [4] 50–100 ms
//  [5] >= 100 ms
constexpr int LAT_BUCKETS = 6;

struct Metrics {
    std::atomic<uint64_t> packetsSent  {0};
    std::atomic<uint64_t> packetsRecv  {0};
    std::atomic<uint64_t> sendErrors   {0};
    std::atomic<uint64_t> recvErrors   {0};
    std::atomic<uint64_t> bytesOut     {0};
    std::atomic<uint64_t> bytesIn      {0};
    std::atomic<uint64_t> latBucket[LAT_BUCKETS] {};

    void recordLatencyUs(int64_t us);

    // Print one-line snapshot (called every second by main thread)
    // prevSent/prevRecv are the values from the last call (to compute per-sec rate)
    void printSnapshot(int elapsedSec,
                       uint64_t& prevSent, uint64_t& prevRecv,
                       uint64_t& prevBytes) const;

    void printSummary(int totalSec) const;
};
